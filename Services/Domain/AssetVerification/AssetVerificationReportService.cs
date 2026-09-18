using NewCosmos.Helpers;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.AssetVerification;

public class AssetVerificationReportService : BaseService, IAssetVerificationReportService
{
    protected override string ServiceName => "AssetVerificationReportService";
    private readonly IDatabaseService _db;
    private readonly IAssetVerificationService _assetService;

    public AssetVerificationReportService(
        IDatabaseService db,
        IAssetVerificationService assetService,
        ILoggerService logger)
        : base(logger)
    {
        _db = db;
        _assetService = assetService;
    }

    public async Task<Result<MonthlyReportData>> GenerateMonthlyReportAsync(
        int year,
        int month,
        ReportTemplateType templateType,
        string generatedBy,
        CancellationToken ct = default)
    {
        return await GenerateReportAsync(year, month, null, templateType, generatedBy, ct);
    }

    public async Task<Result<MonthlyReportData>> GenerateReportAsync(
        int year,
        int month,
        int? weekNumber,
        ReportTemplateType templateType,
        string generatedBy,
        CancellationToken ct = default)
    {
        var mode = ReportPeriodHelper.GetMode(year, month);
        var isWeekly = mode == ReportPeriodMode.Weekly && weekNumber.HasValue;

        LogInfo(isWeekly
            ? $"开始生成周报表: {year}年第{weekNumber}周"
            : $"开始生成月报表: {year}年{month}月");

        try
        {
            var (startDate, endDate) = isWeekly
                ? ReportPeriodHelper.GetWeeklyPeriod(year, weekNumber!.Value)
                : GetALinePeriod(year, month);

            var statsResult = await _assetService.GetStatsByDateRangeAsync(startDate, endDate, ct);
            if (statsResult.IsFailure)
                return Result.Failure<MonthlyReportData>(statsResult.ErrorCode!, statsResult.Message!);

            var tasksResult = await _assetService.SearchByDateRangePagedAsync(startDate, endDate, null, null, 1, 10000, ct);
            if (tasksResult.IsFailure)
                return Result.Failure<MonthlyReportData>(tasksResult.ErrorCode!, tasksResult.Message!);

            var reportData = new MonthlyReportData
            {
                Year = year,
                Month = month,
                WeekNumber = weekNumber,
                PeriodType = isWeekly ? "weekly" : "monthly",
                GeneratedAt = DateTime.Now,
                GeneratedBy = generatedBy,
                Stats = statsResult.Value!,
                Tasks = tasksResult.Value!.Items
            };

            var history = new MonthlyReportHistory
            {
                Year = year,
                Month = month,
                PeriodType = isWeekly ? "weekly" : "monthly",
                WeekNumber = weekNumber,
                WeekStart = isWeekly ? startDate : null,
                WeekEnd = isWeekly ? endDate.AddDays(-1) : null,
                ReportType = templateType.ToString(),
                TotalCount = reportData.Tasks.Count,
                CompletedCount = statsResult.Value!.CompletedCount,
                PendingCount = statsResult.Value!.PendingCount,
                ErrorCount = statsResult.Value!.ErrorCount,
                GeneratedBy = generatedBy,
                GeneratedAt = DateTime.Now
            };

            var periodDesc = isWeekly ? $"{year}年第{weekNumber}周" : $"{year}年{month}月";
            Logger.LogBusiness("生成资产核查报表",
                ("Year", year),
                ("Month", month),
                ("WeekNumber", weekNumber?.ToString() ?? ""),
                ("PeriodType", isWeekly ? "weekly" : "monthly"),
                ("TotalCount", reportData.Tasks.Count),
                ("GeneratedBy", generatedBy));

            LogInfo($"报表生成成功: {periodDesc}, 共{reportData.Tasks.Count}条记录");
            return Result.Success(reportData);
        }
        catch (Exception ex)
        {
            LogException(ex, "报表生成失败");
            return Result.Failure<MonthlyReportData>(ErrorCodes.UNKNOWN_ERROR, $"报表生成失败: {ex.Message}");
        }
    }

