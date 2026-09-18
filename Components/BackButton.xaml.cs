using System.Windows.Input;

namespace NewCosmos.Components;

/// <summary>
/// 全库统一的页面返回按钮：图标 + 可选文字 + 命令。
/// 视觉与交互集中于此，替代各页手写的 E72B 返回实现（Button/TapGesture/code-behind 多种混用）。
/// </summary>
public partial class BackButton : ContentView
{
    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(BackButton));

    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(BackButton),
            string.Empty, propertyChanged: OnTextChanged);

    public static readonly BindableProperty IconColorProperty =
        BindableProperty.Create(nameof(IconColor), typeof(Color), typeof(BackButton),
            null, propertyChanged: OnIconColorChanged);

    /// <summary>返回命令（通常绑定 ViewModel 的 GoBackCommand）</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>可选文字；为空时仅显示图标</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>图标与文字颜色；缺省用 XAML 内的主题色，深色背景页头可设 White</summary>
    public Color? IconColor
    {
        get => (Color?)GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    public BackButton()
    {
        InitializeComponent();
    }

    private static void OnTextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is BackButton view)
        {
            view.TextLabel.Text = newValue as string;
            view.TextLabel.IsVisible = !string.IsNullOrEmpty(newValue as string);
        }
    }

    private static void OnIconColorChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is BackButton view)
            view.ApplyColor();
    }

    private void ApplyColor()
    {
        if (IconColor == null)
            return;
        IconLabel.TextColor = IconColor;
        TextLabel.TextColor = IconColor;
    }
}
