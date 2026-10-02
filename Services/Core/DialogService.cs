namespace NewCosmos.Services.Core;

/// <summary>
/// 对话服务实现
/// 封装 Application.Current.MainPage.DisplayAlert 等操    /// </summary>
public class DialogService : BaseService, IDialogService
{
    protected override string ServiceName => "DialogService";

    public DialogService(ILoggerService logger) : base(logger) { }

    public event Action<string, Components.SnackBarType, int>? SnackBarRequested;

    public async Task DisplayAlertAsync(string title, string message, string cancel)
    {
#if WINDOWS
        await ShowAlertCoreAsync(title, message, null, cancel);
#else
        if (Helpers.WindowNavigator.CurrentPage is Page mainPage)
        {
            await mainPage.DisplayAlertAsync(title, message, cancel);
        }
#endif
    }

    public async Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel)
    {
#if WINDOWS
        return await ShowAlertCoreAsync(title, message, accept, cancel);
#else
        if (Helpers.WindowNavigator.CurrentPage is Page mainPage)
        {
            return await mainPage.DisplayAlertAsync(title, message, accept, cancel);
        }
        return false;
#endif
    }

    public async Task<string?> DisplayActionSheetAsync(string title, string cancel, string destruction, params string[] buttons)
    {
        if (Helpers.WindowNavigator.CurrentPage is Page mainPage)
        {
            return await mainPage.DisplayActionSheetAsync(title, cancel, destruction, buttons);
        }
        return null;
    }

    public Task ShowSnackBarAsync(string message, Components.SnackBarType type = Components.SnackBarType.Success, int duration = 3000)
    {
        SnackBarRequested?.Invoke(message, type, duration);
        return Task.CompletedTask;
    }

    public async Task<string?> DisplayPromptAsync(string title, string message, string accept = "确定", string cancel = "取消", string placeholder = "", int maxLength = -1, Keyboard? keyboard = null)
    {
#if WINDOWS
        var xamlRoot = await ResolveXamlRootAsync();
        if (xamlRoot is null)
        {
            return null;
        }

        var dialog = new Microsoft.Maui.Controls.Platform.PromptDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Message = message,
            PrimaryButtonText = accept,
            SecondaryButtonText = cancel,
            Placeholder = placeholder,
            MaxLength = maxLength >= 0 ? maxLength : 0
        };

        var result = await dialog.ShowAsync();
        return result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary ? dialog.Input : null;
#else
        if (Helpers.WindowNavigator.CurrentPage is Page mainPage)
        {
            return await mainPage.DisplayPromptAsync(title, message, accept, cancel, placeholder, maxLength, keyboard ?? Keyboard.Text);
        }
        return null;
#endif
    }

#if WINDOWS
    private static async Task<bool> ShowAlertCoreAsync(string title, string message, string? accept, string cancel)
    {
        var xamlRoot = await ResolveXamlRootAsync();
        if (xamlRoot is null)
        {
            return accept is not null;
        }

        var dialog = new Microsoft.Maui.Controls.Platform.AlertDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = message,
            VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto
        };

        if (accept is not null)
        {
            dialog.PrimaryButtonText = accept;
            dialog.SecondaryButtonText = cancel;
            dialog.DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary;
        }
        else
        {
            dialog.PrimaryButtonText = cancel;
        }

        var result = await dialog.ShowAsync();
        return accept is not null && result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary;
    }

    private static async Task<Microsoft.UI.Xaml.XamlRoot?> ResolveXamlRootAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var root = ResolveXamlRootCore();
            if (root is not null)
            {
                return root;
            }
            await Task.Delay(100);
        }
        return null;
    }

    private static Microsoft.UI.Xaml.XamlRoot? ResolveXamlRootCore()
    {
        if (Helpers.WindowNavigator.CurrentPage is Page page
            && page.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement pageRoot)
        {
            for (var element = pageRoot; element is not null; element = element.Parent as Microsoft.UI.Xaml.FrameworkElement)
            {
                if (element.XamlRoot is not null)
                {
                    return element.XamlRoot;
                }
            }
        }

        if (Application.Current?.Windows?.FirstOrDefault()?.Handler?.PlatformView is Microsoft.UI.Xaml.Window platformWindow
            && platformWindow.Content is Microsoft.UI.Xaml.FrameworkElement windowContent
            && windowContent.XamlRoot is not null)
        {
            return windowContent.XamlRoot;
        }

        return null;
    }
#endif
}
