using NewCosmos.Constants;
using NewCosmos.Models.Options;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using System.Collections.Concurrent;

namespace NewCosmos.Services.UserManagement;

/// <summary>用户有效数据范围快照</summary>
public class DataScopeInfo
{
    /// <summary>有效数据范围（多角色取最大）</summary>
    public string Scope { get; init; } = DataScopeConstants.SELF;

    /// <summary>用户所属组织 id（未分配为 null）</summary>
    public int? OrganizationId { get; init; }

    /// <summary>是否超级管理员（按 SUPER_ADMIN 角色判定）</summary>
    public bool IsSuperAdmin { get; init; }
}

/// <summary>
/// 数据范围服务：解析用户有效数据范围与可管理组织边界。
/// 功能权限管"能不能做"（INewPermissionService），本服务管"能对谁做"——
/// 以用户所属组织为基准、按角色 data_scope 取最大范围。
/// </summary>
public interface IDataScopeService
{
    /// <summary>获取用户有效数据范围（含超管标记与所属组织）</summary>
    Task<DataScopeInfo> GetEffectiveScopeAsync(int userId, CancellationToken ct = default);

    /// <summary>用户可管理的组织 id 集合：ALL=全部；ORG_AND_CHILDREN=组织子树；ORG/SELF=本组织</summary>
    Task<List<int>> GetManageableOrganizationIdsAsync(int userId, CancellationToken ct = default);

    /// <summary>目标组织是否在用户可管理范围内</summary>
    Task<bool> CanManageOrganizationAsync(int userId, int organizationId, CancellationToken ct = default);

    /// <summary>用户是否超级管理员（按 SUPER_ADMIN 角色判定，不再以 USER_DELETE 权限代理）</summary>
    Task<bool> IsSuperAdminAsync(int userId, CancellationToken ct = default);

    /// <summary>失效指定用户的数据范围缓存（角色分配/组织变更后调用）</summary>
    Task InvalidateUserCacheAsync(int userId);

    /// <summary>失效全部数据范围缓存</summary>
    Task InvalidateAllCacheAsync();
}

public class DataScopeService : BaseService, IDataScopeService
{
    protected override string ServiceName => "DataScopeService";

    private readonly IDatabaseService _dbService;
    private readonly ILoggerService _logger;
    private readonly PerformanceOptions _perfOptions;

    // 单用户范围缓存（TTL + 版本比对双重失效，模式与 NewPermissionService 一致）
    private readonly ConcurrentDictionary<int, (DataScopeInfo Info, DateTime CachedAt)> _cache = new();
    private long? _currentVersion;
    private DateTime _versionCheckTime = DateTime.MinValue;

    // 组织树缓存：全量加载（组织表量级小），parent 映射供子树计算
    private (DateTime LoadedAt, Dictionary<int, int?> ParentMap, List<int> AllIds)? _orgTree;
    private readonly SemaphoreSlim _orgTreeLock = new(1, 1);

    public DataScopeService(IDatabaseService dbService, ILoggerService logger, PerformanceOptions perfOptions)
        : base(logger)
    {
        _dbService = dbService;
        _logger = logger;
        _perfOptions = perfOptions;
    }

    public async Task<DataScopeInfo> GetEffectiveScopeAsync(int userId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(userId, out var cached) && !await IsCacheExpiredAsync(cached.CachedAt))
            return cached.Info;

