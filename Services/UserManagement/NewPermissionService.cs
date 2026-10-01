using NewCosmos.Constants;
using NewCosmos.Models.Entities.UserManagement;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using System.Collections.Concurrent;

namespace NewCosmos.Services.UserManagement;

public class NewPermissionService : BaseService, INewPermissionService
{
    protected override string ServiceName => "NewPermissionService";

    private readonly IDatabaseService _dbService;
    private readonly ILoggerService _logger;

    private readonly ConcurrentDictionary<int, HashSet<string>> _cache = new();
    private readonly TimeSpan _cacheDuration;
    private readonly ConcurrentDictionary<int, DateTime> _cacheTime = new();
    private readonly PerformanceOptions _perfOptions;

    private long? _currentVersion;
    private DateTime _versionCheckTime = DateTime.MinValue;

    public NewPermissionService(IDatabaseService dbService, ILoggerService logger, PerformanceOptions perfOptions) : base(logger)
    {
        _dbService = dbService;
        _logger = logger;
        _perfOptions = perfOptions;
        _cacheDuration = TimeSpan.FromMinutes(_perfOptions.PermissionCacheMinutes);
    }

    public async Task<bool> HasPermissionAsync(int userId, string permissionCode, CancellationToken ct = default)
    {
        LogDebug($"执行权限检查: UserId={userId}");
        var permissions = await GetUserPermissionCodesAsync(userId, ct);
        var hasPermission = permissions.Contains(permissionCode);
        LogDebug($"权限检查结果: UserId={userId}, HasPermission={hasPermission}");
        return hasPermission;
    }

    public async Task<IReadOnlyCollection<string>> GetUserPermissionCodesAsync(int userId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(userId, out var cached) && !await IsCacheExpiredAsync(userId))
        {
            LogDebug($"从缓存获取权限: UserId={userId}, Count={cached.Count}");
            return cached;
        }

