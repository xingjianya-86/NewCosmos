using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.NavigationData;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.Recovery;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.ViewModels.Recovery;

/// <summary>
/// 后补追缴搜索页面 ViewModel
/// </summary>
public partial class RecoverySearchViewModel : ViewModelBase
{
    private readonly IRecoveryService _recoveryService;
    private readonly IDialogService _dialogService;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerService _logger;

    #region 统计属性

    /// <summary>停止人员总数</summary>
    [ObservableProperty]
    private string _totalCountText = "—";

    /// <summary>农村低保人数</summary>
    [ObservableProperty]
    private string _ruralSubsistenceCountText = "—";

    /// <summary>城市低保人数</summary>
    [ObservableProperty]
    private string _urbanSubsistenceCountText = "—";

    /// <summary>刚性支出人数</summary>
    [ObservableProperty]
    private string _rigidExpenditureCountText = "—";

    #endregion

    /// <summary>
    /// 搜索关键词
    /// </summary>
    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    /// <summary>
    /// 搜索结果列表
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<StoppedPersonDto> _searchResults = new();

    /// <summary>
    /// 是否有搜索结果
    /// </summary>
    [ObservableProperty]
    private bool _hasSearchResults;

    /// <summary>
    /// 选中的停止人员
    /// </summary>
    [ObservableProperty]
    private StoppedPersonDto? _selectedPerson;

    /// <summary>
    /// 是否处于停止人员搜索模式
    /// </summary>
    [ObservableProperty]
    private bool _isStoppedMode = true;

    /// <summary>
    /// 是否处于我的追缴记录模式
    /// </summary>
    [ObservableProperty]
    private bool _isHistoryMode;

    /// <summary>
    /// 历史记录搜索关键词
    /// </summary>
    [ObservableProperty]
    private string _historyKeyword = string.Empty;

    /// <summary>
    /// 追缴记录列表（草稿/已确认/已打印）
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<RecoveryRecord> _historyRecords = new();

    /// <summary>
    /// 是否有追缴记录
    /// </summary>
    [ObservableProperty]
    private bool _hasHistoryRecords;

