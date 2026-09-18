using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Models.Lottery;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Lottery;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.ViewModels.Lottery;

/// <summary>
/// 彩票历史记录页面 ViewModel
/// </summary>
public partial class LotteryHistoryViewModel : ViewModelBase
{
    private readonly ILotteryDataService _lotteryDataService;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerService _logger;
    private const int PageSize = 30;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    public LotteryHistoryViewModel(
        IServiceProvider serviceProvider,
        ILoggerService logger,
        ILotteryDataService lotteryDataService,
        IDialogService dialogService)
        : base()
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _lotteryDataService = lotteryDataService;
        _dialogService = dialogService;
    }

    #region Observable Properties

    /// <summary>
    /// 当前选中的彩种
    /// </summary>
    [ObservableProperty]
    private LotteryType _selectedLotteryType = LotteryType.SSQ;

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
        if (value != null && value.Value != SelectedLotteryType)
        {
            SelectedLotteryType = value.Value;
            CurrentPage = 1;
            _ = LoadDrawListAsync();
        }
    }

    /// <summary>
    /// 开奖记录列表
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<LotteryDraw> _drawList = new();

    /// <summary>
    /// 总记录数
    /// </summary>
    [ObservableProperty]
    private int _totalCount;

    /// <summary>
    /// 当前页码
    /// </summary>
    [ObservableProperty]
    private int _currentPage;

    /// <summary>
    /// 总页数
    /// </summary>
    [ObservableProperty]
    private int _totalPages;

    /// <summary>
    /// 是否有上一页
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoPreviousPage))]
    private bool _hasPreviousPage;

    /// <summary>
    /// 是否有下一页
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNextPage))]
    private bool _hasNextPage;

    /// <summary>
    /// 是否可以翻到上一页
    /// </summary>
    public bool CanGoPreviousPage => HasPreviousPage && !IsBusy;

    /// <summary>
    /// 是否可以翻到下一页
    /// </summary>
    public bool CanGoNextPage => HasNextPage && !IsBusy;

    #endregion

    #region Commands

    /// <summary>
    /// 加载开奖记录
    /// </summary>
    [RelayCommand]
    private async Task LoadDrawListAsync()
    {
        await ExecuteAsync(async () =>
        {
            LoadingMessage = "正在加载开奖记录...";
            DrawList.Clear();

            // 获取总数
            var countResult = await _lotteryDataService.GetDrawCountAsync(SelectedLotteryType);
            if (countResult.IsSuccess)
            {
                TotalCount = countResult.Value;
                TotalPages = (int)Math.Ceiling((double)TotalCount / PageSize);
            }

            // 获取当前页数据
            var result = await _lotteryDataService.GetDrawListAsync(SelectedLotteryType, CurrentPage, PageSize);
            if (result.IsSuccess)
            {
                foreach (var item in result.Value)
                {
                    DrawList.Add(item);
                }

                HasPreviousPage = CurrentPage > 1;
                HasNextPage = CurrentPage < TotalPages;
            }
            else
            {
                await _dialogService.DisplayAlertAsync("加载失败", result.Message ?? "未知错误", "确定");
            }
            return Result.Success();
        });
    }

    /// <summary>
    /// 上一页
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoPreviousPage))]
    private async Task GoPreviousPageAsync()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
            await LoadDrawListAsync();
        }
    }

    /// <summary>
    /// 下一页
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoNextPage))]
    private async Task GoNextPageAsync()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
            await LoadDrawListAsync();
        }
    }

    /// <summary>
    /// 查看开奖详情
    /// </summary>
    [RelayCommand]
    private async Task ViewDrawDetailAsync(LotteryDraw draw)
    {
        if (draw == null) return;

        var detail = $"期号: {draw.DrawNumber}\n" +
                     $"开奖日期: {draw.DrawDate:yyyy-MM-dd}\n" +
                     $"红球: {draw.GetRedNumbersString()}\n" +
                     $"蓝球: {draw.GetBlueNumbersString()}\n" +
                     $"销售额: {draw.SalesAmount:N0} 元\n" +
                     $"奖池: {draw.PoolMoney:N0} 元";

        await _dialogService.DisplayAlertAsync("开奖详情", detail, "确定");
    }

    #endregion

    /// <summary>
    /// 页面初始化
    /// </summary>
    public async Task InitializeAsync()
    {
        CurrentPage = 1;
        await LoadDrawListAsync();
    }
}
