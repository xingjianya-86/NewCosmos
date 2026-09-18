namespace NewCosmos.Services.Platform;

/// <summary>
/// 文件选择结果状态
/// </summary>
public enum FilePickStatus
{
    /// <summary>已选择</summary>
    Picked,

    /// <summary>用户取消</summary>
    Canceled,

    /// <summary>选择器失败（环境/权限/未打包自包含等问题）</summary>
    Failed
}

/// <summary>
/// 文件选择结果：区分“用户取消”与“选择器失败”，避免静默无响应。
/// </summary>
public class FilePickResult
{
    public FilePickStatus Status { get; init; }

    /// <summary>已选择文件的绝对路径</summary>
    public string? Path { get; init; }

    /// <summary>文件名（含扩展名）</summary>
    public string? FileName { get; init; }

    /// <summary>失败原因（仅 Failed 时）</summary>
    public string? Error { get; init; }

    public static FilePickResult Picked(string path, string fileName) =>
        new() { Status = FilePickStatus.Picked, Path = path, FileName = fileName };

    public static FilePickResult Canceled() => new() { Status = FilePickStatus.Canceled };

    public static FilePickResult Failed(string error) => new() { Status = FilePickStatus.Failed, Error = error };
}

/// <summary>
/// 文件选择服务（Windows App SDK 1.8 新版 Microsoft.Windows.Storage.Pickers.FileOpenPicker）
/// </summary>
public interface IFilePickerService
{
    /// <summary>
    /// 弹出文件选择器。立即返回结构化结果（已选/取消/失败），调用方须给出相应反馈。
    /// </summary>
    /// <param name="title">对话框标题</param>
    /// <param name="extensions">扩展名过滤（如 ".docx"）；为空则不过滤（*.*）</param>
    /// <param name="initialDirectory">初始目录（暂仅作占位，当前 SDK 不支持任意起始路径）</param>
    Task<FilePickResult> PickFileAsync(string title, IReadOnlyList<string> extensions, string? initialDirectory = null);
}
