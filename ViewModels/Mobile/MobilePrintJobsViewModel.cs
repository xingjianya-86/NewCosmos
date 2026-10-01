using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.Printing;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.Mobile;

/// <summary>
/// 手机端·打印任务查看 ViewModel：查看推送打印队列状态，失败任务可重试，未打印任务可删除。
/// </summary>
public partial class MobilePrintJobsViewModel : PagedSearchViewModelBase
{
    private readonly IPrintQueueService _queueService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    [ObservableProperty]
    private ObservableCollection<PrintJob> _jobs = new();

    [ObservableProperty]
    private int _selectedTabIndex;

    public bool IsTab0Selected => SelectedTabIndex == 0;
    public bool IsTab1Selected => SelectedTabIndex == 1;
    public bool IsTab2Selected => SelectedTabIndex == 2;
    public bool IsTab3Selected => SelectedTabIndex == 3;
    public bool IsTab4Selected => SelectedTabIndex == 4;

    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _failedCount;
    [ObservableProperty] private bool _hasJobs;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTab0Selected));
        OnPropertyChanged(nameof(IsTab1Selected));
        OnPropertyChanged(nameof(IsTab2Selected));
        OnPropertyChanged(nameof(IsTab3Selected));
        OnPropertyChanged(nameof(IsTab4Selected));

        PageIndex = 1;
        SafeFireAndForget(LoadDataAsync);
    }

    public MobilePrintJobsViewModel(
        IPrintQueueService queueService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider)
    {
        _queueService = queueService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        PageSize = 20;
        Title = "打印任务";
    }

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await LoadCountsAsync();
        await LoadDataAsync();
    }

    protected override async Task LoadDataAsync()
    {
        await ExecuteAsync(async () =>
        {
            var status = SelectedTabIndex switch
            {
                1 => PrintJobConstants.StatusPending,
                2 => PrintJobConstants.StatusProcessing,
                3 => PrintJobConstants.StatusCompleted,
                4 => PrintJobConstants.StatusFailed,
                _ => null
            };

            var result = await _queueService.GetPagedAsync(status, PageIndex, PageSize, CancellationToken);
            if (result.IsSuccess && result.Value != null)
            {
                Jobs.Clear();
                foreach (var job in result.Value.Items) Jobs.Add(job);
                TotalCount = result.Value.TotalCount;
                HasJobs = Jobs.Count > 0;
            }
            else
            {
                _logger.Warn($"打印任务加载失败: {result.Message}");
            }
            return Result.Success();
        }, "加载打印任务...");
    }

    private async Task LoadCountsAsync()
    {
        try
        {
            var pending = await _queueService.CountByStatusAsync(PrintJobConstants.StatusPending);
            if (pending.IsSuccess) PendingCount = pending.Value;
            var failed = await _queueService.CountByStatusAsync(PrintJobConstants.StatusFailed);
            if (failed.IsSuccess) FailedCount = failed.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载打印任务计数失败");
        }
    }

    [RelayCommand]
    private void SwitchTab(string? tabIndex)
    {
        if (int.TryParse(tabIndex, out var idx))
            SelectedTabIndex = idx;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadCountsAsync();
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task RetryAsync(PrintJob? job)
    {
        if (job == null) return;
        var result = await _queueService.RetryAsync(job.Id);
        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("重试失败", result.Message ?? "重试失败", "确定");
            return;
        }
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(PrintJob? job)
    {
        if (job == null) return;

        var confirm = await _dialogService.DisplayAlertAsync("确认删除",
            $"确定删除打印任务 {job.JobNo} 吗？", "删除", "取消");
        if (!confirm) return;

        var result = await _queueService.DeleteAsync(job.Id);
        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("删除失败", result.Message ?? "删除失败", "确定");
            return;
        }
        await RefreshAsync();
    }
}
