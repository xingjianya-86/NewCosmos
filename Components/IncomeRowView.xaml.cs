using System.Windows.Input;

namespace NewCosmos.Components;

/// <summary>
/// 收入明细行共享视图（2026-11 表单模板收敛）：成员 Picker / 子类型 Picker / 描述 Entry / 月收入 / 删除按钮
/// 五段布局由 13 个 BindableProperty 驱动，可选行按 Show* 开关塌缩。
/// 原 Step3 五类收入（务工/经营/财产/转移/其他）各 ~40 行逐行重复模板收敛为一处；
/// 差异（选项源、绑定字段名、占位符、删除命令）全部经外层 DataTemplate 传入，实体类型不变。
/// </summary>
public partial class IncomeRowView : ContentView
{
    public static readonly BindableProperty ShowMemberProperty =
        BindableProperty.Create(nameof(ShowMember), typeof(bool), typeof(IncomeRowView), false);

    public static readonly BindableProperty MemberItemsProperty =
        BindableProperty.Create(nameof(MemberItems), typeof(System.Collections.IEnumerable), typeof(IncomeRowView));

    public static readonly BindableProperty SelectedMemberProperty =
        BindableProperty.Create(nameof(SelectedMember), typeof(object), typeof(IncomeRowView), defaultBindingMode: BindingMode.TwoWay);

    public static readonly BindableProperty ShowSubTypeProperty =
        BindableProperty.Create(nameof(ShowSubType), typeof(bool), typeof(IncomeRowView), false);

    public static readonly BindableProperty SubTypeItemsProperty =
        BindableProperty.Create(nameof(SubTypeItems), typeof(System.Collections.IEnumerable), typeof(IncomeRowView));

    public static readonly BindableProperty SubTypeProperty =
        BindableProperty.Create(nameof(SubType), typeof(string), typeof(IncomeRowView), null, BindingMode.TwoWay);

    public static readonly BindableProperty ShowDescriptionProperty =
        BindableProperty.Create(nameof(ShowDescription), typeof(bool), typeof(IncomeRowView), false);

    public static readonly BindableProperty DescriptionProperty =
        BindableProperty.Create(nameof(Description), typeof(string), typeof(IncomeRowView), null, BindingMode.TwoWay);

    public static readonly BindableProperty DescriptionPlaceholderProperty =
        BindableProperty.Create(nameof(DescriptionPlaceholder), typeof(string), typeof(IncomeRowView), string.Empty);

    /// <summary>月收入文本（string 型：Entry↔BP↔实体 decimal 的转换与原直连同走 TypeConverter）</summary>
    public static readonly BindableProperty AmountProperty =
        BindableProperty.Create(nameof(Amount), typeof(string), typeof(IncomeRowView), null, BindingMode.TwoWay);

    public static readonly BindableProperty IsEditableProperty =
        BindableProperty.Create(nameof(IsEditable), typeof(bool), typeof(IncomeRowView), true);

    public static readonly BindableProperty RemoveCommandProperty =
        BindableProperty.Create(nameof(RemoveCommand), typeof(ICommand), typeof(IncomeRowView));

    public static readonly BindableProperty RemoveParameterProperty =
        BindableProperty.Create(nameof(RemoveParameter), typeof(object), typeof(IncomeRowView));

    public bool ShowMember
    {
        get => (bool)GetValue(ShowMemberProperty);
        set => SetValue(ShowMemberProperty, value);
    }

    public System.Collections.IEnumerable? MemberItems
    {
        get => (System.Collections.IEnumerable?)GetValue(MemberItemsProperty);
        set => SetValue(MemberItemsProperty, value);
    }

    public object? SelectedMember
    {
        get => GetValue(SelectedMemberProperty);
        set => SetValue(SelectedMemberProperty, value);
    }

    public bool ShowSubType
    {
        get => (bool)GetValue(ShowSubTypeProperty);
        set => SetValue(ShowSubTypeProperty, value);
    }

    public System.Collections.IEnumerable? SubTypeItems
    {
        get => (System.Collections.IEnumerable?)GetValue(SubTypeItemsProperty);
        set => SetValue(SubTypeItemsProperty, value);
    }

    public string? SubType
    {
        get => (string?)GetValue(SubTypeProperty);
        set => SetValue(SubTypeProperty, value);
    }

    public bool ShowDescription
    {
        get => (bool)GetValue(ShowDescriptionProperty);
        set => SetValue(ShowDescriptionProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string? DescriptionPlaceholder
    {
        get => (string?)GetValue(DescriptionPlaceholderProperty);
        set => SetValue(DescriptionPlaceholderProperty, value);
    }

    public string? Amount
    {
        get => (string?)GetValue(AmountProperty);
        set => SetValue(AmountProperty, value);
    }

    public bool IsEditable
    {
        get => (bool)GetValue(IsEditableProperty);
        set => SetValue(IsEditableProperty, value);
    }

    public ICommand? RemoveCommand
    {
        get => (ICommand?)GetValue(RemoveCommandProperty);
        set => SetValue(RemoveCommandProperty, value);
    }

    public object? RemoveParameter
    {
        get => GetValue(RemoveParameterProperty);
        set => SetValue(RemoveParameterProperty, value);
    }

    public IncomeRowView()
    {
        InitializeComponent();
    }
}
