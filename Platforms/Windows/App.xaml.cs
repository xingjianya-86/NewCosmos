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
                // 崩溃记录器自身失败时静默：UnhandledException 处理器内不能再抛（日志系统可能就是坏的）
            }
        };
    }

    protected override MauiApp CreateMauiApp()
    {
        return MauiProgram.CreateMauiApp();
    }
}