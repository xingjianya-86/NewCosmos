using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.Printing;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.Platform;
using NewCosmos.Services.UserManagement;
using NewCosmos.Services.Utilities;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.SocialAssistance;
using System.Collections.ObjectModel;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

namespace NewCosmos.ViewModels.ArchiveManagement;

/// <summary>
/// 低收入人口历史档案查询 ViewModel
/// 只做三件事：搜索（当前库 + 5 个历史导入库）→ 查看（当前库只读详情 / 历史库按人群摘要）→ 打印证明（按单位专属模板）
/// </summary>
public partial class ArchiveQueryViewModel : ViewModelBase
{
    private readonly IApplicationService _applicationService;
    private readonly IImportedArchiveService _importedArchiveService;
    private readonly IFamilyMemberService _familyMemberService;
    private readonly IProofUnitTemplateService _proofUnitTemplateService;
    private readonly ITemplateService _templateService;
    private readonly IPrintExecuteService _printExecuteService;
    private readonly IPrintRecordService _printRecordService;
    private readonly IPrinterService _printerService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly INewPermissionService _permissionService;
    private readonly IPrintJobFactory _printJobFactory;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    /// <summary>业务类型（打印留痕与权限 PRINT_LOWINCOMEPROOF 共用）</summary>
    private const string ProofBusinessType = "LowIncomeProof";

    /// <summary>当前库档案实体（选中当前库档案后加载）</summary>
    private ApplicationEntity? _currentApplication;

    /// <summary>历史库家庭详情（选中导入库档案后加载）</summary>
    private ImportedFamilyDetail? _importedDetail;

    /// <summary>打印数据（选中后构建，预览/打印共用）</summary>
    private Dictionary<string, string>? _proofFields;
    private long? _proofBusinessId;

    /// <summary>
    /// 当前详情户主身份证（留痕归属过滤用）：打印历史只显示归属本档案的记录。
    /// business_id 跨当前库/5 张导入台账六套 id 空间，仅靠 (type,id) 会把 id 碰撞的别家记录查进来（串台）；
    /// NULL/空归属的记录一律不展示（含存量无归属行）。
    /// </summary>
    private string _proofOwnerIdCard = string.Empty;

    #region 搜索区

    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ArchiveSearchResultItem> _searchResults = new();

    [ObservableProperty]
    private bool _hasSearchResults;

    #endregion

    #region 选中档案

    [ObservableProperty]
    private bool _hasSelectedArchive;

    [ObservableProperty]
    private string _applicantName = string.Empty;

    [ObservableProperty]
    private string _applicantIdCard = string.Empty;

    [ObservableProperty]
    private string _sourceDisplay = string.Empty;

    [ObservableProperty]
    private string _classificationName = string.Empty;

    [ObservableProperty]
    private bool _isCurrentLibrary;

    [ObservableProperty]
    private bool _isImportedLibrary;

    [ObservableProperty]
    private bool _isLoadingDetail;

    #endregion

    #region 历史库摘要

    /// <summary>按人群生成的摘要键值对</summary>
    [ObservableProperty]
    private ObservableCollection<SummaryFieldItem> _summaryFields = new();

    /// <summary>家庭成员列表（历史库）</summary>
    [ObservableProperty]
    private ObservableCollection<ImportedMemberDisplayItem> _memberItems = new();

    [ObservableProperty]
    private bool _hasImportedDetail;

    #endregion

    #region 打印证明区

    /// <summary>证明模板选项（通配证明模板 + 单位专属模板合并）</summary>
    [ObservableProperty]
    private ObservableCollection<ProofTemplateOption> _proofTemplateOptions = new();

    [ObservableProperty]
    private ProofTemplateOption? _selectedProofTemplateOption;

    /// <summary>目标证明单位（通配模板时手填）</summary>
    [ObservableProperty]
    private string _targetUnitName = string.Empty;

    /// <summary>是否选中通配证明模板（显示手填单位 Entry）</summary>
    public bool IsGenericTemplateSelected => SelectedProofTemplateOption?.IsGeneric == true;

    /// <summary>是否选中单位专属模板（显示单位名只读）</summary>
    public bool IsUnitTemplateSelected => SelectedProofTemplateOption?.IsGeneric == false;

    /// <summary>是否有可用的证明模板（通配或单位任一）</summary>
    public bool HasProofTemplateOptions => ProofTemplateOptions.Count > 0;

    /// <summary>无通配证明模板提示（去模板管理勾选证明文件分类）</summary>
    public string NoGenericTemplateHint => ProofTemplateOptions.Any(o => o.IsGeneric)
        ? string.Empty
        : "未配置通配证明模板，请到「模板管理」将模板标记为「证明文件」分类。";

    /// <summary>无单位专属模板提示（去单位模板维护）</summary>
    public string NoUnitTemplatesHint => ProofTemplateOptions.Any(o => !o.IsGeneric)
        ? string.Empty
        : "未配置单位专属模板，可点击「单位模板维护」为接收单位上传专属模板；也可直接使用通配证明模板。";

    /// <summary>选中模板实际名称（通配=模板名；单位=单位名·模板名）</summary>
    public string SelectedTemplateName => SelectedProofTemplateOption == null
        ? string.Empty
        : SelectedProofTemplateOption.TemplateName;

    [ObservableProperty]
    private ObservableCollection<string> _printerNames = new();

    [ObservableProperty]
    private string? _selectedPrinter;

    [ObservableProperty]
    private int _copies = 1;

    partial void OnCopiesChanged(int value)
    {
        Copies = Math.Clamp(value, 1, 99);
    }

    [RelayCommand]
    private void IncreaseCopies() => Copies = Math.Min(99, Copies + 1);

    [RelayCommand]
    private void DecreaseCopies() => Copies = Math.Max(1, Copies - 1);

    [ObservableProperty]
    private bool _isDuplex;

    /// <summary>PDF 预览文件路径（PdfPreviewView 绑定）</summary>
    [ObservableProperty]
    private string _pdfFilePath = string.Empty;

    [ObservableProperty]
    private bool _hasPdf;

    [ObservableProperty]
    private bool _isPreviewBusy;

