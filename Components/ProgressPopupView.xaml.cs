using System.Collections.ObjectModel;
using System.Windows.Input;
using NewCosmos.Models.Results;

namespace NewCosmos.Components;

public partial class ProgressPopupView : ContentView
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(ProgressPopupView), "执行进度");

    public static readonly BindableProperty ProgressProperty =
        BindableProperty.Create(nameof(Progress), typeof(double), typeof(ProgressPopupView), 0.0);

    public static readonly BindableProperty ProgressTextProperty =
        BindableProperty.Create(nameof(ProgressText), typeof(string), typeof(ProgressPopupView), "准备中...");

    public static readonly BindableProperty StepsProperty =
        BindableProperty.Create(nameof(Steps), typeof(ObservableCollection<ProgressStep>), typeof(ProgressPopupView), 
            defaultValueCreator: _ => new ObservableCollection<ProgressStep>());

    public static readonly BindableProperty CanCloseProperty =
        BindableProperty.Create(nameof(CanClose), typeof(bool), typeof(ProgressPopupView), false);

    public static readonly BindableProperty CloseCommandProperty =
        BindableProperty.Create(nameof(CloseCommand), typeof(ICommand), typeof(ProgressPopupView));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public string ProgressText
    {
        get => (string)GetValue(ProgressTextProperty);
        set => SetValue(ProgressTextProperty, value);
    }

    public string PercentageText => $"{Progress * 100:0}%";

    public ObservableCollection<ProgressStep> Steps
    {
        get => (ObservableCollection<ProgressStep>)GetValue(StepsProperty);
        set => SetValue(StepsProperty, value);
    }

    public bool CanClose
    {
        get => (bool)GetValue(CanCloseProperty);
        set => SetValue(CanCloseProperty, value);
    }

    public ICommand CloseCommand
    {
        get => (ICommand)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public ProgressPopupView()
    {
        InitializeComponent();
    }

    public void UpdateProgress(int currentStep, int totalSteps, string stepName)
    {
        var progress = totalSteps > 0 ? (double)currentStep / totalSteps : 0;
        Progress = progress;
        ProgressText = stepName;
        OnPropertyChanged(nameof(PercentageText));
    }

    public void UpdateStepStatus(int stepIndex, ProgressStepStatus status, string description = null)
    {
        if (stepIndex >= 0 && stepIndex < Steps.Count)
        {
            var step = Steps[stepIndex];
            step.Status = status;
            if (!string.IsNullOrEmpty(description))
            {
                step.StepDescription = description;
            }
            OnPropertyChanged(nameof(Steps));
        }
    }

    public void SetSteps(List<ProgressStep> steps)
    {
        Steps.Clear();
        foreach (var step in steps)
        {
            Steps.Add(step);
        }
    }

    public void Reset()
    {
        Progress = 0;
        ProgressText = "准备执行...";
        CanClose = false;
        foreach (var step in Steps)
        {
            step.Status = ProgressStepStatus.Pending;
        }
        OnPropertyChanged(nameof(Steps));
    }
}
