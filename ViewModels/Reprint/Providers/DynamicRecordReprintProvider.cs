using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.Printing;
using NewCosmos.Services.Domain.SocialAssistance;

namespace NewCosmos.ViewModels.Reprint.Providers;

/// <summary>
/// 动态管理档案域补打策略：
/// 变更记录→档案链归一到现役档案搜索（IDynamicManagementRecordService），
/// 留痕原样补打（pdf_data 优先，文件路径兜底）+ 现势重新生成（写新留痕）。
/// </summary>
public class DynamicRecordReprintProvider : IReprintDomainProvider, IDynamicRecordReprintCapability
{
    private readonly IDynamicManagementRecordService _recordService;
    private readonly IApplicationService _applicationService;
    private readonly IPrintRecordService _printRecordService;
    private readonly StorageOptions _storageOptions;
    private readonly ILoggerService _logger;

    public DynamicRecordReprintProvider(
        IDynamicManagementRecordService recordService,
        IApplicationService applicationService,
        IPrintRecordService printRecordService,
        StorageOptions storageOptions,
        ILoggerService logger)
    {
        _recordService = recordService;
        _applicationService = applicationService;
        _printRecordService = printRecordService;
        _storageOptions = storageOptions;
        _logger = logger;
    }

    public string DomainKey => DynamicManagementRecordService.BusinessTypeKey;
    public string DisplayName => "动态管理档案";
    public ReprintDomainMode Mode => ReprintDomainMode.DynamicRecord;
    public ReprintMonthWindow MonthWindow => ReprintMonthWindow.BusinessProcess;

    public async Task<Result<List<ReprintArchiveItem>>> SearchByPersonAsync(string keyword, int limit = 20, CancellationToken ct = default)
    {
        // 不归一：逐档案返回（含历史旧档案 + 现役档案），供补打中心新旧选择
        var result = await _recordService.SearchChangedArchivesRawAsync(keyword, limit, ct);
        if (result.IsFailure)
            return Result.Failure<List<ReprintArchiveItem>>(result.ErrorCode!, result.Message ?? "变更档案搜索失败");

        var items = (result.Value ?? new List<ChangedArchiveSummary>()).Select(s => new ReprintArchiveItem(
            DomainKey,
            s.ApplicationId,
            s.HeadName,
            s.IdCard,
            s.ApplicationNo,
            s.Status, // Stopped=历史档案
            s.LastChangeDate,
            $"{NewCosmos.Constants.ClassificationConstants.ConvertToFullName(s.Classification)} · 变更{s.LastChangeDate:yyyy-MM-dd}") { ChainType = s.ChainType ?? "" }).ToList();
        return Result.Success(items);
    }

    public async Task<Result<List<ReprintArchiveItem>>> SearchByMonthAsync(int year, int month, int limit = 200, CancellationToken ct = default)
    {
        // 服务端按月查询（变更日期范围，档案链归一现役）
        var result = await _recordService.SearchMonthlyChangedArchivesAsync(year, month, ct);
        if (result.IsFailure)
            return Result.Failure<List<ReprintArchiveItem>>(result.ErrorCode!, result.Message ?? "变更档案按月查询失败");

        var items = (result.Value ?? new List<ChangedArchiveSummary>())
            .OrderByDescending(s => s.LastChangeDate)
            .Take(limit)
            .Select(s => new ReprintArchiveItem(
                DomainKey,
                s.ApplicationId,
                s.HeadName,
                s.IdCard,
                s.ApplicationNo,
                s.Status,
                s.LastChangeDate,
                $"{NewCosmos.Constants.ClassificationConstants.ConvertToFullName(s.Classification)} · 变更{s.LastChangeDate:yyyy-MM-dd}") { ChainType = s.ChainType ?? "" }).ToList();
        return Result.Success(items);
    }

    public async Task<Result<ReprintArchivePayload>> PrepareAsync(long businessId, CancellationToken ct = default)
    {
        // 档案链归一 + 字段装配（DynamicRecord 模式不进 ArchiveOutput，字段供展示/重新生成）
        var normalizeResult = await _recordService.NormalizeToLatestApplicationIdAsync(businessId, ct);
        if (normalizeResult.IsFailure)
            return Result.Failure<ReprintArchivePayload>(normalizeResult.ErrorCode!, normalizeResult.Message!);

        var fieldsResult = await _recordService.BuildFieldsAsync(normalizeResult.Value, ct);
        if (fieldsResult.IsFailure)
            return Result.Failure<ReprintArchivePayload>(fieldsResult.ErrorCode!, fieldsResult.Message!);
        var fields = fieldsResult.Value;

        fields.TryGetValue(NewCosmos.Constants.FieldKeys.DM_HEAD_NAME, out var name);
        fields.TryGetValue(NewCosmos.Constants.FieldKeys.DM_HEAD_HUKOU, out _);

        return Result.Success(new ReprintArchivePayload(
            DomainKey,
            normalizeResult.Value,
            name ?? "",
            "", // 身份证不在 38 字段集内，展示层从搜索项带入
            "", "",
            fields,
            new List<Dictionary<string, string>>(),
            null));
    }

