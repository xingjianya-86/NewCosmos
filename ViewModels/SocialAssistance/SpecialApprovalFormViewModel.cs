using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
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
    private readonly IDictCacheService _dictCacheService;
    private readonly INewPermissionService _permissionService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;

    private long _applicationId;

    /// <summary>当前申请的户籍类型（Rural/Urban），用于指定分类代码按户籍补全</summary>
    private string _applicationHukouType = string.Empty;

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

    [ObservableProperty]
    private bool _isSubmitted;

    [ObservableProperty]
    private bool _isReviewed;

    [ObservableProperty]
    private string _templateKey = string.Empty;
    #endregion

    #region 会议审议
    [ObservableProperty]
    private DateTime _meetingDate = DateTime.Today;

    [ObservableProperty]
    private string _meetingLocation = string.Empty;

    [ObservableProperty]
    private string _meetingTopic = string.Empty;

    [ObservableProperty]
    private string _meetingSummary = string.Empty;

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
        IDictCacheService dictCacheService,
        INewPermissionService permissionService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider)
    {
        _formService = formService;
        _applicationService = applicationService;
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

            // 权限
            CanReview = await _permissionService.HasPermissionAsync(App.CurrentUserId ?? 0, PermissionCodes.SPECIAL_APPROVAL_REVIEW, ct);

            // 申请信息
            var appResult = await _applicationService.GetByIdAsync(applicationId, ct);
            if (appResult.IsSuccess && appResult.Value != null)
            {
                var app = appResult.Value;
                _applicationHukouType = app.HukouType ?? string.Empty;
                ApplicantName = app.ApplicantName;
                ApplicantIdCard = app.ApplicantIdCard;
                ApplicantPhone = app.ApplicantPhone;
                FamilyAddress = $"{app.Town ?? ""}{app.Community ?? ""}{app.Address ?? ""}".Trim();
                FamilySizeText = $"{app.FamilySize} 人";
                ClassificationName = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? "");
                ReportUnit = string.IsNullOrWhiteSpace(ReportUnit) ? await GetOrganizationNameAsync() : ReportUnit;
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
                IsSubmitted = form.Status == SpecialApprovalConstants.StatusSubmitted;
                IsReviewed = form.Status is SpecialApprovalConstants.StatusApproved or SpecialApprovalConstants.StatusRejected;

                // 回填预案选择
                if (!string.IsNullOrEmpty(form.TemplateKey))
                {
                    SelectedTemplateOption = TemplateOptions.FirstOrDefault(o => o.Key == form.TemplateKey);
                }
            }
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

    partial void OnSelectedTemplateOptionChanged(DictItemOption? value)
    {
        if (value != null && !string.IsNullOrEmpty(value.Key))
        {
            // 选择预案：自动填充文案（可继续编辑）
            TemplateKey = value.Key;
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
            return result.IsSuccess && result.Value != null ? result.Value.Name ?? string.Empty : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>申报表状态显示</summary>
    public string StatusText => SpecialApprovalConstants.GetStatusDescription(Status);

    /// <summary>是否可编辑申报内容（草稿或已提交但未审议）</summary>
    public bool IsEditable => !IsReviewed && !string.IsNullOrEmpty(ApplicationName);

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

        var confirm = await _dialogService.DisplayAlertAsync("确认提交",
            "提交后申报表将锁定，进入会议审议环节，确定提交？", "提交", "取消");
        if (!confirm) return;

        var result = await ExecuteAsync(() =>
            _formService.SubmitAsync(BuildRequest(), CancellationToken), "提交申报...");
        if (result.IsSuccess)
        {
            await _dialogService.DisplayAlertAsync("成功", "申报表已提交", "确定");
            await LoadAsync(_applicationId);
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

        var result = await ExecuteAsync<Archive>(() =>
            _formService.GenerateDocumentAsync(_applicationId, CancellationToken), "生成申报表文档...");

        if (result.IsSuccess)
        {
            await _dialogService.DisplayAlertAsync("成功", "申报表文档已生成并归档", "确定");
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

        var actionText = approved ? "通过" : "驳回";
        var confirm = await _dialogService.DisplayAlertAsync("确认审议",
            $"确定审议{actionText}该一事一议申报？\n{(!approved ? "驳回后该申请将不予救助。" : "通过后该申请将按指定分类施保。")}",
            "确定", "取消");
        if (!confirm) return;

        var request = new SpecialApprovalReviewRequest
        {
            ApplicationId = _applicationId,
            MeetingDate = MeetingDate,
            MeetingLocation = MeetingLocation,
            MeetingTopic = MeetingTopic,
            MeetingSummary = MeetingSummary,
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
            await _dialogService.DisplayAlertAsync("成功", $"一事一议审议{actionText}", "确定");
            await LoadAsync(_applicationId);
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
        CreatedBy = App.CurrentUserFullName
    };
}
