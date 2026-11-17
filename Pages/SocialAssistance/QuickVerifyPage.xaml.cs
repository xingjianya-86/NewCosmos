using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Pages.SocialAssistance;

/// <summary>
/// 快速核验 · 模拟测算页（低收入模块首页功能卡入口）。
/// Tab①数据核验：按身份证只读聚合系统内台账；Tab②模拟测算：档案收入 + 输入人口/子女数 → 权威服务试算。
/// </summary>
public partial class QuickVerifyPage : ContentPage
{
    private readonly QuickVerifyViewModel _viewModel;

    public QuickVerifyPage(QuickVerifyViewModel viewModel)
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
