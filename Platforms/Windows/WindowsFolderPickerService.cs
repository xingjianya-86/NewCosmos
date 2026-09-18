using Microsoft.Maui.Storage;
using Microsoft.Windows.Storage.Pickers;
using NewCosmos.Services.Core;
using NewCosmos.Services.Platform;

namespace NewCosmos.Platforms.Windows;

/// <summary>
/// 文件夹选择器（Windows App SDK 1.8 新版 Microsoft.Windows.Storage.Pickers）：
/// 直接接受 WindowId，无需 HWND 互操作；返回结构化结果区分取消/失败；记录上次导出目录。
/// </summary>
public class WindowsFolderPickerService : IFolderPickerService
{
    private const string LastExportDirectoryKey = "LastExportDirectory";

    private readonly ILoggerService _logger;

    public WindowsFolderPickerService(ILoggerService logger)
    {
        _logger = logger;
    }

    public async Task<FolderPickResult> PickFolderAsync(string title = "选择保存位置", string? initialDirectory = null)
    {
        try
        {
            var mauiWindow = Application.Current?.Windows?.FirstOrDefault();
            var nativeWindow = mauiWindow?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            if (nativeWindow == null)
            {
                _logger.Warn("[FolderPicker] 未获取到应用窗口，无法打开文件夹选择器");
                return FolderPickResult.Failed("未获取到应用窗口，无法打开文件夹选择器");
            }

            var picker = new FolderPicker(nativeWindow.AppWindow.Id)
            {
                CommitButtonText = "选择此文件夹",
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List
            };

            var result = await picker.PickSingleFolderAsync();
            if (result == null || string.IsNullOrWhiteSpace(result.Path))
                return FolderPickResult.Canceled();

            Preferences.Set(LastExportDirectoryKey, result.Path);
            return FolderPickResult.Picked(result.Path);
        }
        catch (Exception ex)
        {
            _logger.Error($"[FolderPicker] 打开文件夹选择器失败: {ex.Message}");
            return FolderPickResult.Failed($"无法打开文件夹选择器：{ex.Message}");
        }
    }

    public string? GetLastDirectory()
    {
        var dir = Preferences.Get(LastExportDirectoryKey, string.Empty);
        return string.IsNullOrWhiteSpace(dir) ? null : dir;
    }
}
