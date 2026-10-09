using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Enums;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.Printing;
using NewCosmos.Services.Platform;
using NewCosmos.Services.Utilities;
using NewCosmos.ViewModels.ArchiveManagement;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Reprint.Providers;

namespace NewCosmos.ViewModels.Reprint;

public partial class UnifiedReprintViewModel
{
    #region 批量打印

    /// <summary>批量域选中索引（Providers 顺序）</summary>
    [ObservableProperty]
    private int _batchDomainIndex;

    /// <summary>批量关键词（ArchiveSet/核查用；动态管理域按月不过滤关键词）</summary>
    [ObservableProperty]
    private string _batchKeyword = string.Empty;

    /// <summary>批量年份（默认当前年）</summary>
    [ObservableProperty]
    private int _batchYear = DateTime.Now.Year;

    /// <summary>批量月份索引（0=全部；>0 时按该月对应的 B 线周期区间筛选）</summary>
    [ObservableProperty]
    private int _batchMonthIndex;

    /// <summary>批量核查模板集</summary>
    [ObservableProperty]
    private ObservableCollection<TemplateSelectItem> _batchTemplates = new();

    [ObservableProperty]
    private TemplateSelectItem? _selectedBatchTemplate;

    /// <summary>批量筛选出的记录集</summary>
    [ObservableProperty]
    private ObservableCollection<ReprintArchiveItem> _batchRecords = new();

    [ObservableProperty]
    private bool _batchIsGenerating;

    [ObservableProperty]
    private double _batchProgress;

    [ObservableProperty]
    private string _batchProgressText = string.Empty;

    [ObservableProperty]
    private string _batchResultSummary = string.Empty;

    public List<int> BatchYearOptions { get; } = new() { DateTime.Now.Year - 1, DateTime.Now.Year, DateTime.Now.Year + 1 };

    /// <summary>批量月份选项（0=全部，1..12=业务月；月份筛选按域自身时间轴周期区间过滤）</summary>
    public List<string> BatchMonthOptions { get; } = new List<string> { "全部" }
        .Concat(Enumerable.Range(1, 12).Select(m => $"{m}月")).ToList();

    /// <summary>批量模式：0=整份档案（逐户全部适用模板），1=指定模板（逐户该模板）</summary>
    [ObservableProperty]
    private int _batchModeIndex;

    public List<string> BatchModeOptions { get; } = new() { "整份档案（逐户全部适用模板）", "指定模板（逐户该模板）" };

    /// <summary>是否「指定模板」模式</summary>
    public bool IsBatchTemplateMode => BatchModeIndex == 1;

