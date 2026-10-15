using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Pages.SocialAssistance;

public partial class NearRelativeEntryPage : ContentPage, IParameterizedPage<NearRelativeEntryParameter>
{
    private readonly NearRelativeEntryViewModel _viewModel;

    public NearRelativeEntryPage(NearRelativeEntryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>导航参数（列表页编辑=StaffId；工作流卡片=户主上下文）由基类导航在 OnAppearing 前注入。</summary>
    Task IParameterizedPage<NearRelativeEntryParameter>.SetParameterAsync(NearRelativeEntryParameter parameter)
        => _viewModel.SetNavigationParameter(parameter);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}
