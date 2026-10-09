using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.NavigationData;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.AssetVerification;
using NewCosmos.Services.System;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.ViewModels.AssetVerification;

public partial class MonthlyReportViewModel : ViewModelBase
{
    private readonly IAssetVerificationService _verificationService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly AddressResolver _addressResolver;
    private readonly IDictCacheService _dictCacheService;

    [ObservableProperty]
    private int _selectedYear;

    [ObservableProperty]
    private int? _selectedMonth;

    [ObservableProperty]
    private string _periodText = string.Empty;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _submittedCount;

    [ObservableProperty]
    private int _hasReportCount;

    [ObservableProperty]
    private int _archivedCount;

    [ObservableProperty]
    private int _rejectedCount;

    [ObservableProperty]
    private bool _isMonthSelected;

    // ===== 周报支持 =====
    [ObservableProperty]
    private ReportPeriodMode _reportMode;

    [ObservableProperty]
    private bool _isWeeklyMode;

    [ObservableProperty]
    private WeekInfo? _selectedWeek;

    [ObservableProperty]
    private List<WeekInfo> _weekOptions = new();

    /// <summary>当前选中的周序号（从 SelectedWeek 派生）</summary>
    public int? SelectedWeekNumber => SelectedWeek?.WeekNumber;

    public bool CanExport => SelectedMonth.HasValue;

    public string TotalLabel => IsWeeklyMode ? "本周合计" : "本月合计";

    public string ReportTypeLabel => IsWeeklyMode ? "周报" : "月报";

    public List<int> YearOptions { get; } = Enumerable.Range(DateTime.Now.Year - 5, 11).ToList();

    public List<int?> MonthOptions { get; } = new() { null, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

    public MonthlyReportViewModel(
        IAssetVerificationService verificationService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        AddressResolver addressResolver,
        IDictCacheService dictCacheService)
    {
        _verificationService = verificationService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _addressResolver = addressResolver;
        _dictCacheService = dictCacheService;

        Title = "核查月报表";

        // 默认选中当前月
        _selectedYear = DateTime.Now.Year;
        _selectedMonth = DateTime.Now.Month;

        RefreshModeAndWeeks();
    }

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    partial void OnSelectedYearChanged(int value)
    {
        RefreshModeAndWeeks();
    }

    partial void OnSelectedMonthChanged(int? value)
    {
        IsMonthSelected = value.HasValue;
        RefreshModeAndWeeks();
    }

    partial void OnSelectedWeekChanged(WeekInfo? value)
    {
        OnPropertyChanged(nameof(SelectedWeekNumber));
        UpdatePeriodText();
    }

    private void RefreshModeAndWeeks()
    {
        if (!SelectedMonth.HasValue)
        {
            ReportMode = ReportPeriodMode.Monthly;
            IsWeeklyMode = false;
            SelectedWeek = null;
            WeekOptions = new();
            UpdatePeriodText();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(TotalLabel));
            OnPropertyChanged(nameof(ReportTypeLabel));
            OnPropertyChanged(nameof(CanExport));
            return;
        }

        ReportMode = ReportPeriodHelper.GetMode(SelectedYear, SelectedMonth.Value);
        IsWeeklyMode = ReportMode == ReportPeriodMode.Weekly;

        if (IsWeeklyMode)
        {
            WeekOptions = ReportPeriodHelper.GetWeeksInMonth(SelectedYear, SelectedMonth.Value);
            SelectedWeek = WeekOptions.FirstOrDefault();
        }
        else
        {
            SelectedWeek = null;
            WeekOptions = new();
        }

        UpdatePeriodText();
        Title = IsWeeklyMode ? "核查周报表" : "核查月报表";
        OnPropertyChanged(nameof(TotalLabel));
        OnPropertyChanged(nameof(ReportTypeLabel));
        OnPropertyChanged(nameof(CanExport));
    }

