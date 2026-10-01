using NewCosmos.Models.Options;
using NewCosmos.Services.Core;

namespace NewCosmos.Services.Templates;

/// <summary>
/// 模板引擎共用的文件/分块小工具（原先在 ExcelEngine 与 WordEngine 中各有一份拷贝）。
/// 平台无关：双平台均可使用。
/// </summary>
internal static class TemplateEngineHelpers
{
    /// <summary>在应用临时目录下生成唯一文件路径（自动创建目录）。subDirectory 为 null 时用临时根目录。</summary>
    public static string NewTempFilePath(StorageOptions storageOptions, string extension, string? subDirectory = "PrintTemp", string prefix = "temp_")
    {
        var dir = storageOptions.GetTempPath(subDirectory);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{prefix}{Guid.NewGuid()}{extension}");
    }

    public static void TryDeleteFile(string path, ILoggerService? logger = null, string tag = "TemplateEngine")
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            logger?.Warn($"[{tag}] 删除临时文件失败: {path}, 错误: {ex.Message}");
        }
    }

    public static List<List<T>> ChunkList<T>(List<T> source, int pageSize)
    {
        var chunks = new List<List<T>>();
        for (int i = 0; i < source.Count; i += pageSize)
        {
            chunks.Add(source.Skip(i).Take(pageSize).ToList());
        }
        return chunks;
    }
}
