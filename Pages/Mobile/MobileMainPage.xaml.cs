using NewCosmos.Services.Platform;
using NewCosmos.ViewModels.Mobile;

namespace NewCosmos.Pages.Mobile;

public partial class MobileMainPage : ContentPage
{
    private readonly MobileMainViewModel _viewModel;

    public MobileMainPage(MobileMainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }

    /// <summary>右上角「北斗终端」：打开随包内嵌的终端序列页（系统浏览器）。</summary>
    private async void OnTerminalButtonClicked(object? sender, EventArgs e)
    {
        if (sender is Button button)
            button.IsEnabled = false;

        try
        {
            await TerminalPageLauncher.OpenAsync();
        }
        finally
        {
            if (sender is Button target)
                target.IsEnabled = true;
        }
    }
}