    [ObservableProperty]
    private bool _isPrinting;

    /// <summary>是否有打印证明权限（PRINT_LOWINCOMEPROOF）——手机端"推送打印"按钮门控用</summary>
    [ObservableProperty]
    private bool _canPrintProof;

    /// <summary>整体忙碌（加载蒙版绑定：自身 + 预览 + 打印）</summary>
    public bool IsOverallBusy => IsBusy || IsPreviewBusy || IsPrinting;

    #endregion

    #region 历史记录

    /// <summary>该档案的证明开具历史</summary>
    [ObservableProperty]
    private ObservableCollection<PrintRecordDisplayItem> _printHistory = new();

    [ObservableProperty]
    private bool _hasPrintHistory;

    #endregion

    public ArchiveQueryViewModel(
        IApplicationService applicationService,
        IImportedArchiveService importedArchiveService,
        IFamilyMemberService familyMemberService,
        IProofUnitTemplateService proofUnitTemplateService,
        ITemplateService templateService,
        IPrintExecuteService printExecuteService,
        IPrintRecordService printRecordService,
        IPrinterService printerService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        Services.Domain.Reporting.IStatisticsService statisticsService,
        INewPermissionService permissionService,
        IPrintJobFactory printJobFactory)
    {
        _applicationService = applicationService;
        _importedArchiveService = importedArchiveService;
        _familyMemberService = familyMemberService;
        _proofUnitTemplateService = proofUnitTemplateService;
        _templateService = templateService;
        _printExecuteService = printExecuteService;
        _printRecordService = printRecordService;
        _printerService = printerService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _statisticsService = statisticsService;
        _permissionService = permissionService;
        _printJobFactory = printJobFactory;
        Title = "救助档案管理";
    }

    private readonly Services.Domain.Reporting.IStatisticsService _statisticsService = null!;

    #region 页头统计栏

    /// <summary>统计加载失败占位</summary>
    private const string StatNA = "—";

    /// <summary>归档总数（当前库已归档 + 历史导入库合计）</summary>
    [ObservableProperty]
    private string _totalArchivesText = StatNA;

    /// <summary>当月归档数（当前库本月完成归档，按 first_approved_at）</summary>
    [ObservableProperty]
    private string _monthlyArchivesText = StatNA;