    public async Task<Result<byte[]>> ExportToExcelAsync(int year, int month, CancellationToken ct = default)
    {
        var mode = ReportPeriodHelper.GetMode(year, month);
        if (mode == ReportPeriodMode.Weekly)
        {
            var weeks = ReportPeriodHelper.GetWeeksInMonth(year, month);
            if (weeks.Count > 0)
                return await ExportToExcelAsync(year, month, weeks[0].WeekNumber, ct);
        }
        return await ExportToExcelAsync(year, month, null, ct);
    }

    public async Task<Result<byte[]>> ExportToExcelAsync(int year, int month, int? weekNumber, CancellationToken ct = default)
    {
        var mode = ReportPeriodHelper.GetMode(year, month);
        var isWeekly = mode == ReportPeriodMode.Weekly && weekNumber.HasValue;
        var periodDesc = isWeekly ? $"{year}年第{weekNumber}周" : $"{year}年{month}月";
        LogInfo($"导出Excel: {periodDesc}");

        try
        {
            var reportResult = await GenerateReportAsync(year, month, weekNumber, ReportTemplateType.Default, "System", ct);
            if (reportResult.IsFailure)
                return Result.Failure<byte[]>(reportResult.ErrorCode!, reportResult.Message!);

            var excelBytes = await GenerateExcelBytesAsync(reportResult.Value!, ct);

            Logger.LogBusiness("导出资产核查报表Excel",
                ("Year", year),
                ("Month", month),
                ("WeekNumber", weekNumber?.ToString() ?? ""),
                ("PeriodType", isWeekly ? "weekly" : "monthly"),
                ("RecordCount", reportResult.Value!.Tasks.Count));

            LogInfo($"Excel导出成功: {periodDesc}");
            return Result.Success(excelBytes);
        }
        catch (Exception ex)
        {
            LogException(ex, "Excel导出失败");
            return Result.Failure<byte[]>(ErrorCodes.UNKNOWN_ERROR, $"Excel导出失败: {ex.Message}");
        }
    }

    private Task<byte[]> GenerateExcelBytesAsync(MonthlyReportData data, CancellationToken ct)
    {
        LogInfo($"生成Excel字节数组: {data.Tasks.Count}条记录");

        return Task.Run(() =>
        {
            var isWeekly = data.PeriodType == "weekly" && data.WeekNumber.HasValue;
            var title = isWeekly
                ? $"{data.Year}年第{data.WeekNumber}周资产核查周报表"
                : $"{data.Year}年{data.Month}月资产核查月报表";
            var sheetName = isWeekly
                ? $"{data.Year}年第{data.WeekNumber}周资产核查"
                : $"{data.Year}年{data.Month}月资产核查";

            OfficeOpenXml.ExcelPackage.License.SetNonCommercialOrganization("民政社会救助管理系统");
            using var package = new OfficeOpenXml.ExcelPackage();
            var ws = package.Workbook.Worksheets.Add(sheetName);

            // 标题与统计
            ws.Cells[1, 1].Value = title;
            ws.Cells[1, 1, 1, 10].Merge = true;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.Font.Size = 14;
            ws.Cells[2, 1].Value = $"总数: {data.Stats.TotalCount}  已提交: {data.Stats.SubmittedCount}  有报告: {data.Stats.HasReportCount}  已建档: {data.Stats.ArchivedCount}  已拒绝: {data.Stats.RejectedCount}";
            ws.Cells[2, 1, 2, 10].Merge = true;
            ws.Cells[3, 1].Value = $"生成时间: {data.GeneratedAt:yyyy-MM-dd HH:mm}  生成人: {data.GeneratedBy}";
            ws.Cells[3, 1, 3, 10].Merge = true;

            // 表头
            var headers = new[] { "序号", "姓名", "身份证号", "与户主关系", "家庭住址", "联系电话", "申请日期", "批次号", "状态", "核查结果" };
            for (var i = 0; i < headers.Length; i++)
            {
                ws.Cells[4, i + 1].Value = headers[i];
                ws.Cells[4, i + 1].Style.Font.Bold = true;
            }

            // 数据行
            var row = 5;
            var seq = 1;
            foreach (var task in data.Tasks)
            {
                ct.ThrowIfCancellationRequested();
                ws.Cells[row, 1].Value = seq++;
                ws.Cells[row, 2].Value = task.ArchiveName;
                ws.Cells[row, 3].Value = task.ArchiveIdCard;
                ws.Cells[row, 4].Value = task.Relationship;
                ws.Cells[row, 5].Value = task.FamilyAddress;
                ws.Cells[row, 6].Value = task.ContactPhone;
                ws.Cells[row, 7].Value = task.ApplicationDate == default ? "" : task.ApplicationDate.ToString("yyyy-MM-dd");
                ws.Cells[row, 8].Value = task.BatchId;
                ws.Cells[row, 9].Value = task.StatusDisplay;
                ws.Cells[row, 10].Value = task.VerificationResult;
                row++;
            }

            // 固定列宽
            double[] widths = { 6, 12, 22, 12, 36, 15, 12, 16, 10, 20 };
            for (var i = 0; i < widths.Length; i++)
            {
                ws.Column(i + 1).Width = widths[i];
            }

            return package.GetAsByteArray();
        }, ct);
    }

