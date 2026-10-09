using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.SocialAssistance;
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

    /// <summary>
    /// 全部家庭成员（加载时缓存，供提交使用）
    /// </summary>
    private List<FamilyMember> _allMembers = new();

    /// <summary>
    /// 死亡原户主成员（DB 无 is_household_head 列，IsHouseholdHead 恒为 false，
    /// 须用 is_applicant / member_category='HouseholdHead' / 与旧档案申请人一致 定位）
    /// </summary>
    private FamilyMember? _originalHeadMember;

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

    [ObservableProperty]
    private DateTime _deathDate = DateTime.Today;

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

    #endregion

    public HouseholdDeathChangeViewModel(
        IServiceProvider serviceProvider,
        IApplicationService applicationService,
        IFamilyMemberService familyMemberService,
        IChangeService changeService,
        IDialogService dialogService,
        ILoggerService logger,
        IDictCacheService dictCacheService)
    {
        _serviceProvider = serviceProvider;
        _applicationService = applicationService;
        _familyMemberService = familyMemberService;
        _changeService = changeService;
        _dialogService = dialogService;
        _logger = logger;
        _dictCacheService = dictCacheService;
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
    [RelayCommand]
    private async Task SubmitAsync()
    {
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
                NewHeadMemberId = SelectedNewHead.Id,
                NewHeadName = SelectedNewHead.Name,
                NewHeadIdCard = SelectedNewHead.IdCard,
                DeathDate = DeathDate,
                DeathReason = DeathReason,
                DeathCertificateNo = DeathCertificateNo,
                OperatorName = operatorName
            };

            var result = await _changeService.ExecuteHouseholdDeathAsync(context, CancellationToken);
            if (result.IsSuccess && result.Value != null)
            {
                var newApplicationId = result.Value.NewApplicationId;
                _logger.LogBusiness("户主死亡变更提交成功",
                    ("OldApplicationId", ApplicationId),
                    ("NewApplicationId", newApplicationId),
                    ("TriggeredGracePeriod", result.Value.TriggeredGracePeriod));

                var successMessage = "户主死亡变更已提交，旧档案已停止。幸存家庭收入已按现行规则重算写入新档案，请在表单 Step5 完成正式分类判定（渐退资格在判定后确认）。"
                    + "\n若原家庭享受分类施保，进入渐退期后将减去死亡户主享受的份额、其余成员继续享受；减发将记入变更记录（分类施保减除）并进入月报「分类施保金减发人员表」。";
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
        if (DeathDate == default)
        {
            _dialogService.DisplayAlertAsync("验证失败", "请选择死亡日期", "确定");
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

        if (SelectedNewHead == null)
        {
            _dialogService.DisplayAlertAsync("验证失败", "请选择新户主", "确定");
            return false;
        }

        return true;
    }

    #endregion
}
