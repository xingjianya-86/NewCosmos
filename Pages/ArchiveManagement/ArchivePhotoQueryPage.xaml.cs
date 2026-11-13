using NewCosmos.Models.Requests;
using NewCosmos.ViewModels.ArchiveManagement;
using NewCosmos.ViewModels.Base;
#if WINDOWS
using Windows.Storage;
#endif

namespace NewCosmos.Pages.ArchiveManagement;

/// <summary>
/// 档案照片查询（桌面端）：检索人员证件照、缩略图列表、勾选多张、预览、导出/另存为。
/// 支持从业务列表（高龄/工作流/月度审核等）带身份证号进入并自动检索。
/// Windows 端另支持把图片文件直接拖进窗口插入（归属人校验与「插入照片」同一链路）。
/// </summary>
public partial class ArchivePhotoQueryPage : ContentPage, IParameterizedPage<ArchivePhotoQueryParameter>
{
    private readonly ArchivePhotoQueryViewModel _viewModel;
    private bool _layoutApplied;

    public ArchivePhotoQueryPage(ArchivePhotoQueryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ApplyResponsiveLayout();
        await _viewModel.OnAppearingAsync();
    }

    /// <summary>
    /// 手机/桌面布局分叉（同一 XAML 双端共用，PageResolver 无手机兄弟页故 Android 也走本页）：
    /// 手机隐藏查询条上的次要按钮（收进「更多」菜单）与右侧预览列（改走全屏预览层），列表占满整行；
    /// 桌面保持双列 + 全部按钮不变。
    /// </summary>
    private void ApplyResponsiveLayout()
    {
        if (_layoutApplied) return;

        var phone = DeviceInfo.Idiom == DeviceIdiom.Phone;

        // 手机：导出格式 / 导出全部 / 另存为 / 清空勾选 / 转 WebP / 打开采集页 / 照片补传 / 提示文案 → 收进「更多」
        pickerExportFormat.IsVisible = !phone;
        btnExportAll.IsVisible = !phone;
        btnExportToFolder.IsVisible = !phone;
        btnClearSelection.IsVisible = !phone;
        btnConvertWebP.IsVisible = !phone;
        btnOpenCapture.IsVisible = !phone;
        btnBackfill.IsVisible = !phone;
        lblHint.IsVisible = !phone;
        btnMore.IsVisible = phone;

        // 手机：单列列表 + 隐藏预览列（预览改由选中行触发的全屏层承载）
        previewBorder.IsVisible = !phone;
        listPreviewGrid.ColumnDefinitions.Clear();
        if (phone)
        {
            listPreviewGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }
        else
        {
            listPreviewGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(6.5, GridUnitType.Star)));
            listPreviewGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(3.5, GridUnitType.Star)));
        }

        _layoutApplied = true;
    }

    Task IParameterizedPage<ArchivePhotoQueryParameter>.SetParameterAsync(ArchivePhotoQueryParameter parameter)
    {
        _viewModel.ApplyParameter(parameter);
        return Task.CompletedTask;
    }

#if WINDOWS
    // ────────── 拖拽插入（仅 Windows）──────────
    // MAUI 无跨平台拖放 API，此处是本页唯一的平台代码：
    // 对页面的 WinUI 视图开 AllowDrop → DragOver 置 Copy → Drop 取图片路径 →
    // 回到 ViewModel.InsertFromDroppedPathsAsync（归属人校验、类别判定、入库与按钮插入完全同源）。

    /// <summary>允许拖入的图片扩展名。</summary>
    private static readonly HashSet<string> DropImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"
    };

    private Microsoft.UI.Xaml.UIElement? _dropTarget;
    private bool _dropHooked;

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler?.PlatformView is Microsoft.UI.Xaml.UIElement element)
            HookDragDrop(element);
        else
            UnhookDragDrop();
    }

    private void HookDragDrop(Microsoft.UI.Xaml.UIElement element)
    {
        if (_dropHooked && ReferenceEquals(_dropTarget, element)) return;

        UnhookDragDrop();

        _dropTarget = element;
        element.AllowDrop = true;
        element.DragOver += OnDragOver;
        element.Drop += OnDrop;
        _dropHooked = true;
    }

    private void UnhookDragDrop()
    {
        if (!_dropHooked || _dropTarget == null) return;

        _dropTarget.DragOver -= OnDragOver;
        _dropTarget.Drop -= OnDrop;
        _dropTarget.AllowDrop = false;
        _dropTarget = null;
        _dropHooked = false;
    }

    private static void OnDragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "插入到查询框指定的档案人员";
        e.DragUIOverride.IsContentVisible = true;
    }

    private async void OnDrop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.OfType<StorageFile>()
                .Select(f => new { f.FileType, f.Path })
                .Where(x => DropImageExtensions.Contains(x.FileType) && !string.IsNullOrEmpty(x.Path))
                .Select(x => x.Path)
                .ToList();

            await _viewModel.InsertFromDroppedPathsAsync(paths);
        }
        catch (Exception ex)
        {
            // 拖放失败不打断页面（按钮插入链路不受影响）
            System.Diagnostics.Debug.WriteLine($"拖拽插入失败: {ex.Message}");
        }
    }
#endif
}
