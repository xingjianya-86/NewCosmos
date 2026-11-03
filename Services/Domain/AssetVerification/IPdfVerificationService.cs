using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.AssetVerification;

public interface IPdfVerificationService
{
    // 已删除（2026-11 死代码清理，全仓 grep 零调用方）：
    // BatchVerifyAsync（纯文件名分段匹配）/ FindCheckByNameAsync / GetVerificationHistoryAsync /
    // AssetCheckItemResult 类（仅被上述死方法引用）。

    Task<Result<bool>> HasLowIncomeIdentityAsync(
        string name, string idCard, CancellationToken ct = default);

    // UpdateCheckStatusAsync 已移至 IAssetVerificationService（唯一实现带终态守卫）；本接口副本 2026-11 删除

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

    /// <summary>同户同 hash 报告匹配（含历史版本）；用于“未变更/恢复历史版本”。</summary>
    Task<Result<ReportHashMatch?>> FindHouseholdReportByHashAsync(
        long checkId, string fileHash, CancellationToken ct = default);

    /// <summary>把指定历史报告恢复为同户当前版本（旧当前版本转历史，不新增行）。</summary>
    Task<Result> SetCurrentReportAsync(
        long reportId, CancellationToken ct = default);

    /// <summary>上传报告后的状态更新：只允许 0/1 → 1/2，终态 2/3 不被覆盖。</summary>
    Task<Result> UpdateCheckStatusFromUploadAsync(
        long checkId, string status, CancellationToken ct = default);

    Task<Result<bool>> ReportExistsByCheckIdAsync(
        long checkId, CancellationToken ct = default);

    /// <summary>按户（head_id_card）取全部报告版本，version 倒序，当前版本 is_current=true。</summary>
    Task<Result<List<PdfVerificationRecord>>> GetReportHistoryByCheckIdAsync(
        long checkId, CancellationToken ct = default);

    /// <summary>按报告行 ID 取 PDF 数据（用于历史版本预览/打印）。</summary>
    Task<Result<byte[]>> GetReportDataByReportIdAsync(
        long reportId, CancellationToken ct = default);

    Task<Result<byte[]>> GetReportDataByCheckIdAsync(
        long checkId, CancellationToken ct = default);
}

// 已删除（2026-11）：AssetCheckItemResult 类——仅被死方法 FindCheckByNameAsync 引用。