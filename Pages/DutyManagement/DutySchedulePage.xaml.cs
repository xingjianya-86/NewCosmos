using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Helpers;
using NewCosmos.ViewModels.DutyManagement;

namespace NewCosmos.Pages.DutyManagement;

/// <summary>
/// 值班表主页面
/// </summary>
public partial class DutySchedulePage : ContentPage
{
    private readonly DutyScheduleViewModel _viewModel;

    public DutySchedulePage(DutyScheduleViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializePermissionsAsync(App.CurrentUserId ?? 1);
        await _viewModel.OnAppearingAsync();
    }
}
