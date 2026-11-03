using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ChangeManagement;

/// <summary>
/// 户主死亡变更 ViewModel（单页表单：死亡信息 + 选择新户主）
/// 提交停旧建新后跳转统一申请表单（ApplicationFormPage）规划新档案，
/// 家庭成员变化引发的资金/保障变化在统一表单中重新认定。
/// </summary>
public partial class HouseholdDeathChangeViewModel : ViewModelBase
{
    private const string DeathReasonOther = "其他";

    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IApplicationService _applicationService = null!;
    private readonly IFamilyMemberService _familyMemberService = null!;
    private readonly IChangeService _changeService = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IDictCacheService _dictCacheService = null!;
    private readonly INewPermissionService _permissionService = null!;

    /// <summary>
    /// 全部家庭成员（加载时缓存，供提交使用）
    /// </summary>
    private List<FamilyMember> _allMembers = new();

    /// <summary>
    /// 死亡原户主成员（DB 无 is_household_head 列，IsHouseholdHead 恒为 false，
    /// 须用 is_applicant / member_category='HouseholdHead' / 与旧档案申请人一致 定位）
    /// </summary>
    private FamilyMember? _originalHeadMember;

    /// <summary>本档案是否特困（单人户户主死亡→整户停止时，特困退出需追加《特困供养人员取消备案表》）</summary>
    private bool _isDestitute;

    #region 原户主信息

    [ObservableProperty]
    private long _applicationId;

    [ObservableProperty]
    private string _originalHeadName = string.Empty;

    [ObservableProperty]
    private string _originalHeadIdCard = string.Empty;

    [ObservableProperty]
    private int _originalFamilySize;

    [ObservableProperty]
    private decimal _originalGuaranteeAmount;

    #endregion

    #region 死亡信息

    /// <summary>
    /// 死亡日期与时刻（唯一事实来源；null = 未选择）。
    /// 组件 DateTimeEntryView 绑定本属性：未确认选择前保持 null，
    /// 「必填」由 ValidateInput 的 null 拦截真实把关（替代旧 TimePicker 默认 12:00 的假必填），
    /// 组件 MaximumDate 默认当前时刻，禁止选未来。
    /// </summary>
    [ObservableProperty]
    private DateTime? _deathDateTime;

    /// <summary>
    /// 死亡原因（提交时由预设选择组装，不由 UI 直接编辑）
    /// </summary>
    [ObservableProperty]
    private string _deathReason = string.Empty;

    [ObservableProperty]
    private string _deathCertificateNo = string.Empty;

    /// <summary>死亡原因预设选项（字典 DeathReasons）</summary>
    public ObservableCollection<DictItemOption> DeathReasonOptions { get; } = new();

    [ObservableProperty]
    private DictItemOption? _selectedDeathReason;

    /// <summary>选中"其他"时显示补充说明输入框</summary>
    [ObservableProperty]
    private bool _isOtherReasonVisible;

    [ObservableProperty]
    private string _otherDeathReasonText = string.Empty;

    partial void OnSelectedDeathReasonChanged(DictItemOption? value)
    {
        IsOtherReasonVisible = string.Equals(value?.Display, DeathReasonOther, StringComparison.Ordinal);
        if (!IsOtherReasonVisible)
            OtherDeathReasonText = string.Empty;
    }

    #endregion

    #region 可选新户主

    [ObservableProperty]
    private ObservableCollection<FamilyMember> _eligibleMembers = new();

    [ObservableProperty]
    private FamilyMember _selectedNewHead = null!;

    /// <summary>是否单人户（除死亡户主外无其他共同生活成员 → 走"整户停止"简化流程，不建新档）</summary>
    [ObservableProperty]
    private bool _isSinglePersonHousehold;

    /// <summary>是否显示"选择新户主"区（单人户隐藏）</summary>
    public bool IsNewHeadSelectionVisible => !IsSinglePersonHousehold;

    /// <summary>提交按钮文案（单人户="确认整户停止"；多人口="停旧建新并去规划新档案"）</summary>
    public string SubmitButtonText => IsSinglePersonHousehold ? "确认整户停止" : "停旧建新并去规划新档案";

    partial void OnIsSinglePersonHouseholdChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNewHeadSelectionVisible));
        OnPropertyChanged(nameof(SubmitButtonText));
    }

    #endregion

    public HouseholdDeathChangeViewModel(
        IServiceProvider serviceProvider,
        IApplicationService applicationService,
        IFamilyMemberService familyMemberService,
        IChangeService changeService,
        IDialogService dialogService,
        ILoggerService logger,
        IDictCacheService dictCacheService,
        INewPermissionService permissionService)
    {
        _serviceProvider = serviceProvider;
        _applicationService = applicationService;
        _familyMemberService = familyMemberService;
        _changeService = changeService;
        _dialogService = dialogService;
        _logger = logger;
        _dictCacheService = dictCacheService;
        _permissionService = permissionService;
        Title = "户主死亡变更";
    }

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    /// <summary>
    /// 加载数据
    /// </summary>
    public async Task LoadDataAsync(long applicationId)
    {
        ApplicationId = applicationId;
        IsBusy = true;

        try
        {
            _logger.LogBusiness("加载户主死亡变更数据", ("ApplicationId", applicationId));

            // 死亡原因预设选项（字典 DeathReasons，Key=Display=中文原因）
            DeathReasonOptions.Clear();
            foreach (var option in _dictCacheService.GetOptions(DictionaryTypeCodes.DeathReasons))
            {
                DeathReasonOptions.Add(option);
            }

            // 加载申请信息
            var appResult = await _applicationService.GetByIdAsync(applicationId, CancellationToken);
            if (appResult.IsSuccess && appResult.Value != null)
            {
                var app = appResult.Value;
                OriginalHeadName = app.ApplicantName;
                OriginalHeadIdCard = app.ApplicantIdCard;
                OriginalFamilySize = app.FamilySize;
                OriginalGuaranteeAmount = app.TotalGuaranteeAmount;
                _isDestitute = ClassificationConstants.IsCodeDestitute(app.ClassificationResult ?? string.Empty);
            }

            // 加载全部成员并缓存；定位死亡原户主成员（is_applicant=true / member_category='HouseholdHead' /
            // 身份证号或姓名与旧档案申请人一致——存量数据存在 is_applicant 未置位的申请人成员）
            var membersResult = await _familyMemberService.GetByApplicationIdAsync(applicationId, CancellationToken);
            if (membersResult.IsSuccess)
            {
                _allMembers = membersResult.Value ?? new List<FamilyMember>();
                _originalHeadMember = _allMembers.FirstOrDefault(m =>
                    m.IsApplicant ||
                    string.Equals(m.MemberCategory, MemberCategoryConstants.HOUSEHOLD_HEAD, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(OriginalHeadIdCard)
                        && string.Equals(m.IdCard, OriginalHeadIdCard, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(OriginalHeadName)
                        && string.Equals(m.Name, OriginalHeadName, StringComparison.Ordinal)));

                // 可选新户主 = 排除死亡原户主 + 排除赡养抚养扶养人（Support 不计入共同生活成员）
                EligibleMembers.Clear();
                foreach (var member in _allMembers.Where(m =>
                    m.Id != _originalHeadMember?.Id &&
                    !string.Equals(m.MemberCategory, MemberCategoryConstants.SUPPORT, StringComparison.OrdinalIgnoreCase)))
                {
                    EligibleMembers.Add(member);
                }

                // 单人户判定：除死亡户主外无其他共同生活成员（EligibleMembers 已排除户主与赡养人）→ 走"整户停止"简化流程
                IsSinglePersonHousehold = EligibleMembers.Count == 0;
            }

            _logger.LogBusiness("数据加载完成");
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    #region 提交

    /// <summary>
    /// 提交：停旧建新，成功后跳转统一申请表单规划新档案
    /// </summary>
    /// <summary>办理权限校验（异常按无权限处理，保守拒绝）</summary>
    private async Task<bool> HasManagePermissionAsync()
    {
        try
        {
            var userId = App.CurrentUserId ?? 0;
            if (userId <= 0) return false;
            var permissions = await _permissionService.CheckPermissionsAsync(userId, new[] { PermissionCodes.CHANGE_MANAGE });
            return permissions != null && permissions.TryGetValue(PermissionCodes.CHANGE_MANAGE, out var granted) && granted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "户主死亡变更办理权限校验失败");
            return false;
        }
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        // 办理权限门控（2026-10-31 方案2 堵洞）：此前本页零权限校验，村级可经
        // 死亡联动跳入直接提交镇级变更手续——提交前强制 CHANGE_MANAGE。
        if (!await HasManagePermissionAsync())
        {
            await _dialogService.DisplayAlertAsync("提示", "无权限办理户主死亡变更，请联系管理员分配「办理信息变更」权限。", "确定");
            return;
        }

        _logger.LogBusiness("开始提交户主死亡变更");

        if (!ValidateInput())
            return;

        IsBusy = true;

        try
        {
            var operatorName = string.IsNullOrEmpty(App.CurrentUserName) ? "System" : App.CurrentUserName;

            // 组装死亡原因：预设项直接取 Display；"其他"拼补充说明
            DeathReason = string.Equals(SelectedDeathReason?.Display, DeathReasonOther, StringComparison.Ordinal)
                ? $"{DeathReasonOther}:{OtherDeathReasonText.Trim()}"
                : SelectedDeathReason?.Display ?? string.Empty;

            var context = new HouseholdDeathContext
            {
                ApplicationId = ApplicationId,
                DeceasedHeadMemberId = _originalHeadMember?.Id ?? 0,
                DeceasedHeadName = OriginalHeadName,
                DeceasedHeadIdCard = OriginalHeadIdCard,
                NewHeadMemberId = SelectedNewHead?.Id ?? 0,
                NewHeadName = SelectedNewHead?.Name ?? string.Empty,
                NewHeadIdCard = SelectedNewHead?.IdCard ?? string.Empty,
                // ValidateInput 已保证 DeathDateTime 非空；此处拆回日期/时刻两列（DB 契约不变）
                DeathDate = DeathDateTime!.Value.Date,
                DeathTime = DeathDateTime!.Value.TimeOfDay,
                DeathReason = DeathReason,
                DeathCertificateNo = DeathCertificateNo,
                OperatorName = operatorName
            };

            // 单人户：唯一成员即户主、无人接任 → 只整户停止、不建新档；多人口走停旧建新
            var result = IsSinglePersonHousehold
                ? await _changeService.ExecuteHouseholdHeadDeathStopOnlyAsync(context, CancellationToken)
                : await _changeService.ExecuteHouseholdDeathAsync(context, CancellationToken);
            if (result.IsSuccess && result.Value != null)
            {
                // 单人户整户停止：整户已死、家庭不存在 → 引导进档案制作中心出停保卷宗
                //（封面/目录/变更告知书；不打入户调查表——已无家庭可调查）
                if (IsSinglePersonHousehold)
                {
                    _logger.LogBusiness("单人户户主死亡整户停止提交成功",
                        ("ApplicationId", ApplicationId),
                        ("ChangeId", result.Value.ChangeId));

                    await _dialogService.DisplayAlertAsync("成功",
                        "单人户户主死亡已登记，整户保障已停止（无继承人建立新档案），档案已置为已停保。\n"
                        + "已进入档案制作中心，可点击「档案输出」打印《档案_变更告知书》（终止）及停保卷宗封面/目录。", "确定");

                    // 预置文书输出上下文（PrepareAndNavigateAsync 不清这三项）：
                    // ① OutputCategories=停保码 → 候选池为停保卷宗（封面/目录/入户调查表/变更告知书，靠停保码命中）；
                    // ② TemplateFilter=封面/目录/变更告知书 → 白名单剔除入户调查表（整户已死，无需入户调查）。
                    PrintNavigationData.OutputCategories = ClassificationConstants.StopCategoryCodes;
                    PrintNavigationData.TemplateFilter = new[]
                    {
                        DocumentTemplateNames.Cover,
                        DocumentTemplateNames.Directory,
                        DocumentTemplateNames.ChangeNotice
                    };
                    if (_isDestitute)
                    {
                        // 特困退出：追加《特困供养人员取消备案表》——唯一叶子「档案_特困取消备案表」使其加载命中，
                        // TemplateFilter 白名单使其列入清单（字段已由 ArchiveProductionViewModel 停发特困分支装配）。
                        PrintNavigationData.OutputCategories = ClassificationConstants.StopCategoryCodes
                            .Concat(new[] { "档案_特困取消备案表" }).ToArray();
                        PrintNavigationData.TemplateFilter = new[]
                        {
                            DocumentTemplateNames.Cover,
                            DocumentTemplateNames.Directory,
                            DocumentTemplateNames.ChangeNotice,
                            DocumentTemplateNames.DestituteCancelFiling
                        };
                    }
                    PrintNavigationData.PrefilterTemplateNames = null;
                    PrintNavigationData.OperationOverride = null;

                    // 弹出本页 → 进档案制作中心（加载本停保档案），用户点「档案输出」即得 3 张停保卷宗
                    var stopNav = Helpers.WindowNavigator.CurrentPage!.Navigation;
                    await stopNav.PopAsync();
                    RestoreWindowTitleFromNavigation();

                    await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveProductionPage, ApplicationArchiveParameter>(
                        new ApplicationArchiveParameter(ApplicationId));
                    return;
                }

                var newApplicationId = result.Value.NewApplicationId;
                _logger.LogBusiness("户主死亡变更提交成功",
                    ("OldApplicationId", ApplicationId),
                    ("NewApplicationId", newApplicationId),
                    ("TriggeredGracePeriod", result.Value.TriggeredGracePeriod));

                var successMessage = "户主死亡变更已提交，旧档案已停止。幸存家庭收入已按现行规则重算写入新档案，请在表单 Step5 完成正式分类判定（渐退资格在判定后确认）。"
                    + "\n若原家庭享受分类施保，进入渐退期后将减去死亡户主享受的份额、其余成员继续享受。"
                    + "减发在 Step5 判定后记入变更记录；月报去向分两种——本次若同时产生保障金增减发，"
                    + "分类施保减发并入「保障金增发/减发表」同一行体现（该表已无该户独立行）；"
                    + "若月保障金按渐退原额封顶续发而无增减发，则进入月报「分类施保金减发人员表」。";
                await _dialogService.DisplayAlertAsync("成功", successMessage, "确定");

                // 弹出本页，直接进入统一申请表单（Edit 模式）规划新档案，
                // 家庭成员变化引发的资金变化在统一表单的认定步骤中完整处理
                var navigation = Helpers.WindowNavigator.CurrentPage!.Navigation;
                await navigation.PopAsync();
                RestoreWindowTitleFromNavigation();

                await NavigateToPageAsync<Pages.SocialAssistance.ApplicationFormPage, FormPageParameter>(new FormPageParameter(FormOperationMode.Edit, newApplicationId));
            }
            else
            {
                var message = result.Message ?? "户主死亡变更提交失败";
                _logger.Error($"户主死亡变更提交失败: {message}");
                await _dialogService.DisplayAlertAsync("错误", message, "确定");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败: {ex.Message}");
            await _dialogService.DisplayAlertAsync("错误", $"提交失败: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool ValidateInput()
    {
        // 死亡日期与时刻必填（组件 DateTimeEntryView 未选择前 DeathDateTime 恒为 null，此处为真实把关；
        // 服务侧 ChangeService 另有 DeathTime 空值校验双门）
        if (DeathDateTime is null)
        {
            _dialogService.DisplayAlertAsync("验证失败", "请选择死亡日期与时间", "确定");
            return false;
        }

        // 未来时刻拦截（组件 MaximumDate 已在选择侧拦截，此处防预填/绑定异常绕过）
        if (DeathDateTime.Value > DateTime.Now)
        {
            _dialogService.DisplayAlertAsync("验证失败", "死亡时间不能晚于当前时间", "确定");
            return false;
        }

        if (SelectedDeathReason == null || string.IsNullOrWhiteSpace(SelectedDeathReason.Display))
        {
            _dialogService.DisplayAlertAsync("验证失败", "请选择死亡原因", "确定");
            return false;
        }

        if (string.Equals(SelectedDeathReason.Display, DeathReasonOther, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(OtherDeathReasonText))
        {
            _dialogService.DisplayAlertAsync("验证失败", "死亡原因为「其他」时，请填写补充说明", "确定");
            return false;
        }

        // 单人户无新户主可选（走整户停止），跳过"新户主必选"校验
        if (!IsSinglePersonHousehold && SelectedNewHead == null)
        {
            _dialogService.DisplayAlertAsync("验证失败", "请选择新户主", "确定");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 跨流程联动预填（死亡情况说明「户主死亡」卡片跳转）：
    /// 只回填表单字段，仍走本页自身的校验与确认，不代替审批。
    /// </summary>
    public void ApplyDeathPrefill(DateTime? deathDate, string? deathTime, string? deathReason, string? deathCertificateNo)
    {
        // 日期 + 时刻合成唯一来源；缺时刻时只回填日期部分（00:00），仍由本页校验与确认把关
        if (deathDate is { } date && date != default)
        {
            var time = !string.IsNullOrWhiteSpace(deathTime) && TimeSpan.TryParse(deathTime, out var t)
                ? t
                : TimeSpan.Zero;
            DeathDateTime = date.Date + time;
        }

        if (!string.IsNullOrWhiteSpace(deathReason))
        {
            var option = DeathReasonOptions.FirstOrDefault(o => o.Display == deathReason);
            if (option != null)
            {
                SelectedDeathReason = option;
            }
            else
            {
                var other = DeathReasonOptions.FirstOrDefault(o => o.Display == DeathReasonOther);
                if (other != null)
                {
                    SelectedDeathReason = other;
                    OtherDeathReasonText = deathReason;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(deathCertificateNo))
            DeathCertificateNo = deathCertificateNo;
    }

    #endregion
}
