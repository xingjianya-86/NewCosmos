using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Enums;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.Platform;
using NewCosmos.Services.Utilities;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Diagnostics;

using NewCosmos.Helpers;

namespace NewCosmos.ViewModels.Reporting;

/// <summary>
/// 月报表主界面：周期选择、汇总卡片、表单勾选列表（预览/打印/导出）、会议记录录入
/// </summary>
public partial class MonthlyReportMainViewModel : ViewModelBase
{
    private readonly IMonthlyReportService _reportService = null!;
    private readonly IPrintService _printService = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IPrinterService _printerService = null!;
    private readonly IUserService _userService = null!;
    private readonly IBusinessTimelineService _businessTimelineService = null!;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    public ObservableCollection<int> YearOptions { get; } = new();
    public ObservableCollection<int> MonthOptions { get; } = new(Enumerable.Range(1, 12));
    public ObservableCollection<string> TownOptions { get; } = new();

    /// <summary>
    /// 报表表单勾选列表（替代原 14 个按钮）
    /// </summary>
    public ObservableCollection<MonthlyFormOption> Forms { get; } = new();

    [ObservableProperty]
    private int _selectedYear;

    [ObservableProperty]
    private int _selectedMonth;

    [ObservableProperty]
    private string _selectedTown = "全部";

    [ObservableProperty]
    private string _cycleRangeText = string.Empty;

    // 汇总卡片
    [ObservableProperty]
    private int _addedCount;

    [ObservableProperty]
    private int _stoppedCount;

    [ObservableProperty]
    private int _increaseCount;

    [ObservableProperty]
    private int _decreaseCount;

    [ObservableProperty]
    private int _deathCount;

    [ObservableProperty]
    private string _totalAmountText = "-";

    [ObservableProperty]
    private string _summaryText = string.Empty;

    // 打印份数
    [ObservableProperty]
    private int _copies = 1;

    // 会议记录
    [ObservableProperty]
    private string _meetingTime = string.Empty;

    [ObservableProperty]
    private string _host = string.Empty;

    [ObservableProperty]
    private string _recorder = string.Empty;

    [ObservableProperty]
    private string _attendees = string.Empty;

    [ObservableProperty]
    private string _absentees = string.Empty;

    [ObservableProperty]
    private string _applyCategory = string.Empty;

    // 出席/缺席弹窗状态
    [ObservableProperty]
    private bool _isAttendancePopupVisible;

    [ObservableProperty]
    private string _attendancePopupTitle = "会议出席/缺席登记";

    [ObservableProperty]
    private string _attendanceHistoryText = string.Empty;

    [ObservableProperty]
    private bool _hasAttendanceHistory;

    [ObservableProperty]
    private string _popupAttendees = string.Empty;

    [ObservableProperty]
    private string _popupAbsentees = string.Empty;

    // PDF 预览
    [ObservableProperty]
    private string _pdfPreviewUrl = string.Empty;

    [ObservableProperty]
    private bool _hasPdfPreview;

