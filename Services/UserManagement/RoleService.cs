using NewCosmos.Models.Entities.UserManagement;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.UserManagement;

public class RoleService : BaseService, IRoleService
{
    protected override string ServiceName => "RoleService";
    private readonly IDatabaseService _dbService;

    public RoleService(IDatabaseService dbService, ILoggerService logger)
        : base(logger)
    {
        _dbService = dbService;
    }

    public async Task<Role?> GetRoleByIdAsync(int roleId, CancellationToken ct = default)
    {
        const string sql = "SELECT id, name, code, description, level, data_scope FROM nc_sys_roles WHERE id = $1";
        var result = await _dbService.QuerySingleAsync<Role>(sql, ct, roleId);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<Role?> GetRoleByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        const string sql = "SELECT id, name, code, description, level, data_scope FROM nc_sys_roles WHERE code = $1";
        var result = await _dbService.QuerySingleAsync<Role>(sql, ct, code);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<IReadOnlyCollection<Role>> GetAllRolesAsync(CancellationToken ct = default)
    {
        const string sql = "SELECT id, name, code, description, level, data_scope FROM nc_sys_roles ORDER BY level, id";
        var result = await _dbService.QueryAsync<Role>(sql, ct);
        return result.IsSuccess && result.Value is not null ? result.Value : new List<Role>();
    }

    public async Task<IReadOnlyCollection<Role>> GetUserRolesAsync(int userId, CancellationToken ct = default)
    {
        var sql = @"
SELECT r.id, r.name, r.code, r.description, r.level, r.data_scope
FROM nc_sys_roles r
JOIN nc_sys_user_roles ur ON r.id = ur.role_id
WHERE ur.user_id = $1
ORDER BY r.level, r.id";
        var result = await _dbService.QueryAsync<Role>(sql, ct, userId);
        return result.IsSuccess && result.Value is not null ? result.Value : new List<Role>();
    }

    public async Task<bool> AssignRoleToUserAsync(int userId, int roleId, CancellationToken ct = default)
    {
        const string sql = @"
INSERT INTO nc_sys_user_roles (user_id, role_id) 
VALUES ($1, $2)";
        var result = await _dbService.ExecuteNonQueryAsync(sql, ct, userId, roleId);
        return result.IsSuccess && result.Value > 0;
    }

    public async Task<bool> RemoveRoleFromUserAsync(int userId, int roleId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM nc_sys_user_roles WHERE user_id = $1 AND role_id = $2";
        var result = await _dbService.ExecuteNonQueryAsync(sql, ct, userId, roleId);
        return result.IsSuccess && result.Value > 0;
    }

    public async Task<bool> CheckUserHasRoleAsync(int userId, string roleCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(roleCode)) return false;
        var sql = @"
SELECT COUNT(*)
FROM nc_sys_user_roles ur
JOIN nc_sys_roles r ON ur.role_id = r.id
WHERE ur.user_id = $1 AND r.code = $2";
        var result = await _dbService.ExecuteScalarAsync(sql, ct, userId, roleCode);
        return result.IsSuccess && result.Value > 0;
    }

    public async Task<Result<int>> CreateRoleAsync(RoleCreateRequest request, CancellationToken ct = default)
    {
        LogInfo("执行操作");

        if (string.IsNullOrWhiteSpace(request.Name))
            return Result.Failure<int>("VALIDATION_FAILED", "角色名称不能为空");

        if (string.IsNullOrWhiteSpace(request.Code))
            return Result.Failure<int>("VALIDATION_FAILED", "角色代码不能为空");

        var existingCode = await GetRoleByCodeAsync(request.Code, ct);
        if (existingCode != null)
            return Result.Failure<int>("DUPLICATE_CODE", "角色代码已存在");

        const string sql = @"
INSERT INTO nc_sys_roles (name, code, description, level, data_scope)
VALUES ($1, $2, $3, $4, $5)
RETURNING id";
        var result = await _dbService.ExecuteScalarAsync(sql, ct, request.Name, request.Code, request.Description, request.Level, request.DataScope);
        if (result.IsFailure)
            return Result.Failure<int>(result.ErrorCode!, result.Message!);

        var roleId = Convert.ToInt32(result.Value);
        LogInfo("执行操作");
        Logger.LogSecurity("创建角色", ("RoleId", roleId), ("Name", request.Name), ("CreatedBy", request.CreatedBy));

        return Result.Success(roleId);
    }