        LogInfo($"从数据库加载权限: UserId={userId}");
        try
        {
            var permissions = await LoadUserPermissionsAsync(userId, ct);
            _cache[userId] = permissions;
            _cacheTime[userId] = DateTime.UtcNow;

            LogInfo($"权限加载完成: UserId={userId}, Count={permissions.Count}");
            return permissions;
        }
        catch (Exception ex)
        {
            // 失败不归零：有旧缓存则 stale 返回并刷新时间戳（短暂降级，下次到期再试）
            if (_cache.TryGetValue(userId, out var stale))
            {
                _cacheTime[userId] = DateTime.UtcNow;
                _logger.Warn($"权限加载失败，使用过期缓存: UserId={userId}, Count={stale.Count}, {ex.Message}");
                return stale;
            }

            _logger.Error($"权限加载失败且无缓存: UserId={userId}, {ex.Message}");
            throw;
        }
    }

    public async Task<IReadOnlyDictionary<string, bool>> CheckPermissionsAsync(int userId, IReadOnlyCollection<string> permissionCodes, CancellationToken ct = default)
    {
        LogDebug($"批量权限检查开始: UserId={userId}");
        var userPermissions = await GetUserPermissionCodesAsync(userId, ct);

        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in permissionCodes)
        {
            result[code] = userPermissions.Contains(code);
        }

        LogDebug($"批量权限检查完成: UserId={userId}, GrantedCount={result.Count(kv => kv.Value)}");
        return result;
    }

    public Task InvalidateUserCacheAsync(int userId)
    {
        _cache.TryRemove(userId, out _);
        _cacheTime.TryRemove(userId, out _);
        _logger.Info($"用户权限缓存已清除: UserId={userId}");

        return Task.CompletedTask;
    }

    public Task InvalidateAllCacheAsync()
    {
        _cache.Clear();
        _cacheTime.Clear();
        _currentVersion = null;
        _logger.Info($"所有权限缓存已清除");

        return Task.CompletedTask;
    }

    /// <summary>
    /// 同步默认权限（版本比对 + 一次性重建）：
    /// 库内矩阵版本（nc_perm_cache_version key=permission_matrix）与
    /// <see cref="PermissionSeedCatalog.MatrixVersion"/> 一致 → 跳过，保护用户自定义的角色-权限分配；
    /// 不一致（含首次升级）→ 在单事务内重建：更新默认角色（含 data_scope）、补齐权限定义、
    /// 清空 6 个默认角色的旧映射并按目录矩阵重写、清空用户直授、确保 admin 具备 SUPER_ADMIN，
    /// 最后写回版本标记并递增权限缓存版本（多机失效）。
    /// </summary>
    public async Task InitializeDefaultPermissionsAsync(CancellationToken ct = default)
    {
        _logger.Info("开始同步默认权限（版本比对重建）");

        var storedVersion = await ReadMatrixVersionAsync(ct);
        if (storedVersion == PermissionSeedCatalog.MatrixVersion)
        {
            LogDebug($"权限矩阵版本一致({storedVersion})，跳过重建");
            return;
        }

        _logger.Warn($"权限矩阵版本不一致(库内={storedVersion?.ToString() ?? "无"}, 目录={PermissionSeedCatalog.MatrixVersion})，执行一次性重建");

        await using var tx = await _dbService.BeginTransactionScopeAsync(ct);
        try
        {
            // 0) 确保 data_scope 列存在：存量库升级时普通启动不触发 Schema 同步，重建自带幂等补列
            var ensureColumn = await _dbService.ExecuteNonQueryAsync(
                "ALTER TABLE nc_sys_roles ADD COLUMN IF NOT EXISTS data_scope character varying(30)", ct);
            if (ensureColumn.IsFailure)
                throw new BusinessException(ErrorCodes.DB_QUERY_ERROR, $"补齐 nc_sys_roles.data_scope 列失败: {ensureColumn.Message}");

            // 1) 默认角色：更新名称/等级/描述/数据范围；不存在则按固定 id 插入
            // [N+1 豁免] 仅 6 个种子角色的一次性初始化，收益极低；nc_sys_roles.code 无唯一约束
            // （new_permission_system/database.yaml 未定义 unique），改 ON CONFLICT (code) 需先加唯一索引属 Schema 变更，不在本批范围
            foreach (var role in PermissionSeedCatalog.Roles)
            {
                var updateSql = @"
                    UPDATE nc_sys_roles
                    SET name = $2, level = $3, description = $4, data_scope = $5
                    WHERE code = $1";
                var updated = await _dbService.ExecuteNonQueryAsync(updateSql, ct,
                    role.Code, role.Name, role.Level, role.Desc, role.DataScope);
                if (updated.IsFailure)
                    throw new BusinessException(ErrorCodes.DB_QUERY_ERROR, $"更新默认角色失败: {role.Code}: {updated.Message}");
                if (updated.Value > 0)
                    continue;

                var insertSql = @"
                    INSERT INTO nc_sys_roles (id, code, name, level, description, data_scope)
                    VALUES ($1, $2, $3, $4, $5, $6)";
                var inserted = await _dbService.ExecuteNonQueryAsync(insertSql, ct,
                    role.Id, role.Code, role.Name, role.Level, role.Desc, role.DataScope);
                if (inserted.IsFailure)
                    throw new BusinessException(ErrorCodes.DB_QUERY_ERROR, $"插入默认角色失败: {role.Code}: {inserted.Message}");
            }

            // 2) 权限定义：单语句补齐缺失（多行 VALUES + NOT EXISTS）
            var permValues = string.Join(",\n                ", PermissionSeedCatalog.Permissions.Select(p =>
                $"('{Sq(p.Code)}', '{Sq(p.Name)}', '{Sq(p.Module)}', '{Sq(p.Desc)}', {p.SortOrder})"));
            var ensurePermsSql = $@"
                INSERT INTO nc_sys_permissions (code, name, module, description, sort_order)
                SELECT v.code, v.name, v.module, v.description, v.sort_order
                FROM (VALUES
                    {permValues}
                ) AS v(code, name, module, description, sort_order)
                WHERE NOT EXISTS (SELECT 1 FROM nc_sys_permissions p WHERE p.code = v.code)";
            var permsResult = await _dbService.ExecuteNonQueryAsync(ensurePermsSql, ct);
            if (permsResult.IsFailure)
                throw new BusinessException(ErrorCodes.DB_QUERY_ERROR, $"补齐权限定义失败: {permsResult.Message}");

            // 3) 重建 6 个默认角色的角色-权限映射：清空旧映射后按目录矩阵重写。
            //    注意：roleCodes 是 string[]，直接传给 params object[] 会因数组协变被当作形参数组本身
            //    （$1 退化为首个标量 → ANY($1) 报 42809），必须显式包装为单元素参数
            //    （同型先例：MonthlyReportService/SubsidyDataService 中 new object[] { array } 的注释）。
            var roleCodes = PermissionSeedCatalog.Roles.Select(r => r.Code).ToArray();
            var deleteSql = @"
                DELETE FROM nc_perm_role_permissions
                WHERE role_id IN (SELECT id FROM nc_sys_roles WHERE code = ANY($1))";
            var deleteResult = await _dbService.ExecuteNonQueryAsync(deleteSql, ct, new object[] { roleCodes });
            if (deleteResult.IsFailure)
                throw new BusinessException(ErrorCodes.DB_QUERY_ERROR, $"清理旧角色-权限映射失败: {deleteResult.Message}");

            var pairs = PermissionSeedCatalog.RolePermissions
                .SelectMany(kv => kv.Value.Select(permCode => (RoleCode: kv.Key, PermCode: permCode)))
                .ToList();
            var pairValues = string.Join(",\n                ", pairs.Select(p =>
                $"('{Sq(p.RoleCode)}', '{Sq(p.PermCode)}')"));
            var mappingSql = $@"
                INSERT INTO nc_perm_role_permissions (role_id, permission_code, created_at)
                SELECT r.id, m.perm_code, NOW()
                FROM (VALUES
                    {pairValues}
                ) AS m(role_code, perm_code)
                JOIN nc_sys_roles r ON r.code = m.role_code
                JOIN nc_sys_permissions p ON p.code = m.perm_code
                WHERE NOT EXISTS (
                    SELECT 1 FROM nc_perm_role_permissions rp
                    WHERE rp.role_id = r.id AND rp.permission_code = m.perm_code
                )";
            var mappingResult = await _dbService.ExecuteNonQueryAsync(mappingSql, ct);
            if (mappingResult.IsFailure)
                throw new BusinessException(ErrorCodes.DB_QUERY_ERROR, $"重建角色-权限映射失败: {mappingResult.Message}");

            // 4) 清空用户直授（老用户权限一并重建；仅超管一人，无损失）
            var clearGrantsResult = await _dbService.ExecuteNonQueryAsync(
                "DELETE FROM nc_perm_user_permissions", ct);
            if (clearGrantsResult.IsFailure)
                throw new BusinessException(ErrorCodes.DB_QUERY_ERROR, $"清空用户直授权限失败: {clearGrantsResult.Message}");

            // 5) 确保 admin 具备 SUPER_ADMIN（admin 不存在时由 InitializationService 负责）
            var assignSql = @"
                INSERT INTO nc_sys_user_roles (user_id, role_id)
                SELECT u.id, r.id
                FROM nc_sys_users u
                CROSS JOIN nc_sys_roles r
                WHERE u.username = 'admin' AND r.code = 'SUPER_ADMIN'
                  AND NOT EXISTS (
                      SELECT 1 FROM nc_sys_user_roles ur
                      WHERE ur.user_id = u.id AND ur.role_id = r.id)";
            await _dbService.ExecuteNonQueryAsync(assignSql, ct);

            // 6) 写回矩阵版本标记
            await WriteMatrixVersionAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"权限矩阵重建失败: {ex.Message}");
            throw;
        }

        // 7) 递增权限缓存版本并清空本机缓存（多机依赖版本比对失效）
        await IncrementCacheVersionAsync(ct);
        await InvalidateAllCacheAsync();
        _logger.Info($"权限矩阵重建完成: 权限 {PermissionSeedCatalog.Permissions.Length} 条, 映射 {PermissionSeedCatalog.RolePermissions.Values.Sum(v => v.Length)} 条");
    }

    /// <summary>读取库内权限矩阵版本标记（标记缺失/异常返回 null；0 视为未播种）</summary>
    private async Task<int?> ReadMatrixVersionAsync(CancellationToken ct)
    {
        var sql = "SELECT version FROM nc_perm_cache_version WHERE key = $1";
        var result = await _dbService.ExecuteScalarAsync<long>(sql, ct, PermissionSeedCatalog.MatrixVersionKey);
        return result.IsSuccess && result.Value > 0 ? Convert.ToInt32(result.Value) : null;
    }

    /// <summary>写回权限矩阵版本标记（先改后插，不依赖 key 列唯一约束）</summary>
    private async Task WriteMatrixVersionAsync(CancellationToken ct)
    {
        var existsResult = await _dbService.ExecuteScalarAsync(
            "SELECT COUNT(*) FROM nc_perm_cache_version WHERE key = $1", ct, PermissionSeedCatalog.MatrixVersionKey);
        if (existsResult.IsSuccess && Convert.ToInt32(existsResult.Value) > 0)
        {
            await _dbService.ExecuteNonQueryAsync(
                "UPDATE nc_perm_cache_version SET version = $2, updated_at = NOW() WHERE key = $1",
                ct, PermissionSeedCatalog.MatrixVersionKey, PermissionSeedCatalog.MatrixVersion);
        }
        else
        {
            await _dbService.ExecuteNonQueryAsync(
                "INSERT INTO nc_perm_cache_version (key, version, updated_at) VALUES ($1, $2, NOW())",
                ct, PermissionSeedCatalog.MatrixVersionKey, PermissionSeedCatalog.MatrixVersion);
        }
    }

    private async Task<HashSet<string>> LoadUserPermissionsAsync(int userId, CancellationToken ct)
    {
        var sql = @"
            SELECT DISTINCT permission_code FROM (
                SELECT permission_code FROM nc_perm_user_permissions WHERE user_id = $1
                UNION ALL
                SELECT nrp.permission_code
                FROM nc_sys_user_roles ur
                JOIN nc_perm_role_permissions nrp ON ur.role_id = nrp.role_id
                WHERE ur.user_id = $1
            ) AS all_permissions";

        try
        {
            var result = await _dbService.QueryAsync<string>(sql, ct, userId);

            if (result.IsFailure)
            {
                // 违反 AGENTS §6：禁止吞异常/失败返回空集合（空集会被当作"无权限"缓存并清空 UI）
                LogWarn($"用户权限查询失败: UserId={userId}, {result.Message}");
                throw new BusinessException(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    result.Message ?? "加载用户权限失败");
            }

            if (result.Value is not null)
            {
                var permissions = new HashSet<string>(result.Value, StringComparer.OrdinalIgnoreCase);
                LogInfo($"用户权限加载完成: UserId={userId}, Count={permissions.Count}");
                return permissions;
            }

            LogWarn($"用户权限查询结果为空: UserId={userId}");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"LoadUserPermissionsAsync({userId}) 执行失败");
            throw new BusinessException(ErrorCodes.DB_QUERY_ERROR, "加载用户权限失败");
        }
    }

    private async Task<bool> IsCacheExpiredAsync(int userId)
    {
        if (!_cacheTime.TryGetValue(userId, out var cacheTime))
            return true;

        if (DateTime.UtcNow - cacheTime > _cacheDuration)
            return true;

        await CheckVersionAsync();

        return false;
    }

    private async Task CheckVersionAsync()
    {
        if (DateTime.UtcNow - _versionCheckTime < TimeSpan.FromSeconds(_perfOptions.PermissionVersionCheckIntervalSeconds))
            return;

        try
        {
            var sql = "SELECT version FROM nc_perm_cache_version WHERE key = 'permissions'";
            var result = await _dbService.QuerySingleAsync<long>(sql);

            if (result.IsSuccess)
            {
                var newVersion = result.Value;

                if (_currentVersion.HasValue && _currentVersion != newVersion)
                {
                    // 标记过期而非直接清空：重载失败时仍可 stale 返回，避免清缓存后失败=空集
                    foreach (var key in _cache.Keys)
                        _cacheTime[key] = DateTime.MinValue;
                    _logger.Info($"缓存版本已更新，权限缓存已标记过期（保留旧值待重载）");
                }

                _currentVersion = newVersion;
            }
        }
        catch (Exception ex)
        {
            // 降级：版本检查只是缓存刷新的优化手段，失败时继续用现有缓存即可。
            // 原先此处 throw 会一路冒泡到 MainViewModel.LoadPermissionsAsync 的 catch，
            // 进而 CloseWindow() 直接关闭应用——一次数据库瞬时抖动 = 启动即退出。
            _logger.Warn($"检查权限缓存版本失败，降级使用现有缓存: {ex.Message}");
        }

        _versionCheckTime = DateTime.UtcNow;
    }

    private async Task<int?> GetRoleIdByCodeAsync(string roleCode, CancellationToken ct)
    {
        var sql = "SELECT id FROM nc_sys_roles WHERE code = $1 LIMIT 1";
        var result = await _dbService.QuerySingleAsync<int>(sql, ct, roleCode);

        return result.IsSuccess ? result.Value : null;
    }

    /// <summary>SQL 字符串字面量转义（种子常量均为编译期字符串，仅作防御）</summary>
    private static string Sq(string s) => s.Replace("'", "''");

    private async Task IncrementCacheVersionAsync(CancellationToken ct)    {
        var checkSql = "SELECT COUNT(*) FROM nc_perm_cache_version WHERE key = 'permissions'";
        var exists = await _dbService.ExecuteScalarAsync(checkSql, ct);
        if (exists.IsSuccess && Convert.ToInt32(exists.Value) > 0)
        {
            var updateSql = "UPDATE nc_perm_cache_version SET version = version + 1, updated_at = NOW() WHERE key = 'permissions'";
            await _dbService.ExecuteNonQueryAsync(updateSql, ct);
        }
        else
        {
            var insertSql = @"INSERT INTO nc_perm_cache_version (key, version, updated_at)
                             VALUES ('permissions', 1, NOW())";
            await _dbService.ExecuteNonQueryAsync(insertSql, ct);
        }
    }

    public async Task<Result> GrantPermissionToRoleAsync(int roleId, string permissionCode, CancellationToken ct = default)
    {
        LogInfo($"授予角色权限: RoleId={roleId}, Permission={permissionCode}");

        const string sql = @"
INSERT INTO nc_perm_role_permissions (role_id, permission_code)
VALUES ($1, $2)";

        var result = await _dbService.ExecuteNonQueryAsync(sql, ct, roleId, permissionCode);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);

        await IncrementCacheVersionAsync(ct);
        LogInfo($"角色权限授予成功: RoleId={roleId}, Permission={permissionCode}");

        return Result.Success();
    }

    public async Task<Result> RevokePermissionFromRoleAsync(int roleId, string permissionCode, CancellationToken ct = default)
    {
        LogInfo($"撤销角色权限: RoleId={roleId}, Permission={permissionCode}");

        const string sql = "DELETE FROM nc_perm_role_permissions WHERE role_id = $1 AND permission_code = $2";
        var result = await _dbService.ExecuteNonQueryAsync(sql, ct, roleId, permissionCode);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);

        await IncrementCacheVersionAsync(ct);
        LogInfo($"角色权限撤销成功: RoleId={roleId}, Permission={permissionCode}");

        return Result.Success();
    }

    public async Task<IReadOnlyCollection<PermissionDefinition>> GetAllPermissionDefinitionsAsync(CancellationToken ct = default)
    {
        LogInfo("获取所有权限定义");

        const string sql = "SELECT code, name, module, description, sort_order FROM nc_sys_permissions ORDER BY sort_order";
        var result = await _dbService.QueryAsync<PermissionDefinition>(sql, ct);

        if (result.IsSuccess && result.Value is not null && result.Value.Count > 0)
        {
            LogInfo($"获取权限定义完成: Count={result.Value.Count}");
            return result.Value;
        }

        LogWarn("权限定义表为空，回退使用目录常量");
        return PermissionSeedCatalog.Permissions
            .Select(p => new PermissionDefinition
            {
                Code = p.Code,
                Name = p.Name,
                Module = p.Module,
                Description = p.Desc,
                SortOrder = p.SortOrder,
            })
            .ToList();
    }
}
