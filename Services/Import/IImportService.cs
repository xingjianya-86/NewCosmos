using NewCosmos.Models.Results;

namespace NewCosmos.Services.Import;

/// <summary>
/// 导入服务接口（支持预览和清空    /// </summary>
public interface IImportService
{
    /// <summary>
    /// 导入类型名称
    /// </summary>
    string ImportTypeName { get; }

    /// <summary>
    /// 文件名匹配模式（精准匹配    /// </summary>
    string[] FileNamePatterns { get; }

    /// <summary>
    /// 预览导入数据（返回前N行）
    /// </summary>
    Task<ImportPreviewResult> PreviewAsync(string filePath, int previewRows = 10, CancellationToken ct = default);

    /// <summary>
    /// 执行导入
    /// </summary>
    Task<ImportResult> ImportAsync(IEnumerable<string> filePaths, IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// 清空表数    /// </summary>
    Task<Result> ClearTableAsync(CancellationToken ct = default);

    /// <summary>
    /// 导入（支持清空表选项    /// </summary>
    Task<ImportResult> ImportAsync(IEnumerable<string> filePaths,  bool clearBeforeImport, IProgress<string>? progress = null, CancellationToken ct = default);
}
