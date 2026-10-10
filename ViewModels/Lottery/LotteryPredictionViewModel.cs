using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Constants;
using NewCosmos.Models.Lottery;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Lottery;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.ViewModels.Lottery;

/// <summary>
/// 彩票预测页面 ViewModel
/// </summary>
public partial class LotteryPredictionViewModel : ViewModelBase
{
    private readonly ILotteryDataService _lotteryDataService;
    private readonly ILotteryPredictService _predictService;
    private readonly IUserPurchaseService _userPurchaseService;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerService _logger;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    public LotteryPredictionViewModel(
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
    /// 生成注数
    /// </summary>
    [ObservableProperty]
    private int _predictionCount = 5;

    /// <summary>
    /// 预测结果列表
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PredictionResult> _predictions = new();

    /// <summary>
    /// 彩种选项列表
    /// </summary>
    public List<LotteryTypeOption> LotteryTypeOptions { get; } = LotteryTypeOption.GetAll();

    /// <summary>
    /// 当前选中的彩种选项
    /// </summary>
    [ObservableProperty]
    private LotteryTypeOption _selectedLotteryTypeOption = new() { Name = "双色球", Value = LotteryType.SSQ };

    /// <summary>
    /// 最新开奖日期
    /// </summary>
    [ObservableProperty]
    private string _latestDrawDate = "--";

    partial void OnSelectedLotteryTypeOptionChanged(LotteryTypeOption value)
    {
        if (value != null)
        {
            SelectedLotteryType = value.Value;
            _ = LoadLatestDrawDateAsync();
        }
    }

    private async Task LoadLatestDrawDateAsync()
    {
        var result = await _lotteryDataService.GetLatestDrawAsync(SelectedLotteryType);
        if (result.IsSuccess && result.Value != null)
        {
            LatestDrawDate = result.Value.DrawDate.ToString("yyyy-MM-dd (ddd)");
        }
        else
        {
            LatestDrawDate = "--";
        }
    }

    /// <summary>
    /// 预设注数选项
    /// </summary>
    public List<int> PredictionCountOptions { get; } = new() { 1, 3, 5, 10, 20 };

    #endregion

    #region Commands

    /// <summary>
    /// 执行预测
    /// </summary>
    [RelayCommand]
    private async Task ExecutePredictionAsync()
    {
        // 机器学习(LSTM)需要先训练模型
        if (!_predictService.IsLstmModelTrained(SelectedLotteryType))
        {
            var goTrain = await _dialogService.DisplayAlertAsync(
                "模型未训练",
                $"尚未训练{SelectedLotteryType.GetDisplayName()}的LSTM模型。\n请先在首页点击「训练预测模型」进行训练。",
                "去训练", "取消");
            if (goTrain)
            {
                await GoBackAsync();
            }
            return;
        }

        await ExecuteAsync(async () =>
        {
            LoadingMessage = "正在生成预测号码...";
            Predictions.Clear();

            var result = await _predictService.PredictByLstmAsync(SelectedLotteryType, PredictionCount);

            if (result.IsSuccess)
            {
                foreach (var item in result.Value)
                {
                    Predictions.Add(item);
                }

                // 预测即购彩：每次生成只入库一次（「保存结果」仅做导出，不再重复入库）
                var saveResult = await _userPurchaseService.SaveFromPredictionsAsync(
                    SelectedLotteryType, result.Value, LotteryConstants.LSTM_ALGORITHM_NAME);
                if (!saveResult.IsSuccess)
                {
                    await _dialogService.DisplayAlertAsync("入库失败",
                        saveResult.Message ?? "购彩记录保存失败，请稍后重试", "确定");
                }
            }
            else
            {
                await _dialogService.DisplayAlertAsync("预测失败", result.Message ?? "未知错误", "确定");
            }
            return Result.Success();
        });
    }

    /// <summary>
    /// 保存预测结果（剪贴板 + 文件）
    /// </summary>
    [RelayCommand]
    private async Task SavePredictionsAsync()
    {
        if (Predictions.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "没有可保存的预测结果，请先生成预测", "确定");
            return;
        }

        try
        {
            // 构建保存内容
            var lines = new List<string>();
            lines.Add($"=== {SelectedLotteryType.GetDisplayName()} 预测结果 ===");
            lines.Add($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"算法: {LotteryConstants.LSTM_ALGORITHM_NAME}");
            lines.Add($"注数: {Predictions.Count}");
            lines.Add("");

            for (int i = 0; i < Predictions.Count; i++)
            {
                var pred = Predictions[i];
                var reds = string.Join(",", pred.GetPredictedReds().Select(n => n.ToString("D2")));
                var blues = string.Join(",", pred.GetPredictedBlues().Select(n => n.ToString("D2")));
                lines.Add($"第{i + 1}注: {reds} + {blues}");
            }

            lines.Add("");
            lines.Add("祝您好运！");

            var content = string.Join(Environment.NewLine, lines);

            // 1. 复制到剪贴板
            await Clipboard.SetTextAsync(content);

            // 2. 保存到文件
            var fileName = $"lottery_{SelectedLotteryType.ToString().ToLower()}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var filePath = Path.Combine(AppContext.BaseDirectory, "Data", "Lottery", fileName);
            var dir = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir!);
            await File.WriteAllTextAsync(filePath, content);

            // 注：购彩记录已在「生成预测」时自动入库，此处仅导出，避免重复入库
            await _dialogService.DisplayAlertAsync("保存成功",
                $"已复制到剪贴板\n已保存到文件: {fileName}",
                "确定");
        }
        catch (Exception ex)
        {
            await _dialogService.DisplayAlertAsync("保存失败", ex.Message, "确定");
        }
    }

    /// <summary>
    /// 查看单条预测详情
    /// </summary>
    [RelayCommand]
    private async Task ViewPredictionDetailAsync(PredictionResult prediction)
    {
        if (prediction == null) return;

        var detail = $"彩种: {prediction.LotteryType.GetDisplayName()}\n" +
                     $"算法: {prediction.AlgorithmName}\n" +
                     $"预测号码: {prediction.GetPredictedNumbersString()}\n" +
                     $"置信度: {prediction.ConfidenceScore:F1}%";

        await _dialogService.DisplayAlertAsync("预测详情", detail, "确定");
    }

    #endregion
}
