using NewCosmos.ViewModels.Config;

namespace NewCosmos.Pages.Config;

public partial class ConfigWizardPage : ContentPage
{
    public ConfigWizardPage(ConfigWizardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}