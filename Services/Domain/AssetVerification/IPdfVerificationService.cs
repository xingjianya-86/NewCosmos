using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.AssetVerification;

public interface IPdfVerificationService
{
    Task<Result<List<PdfVerificationResult>>> BatchVerifyAsync(
        IEnumerable<string> pdfFilePaths,
        IEnumerable<string> expectedNames,
        CancellationToken ct = default);

    Task<Result<AssetCheckItemResult?>> FindCheckByNameAsync(
        string name, CancellationToken ct = default);

    Task<Result<bool>> HasLowIncomeIdentityAsync(
        string name, string idCard, CancellationToken ct = default);

    Task<Result> UpdateCheckStatusAsync(
        long checkId, string status, CancellationToken ct = default);

    /// <summary>
    /// 按身份证整户升级核查状态为"已纳入低收入人口"(2)：该身份证作为户主
    /// （head_id_card）的全部未终态核查任务（0/1）一并置 2。
    /// 用于建档/变更成为保障对象后的联动，失败不阻断调用方主流程。
    /// </summary>
    Task<Result> MarkIncludedByIdCardAsync(
        string idCard, CancellationToken ct = default);

    Task<Result> UploadReportAsync(
        long checkId, string batchId, string applicantName, string applicantIdCard,
        string fileName, byte[] fileData, string fileHash, bool isValid, string notes,
        CancellationToken ct = default);

    Task<Result<bool>> ReportExistsByFileHashAsync(
        string fileHash, CancellationToken ct = default);

    Task<Result<bool>> ReportExistsByCheckIdAsync(
        long checkId, CancellationToken ct = default);

    Task<Result<List<PdfVerificationRecord>>> GetVerificationHistoryAsync(
        int limit = 50, CancellationToken ct = default);

    Task<Result<byte[]>> GetReportDataByCheckIdAsync(
        long checkId, CancellationToken ct = default);
}

public class AssetCheckItemResult
{
    public long Id { get; set; }
    public string ApplicantName { get; set; } = string.Empty;
    public string ApplicantIdCard { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string Status { get; set; } = "0";
    public string BatchId { get; set; } = string.Empty;
    public string HeadIdCard { get; set; } = string.Empty;
}