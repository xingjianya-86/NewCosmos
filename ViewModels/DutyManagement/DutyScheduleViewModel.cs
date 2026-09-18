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
/// 值班表主页面 ViewModel：月度值班表展示、轮转生成、手工换人、开关设置、打印导出
/// </summary>
public partial class DutyScheduleViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IDutyService _dutyService = null!;
    private readonly IHolidayManageService _holidayManageService = null!;
    private readonly INewPermissionService _permissionService = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly ILoggerService _logger = null!;

    /// <summary>初始化守卫：节假日种子/快照刷新只触发一次</summary>
    private bool _holidayPrepared;

    /// <summary>开关初始化守卫：加载设置时不触发保存</summary>
    private bool _loadingSettings;

    public DutyScheduleViewModel(
        IServiceProvider serviceProvider,
        IDutyService dutyService,
        IHolidayManageService holidayManageService,
        INewPermissionService permissionService,
        IDialogService dialogService,
        ILoggerService logger)
    {
        _serviceProvider = serviceProvider;
        _dutyService = dutyService;
        _holidayManageService = holidayManageService;
        _permissionService = permissionService;
        _dialogService = dialogService;
        _logger = logger;

        Title = "值班管理";
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

    #region 月度状态与数据

    [ObservableProperty]
    private int _year = DateTime.Now.Year;

    [ObservableProperty]
    private int _month = DateTime.Now.Month;

    [ObservableProperty]
    private DutyMonthSchedule _currentSchedule = new();

    public ObservableCollection<DutyDayRow> Days { get; } = new();

    public string MonthTitle => $"{Year} 年 {Month} 月";

    public string SummaryText => CurrentSchedule.IsGenerated
        ? $"已生成 · {CurrentSchedule.ScheduleCount} 个班次"
        : "未生成";

    partial void OnYearChanged(int value) => OnPropertyChanged(nameof(MonthTitle));
    partial void OnMonthChanged(int value) => OnPropertyChanged(nameof(MonthTitle));

    #endregion

    #region 设置开关

    [ObservableProperty]
    private bool _restdayLeaderSamePair;

    partial void OnRestdayLeaderSamePairChanged(bool value)
    {
        if (_loadingSettings) return;
        _ = SaveSamePairSettingAsync(value);
    }

    private async Task SaveSamePairSettingAsync(bool enabled)
    {
        if (!CanManageDuty)
        {
            _loadingSettings = true;
            RestdayLeaderSamePair = !enabled;
            _loadingSettings = false;
            await _dialogService.DisplayAlertAsync("提示", "您没有管理值班表的权限", "确定");
            return;
        }

        var result = await _dutyService.SaveRestdayLeaderSamePairAsync(enabled, CancellationToken);
        if (result.IsSuccess)
        {
            await ShowSnackBarAsync(enabled
                ? "已开启：周六/周日由同一位带班领导值班（下次生成生效）"
                : "已关闭：周六/周日各自独立轮转（下次生成生效）");
        }
        else
        {
            await ShowFailureAsync(result, "保存开关");
        }
    }

    #endregion

    #region 班务调整入口（点班次人员 → 请假/串班/代班，跳转班务调整页预填）

    [RelayCommand]
    private async Task OpenShiftActionAsync(DutyScheduleCell? cell)
    {
        if (cell == null || !cell.HasSchedule || cell.MemberId == null) return;

        if (!CanManageDuty)
        {
            await _dialogService.DisplayAlertAsync("提示", "您没有管理值班表的权限", "确定");
            return;
        }

        var action = await _dialogService.DisplayActionSheetAsync(
            $"{cell.MemberName}（{cell.DutyDate:MM-dd}）- 班务调整", "取消", null, "请假", "串班", "代班");
        var mode = action switch
        {
            "请假" => DutyAdjustNavParam.Modes.Leave,
            "串班" => DutyAdjustNavParam.Modes.Swap,
            "代班" => DutyAdjustNavParam.Modes.Substitute,
            _ => null
        };
        if (mode == null) return;

        await NavigateToPageAsync<Pages.DutyManagement.DutyAdjustPage, DutyAdjustNavParam>(new DutyAdjustNavParam(
            mode, cell.MemberId.Value, cell.DutyDate, cell.GroupCode));
    }

    #endregion

    #region 导出范围面板（政务值班表，连续/单月输出；日期框默认今日所在月）

    public List<int> ExportYearOptions { get; } = Enumerable
        .Range(DateTime.Now.Year - 2, 8)
        .ToList();

    public List<int> ExportMonthOptions { get; } = Enumerable.Range(1, 12).ToList();

    [ObservableProperty]
    private bool _isExportPanelVisible;

    [ObservableProperty]
    private int _exportStartYear = DateTime.Now.Year;

    [ObservableProperty]
    private int _exportStartMonth = DateTime.Now.Month;

    [ObservableProperty]
    private int _exportEndYear = DateTime.Now.Year;

    [ObservableProperty]
    private int _exportEndMonth = DateTime.Now.Month;

    [RelayCommand]
    private void OpenExportPanel()
    {
        // 每次打开时默认回到今日所在月
        ExportStartYear = DateTime.Now.Year;
        ExportStartMonth = DateTime.Now.Month;
        ExportEndYear = DateTime.Now.Year;
        ExportEndMonth = DateTime.Now.Month;
        IsExportPanelVisible = true;
    }

    [RelayCommand]
    private void CloseExportPanel() => IsExportPanelVisible = false;

    [RelayCommand]
    private async Task ExportGovRangeAsync()
    {
        var folder = await PickExportFolderAsync("选择政务值班表导出目录");
        if (string.IsNullOrEmpty(folder)) return;

        IsExportPanelVisible = false;

        var files = new List<string>();
        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.ExportGovRangeAsync(
                ExportStartYear, ExportStartMonth, ExportEndYear, ExportEndMonth, folder, ct);
            if (result.IsSuccess)
            {
                files.AddRange(result.Value ?? new List<string>());
            }
            else
            {
                await ShowFailureAsync(result, "导出政务值班表");
            }
        }, $"正在导出政务值班表（{ExportStartYear}-{ExportStartMonth:D2} ~ {ExportEndYear}-{ExportEndMonth:D2}）...");

        if (files.Count > 0)
        {
            await ShowExportSuccessAsync(folder, files.Select(System.IO.Path.GetFileName).ToList());
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
    private async Task PreviousMonthAsync()
    {
        var (y, m) = Month == 1 ? (Year - 1, 12) : (Year, Month - 1);
        Year = y;
        Month = m;
        await LoadScheduleAsync();
    }

    [RelayCommand]
    private async Task NextMonthAsync()
    {
        var (y, m) = Month == 12 ? (Year + 1, 1) : (Year, Month + 1);
        Year = y;
        Month = m;
        await LoadScheduleAsync();
    }

    [RelayCommand]
    private async Task LoadScheduleAsync()
    {
        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.GetMonthScheduleAsync(Year, Month, ct);
            if (result.IsFailure)
            {
                await ShowFailureAsync(result, "加载值班表");
                return;
            }

            _loadingSettings = true;
            RestdayLeaderSamePair = result.Value.RestdayLeaderSamePair;
            _loadingSettings = false;

            CurrentSchedule = result.Value;
            Days.Clear();
            foreach (var day in result.Value.Days)
            {
                Days.Add(day);
            }

            OnPropertyChanged(nameof(SummaryText));
        }, "正在加载值班表...");
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (!CanManageDuty)
        {
            await _dialogService.DisplayAlertAsync("提示", "您没有管理值班表的权限", "确定");
            return;
        }

        // 先按常规生成；已生成月份时弹确认走强制重生成
        var firstResult = await ExecuteAsync<DutyGenerationSummary>(
            () => _dutyService.GenerateMonthAsync(Year, Month, force: false, CancellationToken),
            "正在生成值班表...");

        var summary = firstResult.IsSuccess ? firstResult.Value : null;
        if (!firstResult.IsSuccess)
        {
            await ShowFailureAsync(firstResult, "生成值班表");
            return;
        }

        if (summary == null)
        {
            // 已生成月份：确认后强制重生成（游标继续消耗）
            var confirm = await _dialogService.DisplayAlertAsync(
                "重新生成",
                $"{Year} 年 {Month} 月值班表已生成。\n重新生成将删除该月班次并按当前轮转游标继续推演（相当于重新摇号），确定继续吗？",
                "重新生成", "取消");
            if (!confirm) return;

            var forceResult = await ExecuteAsync<DutyGenerationSummary>(
                () => _dutyService.GenerateMonthAsync(Year, Month, force: true, CancellationToken),
                "正在重新生成值班表...");

            if (!forceResult.IsSuccess)
            {
                await ShowFailureAsync(forceResult, "重新生成值班表");
                return;
            }
            summary = forceResult.Value;
        }

        var changeText = (summary.ReplayedChangeCount + summary.PendingChangeCount + summary.SkippedChangeCount) > 0
            ? "\n\n串班/代班：生效 " + summary.ReplayedChangeCount + " 条"
              + (summary.PendingChangeCount > 0
                  ? $"，待生效 {summary.PendingChangeCount} 条（缺 {string.Join("、", summary.PendingChangeMonths)}）"
                  : string.Empty)
              + (summary.SkippedChangeCount > 0
                  ? $"，轮转变化跳过 {summary.SkippedChangeCount} 条（需处理）"
                  : string.Empty)
            : string.Empty;

        await _dialogService.DisplayAlertAsync("生成完成",
            $"工作日 {summary.WorkdayCount} 天 / 休息日 {summary.RestdayCount} 天 / 法定节假日 {summary.HolidayCount} 天\n" +
            $"共生成 {summary.ScheduleCount} 个班次" +
            (summary.AnchorDay > 1 ? $"\n（轮转锚点生效：本月自 {summary.AnchorDay} 日起生成）" : string.Empty) +
            (summary.ConflictSkippedCount > 0 ? $"\n同人冲突顺延 {summary.ConflictSkippedCount} 次" : string.Empty) +
            (summary.UnavailableSkippedCount > 0 ? $"\n请假/离职边界顺延 {summary.UnavailableSkippedCount} 次" : string.Empty) +
            changeText,
            "确定");

        // 跨月串班/代班待生效：弹窗询问一键补齐对方月份（生成后重放自动生效）
        if (summary.PendingChangeMonths.Count > 0)
        {
            var fill = await _dialogService.DisplayAlertAsync("串班/代班待生效",
                $"有 {summary.PendingChangeCount} 条串班/代班因对方月份未生成而待生效（缺：{string.Join("、", summary.PendingChangeMonths)}）。\n\n" +
                "是否立即补齐生成？生成后自动生效。",
                "补齐生成", "稍后");
            if (fill)
            {
                var monthsToFill = summary.PendingChangeMonths;
                var fillResult = await ExecuteAsync(
                    () => _dutyService.GenerateMissingMonthsAsync(monthsToFill, CancellationToken),
                    "正在补齐生成...");
                if (fillResult.IsFailure)
                {
                    await ShowFailureAsync(fillResult, "补齐生成");
                }
                else
                {
                    await _dialogService.DisplayAlertAsync("补齐完成",
                        $"已生成 {fillResult.Value} 个月份，串班/代班已自动重放生效。", "确定");
                }
            }
        }

        await LoadScheduleAsync();
    }

    [RelayCommand]
    private async Task ClearMonthAsync()
    {
        if (!CanManageDuty)
        {
            await _dialogService.DisplayAlertAsync("提示", "您没有管理值班表的权限", "确定");
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync(
            "清空值班表",
            $"确定要删除 {Year} 年 {Month} 月的全部班次吗？\n（轮转游标不受影响，可重新生成）",
            "删除", "取消");
        if (!confirm) return;

        var cleared = false;
        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.ClearMonthAsync(Year, Month, ct);
            cleared = result.IsSuccess;
            if (!result.IsSuccess)
            {
                await ShowFailureAsync(result, "清空值班表");
            }
        }, "正在清空...");

        if (cleared)
        {
            await ShowSnackBarAsync("该月值班表已清空");
            await LoadScheduleAsync();
        }
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.PrintMonthAsync(Year, Month, printerName: string.Empty, copies: 1, ct);
            if (result.IsSuccess)
            {
                await ShowSnackBarAsync("打印任务已提交");
            }
            else
            {
                await ShowFailureAsync(result, "打印值班表");
            }
        }, "正在打印（走 Office COM）...");
    }

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        var folder = await PickExportFolderAsync("选择 PDF 导出目录");
        if (string.IsNullOrEmpty(folder)) return;

        await ExecuteAsync(async ct =>
        {
            var result = await _dutyService.ExportMonthPdfAsync(Year, Month, folder, ct);
            if (result.IsSuccess && !string.IsNullOrEmpty(result.Value))
            {
                await ShowExportSuccessAsync(folder, new[] { System.IO.Path.GetFileName(result.Value) });
            }
            else
            {
                await ShowFailureAsync(result, "导出 PDF");
            }
        }, "正在导出 PDF...");
    }

    [RelayCommand]
    private async Task OpenMemberManagementAsync()
    {
        await NavigateToPageAsync<Pages.DutyManagement.DutyMemberPage>();
    }

    [RelayCommand]
    private async Task OpenHolidayManagementAsync()
    {
        await NavigateToPageAsync<Pages.DutyManagement.HolidayPage>();
    }

    [RelayCommand]
    private async Task OpenAdjustManagementAsync()
    {
        await NavigateToPageAsync<Pages.DutyManagement.DutyAdjustPage>();
    }

    #endregion

    #region 生命周期

    public override async Task OnAppearingAsync()
    {
        // 节假日数据兜底种子 + 快照刷新（只触发一次，失败不影响值班表展示）
        if (!_holidayPrepared)
        {
            _holidayPrepared = true;
            SafeFireAndForget(async () =>
            {
                var seed = await _holidayManageService.EnsureSeedDataAsync();
                if (seed.IsFailure)
                {
                    _logger.Warn($"节假日种子写入失败: {seed.Message}");
                    return;
                }
                var refresh = await _holidayManageService.RefreshCacheAsync();
                if (refresh.IsFailure)
                {
                    _logger.Warn($"节假日快照刷新失败: {refresh.Message}");
                }
            });
        }

        await LoadScheduleAsync();
    }

    #endregion
}
