using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Platform;

namespace NewCosmos.Platforms.Windows;

/// <summary>
/// Windows 相册导出：落到「图片\NewCosmos」（与手机端 MediaStore 目录语义一致，便于桌面端核对）。
/// </summary>
public sealed class WindowsAlbumExportService : IAlbumExportService
{
    private const string AlbumFolder = "NewCosmos";

    public async Task<Result<string>> SaveToAlbumAsync(
        string fileName, byte[] imageBytes, string mimeType, CancellationToken ct = default)
    {
        if (imageBytes == null || imageBytes.Length == 0)
            return Result.Failure<string>(ErrorCodes.VALIDATION_FAILED, "照片内容为空");

        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), AlbumFolder);
            Directory.CreateDirectory(dir);

            var safeName = string.IsNullOrWhiteSpace(fileName) ? $"photo_{DateTime.Now:yyyyMMddHHmmss}.jpg" : fileName;
            var path = Path.Combine(dir, safeName);
            await File.WriteAllBytesAsync(path, imageBytes, ct);
            return Result.Success(path);
        }
        catch (Exception ex)
        {
            return Result.FromException<string>(ex);
        }
    }
}
