using System.Text.Json;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Core;

/// <summary>
/// 输出根一次性迁移（旧相对"输出" → yaml 配置根）与预览临时目录清理。
/// 见 IOutputRootMigrationService 契约；所有失败路径不阻断应用启动。
/// </summary>
public class OutputRootMigrationService : BaseService, IOutputRootMigrationService
{
    protected override string ServiceName => "OutputRootMigrationService";

    private const string MarkerFileName = ".output_migration.json";

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

    public async Task MigrateLegacyOutputAsync(CancellationToken ct = default)
    {
        try
        {
            var options = _config.GetDocumentOutputOptions();
            var newRoot = OutputPathHelper.OutputRoot;
            var legacyRoot = OutputPathHelper.LegacyRoot;
            var markerPath = Path.Combine(newRoot, MarkerFileName);

            if (File.Exists(markerPath))
                return;

            Directory.CreateDirectory(newRoot);

            // 无旧目录 / 新旧同路径（yaml 仍配置为"输出"）：直接写标记，无事可做
            if (!Directory.Exists(legacyRoot) ||
                string.Equals(Path.GetFullPath(legacyRoot), Path.GetFullPath(newRoot), StringComparison.OrdinalIgnoreCase))
            {
                WriteMarker(markerPath, newRoot, legacyRoot, 0, 0, 0, "无需迁移");
                return;
            }

            // 1) 复制文件（保留旧目录作回退；目标已存在则跳过，避免覆盖新根内更新的文件）
            var (copied, skipped) = CopyLegacyTree(legacyRoot, newRoot, ct);
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
        catch (Exception ex)
        {
            LogError(ex, "[输出根迁移] 失败（不阻断启动，下次启动重试）");
        }
    }

    /// <summary>复制旧输出树到新根；返回 (新增文件数, 跳过数)</summary>
    private static (int Copied, int Skipped) CopyLegacyTree(string legacyRoot, string newRoot, CancellationToken ct)
    {
        var copied = 0;
        var skipped = 0;
        foreach (var src in Directory.EnumerateFiles(legacyRoot, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(legacyRoot, src);
            var dst = Path.Combine(newRoot, rel);
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

    public void CleanupTempDirectory()
    {
        try
        {
            var options = _config.GetDocumentOutputOptions();
            var tempDir = Path.Combine(OutputPathHelper.OutputRoot, options.TempSubdirectory);
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
