using NewCosmos.Constants;
using NewCosmos.Models.Results;
using System.Text;
using System.Text.Json;

namespace NewCosmos.Services.Core;

/// <summary>
/// 升级日志服务实现。
/// · <b>本机轨迹</b>：升级前由 <c>AppUpdateCoordinator</c> 写 update_pending.json（更新说明此刻在手，升级后不丢）
///   → 新版首启 <see cref="FlushPendingAsync"/> 写入 update_history.json 并返回该条（"本次更新内容"只提示一次）。
/// · <b>服务端全量</b>：<see cref="IUpdateService.FetchChangelogJsonAsync"/> 拉同目录 changelog.json，
///   与本机历史按版本合并去重（同版本本机优先）。
/// 状态文件与 update_skip.json 同目录（LocalApplicationData\NewCosmos）；服务端日志仅展示，不参与安装决策。
/// </summary>
public class UpdateHistoryService : BaseService, IUpdateHistoryService
{
    protected override string ServiceName => "UpdateHistoryService";

    /// <summary>本机历史最多保留条数（升级日志页只展示近期）。</summary>
    private const int MaxHistory = 100;

    // 与 UpdateService.StateRoot 同一目录（LocalApplicationData\NewCosmos）
    private static readonly string StateRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewCosmos");
    private static readonly string HistoryFile = Path.Combine(StateRoot, "update_history.json");
    private static readonly string PendingFile = Path.Combine(StateRoot, "update_pending.json");

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly IUpdateService _updateService;

    public UpdateHistoryService(IUpdateService updateService, ILoggerService logger) : base(logger)
    {
        _updateService = updateService;
    }

    #region pending（升级前落盘）

    public async Task SavePendingAsync(string version, string notes, DateTime? publishedAt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(version)) return;

