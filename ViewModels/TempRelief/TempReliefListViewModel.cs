using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.TempRelief;
using NewCosmos.Services.Utilities;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.TempRelief;

/// <summary>
/// 临时救助申请列表 ViewModel（工作流风格：3 个状态 Tab + 计数徽章 + 卡片列表）
/// </summary>
public partial class TempReliefListViewModel : PagedSearchViewModelBase
{
    private readonly ITempReliefService _applicationService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IServiceProvider _serviceProvider = null!;

    private int _loadVersion;
    private CancellationTokenSource? _tabLoadCts;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    #region Tab 状态

    [ObservableProperty]
    private int _selectedTabIndex = 1;

    public bool IsTab0Selected => SelectedTabIndex == 0;
    public bool IsTab1Selected => SelectedTabIndex == 1;
    public bool IsTab2Selected => SelectedTabIndex == 2;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTab0Selected));
        OnPropertyChanged(nameof(IsTab1Selected));
        OnPropertyChanged(nameof(IsTab2Selected));

        PageIndex = 1;
        SearchText = string.Empty;
        PageSize = value == 1 ? 10 : 20;

        var version = ++_loadVersion;
        _tabLoadCts?.Cancel();
        _tabLoadCts = new CancellationTokenSource();

        SafeFireAndForget(async () => await LoadDataAsync(_tabLoadCts.Token, version));
    }

    #endregion

    #region 数据集合

    [ObservableProperty]
    private ObservableCollection<TempReliefApplication> _draftApps = new();

    [ObservableProperty]
    private ObservableCollection<TempReliefApplication> _confirmedApps = new();

    [ObservableProperty]
    private ObservableCollection<TempReliefApplication> _stoppedApps = new();

    #endregion

    #region 计数徽章

    [ObservableProperty]
    private int _draftCount;

    [ObservableProperty]
    private int _confirmedCount;

    [ObservableProperty]
    private int _stoppedCount;

    #endregion

    public TempReliefListViewModel(
        ITempReliefService applicationService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        Services.Domain.Reporting.IStatisticsService statisticsService)
    {
        _applicationService = applicationService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _statisticsService = statisticsService;
        Title = "临时救助";
    }

    private readonly Services.Domain.Reporting.IStatisticsService _statisticsService = null!;

    #region 页头统计栏

    /// <summary>统计加载失败占位</summary>
    private const string StatNA = "—";

    /// <summary>本年确认人次</summary>
    [ObservableProperty]
    private string _yearConfirmedText = StatNA;

    /// <summary>本年确认金额（元）</summary>
    [ObservableProperty]
    private string _yearAmountText = StatNA;

    private async Task LoadHeaderStatsAsync()
    {
        try
        {
            var result = await _statisticsService.GetTempReliefStatsAsync();
            if (result.IsSuccess && result.Value != null)
            {
                YearConfirmedText = result.Value.YearConfirmed.ToString("N0");
                YearAmountText = result.Value.YearConfirmedAmount.ToString("N0");
            }
            else
            {
                _logger.Warn($"临时救助统计加载失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "临时救助页头统计加载异常");
        }
    }

    #endregion

    public override async Task OnAppearingAsync()
    {
        try
        {
            await LoadAllCountsAsync();
            await LoadDataAsync();
            _ = LoadHeaderStatsAsync();
        }
        catch (Exception)
        {
            ErrorMessage = "页面加载失败，请重试";
        }
    }

    public override void OnDisappearing()
    {
        _tabLoadCts?.Cancel();
        _tabLoadCts?.Dispose();
        _tabLoadCts = null;
        base.OnDisappearing();
    }

    [RelayCommand]
    private void SwitchTab(string? tabIndex)
    {
        if (int.TryParse(tabIndex, out var idx))
        {
            SelectedTabIndex = idx;
        }
    }

    protected override async Task LoadDataAsync()
    {
        await ExecuteAsync(async ct =>
        {
            await LoadCurrentTabDataAsync(ct, _loadVersion);
        }, "加载数据...");
    }

    private async Task LoadDataAsync(CancellationToken ct, int version)
    {
        await ExecuteAsync(async () =>
        {
            await LoadCurrentTabDataAsync(ct, version);
            return Result.Success();
        }, "加载数据...");
    }

    private async Task LoadCurrentTabDataAsync(CancellationToken ct, int version)
    {
        var status = SelectedTabIndex switch
        {
            0 => TempReliefConstants.StatusDraft,
            1 => TempReliefConstants.StatusConfirmed,
            _ => TempReliefConstants.StatusStopped
        };

        var result = await _applicationService.GetPagedAsync(SearchText, status, PageIndex, PageSize, ct);
        if (result.IsFailure || result.Value == null)
        {
            _logger.LogError(new Exception(result.Message ?? "未知错误"), "加载临时救助列表失败");
            return;
        }

        if (version != _loadVersion) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (version != _loadVersion) return;

            // 填充段复用基类模板半段；版本守卫在本方法内保留（防切 Tab 后旧响应覆盖）
            var target = SelectedTabIndex switch
            {
                0 => DraftApps,
                1 => ConfirmedApps,
                _ => StoppedApps
            };
            FillPagedPage(target, result.Value);
        });
    }

    private async Task LoadAllCountsAsync()
    {
        try
        {
            var draftResult = await _applicationService.GetPagedAsync(string.Empty, TempReliefConstants.StatusDraft, 1, 1);
            if (draftResult.IsSuccess && draftResult.Value != null)
                DraftCount = draftResult.Value.TotalCount;

            var confirmedResult = await _applicationService.GetPagedAsync(string.Empty, TempReliefConstants.StatusConfirmed, 1, 1);
            if (confirmedResult.IsSuccess && confirmedResult.Value != null)
                ConfirmedCount = confirmedResult.Value.TotalCount;

            var stoppedResult = await _applicationService.GetPagedAsync(string.Empty, TempReliefConstants.StatusStopped, 1, 1);
            if (stoppedResult.IsSuccess && stoppedResult.Value != null)
                StoppedCount = stoppedResult.Value.TotalCount;
        }
        catch (Exception)
        {
            // 计数加载失败不影响主列表
        }
    }

    [RelayCommand]
    private async Task CreateNewAsync()
    {
        _logger.LogBusiness("创建临时救助申请");
        await NavigateToPageAsync<Pages.TempRelief.TempReliefFormPage, FormPageParameter>(new FormPageParameter(FormOperationMode.Create));
    }

    [RelayCommand]
    private async Task ViewDetailAsync(TempReliefApplication? app)
    {
        if (app == null) return;

        _logger.LogBusiness("查看临时救助申请", ("ApplicationId", app.Id));
        await NavigateToPageAsync<Pages.TempRelief.TempReliefFormPage, FormPageParameter>(new FormPageParameter(FormOperationMode.View, app.Id));
    }

    [RelayCommand]
    private async Task EditAsync(TempReliefApplication? app)
    {
        if (app == null) return;

        _logger.LogBusiness("编辑临时救助申请", ("ApplicationId", app.Id));
        await NavigateToPageAsync<Pages.TempRelief.TempReliefFormPage, FormPageParameter>(new FormPageParameter(FormOperationMode.Edit, app.Id));
    }

    [RelayCommand]
    private async Task ConfirmAsync(TempReliefApplication? app)
    {
        if (app == null) return;

        var dialogService = _serviceProvider.GetRequiredService<IDialogService>();
        var confirm = await dialogService.DisplayAlertAsync("确认生效",
            $"确定要确认 [{app.ApplicationNo}] {app.ApplicantName} 的临时救助申请生效吗？",
            "确认", "取消");
        if (!confirm) return;

        await ExecuteAsync(async () =>
        {
            var result = await _applicationService.ConfirmAsync(app.Id, App.CurrentUserName, CancellationToken);
            if (result.IsSuccess)
            {
                _logger.LogBusiness("临时救助申请确认成功", ("ApplicationId", app.Id));
                await LoadAllCountsAsync();
                await LoadDataAsync();
            }
            return result;
        }, "确认申请生效...");
    }

    [RelayCommand]
    private async Task StopAsync(TempReliefApplication? app)
    {
        if (app == null) return;

        var dialogService = _serviceProvider.GetRequiredService<IDialogService>();
        var confirm = await dialogService.DisplayAlertAsync("终止申请",
            $"确定要终止 [{app.ApplicationNo}] {app.ApplicantName} 的临时救助申请吗？\n终止后本年度仍占用唯一申请名额。",
            "终止", "取消");
        if (!confirm) return;

        var reason = await dialogService.DisplayPromptAsync("终止原因", "请填写终止原因：", "确定", "取消", "如：信息录入错误/不符合救助条件等");
        if (reason == null) return;

        await ExecuteAsync(async () =>
        {
            var result = await _applicationService.StopAsync(app.Id, reason, App.CurrentUserName, CancellationToken);
            if (result.IsSuccess)
            {
                _logger.LogBusiness("临时救助申请终止成功", ("ApplicationId", app.Id));
                await LoadAllCountsAsync();
                await LoadDataAsync();
            }
            return result;
        }, "终止申请...");
    }

    [RelayCommand]
    private async Task DeleteAsync(TempReliefApplication? app)
    {
        if (app == null) return;

        var dialogService = _serviceProvider.GetRequiredService<IDialogService>();
        var confirm = await dialogService.DisplayAlertAsync("确认删除",
            $"确定要删除申请 [{app.ApplicationNo}] {app.ApplicantName} 吗？\n此操作不可恢复。",
            "删除", "取消");
        if (!confirm) return;

        var result = await _applicationService.DeleteAsync(app.Id);
        if (result.IsSuccess)
        {
            var target = SelectedTabIndex switch
            {
                0 => DraftApps,
                1 => ConfirmedApps,
                _ => StoppedApps
            };
            target.Remove(app);
            _logger.LogBusiness("临时救助申请删除成功", ("ApplicationId", app.Id));
        }
        else
        {
            await dialogService.DisplayAlertAsync("删除失败", result.Message ?? "删除失败，请重试", "确定");
        }
    }

    /// <summary>
    /// 已确认记录补打：加载完整数据 → 构建打印字段 → 导航到档案输出页（复用已有模板打印逻辑）
    /// </summary>
    [RelayCommand]
    private async Task ReprintAsync(TempReliefApplication? app)
    {
        if (app == null) return;

        await ExecuteAsync(async () =>
        {
            // 加载完整数据（主表 + 子表）
            var fullResult = await _applicationService.GetByIdAsync(app.Id, CancellationToken);
            if (fullResult.IsFailure || fullResult.Value == null)
            {
                var ds = _serviceProvider.GetRequiredService<IDialogService>();
                await ds.DisplayAlertAsync("提示", "加载该档案数据失败，请确认档案数据完整", "确定");
                return Result.Failure(ErrorCodes.NOT_FOUND, "加载失败");
            }
            var fullApp = fullResult.Value;

            // 加载子表（打印需要疾病/意外/教育/成员明细）
            var diseases = await _applicationService.GetDiseasesByApplicationIdAsync(app.Id, CancellationToken);
            if (diseases.IsSuccess && diseases.Value != null) fullApp.Diseases = diseases.Value;

            var accidents = await _applicationService.GetAccidentsByApplicationIdAsync(app.Id, CancellationToken);
            if (accidents.IsSuccess && accidents.Value != null) fullApp.Accidents = accidents.Value;

            var educations = await _applicationService.GetEducationsByApplicationIdAsync(app.Id, CancellationToken);
            if (educations.IsSuccess && educations.Value != null) fullApp.Educations = educations.Value;

            var membersResult = await _applicationService.GetMembersByApplicationIdAsync(app.Id, CancellationToken);
            var memberList = membersResult.IsSuccess ? membersResult.Value ?? new() : new List<TempReliefMember>();

            // 构建打印字段
            var timelineService = _serviceProvider.GetRequiredService<IBusinessTimelineService>();
            var (acceptanceDate, investigationDate) = await TempReliefPrintDataBuilder.ResolveScheduleAsync(timelineService, fullApp);
            var contactUnitPhone = (await _applicationService.GetReportUnitPhoneAsync(fullApp.ReportUnit, CancellationToken)).Value ?? string.Empty;
            var fields = TempReliefPrintDataBuilder.BuildSingleFields(
                fullApp, memberList, contactUnitPhone, acceptanceDate, investigationDate);

            _logger.LogBusiness("临时救助列表补打 - 构建打印数据完成",
                ("ApplicationId", app.Id.ToString()),
                ("Applicant", DataMasker.MaskName(fullApp.ApplicantName)),
                ("ReliefType", fullApp.ReliefType));

            // 填充 PrintNavigationData（复用全局单例，与 ArchiveOutputViewModel 一致）
            PrintNavigationData.BusinessType = TempReliefConstants.BusinessType;
            PrintNavigationData.BusinessId = app.Id;
            PrintNavigationData.Classification = fullApp.ReliefType;
            PrintNavigationData.FieldData = fields;
            PrintNavigationData.TableData = new();
            PrintNavigationData.SupporterTableData = null;

            // 导航到档案输出页
            await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveOutputPage>();
            return Result.Success();
        }, "准备补打数据...");
    }

    // 返回上一页：使用基类 GoBackCommand（含栈守卫与窗口标题恢复）
}