    public MonthlyReportMainViewModel(
        IMonthlyReportService reportService,
        IPrintService printService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        IPrinterService printerService,
        IUserService userService,
        IBusinessTimelineService businessTimelineService)
    {
        _reportService = reportService;
        _printService = printService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _printerService = printerService;
        _userService = userService;
        _businessTimelineService = businessTimelineService;

        var now = DateTime.Now;
        for (var y = now.Year - 5; y <= now.Year + 1; y++)
            YearOptions.Add(y);
        SelectedYear = now.Year;
        SelectedMonth = now.Month;

        foreach (var (key, label) in new[]
        {
            ("新增救助明细_最低生活保障", "新增救助明细（最低生活保障）"),
            ("新增救助明细_最低生活保障边缘家庭", "新增救助明细（最低生活保障边缘家庭）"),
            ("新增救助明细_特困人员", "新增救助明细（特困人员）"),
            ("新增救助明细_刚性支出困难家庭", "新增救助明细（刚性支出困难家庭）"),
            ("停保汇总表_最低生活保障", "停保汇总表（最低生活保障）"),
            ("停保汇总表_最低生活保障边缘家庭", "停保汇总表（最低生活保障边缘家庭）"),
            ("停保汇总表_特困人员", "停保汇总表（特困人员）"),
            ("停保汇总表_刚性支出困难家庭", "停保汇总表（刚性支出困难家庭）"),
            ("保障金增发表_最低生活保障", "保障金增发表（最低生活保障）"),
            ("保障金减发表_最低生活保障", "保障金减发表（最低生活保障）"),
            ("施保金减发", "分类施保金减发人员表（最低生活保障）"),
            ("分类施保增加", "分类施保金增发人员表（最低生活保障）"),
            ("自然减员表", "人员变动_自然减员月报表"),
            ("临时救助新增汇总表", "临时救助新增汇总表"),
            ("档案_退出对象兜底情况纠治表", "退出对象兜底情况纠治表（批量）"),
            ("会议记录", "会议记录"),
            ("会议记录_一事一议", "会议记录（一事一议）"),
            (NearRelativeConstants.FormKeyStaffBatch, "近亲属备案（工作人员批量）"),
            (NearRelativeConstants.FormKeySummary, "近亲属备案汇总")
        })
        {
            var option = new MonthlyFormOption(key, label);
            option.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MonthlyFormOption.IsSelected))
                {
                    PreviewSelectedCommand.NotifyCanExecuteChanged();
                    PrintSelectedCommand.NotifyCanExecuteChanged();
                    SaveSelectedCommand.NotifyCanExecuteChanged();
                }
            };
            Forms.Add(option);
        }
    }

    partial void OnSelectedYearChanged(int value) => SafeFireAndForget(RefreshCycleRangeAsync);

    partial void OnSelectedMonthChanged(int value) => SafeFireAndForget(RefreshCycleRangeAsync);

    /// <summary>
    /// 周期提示刷新（非关键，失败静默）。async Task 版本，避免裸 async void 直接冒泡异常；
    /// 由 ObservableProperty 变更回调经 SafeFireAndForget 调用（自带 try/catch）。
    /// </summary>
    private async Task RefreshCycleRangeAsync()
    {
        // 构造期 SelectedYear/SelectedMonth 会各自触发一次本方法，另一项可能仍为默认 0；
        // 无效周期直接跳过，避免 new DateTime(year, 0, 16) 抛"不可表示的 DateTime"
        if (SelectedYear <= 0 || SelectedMonth is < 1 or > 12) return;

        var result = await _reportService.GetCycleRangeAsync(SelectedYear, SelectedMonth);
        if (result.IsSuccess)
            CycleRangeText = result.Value.Display;
    }

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        if (TownOptions.Count == 0)
        {
            TownOptions.Add("全部");
            var townResult = await _reportService.GetTownOptionsAsync();
            if (townResult.IsSuccess)
            {
                foreach (var t in townResult.Value)
                    if (!TownOptions.Contains(t))
                        TownOptions.Add(t);
            }
        }
        await RefreshCycleRangeAsync();
        await LoadMeetingAsync();
    }

    /// <summary>
    /// 重新加载当前周期数据（统计卡片 + 会议记录）
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await ExecuteAsync(async ct =>
        {
            await RefreshCycleRangeAsync();
            await LoadSummaryAsync(ct);
            await LoadMeetingAsync();
        }, "加载月报数据...");
    }

    private async Task LoadSummaryAsync(CancellationToken ct)
    {
        var town = SelectedTown == "全部" ? "" : SelectedTown;

        var added = await _reportService.GetAddedRowsAsync(SelectedYear, SelectedMonth, town, null, ct);
        if (added.IsFailure) { await ShowErrorAsync(added.Message); return; }
        AddedCount = added.Value?.Count ?? 0;

        var stopped = await _reportService.GetStoppedRowsAsync(SelectedYear, SelectedMonth, town, null, ct);
        if (stopped.IsFailure) { await ShowErrorAsync(stopped.Message); return; }
        StoppedCount = stopped.Value?.Count ?? 0;

        var inc = await _reportService.GetIncreaseRowsAsync(SelectedYear, SelectedMonth, town, null, ct);
        var dec = await _reportService.GetDecreaseRowsAsync(SelectedYear, SelectedMonth, town, null, ct);
        if (inc.IsFailure) { await ShowErrorAsync(inc.Message); return; }
        if (dec.IsFailure) { await ShowErrorAsync(dec.Message); return; }
        IncreaseCount = inc.Value?.Count ?? 0;
        DecreaseCount = dec.Value?.Count ?? 0;

        var death = await _reportService.GetDeathRowsAsync(SelectedYear, SelectedMonth, town, null, ct);
        if (death.IsFailure) { await ShowErrorAsync(death.Message); return; }
        DeathCount = death.Value?.Count ?? 0;

        var stats = await _reportService.GetStatisticsAsync(SelectedYear, SelectedMonth, ct);
        if (stats.IsSuccess)
        {
            TotalAmountText = stats.Value.TotalAmount.ToString("N2");
            SummaryText = $"在保 {stats.Value.TotalArchives} 户，月保障金合计 {stats.Value.TotalAmount:N2} 元";
        }
    }

    private async Task LoadMeetingAsync()
    {
        var town = SelectedTown == "全部" ? "" : SelectedTown;
        var result = await _reportService.GetMeetingAsync(SelectedYear, SelectedMonth, town, ct: CancellationToken);
        if (!result.IsSuccess || result.Value == null)
            return;
        MeetingTime = result.Value.MeetingTime;
        Host = result.Value.Host;
        Recorder = result.Value.Recorder;
        Attendees = result.Value.Attendees;
        Absentees = result.Value.Absentees;
        ApplyCategory = result.Value.ApplyCategory;

        // 新记录（无会议记录入库）：自动带出默认值——会议时间=B线会议日（每月7号）、
        // 主持人=当前单位民政助理、记录人=当前登录用户；用户可修改后再保存。
        if (result.Value.Id != 0)
            return;
        if (string.IsNullOrWhiteSpace(MeetingTime))
            MeetingTime = await FormatMeetingTimeAsync();
        if (string.IsNullOrWhiteSpace(Host))
            Host = await GetCivilAssistantNameAsync();
        if (string.IsNullOrWhiteSpace(Recorder))
            Recorder = App.CurrentUserFullName;
    }

    /// <summary>
    /// B线会议日（每月7号，节假日顺延）带星期格式：2026年8月7日 星期五
    /// </summary>
    private async Task<string> FormatMeetingTimeAsync()
    {
        var timeline = await _businessTimelineService.CalculateTimelineAsync(
            SelectedYear, SelectedMonth, TimelineType.BusinessProcess);
        if (timeline == null)
            return string.Empty;
        var d = timeline.MeetingDate;
        var weekday = new[] { "日", "一", "二", "三", "四", "五", "六" }[(int)d.DayOfWeek];
        return $"{d.Year}年{d.Month}月{d.Day}日 星期{weekday}";
    }

    /// <summary>
    /// 当前单位职位为"民政助理"的用户姓名；未配置则返回空
    /// </summary>
    private async Task<string> GetCivilAssistantNameAsync()
    {
        var orgId = App.CurrentUserOrganizationId;
        if (!orgId.HasValue)
            return string.Empty;
        var usersResult = await _userService.GetByOrganizationAsync(orgId.Value);
        if (usersResult.IsFailure || usersResult.Value == null)
            return string.Empty;
        return usersResult.Value.FirstOrDefault(u => u.Position == "民政助理")?.FullName ?? string.Empty;
    }

    /// <summary>
    /// 渲染并预览勾选的多张表单（合并为一份 PDF）
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedForms))]
    private async Task PreviewSelectedAsync()
    {
        var town = SelectedTown == "全部" ? "" : SelectedTown;
        var keys = Forms.Where(f => f.IsSelected).Select(f => f.FormKey).ToList();
        if (keys.Count == 0)
        {
            await ShowErrorAsync("请先勾选要预览的报表表单");
            return;
        }

        // 含会议记录：先确认出席/缺席（弹窗确认后由 ConfirmAttendanceCommand 续执行）
        if (keys.Any(k => k is "会议记录" or "会议记录_一事一议") && !_skipAttendancePrompt)
        {
            var proceed = await PromptMeetingAttendanceAsync(town);
            if (!proceed || IsAttendancePopupVisible)
            {
                if (IsAttendancePopupVisible)
                    _pendingAction = PreviewSelectedAsync;
                return;
            }
        }

        await ExecuteAsync(async ct =>
        {
            var result = await _printService.RenderMonthlyFormsMergedAsync(SelectedYear, SelectedMonth, keys, town, ct);
            if (result.IsFailure)
            {
                await ShowErrorAsync(result.Message);
                return;
            }
            if (result.Value.Length == 0)
            {
                await _dialogService.DisplayAlertAsync("提示", "勾选的表单本周期均无数据，未生成预览", "确定");
                return;
            }

            var filePath = Path.Combine(
                OutputPathHelper.GetTempDirectory(),
                $"monthly_{SelectedYear}{SelectedMonth:D2}_selected.pdf");
            // 先清空再赋值，强制 PdfPreviewView 重新加载（同路径时值不变不会触发刷新）
            PdfPreviewUrl = "";
            HasPdfPreview = false;
            await File.WriteAllBytesAsync(filePath, result.Value, ct);
            PdfPreviewUrl = filePath;
            HasPdfPreview = true;
        }, "正在生成预览...");
    }

    /// <summary>
    /// 批量打印勾选的表单（默认打印机，可设置份数）
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedForms))]
    private async Task PrintSelectedAsync()
    {
        var town = SelectedTown == "全部" ? "" : SelectedTown;
        var keys = Forms.Where(f => f.IsSelected).Select(f => f.FormKey).ToList();
        if (keys.Count == 0)
        {
            await ShowErrorAsync("请先勾选要打印的报表表单");
            return;
        }

        // 含会议记录：先确认出席/缺席（弹窗确认后由 ConfirmAttendanceCommand 续执行）
        if (keys.Any(k => k is "会议记录" or "会议记录_一事一议") && !_skipAttendancePrompt)
        {
            var proceed = await PromptMeetingAttendanceAsync(town);
            if (!proceed || IsAttendancePopupVisible)
            {
                if (IsAttendancePopupVisible)
                    _pendingAction = PrintSelectedAsync;
                return;
            }
        }

        await ExecuteAsync(async ct =>
        {

            var printer = _printerService.GetDefaultPrinter();
            if (string.IsNullOrWhiteSpace(printer))
            {
                await ShowErrorAsync("未检测到默认打印机，请先在系统中设置默认打印机");
                return;
            }

            var copies = Math.Max(1, Copies);
            var printed = 0;
            var skipped = new List<string>();
            foreach (var key in keys)
            {
                var result = await _printService.PrintMonthlyFormAsync(SelectedYear, SelectedMonth, key, town, printer, copies, ct);
                if (result.IsFailure)
                {
                    // 无数据表单跳过不打印
                    if (result.ErrorCode == "NOT_FOUND" && result.Message?.Contains("无数据") == true)
                    {
                        skipped.Add(key);
                        continue;
                    }
                    await ShowErrorAsync($"打印失败（{key}）：{result.Message}");
                    return;
                }
                printed++;
            }
            var skipText = skipped.Count > 0 ? $"\n无数据跳过：{string.Join("、", skipped)}" : "";
            await _dialogService.DisplayAlertAsync("提示", $"已发送 {printed} 张表单到打印机（{printer}）{skipText}", "确定");
        }, "正在打印...");
    }

    /// <summary>
    /// 「保存选中」：勾选表单的源文件（xlsx/docx）+ PDF 落固定输出根（不打印）
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedForms))]
    private Task SaveSelectedAsync() => SaveFormsAsync(all: false);

    /// <summary>
    /// 「全部保存」：全部表单落固定输出根（不打印，不依赖勾选）
    /// </summary>
    [RelayCommand]
    private Task SaveAllAsync() => SaveFormsAsync(all: true);

    private async Task SaveFormsAsync(bool all)
    {
        var town = SelectedTown == "全部" ? "" : SelectedTown;
        var keys = (all
            ? Forms.Select(f => f.FormKey)
            : Forms.Where(f => f.IsSelected).Select(f => f.FormKey)).ToList();
        if (keys.Count == 0)
        {
            await ShowErrorAsync(all ? "没有可保存的报表表单" : "请先勾选要保存的报表表单");
            return;
        }

        // 含会议记录：先确认出席/缺席（弹窗确认后由 ConfirmAttendanceCommand 续执行）
        if (keys.Any(k => k is "会议记录" or "会议记录_一事一议") && !_skipAttendancePrompt)
        {
            var proceed = await PromptMeetingAttendanceAsync(town);
            if (!proceed || IsAttendancePopupVisible)
            {
                if (IsAttendancePopupVisible)
                    _pendingAction = () => SaveFormsAsync(all);
                return;
            }
        }

        // 输出固定到 config\document_output.yaml 的 output.base_directory（不再弹目录选择）
        var folder = Path.Combine(OutputPathHelper.OutputRoot, "月报表", $"{SelectedYear:D4}{SelectedMonth:D2}");
        Directory.CreateDirectory(folder);

        var savedFiles = new List<string>();
        var skipped = new List<string>();

        await ExecuteAsync(async ct =>
        {
            foreach (var key in keys)
            {
                // 源文件（可再编辑）
                var sourceResult = await _printService.RenderMonthlyFormToSourceAsync(SelectedYear, SelectedMonth, key, town, ct);
                if (sourceResult.IsFailure)
                {
                    if (sourceResult.ErrorCode == "NOT_FOUND" && sourceResult.Message?.Contains("无数据") == true)
                    {
                        skipped.Add(key);
                        continue;
                    }
                    await ShowErrorAsync($"保存失败（{key}）：{sourceResult.Message}");
                    return;
                }
                foreach (var file in sourceResult.Value)
                {
                    var path = Path.Combine(folder, file.FileName + file.Extension);
                    await File.WriteAllBytesAsync(path, file.Bytes, ct);
                    savedFiles.Add(Path.GetFileName(path));
                }

                // PDF（合并预览版，所见即所得）
                var pdfResult = await _printService.RenderMonthlyFormAsync(SelectedYear, SelectedMonth, key, town, ct);
                if (pdfResult.IsFailure)
                {
                    await ShowErrorAsync($"保存失败（{key}）：{pdfResult.Message}");
                    return;
                }
                var pdfPath = Path.Combine(folder, $"{SelectedYear:D4}{SelectedMonth:D2}_{key}.pdf");
                await File.WriteAllBytesAsync(pdfPath, pdfResult.Value, ct);
                savedFiles.Add(Path.GetFileName(pdfPath));
            }
        }, "正在生成...");

        if (savedFiles.Count > 0)
        {
            var skipText = skipped.Count > 0 ? $"无数据跳过：{string.Join("、", skipped)}" : null;
            await ShowExportSuccessAsync(folder, savedFiles, skipText);
        }
        else if (skipped.Count > 0)
        {
            await _dialogService.DisplayAlertAsync("提示", $"所选表单均无数据，未生成文件：{string.Join("、", skipped)}", "确定");
        }
    }

    /// <summary>
    /// 全选
    /// </summary>
    [RelayCommand]
    private void SelectAllForms()
    {
        foreach (var f in Forms)
            f.IsSelected = true;
    }

    /// <summary>
    /// 清空选择
    /// </summary>
    [RelayCommand]
    private void ClearFormSelection()
    {
        foreach (var f in Forms)
            f.IsSelected = false;
    }

    /// <summary>
    /// 是否有勾选的表单（控制操作按钮可用性）
    /// </summary>
    private bool HasSelectedForms() => !IsBusy && Forms.Any(f => f.IsSelected);

    // 出席/缺席弹窗确认后的续执行动作（弹窗打开时挂载，确认后重入原命令）
    private Func<Task>? _pendingAction;

    private bool _skipAttendancePrompt;

    /// <summary>
    /// 含会议记录时先询问是否重新录入出席/缺席；
    /// 返回 false 表示流程中止（弹窗已弹出或用户未确认）
    /// </summary>
    private async Task<bool> PromptMeetingAttendanceAsync(string town)
    {
        var answer = await _dialogService.DisplayAlertAsync(
            "会议出席/缺席",
            "本次生成包含”会议记录“，是否重新录入本期出席/缺席人员？\n（不录入将按已保存的内容生成）",
            "重新录入", "直接生成");
        if (!answer)
            return true;

        await OpenAttendancePopupAsync(town);
        return false;
    }

    /// <summary>
    /// 打开出席/缺席弹窗并回填最近一次历史
    /// </summary>
    private async Task OpenAttendancePopupAsync(string town)
    {
        var latestResult = await _reportService.GetLatestMeetingAttendanceAsync(SelectedYear, SelectedMonth, town);
        var latest = latestResult.IsSuccess ? latestResult.Value : null;
        if (latest != null && latest.CreatedAt != default)
        {
            AttendanceHistoryText = $"最近保存：{latest.CreatedAt:yyyy-MM-dd HH:mm}（{latest.CreatedBy}）";
            HasAttendanceHistory = true;
        }
        else
        {
            AttendanceHistoryText = string.Empty;
            HasAttendanceHistory = false;
        }

        PopupAttendees = latest?.Attendees ?? string.Empty;
        PopupAbsentees = latest?.Absentees ?? string.Empty;
        IsAttendancePopupVisible = true;
    }

    /// <summary>
    /// 弹窗确认：保存本期会议记录与出席/缺席历史，然后继续原操作
    /// </summary>
    [RelayCommand]
    private async Task ConfirmAttendanceAsync()
    {
        var town = SelectedTown == "全部" ? "" : SelectedTown;
        Attendees = PopupAttendees?.Trim() ?? string.Empty;
        Absentees = PopupAbsentees?.Trim() ?? string.Empty;

        var meeting = new MonthlyMeeting
        {
            Year = SelectedYear,
            Month = SelectedMonth,
            Town = town,
            MeetingTime = MeetingTime,
            Host = Host,
            Recorder = Recorder,
            Attendees = Attendees,
            Absentees = Absentees,
            ApplyCategory = ApplyCategory
        };
        // [CT 豁免] 会议记录+出勤历史两步保存无事务包装，取消会半保存——保存收尾必须完成
        var result = await _reportService.SaveMeetingAsync(meeting, CancellationToken.None);
        if (result.IsFailure)
        {
            await ShowErrorAsync(result.Message);
            return;
        }

        var history = await _reportService.SaveMeetingAttendanceHistoryAsync(
            SelectedYear, SelectedMonth, town, Attendees, Absentees, App.CurrentUserFullName,
            CancellationToken.None); // [CT 豁免] 同上：与会议记录配套的第二步保存，不可中断
        if (history.IsFailure)
        {
            await ShowErrorAsync(history.Message);
            return;
        }

        IsAttendancePopupVisible = false;
        var pending = _pendingAction;
        _pendingAction = null;
        if (pending != null)
        {
            _skipAttendancePrompt = true;
            try { await pending(); }
            finally { _skipAttendancePrompt = false; }
        }
    }

    /// <summary>
    /// 弹窗取消：中止原操作
    /// </summary>
    [RelayCommand]
    private void CancelAttendance()
    {
        IsAttendancePopupVisible = false;
        _pendingAction = null;
    }

    private async Task ShowErrorAsync(string message)
    {
        await _dialogService.DisplayAlertAsync("错误", message ?? "操作失败", "确定");
    }
}

