using System.Collections;
using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace NewCosmos.Components;

/// <summary>
/// PC 端步进条：按 <see cref="CurrentStep"/> / <see cref="StepCount"/> 生成「圆点 + 连接线 + 文字标签」横向进度条。
/// 已过步骤与当前步骤填充主色（当前步标签加粗），未到步骤浅灰；点圆点或标签经 <see cref="StepTappedCommand"/>
/// 回传步骤号（字符串，兼容 SwitchStepCommand 的 string? 参数）。
/// 2026-10-30 六步线性向导重设计：替代「3 Tab + 4 分段卡片」双层导航，全页唯一导航层。
/// </summary>
public partial class StepFlowBar : ContentView
{
    private static readonly Color ActiveColor = Color.FromArgb("#1a73e8");
    private static readonly Color ActiveLabelColor = Color.FromArgb("#1a73e8");
    private static readonly Color DoneLabelColor = Color.FromArgb("#1F2937");
    private static readonly Color InactiveBg = Color.FromArgb("#E5E7EB");
    private static readonly Color InactiveLabel = Color.FromArgb("#6B7280");
    private static readonly Color LineInactive = Color.FromArgb("#E5E7EB");

    public static readonly BindableProperty CurrentStepProperty =
        BindableProperty.Create(nameof(CurrentStep), typeof(int), typeof(StepFlowBar), 1,
            propertyChanged: (b, _, _) => (b as StepFlowBar)?.Rebuild());

    public static readonly BindableProperty StepCountProperty =
        BindableProperty.Create(nameof(StepCount), typeof(int), typeof(StepFlowBar), 6,
            propertyChanged: (b, _, _) => (b as StepFlowBar)?.Rebuild());

    /// <summary>步骤标签集合（与 StepCount 同序；缺项时回退为「第 N 步」）。</summary>
    public static readonly BindableProperty LabelsProperty =
        BindableProperty.Create(nameof(Labels), typeof(IEnumerable), typeof(StepFlowBar), null,
            propertyChanged: (b, _, _) => (b as StepFlowBar)?.Rebuild());

    public static readonly BindableProperty StepTappedCommandProperty =
        BindableProperty.Create(nameof(StepTappedCommand), typeof(ICommand), typeof(StepFlowBar),
            propertyChanged: (b, _, _) => (b as StepFlowBar)?.Rebuild());

    public int CurrentStep
    {
        get => (int)GetValue(CurrentStepProperty);
        set => SetValue(CurrentStepProperty, value);
    }

    public int StepCount
    {
        get => (int)GetValue(StepCountProperty);
        set => SetValue(StepCountProperty, value);
    }

    public IEnumerable? Labels
    {
        get => (IEnumerable?)GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    /// <summary>点击步骤时执行，参数为步骤号的字符串形式。</summary>
    public ICommand? StepTappedCommand
    {
        get => (ICommand?)GetValue(StepTappedCommandProperty);
        set => SetValue(StepTappedCommandProperty, value);
    }

    public StepFlowBar()
    {
        InitializeComponent();
        Rebuild();
    }

    private string? LabelAt(int step)
    {
        if (Labels == null) return null;
        var index = 0;
        foreach (var item in Labels)
        {
            if (index++ == step - 1) return item?.ToString();
        }
        return null;
    }

    private void Rebuild()
    {
        if (Host == null) return;

        Host.Children.Clear();
        Host.ColumnDefinitions.Clear();

        int count = Math.Max(1, StepCount);
        int col = 0;

        for (int step = 1; step <= count; step++)
        {
            if (step > 1)
            {
                Host.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                var line = new BoxView
                {
                    HeightRequest = 2,
                    Color = CurrentStep >= step ? ActiveColor : LineInactive,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Fill
                };
                Host.Add(line, col, 0);
                col++;
            }

            Host.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Host.Add(CreateStepItem(step), col, 0);
            col++;
        }
    }

    private Border CreateStepItem(int step)
    {
        bool done = step < CurrentStep;
        bool active = step == CurrentStep;
        bool filled = done || active;

        var circle = new Border
        {
            WidthRequest = 26,
            HeightRequest = 26,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 13 },
            BackgroundColor = filled ? ActiveColor : InactiveBg,
            Padding = 0,
            Margin = new Thickness(0, 0, 6, 0),
            Content = new Label
            {
                Text = step.ToString(),
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = filled ? Colors.White : InactiveLabel,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };

        var label = new Label
        {
            Text = LabelAt(step) ?? $"第 {step} 步",
            FontSize = 13,
            FontAttributes = active ? FontAttributes.Bold : FontAttributes.None,
            TextColor = active ? ActiveLabelColor : filled ? DoneLabelColor : InactiveLabel,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };

        var item = new HorizontalStackLayout { Spacing = 0, Children = { circle, label } };

        var wrap = new Border
        {
            Padding = new Thickness(4, 4),
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            Content = item
        };

        if (StepTappedCommand != null)
        {
            var gesture = new TapGestureRecognizer();
            var parameter = step.ToString();
            gesture.Tapped += (_, _) =>
            {
                if (StepTappedCommand.CanExecute(parameter))
                    StepTappedCommand.Execute(parameter);
            };
            wrap.GestureRecognizers.Add(gesture);
        }

        return wrap;
    }
}