    partial void OnBatchModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsBatchTemplateMode));
        OnPropertyChanged(nameof(ShowBatchTemplatePicker));
        // 切到「指定模板」而模板列表尚未加载时补加载（否则模板下拉为空，运行时报未选择模板）
        if (value == 1 && BatchTemplates.Count == 0)
            _ = LoadBatchTemplatesAsync();
    }

    /// <summary>模板 Picker 可见性：核查域恒显；档案集域仅「指定模板」模式；动态域不显</summary>
    public bool ShowBatchTemplatePicker =>
        !IsBatchDynamicMode && BatchTemplates.Count > 0 && (IsBatchAssetMode || BatchModeIndex == 1);

    /// <summary>批量域显示名列表（Picker）</summary>
    public List<string> BatchDomainOptions => _providers.Select(p => p.DisplayName).ToList();

    /// <summary>批量模式当前域是否为核查域（模板 Picker 可见性）</summary>
    public bool IsBatchAssetMode
    {
        get
        {
            var provider = _providers.ElementAtOrDefault(BatchDomainIndex);
            return provider?.Mode == ReprintDomainMode.AssetVerification;
        }
    }

    /// <summary>批量模式当前域是否为动态管理域（关键词不必填提示）</summary>
    public bool IsBatchDynamicMode
    {
        get
        {
            var provider = _providers.ElementAtOrDefault(BatchDomainIndex);
            return provider?.Mode == ReprintDomainMode.DynamicRecord;
        }
    }

    partial void OnBatchDomainIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsBatchAssetMode));
        OnPropertyChanged(nameof(IsBatchDynamicMode));
        OnPropertyChanged(nameof(HasBatchKeywordInput));
        OnPropertyChanged(nameof(ShowBatchTemplatePicker));
        if (BatchDomainIndex >= 0 && BatchDomainIndex < _providers.Count)
        {
            _ = LoadBatchTemplatesAsync();
            _ = BatchSearchAsync();
        }
    }

    partial void OnBatchYearChanged(int value) => _ = BatchSearchAsync();

    partial void OnBatchMonthIndexChanged(int value) => _ = BatchSearchAsync();

    /// <summary>批量关键词输入是否可见/可用（动态管理域按月名单，无需关键词）</summary>
    public bool HasBatchKeywordInput => !IsBatchDynamicMode;

    /// <summary>批量页月份过滤是否生效（0=全部，不加时间轴过滤）</summary>
    public bool HasBatchMonthFilter => BatchMonthIndex > 0;

    /// <summary>指定年月对应的月份窗口区间（含端点）；month&lt;=0 返回 null（全部）</summary>
    private async Task<(DateTime From, DateTime To)?> GetMonthWindowAsync(int year, int month, ReprintMonthWindow window)
    {
        if (month <= 0) return null;
        var monthStart = new DateTime(year, month, 1);
        switch (window)
        {
            case ReprintMonthWindow.NaturalMonth:
                return (monthStart, monthStart.AddMonths(1).AddDays(-1));
            case ReprintMonthWindow.BusinessProcess:
            {
                var tl = await _timelineService.CalculateTimelineAsync(year, month, TimelineType.BusinessProcess);
                return (tl.CycleStartDate.Date, tl.CycleEndDate.Date);
            }
            case ReprintMonthWindow.EconomicReview:
            {
                var tl = await _timelineService.CalculateTimelineAsync(year, month, TimelineType.EconomicReview);
                return (tl.CycleStartDate.Date, tl.CycleEndDate.Date);
            }
            case ReprintMonthWindow.TempReliefFull:
            {
                var tl = await _timelineService.CalculateTempReliefAsync(year, month, simplified: false);
                return (tl.InvestigationStartDate.Date, tl.AuditDate.Date);
            }
            default:
                return (monthStart, monthStart.AddMonths(1).AddDays(-1));
        }
    }

    /// <summary>月份窗口口径显示名</summary>
    private static string WindowLabel(ReprintMonthWindow window) => window switch
    {
        ReprintMonthWindow.NaturalMonth => "自然月",
        ReprintMonthWindow.BusinessProcess => "B线",
        ReprintMonthWindow.EconomicReview => "A线",
        ReprintMonthWindow.TempReliefFull => "C线",
        _ => "月份"
    };

    /// <summary>批量当月月份窗口区间（含端点）；全部月份返回 null</summary>
    private Task<(DateTime From, DateTime To)?> GetBatchBusinessRangeAsync(IReprintDomainProvider provider)
        => GetMonthWindowAsync(BatchYear, BatchMonthIndex, provider.MonthWindow);

    /// <summary>窗口区间跨的自然月列表（如 7/21~8/20 → (年,7)、(年,8)）</summary>
    private static IEnumerable<(int Year, int Month)> SpannedMonths(DateTime from, DateTime to)
    {
        var cur = new DateTime(from.Year, from.Month, 1);
        var last = new DateTime(to.Year, to.Month, 1);
        while (cur <= last)
        {
            yield return (cur.Year, cur.Month);
            cur = cur.AddMonths(1);
        }
    }

    /// <summary>批量页月份窗口标签（摘要/日志用）</summary>
    private string BatchScopeText((DateTime From, DateTime To)? range, ReprintMonthWindow window)
        => range is { } r
            ? $"{BatchYear}年{BatchMonthIndex}月（{WindowLabel(window)} {r.From:yyyy-MM-dd}~{r.To:yyyy-MM-dd}）"
            : "全部月份";

    /// <summary>
    /// 进入批量 Tab / 页面出现时初始化：按当前域加载模板（自动选中）并拉取一次名单；
    /// 修复"进入批量页未加载模板，导致运行时报未选择模板"。
    /// </summary>
    public async Task EnsureBatchInitializedAsync()
    {
        if (BatchDomainIndex < 0 || BatchDomainIndex >= _providers.Count) return;
        if (BatchTemplates.Count == 0)
            await LoadBatchTemplatesAsync();
        if (BatchRecords.Count == 0)
            await BatchSearchAsync();
    }

    #endregion

    // ========================
    //  核查域：预览/直打（按人补打模式）
    // ========================

    [RelayCommand]
    private async Task PreviewAssetAsync()
    {
        if (SelectedRecord == null || SelectedAssetTemplate == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择核查记录与模板", "确定");
            return;
        }
        try
        {
            IsBusy = true;
            var result = await AssetCapability!.RenderPreviewAsync(SelectedRecord.BusinessId, SelectedAssetTemplate, CancellationToken);
            if (result.IsSuccess && result.Value != null)
            {
                Output.PdfFilePath = result.Value;
                Output.HasPdf = true;
            }
            else
            {
                await _dialogService.DisplayAlertAsync("错误", result.Message ?? "预览生成失败", "确定");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PrintAssetAsync()
    {
        if (SelectedRecord == null || SelectedAssetTemplate == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择核查记录与模板", "确定");
            return;
        }
        try
        {
            IsBusy = true;
            var result = await AssetCapability!.PrintSingleAsync(SelectedRecord.BusinessId, SelectedAssetTemplate, IsGeneratePdf, printToPrinter: true, CancellationToken);
            if (result.IsSuccess)
            {
                await _dialogService.DisplayAlertAsync("打印完成", $"已发送到打印机: {SelectedAssetTemplate.Name}", "确定");
            }
            else
            {
                await _dialogService.DisplayAlertAsync("错误", result.Message ?? "打印失败", "确定");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>核查直打域「保存」：仅生成落盘到输出根（源文件+PDF），不调打印机</summary>
    [RelayCommand]
    private async Task SaveAssetAsync()
    {
        if (SelectedRecord == null || SelectedAssetTemplate == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择核查记录与模板", "确定");
            return;
        }
        try
        {
            IsBusy = true;
            // 保存恒生成 PDF（打印域的 IsGeneratePdf 开关只作用于真打印）
            var result = await AssetCapability!.PrintSingleAsync(SelectedRecord.BusinessId, SelectedAssetTemplate, generatePdf: true, printToPrinter: false, CancellationToken);
            if (result.IsSuccess)
            {
                await _dialogService.DisplayAlertAsync("保存完成",
                    $"已生成到输出目录: {SelectedAssetTemplate.Name}", "确定");
                TryOpenFolder(Helpers.OutputPathHelper.OutputRoot);
            }
            else
            {
                await _dialogService.DisplayAlertAsync("错误", result.Message ?? "保存失败", "确定");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ========================
    //  动态管理域：原样补打 / 重新生成（按人补打模式）
    // ========================

    [RelayCommand]
    private async Task ReprintOriginalAsync()
    {
        if (SelectedDynamicHistory == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择一条打印留痕", "确定");
            return;
        }
        try
        {
            IsBusy = true;
            var result = await DynamicCapability!.ReprintOriginalAsync(SelectedDynamicHistory.Id, CancellationToken);
            if (result.IsSuccess && result.Value != null)
            {
                Output.PdfFilePath = result.Value;
                Output.HasPdf = true;
            }
            else
            {
                await _dialogService.DisplayAlertAsync("提示", result.Message ?? "原样补打失败", "确定");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RegenerateAsync()
    {
        if (SelectedRecord == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择档案记录", "确定");
            return;
        }
        try
        {
            IsBusy = true;
            // 选中历史档案(Stopped)时按该版本原样生成；否则归一到现役
            var normalize = !string.Equals(SelectedRecord.Status, "Stopped", StringComparison.OrdinalIgnoreCase);
            var result = await DynamicCapability!.RegenerateAsync(SelectedRecord.BusinessId, normalize, CancellationToken);
            if (result.IsSuccess && result.Value != null)
            {
                Output.PdfFilePath = result.Value;
                Output.HasPdf = true;
                // 刷新留痕列表（预览已在右栏展示，不再弹出/打开外部程序）
                await PrepareDynamicAsync(SelectedRecord);
            }
            else
            {
                await _dialogService.DisplayAlertAsync("提示", result.Message ?? "重新生成失败", "确定");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>动态管理域：按选中打印机打印当前档案（生成源文件+PDF并留痕）</summary>
    [RelayCommand]
    private async Task PrintDynamicAsync()
    {
        if (SelectedRecord == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择档案记录", "确定");
            return;
        }
        if (string.IsNullOrWhiteSpace(SelectedDynamicPrinter))
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择打印机", "确定");
            return;
        }

        try
        {
            IsBusy = true;
            var result = await DynamicCapability!.PrintAsync(
                SelectedRecord.BusinessId, SelectedDynamicPrinter, Math.Max(1, DynamicCopies), CancellationToken);

            if (result.IsFailure)
            {
                await _dialogService.DisplayAlertAsync("错误", result.Message ?? "打印失败", "确定");
                return;
            }

            var (success, failed) = result.Value;
            var msg = $"已发送 {success} 户到打印机（{SelectedDynamicPrinter}）";
            if (failed.Count > 0)
                msg += $"\n失败 {failed.Count} 户";
            await _dialogService.DisplayAlertAsync("打印完成", msg, "确定");
            await PrepareDynamicAsync(SelectedRecord);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ========================
    //  批量打印（Tab2，五域通用）
    // ========================

    /// <summary>批量模板加载（核查域=核查类模板；ArchiveSet 域无需模板选择——逐户打全部适用模板；动态管理域=单模板月度合并）</summary>
    private async Task LoadBatchTemplatesAsync()
    {
        BatchTemplates.Clear();
        SelectedBatchTemplate = null;
        var provider = _providers.ElementAtOrDefault(BatchDomainIndex);
        if (provider == null) return;

        if (provider.Mode == ReprintDomainMode.AssetVerification)
        {
            if (provider is IAssetVerificationReprintCapability cap)
            {
                var templates = await cap.GetTemplatesAsync(CancellationToken);
                foreach (var t in templates)
                    BatchTemplates.Add(t);
                SelectedBatchTemplate = templates.Count > 0 ? templates[0] : null;
            }
            OnPropertyChanged(nameof(ShowBatchTemplatePicker));
            return;
        }

        if (provider.Mode == ReprintDomainMode.DynamicRecord)
        {
            OnPropertyChanged(nameof(ShowBatchTemplatePicker));
            return;
        }

        // ArchiveSet/其它域：按域限定加载模板库，供「指定模板」批量
        var allResult = await _templateService.GetAllAsync(CancellationToken);
        if (allResult.IsFailure)
        {
            // 显式失败：批量模板列表不加载并提示用户，不把 DB 故障当"无模板"静默
            await _dialogService.DisplayAlertAsync("错误", $"加载模板库失败: {allResult.Message}", "确定");
            return;
        }
        var all = allResult.Value ?? new List<NewCosmos.Models.Entities.Template>();
        // 优先按模板分类（与档案制作同源）；未接入域回退名称前缀
        var scope = ArchiveCategoryResolver.GetReprintDomainCategories(provider.DomainKey);
        foreach (var t in all.Where(t => scope is null
                     ? MatchesDomainTemplate(t.Name, provider.DomainKey)
                     : t.Categories.Intersect(scope).Any()))
        {
            BatchTemplates.Add(new TemplateSelectItem
            {
                TemplateId = t.Id,
                Name = t.Name,
                FileType = t.FileType,
                IsSelected = true,
                IsApplicable = true,
                BaseCopies = 1
            });
        }
        SelectedBatchTemplate = BatchTemplates.Count > 0 ? BatchTemplates[0] : null;
        OnPropertyChanged(nameof(ShowBatchTemplatePicker));
    }

    /// <summary>域模板过滤兜底（名称前缀；已接入域走 ArchiveCategoryResolver.GetReprintDomainCategories）</summary>
    private static bool MatchesDomainTemplate(string name, string domainKey) => domainKey switch
    {
        "FamilyApplication" => name.StartsWith("档案_") || name.StartsWith("通用_"),
        "TempRelief" => name.StartsWith("临时救助"),
        "ElderlyBenefits" => name.StartsWith("普惠高龄"),
        "DynamicManagementRecord" => name.Contains("动态管理记录"),
        _ => true
    };

    /// <summary>批量筛选：按域 + 关键词 + 月份（B线周期区间）拉取记录集</summary>
    [RelayCommand]
    private async Task BatchSearchAsync()
    {
        var provider = _providers.ElementAtOrDefault(BatchDomainIndex);
        if (provider == null) return;

        try
        {
            IsBusy = true;
            BatchRecords.Clear();
            BatchResultSummary = string.Empty;

            var range = await GetBatchBusinessRangeAsync(provider);
            var scope = BatchScopeText(range, provider.MonthWindow);

            if (provider.Mode == ReprintDomainMode.DynamicRecord)
            {
                // 动态管理批量：按周期区间变更人群（不按关键词过滤），预览户数后合并导出
                if (range == null)
                {
                    BatchResultSummary = "动态管理档案请选择具体月份（不支持「全部」）";
                    return;
                }
                var listResult = await SearchByBusinessRangeAsync(provider, range.Value.From, range.Value.To, CancellationToken);
                if (listResult.IsFailure)
                {
                    await _dialogService.DisplayAlertAsync("提示", listResult.Message ?? "变更人群查询失败", "确定");
                    return;
                }
                foreach (var r in listResult.Value)
                    BatchRecords.Add(r);
                BatchResultSummary = BatchRecords.Count == 0
                    ? $"{scope} 无变更档案"
                    : $"{scope} 变更人群 {BatchRecords.Count} 户（执行后合并为一份 PDF）";
                return;
            }

            var keyword = BatchKeyword?.Trim();
            List<ReprintArchiveItem> records;
            if (range == null)
            {
                // 全部月份：默认最近名单（关键词可空）
                var search = await provider.SearchByPersonAsync(keyword!, 500, CancellationToken);
                if (search.IsFailure)
                {
                    await _dialogService.DisplayAlertAsync("提示", search.Message ?? "批量筛选失败", "确定");
                    return;
                }
                records = search.Value ?? new List<ReprintArchiveItem>();
            }
            else
            {
                // 指定月份：按域自身时间轴周期区间合并自然月查询后过滤
                var listResult = await SearchByBusinessRangeAsync(provider, range.Value.From, range.Value.To, CancellationToken);
                if (listResult.IsFailure)
                {
                    await _dialogService.DisplayAlertAsync("提示", listResult.Message ?? "批量筛选失败", "确定");
                    return;
                }
                records = listResult.Value;
                if (!string.IsNullOrEmpty(keyword))
                    records = records
                        .Where(r => (r.Name?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                                 || (r.IdCard?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
                        .ToList();
            }

            foreach (var r in records)
                BatchRecords.Add(r);

            BatchResultSummary = records.Count == 0
                ? $"{scope} 无记录"
                : $"{scope} 共 {records.Count} 条记录";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量筛选失败");
            await _dialogService.DisplayAlertAsync("错误", $"批量筛选失败: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 单域按月搜索：根据域的 MonthWindow 计算窗口区间，再调用 SearchByBusinessRangeAsync。
    /// </summary>
    private async Task<Result<List<ReprintArchiveItem>>> SearchByProviderMonthAsync(
        IReprintDomainProvider provider, int year, int month, CancellationToken ct)
    {
        var range = await GetMonthWindowAsync(year, month, provider.MonthWindow);
        if (range == null)
            return Result.Success(new List<ReprintArchiveItem>());
        return await SearchByBusinessRangeAsync(provider, range.Value.From, range.Value.To, ct);
    }

    /// <summary>
    /// 按时间轴周期区间查询：合并区间所跨自然月的服务端查询，再按 [from, to] 过滤并去重。
    /// （各域服务端仅有自然月查询，周期区间必然落在相邻两个月。）
    /// </summary>
    private async Task<Result<List<ReprintArchiveItem>>> SearchByBusinessRangeAsync(
        IReprintDomainProvider provider, DateTime from, DateTime to, CancellationToken ct)
    {
        var all = new List<ReprintArchiveItem>();
        var failed = false;
        string? failMsg = null;
        foreach (var (year, month) in SpannedMonths(from, to))
        {
            var r = await provider.SearchByMonthAsync(year, month, 500, ct);
            if (r.IsSuccess && r.Value != null) all.AddRange(r.Value);
            else { failed = true; failMsg = r.Message; }
        }
        if (failed && all.Count == 0)
            return Result.Failure<List<ReprintArchiveItem>>(ErrorCodes.DB_QUERY_ERROR, failMsg ?? "按月查询失败");

        var hiExclusive = to.Date.AddDays(1);
        var filtered = all
            .Where(x => x.BusinessTime == DateTime.MinValue
                        || (x.BusinessTime.Date >= from.Date && x.BusinessTime.Date < hiExclusive))
            .GroupBy(x => (x.DomainKey, x.BusinessId))
            .Select(g => g.First())
            .ToList();
        return Result.Success(filtered);
    }

    private Task<List<long>> GetMonthlyIdsAsync() => Task.FromResult(new List<long>());    /// <summary>批量执行：按域模式分派</summary>
    [RelayCommand]
    private async Task BatchRunAsync()
    {
        var provider = _providers.ElementAtOrDefault(BatchDomainIndex);
        if (provider == null) return;

        try
        {
            BatchIsGenerating = true;
            BatchResultSummary = string.Empty;

            switch (provider.Mode)
            {
                case ReprintDomainMode.AssetVerification:
                    await BatchRunAssetAsync(provider);
                    break;
                case ReprintDomainMode.DynamicRecord:
                    await BatchRunDynamicAsync(provider);
                    break;
                case ReprintDomainMode.ArchiveSet:
                    await BatchRunArchiveSetAsync(provider);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            BatchResultSummary = "批量打印已取消";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量打印异常");
            BatchResultSummary = $"批量打印异常: {ex.Message}";
        }
        finally
        {
            BatchIsGenerating = false;
        }
    }

    /// <summary>核查域批量：单模板 × 筛选记录逐户直打（自 ReprintSelectionViewModel 平移）</summary>
    private async Task BatchRunAssetAsync(IReprintDomainProvider provider)
    {
        var capability = provider as IAssetVerificationReprintCapability;
        if (capability == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "资产核查批量能力未注册", "确定");
            return;
        }
        // 兜底：模板下拉尚未同步时自动取首个可用模板，避免误报"请选择模板"
        var template = SelectedBatchTemplate ?? BatchTemplates.FirstOrDefault();
        if (template == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "未找到可用的核查打印模板，请刷新名单后重试", "确定");
            return;
        }
        SelectedBatchTemplate ??= template;
        if (BatchRecords.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "当前筛选范围内没有记录", "确定");
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync("确认",
            $"即将批量打印 {BatchRecords.Count} 条记录的「{template.Name}」\n是否继续？", "确定", "取消");
        if (!confirm) return;

        var ids = BatchRecords.Select(r => r.BusinessId).ToList();
        var result = await capability.BatchPrintAsync(ids, template, IsGeneratePdf,
            (current, total, name) =>
            {
                BatchProgress = (double)current / Math.Max(total, 1);
                BatchProgressText = $"正在打印 ({current}/{total}): {name}";
            }, CancellationToken);

        if (result.IsSuccess)
        {
            var (successCount, failed) = result.Value;
            var message = $"打印完成: 成功 {successCount} 个";
            if (failed.Count > 0)
            {
                message += $"\n失败 {failed.Count} 个:\n{string.Join("\n", failed.Take(5).Select(f => $"{f.Name}: {f.Error}"))}";
                if (failed.Count > 5) message += $"\n...等 {failed.Count - 5} 个";
            }
            BatchResultSummary = message;
            _logger.LogBusiness("统一补打中心·核查批量完成", ("Success", successCount), ("Failed", failed.Count));
        }
        else
        {
            BatchResultSummary = $"批量打印失败: {result.Message}";
        }
    }

    /// <summary>动态管理域批量：按 B 线周期区间变更人群合并导出</summary>
    private async Task BatchRunDynamicAsync(IReprintDomainProvider provider)
    {
        var capability = provider as IDynamicRecordReprintCapability;
        if (capability == null)
        {
            BatchResultSummary = "动态管理批量能力未注册";
            return;
        }

        var range = await GetBatchBusinessRangeAsync(provider);
        if (range == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "动态管理档案请选择具体月份（周期）后再批量导出", "确定");
            return;
        }

        BatchProgressText = $"正在按 {BatchScopeText(range, provider.MonthWindow)} 变更人群导出...";
        var result = await capability.BatchExportRangeAsync(range.Value.From, range.Value.To, CancellationToken);
        if (result.IsSuccess && result.Value != null)
        {
            BatchProgress = 1;
            BatchProgressText = "批量导出完成";
            BatchResultSummary = $"已合并导出 {BatchRecords.Count} 户：\n{result.Value}";
        }
        else
        {
            BatchResultSummary = $"批量导出失败: {result.Message}";
        }
    }

    /// <summary>ArchiveSet 域批量：逐户构建打印数据 → 全适用模板静默打印（PrintAllCoreAsync）</summary>
    private async Task BatchRunArchiveSetAsync(IReprintDomainProvider provider)
    {
        if (BatchRecords.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "当前筛选范围内没有记录", "确定");
            return;
        }

        // 「指定模板」模式：运行前解析模板（下拉未同步时兜底取首个），避免误报未选择模板
        var batchTemplate = IsBatchTemplateMode
            ? (SelectedBatchTemplate ?? BatchTemplates.FirstOrDefault())
            : null;
        if (IsBatchTemplateMode && batchTemplate == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请在「模板」下拉选择要批量打印的模板", "确定");
            return;
        }
        if (IsBatchTemplateMode && SelectedBatchTemplate == null)
            SelectedBatchTemplate = batchTemplate;

        var confirm = await _dialogService.DisplayAlertAsync("确认",
            IsBatchTemplateMode && batchTemplate != null
                ? $"即将逐户打印 {BatchRecords.Count} 户的「{batchTemplate.Name}」\n是否继续？"
                : $"即将逐户打印 {BatchRecords.Count} 户的全部适用模板（{provider.DisplayName}）\n是否继续？",
            "确定", "取消");
        if (!confirm) return;

        var successCount = 0;
        var failedItems = new List<(string Name, string Error)>();

        for (var i = 0; i < BatchRecords.Count; i++)
        {
            CancellationToken.ThrowIfCancellationRequested();
            var record = BatchRecords[i];
            BatchProgress = (double)i / BatchRecords.Count;
            BatchProgressText = $"正在打印 ({i + 1}/{BatchRecords.Count}): {record.Name}";

            try
            {
                var prepare = await provider.PrepareAsync(record.BusinessId, CancellationToken);
                if (prepare.IsFailure || prepare.Value == null)
                {
                    failedItems.Add((record.Name, prepare.Message ?? "构建打印数据失败"));
                    continue;
                }
                var payload = prepare.Value;

                // 批量逐户也是记录型入口：先清文书模式上下文，防上一次会话残留污染模板候选集
                PrintNavigationData.ClearDocumentMode();
                SupportsDocMode = payload.DomainKey is "FamilyApplication" or "EconomicReview";
                PrintNavigationData.BusinessType = payload.DomainKey;
                PrintNavigationData.BusinessId = payload.BusinessId;
                PrintNavigationData.Classification = payload.Classification;
                PrintNavigationData.Status = string.IsNullOrEmpty(payload.Status) ? record.Status : payload.Status;
                PrintNavigationData.FieldData = payload.FieldData;
                PrintNavigationData.TableData = payload.TableData;
                PrintNavigationData.SupporterTableData = payload.SupporterTableData;

                await Output.InitializeAsync();
                _logger.LogBusiness("统一补打中心·批量单户模板",
                    ("BusinessType", payload.DomainKey),
                    ("Classification", payload.Classification ?? ""),
                    ("Templates", Output.Templates.Count),
                    ("Selected", Output.Templates.Count(t => t.IsSelected && t.IsApplicable)),
                    ("Mode", IsBatchTemplateMode ? "指定模板" : "整份档案"));
                if (batchTemplate != null)
                    await Output.PrintTemplateForBatchAsync(batchTemplate);
                else
                    await Output.PrintAllCoreAsync(interactive: false);
                successCount++;
            }
            catch (Exception ex)
            {
                failedItems.Add((record.Name, ex.Message));
                _logger.LogError(ex, $"批量打印单户失败: {record.Name}");
            }
        }

        BatchProgress = 1;
        var summary = $"打印完成: 成功 {successCount} 户";
        if (failedItems.Count > 0)
        {
            summary += $"\n失败 {failedItems.Count} 户:\n{string.Join("\n", failedItems.Take(5).Select(f => $"{f.Name}: {f.Error}"))}";
            if (failedItems.Count > 5) summary += $"\n...等 {failedItems.Count - 5} 户";
        }
        BatchResultSummary = summary;
        _logger.LogBusiness("统一补打中心·档案集批量完成", ("Success", successCount), ("Failed", failedItems.Count));
    }
}
