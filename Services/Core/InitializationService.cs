using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Database;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Services.Core;

public class InitializationService : BaseService, IInitializationService
{
    protected override string ServiceName => "InitializationService";

    private readonly IDatabaseService _dbService;
    private readonly INewPermissionService _permissionService;
    private readonly ISchemaService _schemaService;
    private readonly IRoleService _roleService;
    private bool? _cachedInitializedStatus;

    public InitializationService(
        IDatabaseService dbService,
        ILoggerService logger,
        INewPermissionService permissionService,
        ISchemaService schemaService,
        IRoleService roleService)
        : base(logger)
    {
        _dbService = dbService;
        _permissionService = permissionService;
        _schemaService = schemaService;
        _roleService = roleService;
    }

    public async Task<bool> IsSystemInitializedAsync()
    {
        if (_cachedInitializedStatus.HasValue)
            return _cachedInitializedStatus.Value;

        try
        {
            var result = await CheckInitializationStatusAsync();
            _cachedInitializedStatus = result.IsSuccess && result.Value;
            return _cachedInitializedStatus.Value;
        }
        catch (Exception ex)
        {
            Logger.Error($"检查系统初始化状态失败: {ex.Message}");
            throw;
        }
    }

    public async Task<Result<bool>> CheckInitializationStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.Info("开始检查系统初始化状态");

            var connectionResult = await _dbService.TestConnectionAsync(cancellationToken);
            if (!connectionResult.IsSuccess)
            {
                return Result<bool>.Failure(
                    connectionResult.ErrorCode ?? ErrorCodes.DB_CONNECTION_FAILED,
                    "数据库连接失败");
            }

            var sql = "SELECT COUNT(*) FROM nc_sys_users WHERE is_active = true";
            var countResult = await _dbService.ExecuteScalarAsync(sql, cancellationToken);

            if (!countResult.IsSuccess)
            {
                Logger.Error("查询用户数量失败");
                return Result<bool>.Failure(
                    countResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    "查询用户数量失败");
            }

            var userCount = Convert.ToInt32(countResult.Value);
            var isInitialized = userCount > 0;

            Logger.Info($"系统初始化状态: {(isInitialized ? "已初始化" : "未初始化")}");

