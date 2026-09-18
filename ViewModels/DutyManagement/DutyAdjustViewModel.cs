using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Components;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.DutyManagement;

/// <summary>
/// 班务调整导航参数：从值班表点人名跳转时预填（Mode + 成员 + 日期）
/// </summary>
public record DutyAdjustNavParam
{
    /// <summary>操作模式常量</summary>
    public static class Modes
    {
        public const string Leave = "Leave";
        public const string Swap = "Swap";
        public const string Substitute = "Substitute";
    }

    /// <summary>操作模式：Modes.Leave / Swap / Substitute</summary>
    public string Mode { get; init; } = Modes.Leave;
    /// <summary>预选成员（请假人员 / 串班转出者 / 代班被代者）</summary>
    public long? MemberId { get; init; }
    /// <summary>预选班次日期（请假起始日 / 串班代班的班次日）</summary>
    public DateTime? DutyDate { get; init; }
    /// <summary>预选班次所属组（联动校验用）</summary>
    public string? GroupCode { get; init; }

    public DutyAdjustNavParam(string mode, long? memberId, DateTime? dutyDate, string? groupCode = null)
    {
        Mode = mode;
        MemberId = memberId;
        DutyDate = dutyDate;
        GroupCode = groupCode;
    }
}

/// <summary>
/// 班务调整管理 ViewModel：请假 / 串班（单向转让）/ 代班 的登记、发起与管理（列表+撤销）唯一完整界面。
/// 撤销后所在月值班表自动强制重生成恢复；请假登记后受影响月份自动顺延刷新。
/// </summary>
public partial class DutyAdjustViewModel : ViewModelBase
{
    private readonly IDutyService _dutyService = null!;
    private readonly INewPermissionService _permissionService = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IServiceProvider _serviceProvider = null!;

    public DutyAdjustViewModel(
        IDutyService dutyService,
        INewPermissionService permissionService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider)
    {
        _dutyService = dutyService;
        _permissionService = permissionService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;

        Title = "班务调整管理";
    }

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    #region 权限

