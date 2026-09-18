using NewCosmos.Models.Entities.UserManagement;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.UserManagement;

public interface IRoleService
{
    Task<Role?> GetRoleByIdAsync(int roleId, CancellationToken ct = default);
    Task<Role?> GetRoleByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyCollection<Role>> GetAllRolesAsync(CancellationToken ct = default);
    Task<IReadOnlyCollection<Role>> GetUserRolesAsync(int userId, CancellationToken ct = default);
    Task<bool> AssignRoleToUserAsync(int userId, int roleId, CancellationToken ct = default);
    Task<bool> RemoveRoleFromUserAsync(int userId, int roleId, CancellationToken ct = default);
    Task<bool> CheckUserHasRoleAsync(int userId, string roleCode, CancellationToken ct = default);

    Task<Result<int>> CreateRoleAsync(RoleCreateRequest request, CancellationToken ct = default);
    Task<Result> UpdateRoleAsync(RoleUpdateRequest request, CancellationToken ct = default);
    Task<Result> DeleteRoleAsync(int roleId, CancellationToken ct = default);
    Task<Result<IReadOnlyCollection<string>>> GetRolePermissionsAsync(int roleId, CancellationToken ct = default);
    Task<Result> SyncRolePermissionsAsync(int roleId, IReadOnlyCollection<string> permissionCodes, CancellationToken ct = default);
}

public class RoleCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Level { get; set; }
    /// <summary>数据范围（SELF/ORG/ORG_AND_CHILDREN/ALL），默认仅本人</summary>
    public string DataScope { get; set; } = Constants.DataScopeConstants.SELF;
    public string CreatedBy { get; set; } = string.Empty;
}

public class RoleUpdateRequest
{
    public int RoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Level { get; set; }
    /// <summary>数据范围（SELF/ORG/ORG_AND_CHILDREN/ALL），默认仅本人</summary>
    public string DataScope { get; set; } = Constants.DataScopeConstants.SELF;
    public string UpdatedBy { get; set; } = string.Empty;
}
