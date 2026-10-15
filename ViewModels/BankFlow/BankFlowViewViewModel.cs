using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.BankFlow;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.BankFlow;

/// <summary>
/// 银行流水核对查看页：批次筛选 + 人员汇总分页搜索 + 导出。
/// 超阈值仅为提示标记（30 个月低保标准初筛），不作自动决策；权威判定在经济状况界面带入时重算。
/// </summary>
public partial class BankFlowViewViewModel : PagedSearchViewModelBase
{
    private readonly IBankFlowQueryService _bankFlowQueryService;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerService _logger;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    /// <summary>人员汇总列表</summary>
    public ObservableCollection<BankFlowRecord> Items { get; } = new();

    /// <summary>批次列表（查看页筛选下拉）</summary>
    [ObservableProperty]
    private ObservableCollection<BankFlowImport> _batches = new();

    [ObservableProperty]
    private BankFlowImport? _selectedBatch;

    /// <summary>仅看超 30 月标准（初筛提示）</summary>
    [ObservableProperty]
    private bool _exceedOnly;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>列表口径统计文本（本页条数/超阈值/存疑）</summary>
    [ObservableProperty]
    private string _summaryText = "暂无数据";

    public BankFlowViewViewModel(
        IBankFlowQueryService bankFlowQueryService,
        IDialogService dialogService,
        IServiceProvider serviceProvider,
        ILoggerService logger)
    {
        _bankFlowQueryService = bankFlowQueryService;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    #region 加载

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        PageIndex = 1;
        await LoadBatchesAsync();
        await LoadDataAsync();
    }

    protected override Task LoadDataAsync()
        => LoadPageAsync(
            ct => _bankFlowQueryService.SearchRecordsAsync(SearchText, SelectedBatch?.Id, ExceedOnly, PageIndex, PageSize, ct),
            Items,
            _ => UpdateSummary(),
            "加载核对数据...");

    private async Task LoadBatchesAsync()
    {
        var result = await _bankFlowQueryService.GetImportsAsync(CancellationToken);
        if (result.IsSuccess && result.Value != null)
        {
            var keepId = SelectedBatch?.Id;
            Batches.Clear();
            foreach (var batch in result.Value)
                Batches.Add(batch);
            SelectedBatch = Batches.FirstOrDefault(b => b.Id == keepId);
        }
        else
        {
            // 批次加载失败仅记日志：列表查询不依赖批次列表，可独立展示
            _logger.Warn($"加载核对批次列表失败: {result.Message}");
        }
    }

    private void UpdateSummary()
    {
        if (TotalCount == 0)
        {
            SummaryText = "暂无数据";
            return;
        }
        var exceed = Items.Count(i => i.IsExceed);
        var invalid = Items.Count(i => !i.IdCardValid);
        SummaryText = $"本页 {Items.Count} 条（共 {TotalCount} 条），超阈值 {exceed} 条、身份证存疑 {invalid} 条";
    }

    /// <summary>切换批次 → 回第一页重查</summary>
    partial void OnSelectedBatchChanged(BankFlowImport? value)
    {
        if (!IsBusy) _ = ResetAndReloadAsync();
    }

    /// <summary>切换「仅看超阈值」→ 回第一页重查</summary>
    partial void OnExceedOnlyChanged(bool value)
    {
        if (!IsBusy) _ = ResetAndReloadAsync();
    }

    private async Task ResetAndReloadAsync()
    {
        PageIndex = 1;
        await LoadDataAsync();
    }

    #endregion

    #region 命令

    [RelayCommand]
    private Task RefreshAsync()
    {
        return LoadBatchesAndDataAsync();
    }

    private async Task LoadBatchesAndDataAsync()
    {
        await LoadBatchesAsync();
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (IsBusy) return;
        try
        {
            var folder = await PickExportFolderAsync("选择导出目录", cancelMessage: "已取消导出");
            if (string.IsNullOrWhiteSpace(folder)) return;

            var result = await _bankFlowQueryService.ExportAsync(
                SearchText, SelectedBatch?.Id, ExceedOnly, CancellationToken);
            if (result.IsFailure || result.Value == null)
            {
                await _dialogService.DisplayAlertAsync("导出失败", result.Message, "确定");
                return;
            }

            var fileName = $"银行流水核对_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            var path = Path.Combine(folder, fileName);
            await File.WriteAllBytesAsync(path, result.Value, CancellationToken);

            _logger.LogBusiness("导出银行流水核对数据",
                ("Count", TotalCount), ("File", fileName));
            await _dialogService.ShowSnackBarAsync($"导出成功: {fileName}");
        }
        catch (OperationCanceledException)
        {
            await _dialogService.ShowSnackBarAsync("导出已取消", Components.SnackBarType.Info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导出银行流水核对数据失败");
            await _dialogService.DisplayAlertAsync("导出失败", ex.Message, "确定");
        }
    }

    #endregion
}
