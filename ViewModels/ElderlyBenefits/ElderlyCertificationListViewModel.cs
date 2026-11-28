using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Import;
using NewCosmos.Services.Platform;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Globalization;

namespace NewCosmos.ViewModels.ElderlyBenefits;

/// <summary>
/// 普惠高龄年度认证工作台：批次、名单快照、Excel+ZIP 批量导入、认证结果手工更正、凭证查看。
/// 停发走「停发预览」页逐人办理，停发成功后由停发页回写认证任务。
/// </summary>
public partial class ElderlyCertificationListViewModel : PagedSearchViewModelBase
{
    private readonly IElderlyCertificationService _certificationService;
    private readonly IElderlyCertificationImportService _importService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IFilePickerService _filePickerService;
    private readonly INewPermissionService _permissionService;

    private bool _suppressCampaignReload;

    #region 抽象属性实现

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    #endregion

    #region 数据集合

    public ObservableCollection<ElderlyCertCampaign> Campaigns { get; } = new();
    public ObservableCollection<ElderlyCertTask> Tasks { get; } = new();
    public ObservableCollection<ElderlyCertImportRowPreview> PreviewIssues { get; } = new();
    public ObservableCollection<string> CertStatusOptions { get; } = new();
    public ObservableCollection<string> EvidenceStatusOptions { get; } = new();

    private readonly Dictionary<string, string> _certStatusLabelToKey = new();
    private readonly Dictionary<string, string> _evidenceStatusLabelToKey = new();

    [ObservableProperty]
    private ElderlyCertCampaign? _selectedCampaign;

    [ObservableProperty]
    private string _selectedCertStatus = "全部";

    [ObservableProperty]
    private string _selectedEvidenceStatus = "全部";

    [ObservableProperty]
    private string _selectedExcelPath = string.Empty;

    [ObservableProperty]
    private string _selectedZipPath = string.Empty;

    [ObservableProperty]
    private string _excelFileName = "未选择";

    [ObservableProperty]
    private string _zipFileName = "未选择";

    [ObservableProperty]
    private ElderlyCertImportPreview? _preview;

    [ObservableProperty]
    private bool _hasPreview;

    [ObservableProperty]
    private string _previewSummary = string.Empty;

    [ObservableProperty]
    private bool _allowAdverseOverwrite;

    [ObservableProperty]
    private string _batchStatusText = string.Empty;

    [ObservableProperty]
    private string _totalText = "0";

    [ObservableProperty]
    private string _pendingText = "0";

    [ObservableProperty]
    private string _verifiedText = "0";

    [ObservableProperty]
    private string _evidenceIssueText = "0";

    [ObservableProperty]
    private string _campaignInfo = "请选择或新建认证批次";

    [ObservableProperty]
    private bool _canManage;

    /// <summary>ELDERLY_VIEW：停发预览查看入口（不要求编辑权限）。</summary>
    [ObservableProperty]
    private bool _canView;

    #endregion

    public ElderlyCertificationListViewModel(
        IElderlyCertificationService certificationService,
        IElderlyCertificationImportService importService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        IFilePickerService filePickerService,
        INewPermissionService permissionService)
    {
        _certificationService = certificationService;
        _importService = importService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _filePickerService = filePickerService;
        _permissionService = permissionService;

        Title = "高龄年度认证";
        PageSize = 20;

        CertStatusOptions.Add("全部");
        foreach (var option in ElderlyCertificationConstants.CertStatusOptions)
        {
            CertStatusOptions.Add(option.Name);
            _certStatusLabelToKey[option.Name] = option.Key;
        }

        EvidenceStatusOptions.Add("全部");
        foreach (var (key, label) in new[]
                 {
                     (ElderlyCertificationConstants.EvidenceStatusComplete, "凭证齐全"),
                     (ElderlyCertificationConstants.EvidenceStatusPartial, "部分缺失"),
                     (ElderlyCertificationConstants.EvidenceStatusMissing, "全部缺失"),
                     (ElderlyCertificationConstants.EvidenceStatusAmbiguous, "文件名歧义"),
                     (ElderlyCertificationConstants.EvidenceStatusNone, "无凭证")
                 })
        {
            EvidenceStatusOptions.Add(label);
            _evidenceStatusLabelToKey[label] = key;
        }
    }

    #region 生命周期

