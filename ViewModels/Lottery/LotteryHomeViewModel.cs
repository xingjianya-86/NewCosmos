using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Models.Lottery;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Navigation;
using NewCosmos.Services.Lottery;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.ViewModels.Lottery;

/// <summary>
/// 彩票模块首页 ViewModel
/// </summary>
public partial class LotteryHomeViewModel : ViewModelBase
{
    private readonly ILotteryDataService _lotteryDataService;
    private readonly ILotteryPredictService _predictService;
    private readonly IUserPurchaseService _userPurchaseService;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerService _logger;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    public LotteryHomeViewModel(
        IServiceProvider serviceProvider,
        ILoggerService logger,
        ILotteryDataService lotteryDataService,
        ILotteryPredictService predictService,
        IUserPurchaseService userPurchaseService,
        IDialogService dialogService)
        : base()
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _lotteryDataService = lotteryDataService;
        _predictService = predictService;
        _userPurchaseService = userPurchaseService;
        _dialogService = dialogService;
    }

    #region Observable Properties

    /// <summary>
    /// 当前选中的彩种
    /// </summary>
    [ObservableProperty]
    private LotteryType _selectedLotteryType = LotteryType.SSQ;

    /// <summary>
    /// 当前彩种显示名称
    /// </summary>
    [ObservableProperty]
    private string _selectedLotteryTypeName = "双色球";

    /// <summary>
    /// 最新开奖标题（动态显示彩种名）
    /// </summary>
    public string LatestDrawTitle => $"最新开奖 — {SelectedLotteryTypeName}";

    /// <summary>
    /// 最新期号
    /// </summary>
    [ObservableProperty]
    private string _latestDrawNumber = "--";

    /// <summary>
    /// 最新开奖日期
    /// </summary>
    [ObservableProperty]
    private string _latestDrawDate = "--";

    /// <summary>
    /// 最新开奖红球
    /// </summary>
    [ObservableProperty]
    private string _latestRedNumbers = "--";

    /// <summary>
    /// 最新开奖蓝球
    /// </summary>
    [ObservableProperty]
    private string _latestBlueNumbers = "--";

    /// <summary>
    /// 历史数据总期数
    /// </summary>
    [ObservableProperty]
    private int _totalDrawCount;

    /// <summary>
    /// 是否有历史数据
    /// </summary>
    [ObservableProperty]
    private bool _hasData;

    /// <summary>
    /// 彩种选项列表
    /// </summary>
    public List<LotteryTypeOption> LotteryTypeOptions { get; } = LotteryTypeOption.GetAll();

    /// <summary>
    /// 当前选中的彩种选项
    /// </summary>
    [ObservableProperty]
    private LotteryTypeOption _selectedLotteryTypeOption = new() { Name = "双色球", Value = LotteryType.SSQ };

    partial void OnSelectedLotteryTypeOptionChanged(LotteryTypeOption value)
    {
        if (value != null)
        {
            SelectedLotteryType = value.Value;
            SelectedLotteryTypeName = value.Name;
            OnPropertyChanged(nameof(LatestDrawTitle));
            _ = LoadLatestDrawAsync();
        }
    }

    #endregion

    #region Commands

    /// <summary>
    /// 加载最新开奖数据
    /// </summary>
    [RelayCommand]
    private async Task LoadLatestDrawAsync()
    {
        await ExecuteAsync(async () =>
        {
            var result = await _lotteryDataService.GetLatestDrawAsync(SelectedLotteryType);
            if (result.IsSuccess && result.Value != null)
            {
                var draw = result.Value;
                LatestDrawNumber = draw.DrawNumber;
                LatestDrawDate = draw.DrawDate.ToString("yyyy-MM-dd");
                LatestRedNumbers = draw.GetRedNumbersString();
                LatestBlueNumbers = draw.GetBlueNumbersString();

                var countResult = await _lotteryDataService.GetDrawCountAsync(SelectedLotteryType);
                if (countResult.IsSuccess)
                {
                    TotalDrawCount = countResult.Value;
                    HasData = TotalDrawCount > 0;
                }
            }
            else
            {
                HasData = false;
            }
            return Result.Success();
        });
    }

    /// <summary>
    /// 同步开奖数据：以本地最新开奖日期为起点增量抓取（首次无数据=全量）
    /// </summary>
    [RelayCommand]
    private async Task SyncHistoryDataAsync()
    {
        await ExecuteAsync(async () =>
        {
            // 增量起点 = 本地最新开奖日期；已存在的最新一期会被重新 upsert 以刷新奖级
            DateTime? sinceDate = null;
            var latestResult = await _lotteryDataService.GetLatestDrawAsync(SelectedLotteryType);
            if (latestResult.IsSuccess && latestResult.Value != null)
                sinceDate = latestResult.Value.DrawDate;

            LoadingMessage = sinceDate.HasValue
                ? "正在增量同步开奖数据..."
                : "正在全量同步开奖数据（首次较慢）...";
            var result = await _lotteryDataService.SyncHistoryDataAsync(SelectedLotteryType, sinceDate);
            if (result.IsSuccess)
            {
                await LoadLatestDrawAsync();

                // 同步后自动验证购彩记录
                LoadingMessage = "正在验证预测记录...";
                var verifyResult = await _userPurchaseService.VerifyAndRewardAsync(SelectedLotteryType);
                var summary = verifyResult.IsSuccess ? verifyResult.Value : null;
                if (summary != null && summary.TotalVerified > 0)
                {
                    var detail = $"验证 {summary.TotalVerified} 注\n命中 {summary.HitCount} 注";
                    if (summary.BestPrizeLevel > 0)
                        detail += $"\n最佳: {summary.BestPrizeDescription}";
                    detail += $"\n\n{summary.EncouragementMessage}";

                    await _dialogService.DisplayAlertAsync("预测验证结果", detail, "确定");
                }
                else
                {
                    var newCount = result.Value;
                    var message = newCount > 0
                        ? $"新增 {newCount} 期开奖数据"
                        : (sinceDate.HasValue ? "已是最新，无新增开奖数据" : "同步完成，无数据");
                    await _dialogService.DisplayAlertAsync("同步完成", message, "确定");
                }
            }
            else
            {
                await _dialogService.DisplayAlertAsync("同步失败", result.Message ?? "未知错误", "确定");
            }
            return Result.Success();
        });
    }

    /// <summary>
    /// 机选一注
    /// </summary>
    [RelayCommand]
    private async Task QuickPickAsync()
    {
        await ExecuteAsync(async () =>
        {
            LoadingMessage = "正在机选号码...";
            var result = await _predictService.RandomPickAsync(SelectedLotteryType, 1);
            if (result.IsSuccess && result.Value.Count > 0)
            {
                var prediction = result.Value[0];
                var numbers = prediction.GetPredictedNumbersString();
                await _dialogService.DisplayAlertAsync("机选结果", $"本期推荐号码：\n{numbers}", "确定");
            }
            else
            {
                await _dialogService.DisplayAlertAsync("机选失败", result.Message ?? "未知错误", "确定");
            }
            return Result.Success();
        });
    }

    /// <summary>
    /// 训练预测模型：机器学习(LSTM)（内嵌原版 KittenCN/predict_Lottery_ticket，全量历史+GitHub默认超参）
    /// </summary>
    [RelayCommand]
    private async Task TrainModelAsync()
    {
        await ExecuteAsync(async () =>
        {
            LoadingMessage = $"正在训练{SelectedLotteryTypeName} LSTM模型（全量历史下载+训练，视数据量约10-40分钟）...";
            var result = await _predictService.TrainLstmModelAsync(SelectedLotteryType, CancellationToken);
            if (result.IsSuccess)
            {
                await _dialogService.DisplayAlertAsync("训练完成",
                    $"{SelectedLotteryTypeName}的LSTM模型已训练完成。", "确定");
            }
            else
            {
                await _dialogService.DisplayAlertAsync("训练失败", result.Message ?? "未知错误", "确定");
            }
            return Result.Success();
        });
    }

    /// <summary>
    /// 切换彩种
    /// </summary>
    [RelayCommand]
    private async Task SwitchLotteryTypeAsync(string type)
    {
        if (Enum.TryParse<LotteryType>(type, true, out var lotteryType))
        {
            SelectedLotteryType = lotteryType;
            await LoadLatestDrawAsync();
        }
    }

    /// <summary>
    /// 打开智能预测页面
    /// </summary>
    [RelayCommand]
    private async Task OpenPredictionAsync()
    {
        var page = _serviceProvider.GetRequiredService<Pages.Lottery.LotteryPredictionPage>();
        _serviceProvider.GetRequiredService<IWindowTitleService>()?.Register(page);
        await Helpers.WindowNavigator.CurrentPage!.Navigation.PushAsync(page);
    }

    /// <summary>
    /// 打开历史记录页面
    /// </summary>
    [RelayCommand]
    private async Task OpenHistoryAsync()
    {
        var page = _serviceProvider.GetRequiredService<Pages.Lottery.LotteryHistoryPage>();
        _serviceProvider.GetRequiredService<IWindowTitleService>()?.Register(page);
        await Helpers.WindowNavigator.CurrentPage!.Navigation.PushAsync(page);
    }

    /// <summary>
    /// 打开中奖结果页面
    /// </summary>
    [RelayCommand]
    private async Task OpenResultAsync()
    {
        var page = _serviceProvider.GetRequiredService<Pages.Lottery.LotteryResultPage>();
        _serviceProvider.GetRequiredService<IWindowTitleService>()?.Register(page);
        await Helpers.WindowNavigator.CurrentPage!.Navigation.PushAsync(page);
    }

    #endregion

    /// <summary>
    /// 页面初始化
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadLatestDrawAsync();
    }
}
