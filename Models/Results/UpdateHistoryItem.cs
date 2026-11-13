namespace NewCosmos.Models.Results;

/// <summary>
/// 一条升级日志（应用内「更新日志」页展示）。
/// <see cref="Source"/> 区分来源：本机升级轨迹 / 服务端全量；Notes 为发布时写入
/// update.json、changelog.json 的说明文本（多行）。
/// </summary>
public sealed record UpdateHistoryItem(
    string Version,
    DateTime UpdatedAt,
    string Notes,
    string Source)
{
    /// <summary>本机升级轨迹（升级前落 pending → 新版首启写入）。</summary>
    public const string SourceLocal = "local";

    /// <summary>服务端全量（changelog.json，跨设备可看）。</summary>
    public const string SourceRemote = "remote";

    /// <summary>来源中文名（列表徽章）。</summary>
    public string SourceName => Source == SourceRemote ? "在线" : "本机";

    /// <summary>时间展示文本（无效时间回退空串）。</summary>
    public string TimeText => UpdatedAt == DateTime.MinValue
        ? string.Empty
        : UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    /// <summary>说明为空时的占位。</summary>
    public string NotesText => string.IsNullOrWhiteSpace(Notes) ? "（该版本未附带更新说明）" : Notes;
}