    public async Task<Result> UpdateRoleAsync(RoleUpdateRequest request, CancellationToken ct = default)
    {
        LogInfo("执行操作");

        if (string.IsNullOrWhiteSpace(request.Name))
            return Result.Failure("VALIDATION_FAILED", "角色名称不能为空");

        const string sql = @"
UPDATE nc_sys_roles
SET name = $1, description = $2, level = $3, data_scope = $4
WHERE id = $5";
        var result = await _dbService.ExecuteNonQueryAsync(sql, ct, request.Name, request.Description, request.Level, request.DataScope, request.RoleId);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);

        // 数据范围可能影响在线客户端的数据可见边界，递增权限缓存版本触发全局失效
        var versionResult = await _dbService.ExecuteNonQueryAsync(
            "UPDATE nc_perm_cache_version SET version = version + 1, updated_at = NOW() WHERE key = 'permissions'", ct);
        if (versionResult.IsFailure)
            LogWarn($"递增权限缓存版本失败（角色数据范围变更可能延迟生效）: {versionResult.Message}");

        LogInfo("执行操作");
        Logger.LogSecurity("更新角色", ("RoleId", request.RoleId), ("UpdatedBy", request.UpdatedBy));

        return Result.Success();
    }

    public async Task<Result> DeleteRoleAsync(int roleId, CancellationToken ct = default)
    {
        LogInfo("执行操作");

        var checkSql = "SELECT COUNT(1) FROM nc_sys_user_roles WHERE role_id = $1";
        var checkResult = await _dbService.ExecuteScalarAsync(checkSql, ct, roleId);
        if (checkResult.IsSuccess && checkResult.Value > 0)
            return Result.Failure("ROLE_IN_USE", "该角色正在被用户使用，无法删除");

        const string sql = "DELETE FROM nc_sys_roles WHERE id = $1";
        var result = await _dbService.ExecuteNonQueryAsync(sql, ct, roleId);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);

        LogInfo("执行操作");
        Logger.LogSecurity("删除角色", ("RoleId", roleId));

        return Result.Success();
    }

    public async Task<Result<IReadOnlyCollection<string>>> GetRolePermissionsAsync(int roleId, CancellationToken ct = default)
    {
        LogInfo("执行操作");

        const string sql = "SELECT permission_code FROM nc_perm_role_permissions WHERE role_id = $1";
        var result = await _dbService.QueryAsync<string>(sql, ct, roleId);

        if (result.IsFailure)
            return Result.Failure<IReadOnlyCollection<string>>(result.ErrorCode!, result.Message!);

        return Result.Success<IReadOnlyCollection<string>>(result.Value ?? new List<string>());
    }

    public async Task<Result> SyncRolePermissionsAsync(int roleId, IReadOnlyCollection<string> permissionCodes, CancellationToken ct = default)
    {
        LogInfo($"同步角色权限: RoleId={roleId}");

        await using var tx = await _dbService.BeginTransactionScopeAsync(ct);
        try
        {
            const string deleteSql = "DELETE FROM nc_perm_role_permissions WHERE role_id = $1";
            var deleteResult = await _dbService.ExecuteNonQueryAsync(deleteSql, ct, roleId);
            if (deleteResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(deleteResult.ErrorCode!, deleteResult.Message!);
            }

            // 多行 VALUES 单语句批量写入（集合可能为空，空集则跳过）
            if (permissionCodes.Count > 0)
            {
                var values = string.Join(",\n                ",
                    permissionCodes.Select((code, i) => $"($1, ${i + 2})"));
                var insertSql = $@"
INSERT INTO nc_perm_role_permissions (role_id, permission_code)
VALUES
                {values}";
                var parameters = new List<object?>(permissionCodes.Count + 1) { roleId };
                parameters.AddRange(permissionCodes);
                var insertResult = await _dbService.ExecuteNonQueryAsync(insertSql, ct, parameters.ToArray());
                if (insertResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure(insertResult.ErrorCode!, insertResult.Message!);
                }
            }

            var checkSql = "SELECT COUNT(1) FROM nc_perm_cache_version WHERE key = 'permissions'";
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

            await tx.CommitAsync(ct);
            LogInfo("执行操作");

            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogError($"操作失败: {ex.Message}");
            return Result.Failure("DB_ERROR", $"操作执行失败: {ex.Message}");
        }
    }
}
