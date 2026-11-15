using NewCosmos.Models.Results;

namespace NewCosmos.Services.Core;

/// <summary>
/// 升级日志服务：随包内置全量（安装包内嵌 CHANGELOG.md，离线可用）+ 本机升级轨迹
/// （升级前落 pending、新版首启 flush 成历史）+ 服务端全量拉取合并。
/// 条目模型见 <see cref="Models.Results.UpdateHistoryItem"/>；状态文件与 update_skip.json 同目录。
/// 服务端 changelog.json 仅作展示，不参与安装决策（安装仍走签名清单 update.json）。
/// </summary>
public interface IUpdateHistoryService
{
    /// <summary>下载完成、启动安装器**前**调用：把本版更新说明落盘（新版首启据此写入历史）。</summary>
    Task SavePendingAsync(string version, string notes, DateTime? publishedAt, CancellationToken ct = default);

    /// <summary>
    /// 新版首启：pending 版本 == 当前版本 → 追加本机历史并**返回该条（只返回一次，供"本次更新内容"提示）**；
    /// pending 比当前版本新（升级进行中）→ 保留；更旧或异常 → 清理；无 pending → 返回 null。
    /// </summary>
    Task<UpdateHistoryItem?> FlushPendingAsync(CancellationToken ct = default);

    /// <summary>本机升级历史 ∪ 随包内置全量日志（新 → 旧，本机最多保留 100 条）；
    /// 读取失败返回 Failure（不吞，避免被当成空数据回写）。</summary>
    Task<Result<IReadOnlyList<UpdateHistoryItem>>> GetHistoryAsync(CancellationToken ct = default);

    /// <summary>
    /// 拉服务端全量 changelog.json，与随包内置、本机历史分层合并去重（随包为底 → 服务端 → 本机优先），
    /// 按版本新 → 旧返回；拉取失败返回 Failure（显式失败，不吞——页面已预载随包+本机完整日志）。
    /// </summary>
    Task<Result<IReadOnlyList<UpdateHistoryItem>>> FetchMergedAsync(CancellationToken ct = default);
}