    [ObservableProperty]
    private bool _canViewDuty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManageOpacity))]
    private bool _canManageDuty;

    public double ManageOpacity => CanManageDuty ? 1.0 : 0.5;

    public async Task InitializePermissionsAsync(int userId)
    {
        CanViewDuty = await _permissionService.HasPermissionAsync(userId, PermissionCodes.DUTY_VIEW);
        CanManageDuty = await _permissionService.HasPermissionAsync(userId, PermissionCodes.DUTY_MANAGE);
    }

    #endregion

    #region 数据

    public ObservableCollection<DutyLeaveView> LeaveRecords { get; } = new();

    /// <summary>串班记录（显示集合：随"显示已撤销"开关过滤）</summary>
    public ObservableCollection<DutyShiftChangeView> SwapRecords { get; } = new();

    /// <summary>代班记录（显示集合：随"显示已撤销"开关过滤）</summary>
    public ObservableCollection<DutyShiftChangeView> SubstituteRecords { get; } = new();

    private readonly List<DutyShiftChangeView> _allSwapChanges = new();
    private readonly List<DutyShiftChangeView> _allSubstituteChanges = new();

    /// <summary>全部成员（请假人员 / 串班转出者 / 代班被代者 选项）</summary>
    public ObservableCollection<DutyMemberView> MemberOptions { get; } = new();

    private List<DutyMemberView> _allMembers = new();

    /// <summary>是否显示已撤销的调整记录（默认隐藏，列表只看生效中）</summary>
    [ObservableProperty]
    private bool _showCancelled;

    partial void OnShowCancelledChanged(bool value) => ApplyRecordFilter();

    private void ApplyRecordFilter()
    {
        SwapRecords.Clear();
        foreach (var c in _allSwapChanges.Where(c => ShowCancelled || !c.IsCancelled))
            SwapRecords.Add(c);

        SubstituteRecords.Clear();
        foreach (var c in _allSubstituteChanges.Where(c => ShowCancelled || !c.IsCancelled))
            SubstituteRecords.Add(c);
    }

    #endregion

    #region SnackBar

    [ObservableProperty]
    private string _snackBarMessage = string.Empty;

    [ObservableProperty]
    private SnackBarType _snackBarType;

    [ObservableProperty]
    private bool _isSnackBarVisible;

    private int _snackBarVersion;

    private Task ShowSnackBarAsync(string message, SnackBarType type = SnackBarType.Success)
    {
        SnackBarMessage = message;
        SnackBarType = type;
        IsSnackBarVisible = true;

        var version = ++_snackBarVersion;
        _ = HideSnackBarAfterDelayAsync(version);
        return Task.CompletedTask;
    }

    private async Task HideSnackBarAfterDelayAsync(int version)
    {
        try
        {
            await Task.Delay(3000);
            if (version == _snackBarVersion)
            {
                IsSnackBarVisible = false;
            }
        }
        catch
        {
            // fire-and-forget：忽略异常
        }
    }

    #endregion

    #region 请假

    [ObservableProperty]
    private bool _isLeaveFormVisible;

    public string LeaveFormTitle => "新增请假";

    [ObservableProperty]
    private DutyMemberView? _selectedLeaveMember;

    [ObservableProperty]
    private DateTime _leaveStartDate = DateTime.Today;

    [ObservableProperty]
    private DateTime _leaveEndDate = DateTime.Today;

    [ObservableProperty]
    private string _leaveReason = string.Empty;

    /// <summary>补登记（开始日期早于今天）即时提醒</summary>
    [ObservableProperty]
    private bool _isBackfillHintVisible;

    [ObservableProperty]
    private string _backfillHintText = string.Empty;

    partial void OnLeaveStartDateChanged(DateTime value) => UpdateBackfillHint();

    private void UpdateBackfillHint()
    {
        if (LeaveStartDate.Date < DateTime.Today)
        {
            BackfillHintText = $"补登记：将删除 {LeaveStartDate:yyyy-MM} 起至最后已生成月的排班数据（需重新生成）；已打印的旧表需重打。";
            IsBackfillHintVisible = true;
        }
        else
        {
            IsBackfillHintVisible = false;
            BackfillHintText = string.Empty;
        }
    }

    [RelayCommand]
    private void StartAddLeave()
    {
        if (!EnsureManagePermission()) return;
        ResetLeaveForm();
        IsLeaveFormVisible = true;
    }

    [RelayCommand]
    private void CancelLeaveForm() => IsLeaveFormVisible = false;

    private void ResetLeaveForm()
    {
        SelectedLeaveMember = null;
        LeaveStartDate = DateTime.Today;
        LeaveEndDate = DateTime.Today;
        LeaveReason = string.Empty;
        UpdateBackfillHint();
    }

    [RelayCommand]
    private async Task SaveLeaveAsync()
    {
        if (!EnsureManagePermission()) return;
        if (SelectedLeaveMember is null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请选择请假人员", "确定");
            return;
        }
        if (LeaveEndDate.Date < LeaveStartDate.Date)
        {
            await _dialogService.DisplayAlertAsync("提示", "结束日期不能早于开始日期", "确定");
            return;
        }

        var isBackfill = LeaveStartDate.Date < DateTime.Today;
        if (isBackfill && string.IsNullOrWhiteSpace(LeaveReason))
        {
            await _dialogService.DisplayAlertAsync("提示", "补登记过去日期的请假必须填写事由", "确定");
            return;
        }

        // 受影响月份预览（删除范围：开始月 → 最后已生成月）
        var scopeText = "（当前没有已生成的值班表）";
        var scopeResult = await _dutyService.GetLeaveAffectedMonthsAsync(LeaveStartDate.Date, CancellationToken);
        if (scopeResult.IsSuccess && scopeResult.Value is { Count: > 0 })
            scopeText = string.Join("、", scopeResult.Value);

        if (isBackfill)
        {
            var confirm = await _dialogService.DisplayAlertAsync("补登记提醒",
                $"开始日期早于今天，将补登记并删除以下已生成月份的排班数据：\n{scopeText}\n\n" +
                "已登记的串班/代班将在重新生成时按记录重放（不匹配或命中请假的会跳过）；已打印的旧表需重新打印。\n是否继续？",
                "继续", "取消");
            if (!confirm) return;
        }

        var choice = await _dialogService.DisplayActionSheetAsync(
            $"登记后将删除排班：{scopeText}",
            "取消", null, "删除并立即重新生成", "仅删除（稍后手动）");
        if (choice is null || choice == "取消") return;
        var regenerateNow = choice == "删除并立即重新生成";

        var saved = false;
        var regenFailed = false;
        var deletedCount = 0;
        var regeneratedCount = 0;
        var memberName = SelectedLeaveMember.Name;
        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.SaveLeaveAsync(new DutyLeaveSave
            {
                MemberId = SelectedLeaveMember.Id,
                StartDate = LeaveStartDate.Date,
                EndDate = LeaveEndDate.Date,
                Reason = LeaveReason?.Trim() ?? string.Empty,
            }, ct);

            saved = result.IsSuccess;
            if (!saved)
            {
                await ShowFailureAsync(result, "登记请假");
                return;
            }

            deletedCount = result.Value?.Count ?? 0;
            if (regenerateNow && deletedCount > 0)
            {
                var genResult = await _dutyService.GenerateMissingMonthsAsync(result.Value!, ct);
                if (genResult.IsFailure)
                {
                    regenFailed = true;
                    await ShowFailureAsync(genResult, "补齐生成");
                }
                else
                {
                    regeneratedCount = genResult.Value;
                }
            }
        }, "正在登记请假...");

        if (!saved) return;

        IsLeaveFormVisible = false;
        await LoadAdjustmentsAsync();

        if (regenerateNow && !regenFailed)
            await ShowSnackBarAsync($"已登记（{memberName}）并重新生成 {regeneratedCount} 个月份，串班/代班已自动生效");
        else if (regenFailed)
            await ShowSnackBarAsync($"已登记（{memberName}），但重新生成失败，请到值班表页手动生成");
        else
            await ShowSnackBarAsync(isBackfill
                ? $"已补登记（{memberName}），已删除 {deletedCount} 个月份排班，请到值班表页重新生成"
                : $"请假已登记（{memberName}），已删除 {deletedCount} 个月份排班，请到值班表页重新生成");
    }

    /// <summary>撤销请假（删除记录并联动删除受影响月份，可选择立即重新生成）</summary>
    [RelayCommand]
    private async Task DeleteLeaveAsync(DutyLeaveView? leave)
    {
        if (!EnsureManagePermission() || leave is null) return;

        var confirmed = await _dialogService.DisplayAlertAsync(
            "撤销请假",
            $"确定撤销 {leave.MemberName} 的请假（{leave.RangeDisplay}）吗？\n撤销后将删除受影响月份的排班数据。",
            "撤销", "取消");
        if (!confirmed) return;

        var choice = await _dialogService.DisplayActionSheetAsync(
            "请选择后续操作", "取消", null, "删除并立即重新生成", "仅删除（稍后手动）");
        if (choice is null || choice == "取消") return;
        var regenerateNow = choice == "删除并立即重新生成";

        var deleted = false;
        var regenFailed = false;
        var deletedCount = 0;
        var regeneratedCount = 0;
        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.DeleteLeaveAsync(leave.Id, ct);
            if (result.IsFailure)
            {
                await ShowFailureAsync(result, "撤销请假");
                return;
            }

            deleted = true;
            deletedCount = result.Value?.Count ?? 0;
            if (regenerateNow && deletedCount > 0)
            {
                var genResult = await _dutyService.GenerateMissingMonthsAsync(result.Value!, ct);
                if (genResult.IsFailure)
                {
                    regenFailed = true;
                    await ShowFailureAsync(genResult, "补齐生成");
                }
                else
                {
                    regeneratedCount = genResult.Value;
                }
            }
        });

        if (!deleted) return;

        await LoadAdjustmentsAsync();
        if (regenerateNow && !regenFailed)
            await ShowSnackBarAsync($"请假已撤销并重新生成 {regeneratedCount} 个月份");
        else if (regenFailed)
            await ShowSnackBarAsync("请假已撤销，但重新生成失败，请到值班表页手动生成");
        else
            await ShowSnackBarAsync($"请假已撤销，已删除 {deletedCount} 个月份排班，请到值班表页重新生成");
    }

    #endregion

    #region 串班（相互对调）

    [ObservableProperty]
    private bool _isSwapFormVisible;

    public string SwapFormTitle => "新增串班（相互对调）";

    /// <summary>甲方（对调方一）</summary>
    [ObservableProperty]
    private DutyMemberView? _selectedSwapFromMember;

    /// <summary>甲方的班次列表（选 X 日）</summary>
    public ObservableCollection<DutyFutureSchedule> SwapFutureSchedules { get; } = new();

    [ObservableProperty]
    private DutyFutureSchedule? _selectedSwapSchedule;

    /// <summary>乙方（对调方二）</summary>
    [ObservableProperty]
    private DutyMemberView? _selectedSwapToMember;

    /// <summary>乙方的班次列表（选 Y 日）</summary>
    public ObservableCollection<DutyFutureSchedule> SwapToFutureSchedules { get; } = new();

    [ObservableProperty]
    private DutyFutureSchedule? _selectedSwapToSchedule;

    [ObservableProperty]
    private string _swapReason = string.Empty;

    partial void OnSelectedSwapFromMemberChanged(DutyMemberView? value) => _ = LoadFutureSchedulesAsync(
        value, SwapFutureSchedules, () => { SelectedSwapSchedule = null; RefreshSwapToSchedules(); });

    partial void OnSelectedSwapScheduleChanged(DutyFutureSchedule? value)
    {
        // 甲方班次确定后，乙方候选=同组其他启用成员
        SwapTargetOptions.Clear();
        SelectedSwapToMember = null;
        if (value is null) return;
        var targets = _allMembers
            .Where(m => m.IsActive && m.Id != value.MemberId)
            .Where(m => m.Groups.Any(g => g.GroupCode == value.GroupCode))
            .OrderBy(m => m.Name);
        foreach (var t in targets)
        {
            SwapTargetOptions.Add(t);
        }
    }

    partial void OnSelectedSwapToMemberChanged(DutyMemberView? value) => _ = LoadFutureSchedulesAsync(
        value, SwapToFutureSchedules, () => SelectedSwapToSchedule = null);

    partial void OnSelectedSwapToScheduleChanged(DutyFutureSchedule? value)
    {
        // 双方不可选同一天（X≠Y）
        if (value != null && SelectedSwapSchedule != null && value.DutyDate == SelectedSwapSchedule.DutyDate)
        {
            _ = ShowSnackBarAsync("串班的两个班次不能是同一天，请重新选择乙方的班次", SnackBarType.Warning);
        }
    }

    /// <summary>乙方候选（同组其他启用成员，排除甲方）</summary>
    public ObservableCollection<DutyMemberView> SwapTargetOptions { get; } = new();

    [RelayCommand]
    private void StartAddSwap()
    {
        if (!EnsureManagePermission()) return;
        ResetSwapForm();
        IsSwapFormVisible = true;
    }

    [RelayCommand]
    private void CancelSwapForm() => IsSwapFormVisible = false;

    private void ResetSwapForm()
    {
        SelectedSwapFromMember = null;
        SelectedSwapSchedule = null;
        SelectedSwapToMember = null;
        SelectedSwapToSchedule = null;
        SwapReason = string.Empty;
        SwapFutureSchedules.Clear();
        SwapToFutureSchedules.Clear();
        SwapTargetOptions.Clear();
    }

    /// <summary>乙方班次列表随乙方选择联动刷新（并保持组与甲方班次一致）</summary>
    private void RefreshSwapToSchedules()
    {
        SwapToFutureSchedules.Clear();
        SelectedSwapToSchedule = null;
    }

    [RelayCommand]
    private async Task SaveShiftSwapAsync()
    {
        if (!EnsureManagePermission()) return;
        if (SelectedSwapFromMember is null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请选择甲方（对调方一）", "确定");
            return;
        }
        if (SelectedSwapSchedule is null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请选择甲方的班次", "确定");
            return;
        }
        if (SelectedSwapToMember is null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请选择乙方（对调方二，同组成员）", "确定");
            return;
        }
        if (SelectedSwapToSchedule is null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请选择乙方的班次（要与甲方对调的班日）", "确定");
            return;
        }

        var saved = false;
        var summary = $"{SelectedSwapFromMember.Name}({SelectedSwapSchedule.DutyDate:MM-dd}) ↔ {SelectedSwapToMember.Name}({SelectedSwapToSchedule.DutyDate:MM-dd})";
        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.SaveShiftChangeAsync(new DutyShiftChangeSave
            {
                ChangeType = DutyConstants.ChangeTypes.SWAP,
                FromMemberId = SelectedSwapFromMember.Id,
                DutyDate = SelectedSwapSchedule.DutyDate,
                ToMemberId = SelectedSwapToMember.Id,
                ToDutyDate = SelectedSwapToSchedule.DutyDate,
                Reason = SwapReason?.Trim() ?? string.Empty,
            }, ct);

            saved = result.IsSuccess;
            if (result.IsSuccess)
            {
                await ShowSnackBarAsync($"串班已生效：{summary}");
            }
            else
            {
                await ShowFailureAsync(result, "保存串班");
            }
        }, "正在保存串班...");

        if (saved)
        {
            IsSwapFormVisible = false;
            await LoadAdjustmentsAsync();
        }
    }

    #endregion

    #region 代班

    [ObservableProperty]
    private bool _isSubstituteFormVisible;

    public string SubstituteFormTitle => "新增代班";

    [ObservableProperty]
    private DutyMemberView? _selectedSubstituteFromMember;

    public ObservableCollection<DutyFutureSchedule> SubstituteFutureSchedules { get; } = new();

    [ObservableProperty]
    private DutyFutureSchedule? _selectedSubstituteSchedule;

    [ObservableProperty]
    private DutyMemberView? _selectedSubstituteMember;

    public ObservableCollection<DutyMemberView> SubstituteTargetOptions { get; } = new();

    [ObservableProperty]
    private string _substituteReason = string.Empty;

    partial void OnSelectedSubstituteFromMemberChanged(DutyMemberView? value) => _ = LoadFutureSchedulesAsync(
        value, SubstituteFutureSchedules, () => { SelectedSubstituteSchedule = null; SelectedSubstituteMember = null; SubstituteTargetOptions.Clear(); });

    partial void OnSelectedSubstituteScheduleChanged(DutyFutureSchedule? value) =>
        RefreshShiftTargets(SubstituteTargetOptions, () => SelectedSubstituteMember = null, value);

    [RelayCommand]
    private void StartAddSubstitute()
    {
        if (!EnsureManagePermission()) return;
        ResetSubstituteForm();
        IsSubstituteFormVisible = true;
    }

    [RelayCommand]
    private void CancelSubstituteForm() => IsSubstituteFormVisible = false;

    private void ResetSubstituteForm()
    {
        SelectedSubstituteFromMember = null;
        SelectedSubstituteSchedule = null;
        SelectedSubstituteMember = null;
        SubstituteReason = string.Empty;
        SubstituteFutureSchedules.Clear();
        SubstituteTargetOptions.Clear();
    }

    [RelayCommand]
    private async Task SaveSubstituteAsync()
    {
        if (!EnsureManagePermission()) return;
        if (SelectedSubstituteFromMember is null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请选择被代班人（让出班次的人员）", "确定");
            return;
        }
        if (SelectedSubstituteSchedule is null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请选择要代班的班次", "确定");
            return;
        }
        if (SelectedSubstituteMember is null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请选择代班人（同组成员）", "确定");
            return;
        }

        var saved = false;
        var summary = $"{SelectedSubstituteMember.Name} 代 {SelectedSubstituteFromMember.Name}（{SelectedSubstituteSchedule.DutyDate:yyyy-MM-dd}）";
        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.SaveShiftChangeAsync(new DutyShiftChangeSave
            {
                ChangeType = DutyConstants.ChangeTypes.SUBSTITUTE,
                FromMemberId = SelectedSubstituteFromMember.Id,
                DutyDate = SelectedSubstituteSchedule.DutyDate,
                ToMemberId = SelectedSubstituteMember.Id,
                Reason = SubstituteReason?.Trim() ?? string.Empty,
            }, ct);

            saved = result.IsSuccess;
            if (result.IsSuccess)
            {
                await ShowSnackBarAsync($"代班已生效：{summary}");
            }
            else
            {
                await ShowFailureAsync(result, "保存代班");
            }
        }, "正在保存代班...");

        if (saved)
        {
            IsSubstituteFormVisible = false;
            await LoadAdjustmentsAsync();
        }
    }

    #endregion

    #region 串班/代班撤销与改期

    /// <summary>撤销串班（标记已撤销并强制重生成所在月，班次恢复）</summary>
    [RelayCommand]
    private async Task CancelSwapAsync(DutyShiftChangeView? change)
    {
        if (!EnsureManagePermission() || change is null) return;
        await CancelChangeCoreAsync(change);
    }

    /// <summary>撤销代班</summary>
    [RelayCommand]
    private async Task CancelSubstituteAsync(DutyShiftChangeView? change)
    {
        if (!EnsureManagePermission() || change is null) return;
        await CancelChangeCoreAsync(change);
    }

    private async Task CancelChangeCoreAsync(DutyShiftChangeView change)
    {
        var confirmed = await _dialogService.DisplayAlertAsync(
            $"撤销{change.ChangeTypeName}",
            $"确定撤销 {change.DutyDate:yyyy-MM-dd} 的{change.ChangeTypeName}（{change.FromMemberName} → {change.ToMemberName}）吗？\n撤销后所在月值班表将自动重排恢复。",
            "撤销", "取消");
        if (!confirmed) return;

        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.CancelShiftChangeAsync(change.Id, ct);
            if (result.IsFailure)
            {
                await ShowFailureAsync(result, $"撤销{change.ChangeTypeName}");
                return;
            }

            await ShowSnackBarAsync($"{change.ChangeTypeName}已撤销，值班表已重排恢复");
            await LoadAdjustmentsAsync();
        });
    }

    // ── 改期面板（串班改乙方班日 / 代班改被代班日；一条记录始终，无撤销+重建的重复） ──

    [ObservableProperty]
    private bool _isReschedulePanelVisible;

    [ObservableProperty]
    private string _reschedulePanelTitle = string.Empty;

    [ObservableProperty]
    private DutyShiftChangeView? _selectedRescheduleChange;

    [ObservableProperty]
    private DateTime _rescheduleNewDate = DateTime.Today;

    /// <summary>改期目标端提示（串班=乙方的班日/代班=被代班日）</summary>
    public string RescheduleDateLabel => SelectedRescheduleChange?.ChangeType == DutyConstants.ChangeTypes.SWAP
        ? "乙方的新班日"
        : "被代的新班日";

    partial void OnSelectedRescheduleChangeChanged(DutyShiftChangeView? value)
    {
        if (value is null) return;
        ReschedulePanelTitle = $"改期：{value.ChangeDirection}";
        RescheduleNewDate = value.ChangeType == DutyConstants.ChangeTypes.SWAP
            ? (value.ToDutyDate ?? DateTime.Today)
            : value.DutyDate;
        OnPropertyChanged(nameof(RescheduleDateLabel));
    }

    /// <summary>打开改期面板（仅生效中记录）</summary>
    [RelayCommand]
    private void StartRescheduleAsync(DutyShiftChangeView? change)
    {
        if (!EnsureManagePermission() || change is null) return;
        if (change.IsCancelled)
        {
            _ = ShowSnackBarAsync("已撤销的记录不能改期", SnackBarType.Warning);
            return;
        }
        SelectedRescheduleChange = change;
        IsReschedulePanelVisible = true;
    }

    [RelayCommand]
    private void CancelRescheduleForm() => IsReschedulePanelVisible = false;

    /// <summary>保存改期（服务端校验新日期班次/占用/冲突后重生成新旧月份，重放自动应用）</summary>
    [RelayCommand]
    private async Task SaveRescheduleAsync()
    {
        if (!EnsureManagePermission() || SelectedRescheduleChange is null) return;

        var saved = false;
        var summary = SelectedRescheduleChange.ChangeDirection;
        var typeName = SelectedRescheduleChange.ChangeTypeName;
        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.RescheduleShiftChangeAsync(
                SelectedRescheduleChange.Id, RescheduleNewDate.Date, ct);

            saved = result.IsSuccess;
            if (result.IsSuccess)
            {
                await ShowSnackBarAsync($"{typeName}已改期（{summary}）");
            }
            else
            {
                await ShowFailureAsync(result, $"改期{typeName}");
            }
        }, $"正在改期{typeName}...");

        if (saved)
        {
            IsReschedulePanelVisible = false;
            await LoadAdjustmentsAsync();
        }
    }

    #endregion

    #region 通用

    private bool EnsureManagePermission()
    {
        if (CanManageDuty) return true;
        _ = ShowSnackBarAsync("您没有管理值班表的权限", SnackBarType.Warning);
        return false;
    }

    /// <summary>加载某成员今天及以后的班次（串班/代班表单选班列表）</summary>
    private async Task LoadFutureSchedulesAsync(
        DutyMemberView? member, ObservableCollection<DutyFutureSchedule> target, Action resetSelections)
    {
        target.Clear();
        resetSelections();
        if (member is null) return;

        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.GetMemberFutureSchedulesAsync(member.Id, ct);
            if (result.IsFailure)
            {
                await ShowFailureAsync(result, "加载未来班次");
                return;
            }
            foreach (var s in result.Value ?? new List<DutyFutureSchedule>())
            {
                target.Add(s);
            }
        }, "正在加载未来班次...");
    }

    /// <summary>按所选班次的组刷新顶班人候选（同组其他启用成员，排除原班人员）</summary>
    private void RefreshShiftTargets(ObservableCollection<DutyMemberView> options, Action resetSelection, DutyFutureSchedule? schedule)
    {
        options.Clear();
        resetSelection();
        if (schedule is null) return;

        var fromId = schedule.MemberId;
        var targets = _allMembers
            .Where(m => m.IsActive && m.Id != fromId)
            .Where(m => m.Groups.Any(g => g.GroupCode == schedule.GroupCode))
            .OrderBy(m => m.Name);
        foreach (var t in targets)
        {
            options.Add(t);
        }
    }

    /// <summary>加载列表数据与成员选项（页面出现/操作后刷新）</summary>
    [RelayCommand]
    private async Task LoadAdjustmentsAsync()
    {
        await ExecuteAsync(async ct =>
        {
            var leavesResult = await _dutyService.GetLeavesAsync(null, ct);
            if (leavesResult.IsFailure)
            {
                await ShowFailureAsync(leavesResult, "加载请假记录");
                return;
            }

            var changesResult = await _dutyService.GetShiftChangesAsync(null, ct);
            if (changesResult.IsFailure)
            {
                await ShowFailureAsync(changesResult, "加载班务调整记录");
                return;
            }

            var membersResult = await _dutyService.GetMembersAsync(ct);
            if (membersResult.IsFailure)
            {
                await ShowFailureAsync(membersResult, "加载成员");
                return;
            }

            LeaveRecords.Clear();
            foreach (var l in leavesResult.Value ?? new List<DutyLeaveView>())
                LeaveRecords.Add(l);

            _allSwapChanges.Clear();
            _allSubstituteChanges.Clear();
            foreach (var c in changesResult.Value ?? new List<DutyShiftChangeView>())
            {
                if (c.ChangeType == DutyConstants.ChangeTypes.SWAP)
                    _allSwapChanges.Add(c);
                else
                    _allSubstituteChanges.Add(c);
            }
            ApplyRecordFilter();

            _allMembers = membersResult.Value ?? new List<DutyMemberView>();
            MemberOptions.Clear();
            foreach (var m in _allMembers.OrderBy(m => m.Name))
                MemberOptions.Add(m);
        });
    }

    #endregion

    #region 导航参数预填（值班表点人名跳转）

    /// <summary>待处理的跳转参数（SetParameterAsync 在页面出现前调用，延后到 OnAppearingAsync 处理）</summary>
    private DutyAdjustNavParam? _pendingParam;

    public void SetNavigationParameter(DutyAdjustNavParam parameter) => _pendingParam = parameter;

    private async Task ApplyPendingParameterAsync()
    {
        var param = _pendingParam;
        _pendingParam = null;
        if (param is null) return;

        var member = param.MemberId is null
            ? null
            : MemberOptions.FirstOrDefault(m => m.Id == param.MemberId.Value);

        switch (param.Mode)
        {
            case DutyAdjustNavParam.Modes.Leave:
                ResetLeaveForm();
                SelectedLeaveMember = member;
                if (param.DutyDate.HasValue && param.DutyDate.Value > LeaveStartDate)
                    LeaveStartDate = param.DutyDate.Value;
                if (LeaveEndDate < LeaveStartDate)
                    LeaveEndDate = LeaveStartDate;
                IsLeaveFormVisible = CanManageDuty;
                break;

            case DutyAdjustNavParam.Modes.Swap:
                ResetSwapForm();
                SelectedSwapFromMember = member;
                IsSwapFormVisible = CanManageDuty;
                break;

            case DutyAdjustNavParam.Modes.Substitute:
                ResetSubstituteForm();
                SelectedSubstituteFromMember = member;
                IsSubstituteFormVisible = CanManageDuty;
                break;
        }
    }

    #endregion

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await LoadAdjustmentsAsync();
        await ApplyPendingParameterAsync();
    }
}
