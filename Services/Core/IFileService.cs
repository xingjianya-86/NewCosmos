namespace NewCosmos.Services.Core;

/// <summary>
/// 文件操作服务接口
/// </summary>
public interface IFileService
{
    /// <summary>
    /// 写入文件
    /// </summary>
    Task WriteAllBytesAsync(string filePath, byte[] data, CancellationToken ct = default);

    /// <summary>
    /// 读取文件
    /// </summary>
    Task<byte[]> ReadAllBytesAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// 复制文件
    /// </summary>
    void CopyFile(string sourcePath, string targetPath, bool overwrite = false);

    /// <summary>
    /// 删除文件
    /// </summary>
    void DeleteFile(string filePath);

    /// <summary>
    /// 检查文件是否存在
    /// </summary>
    bool FileExists(string filePath);

    /// <summary>
    /// 创建目录
    /// </summary>
    void CreateDirectory(string directoryPath);
}
