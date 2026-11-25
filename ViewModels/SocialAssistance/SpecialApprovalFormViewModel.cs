using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.Domain.SpecialApproval;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 一事一议申报表 ViewModel
/// </summary>
public partial class SpecialApprovalFormViewModel : ViewModelBase
{
    private readonly ISpecialApprovalFormService _formService;
    private readonly IApplicationService _applicationService;
    private readonly IMonthlyReportService _monthlyReportService;
    private readonly IDictCacheService _dictCacheService;
    private readonly INewPermissionService _permissionService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;

    private long _applicationId;

    /// <summary>乡镇民政办审核结果默认值（新建/未填时）</summary>
    private const string DefaultAuditResult = "同意纳入";

    /// <summary>当前申请的户籍类型（Rural/Urban），用于指定分类代码按户籍补全</summary>
    private string _applicationHukouType = string.Empty;

    /// <summary>
    /// 正在回填预案选择（LoadAsync 恢复 SelectedTemplateOption）。
    /// 置位期间抑制 OnSelectedTemplateOptionChanged 的自动填充——否则每次加载都会把
    /// 已保存的一事一议说明情况覆盖成预案原文（用户实测「提交后字段恢复默认值」）。
    /// </summary>
    private bool _isRestoringTemplateSelection;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    #region 户主信息（只读）
    [ObservableProperty]
    private string _applicantName = string.Empty;

    [ObservableProperty]
    private string _applicantIdCard = string.Empty;

    [ObservableProperty]
    private string _applicantPhone = string.Empty;

    [ObservableProperty]
    private string _familyAddress = string.Empty;

    [ObservableProperty]
    private string _familySizeText = string.Empty;

    [ObservableProperty]
    private string _classificationName = string.Empty;
    #endregion

    #region 申报表内容
    [ObservableProperty]
    private string _basicSituation = string.Empty;

    [ObservableProperty]
    private string _specialMatters = string.Empty;

    [ObservableProperty]
    private ObservableCollection<DictItemOption> _templateOptions = new();

    [ObservableProperty]
    private DictItemOption? _selectedTemplateOption;

    [ObservableProperty]
    private string _reportUnit = string.Empty;

    [ObservableProperty]
    private string _handlerName = string.Empty;

    [ObservableProperty]
    private DateTime? _formDate = DateTime.Today;

    [ObservableProperty]
    private string _auditResult = string.Empty;

    [ObservableProperty]
    private DateTime? _auditDate;

    [ObservableProperty]
    private string _formNo = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>已上报（源状态 Submitted）：显示评议结论登记卡</summary>
    public bool IsSubmitted => Status == SpecialApprovalConstants.StatusSubmitted;

    /// <summary>已登记结论或已备案（锁定编辑）</summary>
    public bool IsReviewed => Status is SpecialApprovalConstants.StatusReturned
        or SpecialApprovalConstants.StatusNotPassed
        or SpecialApprovalConstants.StatusFiled
        or SpecialApprovalConstants.StatusApproved
        or SpecialApprovalConstants.StatusRejected;

