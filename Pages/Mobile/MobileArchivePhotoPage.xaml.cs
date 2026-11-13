using NewCosmos.Models.Requests;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Mobile;
#if WINDOWS
using Windows.Storage;
#endif

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·档案照片：按人（身份证）采集证件/核查照片，分类标定后存档，列表查阅，
/// 并可推送照片到 PC 端打印队列（批次三）。
/// 参数经 <see cref="IParameterizedPage{TParam}"/> 注入（身份证/申请ID/姓名）。
/// Windows 端另支持把图片文件直接拖进窗口导入（与「拍照 / 从相册选择」同一入库链路）。
/// </summary>
public partial class MobileArchivePhotoPage : ContentPage, IParameterizedPage<ArchivePhotoParameter>
{
    private readonly MobileArchivePhotoViewModel _viewModel;

    public MobileArchivePhotoPage(MobileArchivePhotoViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }

    Task IParameterizedPage<ArchivePhotoParameter>.SetParameterAsync(ArchivePhotoParameter parameter)
    {
        _viewModel.ApplyParameter(parameter.ApplicantIdCard, parameter.ApplicationId, parameter.ApplicantName);
        _viewModel.ApplySession(parameter.SessionQueue, parameter.SessionIndex);
        _viewModel.ApplyAssignContext(parameter.AssignContext);
        return Task.CompletedTask;
    }

#if WINDOWS
    // ────────── 拖拽导入（仅 Windows）──────────
    // MAUI 无跨平台拖放 API，此处是本页唯一的平台代码：
    // 对页面的 WinUI 视图开 AllowDrop → DragOver 置 Copy → Drop 取图片路径 →
    // ViewModel.InsertFromDroppedPathsAsync（与「拍照 / 从相册选择」完全同一条保存链路）。

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
        e.DragUIOverride.Caption = "导入到当前人员的所选类别";
        e.DragUIOverride.IsContentVisible = true;
    }

    private async void OnDrop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.OfType<StorageFile>()
                .Where(f => DropImageExtensions.Contains(f.FileType) && !string.IsNullOrEmpty(f.Path))
                .Select(f => f.Path)
                .ToList();

            await _viewModel.InsertFromDroppedPathsAsync(paths);
        }
        catch (Exception ex)
        {
            // 拖放失败不打断页面（按钮采集链路不受影响）
            System.Diagnostics.Debug.WriteLine($"拖入图片失败: {ex.Message}");
        }
    }
#endif
}
