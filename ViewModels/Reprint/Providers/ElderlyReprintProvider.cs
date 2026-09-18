using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Domain.ElderlyBenefits;

namespace NewCosmos.ViewModels.Reprint.Providers;

/// <summary>
/// 普惠高龄域补打策略（自 ElderlyReprintViewModel 平移）：
/// 已确认+已停发双状态合并搜索 + ElderlyPrintDataBuilder 字段构建（按状态自动判 New/Stop 模板分类）。
/// </summary>
public class ElderlyReprintProvider : IReprintDomainProvider
{
    private readonly IElderlyApplicationService _applicationService;

    public ElderlyReprintProvider(IElderlyApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public string DomainKey => "ElderlyBenefits";
    public string DisplayName => "普惠高龄";
    public ReprintDomainMode Mode => ReprintDomainMode.ArchiveSet;

    public async Task<Result<List<ReprintArchiveItem>>> SearchByPersonAsync(string keyword, int limit = 20, CancellationToken ct = default)
    {
        // 全局搜索：同时查询 Confirmed 和 Stopped 两个状态源并合并；keyword 为空 = 默认最近名单
        var kw = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
        var confirmedResult = await _applicationService.GetPagedAsync(kw!, ElderlyBenefitConstants.StatusConfirmed, 1, limit, ct);
        var stoppedResult = await _applicationService.GetPagedAsync(kw!, ElderlyBenefitConstants.StatusStopped, 1, limit, ct);

        // 双侧均失败才显式失败（对齐 §6 禁止吞异常返回空集合）；单侧失败容忍为该状态无数据
        if (confirmedResult.IsFailure && stoppedResult.IsFailure)
            return Result.Failure<List<ReprintArchiveItem>>(
                confirmedResult.ErrorCode ?? stoppedResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                confirmedResult.Message ?? stoppedResult.Message ?? "普惠高龄搜索失败");

        var merged = new List<ElderlyApplication>();
        if (confirmedResult.IsSuccess && confirmedResult.Value != null)
            merged.AddRange(confirmedResult.Value.Items);
        if (stoppedResult.IsSuccess && stoppedResult.Value != null)
            merged.AddRange(stoppedResult.Value.Items);

        var items = merged.Take(limit).Select(app => new ReprintArchiveItem(
            DomainKey,
            app.Id,
            app.Name ?? "",
            app.IdCard ?? "",
            app.ApplicationNo ?? "",
            app.Status ?? "",
            app.ApplyDate ?? app.ConfirmedAt ?? app.CreatedAt ?? DateTime.MinValue,
            app.Status == ElderlyBenefitConstants.StatusStopped ? "已停发" : "已确认")).ToList();
        return Result.Success(items);
    }

    public async Task<Result<List<ReprintArchiveItem>>> SearchByMonthAsync(int year, int month, int limit = 200, CancellationToken ct = default)
    {
        // 服务端按月查询（新增=apply_date 月、停发=actual_stop_date 月，域内既有口径）；excludeImported=false 与名单口径一致
        var confirmedResult = await _applicationService.GetByMonthAsync(year, month, false, null, false, ct);
        var stoppedResult = await _applicationService.GetByMonthAsync(year, month, true, null, false, ct);

        if (confirmedResult.IsFailure && stoppedResult.IsFailure)
            return Result.Failure<List<ReprintArchiveItem>>(
                confirmedResult.ErrorCode ?? stoppedResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                confirmedResult.Message ?? stoppedResult.Message ?? "普惠高龄按月查询失败");

        var merged = new List<ElderlyApplication>();
        if (confirmedResult.IsSuccess && confirmedResult.Value != null)
            merged.AddRange(confirmedResult.Value);
        if (stoppedResult.IsSuccess && stoppedResult.Value != null)
            merged.AddRange(stoppedResult.Value);

        var items = merged
            .OrderByDescending(app => app.ApplyDate ?? app.ConfirmedAt ?? app.CreatedAt ?? DateTime.MinValue)
            .Take(limit)
            .Select(app => new ReprintArchiveItem(
                DomainKey,
                app.Id,
                app.Name ?? "",
                app.IdCard ?? "",
                app.ApplicationNo ?? "",
                app.Status ?? "",
                app.ApplyDate ?? app.ConfirmedAt ?? app.CreatedAt ?? DateTime.MinValue,
                app.Status == ElderlyBenefitConstants.StatusStopped ? "已停发" : "已确认")).ToList();
        return Result.Success(items);
    }

    public async Task<Result<ReprintArchivePayload>> PrepareAsync(long businessId, CancellationToken ct = default)
    {
        var result = await _applicationService.GetByIdAsync(businessId, ct);
        if (result.IsFailure || result.Value == null)
            return Result.Failure<ReprintArchivePayload>("ARCHIVE_DATA_INCOMPLETE", "加载该记录数据失败，请确认数据完整");
        var app = result.Value;

        var fields = ElderlyPrintDataBuilder.BuildSingleFields(app);

        return Result.Success(new ReprintArchivePayload(
            DomainKey,
            businessId,
            app.Name ?? "",
            app.IdCard ?? "",
            // 根据记录状态自动判断模板分类（登记表/停止类模板）
            app.Status == ElderlyBenefitConstants.StatusStopped ? "Stop" : "New",
            app.Status ?? "",
            fields,
            new List<Dictionary<string, string>>(),
            null));
    }
}