    private void UpdatePeriodText()
    {
        if (IsWeeklyMode && SelectedWeekNumber.HasValue)
        {
            if (SelectedWeekNumber == 0)
            {
                PeriodText = "A线 核查周期（过渡周）：2026-07-11 ~ 2026-08-02";
            }
            else
            {
                var (start, end) = ReportPeriodHelper.GetWeeklyPeriod(SelectedYear, SelectedWeekNumber.Value);
                PeriodText = $"A线 核查周期（周报）：{start:yyyy-MM-dd} ~ {end.AddDays(-1):yyyy-MM-dd}";
            }
        }
        else if (SelectedMonth.HasValue)
        {
            var (start, end) = GetALinePeriod(SelectedYear, SelectedMonth);
            var lastDay = end.AddDays(-1);
            PeriodText = $"A线 核查周期：{start:yyyy-MM-dd} ~ {lastDay:yyyy-MM-dd}";
        }
        else
        {
            var (start, end) = ALinePeriodHelper.GetYearPeriod(SelectedYear);
            var lastDay = end.AddDays(-1);
            PeriodText = $"A线 核查年份：{SelectedYear}年（{start:yyyy-MM-dd} ~ {lastDay:yyyy-MM-dd}）";
        }
    }

    private static (DateTime start, DateTime end) GetALinePeriod(int year, int? month)
    {
        return month.HasValue
            ? ALinePeriodHelper.GetPeriod(year, month.Value)
            : ALinePeriodHelper.GetYearPeriod(year);
    }

    [RelayCommand]
    private async Task LoadStatsAsync()
    {
        if (!SelectedMonth.HasValue) return;

        await ExecuteAsync(async ct =>
        {
            var (start, end) = IsWeeklyMode && SelectedWeekNumber.HasValue
                ? ReportPeriodHelper.GetWeeklyPeriod(SelectedYear, SelectedWeekNumber.Value)
                : GetALinePeriod(SelectedYear, SelectedMonth);

            var result = await _verificationService.GetStatsByDateRangeAsync(start, end, ct);
            if (result.IsSuccess && result.Value != null)
            {
                TotalCount = result.Value.TotalCount;
                SubmittedCount = result.Value.SubmittedCount;
                HasReportCount = result.Value.HasReportCount;
                ArchivedCount = result.Value.ArchivedCount;
                RejectedCount = result.Value.RejectedCount;
            }
            else
            {
                TotalCount = 0;
                SubmittedCount = 0;
                HasReportCount = 0;
                ArchivedCount = 0;
                RejectedCount = 0;
            }
        }, "加载统计...");
    }

    [RelayCommand]
    private async Task ExportPendingAsync() => await NavigateToOutputAsync("0", "已提交申请");

    [RelayCommand]
    private async Task ExportCompletedAsync() => await NavigateToOutputAsync("1", "有报告未建档");

    [RelayCommand]
    private async Task ExportAllMonthlyAsync() => await NavigateToOutputAsync(null, "全部导出");

    [RelayCommand]
    private async Task ExportAllHasReportAsync() =>
        await ExportAllByStatusAsync("1", "有报告未建档", "加载全部有报告未建档数据...");

    [RelayCommand]
    private async Task ExportAllSubmittedAsync() =>
        await ExportAllByStatusAsync("0", "已提交申请", "加载全部已提交申请数据...");

