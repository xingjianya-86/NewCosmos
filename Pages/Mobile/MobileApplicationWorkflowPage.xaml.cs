using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·业务申请工作流（五 Tab：已提交 / 有报告 / 草稿 / 已建档 / 已完结）。
/// 复用桌面 <see cref="ApplicationWorkflowViewModel"/>，仅替换为手机列表布局。
/// </summary>
public partial class MobileApplicationWorkflowPage : ContentPage
{
    private readonly ApplicationWorkflowViewModel _viewModel;

    public MobileApplicationWorkflowPage(ApplicationWorkflowViewModel viewModel)
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
}
