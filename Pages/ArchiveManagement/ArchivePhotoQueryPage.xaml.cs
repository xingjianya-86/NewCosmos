using NewCosmos.ViewModels.ArchiveManagement;

namespace NewCosmos.Pages.ArchiveManagement;

/// <summary>
/// 档案照片查询（桌面端）：检索人员证件照、预览、批量导出到输出根。
/// </summary>
public partial class ArchivePhotoQueryPage : ContentPage
{
    private readonly ArchivePhotoQueryViewModel _viewModel;

    public ArchivePhotoQueryPage(ArchivePhotoQueryViewModel viewModel)
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
}
