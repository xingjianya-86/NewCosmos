using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ElderlyBenefits;

/// <summary>
/// 高龄信息变更待审批列表：待审批/全部筛选、关键字、分页；
/// 弹窗查看 old/new 明细，填写审批意见与银行卡生效月后通过/驳回。
/// 查看权限 ELDERLY_VIEW；通过/驳回权限 ELDERLY_EDIT。
/// </summary>
public partial class ElderlyInfoChangeListViewModel : PagedSearchViewModelBase
{
    private readonly IElderlyInfoChangeService _infoChangeService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly INewPermissionService _permissionService;

    private readonly Dictionary<string, string?> _statusLabelToKey = new();

    #region 抽象属性实现

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    #endregion

    #region 数据集合

    public ObservableCollection<ElderlyInfoChangeRecord> Items { get; } = new();
    public ObservableCollection<string> StatusOptions { get; } = new();

    [ObservableProperty]
    private string _selectedStatus = "待审批";

    [ObservableProperty]
    private bool _canView;

    /// <summary>ELDERLY_EDIT：通过/驳回。</summary>
    [ObservableProperty]
    private bool _canReview;

    [ObservableProperty]
    private ElderlyInfoChangeRecord? _selectedRecord;

    [ObservableProperty]
    private bool _isReviewPanelVisible;

    [ObservableProperty]
    private string _reviewOpinion = string.Empty;

    [ObservableProperty]
    private string _effectiveMonth = string.Empty;

    #endregion

    public ElderlyInfoChangeListViewModel(
        IElderlyInfoChangeService infoChangeService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        INewPermissionService permissionService)
    {
        _infoChangeService = infoChangeService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _permissionService = permissionService;

        Title = "信息变更审批";
        PageSize = 20;

        _statusLabelToKey["待审批"] = ElderlyInfoChangeConstants.StatusPending;
        _statusLabelToKey["全部"] = null;
        _statusLabelToKey["已通过"] = ElderlyInfoChangeConstants.StatusApproved;
        _statusLabelToKey["已驳回"] = ElderlyInfoChangeConstants.StatusRejected;

        foreach (var label in _statusLabelToKey.Keys)
            StatusOptions.Add(label);
    }

    #region 计算属性

    public string ReviewHeader => SelectedRecord == null
        ? "变更单"
        : $"{SelectedRecord.Name} · {SelectedRecord.ChangeNo}";

    public string ReviewSubHeader => SelectedRecord == null
        ? string.Empty
        : $"{SelectedRecord.IdCard} · {SelectedRecord.RiskName} · {SelectedRecord.SourceDisplay} · {SelectedRecord.CreatedAtDisplay}";

    public string ReviewReason => string.IsNullOrWhiteSpace(SelectedRecord?.Reason)
        ? "（未填写变更原因）"
        : SelectedRecord!.Reason!;

    /// <summary>银行卡/代领卡变更时审批弹窗显示生效月。</summary>
    public bool RequiresEffectiveMonth => SelectedRecord?.Details
        .Any(d => ElderlyInfoChangeConstants.BankFieldKeys.Contains(d.FieldKey, StringComparer.OrdinalIgnoreCase)) == true;

    public bool IsPendingSelected => SelectedRecord?.IsPending == true;

    #endregion

    #region 生命周期

    public override async Task OnAppearingAsync()
    {
        await LoadPermissionAsync();
        await LoadDataAsync();
    }

    private async Task LoadPermissionAsync()
    {
        try
        {
            var userId = App.CurrentUserId ?? 0;
            if (userId <= 0) return;
            var permissions = await _permissionService.CheckPermissionsAsync(
                userId, new[] { PermissionCodes.ELDERLY_VIEW, PermissionCodes.ELDERLY_EDIT });
            CanView = permissions.TryGetValue(PermissionCodes.ELDERLY_VIEW, out var canView) && canView;
            CanReview = permissions.TryGetValue(PermissionCodes.ELDERLY_EDIT, out var canEdit) && canEdit;
        }
        catch (Exception ex)
        {
            CanView = false;
            CanReview = false;
            _logger.LogError(ex, "信息变更审批权限加载失败");
        }
    }

