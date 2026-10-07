using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.Platform;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Reporting;
using System.Collections.ObjectModel;
using System.Diagnostics;

using NewCosmos.Helpers;

namespace NewCosmos.ViewModels.ElderlyBenefits;

/// <summary>
/// 普惠高龄月报表 ViewModel
/// 自然月统计周期：选年月 → 加载该月新增/停止人员 → 勾选明细表 → 预览/打印/导出（复用一个 PDF 预览区）
/// </summary>
public partial class ElderlyReportViewModel : ViewModelBase
{
    private readonly IElderlyApplicationService _applicationService;
    private readonly IPrintService _printService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IPrinterService _printerService;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    public ObservableCollection<int> YearOptions { get; } = new();
    public ObservableCollection<int> MonthOptions { get; } = new(Enumerable.Range(1, 12));

    /// <summary>
    /// 明细表勾选列表（新增明细表 / 停止明细表）
    /// </summary>
    public ObservableCollection<MonthlyFormOption> Forms { get; } = new();

    [ObservableProperty]
    private int _selectedYear;

    [ObservableProperty]
    private int _selectedMonth;

    /// <summary>
    /// 自然月周期提示（如 2026-08-01 ~ 2026-08-31）
    /// </summary>
    [ObservableProperty]
    private string _cycleRangeText = string.Empty;

    [ObservableProperty]
    private int _newCount;

    [ObservableProperty]
    private int _stopCount;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _reportTitleText = string.Empty;

    /// <summary>
    /// 打印份数
    /// </summary>
    [ObservableProperty]
    private int _copies = 1;

    // PDF 预览
    [ObservableProperty]
    private string _pdfPreviewUrl = string.Empty;

    [ObservableProperty]
    private bool _hasPdfPreview;

