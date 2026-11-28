using NewCosmos.Models.Results;

namespace NewCosmos.Services.Import;

/// <summary>
/// 高龄年度认证 Excel+ZIP 批量导入：预览（只读）→ 应用（落库）。
/// Excel 名单中的“图片文件名”是唯一匹配依据；ZIP 内一人可多图，按文件名一对一匹配。
/// </summary>
public interface IElderlyCertificationImportService
{
    /// <summary>
    /// 解析 Excel 名单与 ZIP 图片，返回预览统计和异常清单。不写数据库。
    /// </summary>
    Task<Result<ElderlyCertImportPreview>> PreviewAsync(
        long campaignId,
        string excelPath,
        string zipPath,
        CancellationToken ct = default);

    /// <summary>
    /// 按预览结果应用导入：写认证结果、凭证、导入批次与行留痕。
    /// 应用前校验 Excel/ZIP 哈希，防止预览后文件被替换。
    /// </summary>
    Task<Result<ElderlyCertImportApplyResult>> ApplyAsync(
        ElderlyCertImportPreview preview,
        ElderlyCertImportApplyOptions options,
        CancellationToken ct = default);
}
