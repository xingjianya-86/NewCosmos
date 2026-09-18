using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Helpers;
using NewCosmos.ViewModels.DutyManagement;

namespace NewCosmos.Pages.DutyManagement;

/// <summary>
/// 值班成员管理页面
/// </summary>
public partial class DutyMemberPage : ContentPage
{
    private readonly DutyMemberViewModel _viewModel;

    public DutyMemberPage(DutyMemberViewModel viewModel)
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
