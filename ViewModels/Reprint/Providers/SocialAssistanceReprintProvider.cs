using NewCosmos.Models.Results;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.ViewModels.ArchiveManagement;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

namespace NewCosmos.ViewModels.Reprint.Providers;

/// <summary>
/// 低收入人口域补打策略（自 SocialAssistanceReprintViewModel 平移）：
/// 全状态档案搜索（含在享/停保）+ BuildPrintDataAsync 全套字段构建。
/// </summary>
public class SocialAssistanceReprintProvider : IReprintDomainProvider
{
    public const string DomainKeyConst = "FamilyApplication";

    private readonly IApplicationService _applicationService;
    private readonly ArchiveProductionViewModel _productionViewModel;

    /// <summary>
    /// BuildPrintDataAsync 串行闸：统一页「选中记录准备」（IsBusy 守卫）与「输出分类切换」
    /// （OnOutputCategoryIndexChanged，无 IsBusy 守卫）两条入口可并发进入，共用注入实例的
    /// _fieldData/_supporterTableData 内部字段状态。并发改写会产出血缘不全的打印数据
    /// （FAMILY_LAND_AREA / 赡养人表丢失 → 档案输出清单误剔除「村级土地说明」「赡养费承诺书」）。
    /// </summary>
    private readonly SemaphoreSlim _prepareGate = new(1, 1);

    public SocialAssistanceReprintProvider(
        IApplicationService applicationService,
        ArchiveProductionViewModel productionViewModel)
    {
        _applicationService = applicationService;
        _productionViewModel = productionViewModel;
    }

    public string DomainKey => DomainKeyConst;
    public string DisplayName => "低收入人口";
    public ReprintDomainMode Mode => ReprintDomainMode.ArchiveSet;
    public ReprintMonthWindow MonthWindow => ReprintMonthWindow.BusinessProcess;

    public async Task<Result<List<ReprintArchiveItem>>> SearchByPersonAsync(string keyword, int limit = 20, CancellationToken ct = default)
    {
        // GetPagedAsync(page, size, status=null, keyword) —— status 为空=全状态（含在享/停保）；
        // keyword 为空 = 默认最近名单（各域按业务时间倒序 LIMIT）。
        var kw = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
        var result = await _applicationService.GetPagedAsync(1, limit, null, kw!, ct);
        if (result.IsFailure)
            return Result.Failure<List<ReprintArchiveItem>>(result.ErrorCode!, result.Message ?? "档案搜索失败");

        var items = (result.Value?.Items ?? new List<ApplicationEntity>()).Select(app => new ReprintArchiveItem(
            DomainKey,
            app.Id,
            app.ApplicantName ?? "",
            app.ApplicantIdCard ?? "",
            app.ApplicationNo ?? "",
            app.Status ?? "",
            app.FirstApprovedAt ?? app.UpdatedAt,
            NewCosmos.Constants.ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? "")) { ChainType = app.ChainType ?? "" }).ToList();
        return Result.Success(items);
    }

    public async Task<Result<List<ReprintArchiveItem>>> SearchByMonthAsync(int year, int month, int limit = 200, CancellationToken ct = default)
    {
        // 服务端按月查询（first_approved_at 范围口径，与名单"首次审批/新增"业务时间一致）
        var result = await _applicationService.GetByMonthPagedAsync(year, month, 1, limit, ct);
        if (result.IsFailure)
            return Result.Failure<List<ReprintArchiveItem>>(result.ErrorCode!, result.Message ?? "按月查询失败");

        var items = (result.Value?.Items ?? new List<ApplicationEntity>()).Select(app => new ReprintArchiveItem(
            DomainKey,
            app.Id,
            app.ApplicantName ?? "",
            app.ApplicantIdCard ?? "",
            app.ApplicationNo ?? "",
            app.Status ?? "",
            app.FirstApprovedAt ?? app.UpdatedAt,
            NewCosmos.Constants.ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? "")) { ChainType = app.ChainType ?? "" }).ToList();
        return Result.Success(items);
    }

    public async Task<Result<ReprintArchivePayload>> PrepareAsync(long businessId, CancellationToken ct = default)
    {
        await _prepareGate.WaitAsync(ct);
        try
        {
            var data = await _productionViewModel.BuildPrintDataAsync(businessId);
            if (data == null)
                return Result.Failure<ReprintArchivePayload>("ARCHIVE_DATA_INCOMPLETE", "加载该档案的打印数据失败，请确认档案数据完整");

            return Result.Success(new ReprintArchivePayload(
                DomainKey,
                data.BusinessId ?? businessId,
                data.ApplicantName,
                data.FieldData.TryGetValue(NewCosmos.Constants.FieldKeys.APPLICANT_ID_CARD, out var idCard) ? idCard : "",
                data.Classification,
                "", // Status 由统一页从记录项带入
                data.FieldData,
                data.TableData,
                data.SupporterTableData));
        }
        finally
        {
            _prepareGate.Release();
        }
    }
}
