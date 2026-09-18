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
        if (Helpers.WindowNavigator.CurrentPage is Page mainPage)
        {
            await mainPage.DisplayAlertAsync(title, message, cancel);
        }
    }

    public async Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel)
    {
        if (Helpers.WindowNavigator.CurrentPage is Page mainPage)
        {
            return await mainPage.DisplayAlertAsync(title, message, accept, cancel);
        }
        return false;
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
        if (Helpers.WindowNavigator.CurrentPage is Page mainPage)
        {
            return await mainPage.DisplayPromptAsync(title, message, accept, cancel, placeholder, maxLength, keyboard ?? Keyboard.Text);
        }
        return null;
    }
}