/// <summary>
/// 月报表表单勾选项（勾选列表 + 标签）
/// </summary>
public partial class MonthlyFormOption : ObservableObject
{
    public string FormKey { get; }

    public string Label { get; }

    /// <summary>
    /// 是否勾选
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    public MonthlyFormOption(string formKey, string label)
    {
        FormKey = formKey;
        Label = label;
    }
}

/// <summary>
/// 月报历史列表 VM
/// </summary>
public partial class ReportHistoryViewModel : PagedSearchViewModelBase
{
    private readonly IMonthlyReportService _reportService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IServiceProvider _serviceProvider = null!;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    public ObservableCollection<MonthlyReportSummary> Reports { get; } = new();

    [ObservableProperty]
    private int? _filterYear;

    [ObservableProperty]
    private MonthlyReportSummary _selectedReport = null!;

    public List<int> YearFilterOptions { get; } = new();

    public ReportHistoryViewModel(IMonthlyReportService reportService, ILoggerService logger, IServiceProvider serviceProvider)
    {
        _reportService = reportService;
        _logger = logger;
        _serviceProvider = serviceProvider;

        for (var y = DateTime.Now.Year - 5; y <= DateTime.Now.Year; y++)
            YearFilterOptions.Add(y);
    }

    protected override Task LoadDataAsync()
        => LoadPageAsync(
            ct => _reportService.GetReportHistoryAsync(FilterYear, null, PageIndex, PageSize, ct),
            Reports,
            null,
            "加载报表历史...");

    [RelayCommand]
    private async Task DeleteReportAsync()
    {
        if (SelectedReport == null) return;
        var result = await _reportService.DeleteAsync(SelectedReport.Year, SelectedReport.Month);
        if (result.IsSuccess)
            await LoadDataAsync();
    }
}