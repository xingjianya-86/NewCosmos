using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Models.Lottery;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Lottery;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.ViewModels.Lottery;

/// <summary>
/// 彩票中奖结果页面 ViewModel
/// 展示用户购彩记录的命中/奖级/奖金，并提供汇总与手动验证
/// </summary>
public partial class LotteryResultViewModel : ViewModelBase
{
    private readonly IUserPurchaseService _userPurchaseService;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerService _logger;
    private const int PageSize = 30;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    public LotteryResultViewModel(
        IServiceProvider serviceProvider,
        ILoggerService logger,
        IUserPurchaseService userPurchaseService,
        IDialogService dialogService)
        : base()
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _userPurchaseService = userPurchaseService;
        _dialogService = dialogService;
    }

    #region Observable Properties

    /// <summary>当前选中的彩种</summary>
    [ObservableProperty]
    private LotteryType _selectedLotteryType = LotteryType.SSQ;

    /// <summary>彩种选项列表</summary>
    public List<LotteryTypeOption> LotteryTypeOptions { get; } = LotteryTypeOption.GetAll();

    /// <summary>当前选中的彩种选项</summary>
    [ObservableProperty]
    private LotteryTypeOption _selectedLotteryTypeOption = new() { Name = "双色球", Value = LotteryType.SSQ };

    partial void OnSelectedLotteryTypeOptionChanged(LotteryTypeOption value)
    {
        if (value != null && value.Value != SelectedLotteryType)
        {
            SelectedLotteryType = value.Value;
            CurrentPage = 1;
            _ = LoadRecordsAsync();
        }
    }

    /// <summary>状态筛选选项列表</summary>
    public List<LotteryResultFilterOption> FilterOptions { get; } = LotteryResultFilterOption.GetAll();

    /// <summary>当前选中的状态筛选</summary>
    [ObservableProperty]
    private LotteryResultFilterOption _selectedFilterOption = new() { Name = "全部", Value = LotteryPurchaseFilter.All };

    partial void OnSelectedFilterOptionChanged(LotteryResultFilterOption value)
    {
        if (value != null)
        {
            CurrentPage = 1;
            _ = LoadRecordsAsync();
        }
    }

    /// <summary>购彩记录列表</summary>
    [ObservableProperty]
    private ObservableCollection<UserPurchaseRecord> _records = new();

    /// <summary>当前筛选下总记录数</summary>
    [ObservableProperty]
    private int _totalCount;

    /// <summary>当前页码</summary>
    [ObservableProperty]
    private int _currentPage;

    /// <summary>总页数</summary>
    [ObservableProperty]
    private int _totalPages;

    /// <summary>是否有上一页</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoPreviousPage))]
    private bool _hasPreviousPage;

    /// <summary>是否有下一页</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNextPage))]
    private bool _hasNextPage;

    public bool CanGoPreviousPage => HasPreviousPage && !IsBusy;
    public bool CanGoNextPage => HasNextPage && !IsBusy;

    /// <summary>汇总统计</summary>
    [ObservableProperty]
    private LotteryPurchaseSummary _summary = new();

    /// <summary>累计注数文本</summary>
    public string TotalCountText => Summary.TotalCount.ToString();

    /// <summary>中奖注数文本</summary>
    public string HitCountText => Summary.HitCount.ToString();

    /// <summary>中奖金额文本</summary>
    public string PrizeTotalText => $"¥{Summary.TotalPrizeAmount:N2}";

    /// <summary>待开奖注数文本</summary>
    public string PendingCountText => Summary.PendingCount.ToString();

    /// <summary>列表为空</summary>
    public bool IsEmpty => Records.Count == 0;

    partial void OnSummaryChanged(LotteryPurchaseSummary value)
    {
        OnPropertyChanged(nameof(TotalCountText));
        OnPropertyChanged(nameof(HitCountText));
        OnPropertyChanged(nameof(PrizeTotalText));
        OnPropertyChanged(nameof(PendingCountText));
    }

    partial void OnRecordsChanged(ObservableCollection<UserPurchaseRecord> value)
    {
        OnPropertyChanged(nameof(IsEmpty));
    }

    #endregion

    #region Commands

    /// <summary>
    /// 加载购彩记录与汇总
    /// </summary>
    [RelayCommand]
    private async Task LoadRecordsAsync()
    {
        await ExecuteAsync(async () =>
        {
            LoadingMessage = "正在加载中奖结果...";
            Records.Clear();

            var summaryResult = await _userPurchaseService.GetPurchaseSummaryAsync(SelectedLotteryType);
            if (summaryResult.IsSuccess)
            {
                Summary = summaryResult.Value;
            }

            var countResult = await _userPurchaseService.GetPurchaseCountAsync(SelectedLotteryType, SelectedFilterOption.Value);
            if (countResult.IsSuccess)
            {
                TotalCount = countResult.Value;
                TotalPages = (int)Math.Ceiling((double)TotalCount / PageSize);
            }

            var listResult = await _userPurchaseService.GetPurchasesAsync(
                SelectedLotteryType, SelectedFilterOption.Value, CurrentPage, PageSize);

            if (listResult.IsSuccess)
            {
                foreach (var item in listResult.Value)
                {
                    Records.Add(item);
                }

                HasPreviousPage = CurrentPage > 1;
                HasNextPage = CurrentPage < TotalPages;
                OnPropertyChanged(nameof(IsEmpty));
            }
            else
            {
                await _dialogService.DisplayAlertAsync("加载失败", listResult.Message ?? "未知错误", "确定");
            }

            return Result.Success();
        });
    }

    /// <summary>
    /// 立即验证（对照最新开奖结果）
    /// </summary>
    [RelayCommand]
    private async Task VerifyNowAsync()
    {
        await ExecuteAsync(async () =>
        {
            LoadingMessage = "正在验证购彩记录...";
            var verifyResult = await _userPurchaseService.VerifyAndRewardAsync(SelectedLotteryType);
            if (!verifyResult.IsSuccess)
            {
                await _dialogService.DisplayAlertAsync("验证失败", verifyResult.Message ?? "未知错误", "确定");
                return Result.Success();
            }

            var summary = verifyResult.Value;
            var detail = $"已验证 {summary.TotalVerified} 注，命中 {summary.HitCount} 注";
            if (summary.TotalPrizeAmount > 0)
                detail += $"\n中奖金额：¥{summary.TotalPrizeAmount:N2}";
            if (summary.BestPrizeLevel > 0)
                detail += $"\n最佳成绩：{summary.BestPrizeDescription}";
            if (summary.PendingCount > 0)
                detail += $"\n待开奖：{summary.PendingCount} 注";
            detail += $"\n\n{summary.EncouragementMessage}";

            await _dialogService.DisplayAlertAsync("验证完成", detail, "确定");
            return Result.Success();
        });

        await LoadRecordsAsync();
    }

    /// <summary>上一页</summary>
    [RelayCommand(CanExecute = nameof(CanGoPreviousPage))]
    private async Task GoPreviousPageAsync()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
            await LoadRecordsAsync();
        }
    }

    /// <summary>下一页</summary>
    [RelayCommand(CanExecute = nameof(CanGoNextPage))]
    private async Task GoNextPageAsync()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
            await LoadRecordsAsync();
        }
    }

    /// <summary>查看单注详情</summary>
    [RelayCommand]
    private async Task ViewRecordDetailAsync(UserPurchaseRecord record)
    {
        if (record == null) return;

        var detail = $"彩种：{record.LotteryType.GetDisplayName()}\n" +
                     $"号码：{record.GetNumbersDisplay()}\n" +
                     $"算法：{record.Algorithm}\n" +
                     $"购彩日期：{record.PurchaseDate:yyyy-MM-dd}\n" +
                     $"状态：{record.StatusText}";

        if (!string.IsNullOrEmpty(record.DrawNumber))
            detail += $"\n开奖期号：{record.DrawNumber}";

        if (!string.IsNullOrEmpty(record.ActualNumbersDisplay))
            detail += $"\n开奖号码：{record.ActualNumbersDisplay}";

        if (record.IsHit)
        {
            detail += $"\n命中：{record.HitSummaryText}";
            detail += $"\n奖级：{record.PrizeLevelText}";
            detail += $"\n奖金：{record.PrizeAmountText}";
        }

        await _dialogService.DisplayAlertAsync("购彩详情", detail, "确定");
    }

    #endregion

    /// <summary>
    /// 页面初始化
    /// </summary>
    public async Task InitializeAsync()
    {
        CurrentPage = 1;
        await LoadRecordsAsync();
    }
}

/// <summary>
/// 中奖结果页状态筛选选项
/// </summary>
public class LotteryResultFilterOption
{
    public string Name { get; set; } = string.Empty;
    public LotteryPurchaseFilter Value { get; set; }

    public override string ToString() => Name;

    public override bool Equals(object? obj) =>
        obj is LotteryResultFilterOption o && o.Value == Value;

    public override int GetHashCode() => Value.GetHashCode();

    public static List<LotteryResultFilterOption> GetAll() => new()
    {
        new LotteryResultFilterOption { Name = "全部", Value = LotteryPurchaseFilter.All },
        new LotteryResultFilterOption { Name = "待开奖", Value = LotteryPurchaseFilter.Pending },
        new LotteryResultFilterOption { Name = "已中奖", Value = LotteryPurchaseFilter.Hit },
        new LotteryResultFilterOption { Name = "未中奖", Value = LotteryPurchaseFilter.Miss }
    };
}