    private async Task LoadHeaderStatsAsync()
    {
        try
        {
            var now = DateTime.Now;
            var totalTask = SafeStat<Services.Domain.Reporting.ArchiveStats>(
                () => _statisticsService.GetArchiveStatsAsync(),
                s => TotalArchivesText = s.TotalArchives.ToString("N0"));
            var monthlyTask = SafeStat<int>(
                () => _statisticsService.GetMonthlyArchivesCountAsync(now.Year, now.Month),
                v => MonthlyArchivesText = v.ToString("N0"));
            await Task.WhenAll(totalTask, monthlyTask);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "档案页头统计加载失败");
        }
    }

    private async Task SafeStat<T>(Func<Task<Result<T>>> call, Action<T> apply)
    {
        try
        {
            var result = await call();
            if (result.IsSuccess && result.Value != null)
                apply(result.Value);
            else
                _logger.Warn($"档案页头统计加载失败: {result.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "档案页头统计加载失败");
        }
    }

    #endregion

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await LoadProofTemplateOptionsAsync();
        await LoadProofPermissionAsync();
        LoadPrinterList();
        _ = LoadHeaderStatsAsync();
    }

    /// <summary>加载打印证明权限（手机端"推送打印"按钮门控；Windows 手动打印仍由 ExecutePrintAsync 内部校验）</summary>
    private async Task LoadProofPermissionAsync()
    {
        try
        {
            var userId = App.CurrentUserId ?? 0;
            if (userId <= 0)
            {
                CanPrintProof = false;
                return;
            }
            CanPrintProof = await _permissionService.HasPermissionAsync(
                userId, PermissionCodes.PRINT_LOWINCOMEPROOF, CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载打印证明权限失败");
            CanPrintProof = false;
        }
    }

    protected override void OnBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsOverallBusy));
    }

    #region 搜索

    /// <summary>
    /// 搜索档案：当前库（nc_biz_applications）+ 5 个历史导入库双源合并
    /// </summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        var keyword = SearchKeyword?.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            await ShowTipAsync("请输入姓名或身份证号后再搜索");
            return;
        }

        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("档案查询-搜索", ("Keyword", keyword));

            SearchResults.Clear();
            HasSearchResults = false;

            // ① 当前库
            var current = await _applicationService.GetPagedAsync(1, 20, null, keyword, ct);
            if (current.IsSuccess && current.Value != null)
            {
                foreach (var app in current.Value.Items)
                {
                    SearchResults.Add(MapCurrentToItem(app));
                }
            }
            else if (current.IsFailure)
            {
                _logger.LogError(new Exception(current.Message ?? "未知错误"), "当前库档案搜索失败");
            }

            // ② 5 个历史导入库（农村/城市最低生活保障、最低生活保障边缘、特困供养、刚性支出困难家庭）
            var imported = await _importedArchiveService.SearchAsync(keyword, ct);
            if (imported.IsSuccess && imported.Value != null)
            {
                foreach (var item in imported.Value)
                {
                    SearchResults.Add(item);
                }
            }
            else if (imported.IsFailure)
            {
                _logger.LogError(new Exception(imported.Message ?? "未知错误"), "导入库档案搜索失败");
                await ShowTipAsync($"历史导入库搜索失败：{imported.Message}\n当前仅显示当前库结果。");
            }

            HasSearchResults = SearchResults.Count > 0;
            if (!HasSearchResults)
            {
                await ShowTipAsync("未找到匹配的档案，请更换关键词重试");
            }
        }, "正在搜索档案...");
    }

    /// <summary>当前库档案映射为统一搜索结果项</summary>
    private static ArchiveSearchResultItem MapCurrentToItem(ApplicationEntity app) => new()
    {
        Source = ArchiveSearchSource.Current,
        ApplicationId = app.Id,
        ApplicantName = app.ApplicantName,
        ApplicantIdCard = app.ApplicantIdCard,
        Address = app.Address ?? string.Empty,
        FamilySize = app.FamilySize,
        TotalAmount = app.TotalGuaranteeAmount
    };

    #endregion

    #region 选择档案

    [RelayCommand]
    private async Task SelectArchiveAsync(ArchiveSearchResultItem item)
    {
        if (item == null) return;

        SearchResults.Clear();
        HasSearchResults = false;
        ResetSelectedState();

        IsLoadingDetail = true;
        try
        {
            _logger.LogBusiness("档案查询-选择档案",
                ("Source", item.SourceDisplay), ("Name", DataMasker.MaskName(item.ApplicantName)));

            HasSelectedArchive = true;
            ApplicantName = item.ApplicantName;
            ApplicantIdCard = item.ApplicantIdCard;
            _proofOwnerIdCard = item.ApplicantIdCard ?? string.Empty;
            SourceDisplay = item.SourceDisplay;
            ClassificationName = string.IsNullOrEmpty(item.DerivedClassificationName)
                ? item.SourceDisplay : item.DerivedClassificationName;
            IsCurrentLibrary = item.IsFromCurrentLibrary;
            IsImportedLibrary = item.IsFromImportedLibrary;

            if (item.IsFromCurrentLibrary)
            {
                await LoadCurrentLibraryDetailAsync(item.ApplicationId);
            }
            else
            {
                await LoadImportedDetailAsync(item);
            }

            await LoadPrintHistoryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "档案查询-选择档案失败");
            await ShowTipAsync($"加载档案信息失败: {ex.Message}");
        }
        finally
        {
            IsLoadingDetail = false;
        }
    }

    private void ResetSelectedState()
    {
        _currentApplication = null;
        _importedDetail = null;
        _proofFields = null;
        _proofBusinessId = null;
        _proofOwnerIdCard = string.Empty;
        SummaryFields.Clear();
        MemberItems.Clear();
        HasImportedDetail = false;
        HasPdf = false;
        PdfFilePath = string.Empty;
        OnPropertyChanged(nameof(SelectedTemplateName));
        PrintHistory.Clear();
        HasPrintHistory = false;
    }

    /// <summary>加载当前库档案完整信息（打印字段构建用）</summary>
    private async Task LoadCurrentLibraryDetailAsync(long applicationId)
    {
        var result = await _applicationService.GetByIdAsync(applicationId, CancellationToken);
        if (result.IsFailure || result.Value == null)
        {
            await ShowTipAsync($"当前库档案加载失败: {result.Message}");
            return;
        }

        var app = result.Value;
        _currentApplication = app;
        _proofBusinessId = app.Id;

        ClassificationName = string.IsNullOrEmpty(app.ClassificationResult)
            ? SourceDisplay
            : ClassificationConstants.ConvertToFullName(app.ClassificationResult);

        var guaranteeAmount = app.TotalGuaranteeAmount;

        // 正式享受时间 = data_completed_at 所在 B 线周期次月1日（不回退 submit_at/created_at）
        var enjoyDate = app.DataCompletedAt.HasValue
            ? await CalculateEnjoyStartDateAsync(app.DataCompletedAt.Value, CancellationToken)
            : default(DateTime?);
        var enjoyDateText = enjoyDate.HasValue ? enjoyDate.Value.ToString("yyyy年M月d日") : string.Empty;
        var enjoyDateIso = enjoyDate.HasValue ? enjoyDate.Value.ToString("yyyy-MM-dd") : string.Empty;

        // 家庭成员文本（仅户主+共同生活成员，排除赡养人与死亡成员；姓名/身份证/家庭关系）
        var memberText = await BuildFamilyMembersTextAsync(app.Id);

        // 渐退期信息（仅当前库；历史库档案无渐退期）
        var gracePeriodText = await BuildGracePeriodInfoAsync(app.Id, CancellationToken);

        // 操作员单位名称/电话（打印证明落款用）
        var (unitName, unitPhone) = await GetProofUnitInfoAsync();

        // 完整家庭住址（省+市+区县+镇+村+门牌）
        var fullAddress = BuildFullAddress(app.Province, app.City, app.District, app.Town, app.Community, app.Address);

        _proofFields = new Dictionary<string, string>
        {
            ["{户主姓名}"] = app.ApplicantName,
            [FieldKeys.APPLICANT_NAME] = app.ApplicantName,
            ["{身份证号}"] = app.ApplicantIdCard,
            [FieldKeys.APPLICANT_ID_CARD] = app.ApplicantIdCard,
            ["{分类结果}"] = ClassificationName,
            [FieldKeys.PROOF_CLASSIFICATION_RESULT] = ClassificationName,
            ["{保障类别}"] = ClassificationName,
            ["{保障金额}"] = guaranteeAmount.ToString("F2"),
            [FieldKeys.PROOF_GUARANTEE_AMOUNT] = guaranteeAmount.ToString("F2"),
            ["{起始日期}"] = enjoyDateText,
            [FieldKeys.PROOF_ENJOY_START_DATE] = enjoyDateIso,
            ["{入保时间}"] = enjoyDateText,
            ["{家庭地址}"] = fullAddress,
            [FieldKeys.FAMILY_ADDRESS] = fullAddress,
            ["{家庭住址}"] = fullAddress,
            ["{家庭人数}"] = app.FamilySize.ToString(),
            [FieldKeys.FAMILY_SIZE] = app.FamilySize.ToString(),
            ["{家庭成员}"] = memberText,
            ["{渐退期信息}"] = gracePeriodText,
            ["{申请原因}"] = GetApplicationReasonDisplay(app.ApplicationReason),
            [FieldKeys.APPLICATION_REASON] = GetApplicationReasonDisplay(app.ApplicationReason),
            ["{经办人}"] = string.IsNullOrEmpty(App.CurrentUserFullName) ? string.Empty : App.CurrentUserFullName,
            [FieldKeys.OPERATOR_NAME] = string.IsNullOrEmpty(App.CurrentUserFullName) ? string.Empty : App.CurrentUserFullName,
            ["{当前用户单位名称}"] = unitName,
            [FieldKeys.OPERATOR_UNIT] = unitName,
            ["{当前用户单位电话}"] = unitPhone,
            [FieldKeys.OPERATOR_UNIT_PHONE] = unitPhone
        };

        _logger.LogBusiness("档案查询-当前库详情加载完成",
            ("ApplicationId", app.Id.ToString()), ("Classification", ClassificationName));
    }

    /// <summary>加载历史库家庭详情并按人群生成摘要</summary>
    private async Task LoadImportedDetailAsync(ArchiveSearchResultItem item)
    {
        var result = await _importedArchiveService.GetImportedFamilyDetailAsync(item, CancellationToken);
        if (result.IsFailure || result.Value == null)
        {
            await ShowTipAsync($"历史库档案加载失败: {result.Message}");
            return;
        }

        var detail = result.Value;
        _importedDetail = detail;
        _proofBusinessId = item.SourceId;
        _selectedSourceType = item.Source;

        ClassificationName = string.IsNullOrEmpty(detail.DerivedClassificationCode)
            ? SourceDisplay
            : ClassificationConstants.ConvertToFullName(detail.DerivedClassificationCode);

        // 按人群生成摘要字段（低保/特困/边缘/刚性支出字段组合不同）
        SummaryFields.Clear();
        BuildImportedSummary(detail);

        // 家庭成员列表
        MemberItems.Clear();
        foreach (var m in detail.Members)
        {
            MemberItems.Add(new ImportedMemberDisplayItem(
                DataMasker.MaskName(m.Name),
                m.Relationship ?? "-",
                m.Age.HasValue ? m.Age.Value.ToString() : "-",
                m.WorkCapacity ?? "-",
                m.AnnualIncome.HasValue ? m.AnnualIncome.Value.ToString("F2") : "-",
                m.ClassifiedSubsidyAmount > 0 ? m.ClassifiedSubsidyAmount.ToString("F2") : "-",
                string.IsNullOrEmpty(m.IdCard) ? "-" : DataMasker.MaskIdCard(m.IdCard)));
        }
        HasImportedDetail = true;

        // 打印字段（历史库口径：保障金额=月保障金额，起始日期=首次享受月份当月1日）
        var amount = detail.MonthlyGuaranteeAmount > 0
            ? detail.MonthlyGuaranteeAmount
            : detail.TotalAmount;
        // 家庭成员文本（姓名/身份证/家庭关系）
        var memberText = BuildImportedMembersText(detail.Members);

        // 操作员单位名称/电话（打印证明落款用）
        var (unitName, unitPhone) = await GetProofUnitInfoAsync();

        // 正式享受时间 = 首次享受月份（如 "2026-03"）当月1日
        var enjoyDateText = ParseFirstReceiveMonthDate(detail.FirstReceiveMonth);
        var enjoyDateIso = ParseFirstReceiveMonthDateIso(detail.FirstReceiveMonth);

        // 完整家庭住址（省+市+区县+镇+村+门牌）
        var fullAddress = BuildFullAddress(detail.Province, detail.City, detail.District, detail.Town, detail.Community, detail.Address);

        _proofFields = new Dictionary<string, string>
        {
            ["{户主姓名}"] = detail.ApplicantName,
            [FieldKeys.APPLICANT_NAME] = detail.ApplicantName,
            ["{身份证号}"] = detail.ApplicantIdCard,
            [FieldKeys.APPLICANT_ID_CARD] = detail.ApplicantIdCard,
            ["{分类结果}"] = ClassificationName,
            [FieldKeys.PROOF_CLASSIFICATION_RESULT] = ClassificationName,
            ["{保障类别}"] = ClassificationName,
            ["{保障金额}"] = amount.ToString("F2"),
            [FieldKeys.PROOF_GUARANTEE_AMOUNT] = amount.ToString("F2"),
            ["{起始日期}"] = enjoyDateText,
            [FieldKeys.PROOF_ENJOY_START_DATE] = enjoyDateIso,
            ["{入保时间}"] = enjoyDateText,
            ["{家庭地址}"] = fullAddress,
            [FieldKeys.FAMILY_ADDRESS] = fullAddress,
            ["{家庭住址}"] = fullAddress,
            ["{家庭人数}"] = detail.FamilySize.ToString(),
            [FieldKeys.FAMILY_SIZE] = detail.FamilySize.ToString(),
            ["{家庭成员}"] = memberText,
            ["{渐退期信息}"] = string.Empty,
            ["{申请原因}"] = detail.ApplyReason,
            [FieldKeys.APPLICATION_REASON] = detail.ApplyReason,
            ["{经办人}"] = string.IsNullOrEmpty(App.CurrentUserFullName) ? string.Empty : App.CurrentUserFullName,
            [FieldKeys.OPERATOR_NAME] = string.IsNullOrEmpty(App.CurrentUserFullName) ? string.Empty : App.CurrentUserFullName,
            ["{当前用户单位名称}"] = unitName,
            [FieldKeys.OPERATOR_UNIT] = unitName,
            ["{当前用户单位电话}"] = unitPhone,
            [FieldKeys.OPERATOR_UNIT_PHONE] = unitPhone
        };

        _logger.LogBusiness("档案查询-历史库详情加载完成",
            ("Source", detail.SourceDisplay), ("Members", detail.Members.Count.ToString()));
    }

    /// <summary>
    /// 按人群生成摘要字段：低保（农村/城市）展示保障金额/人均年收入/首次享受；
    /// 特困展示供养方式/基本生活费/分类施保；边缘/刚性支出展示户口类型与成员收入
    /// </summary>
    private void BuildImportedSummary(ImportedFamilyDetail detail)
    {
        var source = _selectedSourceType;

        SummaryFields.Add(new SummaryFieldItem("家庭人口", $"{detail.FamilySize} 人"));
        if (!string.IsNullOrEmpty(detail.HukouType))
            SummaryFields.Add(new SummaryFieldItem("户口类型", detail.HukouType));
        if (!string.IsNullOrEmpty(detail.FirstReceiveMonth))
            SummaryFields.Add(new SummaryFieldItem("首次享受月份", detail.FirstReceiveMonth));

        switch (source)
        {
            case ArchiveSearchSource.RuralSubsistence:
            case ArchiveSearchSource.UrbanSubsistence:
                SummaryFields.Add(new SummaryFieldItem("保障金额",
                    detail.MonthlyGuaranteeAmount > 0
                        ? $"{detail.MonthlyGuaranteeAmount:F2} 元/月" : "-"));
                SummaryFields.Add(new SummaryFieldItem("人均年收入",
                    detail.PerCapitaIncome.HasValue && detail.PerCapitaIncome.Value > 0
                        ? $"{detail.PerCapitaIncome.Value:F2} 元/年" : "-"));
                SummaryFields.Add(new SummaryFieldItem("分类施保合计",
                    detail.ClassifiedSubsidyAmount > 0
                        ? $"{detail.ClassifiedSubsidyAmount:F2} 元" : "-"));
                break;

            case ArchiveSearchSource.Destitute:
                SummaryFields.Add(new SummaryFieldItem("供养方式",
                    string.IsNullOrEmpty(detail.SupportMode) ? "-" : detail.SupportMode));
                SummaryFields.Add(new SummaryFieldItem("基本生活费",
                    detail.MonthlyGuaranteeAmount > 0
                        ? $"{detail.MonthlyGuaranteeAmount:F2} 元/月" : "-"));
                SummaryFields.Add(new SummaryFieldItem("家庭分类施保",
                    detail.FamilyClassifiedAmount > 0
                        ? $"{detail.FamilyClassifiedAmount:F2} 元" : "-"));
                break;

            case ArchiveSearchSource.LowIncomeEdge:
            case ArchiveSearchSource.RigidExpenditure:
            default:
                SummaryFields.Add(new SummaryFieldItem("成员月收入口径",
                    source == ArchiveSearchSource.RigidExpenditure ? "刚性支出困难家庭" : "低收入边缘家庭"));
                break;
        }

        // 汇总展示家庭成员收入概况
        var members = detail.Members;
        if (members.Count > 0)
        {
            SummaryFields.Add(new SummaryFieldItem("家庭成员数", $"{members.Count} 人（其中户主 1 人）"));
        }
    }

    private ArchiveSearchSource _selectedSourceType;

    #endregion

    #region 查看当前库档案

    /// <summary>当前库档案：跳转低收入人口认定申请页只读查看全部字段</summary>
    [RelayCommand]
    private async Task ViewCurrentArchiveAsync()
    {
        if (!IsCurrentLibrary || _currentApplication == null) return;

        _logger.LogBusiness("档案查询-查看档案信息", ("ApplicationId", _currentApplication.Id.ToString()));
        await NavigateToPageAsync<Pages.SocialAssistance.ApplicationFormPage, FormPageParameter>(new FormPageParameter(FormOperationMode.View, _currentApplication.Id));
    }

    #endregion

    #region 每月公示文档输出

    /// <summary>进入每月公示文档输出页（公示人员以导入库为准）</summary>
    [RelayCommand]
    private async Task OpenPublicityOutputAsync()
    {
        _logger.LogBusiness("档案查询-每月公示文档输出");
        await NavigateToPageAsync<Pages.ArchiveManagement.MonthlyPublicityPage>(_ => { });
    }

    #endregion

    #region 单位模板维护

    [RelayCommand]
    private async Task ManageUnitTemplatesAsync()
    {
        _logger.LogBusiness("档案查询-单位模板维护");
        await NavigateToPageAsync<Pages.ArchiveManagement.ProofUnitTemplateManagePage>(_ => { });
    }

    /// <summary>加载证明模板选项：通配证明模板（分类=证明文件）+ 单位专属模板（全局共享）合并</summary>
    private async Task LoadProofTemplateOptionsAsync()
    {
        try
        {
            // ① 通配证明模板（GetByCategoriesAsync 抛异常而非 Result，需 try/catch）
            List<Template> genericTemplates;
            try
            {
                genericTemplates = await _templateService.GetByCategoriesAsync(
                    new[] { ProofUnitTemplateService.ProofCategory }, CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载通配证明模板失败");
                genericTemplates = [];
            }

            // ② 单位专属模板
            var unitResult = await _proofUnitTemplateService.GetAllAsync(CancellationToken);
            if (unitResult.IsFailure)
            {
                _logger.LogError(new Exception(unitResult.Message ?? "未知错误"), "加载单位模板失败");
                unitResult = Result.Failure<List<ProofUnitTemplateItem>>(unitResult.ErrorCode!, unitResult.Message!);
                return;
            }
            var unitTemplates = unitResult.Value ?? [];

            // 合并：通配在前（默认选中第一个通配模板）
            ProofTemplateOptions.Clear();
            foreach (var t in genericTemplates)
            {
                ProofTemplateOptions.Add(new ProofTemplateOption(
                    IsGeneric: true,
                    TemplateId: t.Id,
                    DisplayName: $"通配·{t.Name}",
                    TemplateName: t.Name,
                    UnitName: null));
            }
            foreach (var u in unitTemplates)
            {
                ProofTemplateOptions.Add(new ProofTemplateOption(
                    IsGeneric: false,
                    TemplateId: u.TemplateId,
                    DisplayName: $"{u.UnitName}（专属）",
                    TemplateName: u.TemplateName,
                    UnitName: u.UnitName));
            }

            // 保持原选中项（如仍存在），否则默认选第一个通配模板
            if (SelectedProofTemplateOption != null
                && ProofTemplateOptions.Any(o => o.TemplateId == SelectedProofTemplateOption.TemplateId
                    && o.IsGeneric == SelectedProofTemplateOption.IsGeneric))
            {
                SelectedProofTemplateOption = ProofTemplateOptions.First(o =>
                    o.TemplateId == SelectedProofTemplateOption.TemplateId
                    && o.IsGeneric == SelectedProofTemplateOption.IsGeneric);
            }
            else if (ProofTemplateOptions.Count > 0)
            {
                SelectedProofTemplateOption = ProofTemplateOptions.FirstOrDefault(o => o.IsGeneric)
                    ?? ProofTemplateOptions[0];
            }
            else
            {
                SelectedProofTemplateOption = null;
            }

            OnPropertyChanged(nameof(HasProofTemplateOptions));
            OnPropertyChanged(nameof(NoGenericTemplateHint));
            OnPropertyChanged(nameof(NoUnitTemplatesHint));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载证明模板选项异常");
        }
    }

    partial void OnSelectedProofTemplateOptionChanged(ProofTemplateOption? value)
    {
        OnPropertyChanged(nameof(IsGenericTemplateSelected));
        OnPropertyChanged(nameof(IsUnitTemplateSelected));
        OnPropertyChanged(nameof(SelectedTemplateName));
        OnPropertyChanged(nameof(HasProofTemplateOptions));
        OnPropertyChanged(nameof(NoGenericTemplateHint));
        OnPropertyChanged(nameof(NoUnitTemplatesHint));
    }

    private void LoadPrinterList()
    {
        try
        {
            var printers = _printerService.GetInstalledPrinters();
            PrinterNames.Clear();
            foreach (var p in printers)
            {
                PrinterNames.Add(p);
            }

            var defaultPrinter = _printerService.GetDefaultPrinter();
            if (!string.IsNullOrEmpty(defaultPrinter) && PrinterNames.Contains(defaultPrinter))
            {
                SelectedPrinter = defaultPrinter;
            }
            else if (PrinterNames.Count > 0)
            {
                SelectedPrinter = PrinterNames[0];
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载打印机列表失败");
        }
    }

    #endregion

    #region 预览 / 打印

    private bool EnsureCanProceed()
    {
        if (!HasSelectedArchive)
        {
            _ = ShowTipAsync("请先搜索并选择档案");
            return false;
        }
        if (_proofFields == null)
        {
            _ = ShowTipAsync("档案数据尚未加载完成，请稍候再试");
            return false;
        }
        if (SelectedProofTemplateOption == null)
        {
            _ = ShowTipAsync("请先选择证明文件模板");
            return false;
        }
        if (SelectedProofTemplateOption.IsGeneric
            && string.IsNullOrWhiteSpace(TargetUnitName))
        {
            _ = ShowTipAsync("请填写目标证明单位");
            return false;
        }
        return true;
    }

    /// <summary>当前生效的接收单位名（通配=手填目标单位；单位专属=映射单位）</summary>
    private string EffectiveUnitName => SelectedProofTemplateOption!.IsGeneric
        ? TargetUnitName.Trim()
        : SelectedProofTemplateOption.UnitName ?? string.Empty;

    /// <summary>预览证明文件（按选中模板生成 PDF）</summary>
    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (!EnsureCanProceed()) return;

        var option = SelectedProofTemplateOption!;
        var unitName = EffectiveUnitName;
        var fields = BuildProofFieldsWithUnit(unitName);

        IsPreviewBusy = true;
        try
        {
            var result = await _printExecuteService.GeneratePreviewPdfAsync(option.TemplateId, fields, null, CancellationToken);
            if (result.IsFailure)
            {
                await ShowTipAsync($"证明文件生成失败: {result.Message}");
                return;
            }

            // 写入临时文件供 WebView2 预览
            var tempPath = Path.Combine(OutputPathHelper.GetTempDirectory(), $"nc_proof_{DateTime.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.pdf");
            await File.WriteAllBytesAsync(tempPath, result.Value, CancellationToken);

            PdfFilePath = tempPath;
            HasPdf = true;

            _logger.LogBusiness("档案查询-预览证明",
                ("TemplateId", option.TemplateId.ToString()), ("Unit", unitName));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "预览证明文件失败");
            await ShowTipAsync($"预览失败: {ex.Message}");
        }
        finally
        {
            IsPreviewBusy = false;
        }
    }

    /// <summary>打印证明文件（统一留痕 + PDF 导出，权限 PRINT_LOWINCOMEPROOF）</summary>
    [RelayCommand]
    private Task PrintAsync() => ExecuteProofActionAsync(printToPrinter: true);

    /// <summary>保存证明文件（四件套之「保存」：仅生成落盘到输出根，不打印）</summary>
    [RelayCommand]
    private Task SaveAsync() => ExecuteProofActionAsync(printToPrinter: false);

    /// <summary>打印/保存共用链：生成 + 留痕记录；printToPrinter=false 时不调打印机</summary>
    private async Task ExecuteProofActionAsync(bool printToPrinter)
    {
        if (!EnsureCanProceed()) return;

        var action = printToPrinter ? "打印" : "保存";
        var option = SelectedProofTemplateOption!;
        var unitName = EffectiveUnitName;
        var fields = BuildProofFieldsWithUnit(unitName);

        IsPrinting = true;
        try
        {
            var batchNo = Guid.NewGuid().ToString("N").ToUpper();
            var result = await _printExecuteService.ExecutePrintAsync(
                option.TemplateId,
                option.TemplateName,
                ProofBusinessType,
                _proofBusinessId,
                batchNo,
                fields,
                null,
                ApplicantName,
                ApplicantIdCard,
                printToPrinter ? SelectedPrinter ?? string.Empty : null!,
                Math.Max(1, Copies),
                IsDuplex,
                printToPrinter: printToPrinter,
                CancellationToken);

            if (result.IsFailure)
            {
                await ShowTipAsync($"{action}失败: {result.Message}");
                return;
            }

            await ShowTipAsync($"证明文件{action}完成（单位：{unitName}）");
            await LoadPrintHistoryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"证明文件{action}失败");
            await ShowTipAsync($"{action}失败: {ex.Message}");
        }
        finally
        {
            IsPrinting = false;
        }
    }

    /// <summary>
    /// 手机端推送打印证明：复用打印字段构建，写入推送打印队列，由 PC 端打印代理执行（使用 PC 默认打印机）。
    /// 权限口径与 PC 手动打印一致（PRINT_LOWINCOMEPROOF）。
    /// </summary>
    [RelayCommand]
    private async Task PushPrintProofAsync()
    {
        if (!EnsureCanProceed()) return;

        if (!CanPrintProof)
        {
            await ShowTipAsync("您没有打印证明的权限（PRINT_LOWINCOMEPROOF），请联系管理员");
            return;
        }

        var option = SelectedProofTemplateOption!;
        var unitName = EffectiveUnitName;
        var fields = BuildProofFieldsWithUnit(unitName);

        await ExecuteAsync(async () =>
        {
            var request = new PrintJobRequest
            {
                BusinessType = ProofBusinessType,
                BusinessId = _proofBusinessId,
                Classification = "Proof",
                TemplateId = option.TemplateId,
                TemplateName = option.TemplateName,
                ApplicantName = ApplicantName,
                ApplicantIdCard = ApplicantIdCard,
                PrinterName = null, // PC 代理使用默认打印机
                Copies = Math.Max(1, Copies),
                IsDuplex = IsDuplex,
                Fields = fields,
                TableRows = new()
            };

            var result = await _printJobFactory.EnqueueAsync(request, CancellationToken);
            if (result.IsFailure)
            {
                await ShowTipAsync($"推送打印失败: {result.Message}");
                return result;
            }

            _logger.LogBusiness("档案查询-推送打印证明",
                ("TemplateId", option.TemplateId.ToString()),
                ("Unit", unitName),
                ("JobNo", result.Value?.JobNo ?? string.Empty));
            await ShowTipAsync($"已推送打印（任务号 {result.Value?.JobNo}），将在电脑端打印。");
            return Result.Success();
        }, "推送打印...");
    }

    /// <summary>当前库家庭成员文本列表（仅户主+共同生活成员；排除赡养人/抚养/扶养与死亡成员；姓名/身份证/家庭关系）</summary>
    private async Task<string> BuildFamilyMembersTextAsync(long applicationId)
    {
        try
        {
            var result = await _familyMemberService.GetByApplicationIdAsync(applicationId, CancellationToken);
            if (result.IsFailure || result.Value == null || result.Value.Count == 0)
                return string.Empty;

            // 已死亡登记成员（历史数据未软删，生成文本时须统一过滤）
            var deathResult = await _familyMemberService.GetDeadIdCardsByApplicationIdAsync(applicationId, CancellationToken);
            var deathIdCards = (deathResult.IsSuccess && deathResult.Value != null)
                ? deathResult.Value.ToHashSet()
                : new HashSet<string>();

            // 仅保留户主 + 共同生活成员；排除赡养抚养扶养（Support）与死亡成员
            var eligible = result.Value
                .Where(m => m.MemberCategory != MemberCategoryConstants.SUPPORT
                    && !deathIdCards.Contains(m.IdCard ?? ""))
                .OrderByDescending(m => m.IsHouseholdHead)
                .ThenBy(m => m.Id)
                .ToList();
            if (eligible.Count == 0)
                return string.Empty;

            var parts = new List<string>();
            foreach (var m in eligible)
            {
                var relation = GetRelationshipDisplay(m.RelationshipToHead);
                var idCard = string.IsNullOrEmpty(m.IdCard) ? "-" : m.IdCard;
                parts.Add($"{m.Name}（身份证：{idCard}，家庭关系：{relation}）");
            }
            return string.Join("；", parts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "档案查询-加载家庭成员文本失败");
            return string.Empty;
        }
    }

    /// <summary>历史库家庭成员文本列表（导入成员表即家庭成员，无赡养人/死亡标记；姓名/身份证/家庭关系）</summary>
    private string BuildImportedMembersText(List<ImportedMemberDetail> members)
    {
        if (members == null || members.Count == 0)
            return string.Empty;

        var parts = new List<string>();
        foreach (var m in members)
        {
            var relation = GetRelationshipDisplay(m.Relationship);
            var idCard = string.IsNullOrEmpty(m.IdCard) ? "-" : m.IdCard;
            parts.Add($"{m.Name}（身份证：{idCard}，家庭关系：{relation}）");
        }
        return string.Join("；", parts);
    }

    /// <summary>家庭关系代码转中文（字典 FamilyRelationships；查不到原样保留，兼容已中文存储）</summary>
    private string GetRelationshipDisplay(string? relationship)
    {
        if (string.IsNullOrWhiteSpace(relationship))
            return "-";
        var dictCache = _serviceProvider.GetRequiredService<IDictCacheService>();
        return dictCache.GetValue(DictionaryTypeCodes.FamilyRelationships, relationship);
    }

    /// <summary>完整家庭住址（省+市+区县+镇+村+门牌，空段自动跳过）</summary>
    private static string BuildFullAddress(string? province, string? city, string? district, string? town, string? community, string? address)
    {
        var parts = new List<string>();
        AddIfNotEmpty(parts, province, city, district, town, community, address);
        return string.Concat(parts);
    }

    private static void AddIfNotEmpty(List<string> parts, params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                parts.Add(v.Trim());
        }
    }

    /// <summary>当前库正式享受时间：data_completed_at 所在 B 线周期（BusinessProcess）结算日次月1日</summary>
    private async Task<DateTime?> CalculateEnjoyStartDateAsync(DateTime dataCompletedAt, CancellationToken ct)
    {
        try
        {
            var timelineService = _serviceProvider.GetRequiredService<IBusinessTimelineService>();
            var tl = await timelineService.CalculateTimelineAsync(
                dataCompletedAt.Year, dataCompletedAt.Month, Models.Enums.TimelineType.BusinessProcess);
            return new DateTime(tl.CycleEndDate.Year, tl.CycleEndDate.Month, 1).AddMonths(1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "档案查询-计算正式享受时间失败");
            return null;
        }
    }

    /// <summary>历史库首次享受月份（如 "2026-03"）转当月1日文本（yyyy年M月d日）</summary>
    private static string ParseFirstReceiveMonthDate(string? firstReceiveMonth)
    {
        var d = ParseFirstReceiveMonthDateTime(firstReceiveMonth);
        return d.HasValue ? d.Value.ToString("yyyy年M月d日") : string.Empty;
    }

    /// <summary>历史库首次享受月份转当月1日 ISO（yyyy-MM-dd）</summary>
    private static string ParseFirstReceiveMonthDateIso(string? firstReceiveMonth)
    {
        var d = ParseFirstReceiveMonthDateTime(firstReceiveMonth);
        return d.HasValue ? d.Value.ToString("yyyy-MM-dd") : string.Empty;
    }

    private static DateTime? ParseFirstReceiveMonthDateTime(string? firstReceiveMonth)
    {
        if (string.IsNullOrWhiteSpace(firstReceiveMonth))
            return null;
        if (DateTime.TryParseExact(firstReceiveMonth.Trim(), "yyyy-MM", null,
                System.Globalization.DateTimeStyles.None, out var m1)
            || DateTime.TryParseExact(firstReceiveMonth.Trim(), "yyyy-M", null,
                System.Globalization.DateTimeStyles.None, out m1))
        {
            return new DateTime(m1.Year, m1.Month, 1);
        }
        return null;
    }

    /// <summary>渐退期信息文本（仅当前库档案；不在渐退期或日期缺失时返回空串）</summary>
    private static string BuildGracePeriodInfo(GracePeriodRecord? record)
    {
        if (record == null || record.StartDate == null || record.EndDate == null)
            return string.Empty;

        var sb = new System.Text.StringBuilder("该家庭处于渐退期（");
        sb.Append(record.StartDate.Value.ToString("yyyy年M月d日"));
        sb.Append("至");
        sb.Append(record.EndDate.Value.ToString("yyyy年M月d日"));
        sb.Append('）');
        if (!string.IsNullOrEmpty(record.OriginalClassification))
            sb.Append("，原保障类别：").Append(ClassificationConstants.ConvertToFullName(record.OriginalClassification));
        if (record.OriginalGuaranteeAmount is > 0)
            sb.Append("，原保障金额：").Append(record.OriginalGuaranteeAmount.Value.ToString("F2")).Append("元/月");
        return sb.ToString();
    }

    /// <summary>从 nc_biz_grace_periods 读取当前库档案的渐退期文本（读取失败返回空串）</summary>
    private async Task<string> BuildGracePeriodInfoAsync(long applicationId, CancellationToken ct)
    {
        try
        {
            var graceService = _serviceProvider.GetRequiredService<IGracePeriodService>();
            var result = await graceService.GetActiveAsync(applicationId, ct);
            if (result.IsFailure || result.Value == null)
                return string.Empty;
            return BuildGracePeriodInfo(result.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "档案查询-读取渐退期信息失败");
            return string.Empty;
        }
    }

    /// <summary>当前登录用户所属单位名称/电话（打印证明落款用）</summary>
    private async Task<(string Name, string Phone)> GetProofUnitInfoAsync()
    {
        var orgId = App.CurrentUserOrganizationId;
        if (!orgId.HasValue)
            return ("-", "-");
        try
        {
            var orgService = _serviceProvider.GetRequiredService<IOrganizationService>();
            var orgResult = await orgService.GetByIdAsync(orgId.Value);
            if (orgResult.IsFailure || orgResult.Value == null)
                return ("-", "-");
            var org = orgResult.Value;
            return (org.Name ?? "-", org.Phone ?? "-");
        }
        catch
        {
            return ("-", "-");
        }
    }

    /// <summary>申请原因代码转显示文本（字典 ApplicationReasons；空码取"其他"）</summary>
    private string GetApplicationReasonDisplay(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return PickerConstants.ApplicationReason.OtherDisplay;
        var dictCache = _serviceProvider.GetRequiredService<IDictCacheService>();
        return dictCache.GetValue(DictionaryTypeCodes.ApplicationReasons, code);
    }

    /// <summary>在统一字段基础上追加接收单位（FieldKey + 中文占位符双写）</summary>
    private Dictionary<string, string> BuildProofFieldsWithUnit(string unitName)
    {
        var fields = new Dictionary<string, string>(_proofFields!, StringComparer.Ordinal);
        fields["{接收单位}"] = unitName;
        fields[FieldKeys.RECEIVE_UNIT_NAME] = unitName;
        fields["{日期}"] = DateTime.Now.ToString("yyyy年M月d日");
        fields["{开具日期}"] = DateTime.Now.ToString("yyyy年M月d日");
        fields[FieldKeys.PROOF_ISSUE_DATE] = DateTime.Now.ToString("yyyy-MM-dd");
        return fields;
    }

    #endregion

    #region 历史记录

    /// <summary>加载该档案的证明开具历史（nc_biz_print_records）</summary>
    private async Task LoadPrintHistoryAsync()
    {
        PrintHistory.Clear();
        HasPrintHistory = false;
        if (_proofBusinessId == null) return;

        try
        {
            var result = await _printRecordService.GetByBusinessAsync(ProofBusinessType, _proofBusinessId.Value, CancellationToken);
            if (result.IsFailure)
            {
                _logger.LogError(new Exception(result.Message ?? "未知错误"), "加载证明开具历史失败");
                return;
            }

            foreach (var pr in result.Value ?? [])
            {
                // 归属过滤：仅展示本档案户主的打印留痕。id 碰撞串台记录与无归属（NULL）记录一律排除。
                if (string.IsNullOrEmpty(pr.ApplicantIdCard)
                    || !string.Equals(pr.ApplicantIdCard, _proofOwnerIdCard, StringComparison.Ordinal))
                    continue;

                PrintHistory.Add(new PrintRecordDisplayItem(
                    pr.Id,
                    pr.TemplateName,
                    pr.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    DataMasker.MaskName(pr.OperatorName),
                    pr.PrinterName,
                    pr.Status));
            }
            HasPrintHistory = PrintHistory.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载证明开具历史异常");
        }
    }

    #endregion

    private async Task ShowTipAsync(string message)
    {
        await _dialogService.DisplayAlertAsync("提示", message, "确定");
    }
}

/// <summary>摘要键值展示项</summary>
public sealed record SummaryFieldItem(string Label, string Value);

/// <summary>
/// 证明模板选项（Picker 合并展示）：
/// IsGeneric=true 通配证明模板（打印时手填目标证明单位）；false 单位专属模板（单位名固定）
/// </summary>
public sealed record ProofTemplateOption(
    bool IsGeneric,
    long TemplateId,
    string DisplayName,
    string TemplateName,
    string? UnitName);

/// <summary>历史库家庭成员展示项（展示层已脱敏）</summary>
public sealed record ImportedMemberDisplayItem(
    string Name,
    string Relationship,
    string Age,
    string WorkCapacity,
    string AnnualIncomeDisplay,
    string ClassifiedSubsidyDisplay,
    string IdCard);