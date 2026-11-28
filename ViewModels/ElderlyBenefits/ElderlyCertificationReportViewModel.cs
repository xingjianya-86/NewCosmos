using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace NewCosmos.ViewModels.ElderlyBenefits;

/// <summary>
/// 高龄年度认证报表页：选择批次 → 勾选 ROSTER/SUMMARY/UNVERIFIED → 导出 Excel/PDF、打印。
/// 查看与导出都只要求 ELDERLY_VIEW。
/// </summary>
public partial class ElderlyCertificationReportViewModel : ViewModelBase
{
    private readonly IElderlyCertificationService _certificationService;
    private readonly ElderlyCertificationReportService _reportService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly INewPermissionService _permissionService;

    private long _campaignId;
    private bool _initialized;

    #region 抽象属性实现

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    #endregion

    public ObservableCollection<ElderlyCertCampaign> Campaigns { get; } = new();

    [ObservableProperty]
    private ElderlyCertCampaign? _selectedCampaign;

    [ObservableProperty]
    private string _campaignInfo = "未选择认证批次";

    [ObservableProperty]
    private bool _exportRoster = true;

    [ObservableProperty]
    private bool _exportSummary = true;

    [ObservableProperty]
    private bool _exportUnverified = true;

    [ObservableProperty]
    private bool _canView;

    /// <summary>输出目录（{输出根}\高龄认证\{年度}）。</summary>
    [ObservableProperty]
    private string _outputFolder = string.Empty;

    /// <summary>最近一次导出的文件路径（换行分隔）。</summary>
    [ObservableProperty]
    private string _lastExportText = string.Empty;

    public ElderlyCertificationReportViewModel(
        IElderlyCertificationService certificationService,
        ElderlyCertificationReportService reportService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        INewPermissionService permissionService)
    {
        _certificationService = certificationService;
        _reportService = reportService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _permissionService = permissionService;

        Title = "认证报表";
    }

    #region 生命周期

    /// <summary>由认证名单页导航时注入要导出的批次ID。</summary>
    public void Initialize(long campaignId)
    {
        _campaignId = campaignId;
        _initialized = true;
    }

    public override async Task OnAppearingAsync()
    {
        if (!_initialized) return;

        await LoadPermissionAsync();
        if (!CanView)
        {
            await _dialogService.DisplayAlertAsync(
                "无权限", "当前账号没有查看高龄认证的权限，不能导出认证报表。", "确定");
            return;
        }

        await LoadCampaignsAsync();
    }

    private async Task LoadPermissionAsync()
    {
        try
        {
            var userId = App.CurrentUserId ?? 0;
            if (userId <= 0)
            {
                CanView = false;
                return;
            }

            var permissions = await _permissionService.CheckPermissionsAsync(
                userId, new[] { PermissionCodes.ELDERLY_VIEW }, CancellationToken);
            CanView = permissions.TryGetValue(PermissionCodes.ELDERLY_VIEW, out var canView) && canView;
        }
        catch (Exception ex)
        {
            CanView = false;
            _logger.LogError(ex, "高龄认证报表权限加载失败");
        }
    }

