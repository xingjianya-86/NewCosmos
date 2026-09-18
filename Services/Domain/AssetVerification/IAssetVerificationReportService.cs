using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.AssetVerification;

public interface IAssetVerificationReportService
{
    Task<Result<MonthlyReportData>> GenerateMonthlyReportAsync(
        int year,
        int month,
        ReportTemplateType templateType,
        string generatedBy,
        CancellationToken ct = default);

    Task<Result<MonthlyReportData>> GenerateReportAsync(
        int year,
        int month,
        int? weekNumber,
        ReportTemplateType templateType,
        string generatedBy,
        CancellationToken ct = default);

    Task<Result<byte[]>> ExportToExcelAsync(
        int year,
        int month,
        CancellationToken ct = default);

    Task<Result<byte[]>> ExportToExcelAsync(
        int year,
        int month,
        int? weekNumber,
        CancellationToken ct = default);

    Task<Result<List<MonthlyReportHistory>>> GetReportHistoryAsync(
        int year,
        CancellationToken ct = default);

    Task<Result<string>> SaveReportHistoryAsync(
        MonthlyReportHistory history,
        byte[] reportData,
        CancellationToken ct = default);
}
