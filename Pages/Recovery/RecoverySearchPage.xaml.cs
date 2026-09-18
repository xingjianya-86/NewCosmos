using NewCosmos.Models.Entities;
using NewCosmos.ViewModels.Recovery;

namespace NewCosmos.Pages.Recovery;

public partial class RecoverySearchPage : ContentPage
{
    private readonly RecoverySearchViewModel _viewModel;

    public RecoverySearchPage(RecoverySearchViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // 加载统计数据
        await _viewModel.LoadStatsAsync();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is StoppedPersonDto selectedPerson)
        {
            _viewModel.SelectedPerson = selectedPerson;
        }
    }
}
