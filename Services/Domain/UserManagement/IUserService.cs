using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.UserManagement;

/// <summary>
/// 用户服务接口
/// </summary>
public interface IUserService
{
    /// <summary>
    /// 根据ID获取用户
    /// </summary>
    Task<Result<User>> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// 根据用户名获取用    /// </summary>
    Task<Result<User>> GetByUsernameAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// 分页获取用户列表
    /// </summary>
    Task<Result<PagedResult<User>>> GetPagedAsync(int pageIndex, int pageSize, string keyword = null, bool? isActive = null, string role = null, CancellationToken ct = default);

    /// <summary>
    /// 分页获取用户列表（带组织边界过滤：organizationIds 非空时仅返回该组织集合内的用户）
    /// </summary>
    Task<Result<PagedResult<User>>> GetPagedAsync(int pageIndex, int pageSize, string keyword = null, bool? isActive = null, string role = null, IReadOnlyCollection<int>? organizationIds = null, CancellationToken ct = default);

    /// <summary>
    /// 搜索用户（分页）
    /// </summary>
    Task<Result<PagedResult<User>>> SearchPagedAsync(string keyword, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 搜索用户（分页，带筛选）
    /// </summary>
    Task<Result<PagedResult<User>>> SearchPagedAsync(string keyword, string? role, bool? isActive, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 搜索用户（分页，带筛选与组织边界过滤）
    /// </summary>
    Task<Result<PagedResult<User>>> SearchPagedAsync(string keyword, string? role, bool? isActive, IReadOnlyCollection<int>? organizationIds, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 创建用户
    /// </summary>
    Task<Result<int>> CreateAsync(UserCreateRequest request, CancellationToken ct = default);

    /// <summary>
    /// 更新用户
    /// </summary>
    Task<Result> UpdateAsync(UserUpdateRequest request, CancellationToken ct = default);

    /// <summary>
    /// 删除用户
    /// </summary>
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// 禁用用户
    /// </summary>
    Task<Result> DisableAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// 启用用户
    /// </summary>
    Task<Result> EnableAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// 重置密码
    /// </summary>
    Task<Result> ResetPasswordAsync(int userId, string newPassword, string updatedBy, CancellationToken ct = default);

    /// <summary>
    /// 修改密码
    /// </summary>
    Task<Result> ChangePasswordAsync(int userId, string oldPassword, string newPassword, CancellationToken ct = default);

    /// <summary>
    /// 验证登录
    /// </summary>
    Task<Result<User>> ValidateLoginAsync(string username, string password, CancellationToken ct = default);

    /// <summary>
    /// 认证用户
    /// </summary>
    Task<Result<User>> AuthenticateAsync(string username, string password, CancellationToken ct = default);

    /// <summary>
    /// 检查用户名是否存在
    /// </summary>
    Task<Result<bool>> CheckUsernameExistsAsync(string username, int? excludeId = null, CancellationToken ct = default);

    /// <summary>
    /// 分配角色
    /// </summary>
    Task<Result> AssignRoleAsync(int userId, int roleId, string assignedBy, CancellationToken ct = default);

    /// <summary>
    /// 移除角色
    /// </summary>
    Task<Result> RemoveRoleAsync(int userId, int roleId, CancellationToken ct = default);

    /// <summary>
    /// 获取用户角色列表
    /// </summary>
    Task<Result<List<Role>>> GetUserRolesAsync(int userId, CancellationToken ct = default);

    /// <summary>
    /// 根据组织ID获取用户列表
    /// </summary>
    Task<Result<List<User>>> GetByOrganizationAsync(int organizationId, CancellationToken ct = default);
}

/// <summary>
/// 用户实体 - 对应原数据库 users     /// </summary>
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string IdentityCard { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int? OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public bool IsPasswordChangeRequired { get; set; } = true;
    public string RoleName { get; set; } = string.Empty;
    public int RoleId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool CanBeManaged { get; set; } = true;
}

/// <summary>
/// 用户创建请求
/// </summary>
public class UserCreateRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string IdentityCard { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public int? OrganizationId { get; set; }
    public int? DefaultRoleId { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// 用户更新请求
/// </summary>
public class UserUpdateRequest
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string IdentityCard { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public int? OrganizationId { get; set; }
    public bool? IsActive { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}