    /// <summary>
    /// 全局导出指定核查状态人员（不限月份，与"有报告未建档"全局导出同口径）：
    /// 全时间范围分页拉取 → 同户全员展开（家庭成员随户主一并带出）→ 按身份证去重保留最新 → 跳转打印输出页。
    /// </summary>
    private async Task ExportAllByStatusAsync(string status, string label, string loadingText)
    {
        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness($"导出全部{label}人员");

            var startDate = new DateTime(2000, 1, 1);
            var endDate = new DateTime(2100, 12, 31);

            var allItems = new List<AssetVerificationTask>();
            var pageIndex = 1;
            const int pageSize = 500;

            while (true)
            {
                var result = await _verificationService.SearchByDateRangePagedAsync(
                    startDate, endDate, null, status, pageIndex, pageSize, ct);

                if (result.IsSuccess && result.Value != null)
                {
                    allItems.AddRange(result.Value.Items);
                    if (allItems.Count >= result.Value.TotalCount) break;
                    pageIndex++;
                }
                else break;
            }

            if (allItems.Count == 0)
            {
                await _dialogService.DisplayAlertAsync("提示", $"没有{label}的数据", "确定");
                return;
            }

            // 同户全员展开（含各状态成员行），再按身份证去重只保留最新一条
            var beforeCount = allItems.Count;
            allItems = await ExpandHasReportFamilyMembersAsync(allItems, ct);
            _logger.LogBusiness($"导出全部{label}人员",
                ("TotalCount", beforeCount),
                ("ExpandedCount", allItems.Count));

            var dedupedItems = allItems
                .GroupBy(x => x.ArchiveIdCard)
                .Select(g => g.OrderByDescending(x => x.CreatedAt).First())
                .ToList();

            _logger.LogBusiness($"导出全部{label}人员",
                ("DedupedCount", dedupedItems.Count));

            // 预加载地址数据
            var communityCodes = dedupedItems
                .Where(x => !string.IsNullOrWhiteSpace(x.Community))
                .Select(x => x.Community!);
            await _addressResolver.WarmupAsync(communityCodes, CancellationToken);

            // 整档入口：先清文书模式上下文，防上一次「仅出文书」的静态残留被继承
            PrintNavigationData.ClearDocumentMode();
            PrintNavigationData.BusinessType = "AssetVerificationMonthlyReport";
            PrintNavigationData.BusinessId = null;
            PrintNavigationData.Classification = ClassificationConstants.AssetVerification;
            PrintNavigationData.FieldData = new Dictionary<string, string>
            {
                ["REPORT_PERIOD"] = "全部月份",
                ["REPORT_LABEL"] = label,
                ["REPORT_START_DATE"] = "2000-01-01",
                ["REPORT_END_DATE"] = "2100-12-31"
            };
            PrintNavigationData.TableData = dedupedItems.OrderBy(x => x.HeadIdCard).ThenBy(x => x.Relationship == "Head" ? 0 : x.Relationship == "Spouse" ? 1 : 2).Select(x =>
            {
                var fullAddress = _addressResolver.BuildAddress(x.Community, x.FamilyAddress);
                return new Dictionary<string, string>
                {
                    [FieldKeys.FAMILY_MEMBER_NAME] = x.ArchiveName ?? "",
                    [FieldKeys.FAMILY_MEMBER_ID_CARD] = x.ArchiveIdCard ?? "",
                    [FieldKeys.FAMILY_MEMBER_ADDRESS] = fullAddress,
                    [FieldKeys.FAMILY_MEMBER_RELATION] = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, x.Relationship),
                    [FieldKeys.HEAD_ID_CARD] = x.HeadIdCard ?? ""
                };
            }).ToList();

