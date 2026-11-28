using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.ViewModels.ElderlyBenefits;

/// <summary>
/// 高龄信息变更表单：按认证任务打开，显示申请主表当前电话/地址/银行卡/代办人，
/// 填写新值与原因后提交 Pending 变更单；同人同字段的旧 Pending 由服务端置 Cancelled。
/// 权限 ELDERLY_EDIT。
/// </summary>
public partial class ElderlyInfoChangeFormViewModel : ViewModelBase
{
    private readonly IElderlyInfoChangeService _infoChangeService;
    private readonly IElderlyCertificationService _certificationService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly INewPermissionService _permissionService;

    private long _taskId;
    private ElderlyInfoChangeApplicationValues? _application;

    #region 抽象属性实现

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    #endregion

    #region 任务与当前值

    [ObservableProperty]
    private string _taskName = string.Empty;

    [ObservableProperty]
    private string _idCard = string.Empty;

    [ObservableProperty]
    private string _applicationStatusText = "加载中...";

    [ObservableProperty]
    private bool _canSubmit;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private string _pendingHint = string.Empty;

    [ObservableProperty]
    private bool _hasPendingChanges;

    [ObservableProperty]
    private string _currentPhone = string.Empty;

    [ObservableProperty]
    private string _currentFamilyAddress = string.Empty;

    [ObservableProperty]
    private string _currentDetailAddress = string.Empty;

    [ObservableProperty]
    private string _currentBankName = string.Empty;

    [ObservableProperty]
    private string _currentBankAccount = string.Empty;

    [ObservableProperty]
    private string _currentAgentName = string.Empty;

    [ObservableProperty]
    private string _currentAgentRelation = string.Empty;

    [ObservableProperty]
    private string _currentAgentReceiveName = string.Empty;

    [ObservableProperty]
    private string _currentAgentReceiveRelation = string.Empty;

    [ObservableProperty]
    private string _currentAgentReceiveBankName = string.Empty;

    [ObservableProperty]
    private string _currentAgentReceiveBankAccount = string.Empty;

    #endregion

    #region 新值输入

    [ObservableProperty]
    private string _newPhone = string.Empty;

    [ObservableProperty]
    private string _newFamilyAddress = string.Empty;

    [ObservableProperty]
    private string _newDetailAddress = string.Empty;

    [ObservableProperty]
    private string _newBankName = string.Empty;

    [ObservableProperty]
    private string _newBankAccount = string.Empty;

    [ObservableProperty]
    private string _newAgentName = string.Empty;

    [ObservableProperty]
    private string _newAgentRelation = string.Empty;

    [ObservableProperty]
    private string _newAgentReceiveName = string.Empty;

    [ObservableProperty]
    private string _newAgentReceiveRelation = string.Empty;

    [ObservableProperty]
    private string _newAgentReceiveBankName = string.Empty;

    [ObservableProperty]
    private string _newAgentReceiveBankAccount = string.Empty;

    [ObservableProperty]
    private string _reason = string.Empty;

    [ObservableProperty]
    private string _effectiveMonth = string.Empty;

    #endregion

    public ElderlyInfoChangeFormViewModel(
        IElderlyInfoChangeService infoChangeService,
        IElderlyCertificationService certificationService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        INewPermissionService permissionService)
    {
        _infoChangeService = infoChangeService;
        _certificationService = certificationService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _permissionService = permissionService;

        Title = "信息变更";
    }

