using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Domain.UserManagement;

namespace NewCosmos.Services.Core;

/// <inheritdoc />
public class AddressScopeService : BaseService, IAddressScopeService
{
    protected override string ServiceName => "AddressScopeService";

    private readonly IOrganizationService _organizationService;

    /// <summary>进程内缓存：组织区划基本不变，避免每张表单反复 JOIN 组织与区划</summary>
    private AddressScope? _cached;

    public AddressScopeService(IOrganizationService organizationService, ILoggerService logger)
        : base(logger)
    {
        _organizationService = organizationService;
    }

    /// <inheritdoc />
    public async Task<Result<AddressScope>> ResolveAsync(CancellationToken ct = default)
    {
        var cached = _cached;
        if (cached != null)
            return Result.Success(cached);

        try
        {
            var orgId = App.CurrentUserOrganizationId;
            if (orgId is null or <= 0)
                return Result.Failure<AddressScope>(ErrorCodes.VALIDATION_FAILED,
                    "当前账号未配置组织单位，无法确定地址管辖范围");

            var orgResult = await _organizationService.GetByIdAsync(orgId.Value, ct);
            if (orgResult.IsFailure || orgResult.Value == null)
                return Result.Failure<AddressScope>(orgResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    orgResult.Message ?? "查询所属组织单位失败");

            var org = orgResult.Value;
            var scope = new AddressScope
            {
                ProvinceId = org.ProvinceId,
                CityId = org.CityId,
                CountyId = org.CountyId,
                TownId = org.TownId,
                VillageId = org.VillageId,
                ProvinceName = org.ProvinceName ?? string.Empty,
                CityName = org.CityName ?? string.Empty,
                CountyName = org.CountyName ?? string.Empty,
                TownName = org.TownName ?? string.Empty,
                VillageName = org.VillageName ?? string.Empty,
                LockLevel = AddressScope.ComputeLockLevel(org)
            };

            LogInfo($"地址管辖范围: OrgId={orgId}, LockLevel={scope.LockLevel}, " +
                    $"{scope.ProvinceName}{scope.CityName}{scope.CountyName}{scope.TownName}{scope.VillageName}");

            _cached = scope;
            return Result.Success(scope);
        }
        catch (Exception ex)
        {
            LogException(ex, "ResolveAsync");
            return Result.FromException<AddressScope>(ex);
        }
    }
}
