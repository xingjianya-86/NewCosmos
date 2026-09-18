using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Models.Results;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.Shared;

/// <summary>
/// 可复用的进度显示 ViewModel 组件
/// 用于数据导入、字典同步、地区管理等需要进度展示的场景
/// </summary>
public partial class ProgressViewModel
{
    [ObservableProperty]
    private bool _isProgressVisible;

    [ObservableProperty]
    private string _progressTitle = string.Empty;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ProgressStep> _progressSteps = new();

    [ObservableProperty]
    private bool _canCloseProgress;

    [RelayCommand]
    private void CloseProgress()
    {
        IsProgressVisible = false;
    }

    public void InitializeSteps(List<string> stepNames)
    {
        ProgressSteps.Clear();
        for (var i = 0; i < stepNames.Count; i++)
        {
            ProgressSteps.Add(new ProgressStep
            {
                StepNumber = i + 1,
                StepName = stepNames[i],
                Status = ProgressStepStatus.Pending
            });
        }
        ProgressValue = 0;
        ProgressText = "准备执行...";
        CanCloseProgress = false;
        IsProgressVisible = true;
    }

    public void UpdateProgress(ProgressContext context)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ProgressValue = context.ProgressPercentage / 100.0;

            var stepIndex = context.CurrentStep - 1;
            if (stepIndex >= 0 && stepIndex < ProgressSteps.Count)
            {
                for (var i = 0; i < stepIndex; i++)
                {
                    if (ProgressSteps[i].Status != ProgressStepStatus.Completed)
                        ProgressSteps[i].Status = ProgressStepStatus.Completed;
                }

                ProgressSteps[stepIndex].Status = ProgressStepStatus.InProgress;
                ProgressText = $"正在执行: {ProgressSteps[stepIndex].StepName}";
            }
        });
    }

    public void MarkAllCompleted()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            for (var i = 0; i < ProgressSteps.Count; i++)
            {
                ProgressSteps[i].Status = ProgressStepStatus.Completed;
            }
            ProgressText = "执行完成";
            CanCloseProgress = true;
        });
    }

    public void MarkStepFailed(int stepIndex)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (stepIndex >= 0 && stepIndex < ProgressSteps.Count)
            {
                ProgressSteps[stepIndex].Status = ProgressStepStatus.Failed;
            }
            ProgressText = "执行失败";
            CanCloseProgress = true;
        });
    }
}