    protected override async Task LoadDataAsync()
    {
        if (!CanView)
        {
            Items.Clear();
            TotalCount = 0;
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _infoChangeService.GetPagedAsync(
                SearchText,
                GetStatusKey(SelectedStatus),
                PageIndex,
                PageSize,
                CancellationToken);

            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);

            Items.Clear();
            foreach (var record in result.Value.Items)
                Items.Add(record);
            TotalCount = result.Value.TotalCount;
            return Result.Success();
        }, "加载信息变更单...");
    }

    partial void OnSelectedStatusChanged(string value)
    {
        PageIndex = 1;
        SafeFireAndForget(LoadDataAsync);
    }

    partial void OnSelectedRecordChanged(ElderlyInfoChangeRecord? value)
    {
        OnPropertyChanged(nameof(ReviewHeader));
        OnPropertyChanged(nameof(ReviewSubHeader));
        OnPropertyChanged(nameof(ReviewReason));
        OnPropertyChanged(nameof(RequiresEffectiveMonth));
        OnPropertyChanged(nameof(IsPendingSelected));
    }

    #endregion

    #region 命令

    [RelayCommand]
    private void OpenReview(ElderlyInfoChangeRecord? record)
    {
        if (record == null) return;
        if (!CanView)
        {
            SafeFireAndForget(async () =>
                await _dialogService.DisplayAlertAsync("无权限", "当前账号没有查看信息变更的权限。", "确定"));
            return;
        }

        SelectedRecord = record;
        ReviewOpinion = string.Empty;
        EffectiveMonth = RequiresEffectiveMonth
            ? (string.IsNullOrWhiteSpace(record.EffectiveMonth)
                ? ElderlyInfoChangeConstants.GetDefaultEffectiveMonth(DateTime.Today)
                : record.EffectiveMonth!)
            : string.Empty;
        IsReviewPanelVisible = true;
    }

    [RelayCommand]
    private void CloseReview()
    {
        IsReviewPanelVisible = false;
        SelectedRecord = null;
        ReviewOpinion = string.Empty;
        EffectiveMonth = string.Empty;
    }

    [RelayCommand]
    private async Task ApproveAsync()
    {
        if (!CanReview)
        {
            await _dialogService.DisplayAlertAsync("无权限", "当前账号没有高龄编辑权限，不能审批信息变更。", "确定");
            return;
        }
        var record = SelectedRecord;
        if (record == null) return;
        if (!record.IsPending)
        {
            await _dialogService.DisplayAlertAsync("提示", "该变更单已处理，请刷新列表。", "确定");
            return;
        }
        if (RequiresEffectiveMonth
            && !ElderlyInfoChangeConstants.IsValidEffectiveMonth(EffectiveMonth?.Trim()))
        {
            await _dialogService.DisplayAlertAsync("提示", "银行卡变更请填写生效月（格式 YYYY-MM）。", "确定");
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认通过",
            $"确认通过 {record.Name} 的信息变更？\n变更单：{record.ChangeNo}\n{record.DetailsSummary}\n\n通过后将写入在享档案主表，认证任务快照不回溯。",
            "通过并写回", "取消");
        if (!confirm) return;

        var result = await _infoChangeService.ApproveAsync(
            record.Id,
            ReviewOpinion,
            string.IsNullOrWhiteSpace(EffectiveMonth) ? null : EffectiveMonth.Trim(),
            App.CurrentUserName,
            CancellationToken);

        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("审批失败", UserFriendlyMessages.Get(result.ErrorCode!, result.Message), "确定");
            return;
        }

        IsReviewPanelVisible = false;
        SelectedRecord = null;
        await LoadDataAsync();
        await _dialogService.ShowSnackBarAsync("信息变更已通过并写回档案");
    }

    [RelayCommand]
    private async Task RejectAsync()
    {
        if (!CanReview)
        {
            await _dialogService.DisplayAlertAsync("无权限", "当前账号没有高龄编辑权限，不能驳回信息变更。", "确定");
            return;
        }
        var record = SelectedRecord;
        if (record == null) return;
        if (!record.IsPending)
        {
            await _dialogService.DisplayAlertAsync("提示", "该变更单已处理，请刷新列表。", "确定");
            return;
        }
        if (string.IsNullOrWhiteSpace(ReviewOpinion))
        {
            await _dialogService.DisplayAlertAsync("提示", "驳回必须填写审批意见。", "确定");
            return;
        }

        var result = await _infoChangeService.RejectAsync(
            record.Id, ReviewOpinion, App.CurrentUserName, CancellationToken);
        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("驳回失败", UserFriendlyMessages.Get(result.ErrorCode!, result.Message), "确定");
            return;
        }

        IsReviewPanelVisible = false;
        SelectedRecord = null;
        await LoadDataAsync();
        await _dialogService.ShowSnackBarAsync("信息变更已驳回");
    }

    #endregion

    #region 工具

    private string? GetStatusKey(string label)
        => string.IsNullOrWhiteSpace(label) ? null : _statusLabelToKey.GetValueOrDefault(label);

    #endregion
}
