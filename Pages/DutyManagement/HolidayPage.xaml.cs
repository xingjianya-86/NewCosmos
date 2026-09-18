using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Helpers;
using NewCosmos.ViewModels.DutyManagement;

namespace NewCosmos.Pages.DutyManagement;

/// <summary>
/// 节假日维护页面
/// </summary>
public partial class HolidayPage : ContentPage
{
    private readonly HolidayViewModel _viewModel;

    public HolidayPage(HolidayViewModel viewModel)
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