    public RecoverySearchViewModel(
        IServiceProvider serviceProvider,
        ILoggerService logger,
        IRecoveryService recoveryService,
        IDialogService dialogService,
        Services.Domain.Printing.IPrintJobFactory printJobFactory)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _recoveryService = recoveryService;
        _dialogService = dialogService;
        _printJobFactory = printJobFactory;
    }

    private readonly Services.Domain.Printing.IPrintJobFactory _printJobFactory = null!;

    /// <summary>
    /// 加载统计数据
    /// </summary>
    public async Task LoadStatsAsync()
    {
        try
        {
            var result = await _recoveryService.GetStoppedPersonStatsAsync();
            if (result.IsSuccess)
            {
                var stats = result.Value;
                TotalCountText = stats.TotalCount.ToString("N0");
                RuralSubsistenceCountText = stats.RuralSubsistenceCount.ToString("N0");
                UrbanSubsistenceCountText = stats.UrbanSubsistenceCount.ToString("N0");
                RigidExpenditureCountText = stats.RigidExpenditureCount.ToString("N0");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "加载停止人员统计失败");
        }
    }

    /// <summary>
    /// 应用导航参数（RecoverySearchParameter，SetParameterAsync 于 Push 前调用）。
    /// 纯内存写入不打 IO：关键词按目标页签落位，pending 标记留给 OnAppearing 的
    /// <see cref="LoadInitialListAsync"/> 消费（属性 setter/参数注入里不做查询，§6）。
    /// </summary>
    public void ApplyNavigationParameter(RecoverySearchParameter? parameter)
    {
        if (parameter == null) return;

        if (!string.IsNullOrWhiteSpace(parameter.Keyword))
        {
            if (parameter.OpenHistoryMode)
                HistoryKeyword = parameter.Keyword;
            else
                SearchKeyword = parameter.Keyword;
        }

        if (parameter.OpenHistoryMode)
        {
            IsStoppedMode = false;
            IsHistoryMode = true;
            _pendingInitialLoad = true;
        }
        else if (!string.IsNullOrWhiteSpace(parameter.Keyword))
        {
            _pendingInitialLoad = true;
        }

        NotifyAllCommandsCanExecuteChanged();
    }

    /// <summary>导航参数触发的待执行初始加载（一次性消费，防重复 OnAppearing 触发）</summary>
    private bool _pendingInitialLoad;

    /// <summary>
    /// 页面出现时的初始加载（桌面/手机孪生页 OnAppearing 统一入口）：
    /// 统计必载；历史页签每次出现刷新列表（与手机端原行为一致）；
    /// 参数注入的 pending 一次性消费——历史模式加载历史，停止人员模式带关键词自动搜索一次。
    /// </summary>
    public async Task LoadInitialListAsync()
    {
        await LoadStatsAsync();

        if (IsHistoryMode)
        {
            await LoadHistoryAsync();
            _pendingInitialLoad = false;
            return;
        }

        if (_pendingInitialLoad)
        {
            _pendingInitialLoad = false;
            if (!string.IsNullOrWhiteSpace(SearchKeyword))
                await SearchAsync();
        }
    }

    /// <summary>
    /// 搜索命令
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchKeyword))
            return;

        try
        {
            IsBusy = true;
            LoadingMessage = "搜索停止人员中...";

            var result = await _recoveryService.SearchStoppedPersonsAsync(SearchKeyword);

            if (result.IsSuccess)
            {
                SearchResults.Clear();
                int rowIndex = 1;
                foreach (var person in result.Value)
                {
                    person.RowIndex = rowIndex++;
                    SearchResults.Add(person);
                }
                HasSearchResults = SearchResults.Count > 0;

                if (!HasSearchResults)
                {
                    ErrorMessage = "未找到匹配的停止人员";
                }
            }
            else
            {
                ErrorMessage = result.Message ?? "搜索失败";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "搜索停止人员异常");
            ErrorMessage = "搜索异常，请稍后重试";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSearch() => !IsBusy && !string.IsNullOrWhiteSpace(SearchKeyword);

    /// <summary>
    /// 选择人员并进入追缴表单
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanProceed))]
    private async Task ProceedToFormAsync()
    {
        if (SelectedPerson == null)
        {
            ErrorMessage = "请选择要追缴的人员";
            return;
        }

        try
        {
            IsBusy = true;
            LoadingMessage = "加载追缴表单...";

            // 导航到追缴表单页面，传递选中的人员信息（参数对象统一契约）
            await NavigateToPageAsync<Pages.Recovery.RecoveryFormPage, RecoveryFormParameter>(
                new RecoveryFormParameter(Person: SelectedPerson));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "导航到追缴表单异常");
            ErrorMessage = "操作异常，请稍后重试";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanProceed() => !IsBusy && SelectedPerson != null;

    /// <summary>
    /// 选择人员（从列表点击）
    /// </summary>
    [RelayCommand]
    private async Task SelectPersonAsync(StoppedPersonDto person)
    {
        if (person == null) return;

        SelectedPerson = person;
        await ProceedToFormAsync();
    }

    /// <summary>
    /// 跳转到手工录入页面
    /// </summary>
    [RelayCommand]
    private async Task GoToManualInputAsync()
    {
        try
        {
            // Record=null 即新建（参数对象统一契约，替代原无参跳转）
            await NavigateToPageAsync<Pages.Recovery.RecoveryManualPage, RecoveryManualParameter>(
                new RecoveryManualParameter());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "导航到手工录入页面异常");
            ErrorMessage = "操作异常，请稍后重试";
        }
    }

    /// <summary>
    /// 切换到停止人员搜索模式
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSwitchToStopped))]
    private async Task SwitchToStoppedAsync()
    {
        IsStoppedMode = true;
        IsHistoryMode = false;
        NotifyAllCommandsCanExecuteChanged();
        await Task.CompletedTask;
    }

    private bool CanSwitchToStopped() => !IsBusy && !IsStoppedMode;

    /// <summary>
    /// 切换到我的追缴记录模式
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSwitchToHistory))]
    private async Task SwitchToHistoryAsync()
    {
        IsStoppedMode = false;
        IsHistoryMode = true;
        NotifyAllCommandsCanExecuteChanged();
        await LoadHistoryAsync();
    }

    private bool CanSwitchToHistory() => !IsBusy && !IsHistoryMode;

    /// <summary>
    /// 加载追缴记录列表（草稿/已确认/已打印）
    /// </summary>
    public async Task LoadHistoryAsync()
    {
        try
        {
            IsBusy = true;
            LoadingMessage = "加载追缴记录中...";

            var result = await _recoveryService.GetRecoveryRecordsAsync(HistoryKeyword, limit: 100);

            if (result.IsSuccess)
            {
                HistoryRecords.Clear();
                foreach (var record in result.Value)
                {
                    HistoryRecords.Add(record);
                }
                HasHistoryRecords = HistoryRecords.Count > 0;
            }
            else
            {
                ErrorMessage = result.Message ?? "加载失败";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "加载追缴记录异常");
            ErrorMessage = "加载异常，请稍后重试";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 搜索我的追缴记录
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSearchHistory))]
    private async Task SearchHistoryAsync()
    {
        await LoadHistoryAsync();
    }

    private bool CanSearchHistory() => !IsBusy && IsHistoryMode;

    /// <summary>
    /// 删除追缴记录（仅草稿可删，二次确认）
    /// </summary>
    [RelayCommand]
    private async Task DeleteRecordAsync(RecoveryRecord record)
    {
        if (record == null) return;

        if (record.Status != RecoveryConstants.STATUS_DRAFT)
        {
            await _dialogService.DisplayAlertAsync("提示", "仅草稿状态的记录可删除", "确定");
            return;
        }

        var confirmed = await _dialogService.DisplayAlertAsync("删除确认",
            $"确认删除 {record.PersonName} 的追缴草稿？此操作不可恢复。", "删除", "取消");
        if (!confirmed) return;

        try
        {
            var result = await _recoveryService.DeleteRecoveryRecordAsync(record.Id);
            if (result.IsSuccess)
            {
                HistoryRecords.Remove(record);
                HasHistoryRecords = HistoryRecords.Count > 0;
                await _dialogService.ShowSnackBarAsync("草稿已删除");
            }
            else
            {
                await _dialogService.DisplayAlertAsync("删除失败", result.Message ?? "删除失败", "确定");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "删除追缴记录异常");
            await _dialogService.DisplayAlertAsync("删除失败", "删除异常，请稍后重试", "确定");
        }
    }

    /// <summary>
    /// 编辑追缴草稿（仅草稿，按录入模式跳对应表单页）
    /// </summary>
    [RelayCommand]
    private async Task EditRecordAsync(RecoveryRecord record)
    {
        if (record == null) return;

        if (record.Status != RecoveryConstants.STATUS_DRAFT)
        {
            await _dialogService.DisplayAlertAsync("提示", "仅草稿状态的记录可编辑", "确定");
            return;
        }

        try
        {
            if (record.InputMode == RecoveryConstants.INPUT_MODE_MANUAL)
            {
                await NavigateToPageAsync<Pages.Recovery.RecoveryManualPage, RecoveryManualParameter>(
                    new RecoveryManualParameter(Record: record));
            }
            else
            {
                await NavigateToPageAsync<Pages.Recovery.RecoveryFormPage, RecoveryFormParameter>(
                    new RecoveryFormParameter(Record: record));
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "编辑追缴记录导航异常");
            await _dialogService.DisplayAlertAsync("提示", "编辑记录失败，请稍后重试", "确定");
        }
    }

    /// <summary>
    /// 补打追缴办理单（仅已确认/已打印记录）
    /// </summary>
    [RelayCommand]
    private async Task ReprintRecordAsync(RecoveryRecord record)
    {
        if (record == null) return;

        if (record.Status == RecoveryConstants.STATUS_DRAFT)
        {
            await _dialogService.DisplayAlertAsync("提示", "草稿状态暂不支持补打", "确定");
            return;
        }

        try
        {
            // 整档入口：先清文书模式上下文，防上一次「仅出文书」的静态残留被继承
            PrintNavigationData.ClearDocumentMode();
            PrintNavigationData.BusinessType = PrintBusinessTypes.Recovery;
            PrintNavigationData.BusinessId = record.Id;
            PrintNavigationData.Classification = record.InputMode == RecoveryConstants.INPUT_MODE_MANUAL
                ? RecoveryConstants.INPUT_MODE_MANUAL
                : RecoveryConstants.INPUT_MODE_SEARCH;
            PrintNavigationData.FieldData = await BuildPrintFieldsAsync(record);
            PrintNavigationData.TableData = new();
            PrintNavigationData.SupporterTableData = null;
            PrintNavigationData.NearRelativePairs = null;

            await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveOutputPage>();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "补打追缴记录异常");
            await _dialogService.DisplayAlertAsync("提示", "补打失败，请稍后重试", "确定");
        }
    }

    /// <summary>
    /// 推送打印追缴办理单（仅已确认/已打印记录）：写入推送打印队列，由 PC 端打印代理执行。
    /// </summary>
    [RelayCommand]
    private async Task PushPrintRecordAsync(RecoveryRecord? record)
    {
        if (record == null) return;

        if (record.Status == RecoveryConstants.STATUS_DRAFT)
        {
            await _dialogService.DisplayAlertAsync("提示", "草稿状态暂不支持推送打印", "确定");
            return;
        }

        try
        {
            var request = new PrintJobRequest
            {
                BusinessType = PrintBusinessTypes.Recovery,
                BusinessId = record.Id,
                Classification = record.InputMode == RecoveryConstants.INPUT_MODE_MANUAL
                    ? RecoveryConstants.INPUT_MODE_MANUAL
                    : RecoveryConstants.INPUT_MODE_SEARCH,
                ApplicantName = record.PersonName,
                ApplicantIdCard = record.IdCard,
                Fields = await BuildPrintFieldsAsync(record),
                TableRows = new()
            };

            var push = await _printJobFactory.EnqueueAsync(request);
            if (push.IsFailure)
            {
                await _dialogService.DisplayAlertAsync("推送失败", push.Message ?? "推送打印失败", "确定");
                return;
            }

            await _dialogService.DisplayAlertAsync("已推送打印",
                $"打印任务 {push.Value?.JobNo} 已推送，将在电脑端自动打印。", "确定");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "推送打印追缴记录异常");
            await _dialogService.DisplayAlertAsync("提示", "推送打印失败，请稍后重试", "确定");
        }
    }

    /// <summary>
    /// 构建《追缴资金办理单》补打字段（单点装配于 RecoveryPrintFieldBuilder，三入口共用）
    /// </summary>
    private Task<Dictionary<string, string>> BuildPrintFieldsAsync(RecoveryRecord record)
        => RecoveryPrintFieldBuilder.BuildFieldsAsync(
            ServiceProvider, _recoveryService, record.Id,
            record.PersonName, record.IdCard, record.SourceTypeDisplay,
            record.RecoveryAmount, record.RecoveredAmount,
            record.StartMonth ?? string.Empty, record.EndMonth ?? string.Empty);
}
