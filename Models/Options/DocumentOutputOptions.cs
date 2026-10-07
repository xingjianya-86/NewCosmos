namespace NewCosmos.Models.Options;

/// <summary>
/// 文档输出配置（config/document_output.yaml）。
/// 统一输出根：打印留痕、保存、预览临时文件全部落在 BaseDirectory 之下，
/// 解析失败时回退到历史相对目录"输出"（保持旧行为，不阻断启动）。
/// </summary>
public class DocumentOutputOptions
{
    /// <summary>输出根目录（已展开 {AppData} 占位符并转为绝对路径）</summary>
    public string BaseDirectory { get; set; } = string.Empty;

    /// <summary>预览临时文件子目录（相对 BaseDirectory，默认 temp）</summary>
    public string TempSubdirectory { get; set; } = "temp";

    /// <summary>预览临时文件保留时长（小时，超龄清理）</summary>
    public int TempFileRetentionHours { get; set; } = 24;

    /// <summary>预览临时目录文件数量上限（超过时按最旧优先删除）</summary>
    public int MaxTempFiles { get; set; } = 1000;
}
