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
    /// 是否支持「导入范围」（年份强制覆盖 / 区间校验 / 年段感知清空）。
    /// false 的类型维持原语义：勾选清空 = 整表 ClearTableAsync，行为不变。
    /// </summary>
    bool SupportsScope { get; }

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

    /// <summary>
    /// 带导入范围的导入：年份覆盖/区间校验由服务内部处理；
    /// clearBeforeImport=true 且 SupportsScope 时，清空按「本次涉及年段」执行（不再整表 TRUNCATE）。
    /// </summary>
    Task<ImportResult> ImportAsync(IEnumerable<string> filePaths, ImportScope? scope, bool clearBeforeImport, IProgress<string>? progress = null, CancellationToken ct = default);
}