    public ElderlyReportViewModel(
        IElderlyApplicationService applicationService,
        IPrintService printService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        IPrinterService printerService)
    {
        _applicationService = applicationService;
        _printService = printService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _printerService = printerService;
        Title = "高龄津贴月报表";

        var now = DateTime.Today;
        var startYear = now.Year - 2;
        for (var y = startYear; y <= now.Year; y++)
            YearOptions.Add(y);
        SelectedYear = now.Year;
        SelectedMonth = now.Month;

        foreach (var (key, label) in ElderlyBenefitConstants.GetMonthlyDetailFormOptions())
        {
            var option = new MonthlyFormOption(key, label);
            option.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MonthlyFormOption.IsSelected))
                {
                    PreviewSelectedCommand.NotifyCanExecuteChanged();
                    PrintSelectedCommand.NotifyCanExecuteChanged();
                    ExportSelectedCommand.NotifyCanExecuteChanged();
                }
            };
            Forms.Add(option);
        }
    }

    partial void OnSelectedYearChanged(int value) => RefreshCycleRange();
    partial void OnSelectedMonthChanged(int value) => RefreshCycleRange();

    /// <summary>
    /// 自然月周期：所选月 1 日 ~ 所选月最后一天
    /// </summary>
    private void RefreshCycleRange()
    {
        if (SelectedYear <= 0 || SelectedMonth <= 0) return;
        var days = DateTime.DaysInMonth(SelectedYear, SelectedMonth);
        CycleRangeText = $"{SelectedYear:D4}-{SelectedMonth:D2}-01 ~ {SelectedYear:D4}-{SelectedMonth:D2}-{days:D2}";
        ReportTitleText = $"{SelectedYear}年{SelectedMonth}月普惠高龄月度明细（自然月）";
    }

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await LoadRecordsAsync();
    }

    [RelayCommand]
    private async Task LoadRecordsAsync()
    {
        await ExecuteAsync(async ct =>
        {
            if (SelectedYear <= 0 || SelectedMonth <= 0) return;

            var newResult = await _applicationService.GetByMonthAsync(SelectedYear, SelectedMonth, false, null, excludeImported: true, ct);
            var stopResult = await _applicationService.GetByMonthAsync(SelectedYear, SelectedMonth, true, null, excludeImported: false, ct);

            if (newResult.IsFailure)
            {
                _logger.Error($"加载普惠高龄新增记录失败: {newResult.Message}");
                return;
            }
            if (stopResult.IsFailure)
            {
                _logger.Error($"加载普惠高龄停止记录失败: {stopResult.Message}");
                return;
            }

            NewCount = (newResult.Value ?? new List<ElderlyApplication>()).Count;
            StopCount = (stopResult.Value ?? new List<ElderlyApplication>()).Count;

            StatusText = $"新增 {NewCount} 人，停止 {StopCount} 人";
        }, "加载月报表数据...");
    }

    /// <summary>
    /// 预览勾选的明细表（合并为一份 PDF）
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedForms))]
    private async Task PreviewSelectedAsync()
    {
        var keys = Forms.Where(f => f.IsSelected).Select(f => f.FormKey).ToList();
        if (keys.Count == 0)
        {
            await ShowErrorAsync("请先勾选要预览的明细表");
            return;
        }

        await ExecuteAsync(async ct =>
        {
            var result = await _printService.RenderElderlyMonthlyFormsMergedAsync(SelectedYear, SelectedMonth, keys, ct);
            if (result.IsFailure)
            {
                await ShowErrorAsync(result.Message);
                return;
            }
            if (result.Value.Length == 0)
            {
                await _dialogService.DisplayAlertAsync("提示", "勾选的明细表本月份均无数据，未生成预览", "确定");
                return;
            }

            var filePath = Path.Combine(OutputPathHelper.GetTempDirectory(), $"elderly_{SelectedYear}{SelectedMonth:D2}_selected.pdf");
            // 先清空再赋值，强制 PdfPreviewView 重新加载（同路径时值不变不会触发刷新）
            PdfPreviewUrl = "";
            HasPdfPreview = false;
            await File.WriteAllBytesAsync(filePath, result.Value, ct);
            PdfPreviewUrl = filePath;
            HasPdfPreview = true;
        }, "正在生成预览...");
    }

    /// <summary>
    /// 打印勾选的明细表（默认打印机，可设置份数）
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedForms))]
    private async Task PrintSelectedAsync()
    {
        var keys = Forms.Where(f => f.IsSelected).Select(f => f.FormKey).ToList();
        if (keys.Count == 0)
        {
            await ShowErrorAsync("请先勾选要打印的明细表");
            return;
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
                var isAge90 = ElderlyBenefitConstants.IsAge90Form(key);
                var isStop = ElderlyBenefitConstants.IsStopForm(key);
                var categoryCode = ElderlyBenefitConstants.GetCategoryCodeFromFormKey(key);
                var result = isAge90
                    ? await _printService.PrintElderlyAge90FormAsync(SelectedYear, SelectedMonth, printer, copies, ct)
                    : await _printService.PrintElderlyMonthlyFormAsync(SelectedYear, SelectedMonth, isStop, categoryCode, printer, copies, ct);
                if (result.IsFailure)
                {
                    if (result.ErrorCode == ErrorCodes.NOT_FOUND && result.Message?.Contains("无数据") == true)
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
            await _dialogService.DisplayAlertAsync("提示", $"已发送 {printed} 张明细表到打印机（{printer}）{skipText}", "确定");
        }, "正在打印...");
    }

    /// <summary>
    /// 导出勾选明细表的源文件（xlsx）+ PDF 到所选目录，完成后打开目录
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedForms))]
    private async Task ExportSelectedAsync()
    {
        var keys = Forms.Where(f => f.IsSelected).Select(f => f.FormKey).ToList();
        if (keys.Count == 0)
        {
            await ShowErrorAsync("请先勾选要导出的明细表");
            return;
        }

        var folder = await PickExportFolderAsync("选择明细表导出目录");
        if (string.IsNullOrWhiteSpace(folder)) return;

        var savedFiles = new List<string>();
        var skipped = new List<string>();

        await ExecuteAsync(async ct =>
        {
            foreach (var key in keys)
            {
                var isAge90 = ElderlyBenefitConstants.IsAge90Form(key);
                var isStop = ElderlyBenefitConstants.IsStopForm(key);
                var categoryCode = ElderlyBenefitConstants.GetCategoryCodeFromFormKey(key);

                // 源文件（可再编辑）
                var sourceResult = isAge90
                    ? await _printService.RenderElderlyAge90FormToSourceAsync(SelectedYear, SelectedMonth, ct)
                    : await _printService.RenderElderlyMonthlyFormToSourceAsync(SelectedYear, SelectedMonth, isStop, categoryCode, ct);
                if (sourceResult.IsFailure)
                {
                    if (sourceResult.ErrorCode == ErrorCodes.NOT_FOUND && sourceResult.Message?.Contains("无数据") == true)
                    {
                        skipped.Add(key);
                        continue;
                    }
                    await ShowErrorAsync($"导出失败（{key}）：{sourceResult.Message}");
                    return;
                }
                foreach (var file in sourceResult.Value)
                {
                    var path = Path.Combine(folder, file.FileName + file.Extension);
                    await File.WriteAllBytesAsync(path, file.Bytes, ct);
                    savedFiles.Add(Path.GetFileName(path));
                }

                // PDF
                var pdfResult = isAge90
                    ? await _printService.RenderElderlyAge90FormAsync(SelectedYear, SelectedMonth, ct)
                    : await _printService.RenderElderlyMonthlyFormAsync(SelectedYear, SelectedMonth, isStop, categoryCode, ct);
                if (pdfResult.IsFailure)
                {
                    await ShowErrorAsync($"导出失败（{key}）：{pdfResult.Message}");
                    return;
                }
                var pdfName = isAge90
                    ? $"{SelectedYear:D4}{SelectedMonth:D2}_满90周岁调整备案表.pdf"
                    : $"{SelectedYear:D4}{SelectedMonth:D2}_{categoryCode}{(isStop ? "停止" : "新增")}明细表.pdf";
                var pdfPath = Path.Combine(folder, pdfName);
                await File.WriteAllBytesAsync(pdfPath, pdfResult.Value, ct);
                savedFiles.Add(Path.GetFileName(pdfPath));
            }
        }, "正在导出...");

        if (savedFiles.Count > 0)
        {
            var skipText = skipped.Count > 0 ? $"无数据跳过：{string.Join("、", skipped)}" : null;
            await ShowExportSuccessAsync(folder, savedFiles, skipText);
        }
        else if (skipped.Count > 0)
        {
            await _dialogService.DisplayAlertAsync("提示", $"所选明细表均无数据，未导出文件：{string.Join("、", skipped)}", "确定");
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

    private async Task ShowErrorAsync(string message)
    {
        await _dialogService.DisplayAlertAsync("错误", message ?? "操作失败", "确定");
    }
}
