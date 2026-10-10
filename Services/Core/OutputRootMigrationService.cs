using System.Text.Json;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Core;

/// <summary>
/// 输出根一次性迁移（旧根 → 当前配置根）与预览临时目录清理。
/// 旧根判定：状态文件 {AppData}\output_root_state.json 记录的上次生效根；
/// 无状态文件（2026-10 之前的老版本升级）回退旧默认根 {AppData}/CosmosApp/ArchiveOutput；
/// 另保留历史相对"输出"目录的迁移（2026-10 首次切换）。
/// 见 IOutputRootMigrationService 契约；所有失败路径不阻断应用启动。
/// </summary>
public class OutputRootMigrationService : BaseService, IOutputRootMigrationService
{
    protected override string ServiceName => "OutputRootMigrationService";

    private const string MarkerFileName = ".output_migration.json";

    /// <summary>切换状态文件（记录上次生效的输出根；迁移成功才更新 → 失败下次启动重试）</summary>
    private const string StateFileName = "output_root_state.json";

    private readonly IDatabaseService _db;
    private readonly IConfigService _config;

    public OutputRootMigrationService(
        IDatabaseService db,
        ILoggerService logger,
        IConfigService config)
        : base(logger)
    {
        _db = db;
        _config = config;
    }

    /// <summary>2026-10 之前的旧默认输出根（config/document_output.yaml 原值）</summary>
    private static string LegacyDefaultRoot =>
        Path.Combine(FileSystem.AppDataDirectory, "CosmosApp", "ArchiveOutput");

    private static string StateFilePath => Path.Combine(FileSystem.AppDataDirectory, StateFileName);

    public async Task MigrateLegacyOutputAsync(CancellationToken ct = default)
    {
        try
        {
            // 1) 上次生效根 → 当前根（用户改过输出目录、或默认根从 AppData 换到「文档」）
            if (!await MigrateFromPreviousRootAsync(ct))
                return;

            // 2) 历史相对"输出"目录（2026-10 首次切换的迁移源，仍保留）
            await MigrateRelativeLegacyOutputAsync(ct);

            WriteState(OutputPathHelper.OutputRoot);
        }
        catch (Exception ex)
        {
            LogError(ex, "[输出根迁移] 失败（不阻断启动，下次启动重试）");
        }
    }

    public async Task MigrateToAsync(CancellationToken ct = default)
    {
        try
        {
            // 调用方（系统设置）已调 OutputPathHelper.Configure 切到新根
            if (!await MigrateFromPreviousRootAsync(ct))
                return;

            WriteState(OutputPathHelper.OutputRoot);
        }
        catch (Exception ex)
        {
            LogError(ex, "[输出根迁移] 切换输出根后迁移失败（下次启动重试）");
        }
    }

    /// <summary>
    /// 把状态文件记录的旧根复制到当前根并改写留痕表路径。
    /// 返回 true 表示可继续（成功/无需迁移），false 表示失败（已记日志，不更新状态 → 下次重试）。
    /// </summary>
    private async Task<bool> MigrateFromPreviousRootAsync(CancellationToken ct)
    {
        var newRoot = OutputPathHelper.OutputRoot;
        var oldRoot = ReadState() ?? LegacyDefaultRoot;
        oldRoot = Path.GetFullPath(oldRoot);

        if (PathEquals(oldRoot, newRoot))
        {
            Directory.CreateDirectory(newRoot);
            return true;
        }

        if (!Directory.Exists(oldRoot))
        {
            // 全新安装/旧根已清理：无事可做，直接记录当前根
            Directory.CreateDirectory(newRoot);
            WriteMarker(newRoot, oldRoot, 0, 0, 0, "旧根不存在，无需迁移");
            LogInfo($"[输出根迁移] 旧根不存在，跳过: {oldRoot}");
            return true;
        }

        if (OutputPathHelper.ArePathsNested(oldRoot, newRoot))
        {
            LogError($"[输出根迁移] 新旧输出根互为包含，拒绝迁移: {oldRoot} → {newRoot}");
            return false;
        }

        Directory.CreateDirectory(newRoot);

        // 1) 复制文件（保留旧根作回退；目标已存在则跳过，不覆盖新根内更新的文件）
        var (copied, skipped) = CopyRootTree(oldRoot, newRoot, _config.GetDocumentOutputOptions().TempSubdirectory, ct);
        LogInfo($"[输出根迁移] 旧根文件复制完成: 新增 {copied}, 已存在跳过 {skipped}（{oldRoot} → {newRoot}）");

        // 2) 改写留痕表路径列（事务内；表不存在=跳过，其余失败=不更新状态、下次启动重试）
        var updated = await RewriteTrackedPathsAsync(oldRoot, newRoot, ct);
        if (updated < 0)
        {
            LogError("[输出根迁移] 留痕表路径改写失败，本次不记录新根，下次启动重试");
            return false;
        }

        // 3) 清理旧根预览临时文件（新根 temp 由每次启动的 CleanupTempDirectory 接管）
        CleanupTempCore(oldRoot);

        WriteMarker(newRoot, oldRoot, copied, skipped, updated, "完成");
        LogInfo($"[输出根迁移] 完成: 留痕行改写 {updated}；旧根保留于 {oldRoot}");
        return true;
    }