    public async Task<Result<List<DynamicPrintHistoryItem>>> GetPrintHistoryAsync(long applicationId, CancellationToken ct = default)
    {
        try
        {
            // 留痕表（含 pdf 实际大小与 pdf_path，原样补打判定用）
            var result = await _printRecordService.GetRecentByBusinessAsync(
                DynamicManagementRecordService.BusinessTypeKey, applicationId, 50, ct);
            if (result.IsFailure)
                return Result.Failure<List<DynamicPrintHistoryItem>>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    result.Message ?? "打印留痕查询失败");

            var items = (result.Value ?? new List<PrintRecord>()).Select(r => new DynamicPrintHistoryItem(
                r.Id,
                r.TemplateName,
                r.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                NewCosmos.Services.Core.DataMasker.MaskName(r.OperatorName),
                r.Status,
                r.Remark,
                r.PdfSize > 0,
                r.PdfPath)).ToList();
            return Result.Success(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "动态管理留痕查询失败");
            return Result.FromException<List<DynamicPrintHistoryItem>>(ex);
        }
    }

    public async Task<Result<string>> ReprintOriginalAsync(long printRecordId, CancellationToken ct = default)
    {
        try
        {
            var result = await _printRecordService.GetPdfContentByIdAsync(printRecordId, ct);
            if (result.IsFailure || result.Value == null)
                return Result.Failure<string>(ErrorCodes.NOT_FOUND, "留痕记录不存在");

            var row = result.Value;

            // 优先用留痕存的 PDF 字节（不依赖输出文件是否仍存在）
            if (row.PdfData is { Length: > 0 })
            {
                var tempDir = _storageOptions.GetTempPath("ReprintPreview");
                Directory.CreateDirectory(tempDir);
                var tempPath = Path.Combine(tempDir, $"dmr_reprint_{printRecordId}_{DateTime.Now:yyyyMMddHHmmssfff}.pdf");
                await File.WriteAllBytesAsync(tempPath, row.PdfData, ct);
                _logger.LogBusiness("动态管理档案原样补打", ("PrintRecordId", printRecordId), ("Source", "pdf_data"));
                return Result.Success(tempPath);
            }

            // 兜底：留痕记录的文件路径仍存在则直接使用
            if (!string.IsNullOrWhiteSpace(row.PdfPath) && File.Exists(row.PdfPath))
            {
                _logger.LogBusiness("动态管理档案原样补打", ("PrintRecordId", printRecordId), ("Source", "pdf_path"));
                return Result.Success(row.PdfPath);
            }

            return Result.Failure<string>(ErrorCodes.NOT_FOUND, "该留痕既无 PDF 数据、原文件也不存在，无法原样补打（可用\"重新生成\"按现势数据重出）");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "动态管理档案原样补打失败");
            return Result.FromException<string>(ex);
        }
    }

    public async Task<Result<string>> RegenerateAsync(long applicationId, bool normalizeToLatest = true, CancellationToken ct = default)
    {
        // ExportSingleAsync 内部按 normalizeToLatest 决定归一或按所选版本；并写留痕（含 pdf_data）
        return await _recordService.ExportSingleAsync(applicationId, normalizeToLatest, ct);
    }

    public async Task<Result<List<ReprintArchiveItem>>> SearchMonthlyChangedAsync(int year, int month, CancellationToken ct = default)
    {
        var result = await _recordService.SearchMonthlyChangedArchivesAsync(year, month, ct);
        if (result.IsFailure)
            return Result.Failure<List<ReprintArchiveItem>>(result.ErrorCode!, result.Message ?? "变更人群查询失败");

        var items = (result.Value ?? new List<ChangedArchiveSummary>()).Select(s => new ReprintArchiveItem(
            DomainKey,
            s.ApplicationId,
            s.HeadName,
            s.IdCard,
            s.ApplicationNo,
            "",
            s.LastChangeDate,
            $"{NewCosmos.Constants.ClassificationConstants.ConvertToFullName(s.Classification)} · 变更{s.LastChangeDate:yyyy-MM-dd}") { ChainType = s.ChainType ?? "" }).ToList();
        return Result.Success(items);
    }

    public async Task<Result<string>> BatchExportRangeAsync(DateTime fromInclusive, DateTime toInclusive, CancellationToken ct = default)
    {
        return await _recordService.ExportRangeBatchAsync(fromInclusive, toInclusive, ct);
    }

    public Task<Result<(int SuccessCount, List<(string Name, string Error)> Failed)>> PrintAsync(
        long applicationId, string printerName, int copies, CancellationToken ct = default)
    {
        return _recordService.PrintAsync(new[] { applicationId }, printerName, copies, null, ct);
    }
}