    private async Task LoadCampaignsAsync()
    {
        await ExecuteAsync(async () =>
        {
            var result = await _certificationService.GetCampaignsAsync(CancellationToken);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);

            var list = result.Value ?? new List<ElderlyCertCampaign>();
            Campaigns.Clear();
            foreach (var campaign in list)
                Campaigns.Add(campaign);

            SelectedCampaign = list.FirstOrDefault(c => c.Id == _campaignId) ?? list.FirstOrDefault();
            return Result.Success();
        }, "加载认证批次...");
    }

    partial void OnSelectedCampaignChanged(ElderlyCertCampaign? value)
    {
        UpdateCampaignInfo();
        UpdateOutputFolder();
    }

    private void UpdateCampaignInfo()
    {
        CampaignInfo = SelectedCampaign == null
            ? "未选择认证批次"
            : $"{SelectedCampaign.DisplayName} · {SelectedCampaign.PeriodDisplay} · {SelectedCampaign.StatusName}";
    }

    private void UpdateOutputFolder()
    {
        if (SelectedCampaign == null)
        {
            OutputFolder = string.Empty;
            return;
        }

        try
        {
            OutputFolder = OutputPathHelper.GetPeriodDirectory(
                OutputCategories.ElderlyCertification,
                SelectedCampaign.Year.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            OutputFolder = string.Empty;
            _logger.Warn($"高龄认证报表输出目录创建失败: {ex.Message}");
        }
    }

    #endregion

    #region 命令

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (!CanView)
        {
            await _dialogService.DisplayAlertAsync(
                "无权限", "当前账号没有查看高龄认证的权限，不能导出认证报表。", "确定");
            return;
        }
        if (SelectedCampaign == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择认证批次", "确定");
            return;
        }

        var keys = GetSelectedKeys();
        if (keys.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "请至少勾选一种报表", "确定");
            return;
        }

        var campaign = SelectedCampaign;
        string folder;
        try
        {
            folder = OutputPathHelper.GetPeriodDirectory(
                OutputCategories.ElderlyCertification,
                campaign.Year.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            await _dialogService.DisplayAlertAsync("导出失败", $"创建输出目录失败：{ex.Message}", "确定");
            return;
        }

        var savedFiles = new List<string>();
        var result = await ExecuteAsync(async () =>
        {
            foreach (var formKey in keys)
            {
                var reportResult = await _reportService.GetReportAsync(campaign.Id, formKey, CancellationToken);
                if (reportResult.IsFailure)
                    return Result.Failure(reportResult.ErrorCode!, reportResult.Message!);

                var excelResult = ElderlyCertificationExcelExporter.Export(reportResult.Value);
                if (excelResult.IsFailure)
                    return Result.Failure(excelResult.ErrorCode!, excelResult.Message!);

                var fileName = $"{campaign.CampaignNo}_{formKey}_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
                var path = Path.Combine(folder, fileName);
                await File.WriteAllBytesAsync(path, excelResult.Value, CancellationToken);
                savedFiles.Add(path);

                Logger.LogBusiness("导出高龄年度认证报表",
                    ("CampaignId", campaign.Id),
                    ("CampaignNo", campaign.CampaignNo),
                    ("FormKey", formKey),
                    ("Rows", reportResult.Value.TotalRows));
            }

            return Result.Success();
        }, "正在导出认证报表...");

        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("导出失败", result.Message, "确定");
            return;
        }

        LastExportText = string.Join(Environment.NewLine, savedFiles);
        await ShowExportSuccessAsync(
            folder,
            savedFiles.Select(Path.GetFileName).ToList(),
            $"共 {savedFiles.Count} 个报表");
    }

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        if (!CanView)
        {
            await _dialogService.DisplayAlertAsync(
                "无权限", "当前账号没有查看高龄认证的权限，不能导出认证报表。", "确定");
            return;
        }
        if (SelectedCampaign == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择认证批次", "确定");
            return;
        }

        var keys = GetSelectedKeys();
        if (keys.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "请至少勾选一种报表", "确定");
            return;
        }

        var campaign = SelectedCampaign;
        string folder;
        try
        {
            folder = OutputPathHelper.GetPeriodDirectory(
                OutputCategories.ElderlyCertification,
                campaign.Year.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            await _dialogService.DisplayAlertAsync("导出失败", $"创建输出目录失败：{ex.Message}", "确定");
            return;
        }

        var savedFiles = new List<string>();
        var result = await ExecuteAsync(async () =>
        {
            foreach (var formKey in keys)
            {
                var reportResult = await _reportService.GetReportAsync(campaign.Id, formKey, CancellationToken);
                if (reportResult.IsFailure)
                    return Result.Failure(reportResult.ErrorCode!, reportResult.Message!);

                var pdfResult = ElderlyCertificationPdfRenderer.Render(reportResult.Value);
                if (pdfResult.IsFailure)
                    return Result.Failure(pdfResult.ErrorCode!, pdfResult.Message!);

                var fileName = $"{campaign.CampaignNo}_{formKey}_{DateTime.Now:yyyyMMddHHmmss}.pdf";
                var path = Path.Combine(folder, fileName);
                await File.WriteAllBytesAsync(path, pdfResult.Value, CancellationToken);
                savedFiles.Add(path);

                Logger.LogBusiness("导出高龄年度认证报表 PDF",
                    ("CampaignId", campaign.Id),
                    ("CampaignNo", campaign.CampaignNo),
                    ("FormKey", formKey),
                    ("Rows", reportResult.Value.TotalRows),
                    ("Bytes", pdfResult.Value.Length));
            }

            return Result.Success();
        }, "正在导出认证 PDF...");

        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("导出失败", result.Message, "确定");
            return;
        }

        LastExportText = string.Join(Environment.NewLine, savedFiles);
        await ShowExportSuccessAsync(
            folder,
            savedFiles.Select(Path.GetFileName).ToList(),
            $"共 {savedFiles.Count} 个 PDF 报表");
    }

    [RelayCommand]
    private async Task PrintPdfAsync()
    {
        if (!CanView)
        {
            await _dialogService.DisplayAlertAsync(
                "无权限", "当前账号没有查看高龄认证的权限，不能打印认证报表。", "确定");
            return;
        }
        if (SelectedCampaign == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择认证批次", "确定");
            return;
        }

        var keys = GetSelectedKeys();
        if (keys.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "请至少勾选一种报表", "确定");
            return;
        }

        var campaign = SelectedCampaign;
        var reports = new List<ElderlyCertificationReport>();
        var result = await ExecuteAsync(async () =>
        {
            foreach (var formKey in keys)
            {
                var reportResult = await _reportService.GetReportAsync(campaign.Id, formKey, CancellationToken);
                if (reportResult.IsFailure)
                    return Result.Failure(reportResult.ErrorCode!, reportResult.Message!);

                reports.Add(reportResult.Value);
            }
            return Result.Success();
        }, "正在生成认证打印 PDF...");

        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("打印失败", result.Message, "确定");
            return;
        }

        var pdfResult = ElderlyCertificationPdfRenderer.RenderMany(reports);
        if (pdfResult.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("打印失败", pdfResult.Message, "确定");
            return;
        }

        var tempPath = Path.Combine(
            OutputPathHelper.GetTempDirectory(),
            $"高龄认证_{campaign.CampaignNo}_{DateTime.Now:yyyyMMddHHmmss}.pdf");

        try
        {
            await File.WriteAllBytesAsync(tempPath, pdfResult.Value, CancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "保存认证打印 PDF 失败");
            await _dialogService.DisplayAlertAsync("打印失败", $"保存临时 PDF 失败：{ex.Message}", "确定");
            return;
        }

        Logger.LogBusiness("打印高龄年度认证报表",
            ("CampaignId", campaign.Id),
            ("CampaignNo", campaign.CampaignNo),
            ("Forms", string.Join(",", keys)),
            ("Bytes", pdfResult.Value.Length));

        await ShellPrintPdfAsync(tempPath);
    }

    private List<string> GetSelectedKeys()
    {
        var keys = new List<string>(3);
        if (ExportRoster) keys.Add(ElderlyCertificationReportFormKeys.Roster);
        if (ExportSummary) keys.Add(ElderlyCertificationReportFormKeys.Summary);
        if (ExportUnverified) keys.Add(ElderlyCertificationReportFormKeys.Unverified);
        return keys;
    }

    /// <summary>
    /// 用系统默认 PDF 应用打印；系统未注册 print 动词（Win32 1155）时回退为打开阅读器。
    /// </summary>
    private async Task ShellPrintPdfAsync(string pdfPath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(pdfPath)
            {
                UseShellExecute = true,
                Verb = "print"
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1155)
        {
            Logger.Warn($"PDF print verb 不可用，回退为默认阅读器打开: {pdfPath}");
            try
            {
                using var fallback = Process.Start(
                    new ProcessStartInfo(pdfPath) { UseShellExecute = true });
                await _dialogService.DisplayAlertAsync(
                    "提示", "已用默认 PDF 阅读器打开认证报表，请在其中执行打印。", "确定");
            }
            catch (Exception openEx)
            {
                Logger.LogError(openEx, $"PDF 打开失败: {pdfPath}");
                await _dialogService.DisplayAlertAsync(
                    "打印失败", $"无法打开 PDF 报表，请检查默认 PDF 阅读器设置。{openEx.Message}", "确定");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"PDF ShellPrint 失败: {pdfPath}");
            await _dialogService.DisplayAlertAsync("打印失败", $"打印失败：{ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private async Task OpenOutputFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(OutputFolder))
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择认证批次", "确定");
            return;
        }

        TryOpenFolder(OutputFolder);
    }

    #endregion
}