    /// <summary>历史相对"输出"目录 → 当前根（2026-10 首次切换；同根/缺失直接写标记）</summary>
    private async Task MigrateRelativeLegacyOutputAsync(CancellationToken ct)
    {
        var newRoot = OutputPathHelper.OutputRoot;
        var legacyRoot = OutputPathHelper.LegacyRoot;
        var markerPath = Path.Combine(newRoot, MarkerFileName);

        if (File.Exists(markerPath))
            return;

        Directory.CreateDirectory(newRoot);

        // 无旧目录 / 新旧同路径（yaml 仍配置为"输出"）：直接写标记，无事可做
        if (!Directory.Exists(legacyRoot) || PathEquals(legacyRoot, newRoot))
        {
            WriteMarker(markerPath, newRoot, legacyRoot, 0, 0, 0, "无需迁移");
            return;
        }

        if (OutputPathHelper.ArePathsNested(legacyRoot, newRoot))
        {
            LogError($"[输出根迁移] 历史\"输出\"目录与当前根互为包含，拒绝迁移: {legacyRoot} → {newRoot}");
            return;
        }

        // 1) 复制文件（保留旧目录作回退；目标已存在则跳过，避免覆盖新根内更新的文件）
        var (copied, skipped) = CopyRootTree(legacyRoot, newRoot, _config.GetDocumentOutputOptions().TempSubdirectory, ct);
        LogInfo($"[输出根迁移] 旧目录文件复制完成: 新增 {copied}, 已存在跳过 {skipped}");

        // 2) 改写留痕表路径列（事务内；表不存在=跳过，其余失败=不写标记、下次启动重试）
        var updated = await RewriteTrackedPathsAsync(legacyRoot, newRoot, ct);
        if (updated < 0)
        {
            LogError("[输出根迁移] 留痕表路径改写失败，本次不写标记，下次启动重试");
            return;
        }

        WriteMarker(markerPath, newRoot, legacyRoot, copied, skipped, updated, "完成");
        LogInfo($"[输出根迁移] 完成: 留痕行改写 {updated}；旧目录保留于 {legacyRoot}");
    }