    public async Task<Result<List<MonthlyReportHistory>>> GetReportHistoryAsync(int year, CancellationToken ct = default)
    {
        LogInfo($"获取报表历史: {year}年");

        var sql = @"SELECT * FROM nc_biz_asset_report_history
                    WHERE year = $1
                    ORDER BY generated_at DESC";
        try
        {
            var result = await _db.QueryAsync<MonthlyReportHistory>(sql, ct, year);
            if (result.IsFailure)
                return Result.Failure<List<MonthlyReportHistory>>(result.ErrorCode!, result.Message!);

            LogInfo($"报表历史获取成功: {year}年, 共{result.Value.Count}条记录");
            return Result.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "获取报表历史失败");
            return Result.Failure<List<MonthlyReportHistory>>(ErrorCodes.UNKNOWN_ERROR, $"获取报表历史失败: {ex.Message}");
        }
    }

    public async Task<Result<string>> SaveReportHistoryAsync(
        MonthlyReportHistory history,
        byte[] reportData,
        CancellationToken ct = default)
    {
        LogInfo($"保存报表历史: {history.Year}年{history.Month}月 周期={history.PeriodType}");

        var sql = @"INSERT INTO nc_biz_asset_report_history
                    (year, month, report_type, total_count, completed_count, pending_count, error_count,
                     generated_by, generated_at, file_path, week_number, week_start, week_end, period_type)
                    VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14)
                    RETURNING id";
        try
        {
            var result = await _db.ExecuteScalarAsync(sql, ct,
                history.Year,
                history.Month,
                history.ReportType,
                history.TotalCount,
                history.CompletedCount,
                history.PendingCount,
                history.ErrorCount,
                history.GeneratedBy,
                history.GeneratedAt,
                history.FilePath,
                history.WeekNumber,
                history.WeekStart,
                history.WeekEnd,
                history.PeriodType ?? "monthly");

            if (result.IsFailure)
                return Result.Failure<string>(result.ErrorCode!, result.Message!);

            LogInfo("报表历史保存成功");
            return Result.Success(result.Value.ToString());
        }
        catch (Exception ex)
        {
            LogException(ex, "保存报表历史失败");
            return Result.Failure<string>(ErrorCodes.UNKNOWN_ERROR, $"保存报表历史失败: {ex.Message}");
        }
    }

    private static (DateTime start, DateTime end) GetALinePeriod(int year, int month)
    {
        return ALinePeriodHelper.GetPeriod(year, month);
    }
}
