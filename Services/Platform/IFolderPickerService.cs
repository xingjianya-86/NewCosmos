namespace NewCosmos.Services.Platform;

/// <summary>
/// 文件夹选择结果状态
/// </summary>
public enum FolderPickStatus
{
    /// <summary>已选择</summary>
    Picked,

    /// <summary>用户取消</summary>
    Canceled,

    /// <summary>选择器失败（环境/权限/未打包自包含等问题）</summary>
    Failed
}

/// <summary>
/// 文件夹选择结果：区分“用户取消”与“选择器失败”，避免静默无响应。
/// </summary>
public class FolderPickResult
{
    public FolderPickStatus Status { get; init; }

    /// <summary>已选择时的绝对路径</summary>
    public string? Path { get; init; }

    /// <summary>失败原因（仅 Failed 时）</summary>
    public string? Error { get; init; }

    public static FolderPickResult Picked(string path) => new() { Status = FolderPickStatus.Picked, Path = path };

    public static FolderPickResult Canceled() => new() { Status = FolderPickStatus.Canceled };

    public static FolderPickResult Failed(string error) => new() { Status = FolderPickStatus.Failed, Error = error };
}

/// <summary>
/// 文件夹选择服务
/// </summary>
public interface IFolderPickerService
{
    /// <summary>
    /// 弹出文件夹选择器。立即返回结构化结果（取消/失败/已选），调用方须给出相应反馈。
    /// </summary>
    /// <param name="title">对话框标题（尽力设置，部分系统版本可能忽略）</param>
    /// <param name="initialDirectory">初始目录（记忆的上次目录）；为空则默认定位到“文档”</param>
    Task<FolderPickResult> PickFolderAsync(string title = "选择保存位置", string? initialDirectory = null);

    /// <summary>
    /// 获取上次导出目录（供导出流程“继续使用上次目录”）。无记录或目录不存在时返回 null。
    /// </summary>
    string? GetLastDirectory();
}
