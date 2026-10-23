using Android.Content;
using Android.OS;
using Android.Provider;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Platform;

namespace NewCosmos.Platforms.Android;

/// <summary>
/// Android 相册导出：MediaStore 插入 Pictures/NewCosmos（API 29+ 走 RELATIVE_PATH，免存储权限；
/// 低版本回落到应用外部 Pictures 目录）。
/// </summary>
public sealed class AndroidAlbumExportService : IAlbumExportService
{
    private const string AlbumFolder = "NewCosmos";

    public Task<Result<string>> SaveToAlbumAsync(string fileName, byte[] jpegBytes, CancellationToken ct = default)
    {
        if (jpegBytes == null || jpegBytes.Length == 0)
            return Task.FromResult(Result.Failure<string>(ErrorCodes.VALIDATION_FAILED, "照片内容为空"));

        try
        {
            var context = global::Android.App.Application.Context;
            var resolver = context.ContentResolver;
            if (resolver == null)
                return Task.FromResult(Result.Failure<string>(ErrorCodes.UNKNOWN_ERROR, "无法访问媒体库"));

            var safeName = string.IsNullOrWhiteSpace(fileName) ? $"photo_{DateTime.Now:yyyyMMddHHmmss}.jpg" : fileName;

            var values = new ContentValues();
            values.Put(MediaStore.Images.Media.InterfaceConsts.DisplayName, safeName);
            values.Put(MediaStore.Images.Media.InterfaceConsts.MimeType, "image/jpeg");
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            {
                // 注意：插值字符串里不能直接写 global::（: 会被当成格式说明符）
                var relativePath = global::Android.OS.Environment.DirectoryPictures + "/" + AlbumFolder;
                values.Put(MediaStore.Images.Media.InterfaceConsts.RelativePath, relativePath);
            }

            var uri = resolver.Insert(MediaStore.Images.Media.ExternalContentUri!, values);
            if (uri == null)
                return Task.FromResult(Result.Failure<string>(ErrorCodes.UNKNOWN_ERROR, "无法创建相册条目"));

            using (var stream = resolver.OpenOutputStream(uri))
            {
                if (stream == null)
                    return Task.FromResult(Result.Failure<string>(ErrorCodes.UNKNOWN_ERROR, "无法写入相册"));

                stream.Write(jpegBytes);
                stream.Flush();
            }

            return Task.FromResult(Result.Success(uri.ToString()!));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.FromException<string>(ex));
        }
    }
}