        try
        {
            Directory.CreateDirectory(StateRoot);
            var payload = JsonSerializer.Serialize(new
            {
                schema = 1,
                version = version.Trim(),
                notes = notes ?? string.Empty,
                publishedAt = (publishedAt ?? DateTime.Now).ToString("o")
            });
            await File.WriteAllTextAsync(PendingFile, payload, Utf8NoBom, ct);
            LogInfo($"已暂存本版更新说明（待新版首启写入历史）: {version}，{notes?.Length ?? 0} 字");
        }
        catch (Exception ex)
        {
            // 不阻断升级流程：丢一条历史优于装不上
            LogWarn($"暂存更新说明失败（不影响升级）: {ex.Message}");
        }
    }

    public async Task<UpdateHistoryItem?> FlushPendingAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(PendingFile)) return null;

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(PendingFile, ct));
            var root = doc.RootElement;

            var pendingVersion = GetString(root, "version");
            if (string.IsNullOrWhiteSpace(pendingVersion))
            {
                DeletePending();
                return null;
            }

            var current = _updateService.CurrentVersion;
            var cmp = UpdateService.CompareVersions(pendingVersion, current);
            if (cmp > 0) return null;                       // 升级安装仍在进行（旧版重启）：保留 pending
            if (cmp < 0) { DeletePending(); return null; }   // 已越过该版本：清理残留

            var entry = new UpdateHistoryItem(
                pendingVersion,
                ParseTime(GetString(root, "publishedAt")) ?? DateTime.Now,
                GetString(root, "notes"),
                UpdateHistoryItem.SourceLocal);

            var (history, readOk) = await LoadHistoryAsync(ct);
            if (!history.Any(h => string.Equals(h.Version, entry.Version, StringComparison.OrdinalIgnoreCase)))
            {
                // 读失败绝不回写（§7：避免把空数据写回去覆盖真实历史）
                if (!readOk)
                {
                    LogError("本机升级历史读取失败，跳过本次写入以免覆盖既有数据（文件可能损坏）");
                }
                else
                {
                    history.Insert(0, entry);
                    if (history.Count > MaxHistory)
                        history.RemoveRange(MaxHistory, history.Count - MaxHistory);
                    await SaveHistoryAsync(history, ct);
                }
            }

            DeletePending();
            LogInfo($"升级完成，已写入更新日志: {entry.Version}");
            return entry;
        }
        catch (Exception ex)
        {
            LogWarn($"写入升级历史失败（下次启动重试）: {ex.Message}");
            return null;
        }
    }

    private void DeletePending()
    {
        try { if (File.Exists(PendingFile)) File.Delete(PendingFile); }
        catch (Exception ex) { LogWarn($"清理更新暂存文件失败: {ex.Message}"); }
    }

    #endregion

    #region 查询

    public async Task<Result<IReadOnlyList<UpdateHistoryItem>>> GetHistoryAsync(CancellationToken ct = default)
    {
        var (history, readOk) = await LoadHistoryAsync(ct);
        if (!readOk)
            return Result.Failure<IReadOnlyList<UpdateHistoryItem>>(ErrorCodes.UNKNOWN_ERROR, "本机升级历史读取失败（文件可能损坏）");

        return Result.Success<IReadOnlyList<UpdateHistoryItem>>(history);
    }

    public async Task<Result<IReadOnlyList<UpdateHistoryItem>>> FetchMergedAsync(CancellationToken ct = default)
    {
        var (local, _) = await LoadHistoryAsync(ct);

        var json = await _updateService.FetchChangelogJsonAsync(ct);
        if (json.IsFailure)
            return Result.Failure<IReadOnlyList<UpdateHistoryItem>>(json.ErrorCode!, json.Message!);

        List<UpdateHistoryItem> remote;
        try
        {
            remote = ParseEntries(json.Value, UpdateHistoryItem.SourceRemote, "entries", versionName: "version", timeName: "publishedAt");
        }
        catch (Exception ex)
        {
            LogWarn($"解析服务端升级日志失败: {ex.Message}");
            return Result.Failure<IReadOnlyList<UpdateHistoryItem>>(ErrorCodes.VALIDATION_FAILED, "升级日志格式不正确");
        }

        // 同版本本机优先（标记该版本已在本机升级过）
        var byVersion = new Dictionary<string, UpdateHistoryItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in remote) byVersion[item.Version] = item;
        foreach (var item in local) byVersion[item.Version] = item;

        var merged = byVersion.Values
            .OrderByDescending(x => x.Version, Comparer<string>.Create(
                (a, b) => UpdateService.CompareVersions(a, b)))
            .ToList();

        LogInfo($"升级日志合并完成: 服务端 {remote.Count} 条 + 本机 {local.Count} 条 → {merged.Count} 条");
        return Result.Success<IReadOnlyList<UpdateHistoryItem>>(merged);
    }

    #endregion

    #region 读写与解析

    /// <summary>读本机历史；readOk=false 表示文件损坏/解析失败（调用方据此禁止回写）。</summary>
    private async Task<(List<UpdateHistoryItem> Items, bool ReadOk)> LoadHistoryAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(HistoryFile)) return (new List<UpdateHistoryItem>(), true);

            var text = await File.ReadAllTextAsync(HistoryFile, ct);
            if (string.IsNullOrWhiteSpace(text)) return (new List<UpdateHistoryItem>(), true);

            var items = ParseEntries(text, UpdateHistoryItem.SourceLocal, "entries", versionName: "version", timeName: "updatedAt");
            return (items, true);
        }
        catch (Exception ex)
        {
            LogError($"读取本机升级历史失败: {ex.Message}");
            return (new List<UpdateHistoryItem>(), false);
        }
    }

    private async Task SaveHistoryAsync(List<UpdateHistoryItem> items, CancellationToken ct)
    {
        Directory.CreateDirectory(StateRoot);
        var payload = JsonSerializer.Serialize(new
        {
            schema = 1,
            updatedAt = DateTime.Now.ToString("o"),
            entries = items.Select(x => new
            {
                version = x.Version,
                updatedAt = x.UpdatedAt.ToString("o"),
                notes = x.Notes,
                source = x.Source
            }).ToList()
        });
        await File.WriteAllTextAsync(HistoryFile, payload, Utf8NoBom, ct);
    }

    private static List<UpdateHistoryItem> ParseEntries(
        string json, string source, string arrayName, string versionName, string timeName)
    {
        var list = new List<UpdateHistoryItem>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty(arrayName, out var entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var el in entries.EnumerateArray())
        {
            var version = GetString(el, versionName);
            if (string.IsNullOrWhiteSpace(version)) continue;

            list.Add(new UpdateHistoryItem(
                version,
                ParseTime(GetString(el, timeName)) ?? DateTime.MinValue,
                GetString(el, "notes"),
                source));
        }

        return list;
    }

    private static string GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? string.Empty
            : string.Empty;

    private static DateTime? ParseTime(string text)
        => DateTime.TryParse(text, null, global::System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null;

    #endregion
}