            return Result<bool>.Success(isInitialized);
        }
        catch (Exception ex)
        {
            Logger.Error($"检查系统初始化状态失败: {ex.Message}");
            return Result.FromException<bool>(ex);
        }
    }

    // ── 默认角色 / 权限 / 角色-权限映射：唯一权威在 Constants\PermissionSeedCatalog ──

    /// <summary>SQL 字符串字面量转义（种子常量均为编译期字符串，仅作防御）</summary>
    private static string Sq(string s) => s.Replace("'", "''");

    public async Task<Result<string>> InitializeSystemAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.Info("开始系统初始化");

            var connectionResult = await _dbService.TestConnectionAsync(cancellationToken);
            if (!connectionResult.IsSuccess)
            {
                return Result<string>.Failure(
                    connectionResult.ErrorCode ?? ErrorCodes.DB_CONNECTION_FAILED,
                    "数据库连接失败");
            }

            // Schema 同步（DDL，保持在种子数据事务之外）
            Logger.Info("开始同步数据库Schema");
            var schemaResult = await _schemaService.InitializeAllTablesAsync("system-init", cancellationToken);
            if (!schemaResult.IsSuccess)
            {
                return Result<string>.Failure(
                    schemaResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    "Schema同步失败");
            }
            Logger.Info("数据库Schema同步完成");

            var generatedPassword = Guid.NewGuid().ToString("N")[..12];
            var adminPasswordHash = BCrypt.Net.BCrypt.HashPassword(generatedPassword);

            // 角色 / 权限 / 映射 / 管理员账户：单事务集合化写入，任一步失败整体回滚
            await _dbService.BeginTransactionAsync(cancellationToken);
            try
            {
                var seedResult = await SeedPermissionSystemAsync(adminPasswordHash, cancellationToken);
                if (seedResult.IsFailure)
                {
                    await _dbService.RollbackTransactionAsync();
                    return Result<string>.Failure(
                        seedResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        seedResult.Message ?? "系统初始化失败");
                }
                await _dbService.CommitTransactionAsync(cancellationToken);
            }
            catch
            {
                await _dbService.RollbackTransactionAsync();
                throw;
            }

            // 提交成功后重置状态缓存：此前缓存的 false 会永久生效，导致初始化后仍被判定为未初始化
            _cachedInitializedStatus = null;

            // 辅助初始化（独立于核心事务，失败仅告警，不影响初始化结果）
            try
            {
                await _permissionService.InitializeDefaultPermissionsAsync(cancellationToken);
                Logger.Info("权限服务初始化辅助完成");
            }
            catch (Exception ex)
            {
                Logger.Warn($"权限服务初始化辅助失败: {ex.Message}");
            }

            Logger.Info("系统初始化完成");
            return Result<string>.Success(generatedPassword);
        }
        catch (Exception ex)
        {
            Logger.Error($"系统初始化失败: {ex.Message}");
            return Result.FromException<string>(ex);
        }
    }

    /// <summary>
    /// 在当前环境事务内以集合化 SQL 写入角色、权限、角色-权限映射及默认管理员。
    /// 每类数据一条语句（幂等：NOT EXISTS 防重，表可能不存在唯一约束，故不用 ON CONFLICT）。
    /// </summary>
    private async Task<Result> SeedPermissionSystemAsync(string adminPasswordHash, CancellationToken ct)
    {
        // 1) 默认角色：单语句写入 6 个角色（保留固定 id——该表可能由 YAML Schema 创建，id 无默认序列），
        //    data_scope 为角色数据范围（唯一权威见 PermissionSeedCatalog.Roles）
        var roleValues = string.Join(",\n                ", PermissionSeedCatalog.Roles.Select(r =>
            $"({r.Id}, '{Sq(r.Code)}', '{Sq(r.Name)}', {r.Level}, '{Sq(r.Desc)}', '{Sq(r.DataScope)}')"));
        var rolesSql = $@"
            INSERT INTO nc_sys_roles (id, code, name, level, description, data_scope)
            SELECT v.id, v.code, v.name, v.level, v.description, v.data_scope
            FROM (VALUES
                {roleValues}
            ) AS v(id, code, name, level, description, data_scope)
            WHERE NOT EXISTS (SELECT 1 FROM nc_sys_roles r WHERE r.code = v.code)";
        var rolesResult = await _dbService.ExecuteNonQueryAsync(rolesSql, ct);
        if (rolesResult.IsFailure)
            return Result.Failure(rolesResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, "创建默认角色失败");
        Logger.Info($"默认角色同步完成: 新增 {rolesResult.Value}/{PermissionSeedCatalog.Roles.Length}");

        // 2) 权限定义：单语句写入全部权限
        var permValues = string.Join(",\n                ", PermissionSeedCatalog.Permissions.Select(p =>
            $"('{Sq(p.Code)}', '{Sq(p.Name)}', '{Sq(p.Module)}', '{Sq(p.Desc)}', {p.SortOrder})"));
        var permsSql = $@"
            INSERT INTO nc_sys_permissions (code, name, module, description, sort_order)
            SELECT v.code, v.name, v.module, v.description, v.sort_order
            FROM (VALUES
                {permValues}
            ) AS v(code, name, module, description, sort_order)
            WHERE NOT EXISTS (SELECT 1 FROM nc_sys_permissions p WHERE p.code = v.code)";
        var permsResult = await _dbService.ExecuteNonQueryAsync(permsSql, ct);
        if (permsResult.IsFailure)
            return Result.Failure(permsResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, "创建权限定义失败");
        Logger.Info($"权限定义同步完成: 新增 {permsResult.Value}/{PermissionSeedCatalog.Permissions.Length}");

        // 3) 角色-权限映射：单语句写入全部映射（目录见 PermissionSeedCatalog.RolePermissions）。
        //    JOIN nc_sys_roles 保证角色缺失时不产生行（根除旧实现 role_id=0 的脏数据）；
        //    JOIN nc_sys_permissions 同理保证权限代码有效。
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
            return Result.Failure(mappingResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, "创建角色-权限映射失败");
        Logger.Info($"角色-权限映射同步完成: 新增 {mappingResult.Value}/{pairs.Count}");

        // 4) 默认管理员：存在则重置密码，不存在则创建。
        //    nc_sys_users.id 在 YAML Schema 中无默认序列，保留 MAX+1，但现已处于事务内，
        //    与本次初始化的其余写入原子提交。
        var updateAdminSql = "UPDATE nc_sys_users SET password_hash = $1 WHERE username = $2 RETURNING id";
        var updateResult = await _dbService.ExecuteScalarAsync(updateAdminSql, ct, adminPasswordHash, "admin");
        if (updateResult.IsFailure)
            return Result.Failure(updateResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, "更新管理员密码失败");

        var adminUserId = Convert.ToInt32(updateResult.Value);
        if (adminUserId > 0)
        {
            Logger.Info("管理员密码已更新");
        }
        else
        {
            var insertAdminSql = @"INSERT INTO nc_sys_users (id, username, password_hash, full_name, is_active, created_at)
                                   VALUES (COALESCE((SELECT MAX(id) FROM nc_sys_users), 0) + 1, $1, $2, $3, true, NOW())
                                   RETURNING id";
            var insertResult = await _dbService.ExecuteScalarAsync(insertAdminSql, ct, "admin", adminPasswordHash, "系统管理员");
            if (insertResult.IsFailure)
                return Result.Failure(insertResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, "创建默认管理员账户失败");
            adminUserId = Convert.ToInt32(insertResult.Value);
            Logger.Info("默认管理员账户已创建（用户名: admin）");
        }

        // 5) 分配 SUPER_ADMIN 角色：单语句完成查找+防重+插入，角色缺失时不产生行
        var assignSql = @"
            INSERT INTO nc_sys_user_roles (user_id, role_id)
            SELECT $1, r.id FROM nc_sys_roles r
            WHERE r.code = $2
              AND NOT EXISTS (
                  SELECT 1 FROM nc_sys_user_roles ur
                  WHERE ur.user_id = $1 AND ur.role_id = r.id)";
        var assignResult = await _dbService.ExecuteNonQueryAsync(assignSql, ct, adminUserId, "SUPER_ADMIN");
        if (assignResult.IsFailure)
            return Result.Failure(assignResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, "分配管理员角色失败");
        Logger.Info(assignResult.Value > 0 ? "管理员已分配SUPER_ADMIN角色" : "管理员已拥有SUPER_ADMIN角色");

        // 6) 写入权限矩阵版本标记：后续启动时 NewPermissionService 比对该标记，
        //    一致则跳过重建（保护用户自定义的角色-权限分配），不一致才触发一次性重建。
        //    不用 ON CONFLICT——存量库可能缺 key 列上的唯一约束，沿用"先改后插"惯用法
        var markerExistsSql = "SELECT COUNT(*) FROM nc_perm_cache_version WHERE key = $1";
        var markerExists = await _dbService.ExecuteScalarAsync(markerExistsSql, ct, PermissionSeedCatalog.MatrixVersionKey);
        if (markerExists.IsSuccess && Convert.ToInt32(markerExists.Value) > 0)
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

        return Result.Success();
    }
}
