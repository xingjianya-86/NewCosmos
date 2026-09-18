using Microsoft.Windows.Storage.Pickers;
using NewCosmos.Services.Core;
using NewCosmos.Services.Platform;

namespace NewCosmos.Platforms.Windows;

/// <summary>
/// 文件选择器（Windows App SDK 1.8 新版 Microsoft.Windows.Storage.Pickers.FileOpenPicker）：
/// 直接接受 WindowId，无需 HWND 互操作；返回结构化结果区分取消/失败。
/// </summary>
public class WindowsFilePickerService : IFilePickerService
{
    private readonly ILoggerService _logger;

    public WindowsFilePickerService(ILoggerService logger)
    {
        _logger = logger;
    }

    public async Task<FilePickResult> PickFileAsync(string title, IReadOnlyList<string> extensions, string? initialDirectory = null)
    {
        try
        {
            var mauiWindow = Application.Current?.Windows?.FirstOrDefault();
            var nativeWindow = mauiWindow?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            if (nativeWindow == null)
            {
                _logger.Warn("[FilePicker] 未获取到应用窗口，无法打开文件选择器");
                return FilePickResult.Failed("未获取到应用窗口，无法打开文件选择器");
            }

            var picker = new FileOpenPicker(nativeWindow.AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                CommitButtonText = "选择文件",
                ViewMode = PickerViewMode.List
            };

            if (extensions != null)
            {
                foreach (var ext in extensions)
                {
                    if (!string.IsNullOrWhiteSpace(ext))
                        picker.FileTypeFilter.Add(ext);
                }
            }

            if (picker.FileTypeFilter.Count == 0)
                picker.FileTypeFilter.Add("*");

            var result = await picker.PickSingleFileAsync();
            if (result == null || string.IsNullOrWhiteSpace(result.Path))
                return FilePickResult.Canceled();

            return FilePickResult.Picked(result.Path, Path.GetFileName(result.Path));
        }
        catch (Exception ex)
        {
            _logger.Error($"[FilePicker] 打开文件选择器失败: {ex.Message}");
            return FilePickResult.Failed($"无法打开文件选择器：{ex.Message}");
        }
    }
}