    public void Initialize(long taskId) => _taskId = taskId;

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
                userId, new[] { PermissionCodes.ELDERLY_EDIT });
            CanSubmit = permissions.TryGetValue(PermissionCodes.ELDERLY_EDIT, out var canEdit) && canEdit;
        }
        catch (Exception ex)
        {
            CanSubmit = false;
            _logger.LogError(ex, "信息变更表单权限加载失败");
        }
    }

    private async Task LoadDataAsync()
    {
        if (_taskId <= 0)
        {
            ApplicationStatusText = "缺少认证任务参数";
            return;
        }

        await ExecuteAsync(async () =>
        {
            var ct = CancellationToken;

            var taskResult = await _certificationService.GetTaskAsync(_taskId, ct);
            if (taskResult.IsFailure)
                return Result.Failure(taskResult.ErrorCode!, taskResult.Message!);

            var task = taskResult.Value;
            if (task == null)
                return Result.Failure(ErrorCodes.NOT_FOUND, "认证任务不存在或已删除");

            TaskName = task.Name;
            IdCard = task.IdCard;

            var appResult = await _infoChangeService.GetApplicationValuesAsync(_taskId, ct);
            if (appResult.IsFailure)
                return Result.Failure(appResult.ErrorCode!, appResult.Message!);

            _application = appResult.Value;
            if (_application == null)
            {
                ApplicationStatusText = "未建档：该人员没有在享档案，请先补建档后再做信息变更";
                IsLoaded = true;
                CanSubmit = false;
                return Result.Success();
            }

            CurrentPhone = _application.Phone ?? string.Empty;
            CurrentFamilyAddress = _application.FamilyAddress ?? string.Empty;
            CurrentDetailAddress = _application.DetailAddress ?? string.Empty;
            CurrentBankName = _application.BankName ?? string.Empty;
            CurrentBankAccount = _application.BankAccount ?? string.Empty;
            CurrentAgentName = _application.AgentName ?? string.Empty;
            CurrentAgentRelation = _application.AgentRelation ?? string.Empty;
            CurrentAgentReceiveName = _application.AgentReceiveName ?? string.Empty;
            CurrentAgentReceiveRelation = _application.AgentReceiveRelation ?? string.Empty;
            CurrentAgentReceiveBankName = _application.AgentReceiveBankName ?? string.Empty;
            CurrentAgentReceiveBankAccount = _application.AgentReceiveBankAccount ?? string.Empty;

            ApplicationStatusText = string.Equals(_application.Status, ElderlyBenefitConstants.StatusConfirmed, StringComparison.Ordinal)
                ? "在享档案：已确认"
                : $"在享档案状态：{ElderlyBenefitConstants.GetStatusName(_application.Status)}（仅已确认档案可提交变更）";
            if (!string.Equals(_application.Status, ElderlyBenefitConstants.StatusConfirmed, StringComparison.Ordinal))
                CanSubmit = false;

            var changesResult = await _infoChangeService.GetByTaskAsync(_taskId, ct);
            if (changesResult.IsSuccess)
            {
                var pendingCount = changesResult.Value.Count(r => r.IsPending);
                HasPendingChanges = pendingCount > 0;
                PendingHint = pendingCount > 0
                    ? $"该人员已有 {pendingCount} 张待审批变更单；同字段再次提交会把旧单置为已作废。"
                    : string.Empty;
            }

            IsLoaded = true;
            return Result.Success();
        }, "加载当前信息...");
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (!CanSubmit)
        {
            await _dialogService.DisplayAlertAsync("无权限", "当前账号没有高龄编辑权限，不能提交信息变更。", "确定");
            return;
        }
        if (_application == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "该人员没有在享档案，请先补建档。", "确定");
            return;
        }

        var details = new List<ElderlyInfoChangeDetailInput>();
        AddChanged(details, ElderlyInfoChangeConstants.FieldPhone, CurrentPhone, NewPhone);
        AddChanged(details, ElderlyInfoChangeConstants.FieldFamilyAddress, CurrentFamilyAddress, NewFamilyAddress);
        AddChanged(details, ElderlyInfoChangeConstants.FieldDetailAddress, CurrentDetailAddress, NewDetailAddress);
        AddChanged(details, ElderlyInfoChangeConstants.FieldBankName, CurrentBankName, NewBankName);
        AddChanged(details, ElderlyInfoChangeConstants.FieldBankAccount, CurrentBankAccount, NewBankAccount);
        AddChanged(details, ElderlyInfoChangeConstants.FieldAgentName, CurrentAgentName, NewAgentName);
        AddChanged(details, ElderlyInfoChangeConstants.FieldAgentRelation, CurrentAgentRelation, NewAgentRelation);
        AddChanged(details, ElderlyInfoChangeConstants.FieldAgentReceiveName, CurrentAgentReceiveName, NewAgentReceiveName);
        AddChanged(details, ElderlyInfoChangeConstants.FieldAgentReceiveRelation, CurrentAgentReceiveRelation, NewAgentReceiveRelation);
        AddChanged(details, ElderlyInfoChangeConstants.FieldAgentReceiveBankName, CurrentAgentReceiveBankName, NewAgentReceiveBankName);
        AddChanged(details, ElderlyInfoChangeConstants.FieldAgentReceiveBankAccount, CurrentAgentReceiveBankAccount, NewAgentReceiveBankAccount);

        if (details.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "没有检测到修改内容。请填写要变更的新值。", "确定");
            return;
        }

        var hasHighRisk = details.Any(d => ElderlyInfoChangeConstants.IsHighRisk(d.FieldKey));
        var confirmMessage = hasHighRisk
            ? $"本次包含银行卡/代办人高风险字段（{details.Count} 项），提交后需审批复核。\n是否提交？"
            : $"将提交 {details.Count} 项信息变更，是否继续？";
        var confirm = await _dialogService.DisplayAlertAsync("提交信息变更", confirmMessage, "提交", "取消");
        if (!confirm) return;

        var result = await _infoChangeService.CreateAsync(
            _taskId,
            Reason,
            ElderlyInfoChangeConstants.SourceManual,
            null,
            null,
            string.IsNullOrWhiteSpace(EffectiveMonth) ? null : EffectiveMonth.Trim(),
            details,
            App.CurrentUserName,
            CancellationToken);

        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("提交失败", UserFriendlyMessages.Get(result.ErrorCode!, result.Message), "确定");
            return;
        }

        await _dialogService.DisplayAlertAsync("提交成功", "信息变更单已提交，等待审批通过后写入在享档案。", "确定");
        await GoBackAsync();
    }

    private static void AddChanged(
        List<ElderlyInfoChangeDetailInput> details,
        string fieldKey,
        string? currentValue,
        string? newValue)
    {
        var trimmed = newValue?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return;

        var current = currentValue?.Trim() ?? string.Empty;
        if (string.Equals(trimmed, current, StringComparison.Ordinal)) return;

        details.Add(new ElderlyInfoChangeDetailInput
        {
            FieldKey = fieldKey,
            OldValue = current,
            NewValue = trimmed
        });
    }
}
