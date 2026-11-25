using NewCosmos.Models.Requests;
using NewCosmos.ViewModels.ArchiveManagement;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.Pages.ArchiveManagement;

public partial class PendingPhotosPage : ContentPage, IParameterizedPage<PendingPhotosParameter>
{
    private readonly PendingPhotosViewModel _viewModel;

    public PendingPhotosPage(PendingPhotosViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }

    public Task SetParameterAsync(PendingPhotosParameter parameter)
    {
        _viewModel.ApplyParameter(parameter);
        return Task.CompletedTask;
    }
}
