using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Constants;
using NewCosmos.Services.Domain.TempRelief;
using NewCosmos.Services.Utilities;

namespace NewCosmos.ViewModels.Reprint.Providers;

/// <summary>
/// 临时救助域补打策略（自 TempReliefReprintViewModel 平移）：
/// 已完结记录搜索（已确认+已终止，草稿无补打意义）+ 三类子表明细加载 + TempReliefPrintDataBuilder 字段构建。
/// </summary>
public class TempReliefReprintProvider : IReprintDomainProvider
{
    private readonly ITempReliefService _tempReliefService;
    private readonly IBusinessTimelineService _timelineService;

    public TempReliefReprintProvider(ITempReliefService tempReliefService, IBusinessTimelineService timelineService)
    {
        _tempReliefService = tempReliefService;
        _timelineService = timelineService;
    }

    public string DomainKey => TempReliefConstants.BusinessType;
    public string DisplayName => "临时救助";
    public ReprintDomainMode Mode => ReprintDomainMode.ArchiveSet;
    public ReprintMonthWindow MonthWindow => ReprintMonthWindow.TempReliefFull;

    public async Task<Result<List<ReprintArchiveItem>>> SearchByPersonAsync(string keyword, int limit = 20, CancellationToken ct = default)
    {
        // keyword 为空 = 默认最近名单（已确认+已终止按时间倒序）
        var kw = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
        var result = await _tempReliefService.GetArchivedPagedAsync(kw, 1, limit, ct);
        if (result.IsFailure)
            return Result.Failure<List<ReprintArchiveItem>>(result.ErrorCode!, result.Message ?? "临时救助搜索失败");

        var items = (result.Value?.Items ?? new List<TempReliefApplication>()).Select(app => new ReprintArchiveItem(
            DomainKey,
            app.Id,
            app.ApplicantName ?? "",
            app.ApplicantIdCard ?? "",
            app.ApplicationNo ?? "",
            app.Status ?? "",
            app.ApplyDate ?? app.ReportTime ?? app.CreatedAt ?? DateTime.MinValue,
            TempReliefConstants.GetReliefTypeName(app.ReliefType))).ToList();
        return Result.Success(items);
    }

    public async Task<Result<List<ReprintArchiveItem>>> SearchByMonthAsync(int year, int month, int limit = 200, CancellationToken ct = default)
    {
        // 服务端按月查询（业务时间 COALESCE 口径，与名单业务时间一致）
        var result = await _tempReliefService.GetByMonthPagedAsync(year, month, 1, limit, ct);
        if (result.IsFailure)
            return Result.Failure<List<ReprintArchiveItem>>(result.ErrorCode!, result.Message ?? "临时救助按月查询失败");

        var items = (result.Value?.Items ?? new List<TempReliefApplication>()).Select(app => new ReprintArchiveItem(
            DomainKey,
            app.Id,
            app.ApplicantName ?? "",
            app.ApplicantIdCard ?? "",
            app.ApplicationNo ?? "",
            app.Status ?? "",
            app.ApplyDate ?? app.ReportTime ?? app.CreatedAt ?? DateTime.MinValue,
            TempReliefConstants.GetReliefTypeName(app.ReliefType))).ToList();
        return Result.Success(items);
    }

    public async Task<Result<ReprintArchivePayload>> PrepareAsync(long businessId, CancellationToken ct = default)
    {
        var fullResult = await _tempReliefService.GetByIdAsync(businessId, ct);
        if (fullResult.IsFailure || fullResult.Value == null)
            return Result.Failure<ReprintArchivePayload>("ARCHIVE_DATA_INCOMPLETE", "加载该档案数据失败，请确认档案数据完整");
        var fullApp = fullResult.Value;

        // 加载子表（打印需要疾病/意外/教育/成员明细）
        // 补打正式文书：任一子表读取失败都必须中止，不能打印缺明细的版本。
        var diseases = await _tempReliefService.GetDiseasesByApplicationIdAsync(businessId, ct);
        if (diseases.IsFailure)
            return Result.Failure<ReprintArchivePayload>("ARCHIVE_DATA_INCOMPLETE", $"疾病明细加载失败: {diseases.Message}");
        fullApp.Diseases = diseases.Value ?? new();

        var accidents = await _tempReliefService.GetAccidentsByApplicationIdAsync(businessId, ct);
        if (accidents.IsFailure)
            return Result.Failure<ReprintArchivePayload>("ARCHIVE_DATA_INCOMPLETE", $"灾害明细加载失败: {accidents.Message}");
        fullApp.Accidents = accidents.Value ?? new();

        var educations = await _tempReliefService.GetEducationsByApplicationIdAsync(businessId, ct);
        if (educations.IsFailure)
            return Result.Failure<ReprintArchivePayload>("ARCHIVE_DATA_INCOMPLETE", $"教育明细加载失败: {educations.Message}");
        fullApp.Educations = educations.Value ?? new();

        var membersResult = await _tempReliefService.GetMembersByApplicationIdAsync(businessId, ct);
        if (membersResult.IsFailure)
            return Result.Failure<ReprintArchivePayload>("ARCHIVE_DATA_INCOMPLETE", $"成员明细加载失败: {membersResult.Message}");
        var memberList = membersResult.Value ?? new();

        var (acceptanceDate, investigationDate) = await TempReliefPrintDataBuilder.ResolveScheduleAsync(_timelineService, fullApp);
        var phoneResult = await _tempReliefService.GetReportUnitPhoneAsync(fullApp.ReportUnit, ct);
        if (phoneResult.IsFailure)
            Serilog.Log.Warning("[TempReliefReprint] 报告单位电话读取失败，文书该字段将为空: {Message}", phoneResult.Message);
        var contactUnitPhone = phoneResult.Value ?? string.Empty;
        var fields = TempReliefPrintDataBuilder.BuildSingleFields(
            fullApp, memberList, contactUnitPhone, acceptanceDate, investigationDate);

        return Result.Success(new ReprintArchivePayload(
            DomainKey,
            businessId,
            fullApp.ApplicantName ?? "",
            fullApp.ApplicantIdCard ?? "",
            fullApp.ReliefType ?? "",
            fullApp.Status ?? "",
            fields,
            new List<Dictionary<string, string>>(),
            null));
    }
}
