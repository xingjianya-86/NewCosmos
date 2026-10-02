using Microsoft.UI.Xaml;

namespace NewCosmos.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
        UnhandledException += (s, e) =>
        {
            try
            {
                global::NewCosmos.App.LogCrashException(e.Exception, "WinUI.UnhandledException");
            }
            catch
            {
            }
        };
    }

    protected override MauiApp CreateMauiApp()
    {
        return MauiProgram.CreateMauiApp();
    }
}