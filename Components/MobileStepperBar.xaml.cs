using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace NewCosmos.Components;

/// <summary>
/// 手机端步骤条：按 <see cref="CurrentStep"/> / <see cref="StepCount"/> 生成圆点与连接线。
/// 已过步骤与当前步骤填充主色，未到步骤为浅灰；点击圆点经 <see cref="StepTappedCommand"/> 回传步骤号（字符串，兼容既有 NavigateToStepCommand）。
/// </summary>
public partial class MobileStepperBar : ContentView
{
    private static readonly Color ActiveColor = Color.FromArgb("#1a73e8");
    private static readonly Color InactiveBg = Color.FromArgb("#E5E7EB");
    private static readonly Color InactiveText = Color.FromArgb("#9CA3AF");
    private static readonly Color LineInactive = Color.FromArgb("#E5E7EB");

    public static readonly BindableProperty CurrentStepProperty =
        BindableProperty.Create(nameof(CurrentStep), typeof(int), typeof(MobileStepperBar), 1,
            propertyChanged: (b, _, _) => (b as MobileStepperBar)?.Rebuild());

    public static readonly BindableProperty StepCountProperty =
        BindableProperty.Create(nameof(StepCount), typeof(int), typeof(MobileStepperBar), 5,
            propertyChanged: (b, _, _) => (b as MobileStepperBar)?.Rebuild());

    public static readonly BindableProperty StepTappedCommandProperty =
        BindableProperty.Create(nameof(StepTappedCommand), typeof(ICommand), typeof(MobileStepperBar));

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

    /// <summary>点击步骤圆点时执行，参数为步骤号的字符串形式。</summary>
    public ICommand? StepTappedCommand
    {
        get => (ICommand?)GetValue(StepTappedCommandProperty);
        set => SetValue(StepTappedCommandProperty, value);
    }

    public MobileStepperBar()
    {
        InitializeComponent();
        Rebuild();
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
                    Color = CurrentStep > step - 1 ? ActiveColor : LineInactive,
                    VerticalOptions = LayoutOptions.Center
                };
                Host.Add(line, col, 0);
                col++;
            }

            Host.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Host.Add(CreateCircle(step), col, 0);
            col++;
        }
    }

    private Border CreateCircle(int step)
    {
        bool done = step < CurrentStep;
        bool active = step == CurrentStep;

        var circle = new Border
        {
            WidthRequest = 30,
            HeightRequest = 30,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 15 },
            BackgroundColor = done || active ? ActiveColor : InactiveBg,
            Padding = 0,
            Content = new Label
            {
                Text = step.ToString(),
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = done || active ? Colors.White : InactiveText,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
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
            circle.GestureRecognizers.Add(gesture);
        }

        return circle;
    }
}