        var info = await LoadEffectiveScopeAsync(userId, ct);
        _cache[userId] = (info, DateTime.UtcNow);
        LogDebug($"数据范围解析完成: UserId={userId}, Scope={info.Scope}, IsSuperAdmin={info.IsSuperAdmin}");
        return info;
    }

    public async Task<List<int>> GetManageableOrganizationIdsAsync(int userId, CancellationToken ct = default)
    {
        var scope = await GetEffectiveScopeAsync(userId, ct);
        if (scope.Scope == DataScopeConstants.ALL)
            return (await LoadOrgTreeAsync(ct)).AllIds;

        if (scope.OrganizationId is not { } orgId)
            return new List<int>();

        if (scope.Scope == DataScopeConstants.ORG_AND_CHILDREN)
            return await GetDescendantIdsAsync(orgId, ct);

        // SELF / ORG：仅本组织
        return new List<int> { orgId };
    }

    public async Task<bool> CanManageOrganizationAsync(int userId, int organizationId, CancellationToken ct = default)
    {
        var manageable = await GetManageableOrganizationIdsAsync(userId, ct);
        return manageable.Contains(organizationId);
    }

    public async Task<bool> IsSuperAdminAsync(int userId, CancellationToken ct = default)
    {
        var scope = await GetEffectiveScopeAsync(userId, ct);
        return scope.IsSuperAdmin;
    }

    public Task InvalidateUserCacheAsync(int userId)
    {
        _cache.TryRemove(userId, out _);
        LogDebug($"用户数据范围缓存已清除: UserId={userId}");
        return Task.CompletedTask;
    }

    public Task InvalidateAllCacheAsync()
    {
        _cache.Clear();
        _orgTree = null;
        _currentVersion = null;
        LogDebug("全部数据范围缓存已清除");
        return Task.CompletedTask;
    }

    /// <summary>从数据库解析用户有效范围：角色集合取最大 data_scope；SUPER_ADMIN 角色直接判定超管</summary>
    private async Task<DataScopeInfo> LoadEffectiveScopeAsync(int userId, CancellationToken ct)
    {
        var rolesSql = @"
            SELECT r.code, r.data_scope
            FROM nc_sys_user_roles ur
            JOIN nc_sys_roles r ON ur.role_id = r.id
            WHERE ur.user_id = $1";
        var rolesResult = await _dbService.QueryAsync<RoleScopeRow>(rolesSql, ct, userId);
        var roles = rolesResult.IsSuccess && rolesResult.Value is not null ? rolesResult.Value : new List<RoleScopeRow>();

        var isSuperAdmin = roles.Any(r => string.Equals(r.Code, "SUPER_ADMIN", StringComparison.OrdinalIgnoreCase));
        var scope = DataScopeConstants.SELF;
        foreach (var role in roles)
        {
            scope = DataScopeConstants.Max(scope, role.DataScope);
        }

        var orgSql = "SELECT organization_id FROM nc_sys_users WHERE id = $1 LIMIT 1";
        var orgResult = await _dbService.ExecuteScalarAsync<int>(orgSql, ct, userId);
        // 无行/NULL 返回 default(0)，归一为 null（未分配组织）
        int? orgId = orgResult.IsSuccess && orgResult.Value > 0 ? orgResult.Value : null;

        return new DataScopeInfo { Scope = scope, OrganizationId = orgId, IsSuperAdmin = isSuperAdmin };
    }

    /// <summary>组织子树（含自身），基于全量内存树计算</summary>
    private async Task<List<int>> GetDescendantIdsAsync(int rootOrgId, CancellationToken ct)
    {
        var tree = await LoadOrgTreeAsync(ct);
        var result = new List<int> { rootOrgId };
        var frontier = new List<int> { rootOrgId };
        while (frontier.Count > 0)
        {
            var next = new List<int>();
            foreach (var candidate in tree.AllIds)
            {
                if (tree.ParentMap.TryGetValue(candidate, out var parent) &&
                    parent.HasValue && frontier.Contains(parent.Value) &&
                    !result.Contains(candidate))
                {
                    result.Add(candidate);
                    next.Add(candidate);
                }
            }
            frontier = next;
        }
        return result;
    }

    /// <summary>全量加载组织树（id → parent_id），带 TTL 缓存</summary>
    private async Task<(Dictionary<int, int?> ParentMap, List<int> AllIds)> LoadOrgTreeAsync(CancellationToken ct)
    {
        var ttl = TimeSpan.FromMinutes(Math.Max(1, _perfOptions.PermissionCacheMinutes));
        if (_orgTree is { } tree && DateTime.UtcNow - tree.LoadedAt < ttl)
            return (tree.ParentMap, tree.AllIds);

        await _orgTreeLock.WaitAsync(ct);
        try
        {
            if (_orgTree is { } cached && DateTime.UtcNow - cached.LoadedAt < ttl)
                return (cached.ParentMap, cached.AllIds);

            var sql = "SELECT id, parent_id FROM nc_sys_organizations";
            var result = await _dbService.QueryAsync<OrgRow>(sql, ct);
            var rows = result.IsSuccess && result.Value is not null ? result.Value : new List<OrgRow>();

            var parentMap = rows.ToDictionary(r => r.Id, r => r.ParentId);
            var allIds = rows.Select(r => r.Id).ToList();
            _orgTree = (DateTime.UtcNow, parentMap, allIds);
            return (parentMap, allIds);
        }
        finally
        {
            _orgTreeLock.Release();
        }
    }

    private bool IsCacheExpired(DateTime cachedAt) =>
        DateTime.UtcNow - cachedAt > TimeSpan.FromMinutes(Math.Max(1, _perfOptions.PermissionCacheMinutes));

    /// <summary>TTL 未过期时再比对新式版本号（角色 data_scope 变更会递增），变更即清缓存</summary>
    private async Task<bool> IsCacheExpiredAsync(DateTime cachedAt)
    {
        if (IsCacheExpired(cachedAt))
            return true;

        await CheckVersionAsync();
        return false;
    }

    /// <summary>节流比对 nc_perm_cache_version('permissions')；失败降级继续用现有缓存</summary>
    private async Task CheckVersionAsync()
    {
        if (DateTime.UtcNow - _versionCheckTime < TimeSpan.FromSeconds(_perfOptions.PermissionVersionCheckIntervalSeconds))
            return;

        try
        {
            var result = await _dbService.QuerySingleAsync<long>("SELECT version FROM nc_perm_cache_version WHERE key = 'permissions'");
            if (result.IsSuccess)
            {
                var newVersion = result.Value;
                if (_currentVersion.HasValue && _currentVersion != newVersion)
                {
                    _cache.Clear();
                    _orgTree = null;
                    LogDebug("权限缓存版本已更新，数据范围缓存已清空");
                }
                _currentVersion = newVersion;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"数据范围缓存版本检查失败，降级使用现有缓存: {ex.Message}");
        }

        _versionCheckTime = DateTime.UtcNow;
    }

    private class RoleScopeRow
    {
        public string Code { get; set; } = string.Empty;
        public string DataScope { get; set; } = DataScopeConstants.SELF;
    }

    private class OrgRow
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
    }
}
