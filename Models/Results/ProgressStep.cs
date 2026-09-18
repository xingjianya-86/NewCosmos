using CommunityToolkit.Mvvm.ComponentModel;

namespace NewCosmos.Models.Results;

public enum ProgressStepStatus
{
    Pending,
    InProgress,
    Completed,
    Failed
}

public partial class ProgressStep : ObservableObject
{
    [ObservableProperty]
    private int _stepNumber;

    [ObservableProperty]
    private string _stepName = string.Empty;

    [ObservableProperty]
    private string _stepDescription = string.Empty;

    [ObservableProperty]
    private ProgressStepStatus _status;

    public string StatusIcon => Status switch
    {
        ProgressStepStatus.Pending => "",
        ProgressStepStatus.InProgress => "",
        ProgressStepStatus.Completed => "",
        ProgressStepStatus.Failed => "",
        _ => ""
    };

    public string StatusColor => Status switch
    {
        ProgressStepStatus.Pending => "#9CA3AF",
        ProgressStepStatus.InProgress => "#3B82F6",
        ProgressStepStatus.Completed => "#10B981",
        ProgressStepStatus.Failed => "#EF4444",
        _ => "#9CA3AF"
    };

    public bool IsBold => Status == ProgressStepStatus.InProgress;

    partial void OnStatusChanged(ProgressStepStatus value)
    {
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(IsBold));
    }
}

public record ProgressContext
{
    public required int CurrentStep { get; init; }
    public required int TotalSteps { get; init; }
    public required string CurrentStepName { get; init; }
    public string DetailText { get; init; } = string.Empty;
    public double ProgressPercentage => TotalSteps > 0 ? (double)CurrentStep / TotalSteps * 100 : 0;
}