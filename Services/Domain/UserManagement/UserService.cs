using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.UserManagement;

public class UserService : BaseService, IUserService
{
    protected override string ServiceName => "UserService";
    private readonly IDatabaseService _db;

    public UserService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<Result<User>> GetByIdAsync(int id, CancellationToken ct = default)
    {
        LogInfo($"执行操作");
        var sql = @"SELECT u.*, o.name as organization_name 
                    FROM nc_sys_users u 
                    LEFT JOIN nc_sys_organizations o ON u.organization_id = o.id 
                    WHERE u.id = $1";
        return await _db.QuerySingleAsync<User>(sql, ct, id);
    }

    public async Task<Result<User>> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        ValidateNotNullOrEmpty(username, nameof(username));
        LogInfo($"执行操作");
        var sql = @"SELECT u.*, o.name as organization_name 
                    FROM nc_sys_users u 
                    LEFT JOIN nc_sys_organizations o ON u.organization_id = o.id 
                    WHERE u.username = $1";
        return await _db.QuerySingleAsync<User>(sql, ct, username);
    }

    public async Task<Result<PagedResult<User>>> GetPagedAsync(int pageIndex, int pageSize, string? keyword = null, bool? isActive = null, string? role = null, CancellationToken ct = default)
        => await GetPagedAsync(pageIndex, pageSize, keyword, isActive, role, null, ct);

    public async Task<Result<PagedResult<User>>> GetPagedAsync(int pageIndex, int pageSize, string? keyword = null, bool? isActive = null, string? role = null, IReadOnlyCollection<int>? organizationIds = null, CancellationToken ct = default)
    {
        LogInfo($"分页获取用户: 第{pageIndex}页");

        var conditions = new SqlConditionBuilder()
            .AddIf(!string.IsNullOrEmpty(keyword), "(u.username LIKE {0} OR u.full_name LIKE {0})", $"%{keyword}%")
            .AddIf(isActive.HasValue, "u.is_active = {0}", (object?)isActive)
            .AddIf(!string.IsNullOrEmpty(role),
                "u.id IN (SELECT ur.user_id FROM nc_sys_user_roles ur INNER JOIN nc_sys_roles r ON ur.role_id = r.id WHERE r.code = {0})",
                role)
            // 组织树边界过滤：仅圈定可管理组织内的用户（NULL 组织用户不在任何组织子树内，天然被排除）
            .AddIf(organizationIds is { Count: > 0 }, "u.organization_id = ANY({0})", (object?)organizationIds);

        var where = conditions.ToWhereClause();
        var countSql = $"SELECT COUNT(*) FROM nc_sys_users u{where}";
        var querySql = $@"SELECT u.*, o.name as organization_name 
                         FROM nc_sys_users u 
                         LEFT JOIN nc_sys_organizations o ON u.organization_id = o.id{where}";

        var countResult = await _db.ExecuteScalarAsync<long>(countSql, ct, conditions.GetParameters());
        if (countResult.IsFailure)
            return Result.Failure<PagedResult<User>>(countResult.ErrorCode!, countResult.Message!);

        var offset = (pageIndex - 1) * pageSize;
        querySql += $" ORDER BY u.id ASC LIMIT ${conditions.ParamCount + 1} OFFSET ${conditions.ParamCount + 2}";
        var pageParams = new List<object?>(conditions.GetParameters()) { pageSize, offset };

        var listResult = await _db.QueryAsync<User>(querySql, ct, pageParams.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<User>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<User>.FromList(listResult.Value, pageIndex, pageSize, Convert.ToInt32(countResult.Value)));
    }

    public async Task<Result<int>> CreateAsync(UserCreateRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));
        ValidateNotNullOrEmpty(request.Username, nameof(request.Username));
        ValidateNotNullOrEmpty(request.Password, nameof(request.Password));
        ValidateNotNullOrEmpty(request.Phone, nameof(request.Phone));

        LogInfo($"执行操作");

        var existsResult = await CheckUsernameExistsAsync(request.Username, null, ct);
        if (existsResult.IsSuccess && existsResult.Value)
            return Result.Failure<int>(ErrorCodes.DUPLICATE_USERNAME, "用户名已存在");

        var phoneExistsSql = "SELECT COUNT(*) FROM nc_sys_users WHERE phone = $1";
        var phoneExistsResult = await _db.ExecuteScalarAsync(phoneExistsSql, ct, request.Phone);
        if (phoneExistsResult.IsSuccess && phoneExistsResult.Value > 0)
            return Result.Failure<int>(ErrorCodes.DUPLICATE_PHONE, "手机号已存在");

        var passwordHash = HashPassword(request.Password);

        var sql = @"INSERT INTO nc_sys_users 
            (username, password_hash, full_name, phone, identity_card, position, organization_id, is_active, is_password_change_required, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, TRUE, TRUE, NOW(), NOW())
            RETURNING id";

        var insertResult = await _db.ExecuteScalarAsync(sql, ct,
            request.Username, passwordHash, request.FullName, request.Phone, request.IdentityCard,
            request.Position, request.OrganizationId);

        if (insertResult.IsFailure)
            return Result.Failure<int>(insertResult.ErrorCode!, insertResult.Message!);

        var userId = Convert.ToInt32(insertResult.Value);

        if (request.DefaultRoleId.HasValue)
        {
            await AssignRoleAsync(userId, request.DefaultRoleId.Value, request.CreatedBy, ct);
        }

        LogInfo($"执行操作");
        Logger.LogSecurity("创建用户", ("UserId", userId), ("Username", request.Username), ("CreatedBy", request.CreatedBy));
        return Result.Success(userId);
    }

    public async Task<Result> UpdateAsync(UserUpdateRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));

        LogInfo($"执行操作");

        var userResult = await GetByIdAsync(request.UserId, ct);
        if (userResult.IsFailure)
            return Result.Failure(userResult.ErrorCode!, userResult.Message!);

        var user = userResult.Value;
        if (user == null)
            return Result.Failure(ErrorCodes.USER_NOT_FOUND, "用户不存在");

        var sql = @"UPDATE nc_sys_users SET 
            full_name = $1, phone = $2, identity_card = $3, position = $4, organization_id = $5, 
            is_active = $6, updated_at = NOW()
            WHERE id = $7";

        var updateResult = await _db.ExecuteNonQueryAsync(sql, ct,
            request.FullName, request.Phone, request.IdentityCard, request.Position, request.OrganizationId,
            request.IsActive ?? user.IsActive, request.UserId);

        if (updateResult.IsFailure)
            return Result.Failure(updateResult.ErrorCode!, updateResult.Message!);

        LogInfo($"执行操作");
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        LogInfo($"执行操作");

        var userResult = await GetByIdAsync(id, ct);
        if (userResult.IsFailure)
            return Result.Failure(userResult.ErrorCode!, userResult.Message!);

        var user = userResult.Value;
        if (user == null)
            return Result.Failure(ErrorCodes.USER_NOT_FOUND, "用户不存在");

        var sql = "UPDATE nc_sys_users SET is_active = false, updated_at = NOW() WHERE id = $1";
        var deleteResult = await _db.ExecuteNonQueryAsync(sql, ct, id);

        if (deleteResult.IsFailure)
            return Result.Failure(deleteResult.ErrorCode!, deleteResult.Message!);

        LogInfo($"执行操作");
        Logger.LogSecurity("删除用户", ("UserId", id), ("Username", user.Username));
        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(int userId, string newPassword, string updatedBy, CancellationToken ct = default)
    {
        ValidateNotNullOrEmpty(newPassword, nameof(newPassword));

        LogInfo($"执行操作");

        var userResult = await GetByIdAsync(userId, ct);
        if (userResult.IsFailure)
            return Result.Failure(userResult.ErrorCode!, userResult.Message!);

        var user = userResult.Value;
        if (user == null)
            return Result.Failure(ErrorCodes.USER_NOT_FOUND, "用户不存在");

        var passwordHash = HashPassword(newPassword);
        var sql = @"UPDATE nc_sys_users SET 
            password_hash = $1, is_password_change_required = TRUE, updated_at = NOW()
            WHERE id = $2";

        var updateResult = await _db.ExecuteNonQueryAsync(sql, ct, passwordHash, userId);

        if (updateResult.IsFailure)
            return Result.Failure(updateResult.ErrorCode!, updateResult.Message!);

        LogInfo($"执行操作");
        Logger.LogSecurity("重置密码", ("UserId", userId), ("UpdatedBy", updatedBy));
        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(int userId, string oldPassword, string newPassword, CancellationToken ct = default)
    {
        ValidateNotNullOrEmpty(oldPassword, nameof(oldPassword));
        ValidateNotNullOrEmpty(newPassword, nameof(newPassword));

        LogInfo($"执行操作");

        var userResult = await GetByIdAsync(userId, ct);
        if (userResult.IsFailure)
            return Result.Failure(userResult.ErrorCode!, userResult.Message!);

        var user = userResult.Value;
        if (user == null)
            return Result.Failure(ErrorCodes.USER_NOT_FOUND, "用户不存在");

        if (!VerifyPassword(oldPassword, user.PasswordHash))
            return Result.Failure(ErrorCodes.INVALID_PASSWORD, "原密码错误");

        var passwordHash = HashPassword(newPassword);
        var sql = @"UPDATE nc_sys_users SET 
            password_hash = $1, is_password_change_required = FALSE, updated_at = NOW()
            WHERE id = $2";

        var updateResult = await _db.ExecuteNonQueryAsync(sql, ct, passwordHash, userId);

        if (updateResult.IsFailure)
            return Result.Failure(updateResult.ErrorCode!, updateResult.Message!);

        LogInfo($"执行操作");
        Logger.LogSecurity("修改密码", ("UserId", userId));
        return Result.Success();
    }

    public async Task<Result<User>> ValidateLoginAsync(string username, string password, CancellationToken ct = default)
    {
        ValidateNotNullOrEmpty(username, nameof(username));
        ValidateNotNullOrEmpty(password, nameof(password));

        LogInfo($"开始验证登录: 用户名={username}");

        var userResult = await GetByUsernameAsync(username, ct);
        if (userResult.IsFailure)
        {
            LogInfo($"用户查询失败: {userResult.Message}");
            return Result.Failure<User>(userResult.ErrorCode!, userResult.Message!);
        }

        var user = userResult.Value;
        if (user == null)
        {
            Logger.LogLoginFailed(username, "未知", "用户不存在", 1);
            return Result.Failure<User>(ErrorCodes.AUTHENTICATION_FAILED, "用户不存在");
        }

        if (!user.IsActive)
        {
            Logger.LogLoginFailed(username, "未知", "账户已禁用", 0);
            return Result.Failure<User>(ErrorCodes.ACCOUNT_LOCKED, "账户已禁用");
        }

        var hash = user.PasswordHash;

        var verifyResult = VerifyPassword(password, hash);

        if (!verifyResult)
        {
            Logger.LogLoginFailed(username, "未知", "密码错误", 1);
            return Result.Failure<User>(ErrorCodes.INVALID_PASSWORD, "密码错误");
        }

        // 登录成功只记审计事件（含用户 Id/名称/IP/结果），不再重复 LogInfo——用户名已在开头记录
        Logger.LogLoginSuccess(user.Id, username, "未知", "确定");
        return Result.Success(user);
    }

    public async Task<Result<bool>> CheckUsernameExistsAsync(string username, int? excludeId = null, CancellationToken ct = default)
    {
        ValidateNotNullOrEmpty(username, nameof(username));

        string sql;
        object[] parameters;

        if (excludeId.HasValue)
        {
            sql = "SELECT COUNT(*) FROM nc_sys_users WHERE username = $1 AND id != $2";
            parameters = new object[] { username, excludeId.Value };
        }
        else
        {
            sql = "SELECT COUNT(*) FROM nc_sys_users WHERE username = $1";
            parameters = new object[] { username };
        }

        var result = await _db.ExecuteScalarAsync(sql, ct, parameters);
        if (result.IsFailure)
            return Result.Failure<bool>(result.ErrorCode!, result.Message!);

        return Result.Success(result.Value > 0);
    }

    public async Task<Result> AssignRoleAsync(int userId, int roleId, string assignedBy, CancellationToken ct = default)
    {
        LogInfo($"分配角色: UserId={userId}");

        var existsSql = "SELECT COUNT(*) FROM nc_sys_user_roles WHERE user_id = $1 AND role_id = $2";
        var existsResult = await _db.ExecuteScalarAsync(existsSql, ct, userId, roleId);
        if (existsResult.IsSuccess && existsResult.Value > 0)
            return Result.Success();

        var sql = @"INSERT INTO nc_sys_user_roles (user_id, role_id)
                    VALUES ($1, $2)";
        var insertResult = await _db.ExecuteNonQueryAsync(sql, ct, userId, roleId);

        if (insertResult.IsFailure)
            return Result.Failure(insertResult.ErrorCode!, insertResult.Message!);

        LogInfo($"执行操作");
        Logger.LogSecurity("分配角色", ("UserId", userId), ("RoleId", roleId), ("AssignedBy", assignedBy));
        return Result.Success();
    }

    public async Task<Result> RemoveRoleAsync(int userId, int roleId, CancellationToken ct = default)
    {
        LogInfo($"移除角色: UserId={userId}");

        var sql = "DELETE FROM nc_sys_user_roles WHERE user_id = $1 AND role_id = $2";
        var deleteResult = await _db.ExecuteNonQueryAsync(sql, ct, userId, roleId);

        if (deleteResult.IsFailure)
            return Result.Failure(deleteResult.ErrorCode!, deleteResult.Message!);

        LogInfo($"执行操作");
        return Result.Success();
    }

    public async Task<Result<List<Role>>> GetUserRolesAsync(int userId, CancellationToken ct = default)
    {
        LogInfo($"执行操作");
        var sql = @"SELECT r.* FROM nc_sys_roles r 
                    INNER JOIN nc_sys_user_roles ur ON r.id = ur.role_id 
                    WHERE ur.user_id = $1";
        return await _db.QueryAsync<Role>(sql, ct, userId);
    }

    public async Task<Result<List<User>>> GetByOrganizationAsync(int organizationId, CancellationToken ct = default)
    {
        LogInfo($"执行操作");
        var sql = @"SELECT u.*, o.name as organization_name 
                    FROM nc_sys_users u 
                    LEFT JOIN nc_sys_organizations o ON u.organization_id = o.id 
                    WHERE u.organization_id = $1 AND u.is_active = TRUE
                    ORDER BY u.id ASC";
        return await _db.QueryAsync<User>(sql, ct, organizationId);
    }

    public async Task<Result<PagedResult<User>>> SearchPagedAsync(string keyword, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo($"搜索用户: keyword={keyword}, 第{pageIndex}页");
        return await GetPagedAsync(pageIndex, pageSize, keyword, null, null, ct);
    }

    public async Task<Result<PagedResult<User>>> SearchPagedAsync(string keyword, string? role, bool? isActive, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo($"搜索用户: keyword={keyword}, role={role}, isActive={isActive}, 第{pageIndex}页");
        return await SearchPagedAsync(keyword, role, isActive, null, pageIndex, pageSize, ct);
    }

    /// <summary>带组织边界过滤的分页搜索：organizationIds 为空则不过滤（超管全量）</summary>
    public async Task<Result<PagedResult<User>>> SearchPagedAsync(string keyword, string? role, bool? isActive, IReadOnlyCollection<int>? organizationIds, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo($"搜索用户: keyword={keyword}, role={role}, isActive={isActive}, orgFilter={organizationIds?.Count ?? 0}, 第{pageIndex}页");
        return await GetPagedAsync(pageIndex, pageSize, keyword, isActive, role, organizationIds, ct);
    }

    public async Task<Result> DisableAsync(int id, CancellationToken ct = default)
    {
        LogInfo($"执行操作");
        var sql = "UPDATE nc_sys_users SET is_active = false, updated_at = NOW() WHERE id = $1";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, id);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        LogInfo($"执行操作");
        return Result.Success();
    }

    public async Task<Result> EnableAsync(int id, CancellationToken ct = default)
    {
        LogInfo($"执行操作");
        var sql = "UPDATE nc_sys_users SET is_active = true, updated_at = NOW() WHERE id = $1";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, id);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        LogInfo($"执行操作");
        return Result.Success();
    }

    public async Task<Result<User>> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        return await ValidateLoginAsync(username, password, ct);
    }

    private static string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password);
    }

    private static bool VerifyPassword(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }
        
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (ArgumentException)
        {
            // hash 格式无效时返回 false
            return false;
        }
    }
}