    /// <summary>复制输出根目录树；跳过 temp 子树（预览临时文件不迁移）与迁移标记文件。返回 (新增文件数, 跳过数)</summary>
    private static (int Copied, int Skipped) CopyRootTree(string sourceRoot, string targetRoot,
        string tempSubdirectory, CancellationToken ct)
    {
        var copied = 0;
        var skipped = 0;
        var tempPrefix = string.IsNullOrWhiteSpace(tempSubdirectory)
            ? null
            : tempSubdirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
              + Path.DirectorySeparatorChar;

        foreach (var src in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(sourceRoot, src);

            // 预览临时文件与迁移标记不迁移
            if (Path.GetFileName(src).Equals(MarkerFileName, StringComparison.OrdinalIgnoreCase) ||
                (tempPrefix != null && rel.StartsWith(tempPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                skipped++;
                continue;
            }

            var dst = Path.Combine(targetRoot, rel);
            if (File.Exists(dst))
            {
                skipped++;
                continue;
            }
            var dstDir = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(dstDir))
                Directory.CreateDirectory(dstDir);
            File.Copy(src, dst);
            copied++;
        }
        return (copied, skipped);
    }

    /// <summary>
    /// 改写三张留痕表的路径列（前缀替换，strpos 前缀匹配避免 LIKE 通配符 _ % 误伤）。
    /// 返回改写行数；表不存在返回 0（新库）；其余错误返回 -1（不写标记）。
    /// </summary>
    private async Task<int> RewriteTrackedPathsAsync(string legacyRoot, string newRoot, CancellationToken ct)
    {
        var total = 0;

        var printRecords = await ExecutePrefixReplaceAsync(@"
            UPDATE nc_biz_print_records SET
                file_path = CASE WHEN strpos(file_path, $2) = 1 THEN $1 || substr(file_path, length($2) + 1) ELSE file_path END,
                pdf_path  = CASE WHEN pdf_path IS NOT NULL AND strpos(pdf_path, $2) = 1 THEN $1 || substr(pdf_path, length($2) + 1) ELSE pdf_path END
            WHERE strpos(file_path, $2) = 1
               OR (pdf_path IS NOT NULL AND strpos(pdf_path, $2) = 1)",
            legacyRoot, newRoot, ct);
        if (printRecords < 0) return -1;
        total += printRecords;

        var archiveFiles = await ExecutePrefixReplaceAsync(@"
            UPDATE nc_biz_archive_files SET
                output_path = CASE WHEN strpos(output_path, $2) = 1 THEN $1 || substr(output_path, length($2) + 1) ELSE output_path END
            WHERE strpos(output_path, $2) = 1",
            legacyRoot, newRoot, ct);
        if (archiveFiles < 0) return -1;
        total += archiveFiles;

        var assetReports = await ExecutePrefixReplaceAsync(@"
            UPDATE nc_biz_asset_report_history SET
                file_path = CASE WHEN strpos(file_path, $2) = 1 THEN $1 || substr(file_path, length($2) + 1) ELSE file_path END
            WHERE strpos(file_path, $2) = 1",
            legacyRoot, newRoot, ct);
        if (assetReports < 0) return -1;
        total += assetReports;

        return total;
    }

    /// <summary>执行前缀替换 UPDATE；表不存在 → 0；其他错误 → -1</summary>
    private async Task<int> ExecutePrefixReplaceAsync(string sql, string legacyRoot, string newRoot, CancellationToken ct)
    {
        var result = await _db.ExecuteNonQueryAsync(sql, ct, newRoot, legacyRoot);
        if (result.IsSuccess)
        {
            LogInfo($"[输出根迁移] 路径改写 {result.Value} 行");
            return result.Value;
        }

        var msg = result.Message ?? string.Empty;
        if (msg.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("42P01", StringComparison.Ordinal))
        {
            LogWarn($"[输出根迁移] 表不存在，跳过: {msg.Split('\n')[0]}");
            return 0;
        }

        LogError($"[输出根迁移] 路径改写失败: {msg}");
        return -1;
    }

    private static void WriteMarker(string markerPath, string newRoot, string legacyRoot,
        int copied, int skipped, int updated, string note)
    {
        var marker = new
        {
            MigratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            LegacyRoot = legacyRoot,
            NewRoot = newRoot,
            CopiedFiles = copied,
            SkippedFiles = skipped,
            UpdatedRows = updated,
            Note = note,
        };
        File.WriteAllText(markerPath, JsonSerializer.Serialize(marker,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteMarker(string newRoot, string oldRoot, int copied, int skipped, int updated, string note) =>
        WriteMarker(Path.Combine(newRoot, MarkerFileName), newRoot, oldRoot, copied, skipped, updated, note);

    /// <summary>读取上次生效的输出根；无状态文件返回 null</summary>
    private static string? ReadState()
    {
        try
        {
            if (!File.Exists(StateFilePath))
                return null;
            var state = JsonSerializer.Deserialize<OutputRootState>(File.ReadAllText(StateFilePath));
            var root = state?.LastAppliedRoot?.Trim();
            return string.IsNullOrWhiteSpace(root) ? null : root;
        }
        catch
        {
            // 状态文件损坏：回退旧默认根，下次成功迁移时重写
            return null;
        }
    }

    /// <summary>记录本次生效的输出根（仅迁移成功后调用）</summary>
    private static void WriteState(string root)
    {
        try
        {
            var dir = Path.GetDirectoryName(StateFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(StateFilePath, JsonSerializer.Serialize(new OutputRootState
            {
                LastAppliedRoot = root,
                UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 状态记录失败不阻断（下次启动按旧根重算，迁移幂等）
        }
    }

    private sealed class OutputRootState
    {
        public string? LastAppliedRoot { get; set; }
        public string? UpdatedAt { get; set; }
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    public void CleanupTempDirectory() => CleanupTempCore(OutputPathHelper.OutputRoot);

    /// <summary>清理指定输出根下预览临时目录（超龄文件删除 + 总数上限），失败仅记日志</summary>
    private void CleanupTempCore(string root)
    {
        try
        {
            var options = _config.GetDocumentOutputOptions();
            var tempDir = Path.Combine(root, options.TempSubdirectory);
            if (!Directory.Exists(tempDir))
                return;

            var files = new DirectoryInfo(tempDir).GetFiles("*.pdf")
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();
            if (files.Count == 0)
                return;

            var cutoff = DateTime.UtcNow.AddHours(-options.TempFileRetentionHours);
            var deleted = 0;
            foreach (var file in files)
            {
                if (file.LastWriteTimeUtc >= cutoff)
                    continue;
                try
                {
                    file.Delete();
                    deleted++;
                }
                catch (Exception ex)
                {
                    LogDebug($"[预览清理] 删除超龄临时文件失败（可能正被占用）: {file.Name} - {ex.Message}");
                }
            }

            // 数量上限：超限时按最旧优先删除（与上面删除后重新取数）
            var remaining = new DirectoryInfo(tempDir).GetFiles("*.pdf")
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();
            if (remaining.Count > options.MaxTempFiles)
            {
                foreach (var file in remaining.Take(remaining.Count - options.MaxTempFiles))
                {
                    try
                    {
                        file.Delete();
                        deleted++;
                    }
                    catch { /* 占用中的文件跳过 */ }
                }
            }

            if (deleted > 0)
                LogInfo($"[预览清理] 清理预览临时文件 {deleted} 个（{tempDir}）");
        }
        catch (Exception ex)
        {
            LogWarn($"[预览清理] 清理失败（不阻断启动）: {ex.Message}");
        }
    }
}
