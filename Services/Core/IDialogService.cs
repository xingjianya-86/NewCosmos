namespace NewCosmos.Services.Core;

/// <summary>
/// 对话服务接口
/// 封装 MAUI 页面对话框操    /// </summary>
public interface IDialogService
{
    /// <summary>
    /// 显示提示对话    /// </summary>
    Task DisplayAlertAsync(string title, string message, string cancel);

    /// <summary>
    /// 显示确认对话    /// </summary>
    /// <returns>用户是否点击确认按钮</returns>
    Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel);

    /// <summary>
    /// 显示操作表（多选菜单）
    /// </summary>
    /// <returns>用户选择的按钮文本，取消返回 null</returns>
    Task<string?> DisplayActionSheetAsync(string title, string cancel, string destruction, params string[] buttons);

    /// <summary>
    /// 显示 SnackBar 提示
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <param name="type">消息类型</param>
    /// <param name="duration">显示时长（毫秒）</param>
    Task ShowSnackBarAsync(string message, Components.SnackBarType type = Components.SnackBarType.Success, int duration = 3000);

    /// <summary>
    /// 显示输入提示对话框
    /// </summary>
    /// <returns>用户输入的内容，取消返回 null</returns>
    Task<string?> DisplayPromptAsync(string title, string message, string accept = "确定", string cancel = "取消", string placeholder = "", int maxLength = -1, Keyboard? keyboard = null);
}