    public override async Task OnAppearingAsync()
    {
        await LoadPermissionAsync();
        await LoadCampaignsAsync();
        if (SelectedCampaign != null)
        {
            await LoadDataAsync();
            await LoadStatsAsync();
        }
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
            CanManage = permissions.TryGetValue(PermissionCodes.ELDERLY_EDIT, out var canManage) && canManage;
        }
        catch (Exception ex)
        {
            CanView = false;
            CanManage = false;
            _logger.LogError(ex, "高龄认证权限加载失败");
        }
    }

    private async Task LoadCampaignsAsync()
    {
        await ExecuteAsync(async () =>
        {
            var ct = CancellationToken;
            var result = await _certificationService.GetCampaignsAsync(ct);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);

            var list = result.Value ?? new List<ElderlyCertCampaign>();
            Campaigns.Clear();
            foreach (var campaign in list)
                Campaigns.Add(campaign);

            _suppressCampaignReload = true;
            if (SelectedCampaign == null || list.All(c => c.Id != SelectedCampaign.Id))
                SelectedCampaign = list.FirstOrDefault();
            _suppressCampaignReload = false;

            UpdateCampaignInfo();
            return Result.Success();
        }, "加载认证批次...");
    }

    partial void OnSelectedCampaignChanged(ElderlyCertCampaign? value)
    {
        ClearPreviewState();
        UpdateCampaignInfo();

        if (_suppressCampaignReload) return;
        PageIndex = 1;
        SafeFireAndForget(async () =>
        {
            await LoadDataAsync();
            await LoadStatsAsync();
        });
    }

    partial void OnSelectedCertStatusChanged(string value)
    {
        if (SelectedCampaign == null) return;
        PageIndex = 1;
        SafeFireAndForget(LoadDataAsync);
    }

    partial void OnSelectedEvidenceStatusChanged(string value)
    {
        if (SelectedCampaign == null) return;
        PageIndex = 1;
        SafeFireAndForget(LoadDataAsync);
    }

    private void UpdateCampaignInfo()
    {
        CampaignInfo = SelectedCampaign == null
            ? "请选择或新建认证批次"
            : $"{SelectedCampaign.DisplayName} · {SelectedCampaign.PeriodDisplay} · {SelectedCampaign.StatusName}";
    }

    protected override async Task LoadDataAsync()
    {
        if (SelectedCampaign == null)
        {
            Tasks.Clear();
            TotalCount = 0;
            return;
        }

        await ExecuteAsync(async () =>
        {
            var ct = CancellationToken;
            var result = await _certificationService.GetTasksPagedAsync(
                SelectedCampaign.Id,
                SearchText,
                GetCertStatusKey(SelectedCertStatus),
                GetEvidenceStatusKey(SelectedEvidenceStatus),
                PageIndex,
                PageSize,
                ct);

            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);

            Tasks.Clear();
            foreach (var task in result.Value.Items)
                Tasks.Add(task);
            TotalCount = result.Value.TotalCount;
            return Result.Success();
        }, "加载认证名单...");
    }

    private async Task LoadStatsAsync()
    {
        if (SelectedCampaign == null) return;
        try
        {
            var result = await _certificationService.GetStatsAsync(SelectedCampaign.Id, CancellationToken);
            if (result.IsFailure)
            {
                _logger.Warn($"高龄认证统计加载失败: {result.Message}");
                return;
            }

            var stats = result.Value;
            TotalText = stats.Total.ToString("N0");
            PendingText = stats.Pending.ToString("N0");
            VerifiedText = stats.Verified.ToString("N0");
            EvidenceIssueText = stats.EvidenceIssue.ToString("N0");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "高龄认证统计加载异常");
        }
    }

    #endregion

    #region 命令

    [RelayCommand]
    private async Task ReloadAsync()
    {
        await LoadCampaignsAsync();
        if (SelectedCampaign != null)
        {
            await LoadDataAsync();
            await LoadStatsAsync();
        }
    }

    [RelayCommand]
    private async Task CreateCampaignAsync()
    {
        if (!CanManage)
        {
            await _dialogService.DisplayAlertAsync("无权限", "当前账号没有高龄编辑权限，不能新建认证批次", "确定");
            return;
        }

        var yearText = await _dialogService.DisplayPromptAsync(
            "新建认证批次",
            "认证年度",
            "下一步", "取消",
            DateTime.Today.Year.ToString(CultureInfo.InvariantCulture),
            4,
            Keyboard.Numeric);
        if (string.IsNullOrWhiteSpace(yearText)) return;
        if (!int.TryParse(yearText, out var year) || year is < 2000 or > 2100)
        {
            await _dialogService.DisplayAlertAsync("提示", "年度不合法", "确定");
            return;
        }

        var defaultName = $"{year}年度高龄津贴领取资格认证";
        var name = await _dialogService.DisplayPromptAsync("新建认证批次", "批次名称", "创建", "取消", defaultName, 100);
        if (string.IsNullOrWhiteSpace(name)) return;

        var start = new DateTime(year, 1, 1);
        var end = new DateTime(year, 12, 31);
        var result = await _certificationService.CreateCampaignAsync(
            year, 1, name.Trim(), start, end, true, App.CurrentUserName, CancellationToken);

        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("创建失败", UserFriendlyMessages.Get(result.ErrorCode!, result.Message), "确定");
            return;
        }

        await LoadCampaignsAsync();
        var created = Campaigns.FirstOrDefault(c => c.Id == result.Value);
        if (created != null)
            SelectedCampaign = created;
        await _dialogService.ShowSnackBarAsync("认证批次已创建");
    }

    [RelayCommand]
    private async Task GenerateRosterAsync()
    {
        if (!CanManage)
        {
            await _dialogService.DisplayAlertAsync("无权限", "当前账号没有高龄编辑权限，不能生成名单", "确定");
            return;
        }
        if (SelectedCampaign == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择认证批次", "确定");
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync(
            "生成应认证名单",
            $"将按“在享 Confirmed 档案 + 名册 Active 未建档人员”生成/刷新 {SelectedCampaign.DisplayName} 的名单快照。已存在的任务不会覆盖，只补新增人员。",
            "生成", "取消");
        if (!confirm) return;

        var result = await _certificationService.GenerateRosterAsync(SelectedCampaign.Id, App.CurrentUserName, CancellationToken);
        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("生成失败", UserFriendlyMessages.Get(result.ErrorCode!, result.Message), "确定");
            return;
        }

        await LoadDataAsync();
        await LoadStatsAsync();
        await _dialogService.DisplayAlertAsync("提示", $"名单已刷新，本次新增 {result.Value} 人", "确定");
    }

    /// <summary>信息变更入口：填写新值提交 Pending（权限 ELDERLY_EDIT）。</summary>
    [RelayCommand]
    private async Task OpenInfoChangeAsync(ElderlyCertTask? task)
    {
        if (!CanManage)
        {
            await _dialogService.DisplayAlertAsync(
                "无权限", "当前账号没有高龄编辑权限，不能提交信息变更。", "确定");
            return;
        }
        if (task == null) return;

        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyInfoChangeFormPage, long>(task.Id);
    }

    /// <summary>变更审批入口：查看用 ELDERLY_VIEW；通过/驳回在审批页再校验 ELDERLY_EDIT。</summary>
    [RelayCommand]
    private async Task OpenInfoChangeListAsync()
    {
        if (!CanView)
        {
            await _dialogService.DisplayAlertAsync(
                "无权限", "当前账号没有查看高龄认证的权限，不能打开变更审批。", "确定");
            return;
        }

        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyInfoChangeListPage>();
    }

    /// <summary>
    /// 停发预览入口：查看用 ELDERLY_VIEW；逐人停发/暂缓/排除在预览页再校验 ELDERLY_EDIT。
    /// 返回本页时 OnAppearing 会重新加载名单与统计（认证任务已被停发回写）。
    /// </summary>
    [RelayCommand]
    private async Task OpenStopPreviewAsync()
    {
        if (!CanView)
        {
            await _dialogService.DisplayAlertAsync(
                "无权限", "当前账号没有查看高龄认证的权限，不能打开停发预览。", "确定");
            return;
        }
        if (SelectedCampaign == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择认证批次", "确定");
            return;
        }

        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyCertificationStopPreviewPage, ElderlyCertCampaign>(
            SelectedCampaign);
    }

    /// <summary>报表入口：查看用 ELDERLY_VIEW，导出所需权限与查看一致（ELDERLY_VIEW）。</summary>
    [RelayCommand]
    private async Task OpenReportAsync()    {
        if (!CanView)
        {
            await _dialogService.DisplayAlertAsync(
                "无权限", "当前账号没有查看高龄认证的权限，不能打开认证报表。", "确定");
            return;
        }
        if (SelectedCampaign == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择认证批次", "确定");
            return;
        }

        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyCertificationReportPage, long>(
            SelectedCampaign.Id);
    }

    [RelayCommand]
    private async Task PickExcelAsync()
    {
        var result = await _filePickerService.PickFileAsync(
            "选择认证名单 Excel", new[] { ".xlsx", ".xls" });
        if (result.Status == FilePickStatus.Picked)
        {
            SelectedExcelPath = result.Path ?? string.Empty;
            ExcelFileName = result.FileName ?? Path.GetFileName(SelectedExcelPath);
            ClearPreviewState();
        }
        else if (result.Status == FilePickStatus.Failed)
        {
            await _dialogService.DisplayAlertAsync("选择失败", result.Error ?? "无法打开文件选择器", "确定");
        }
    }

    [RelayCommand]
    private async Task PickZipAsync()
    {
        var result = await _filePickerService.PickFileAsync(
            "选择认证图片 ZIP", new[] { ".zip" });
        if (result.Status == FilePickStatus.Picked)
        {
            SelectedZipPath = result.Path ?? string.Empty;
            ZipFileName = result.FileName ?? Path.GetFileName(SelectedZipPath);
            ClearPreviewState();
        }
        else if (result.Status == FilePickStatus.Failed)
        {
            await _dialogService.DisplayAlertAsync("选择失败", result.Error ?? "无法打开文件选择器", "确定");
        }
    }

    [RelayCommand]
    private async Task PreviewImportAsync()
    {
        if (SelectedCampaign == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择认证批次", "确定");
            return;
        }
        if (string.IsNullOrWhiteSpace(SelectedExcelPath))
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择 Excel 名单", "确定");
            return;
        }

        try
        {
            var result = await _importService.PreviewAsync(
                SelectedCampaign.Id, SelectedExcelPath, SelectedZipPath, CancellationToken);
            if (result.IsFailure)
            {
                ClearPreviewState();
                PreviewSummary = $"预览失败：{UserFriendlyMessages.Get(result.ErrorCode!, result.Message)}";
                await _dialogService.DisplayAlertAsync("预览失败", PreviewSummary, "确定");
                return;
            }

            Preview = result.Value;
            HasPreview = true;
            PreviewSummary = Preview.SummaryText;

            PreviewIssues.Clear();
            foreach (var row in Preview.Rows
                         .Where(r => !r.CanApply || r.EvidenceStatus is
                             ElderlyCertificationConstants.EvidenceStatusPartial or
                             ElderlyCertificationConstants.EvidenceStatusMissing or
                             ElderlyCertificationConstants.EvidenceStatusAmbiguous)
                         .Take(300))
                PreviewIssues.Add(row);

            BatchStatusText = Preview.Warnings.Count > 0
                ? $"预览完成。提示：{string.Join("；", Preview.Warnings.Take(3))}"
                : "预览完成，未发现额外提示。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "高龄认证导入预览异常");
            ClearPreviewState();
            await _dialogService.DisplayAlertAsync("预览失败", $"预览异常：{ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private async Task ApplyImportAsync()
    {
        if (!CanManage)
        {
            await _dialogService.DisplayAlertAsync("无权限", "当前账号没有高龄编辑权限，不能应用导入", "确定");
            return;
        }
        if (Preview == null || !HasPreview)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先预览导入", "确定");
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认应用导入",
            $"{Preview.SummaryText}\n\n将写入认证结果和凭证，是否继续？",
            "应用", "取消");
        if (!confirm) return;

        var options = new ElderlyCertImportApplyOptions
        {
            CampaignId = Preview.CampaignId,
            ExcelPath = Preview.ExcelPath,
            ZipPath = Preview.ZipPath,
            OperatorName = App.CurrentUserName,
            AllowAdverseOverwrite = AllowAdverseOverwrite,
            ExcelHash = Preview.ExcelHash,
            ZipHash = Preview.ZipHash
        };

        try
        {
            var result = await _importService.ApplyAsync(Preview, options, CancellationToken);
            if (result.IsFailure)
            {
                await _dialogService.DisplayAlertAsync("导入失败", UserFriendlyMessages.Get(result.ErrorCode!, result.Message), "确定");
                return;
            }

            BatchStatusText = result.Value.SummaryText;
            await LoadDataAsync();
            await LoadStatsAsync();
            await _dialogService.DisplayAlertAsync("导入完成", result.Value.SummaryText, "确定");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "高龄认证导入应用异常");
            await _dialogService.DisplayAlertAsync("导入失败", $"导入异常：{ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private void ClearPreview()
    {
        ClearPreviewState();
    }

    [RelayCommand]
    private async Task ViewEvidenceAsync(ElderlyCertTask? task)
    {
        if (task == null) return;
        var result = await _certificationService.GetEvidenceAsync(task.Id, includeData: false, ct: CancellationToken);
        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("查询失败", UserFriendlyMessages.Get(result.ErrorCode!, result.Message), "确定");
            return;
        }

        var evidence = result.Value ?? new List<ElderlyCertEvidence>();
        if (evidence.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("凭证", "该人暂无已匹配凭证", "确定");
            return;
        }

        var lines = evidence
            .OrderBy(e => e.Seq)
            .Select(e => $"{e.Seq}. {e.EvidenceTypeName} · {e.Display} · {e.FileSizeDisplay}")
            .ToList();
        await _dialogService.DisplayAlertAsync(
            $"{task.Name} 的凭证（{evidence.Count} 张）",
            string.Join("\n", lines),
            "关闭");
    }

    [RelayCommand]
    private async Task ChangeResultAsync(ElderlyCertTask? task)
    {
        if (!CanManage)
        {
            await _dialogService.DisplayAlertAsync("无权限", "当前账号没有高龄编辑权限，不能修改认证结果", "确定");
            return;
        }
        if (task == null) return;

        var labels = ElderlyCertificationConstants.CertStatusOptions.Select(o => o.Name).ToArray();
        var selected = await _dialogService.DisplayActionSheetAsync(
            $"设置 {task.Name} 的认证结果", "取消", null, labels);
        if (string.IsNullOrWhiteSpace(selected)) return;

        var option = ElderlyCertificationConstants.CertStatusOptions.FirstOrDefault(o => o.Name == selected);
        var statusKey = option.Key;
        if (string.IsNullOrWhiteSpace(statusKey)) return;

        DateTime? deathDate = task.DeathDate;
        if (statusKey == ElderlyCertificationConstants.CertStatusDeceased && deathDate == null)
        {
            var text = await _dialogService.DisplayPromptAsync(
                "死亡日期", "请输入死亡日期（yyyy-MM-dd）", "确定", "取消", DateTime.Today.ToString("yyyy-MM-dd"), 10);
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!DateTime.TryParse(text, out var parsed))
            {
                await _dialogService.DisplayAlertAsync("提示", "日期格式不正确", "确定");
                return;
            }
            deathDate = parsed.Date;
        }

        var result = await _certificationService.UpdateTaskResultAsync(
            task.Id,
            statusKey,
            task.VerifyMethod ?? ElderlyCertificationConstants.VerifyMethodOther,
            statusKey == ElderlyCertificationConstants.CertStatusPending ? task.VerifiedAt : DateTime.Today,
            App.CurrentUserName,
            task.ResultNote,
            deathDate,
            App.CurrentUserName,
            CancellationToken);

        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("保存失败", UserFriendlyMessages.Get(result.ErrorCode!, result.Message), "确定");
            return;
        }

        await LoadDataAsync();
        await LoadStatsAsync();
        await _dialogService.ShowSnackBarAsync("认证结果已更新");
    }

    [RelayCommand]
    private async Task OpenUnmatchedEvidenceAsync()
    {
        if (Preview == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先预览导入", "确定");
            return;
        }
        if (Preview.UnmatchedEntries.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("未匹配图片", "本次 ZIP 没有多余图片", "确定");
            return;
        }

        var lines = Preview.UnmatchedEntries.Take(30)
            .Select(e => $"{e.FileName}（{e.DisplaySize}）")
            .ToList();
        var more = Preview.UnmatchedEntries.Count > 30 ? $"\n……共 {Preview.UnmatchedEntries.Count} 张" : string.Empty;
        await _dialogService.DisplayAlertAsync(
            "未匹配图片",
            string.Join("\n", lines) + more,
            "关闭");
    }

    #endregion

    #region 工具

    private string? GetCertStatusKey(string label) =>
        string.IsNullOrWhiteSpace(label) || label == "全部" ? null : _certStatusLabelToKey.GetValueOrDefault(label);

    private string? GetEvidenceStatusKey(string label) =>
        string.IsNullOrWhiteSpace(label) || label == "全部" ? null : _evidenceStatusLabelToKey.GetValueOrDefault(label);

    private void ClearPreviewState()
    {
        Preview = null;
        HasPreview = false;
        PreviewSummary = string.Empty;
        BatchStatusText = string.Empty;
        PreviewIssues.Clear();
    }

    #endregion
}
