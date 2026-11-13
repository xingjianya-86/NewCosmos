using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Requests;
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
        Services.Domain.Reporting.IStatisticsService statisticsService,
        Services.Domain.Printing.IPrintJobFactory printJobFactory,
        Services.UserManagement.INewPermissionService permissionService)
    {
        _applicationService = applicationService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _statisticsService = statisticsService;
        _printJobFactory = printJobFactory;
        _permissionService = permissionService;
        Title = "临时救助";
    }

    private readonly Services.Domain.Reporting.IStatisticsService _statisticsService = null!;
    private readonly Services.Domain.Printing.IPrintJobFactory _printJobFactory = null!;
    private readonly Services.UserManagement.INewPermissionService _permissionService = null!;

    /// <summary>
    /// 动作前权限门控（对齐 ElderlyStopViewModel 2026-10-31 堵洞模式）：
    /// 本页确认/终止/删除此前零权限校验，仅靠入口卡片 TEMP_CREATE 可见性——入口可见即全部可点。
    /// </summary>
    private async Task<bool> HasTempPermissionAsync(string permissionCode, string actionLabel)
    {
        try
        {
            var userId = App.CurrentUserId ?? 0;
            if (userId > 0 && await _permissionService.HasPermissionAsync(userId, permissionCode, CancellationToken))
                return true;

            _logger.LogSecurity("临时救助操作被拦截",
                ("UserId", userId), ("Permission", permissionCode), ("Action", actionLabel));
            await _serviceProvider.GetRequiredService<IDialogService>().DisplayAlertAsync(
                "提示", $"无权限执行{actionLabel}，请联系管理员分配相应权限。", "确定");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "临时救助权限校验失败");
            return false;
        }
    }

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

    /// <summary>
    /// 已确认记录照片查看/上传：进入**档案照片查询页**（与低收入工作流、高龄申请列表同口径）——
    /// 检索、缩略图、预览、勾选批量推送打印、插入/拖拽；要就地拍照用查询页里的「打开采集页」。
    /// 照片按身份证归档，与高龄津贴、低收入人口认定共享同一份人员照片。
    /// 列表入口不传 AssignContext = 只存档不识别；识别回填由表单内的采集入口承担。
    /// </summary>
    [RelayCommand]
    private async Task OpenPhotosAsync(TempReliefApplication? app)
    {
        if (app == null || string.IsNullOrWhiteSpace(app.ApplicantIdCard))
        {
            await _serviceProvider.GetRequiredService<IDialogService>().DisplayAlertAsync(
                "提示", "该记录缺少身份证号，无法关联照片", "确定");
            return;
        }

        _logger.LogBusiness("临时救助列表-档案照片", ("ApplicationId", app.Id));
        await NavigateToPageAsync<Pages.ArchiveManagement.ArchivePhotoQueryPage, ArchivePhotoQueryParameter>(
            new ArchivePhotoQueryParameter(app.ApplicantIdCard.Trim(), ApplicationId: app.Id));
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
        if (!await HasTempPermissionAsync(PermissionCodes.TEMP_EDIT, "「确认生效」")) return;

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
        if (!await HasTempPermissionAsync(PermissionCodes.TEMP_EDIT, "「终止申请」")) return;

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
        if (!await HasTempPermissionAsync(PermissionCodes.TEMP_DELETE, "「删除申请」")) return;

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
            // 整档入口：先清文书模式上下文，防上一次「仅出文书」的静态残留被继承
            PrintNavigationData.ClearDocumentMode();
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

    /// <summary>
    /// 手机端推送打印：构建单据字段后写入推送打印队列，由 PC 端打印代理执行。
    /// </summary>
    [RelayCommand]
    private async Task PushPrintAsync(TempReliefApplication? app)
    {
        if (app == null) return;

        await ExecuteAsync(async () =>
        {
            var result = await _printJobFactory.EnqueueTempReliefAsync(app.Id, ct: CancellationToken);
            var dialogService = _serviceProvider.GetRequiredService<IDialogService>();
            if (result.IsFailure)
            {
                await dialogService.DisplayAlertAsync("推送失败", result.Message ?? "推送打印失败", "确定");
                return result;
            }

            _logger.LogBusiness("临时救助推送打印入队",
                ("ApplicationId", (object)app.Id),
                ("JobNo", result.Value?.JobNo ?? string.Empty));
            await dialogService.DisplayAlertAsync("已推送打印",
                $"打印任务 {result.Value?.JobNo} 已推送，将在电脑端自动打印。", "确定");
            return Result.Success();
        }, "推送打印...");
    }

    // 返回上一页：使用基类 GoBackCommand（含栈守卫与窗口标题恢复）
}