            await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveOutputPage>();
        }, loadingText);
    }

    private async Task NavigateToOutputAsync(string? statusFilter, string label)
    {
        if (!SelectedMonth.HasValue)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择月份", "确定");
            return;
        }

        if (IsWeeklyMode && !SelectedWeekNumber.HasValue)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择周", "确定");
            return;
        }

        await ExecuteAsync(async ct =>
        {
            var (start, end) = IsWeeklyMode && SelectedWeekNumber.HasValue
                ? ReportPeriodHelper.GetWeeklyPeriod(SelectedYear, SelectedWeekNumber.Value)
                : GetALinePeriod(SelectedYear, SelectedMonth);

            var periodDesc = IsWeeklyMode && SelectedWeekNumber.HasValue
                ? (SelectedWeekNumber == 0
                    ? $"{SelectedYear}年过渡周(7.11~8.2)"
                    : $"{SelectedYear}年第{SelectedWeekNumber}周")
                : $"{SelectedYear}年{SelectedMonth!.Value}月";

            _logger.LogBusiness($"核查报表-{label}",
                ("Year", SelectedYear), ("Month", SelectedMonth!.Value),
                ("WeekNumber", SelectedWeekNumber?.ToString() ?? ""),
                ("PeriodType", IsWeeklyMode ? "weekly" : "monthly"),
                ("Status", statusFilter ?? "全部"));

            var allItems = new List<AssetVerificationTask>();
            var pageIndex = 1;
            const int pageSize = 500;

            while (true)
            {
                var result = await _verificationService.SearchByDateRangePagedAsync(start, end, null, statusFilter, pageIndex, pageSize, ct);

                if (result.IsSuccess && result.Value != null)
                {
                    allItems.AddRange(result.Value.Items);
                    if (allItems.Count >= result.Value.TotalCount) break;
                    pageIndex++;
                }
                else break;
            }

            if (allItems.Count == 0)
            {
                await _dialogService.DisplayAlertAsync("提示", "没有可导出的数据", "确定");
                return;
            }

            // 所有导出（已提交申请/有报告未建档/全部导出）统一口径：同户全员展开，家庭成员随户主一并进入名册
            var beforeCount = allItems.Count;
            allItems = await ExpandHasReportFamilyMembersAsync(allItems, ct);
            _logger.LogBusiness($"核查报表-同户成员展开-{label}",
                ("Before", beforeCount),
                ("After", allItems.Count));

            // 按身份证去重，只保留最新一条
            allItems = allItems
                .GroupBy(x => x.ArchiveIdCard)
                .Select(g => g.OrderByDescending(x => x.CreatedAt).First())
                .ToList();

            // 预加载社区名称
            var communityCodes = allItems
                .Where(x => !string.IsNullOrWhiteSpace(x.Community))
                .Select(x => x.Community!);
            await _addressResolver.WarmupAsync(communityCodes, CancellationToken);

            // 整档入口：先清文书模式上下文，防上一次「仅出文书」的静态残留被继承
            PrintNavigationData.ClearDocumentMode();
            PrintNavigationData.BusinessType = "AssetVerificationMonthlyReport";
            // BusinessId 编码：月报=year*100+month，周报=year*100+weekNumber
            PrintNavigationData.BusinessId = IsWeeklyMode && SelectedWeekNumber.HasValue
                ? long.Parse($"{SelectedYear}{SelectedWeekNumber.Value:D2}")
                : long.Parse($"{SelectedYear}{SelectedMonth!.Value:D2}");
            PrintNavigationData.Classification = ClassificationConstants.AssetVerification;
            PrintNavigationData.FieldData = new Dictionary<string, string>
            {
                ["REPORT_PERIOD"] = periodDesc,
                ["REPORT_LABEL"] = label,
                ["REPORT_START_DATE"] = start.ToString("yyyy-MM-dd"),
                ["REPORT_END_DATE"] = end.AddDays(-1).ToString("yyyy-MM-dd")
            };
            PrintNavigationData.TableData = allItems.OrderBy(x => x.HeadIdCard).ThenBy(x => x.Relationship == "Head" ? 0 : x.Relationship == "Spouse" ? 1 : 2).Select(x =>
            {
                var fullAddress = _addressResolver.BuildAddress(x.Community, x.FamilyAddress);
                return new Dictionary<string, string>
                {
                    [FieldKeys.FAMILY_MEMBER_NAME] = x.ArchiveName ?? "",
                    [FieldKeys.FAMILY_MEMBER_ID_CARD] = x.ArchiveIdCard ?? "",
                    [FieldKeys.FAMILY_MEMBER_ADDRESS] = fullAddress,
                    [FieldKeys.FAMILY_MEMBER_RELATION] = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, x.Relationship),
                    [FieldKeys.HEAD_ID_CARD] = x.HeadIdCard ?? ""
                };
            }).ToList();

            _logger.LogBusiness($"核查报表数据准备完成-{label}", ("TotalCount", allItems.Count));

            await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveOutputPage>();
        }, $"加载{label}数据...");
    }

    /// <summary>
    /// 核查报表导出同户全员展开：
    /// 按名单记录的 head_id_card 取回同户全部核查记录（不筛状态——已建档/被拒成员也一并呈现，由经办人核实），
    /// 与原名单合并后按身份证去重保留最新一条。查询失败时降级返回原名单（不阻断导出）。
    /// 用于全部导出口径（已提交申请/有报告未建档/全部导出），保证整户成员随户主一并带出。
    /// </summary>
    private async Task<List<AssetVerificationTask>> ExpandHasReportFamilyMembersAsync(
        List<AssetVerificationTask> items, CancellationToken ct)
    {
        var headIds = items
            .Where(x => !string.IsNullOrWhiteSpace(x.HeadIdCard))
            .Select(x => x.HeadIdCard!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (headIds.Count == 0) return items;

        var familyResult = await _verificationService.GetByHeadIdsAsync(headIds, ct);
        if (familyResult.IsFailure || familyResult.Value == null)
        {
            _logger.Warn($"同户成员展开失败（降级为原名单导出）: {familyResult.Message}");
            return items;
        }

        return items.Concat(familyResult.Value)
            .GroupBy(x => x.ArchiveIdCard)
            .Select(g => g.OrderByDescending(x => x.CreatedAt).First())
            .ToList();
    }
}
