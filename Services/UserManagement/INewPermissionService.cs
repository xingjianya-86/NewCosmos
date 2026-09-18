using NewCosmos.Models.Entities.UserManagement;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.UserManagement;

public interface INewPermissionService
{
    Task<bool> HasPermissionAsync(int userId, string permissionCode, CancellationToken ct = default);
    Task<IReadOnlyCollection<string>> GetUserPermissionCodesAsync(int userId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, bool>> CheckPermissionsAsync(int userId, IReadOnlyCollection<string> permissionCodes, CancellationToken ct = default);
    Task InvalidateUserCacheAsync(int userId);
    Task InvalidateAllCacheAsync();
    Task InitializeDefaultPermissionsAsync(CancellationToken ct = default);
    Task<Result> GrantPermissionToRoleAsync(int roleId, string permissionCode, CancellationToken ct = default);
    Task<Result> RevokePermissionFromRoleAsync(int roleId, string permissionCode, CancellationToken ct = default);
    Task<IReadOnlyCollection<PermissionDefinition>> GetAllPermissionDefinitionsAsync(CancellationToken ct = default);
}
