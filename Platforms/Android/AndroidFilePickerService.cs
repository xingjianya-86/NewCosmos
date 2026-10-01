using NewCosmos.Services.Platform;

namespace NewCosmos.Services.Platform;

/// <summary>
/// Android 文件选择器 - 使用 MAUI FilePicker（跨平台 API）
/// </summary>
public class AndroidFilePickerService : IFilePickerService
{
    public async Task<FilePickResult> PickFileAsync(string title, IReadOnlyList<string> extensions, string? initialDirectory = null)
    {
        try
        {
            var pickOptions = new PickOptions
            {
                PickerTitle = title,
            };

            if (extensions?.Count > 0)
            {
                var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, extensions.Select(e => e.TrimStart('.')).ToArray() }
                });
                pickOptions.FileTypes = fileTypes;
            }

            var result = await FilePicker.PickAsync(pickOptions);
            if (result == null)
                return FilePickResult.Canceled();

            return FilePickResult.Picked(result.FullPath, result.FileName);
        }
        catch (Exception ex)
        {
            return FilePickResult.Failed(ex.Message);
        }
    }
}
