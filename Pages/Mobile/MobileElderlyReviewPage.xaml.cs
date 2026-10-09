using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·高龄津贴复核办理。复用桌面 <see cref="ElderlyReviewViewModel"/>，参数注入同契约。
/// </summary>
public partial class MobileElderlyReviewPage : ContentPage, IParameterizedPage<ElderlyReviewPageParameter>
{
    private readonly ElderlyReviewViewModel _viewModel;

    public MobileElderlyReviewPage(ElderlyReviewViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>补全表单返回本页时由 VM 决定是否重评（_reEvaluateOnAppear 置位才触发）</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }

    /// <summary>导航参数入口：定位人员并评估（仅经 NavigateToPageAsync 单一通道调用）。</summary>
    async Task IParameterizedPage<ElderlyReviewPageParameter>.SetParameterAsync(ElderlyReviewPageParameter parameter)
        => await _viewModel.LoadAsync(parameter);
}
