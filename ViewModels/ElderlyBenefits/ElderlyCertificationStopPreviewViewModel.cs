using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ElderlyBenefits;

/// <summary>
/// 高龄年度认证「停发预览」：异常认证结果逐人核对 → 办理停发/暂缓/排除 → 回写认证任务。
/// 只允许逐人办理停发，本页不提供任何批量停发入口。
/// 查看用 ELDERLY_VIEW，暂缓/排除/办理停发用 ELDERLY_EDIT（保存前在 VM 内校验）。
/// </summary>
public partial class ElderlyCertificationStopPreviewViewModel : PagedSearchViewModelBase
{
    private readonly IElderlyCertificationService _certificationService;
    private readonly IElderlyApplicationService _applicationService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly INewPermissionService _permissionService;

    private readonly Dictionary<string, string> _statusLabelToKey = new();
    private ElderlyCertCampaign? _campaign;
    private bool _initialized;

    #region 抽象属性实现

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    #endregion

    #region 数据集合

    public ObservableCollection<ElderlyCertificationStopPreviewItem> Items { get; } = new();
    public ObservableCollection<string> StatusOptions { get; } = new();

    [ObservableProperty]
    private string _selectedStatus = "全部";

    [ObservableProperty]
    private string _campaignInfo = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _canView;

    [ObservableProperty]
    private bool _canEdit;

    #endregion

    public ElderlyCertificationStopPreviewViewModel(
        IElderlyCertificationService certificationService,
        IElderlyApplicationService applicationService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        INewPermissionService permissionService)
    {
        _certificationService = certificationService;
        _applicationService = applicationService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _permissionService = permissionService;

        Title = "认证停发预览";
        PageSize = 20;

        StatusOptions.Add("全部");
        foreach (var option in ElderlyCertificationConstants.CertStatusOptions
                     .Where(o => ElderlyCertificationConstants.IsStopCandidate(o.Key)))
        {
            StatusOptions.Add(option.Name);
            _statusLabelToKey[option.Name] = option.Key;
        }
    }

    #region 生命周期

    /// <summary>由认证名单页导航时注入批次快照；随后 OnAppearing 重新拉取批次状态。</summary>
    public void Initialize(ElderlyCertCampaign campaign)
    {
        _campaign = campaign;
        _initialized = true;
        UpdateCampaignInfo();
    }

    public override async Task OnAppearingAsync()
    {
        if (!_initialized || _campaign == null)
            return;

        await LoadPermissionAsync();
        if (!CanView)
        {
            await _dialogService.DisplayAlertAsync(
                "无权限", "当前账号没有查看高龄认证的权限，不能查看停发预览。", "确定");
            return;
        }

        // 批次可能在名单页停留期间被关闭/调整截止日，重新取一次以保证“待截止/逾期未核查”提示准确。
        var campaignResult = await _certificationService.GetCampaignAsync(_campaign.Id, CancellationToken);
        if (campaignResult.IsSuccess && campaignResult.Value != null)
            _campaign = campaignResult.Value;
        else if (campaignResult.IsFailure)
            _logger.Warn($"停发预览批次刷新失败: {campaignResult.Message}");

        UpdateCampaignInfo();
        await LoadDataAsync();
        await LoadStatsAsync();
    }

    private async Task LoadPermissionAsync()
    {
        try
        {
            var userId = App.CurrentUserId ?? 0;
            if (userId <= 0)
            {
                CanView = false;
                CanEdit = false;
                return;
            }

            var permissions = await _permissionService.CheckPermissionsAsync(
                userId,
                new[] { PermissionCodes.ELDERLY_VIEW, PermissionCodes.ELDERLY_EDIT },
                CancellationToken);
            CanView = permissions.TryGetValue(PermissionCodes.ELDERLY_VIEW, out var canView) && canView;
            CanEdit = permissions.TryGetValue(PermissionCodes.ELDERLY_EDIT, out var canEdit) && canEdit;
        }
        catch (Exception ex)
        {
            CanView = false;
            CanEdit = false;
            _logger.LogError(ex, "高龄认证停发预览权限加载失败");
        }
    }

    private void UpdateCampaignInfo()
    {
        CampaignInfo = _campaign == null
            ? "未选择认证批次"
            : $"{_campaign.DisplayName} · {_campaign.PeriodDisplay} · {_campaign.StatusName}";
    }

