using NewCosmos.ViewModels.Mobile;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·历史档案照片补传（高龄 / 低收入 / 临时救助）。
/// 手工勾选人员后连续采集或推送照片打印；照片按身份证归档，补传不改业务表。
/// </summary>
public partial class MobilePhotoBackfillPage : ContentPage
{
    private readonly MobilePhotoBackfillViewModel _viewModel;

    public MobilePhotoBackfillPage(MobilePhotoBackfillViewModel viewModel)
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
