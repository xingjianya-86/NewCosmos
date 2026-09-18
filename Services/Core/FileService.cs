namespace NewCosmos.Services.Core;

/// <summary>
/// 文件操作服务实现
/// </summary>
public class FileService : BaseService, IFileService
{
    protected override string ServiceName => "FileService";

    public FileService(ILoggerService logger) : base(logger)
    {
    }

    public async Task WriteAllBytesAsync(string filePath, byte[] data, CancellationToken ct = default)
    {
        LogInfo($"写入文件: {Path.GetFileName(filePath)}");
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllBytesAsync(filePath, data, ct);
            LogInfo($"文件写入成功: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            LogError($"文件写入失败: {ex.Message}");
            throw;
        }
    }

    public async Task<byte[]> ReadAllBytesAsync(string filePath, CancellationToken ct = default)
    {
        LogInfo($"读取文件: {Path.GetFileName(filePath)}");
        try
        {
            var data = await File.ReadAllBytesAsync(filePath, ct);
            LogInfo($"文件读取成功: {Path.GetFileName(filePath)}, 大小: {data.Length} 字节");
            return data;
        }
        catch (Exception ex)
        {
            LogError($"文件读取失败: {ex.Message}");
            throw;
        }
    }

    public void CopyFile(string sourcePath, string targetPath, bool overwrite = false)
    {
        LogInfo($"复制文件: {Path.GetFileName(sourcePath)} -> {Path.GetFileName(targetPath)}");
        try
        {
            File.Copy(sourcePath, targetPath, overwrite);
            LogInfo("文件复制成功");
        }
        catch (Exception ex)
        {
            LogError($"文件复制失败: {ex.Message}");
            throw;
        }
    }

    public void DeleteFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !FileExists(filePath))
            return;

        LogInfo($"删除文件: {Path.GetFileName(filePath)}");
        try
        {
            File.Delete(filePath);
            LogInfo("文件删除成功");
        }
        catch (Exception ex)
        {
            LogError($"文件删除失败: {ex.Message}");
        }
    }

    public bool FileExists(string filePath)
    {
        return !string.IsNullOrEmpty(filePath) && File.Exists(filePath);
    }

    public void CreateDirectory(string directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath))
            return;

        Directory.CreateDirectory(directoryPath);
    }
}