    protected override async Task LoadDataAsync()
    {
        if (_campaign == null)
        {
            Items.Clear();
            TotalCount = 0;
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _certificationService.GetStopPreviewPagedAsync(
                _campaign.Id,
                SearchText,
                GetStatusKey(SelectedStatus),
                PageIndex,
                PageSize,
                CancellationToken);

            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);

            var today = DateTime.Today;
            Items.Clear();
            foreach (var task in result.Value.Items)
            {
                Items.Add(new ElderlyCertificationStopPreviewItem(
                    task,
                    _campaign.Status,
                    _campaign.PeriodEnd,
                    _campaign.DeadlineStopOffset,
                    today));
            }

            TotalCount = result.Value.TotalCount;
            return Result.Success();
        }, "加载停发预览...");
    }

    private async Task LoadStatsAsync()
    {
        if (_campaign == null) return;

        var result = await _certificationService.GetStatsAsync(_campaign.Id, CancellationToken);
        if (result.IsFailure)
        {
            _logger.Warn($"高龄认证停发预览统计加载失败: {result.Message}");
            return;
        }

        var stats = result.Value;
        SummaryText = $"停发候选 {stats.StopCandidate:N0} 人 · 已停发 {stats.Stopped:N0} 人 · 待核查 {stats.Pending:N0} 人";
    }

    partial void OnSelectedStatusChanged(string value)
    {
        if (!_initialized || _campaign == null) return;
        PageIndex = 1;
        SafeFireAndForget(LoadDataAsync);
    }

    #endregion

    #region 命令

    /// <summary>逐人办理停发：打开现有停发登记页并预填建议原因/应停日期，成功后由停发页回写认证任务。</summary>
    [RelayCommand]
    private async Task StopAsync(ElderlyCertificationStopPreviewItem? item)
    {
        if (item == null) return;
        if (!await EnsureEditPermissionAsync()) return;

        if (!item.CanStop || item.ApplicationId is not { } applicationId)
        {
            await _dialogService.DisplayAlertAsync(
                "需先补建档",
                $"{item.Name} 在年度认证名册中尚未建立高龄档案（无档案ID），请先补建档后再办理停发。",
                "确定");
            return;
        }

        var appResult = await _applicationService.GetByIdAsync(applicationId, CancellationToken);
        if (appResult.IsFailure || appResult.Value == null)
        {
            await _dialogService.DisplayAlertAsync(
                "档案加载失败",
                $"{item.Name} 的高龄档案加载失败：{appResult.Message}",
                "确定");
            return;
        }

        _logger.LogBusiness("高龄认证停发预览-办理停发",
            ("TaskId", item.TaskId),
            ("Name", DataMasker.MaskName(item.Name)),
            ("IdCard", DataMasker.MaskIdCard(item.IdCard)),
            ("CertStatus", item.Task.CertStatus));

        var deathDate = item.Task.CertStatus == ElderlyCertificationConstants.CertStatusDeceased
            ? item.Task.DeathDate
            : null;

        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyStopPage, ElderlyStopPrefill>(
            new ElderlyStopPrefill(
                appResult.Value,
                deathDate,
                null,
                item.Task.ResultNote,
                item.TaskId,
                item.SuggestedStopReasonKey,
                item.SuggestedDueStopDate,
                item.PresetNote));
    }

    /// <summary>暂缓：必须填写跟进日期，可填备注。</summary>
    [RelayCommand]
    private async Task DeferAsync(ElderlyCertificationStopPreviewItem? item)
    {
        if (item == null) return;
        if (!await EnsureEditPermissionAsync()) return;

        var dateText = await _dialogService.DisplayPromptAsync(
            "暂缓停发",
            $"{item.Name}：请输入跟进日期（yyyy-MM-dd）",
            "下一步",
            "取消",
            DateTime.Today.AddDays(30).ToString("yyyy-MM-dd"),
            10);

        if (dateText == null) return;
        if (!DateTime.TryParse(dateText, out var followUpDate))
        {
            await _dialogService.DisplayAlertAsync("提示", "跟进日期格式不正确，请使用 yyyy-MM-dd", "确定");
            return;
        }

        var note = await _dialogService.DisplayPromptAsync(
            "暂缓停发",
            "备注（可选）",
            "保存",
            "取消",
            item.StopNote ?? string.Empty,
            500);
        if (note == null) return;

        var result = await _certificationService.SetStopDecisionAsync(
            item.TaskId,
            ElderlyCertificationConstants.StopDecisionDeferred,
            note,
            followUpDate,
            App.CurrentUserName,
            CancellationToken);

        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("保存失败", UserFriendlyMessages.Get(result.ErrorCode!, result.Message), "确定");
            return;
        }

        await LoadDataAsync();
        await _dialogService.ShowSnackBarAsync($"已暂缓 {item.Name}，跟进日期 {followUpDate:yyyy-MM-dd}");
    }

    /// <summary>排除：不进入停发，必须确认，可填备注。</summary>
    [RelayCommand]
    private async Task ExcludeAsync(ElderlyCertificationStopPreviewItem? item)
    {
        if (item == null) return;
        if (!await EnsureEditPermissionAsync()) return;

        var confirm = await _dialogService.DisplayAlertAsync(
            "排除停发",
            $"确定将 {item.Name} 排除出本次停发范围吗？排除后不再提示停发，可随时恢复为待停发。",
            "排除",
            "取消");
        if (!confirm) return;

        var note = await _dialogService.DisplayPromptAsync(
            "排除停发",
            "排除原因（可选）",
            "保存",
            "取消",
            item.StopNote ?? string.Empty,
            500);
        if (note == null) return;

        var result = await _certificationService.SetStopDecisionAsync(
            item.TaskId,
            ElderlyCertificationConstants.StopDecisionExcluded,
            note,
            null,
            App.CurrentUserName,
            CancellationToken);

        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("保存失败", UserFriendlyMessages.Get(result.ErrorCode!, result.Message), "确定");
            return;
        }

        await LoadDataAsync();
        await _dialogService.ShowSnackBarAsync($"已排除 {item.Name}");
    }

    /// <summary>暂缓/排除 → 恢复为待停发。</summary>
    [RelayCommand]
    private async Task ResetDecisionAsync(ElderlyCertificationStopPreviewItem? item)
    {
        if (item == null || !item.CanResetDecision) return;
        if (!await EnsureEditPermissionAsync()) return;

        var confirm = await _dialogService.DisplayAlertAsync(
            "恢复待停发",
            $"确定将 {item.Name} 恢复为「待停发」吗？",
            "恢复",
            "取消");
        if (!confirm) return;

        var result = await _certificationService.SetStopDecisionAsync(
            item.TaskId,
            ElderlyCertificationConstants.StopDecisionPending,
            null,
            null,
            App.CurrentUserName,
            CancellationToken);

        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("保存失败", UserFriendlyMessages.Get(result.ErrorCode!, result.Message), "确定");
            return;
        }

        await LoadDataAsync();
        await _dialogService.ShowSnackBarAsync($"{item.Name} 已恢复为待停发");
    }

    [RelayCommand]
    private async Task ViewEvidenceAsync(ElderlyCertificationStopPreviewItem? item)
    {
        if (item == null) return;

        var result = await _certificationService.GetEvidenceAsync(item.TaskId, includeData: false, ct: CancellationToken);
        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("查询失败", UserFriendlyMessages.Get(result.ErrorCode!, result.Message), "确定");
            return;
        }

        var evidence = result.Value ?? new List<ElderlyCertEvidence>();
        if (evidence.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("凭证", "该人暂无已匹配凭证", "确定");
            return;
        }

        var lines = evidence
            .OrderBy(e => e.Seq)
            .Select(e => $"{e.Seq}. {e.EvidenceTypeName} · {e.Display} · {e.FileSizeDisplay}")
            .ToList();
        await _dialogService.DisplayAlertAsync(
            $"{item.Name} 的凭证（{evidence.Count} 张）",
            string.Join("\n", lines),
            "关闭");
    }

    #endregion

    #region 工具

    private string? GetStatusKey(string label) =>
        string.IsNullOrWhiteSpace(label) || label == "全部" ? null : _statusLabelToKey.GetValueOrDefault(label);

    /// <summary>暂缓/排除/办理停发的权限门控：每次操作前实时校验，异常按无权限处理。</summary>
    private async Task<bool> EnsureEditPermissionAsync()
    {
        if (!CanEdit)
        {
            await _dialogService.DisplayAlertAsync(
                "无权限", "当前账号没有编辑普惠高龄的权限，不能变更停发决定。", "确定");
            return false;
        }

        try
        {
            var userId = App.CurrentUserId ?? 0;
            if (userId <= 0) return false;

            var permissions = await _permissionService.CheckPermissionsAsync(
                userId, new[] { PermissionCodes.ELDERLY_EDIT }, CancellationToken);
            var granted = permissions.TryGetValue(PermissionCodes.ELDERLY_EDIT, out var value) && value;
            if (!granted)
            {
                await _dialogService.DisplayAlertAsync(
                    "无权限", "当前账号没有编辑普惠高龄的权限，不能变更停发决定。", "确定");
            }
            return granted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "高龄认证停发预览权限校验失败");
            await _dialogService.DisplayAlertAsync("提示", "权限校验失败，请联系管理员。", "确定");
            return false;
        }
    }

    #endregion
}