    partial void OnStatusChanged(string value)
    {
        OnPropertyChanged(nameof(IsSubmitted));
        OnPropertyChanged(nameof(IsReviewed));
        OnPropertyChanged(nameof(CanSubmitForm));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(CanMarkFiled));
        OnPropertyChanged(nameof(CanReturnForCorrection));
    }

    [ObservableProperty]
    private string _templateKey = string.Empty;

    // ── 黑民规〔2026〕10 号：适用情形 / 负面清单 / 时间节点 ──
    [ObservableProperty]
    private ObservableCollection<DictItemOption> _caseTypeOptions = new();

    [ObservableProperty]
    private DictItemOption? _selectedCaseType;

    [ObservableProperty]
    private ObservableCollection<NegativeItemOption> _negativeItemOptions = new();

    [ObservableProperty]
    private DateTime? _submittedAt;

    [ObservableProperty]
    private DateTime? _filedAt;

    /// <summary>备案回执文件（文件名/路径）</summary>
    [ObservableProperty]
    private string _filingReceipt = string.Empty;

    /// <summary>是否纳入长期公示</summary>
    [ObservableProperty]
    private bool _isLongTermPublicized;
    #endregion

    #region 会议审议
    [ObservableProperty]
    private DateTime _meetingDate = DateTime.Today;

    [ObservableProperty]
    private string _meetingMembers = string.Empty;

    [ObservableProperty]
    private ObservableCollection<DictItemOption> _overrideClassificationOptions = new();

    [ObservableProperty]
    private DictItemOption? _selectedOverrideClassification;

    [ObservableProperty]
    private string _approverName = string.Empty;

    [ObservableProperty]
    private string _approverTitle = string.Empty;

    [ObservableProperty]
    private string _approverOrganization = string.Empty;

    [ObservableProperty]
    private bool _canReview;
    #endregion

    public SpecialApprovalFormViewModel(
        ISpecialApprovalFormService formService,
        IApplicationService applicationService,
        IMonthlyReportService monthlyReportService,
        IDictCacheService dictCacheService,
        INewPermissionService permissionService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider)
    {
        _formService = formService;
        _applicationService = applicationService;
        _monthlyReportService = monthlyReportService;
        _dictCacheService = dictCacheService;
        _permissionService = permissionService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        Title = "一事一议申报表";
    }

    /// <summary>
    /// 加载申报表数据（户主信息 + 申报表内容 + 预案选项 + 指定分类选项）
    /// </summary>
    public async Task LoadAsync(long applicationId)
    {
        _applicationId = applicationId;
        _applicationHukouType = string.Empty;

        await ExecuteAsync(async ct =>
        {
            LoadTemplateOptions();
            LoadOverrideClassificationOptions();
            LoadCaseTypeOptions();
            LoadNegativeItemOptions();

            // 权限
            CanReview = await _permissionService.HasPermissionAsync(App.CurrentUserId ?? 0, PermissionCodes.SPECIAL_APPROVAL_REVIEW, ct);

            // 申请信息（applicationReasonDetail = 档案"家庭情况说明"落库值，供基本情况空时自动带出）
            var applicationReasonDetail = string.Empty;
            var appResult = await _applicationService.GetByIdAsync(applicationId, ct);
            if (appResult.HasValue)
            {
                var app = appResult.Value;
                _applicationHukouType = app.HukouType ?? string.Empty;
                ApplicantName = app.ApplicantName;
                ApplicantIdCard = app.ApplicantIdCard;
                ApplicantPhone = app.ApplicantPhone;
                FamilyAddress = AddressResolver.BuildAddressFromText(app.District, app.Town, app.Community, app.Address);
                FamilySizeText = $"{app.FamilySize} 人";
                ClassificationName = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? "");
                applicationReasonDetail = app.ApplicationReasonDetail ?? string.Empty;
                ReportUnit = string.IsNullOrWhiteSpace(ReportUnit) ? await GetOrganizationNameAsync() : ReportUnit;
                // 经办人员默认当前登录用户（只补空值，不覆盖已填内容）
                if (string.IsNullOrWhiteSpace(HandlerName))
                    HandlerName = App.CurrentUserFullName;

                // 会议审议默认值（一律只补空值，不覆盖已填内容）：
                // 参会人员与会议记录同源——按乡镇读 nc_biz_meeting_attendance_history 最近一条出席历史；
                // 审批人姓名默认当前登录用户（同月报会议记录「记录人」默认）、单位默认填报单位。
                if (string.IsNullOrWhiteSpace(MeetingMembers) && !string.IsNullOrWhiteSpace(app.Town))
                {
                    var attendance = await _monthlyReportService.GetLatestMeetingAttendanceByTownAsync(app.Town, ct);
                    if (attendance.IsFailure)
                        _logger.Warn($"会议审议参会人员默认回填失败（不影响加载）: {attendance.Message}");
                    else if (!string.IsNullOrWhiteSpace(attendance.Value?.Attendees))
                        MeetingMembers = NormalizeLineBreaks(attendance.Value.Attendees);
                }
                if (string.IsNullOrWhiteSpace(ApproverName))
                    ApproverName = App.CurrentUserFullName;
                if (string.IsNullOrWhiteSpace(ApproverOrganization))
                    ApproverOrganization = ReportUnit;

                // 审批人职务：取当前登录用户的职务（nc_sys_users.position；只补空，查询失败不影响加载）
                if (string.IsNullOrWhiteSpace(ApproverTitle) && App.CurrentUserId.HasValue)
                {
                    try
                    {
                        var userService = _serviceProvider.GetRequiredService<NewCosmos.Services.Domain.UserManagement.IUserService>();
                        var userResult = await userService.GetByIdAsync(App.CurrentUserId.Value, ct);
                        if (userResult.IsFailure)
                            _logger.Warn($"审批人职务默认回填失败（不影响加载）: {userResult.Message}");
                        else if (!string.IsNullOrWhiteSpace(userResult.Value?.Position))
                            ApproverTitle = userResult.Value.Position;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"审批人职务默认回填异常（不影响加载）: {ex.Message}");
                    }
                }
            }

            // 申报表
            var form = await _formService.GetByApplicationIdAsync(applicationId, ct);
            if (form != null && form.Id > 0)
            {
                BasicSituation = form.BasicSituation;
                SpecialMatters = form.SpecialMatters;
                TemplateKey = form.TemplateKey;
                ReportUnit = form.ReportUnit;
                HandlerName = form.HandlerName;
                FormDate = form.FormDate ?? DateTime.Today;
                AuditResult = form.AuditResult;
                AuditDate = form.AuditDate;
                FormNo = form.FormNo;
                Status = form.Status;
                SubmittedAt = form.SubmittedAt;
                FiledAt = form.FiledAt;
                FilingReceipt = form.FilingReceipt ?? string.Empty;
                IsLongTermPublicized = form.IsLongTermPublicized;
                SelectedCaseType = CaseTypeOptions.FirstOrDefault(o => o.Key == form.CaseType);
                var selectedNegatives = SpecialApprovalConstants
                    .SplitNegativeItems(form.NegativeItems)
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var item in NegativeItemOptions)
                    item.IsSelected = selectedNegatives.Contains(item.Code);

                // 回填预案选择（恢复选择不触发填充，见 _isRestoringTemplateSelection）
                if (!string.IsNullOrEmpty(form.TemplateKey))
                {
                    _isRestoringTemplateSelection = true;
                    try
                    {
                        SelectedTemplateOption = TemplateOptions.FirstOrDefault(o => o.Key == form.TemplateKey);
                    }
                    finally
                    {
                        _isRestoringTemplateSelection = false;
                    }
                }
            }

            // 「申请救助家庭基本情况」自动生成：为空时带出档案的"家庭情况说明"
            // （application_reason_detail = FamilySituationTextBuilder 落库产物，五步表单保存必填 → 有档必有值；
            //   与文书/资产核查同源。只补空值，用户已填/已存内容一律不覆盖）
            if (string.IsNullOrWhiteSpace(BasicSituation) && !string.IsNullOrWhiteSpace(applicationReasonDetail))
                BasicSituation = applicationReasonDetail;

            // 乡镇民政办审核结果默认"同意纳入"（只补空值，不覆盖已保存结论）
            if (string.IsNullOrWhiteSpace(AuditResult))
                AuditResult = DefaultAuditResult;
        }, "加载申报表...");
    }

    /// <summary>
    /// 加载预案选项（字典分类 SpecialApprovalTemplates；Key=预案名，Display=预案文案）
    /// </summary>
    private void LoadTemplateOptions()
    {
        TemplateOptions.Clear();
        TemplateOptions.Add(new DictItemOption { Key = "（手工填写）", Display = string.Empty });
        foreach (var option in _dictCacheService.GetOptions(DictionaryTypeCodes.SpecialApprovalTemplates))
        {
            TemplateOptions.Add(option);
        }
    }

    /// <summary>
    /// 加载指定分类选项
    /// </summary>
    private void LoadOverrideClassificationOptions()
    {
        OverrideClassificationOptions.Clear();
        foreach (var (code, name) in SpecialApprovalConstants.OverrideClassificationOptions)
        {
            OverrideClassificationOptions.Add(new DictItemOption { Key = code, Display = name });
        }
    }

    /// <summary>加载 8 类适用情形（code 稳定，Display 为正式文名称）。</summary>
    private void LoadCaseTypeOptions()
    {
        CaseTypeOptions.Clear();
        foreach (var (code, name) in SpecialApprovalConstants.CaseTypes)
        {
            CaseTypeOptions.Add(new DictItemOption { Key = code, Display = name });
        }
    }

    /// <summary>加载 6 项负面清单（默认全不勾选）。</summary>
    private void LoadNegativeItemOptions()
    {
        NegativeItemOptions.Clear();
        foreach (var (code, name) in SpecialApprovalConstants.NegativeItems)
        {
            NegativeItemOptions.Add(new NegativeItemOption { Code = code, Name = name });
        }
    }

    partial void OnSelectedTemplateOptionChanged(DictItemOption? value)
    {
        if (value != null && !string.IsNullOrEmpty(value.Key))
        {
            // 选择预案：自动填充文案（可继续编辑）
            TemplateKey = value.Key;
            // 加载回填选择：只恢复选中项，不把已保存的说明情况覆盖回预案原文
            if (_isRestoringTemplateSelection) return;
            if (!string.IsNullOrEmpty(value.Display))
            {
                SpecialMatters = value.Display;
            }
        }
    }

    private async Task<string> GetOrganizationNameAsync()
    {
        var orgId = App.CurrentUserOrganizationId;
        if (!orgId.HasValue) return string.Empty;
        try
        {
            var orgService = _serviceProvider.GetRequiredService<NewCosmos.Services.Domain.UserManagement.IOrganizationService>();
            var result = await orgService.GetByIdAsync(orgId.Value);
            if (result.IsFailure || result.Value == null)
            {
                _logger.Warn($"获取填报单位失败，文书该字段将为空: {result.ErrorCode} {result.Message}");
                return string.Empty;
            }
            return result.Value.Name ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Warn($"获取填报单位异常，文书该字段将为空: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>申报表状态显示</summary>
    public string StatusText => SpecialApprovalConstants.GetStatusDescription(Status);

    /// <summary>
    /// 「提交申报」可见：新表（Status 为空）、草稿、退回补正。
    /// 已上报/已登记结论/已备案均隐藏，避免重复提交。
    /// </summary>
    public bool CanSubmitForm => string.IsNullOrEmpty(Status) || SpecialApprovalConstants.CanSubmit(Status);

    /// <summary>可登记备案：已返回结论（含历史 Approved）。</summary>
    public bool CanMarkFiled => SpecialApprovalConstants.CanMarkFiled(Status);

    /// <summary>可退回补正：已上报。</summary>
    public bool CanReturnForCorrection => Status == SpecialApprovalConstants.StatusSubmitted;

    /// <summary>
    /// 换行归一化：出席历史存量为裸 \r（WinUI Editor 落库），统一转 \n——
    /// 保证两端 Editor 逐行显示（原 Entry 单行控件 + 裸 \r 导致只显示首个姓名）。
    /// </summary>
    private static string NormalizeLineBreaks(string? text)
        => (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

    /// <summary>
    /// 确保申报表草稿已存在（不存在则先保存）。
    /// 提交申报/生成文档服务端均要求先有草稿；缺失时静默失败会让按钮"点了没反应"，故在此自动补齐。
    /// 返回 false 表示保存失败（已弹失败窗），调用方应中止后续操作。
    /// </summary>
    private async Task<bool> EnsureDraftSavedAsync()
    {
        var existing = await _formService.GetByApplicationIdAsync(_applicationId, CancellationToken);
        if (existing != null && existing.Id > 0)
            return true;

        var saveResult = await ExecuteAsync(() =>
            _formService.SaveDraftAsync(BuildRequest(), CancellationToken), "保存申报表草稿...");
        if (saveResult.IsFailure)
        {
            await ShowFailureAsync(saveResult, "保存申报表草稿");
            return false;
        }
        return true;
    }

    /// <summary>是否可编辑申报内容（新表/草稿/已上报待结论/退回补正；结论登记后锁定）</summary>
    public bool IsEditable => !string.IsNullOrEmpty(ApplicationName)
        && (string.IsNullOrEmpty(Status) || SpecialApprovalConstants.IsContentEditable(Status));

    private string ApplicationName => ApplicantName;

    #region 命令

    /// <summary>
    /// 保存草稿
    /// </summary>
    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        if (string.IsNullOrWhiteSpace(SpecialMatters))
        {
            await _dialogService.DisplayAlertAsync("提示", "请填写一事一议说明情况", "确定");
            return;
        }

        var result = await ExecuteAsync(() =>
            _formService.SaveDraftAsync(BuildRequest(), CancellationToken), "保存申报表...");
        if (result.IsSuccess)
        {
            await _dialogService.DisplayAlertAsync("成功", "申报表草稿已保存", "确定");
            await LoadAsync(_applicationId);
        }
        else
        {
            await ShowFailureAsync(result, "保存草稿");
        }
    }

    /// <summary>
    /// 提交申报
    /// </summary>
    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (string.IsNullOrWhiteSpace(SpecialMatters))
        {
            await _dialogService.DisplayAlertAsync("提示", "请填写一事一议说明情况", "确定");
            return;
        }

        var negativeCodes = NegativeItemOptions.Where(x => x.IsSelected).Select(x => x.Code).ToArray();
        var caseError = SpecialApprovalConstants.ValidateCaseSelection(SelectedCaseType?.Key, negativeCodes);
        if (caseError != null)
        {
            await _dialogService.DisplayAlertAsync("不能提交", caseError, "确定");
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync("确认提交",
            "提交后申报表进入县级评议（10 个工作日），确定提交？", "提交", "取消");
        if (!confirm) return;

        // 服务端要求先有草稿才能提交；缺失则自动补存（失败已弹窗，中止）
        if (!await EnsureDraftSavedAsync()) return;

        var result = await ExecuteAsync(() =>
            _formService.SubmitAsync(BuildRequest(), CancellationToken), "提交申报...");
        if (result.IsSuccess)
        {
            await _dialogService.DisplayAlertAsync("成功", "申报表已提交", "确定");
            await LoadAsync(_applicationId);
        }
        else
        {
            await ShowFailureAsync(result, "提交申报");
        }
    }

    /// <summary>
    /// 生成申报表文档并归档
    /// </summary>
    [RelayCommand]
    private async Task GenerateDocumentAsync()
    {
        if (string.IsNullOrWhiteSpace(SpecialMatters))
        {
            await _dialogService.DisplayAlertAsync("提示", "请先填写并保存申报表内容", "确定");
            return;
        }

        // 服务端要求先有草稿才能生成；缺失则自动补存（失败已弹窗，中止）
        if (!await EnsureDraftSavedAsync()) return;

        var result = await ExecuteAsync<Archive>(() =>
            _formService.GenerateDocumentAsync(_applicationId, CancellationToken), "生成申报表文档...");

        if (result.IsSuccess)
        {
            await _dialogService.DisplayAlertAsync("成功", "申报表文档已生成并归档", "确定");
        }
        else
        {
            await ShowFailureAsync(result, "生成申报表文档");
        }
    }

    /// <summary>
    /// 会议审议通过
    /// </summary>
    [RelayCommand]
    private async Task ApproveReviewAsync()
    {
        await CompleteReviewAsync(true);
    }

    /// <summary>
    /// 会议审议驳回
    /// </summary>
    [RelayCommand]
    private async Task RejectReviewAsync()
    {
        await CompleteReviewAsync(false);
    }

    private async Task CompleteReviewAsync(bool approved)
    {
        if (_applicationId <= 0) return;

        if (approved && SelectedOverrideClassification == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "审议通过前请选择指定救助分类", "确定");
            return;
        }

        var actionText = approved ? "通过" : "不通过";
        var confirm = await _dialogService.DisplayAlertAsync("确认登记结论",
            approved
                ? "确定登记“通过”结论？通过后申报表进入待备案，申请按指定分类施保。"
                : "确定登记“不通过”结论？不通过后该申请不能再提交，申请状态保持不变。",
            "确定", "取消");
        if (!confirm) return;

        var request = new SpecialApprovalReviewRequest
        {
            ApplicationId = _applicationId,
            // 申报内容随审议一并落库（提交后仍可编辑；服务端在锁定表单同一 UPDATE 内写入）
            BasicSituation = BasicSituation,
            TemplateKey = SelectedTemplateOption?.Key ?? TemplateKey,
            MeetingDate = MeetingDate,
            MeetingMembers = MeetingMembers,
            SpecialReason = SpecialMatters,
            SpecialCircumstances = SpecialMatters,
            OverrideClassification = ClassificationConstants.NormalizeCodeByHukou(SelectedOverrideClassification?.Key ?? string.Empty, _applicationHukouType),
            ApproverName = ApproverName,
            ApproverTitle = ApproverTitle,
            ApproverOrganization = ApproverOrganization,
            Approved = approved,
            CreatedBy = App.CurrentUserFullName
        };

        var result = await ExecuteAsync(() =>
            _formService.CompleteReviewAsync(request, CancellationToken), $"会议审议{actionText}...");
        if (result.IsSuccess)
        {
            await _dialogService.DisplayAlertAsync("成功", $"一事一议结论已登记（{actionText}）", "确定");
            await LoadAsync(_applicationId);
        }
    }

    /// <summary>选择备案回执文件（仅存路径/文件名）。</summary>
    [RelayCommand]
    private async Task PickFilingReceiptAsync()
    {
        var path = await PickFileWithFeedbackAsync("选择备案回执",
            new[] { ".pdf", ".jpg", ".jpeg", ".png", ".docx" });
        if (!string.IsNullOrWhiteSpace(path))
            FilingReceipt = path;
    }

    /// <summary>生成不予受理告知书（不符合适用情形，3 个工作日内书面告知）。</summary>
    [RelayCommand]
    private async Task NotAcceptedNoticeAsync()
    {
        if (_applicationId <= 0) return;
        var reason = await _dialogService.DisplayPromptAsync("不予受理告知书",
            "请填写不予受理理由：", "生成", "取消", "如：不符合“一事一议”适用情形等");
        if (reason == null) return;

        var result = await ExecuteAsync<Archive>(() =>
            _formService.GenerateNoticeAsync(_applicationId, SpecialApprovalNoticeKind.NotAccepted, reason, CancellationToken),
            "生成不予受理告知书...");
        if (result.IsSuccess)
            await _dialogService.DisplayAlertAsync("成功", "不予受理告知书已生成并归档", "确定");
        else
            await ShowFailureAsync(result, "生成不予受理告知书");
    }

    /// <summary>生成不予救助告知书（评议不通过）。</summary>
    [RelayCommand]
    private async Task RejectedNoticeAsync()
    {
        if (_applicationId <= 0) return;
        var reason = await _dialogService.DisplayPromptAsync("不予救助告知书",
            "请填写不予救助理由：", "生成", "取消", "如：经评议不符合救助条件等");
        if (reason == null) return;

        var result = await ExecuteAsync<Archive>(() =>
            _formService.GenerateNoticeAsync(_applicationId, SpecialApprovalNoticeKind.Rejected, reason, CancellationToken),
            "生成不予救助告知书...");
        if (result.IsSuccess)
            await _dialogService.DisplayAlertAsync("成功", "不予救助告知书已生成并归档", "确定");
        else
            await ShowFailureAsync(result, "生成不予救助告知书");
    }

    /// <summary>登记备案完成（待备案 → 已备案）。</summary>
    [RelayCommand]
    private async Task MarkFiledAsync()
    {
        if (_applicationId <= 0) return;

        var confirm = await _dialogService.DisplayAlertAsync("登记备案",
            "确定该一事一议已完成备案登记吗？", "已备案", "取消");
        if (!confirm) return;

        var result = await ExecuteAsync(() =>
            _formService.MarkFiledAsync(_applicationId, FilingReceipt, App.CurrentUserFullName, CancellationToken), "登记备案...");
        if (result.IsSuccess)
        {
            await _dialogService.DisplayAlertAsync("成功", "一事一议已登记备案", "确定");
            await LoadAsync(_applicationId);
        }
        else
        {
            await ShowFailureAsync(result, "登记备案");
        }
    }

    /// <summary>退回补正（已上报 → 退回补正）。</summary>
    [RelayCommand]
    private async Task ReturnForCorrectionAsync()
    {
        if (_applicationId <= 0) return;

        var reason = await _dialogService.DisplayPromptAsync("退回补正", "请填写退回补正原因：",
            "确定", "取消", "如：材料不全/情形需补充说明等");
        if (reason == null) return;

        var result = await ExecuteAsync(() =>
            _formService.ReturnForCorrectionAsync(_applicationId, reason, App.CurrentUserFullName, CancellationToken),
            "退回补正...");
        if (result.IsSuccess)
        {
            await _dialogService.DisplayAlertAsync("成功", "已退回补正", "确定");
            await LoadAsync(_applicationId);
        }
        else
        {
            await ShowFailureAsync(result, "退回补正");
        }
    }

    // 返回：使用基类 GoBackCommand（含栈守卫与窗口标题恢复），XAML 已绑定

    #endregion

    private SpecialApprovalFormRequest BuildRequest() => new()
    {
        ApplicationId = _applicationId,
        BasicSituation = BasicSituation,
        SpecialMatters = SpecialMatters,
        TemplateKey = SelectedTemplateOption?.Key ?? TemplateKey,
        ReportUnit = ReportUnit,
        HandlerName = HandlerName,
        FormDate = FormDate,
        AuditResult = AuditResult,
        AuditDate = AuditDate,
        CaseType = SelectedCaseType?.Key ?? string.Empty,
        NegativeItems = NegativeItemOptions.Where(x => x.IsSelected).Select(x => x.Code).ToArray(),
        FilingReceipt = FilingReceipt,
        CreatedBy = App.CurrentUserFullName
    };
}

/// <summary>负面清单勾选项（页面 CheckBox 绑定）。</summary>
public partial class NegativeItemOption : ObservableObject
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}
