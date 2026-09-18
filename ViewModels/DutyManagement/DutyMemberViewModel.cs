using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Components;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.Platform;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.DutyManagement;

/// <summary>
/// 值班成员管理 ViewModel：四组成员名单维护（增删改、启停、组内顺序、多组归属、批量导入）
/// </summary>
public partial class DutyMemberViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IDutyService _dutyService = null!;
    private readonly INewPermissionService _permissionService = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly ILoggerService _logger = null!;

    public DutyMemberViewModel(
        IServiceProvider serviceProvider,
        IDutyService dutyService,
        INewPermissionService permissionService,
        IDialogService dialogService,
        ILoggerService logger)
    {
        _serviceProvider = serviceProvider;
        _dutyService = dutyService;
        _permissionService = permissionService;
        _dialogService = dialogService;
        _logger = logger;

        Title = "值班成员管理";

        // 日志区显隐联动（HasImportLog）
        ImportLogLines.CollectionChanged += (_, __) => OnPropertyChanged(nameof(HasImportLog));
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

    /// <summary>全部成员（不随筛选变化，筛选视图由 FilteredMembers 派生）</summary>
    private List<DutyMemberView> _allMembers = new();

    public ObservableCollection<DutyMemberView> FilteredMembers { get; } = new();

    /// <summary>组筛选选项（显示名）：全部 + 四个组</summary>
    public List<string> GroupFilterOptions { get; } = new List<string>
    {
        "全部成员",
        DutyConstants.Groups.LEADER,
        DutyConstants.Groups.MIDDLE,
        DutyConstants.Groups.MALE,
        DutyConstants.Groups.FEMALE,
    }.Select(DutyConstants.Groups.DisplayName).ToList();

    [ObservableProperty]
    private string _selectedGroupFilter = "全部成员";

    partial void OnSelectedGroupFilterChanged(string value) => ApplyFilter();

    /// <summary>筛选显示名 → 组编码（"全部成员" → 空串）</summary>
    private static string FilterToGroupCode(string display) => display switch
    {
        "领导组" => DutyConstants.Groups.LEADER,
        "中层管理组" => DutyConstants.Groups.MIDDLE,
        "男生组" => DutyConstants.Groups.MALE,
        "女生组" => DutyConstants.Groups.FEMALE,
        _ => string.Empty
    };

    /// <summary>统计文本：各组启用人数</summary>
    public string GroupSummaryText
    {
        get
        {
            var active = _allMembers.Where(m => m.IsActive).ToList();
            return $"共 {_allMembers.Count} 人（启用 {active.Count}）｜ " +
                   $"领导 {CountIn(active, DutyConstants.Groups.LEADER)} · " +
                   $"中层 {CountIn(active, DutyConstants.Groups.MIDDLE)} · " +
                   $"男 {CountIn(active, DutyConstants.Groups.MALE)} · " +
                   $"女 {CountIn(active, DutyConstants.Groups.FEMALE)}";
        }
    }

    private static int CountIn(IEnumerable<DutyMemberView> members, string group) =>
        members.Count(m => m.InGroup(group));

    #endregion

    #region 编辑区（内联）

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isAdding;

    [ObservableProperty]
    private long _editId;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editPhone = string.Empty;

    [ObservableProperty]
    private string _editRemark = string.Empty;

    [ObservableProperty]
    private bool _editIsActive = true;

    /// <summary>是否设置入职时间（勾选后生效，日期默认今日）</summary>
    [ObservableProperty]
    private bool _editHasJoinDate;

    [ObservableProperty]
    private DateTime _editJoinDate = DateTime.Today;

    /// <summary>是否设置离职时间（勾选后生效，日期默认今日）</summary>
    [ObservableProperty]
    private bool _editHasExitDate;

    [ObservableProperty]
    private DateTime _editExitDate = DateTime.Today;

    [ObservableProperty]
    private bool _editInLeader;

    [ObservableProperty]
    private bool _editInMiddle;

    [ObservableProperty]
    private bool _editInMale;

    [ObservableProperty]
    private bool _editInFemale;

    [ObservableProperty]
    private DutyMemberView _selectedMember = null!;

    #endregion

    #region 请假面板

    [ObservableProperty]
    private bool _isLeavePanelVisible;

    [ObservableProperty]
    private string _leavePanelTitle = string.Empty;

    /// <summary>请假面板当前成员</summary>
    private DutyMemberView? _leavePanelMember;

    public ObservableCollection<DutyLeaveView> LeaveRecords { get; } = new();

    /// <summary>新增请假表单（日期默认今日）</summary>
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

    #region 命令

    [RelayCommand]
    private async Task LoadMembersAsync()
    {
        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.GetMembersAsync(ct);
            if (result.IsFailure)
            {
                await ShowFailureAsync(result, "加载成员");
                return;
            }

            _allMembers = result.Value ?? new List<DutyMemberView>();
            ApplyFilter();
            OnPropertyChanged(nameof(GroupSummaryText));
        }, "正在加载成员...");
    }

    private void ApplyFilter()
    {
        FilteredMembers.Clear();
        var groupCode = FilterToGroupCode(SelectedGroupFilter);
        var source = string.IsNullOrEmpty(groupCode)
            ? _allMembers
            : _allMembers.Where(m => m.InGroup(groupCode));
        foreach (var member in source)
        {
            FilteredMembers.Add(member);
        }
    }

    [RelayCommand]
    private void StartAddMember()
    {
        if (!CanManageDuty) return;

        IsAdding = true;
        IsEditing = true;
        EditId = 0;
        EditName = string.Empty;
        EditPhone = string.Empty;
        EditRemark = string.Empty;
        EditIsActive = true;
        EditHasJoinDate = false;
        EditJoinDate = DateTime.Today;
        EditHasExitDate = false;
        EditExitDate = DateTime.Today;
        EditInLeader = false;
        EditInMiddle = false;
        EditInMale = false;
        EditInFemale = false;
    }

    [RelayCommand]
    private void StartEditMember(DutyMemberView? member)
    {
        if (!CanManageDuty || member == null) return;

        IsAdding = false;
        IsEditing = true;
        EditId = member.Id;
        EditName = member.Name;
        EditPhone = member.Phone ?? string.Empty;
        EditRemark = member.Remark ?? string.Empty;
        EditIsActive = member.IsActive;
        EditHasJoinDate = member.JoinDate.HasValue;
        EditJoinDate = member.JoinDate ?? DateTime.Today;
        EditHasExitDate = member.ExitDate.HasValue;
        EditExitDate = member.ExitDate ?? DateTime.Today;
        EditInLeader = member.InGroup(DutyConstants.Groups.LEADER);
        EditInMiddle = member.InGroup(DutyConstants.Groups.MIDDLE);
        EditInMale = member.InGroup(DutyConstants.Groups.MALE);
        EditInFemale = member.InGroup(DutyConstants.Groups.FEMALE);
    }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    [RelayCommand]
    private async Task SaveMemberAsync()
    {
        if (!CanManageDuty || !IsEditing) return;

        if (string.IsNullOrWhiteSpace(EditName))
        {
            await _dialogService.DisplayAlertAsync("提示", "姓名不能为空", "确定");
            return;
        }

        var groupCodes = new List<string>();
        if (EditInLeader) groupCodes.Add(DutyConstants.Groups.LEADER);
        if (EditInMiddle) groupCodes.Add(DutyConstants.Groups.MIDDLE);
        if (EditInMale) groupCodes.Add(DutyConstants.Groups.MALE);
        if (EditInFemale) groupCodes.Add(DutyConstants.Groups.FEMALE);

        if (groupCodes.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "请至少选择一个所属组", "确定");
            return;
        }

        var saved = false;
        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.SaveMemberAsync(new DutyMemberSave
            {
                Id = EditId,
                Name = EditName.Trim(),
                Phone = EditPhone?.Trim() ?? string.Empty,
                IsActive = EditIsActive,
                JoinDate = EditHasJoinDate ? EditJoinDate.Date : null,
                ExitDate = EditHasExitDate ? EditExitDate.Date : null,
                Remark = EditRemark?.Trim() ?? string.Empty,
                GroupCodes = groupCodes
            }, ct);

            saved = result.IsSuccess;
            if (result.IsSuccess)
            {
                IsEditing = false;
                await ShowSnackBarAsync(IsAdding ? "成员已添加" : "成员已保存");
            }
            else
            {
                await ShowFailureAsync(result, "保存成员");
            }
        }, "正在保存成员...");

        if (saved)
        {
            await LoadMembersAsync();
        }
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(DutyMemberView? member)
    {
        if (!CanManageDuty || member == null) return;

        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.SetMemberActiveAsync(member.Id, !member.IsActive, ct);
            if (!result.IsSuccess)
            {
                await ShowFailureAsync(result, "切换状态");
            }
        }, "正在切换状态...");

        await LoadMembersAsync();
    }

    [RelayCommand]
    private async Task DeleteMemberAsync(DutyMemberView? member)
    {
        if (!CanManageDuty || member == null) return;

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认删除",
            $"确定要删除成员 \"{member.Name}\" 吗？\n（其分组记录将一并删除，已生成班次中的姓名快照保留）",
            "删除", "取消");
        if (!confirm) return;

        var deleted = false;
        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.DeleteMemberAsync(member.Id, ct);
            deleted = result.IsSuccess;
            if (!result.IsSuccess)
            {
                await ShowFailureAsync(result, "删除成员");
            }
        }, "正在删除成员...");

        if (deleted)
        {
            await ShowSnackBarAsync("成员已删除");
            await LoadMembersAsync();
        }
    }

    /// <summary>
    /// 组内上移。目标组：当前筛选组；"全部成员"时取成员的第一个所属组。
    /// </summary>
    [RelayCommand]
    private async Task MoveUpMemberAsync(DutyMemberView? member)
    {
        await MoveMemberInGroupAsync(member, -1);
    }

    /// <summary>组内下移（目标组规则同上）</summary>
    [RelayCommand]
    private async Task MoveDownMemberAsync(DutyMemberView? member)
    {
        await MoveMemberInGroupAsync(member, 1);
    }

    private async Task MoveMemberInGroupAsync(DutyMemberView? member, int direction)
    {
        if (!CanManageDuty || member == null) return;

        var groupCode = FilterToGroupCode(SelectedGroupFilter);
        if (string.IsNullOrEmpty(groupCode))
            groupCode = member.Groups.OrderBy(g => g.SortOrder).FirstOrDefault()?.GroupCode ?? string.Empty;

        if (string.IsNullOrEmpty(groupCode)) return;

        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.MoveMemberAsync(member.Id, groupCode, direction, ct);
            if (!result.IsSuccess)
            {
                await ShowFailureAsync(result, "调整顺序");
            }
        }, "正在调整顺序...");

        await LoadMembersAsync();
    }

    #region 请假管理

    /// <summary>打开成员请假面板（加载该成员请假记录）</summary>
    [RelayCommand]
    private async Task OpenLeavePanelAsync(DutyMemberView? member)
    {
        if (member == null) return;

        if (!CanManageDuty)
        {
            await _dialogService.DisplayAlertAsync("提示", "您没有管理值班成员的权限", "确定");
            return;
        }

        _leavePanelMember = member;
        LeavePanelTitle = $"{member.Name} 的请假记录";
        LeaveStartDate = DateTime.Today;
        LeaveEndDate = DateTime.Today;
        LeaveReason = string.Empty;
        UpdateBackfillHint();

        await LoadLeaveRecordsAsync(member.Id);
        IsLeavePanelVisible = true;
    }

    [RelayCommand]
    private void CloseLeavePanel() => IsLeavePanelVisible = false;

    private async Task LoadLeaveRecordsAsync(long memberId)
    {
        LeaveRecords.Clear();
        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.GetLeavesAsync(memberId, ct);
            if (result.IsSuccess)
            {
                foreach (var leave in result.Value ?? new List<DutyLeaveView>())
                {
                    LeaveRecords.Add(leave);
                }
            }
            else
            {
                await ShowFailureAsync(result, "加载请假记录");
            }
        }, "正在加载请假记录...");
    }

    [RelayCommand]
    private async Task SaveLeaveAsync()
    {
        if (!CanManageDuty || _leavePanelMember == null) return;

        if (LeaveEndDate.Date < LeaveStartDate.Date)
        {
            await _dialogService.DisplayAlertAsync("提示", "请假结束日期不能早于开始日期", "确定");
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
        var memberName = _leavePanelMember.Name;
        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.SaveLeaveAsync(new DutyLeaveSave
            {
                MemberId = _leavePanelMember.Id,
                StartDate = LeaveStartDate.Date,
                EndDate = LeaveEndDate.Date,
                Reason = LeaveReason?.Trim() ?? string.Empty
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

        LeaveStartDate = DateTime.Today;
        LeaveEndDate = DateTime.Today;
        LeaveReason = string.Empty;
        UpdateBackfillHint();
        await LoadLeaveRecordsAsync(_leavePanelMember.Id);

        if (regenerateNow && !regenFailed)
            await ShowSnackBarAsync($"已登记（{memberName}）并重新生成 {regeneratedCount} 个月份，串班/代班已自动生效");
        else if (regenFailed)
            await ShowSnackBarAsync($"已登记（{memberName}），但重新生成失败，请到值班表页手动生成");
        else
            await ShowSnackBarAsync(isBackfill
                ? $"已补登记（{memberName}），已删除 {deletedCount} 个月份排班，请到值班表页重新生成"
                : $"请假已登记（{memberName}），已删除 {deletedCount} 个月份排班，请到值班表页重新生成");
    }

    [RelayCommand]
    private async Task DeleteLeaveAsync(DutyLeaveView? leave)
    {
        if (!CanManageDuty || leave == null) return;

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认删除",
            $"确定要删除 {leave.MemberName} 的请假记录（{leave.RangeDisplay}）吗？\n删除后将删除受影响月份的排班数据。",
            "删除", "取消");
        if (!confirm) return;

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
            deleted = result.IsSuccess;
            if (!result.IsSuccess)
            {
                await ShowFailureAsync(result, "删除请假记录");
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
        }, "正在删除请假记录...");

        if (!deleted) return;

        if (_leavePanelMember != null)
        {
            await LoadLeaveRecordsAsync(_leavePanelMember.Id);
        }

        if (regenerateNow && !regenFailed)
            await ShowSnackBarAsync($"请假记录已删除并重新生成 {regeneratedCount} 个月份");
        else if (regenFailed)
            await ShowSnackBarAsync("请假记录已删除，但重新生成失败，请到值班表页手动生成");
        else
            await ShowSnackBarAsync($"请假记录已删除，已删除 {deletedCount} 个月份排班，请到值班表页重新生成");
    }

    #endregion

    #region 批量导入

    /// <summary>允许的导入文件扩展名（FilePicker 用）</summary>
    private static readonly string[] ExcelFileExtensions = { ".xlsx", ".xls" };

    [ObservableProperty]
    private bool _isImportPanelVisible;

    [ObservableProperty]
    private string _importFilePath = string.Empty;

    /// <summary>是否已加载预览（未加载时禁止执行导入）</summary>
    [ObservableProperty]
    private bool _isImportPreviewLoaded;

    [ObservableProperty]
    private string _importSummaryText = string.Empty;

    public ObservableCollection<string> ImportLogLines { get; } = new();

    /// <summary>是否有日志内容（控制日志区显隐；随集合变化通知）</summary>
    public bool HasImportLog => ImportLogLines.Count > 0;

    /// <summary>预览缓存的文件路径（预览与执行必须同一文件）</summary>
    private string? _previewedFilePath;

    [RelayCommand]
    private async Task DownloadImportTemplateAsync()
    {
        var folder = await PickExportFolderAsync("选择导入模板保存目录");
        if (string.IsNullOrEmpty(folder)) return;

        var savedPath = string.Empty;
        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.ExportMemberImportTemplateAsync(folder, ct);
            if (result.IsSuccess && !string.IsNullOrEmpty(result.Value))
            {
                savedPath = result.Value;
            }
            else
            {
                await ShowFailureAsync(result, "生成导入模板");
            }
        }, "正在生成导入模板...");

        if (!string.IsNullOrEmpty(savedPath))
        {
            await ShowExportSuccessAsync(folder, new[] { System.IO.Path.GetFileName(savedPath) });
        }
    }

    [RelayCommand]
    private void OpenImportPanel()
    {
        if (!CanManageDuty)
        {
            _ = _dialogService.DisplayAlertAsync("提示", "您没有管理值班成员的权限", "确定");
            return;
        }

        ImportFilePath = string.Empty;
        IsImportPreviewLoaded = false;
        ImportSummaryText = string.Empty;
        ImportLogLines.Clear();
        _previewedFilePath = null;
        IsImportPanelVisible = true;
    }

    [RelayCommand]
    private void CloseImportPanel() => IsImportPanelVisible = false;

    [RelayCommand]
    private async Task SelectImportFileAsync()
    {
        var path = await PickFileWithFeedbackAsync("选择值班成员导入文件", ExcelFileExtensions);
        if (string.IsNullOrEmpty(path)) return;

        // 选了新文件则作废旧预览，防止预览与执行的文件不一致
        ImportFilePath = path;
        IsImportPreviewLoaded = false;
        ImportSummaryText = string.Empty;
        ImportLogLines.Clear();
        _previewedFilePath = null;
    }

    [RelayCommand]
    private async Task PreviewImportAsync()
    {
        if (!CanManageDuty || string.IsNullOrWhiteSpace(ImportFilePath))
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择导入文件", "确定");
            return;
        }

        await ExecuteAsync(async ct =>
        {
            ImportLogLines.Clear();
            var result = await _dutyService.PreviewMemberImportAsync(ImportFilePath, ct);
            if (result.IsSuccess && result.Value != null)
            {
                var preview = result.Value;
                ImportSummaryText = preview.SummaryText;
                IsImportPreviewLoaded = true;
                _previewedFilePath = ImportFilePath;

                // 按组名单摘要
                foreach (var roster in preview.Rosters)
                {
                    var note = roster.MemberCount == 0 ? "（空 → 将清空该组）" : $"，顺位：{roster.NamesPreview}";
                    ImportLogLines.Add($"【{roster.GroupName}】{roster.MemberCount} 人{note}");
                }
                ImportLogLines.Add($"全量对齐：新增 {preview.NewCount} 人 / 对齐 {preview.UpdateCount} 人 / 移出 {preview.RemoveCount} 处归属");

                // 清空组强警示
                foreach (var cleared in preview.ClearedGroupNames)
                {
                    ImportLogLines.Add($"⚠ 警告：{cleared} 名单为空，导入后该组现有成员将被全部移出");
                }

                // 轮转锚点（当前序列 + 起始日期）
                if (preview.AnchorDate.HasValue)
                {
                    ImportLogLines.Add($"⏱ 轮转锚点：{preview.AnchorDate:yyyy-MM-dd} 起按名单轮转（生成该月值班表时将从此日起）");
                    foreach (var (groupCode, lines) in preview.CurrentSeqByLine)
                    {
                        var lineText = string.Join(" / ", lines.Select(kv =>
                            $"{DutyConstants.DateTypes.DisplayName(kv.Key)}={kv.Value}"));
                        ImportLogLines.Add($"   当前序列 — {DutyConstants.Groups.DisplayName(groupCode)}：{lineText}");
                    }
                }
                else
                {
                    ImportLogLines.Add("（未填写起始日期：仅导入名单，不调整轮转起点）");
                }

                foreach (var error in preview.Errors.Take(50))
                {
                    ImportLogLines.Add($"✗ {error}");
                }
                if (preview.Errors.Count > 50)
                {
                    ImportLogLines.Add($"…… 其余 {preview.Errors.Count - 50} 条校验错误略");
                }
                if (preview.IsValid)
                {
                    ImportLogLines.Add("校验通过，可直接开始导入");
                }
            }
            else
            {
                await ShowFailureAsync(result, "解析导入文件");
            }
        }, "正在解析导入文件...");
    }

    [RelayCommand]
    private async Task StartImportAsync()
    {
        if (!CanManageDuty) return;
        if (!IsImportPreviewLoaded || _previewedFilePath != ImportFilePath)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先预览（预览与执行须为同一文件）", "确定");
            return;
        }

        await ExecuteAsync(async ct =>
        {
            // 强确认：重新预览拿到清空组与移出计数（避免用旧预览做确认依据）
            var previewResult = await _dutyService.PreviewMemberImportAsync(ImportFilePath, ct);
            if (!previewResult.IsSuccess || previewResult.Value == null)
            {
                await ShowFailureAsync(previewResult, "解析导入文件");
                return;
            }
            var preview = previewResult.Value;
            if (!preview.IsValid)
            {
                await _dialogService.DisplayAlertAsync("无法导入",
                    $"导入文件存在 {preview.Errors.Count} 条校验错误，请修正后重新选择文件：\n" +
                    string.Join("\n", preview.Errors.Take(5)), "确定");
                return;
            }

            var confirmText = $"全量对齐：新增 {preview.NewCount} 人 / 对齐 {preview.UpdateCount} 人 / 移出 {preview.RemoveCount} 处归属";
            if (preview.ClearedGroupNames.Count > 0)
            {
                confirmText += $"\n\n⚠ 警告：{string.Join("、", preview.ClearedGroupNames)} 名单为空，该组现有成员将被全部移出！";
            }
            confirmText += "\n\n确定开始导入吗？";

            var confirm = await _dialogService.DisplayAlertAsync("确认导入（全量对齐）", confirmText, "开始导入", "取消");
            if (!confirm) return;

            var importResult = await _dutyService.ImportMembersAsync(ImportFilePath, ct);
            if (importResult.IsSuccess && importResult.Value != null)
            {
                var r = importResult.Value;
                ImportSummaryText = r.SummaryText;
                ImportLogLines.Clear();
                ImportLogLines.Add(r.SummaryText);
                foreach (var error in r.Errors.Take(50))
                {
                    ImportLogLines.Add(error);
                }
                if (r.Errors.Count == 0)
                {
                    ImportLogLines.Add("导入成功");
                }
                await ShowSnackBarAsync(r.Message);
                await LoadMembersAsync();
            }
            else
            {
                await ShowFailureAsync(importResult, "批量导入成员");
            }
        }, "正在批量导入成员...");
    }

    #endregion

    #endregion

    #region 生命周期

    public override async Task OnAppearingAsync()
    {
        await LoadMembersAsync();
    }

    #endregion
}
