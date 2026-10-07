using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.System;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 分类认定 ViewModel
/// </summary>
public partial class ClassificationViewModel : ViewModelBase
{
    private readonly IClassificationService _classificationService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IServiceProvider _serviceProvider = null!;

    private long _applicationId;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    #region 分类结果属性
    [ObservableProperty]
    private string _classificationCode = string.Empty;

    [ObservableProperty]
    private string _classificationName = string.Empty;

    [ObservableProperty]
    private string _classificationDescription = string.Empty;

    [ObservableProperty]
    private string _decisionPath = string.Empty;

    [ObservableProperty]
    private decimal _perCapitaIncome;

    /// <summary>人均年收入（元/年，年值权威口径）</summary>
    [ObservableProperty]
    private decimal _perCapitaAnnualIncome;

    /// <summary>人均年收入（元/年）= 年值权威口径，判定路径文案用年口径与年标准对比</summary>
    public decimal PerCapitaIncomeAnnual => PerCapitaAnnualIncome;

    partial void OnPerCapitaIncomeChanged(decimal value) => OnPropertyChanged(nameof(PerCapitaIncomeAnnual));
    partial void OnPerCapitaAnnualIncomeChanged(decimal value) => OnPropertyChanged(nameof(PerCapitaIncomeAnnual));

    [ObservableProperty]
    private decimal _threshold;

    [ObservableProperty]
    private decimal _lowIncomeThreshold;

    [ObservableProperty]
    private decimal _guaranteeAmount;

    [ObservableProperty]
    private bool _isEligible;

    [ObservableProperty]
    private string _ineligibleReason = string.Empty;

    [ObservableProperty]
    private string _determinationBasis = string.Empty;

    [ObservableProperty]
    private bool _showSpecialApproval;

    #endregion

    #region 分类施保属性
    [ObservableProperty]
    private string _classifiedSubsidyType = string.Empty;

    [ObservableProperty]
    private decimal _classifiedSubsidyAmount;

    [ObservableProperty]
    private int _classifiedSubsidyCount;

    [ObservableProperty]
    private decimal _classifiedSubsidyPerPerson;

    #endregion

    #region 照料补贴属性
    [ObservableProperty]
    private decimal _caregiverSubsidyAmount;

    [ObservableProperty]
    private string _caregiverType = string.Empty;

    #endregion

    #region 保障金总额属性
    [ObservableProperty]
    private decimal _householdMonthlyGuaranteeAmount;

    [ObservableProperty]
    private decimal _totalGuaranteeAmount;

    #endregion

    #region 渐退期属性
    [ObservableProperty]
    private bool _isInGracePeriod;

    [ObservableProperty]
    private int? _gracePeriodMonths;

    [ObservableProperty]
    private DateTime? _gracePeriodStartDate;

    [ObservableProperty]
    private DateTime? _gracePeriodEndDate;

    [ObservableProperty]
    private string _originalClassificationResult = string.Empty;

    [ObservableProperty]
    private string _originalClassificationName = string.Empty;

    [ObservableProperty]
    private decimal? _originalGuaranteeAmount;

    #endregion

    #region 判定详情

    [ObservableProperty]
    private ObservableCollection<string> _determinationDetails = new();

    [ObservableProperty]
    private bool _hasSevereDisease;

    [ObservableProperty]
    private bool _hasSevereDisability;

    [ObservableProperty]
    private bool _hasLaborAbility;

    [ObservableProperty]
    private bool _allAbove60;

    [ObservableProperty]
    private bool _isSingleHousehold;

    [ObservableProperty]
    private bool _hasSupporters;

    [ObservableProperty]
    private bool _hasCaregivers;

    #endregion

    #region 家庭信息

    [ObservableProperty]
    private string _applicantName = string.Empty;

    [ObservableProperty]
    private string _hukouType = string.Empty;

    [ObservableProperty]
    private int _familySize;

    [ObservableProperty]
    private decimal _totalFamilyIncome;

    [ObservableProperty]
    private decimal _rigidExpenditure;

    [ObservableProperty]
    private decimal _rigidExpenditureRatio;

    #endregion

    public ClassificationViewModel(
        IClassificationService classificationService,
        ILoggerService logger,
        IServiceProvider serviceProvider)
    {
        _classificationService = classificationService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        Title = "分类认定";
    }

    /// <summary>
    /// 从申请数据加载分类结果
    /// </summary>
    public async Task LoadFromApplication(ApplicationEntity application)
    {
        _applicationId = application.Id;
        ApplicantName = application.ApplicantName;
        HukouType = application.HukouType ?? string.Empty;
        FamilySize = application.FamilySize;
        TotalFamilyIncome = application.TotalFamilyIncome;
        PerCapitaIncome = application.PerCapitaIncome;
        PerCapitaAnnualIncome = application.PerCapitaAnnualIncome;
        RigidExpenditure = application.RigidExpenditure;

        // 计算刚性支出占比
        RigidExpenditureRatio = TotalFamilyIncome > 0 
            ? RigidExpenditure / TotalFamilyIncome * 100 
            : 0;

        // 分类结果
        ClassificationCode = application.ClassificationResult ?? string.Empty;
        ClassificationName = ClassificationConstants.ConvertFromCode(ClassificationCode);
        ClassificationDescription = ClassificationName;
        IsEligible = application.IsEligible;

        // 保障金额
        HouseholdMonthlyGuaranteeAmount = application.HouseholdMonthlyGuaranteeAmount;
        ClassifiedSubsidyType = application.ClassifiedSubsidyType ?? string.Empty;
        ClassifiedSubsidyAmount = application.ClassifiedSubsidyAmount;
        CaregiverSubsidyAmount = application.CaregiverSubsidyAmount;
        TotalGuaranteeAmount = application.TotalGuaranteeAmount;
        GuaranteeAmount = HouseholdMonthlyGuaranteeAmount;

        // 渐退期
        IsInGracePeriod = application.IsInGracePeriod;
        GracePeriodMonths = application.GracePeriodMonths;
        GracePeriodStartDate = application.GracePeriodStartDate;
        GracePeriodEndDate = application.GracePeriodEndDate;
        OriginalClassificationResult = application.OriginalClassificationResult ?? string.Empty;
        OriginalClassificationName = !string.IsNullOrEmpty(OriginalClassificationResult)
            ? ClassificationConstants.ConvertFromCode(OriginalClassificationResult)
            : string.Empty;
        OriginalGuaranteeAmount = application.OriginalGuaranteeAmount;

        // 更新判定路径
        await UpdateDecisionPathAsync();

        _logger.LogBusiness("分类结果加载", ("分类", ClassificationCode), ("保障金额", TotalGuaranteeAmount));
    }

    /// <summary>
    /// 从分类判定结果加载    /// </summary>
    public async Task LoadFromClassificationResult(ClassificationResult result)
    {
        ClassificationCode = result.Classification;
        ClassificationName = result.Description;
        ClassificationDescription = result.Description;
        IsEligible = result.IsEligible;
        IneligibleReason = result.IneligibleReason;
        DeterminationBasis = result.DeterminationBasis;
        GuaranteeAmount = result.GuaranteeAmount;

        // 分类施保
        if (result.ClassifiedSubsidy != null)
        {
            ClassifiedSubsidyType = result.ClassifiedSubsidy.Types;
            ClassifiedSubsidyCount = result.ClassifiedSubsidy.Count;
            ClassifiedSubsidyPerPerson = result.ClassifiedSubsidy.PerPersonAmount;
            ClassifiedSubsidyAmount = result.ClassifiedSubsidy.TotalAmount;
        }

        // 渐退期
        if (result.GracePeriod != null && result.GracePeriod.IsEligible)
        {
            IsInGracePeriod = true;
            GracePeriodMonths = result.GracePeriod.Months;
            GracePeriodStartDate = result.GracePeriod.StartDate;
            GracePeriodEndDate = result.GracePeriod.EndDate;
            OriginalClassificationResult = result.GracePeriod.OriginalClassification;
            OriginalClassificationName = !string.IsNullOrEmpty(OriginalClassificationResult)
                ? ClassificationConstants.ConvertFromCode(OriginalClassificationResult)
                : string.Empty;
        }

        // 判定详情
        DeterminationDetails.Clear();
        foreach (var detail in result.DeterminationDetails)
        {
            DeterminationDetails.Add(detail);
        }

        // 更新判定路径
        await UpdateDecisionPathAsync();

        _logger.LogBusiness("分类结果加载", ("分类", ClassificationCode), ("保障金额", GuaranteeAmount));
    }

    /// <summary>
    /// 更新判定路径
    /// </summary>
    private async Task UpdateDecisionPathAsync()
    {
        var isRural = ClassificationConstants.IsCodeRural(ClassificationCode);
        var hukouStr = isRural ? "农村" : "城市";

        // 低保标准从配置读取（与 ClassificationService 判定口径一致），失败置 0 并提示——禁止回退硬编码
        var standardType = isRural ? "RuralSubsistenceStandard" : "UrbanSubsistenceStandard";
        var hukou = isRural ? "Rural" : "Urban";
        var stdService = _serviceProvider.GetRequiredService<IStandardConfigService>();
        var stdResult = await stdService.GetStandardValueAsync(standardType, hukou);
        if (stdResult.IsSuccess && stdResult.Value > 0)
        {
            Threshold = stdResult.Value;
            LowIncomeThreshold = Threshold * ClassificationConstants.LowIncomeMultiplier;  // 低收入标准（倍数单点定义）
        }
        else
        {
            Threshold = 0;
            LowIncomeThreshold = 0;
            _logger.Warn($"低保标准读取失败，判定路径阈值显示为空：{stdResult.Message}");
        }
        DecisionPath = ClassificationCode switch
        {
            ClassificationConstants.RuralSubsistence or ClassificationConstants.UrbanSubsistence
                => $"最低生活保障（{hukouStr}户籍）：人均年收入 ¥{PerCapitaIncomeAnnual:F2} < 低保标准 ¥{Threshold * 12:F2}/年",

            ClassificationConstants.RuralLowIncome or ClassificationConstants.UrbanLowIncome
                => $"最低生活保障边缘家庭（{hukouStr}户籍）：低保标准 ≤ 人均年收入 ¥{PerCapitaIncomeAnnual:F2} < 低收入标准 ¥{LowIncomeThreshold * 12:F2}/年",

            ClassificationConstants.RuralLowIncomeSingle or ClassificationConstants.UrbanLowIncomeSingle
                => $"最低生活保障（{hukouStr}户籍·单人保）：低收入区间且有重病、重残（含三级智力/精神）成员",

            ClassificationConstants.RuralDestituteScattered or ClassificationConstants.UrbanDestituteScattered
                => $"特困人员（{hukouStr}户籍·分散供养）：单人户 + 无赡养人 + 有照料人",

            ClassificationConstants.RuralDestituteCentralized or ClassificationConstants.UrbanDestituteCentralized
                => $"特困人员（{hukouStr}户籍·集中供养）：单人户 + 无赡养人 + 机构照料",

            ClassificationConstants.RuralRigidExpenditure or ClassificationConstants.UrbanRigidExpenditure
                => $"刚性支出困难家庭（{hukouStr}户籍）：收入超标但刚性支出占比 ≥ 50%",

            ClassificationConstants.RuralIncomeExceeded or ClassificationConstants.UrbanIncomeExceeded
                => $"收入超标（{hukouStr}户籍）：人均年收入 ¥{PerCapitaIncomeAnnual:F2} ≥ 低收入标准上限",

            ClassificationConstants.IneligibleWithLabor
                => "不符合认定条件：有劳动力且无重病重残",

            ClassificationConstants.Ineligible or ClassificationConstants.IneligibleOther
                => "不符合认定条件：其他原因",

            _ => "未知分类"
        };
    }

    /// <summary>
    /// 是否为低保类型    /// </summary>
    public bool IsSubsistence => ClassificationConstants.IsCodeSubsistence(ClassificationCode);

    /// <summary>
    /// 是否为低收入类型
    /// </summary>
    public bool IsLowIncome => ClassificationConstants.IsCodeLowIncome(ClassificationCode);

    /// <summary>
    /// 是否为特困类型    /// </summary>
    public bool IsDestitute => ClassificationConstants.IsCodeDestitute(ClassificationCode);

    /// <summary>
    /// 是否为刚性支出类型    /// </summary>
    public bool IsRigidExpenditure => ClassificationConstants.IsCodeRigidExpenditure(ClassificationCode);

    /// <summary>
    /// 是否为停保类型    /// </summary>
    public bool IsStop => ClassificationConstants.IsCodeStop(ClassificationCode);

    /// <summary>
    /// 是否有渐退期    /// </summary>
    public bool HasGracePeriod => IsInGracePeriod && GracePeriodEndDate.HasValue;

    /// <summary>
    /// 渐退期状态文本    /// </summary>
    public string GracePeriodStatusText
    {
        get
        {
            if (!IsInGracePeriod) return "无渐退期";

            var daysLeft = GracePeriodEndDate.HasValue
                ? (GracePeriodEndDate.Value - DateTime.Today).Days
                : 0;

            return daysLeft > 0
                ? $"渐退期剩余 {daysLeft} 天（至 {GracePeriodEndDate:yyyy-MM-dd}）"
                : "渐退期已结束";
        }
    }

    [RelayCommand]
    private async Task ClassifyAsync(long applicationId)
    {
        await Task.CompletedTask;
        var classificationType = ClassificationType.RuralSubsistence;
        ClassificationName = classificationType.GetDescription();
        ClassificationCode = classificationType.GetCode();
        IsEligible = !classificationType.IsStop();
        ShowSpecialApproval = !IsEligible;
    }

    [RelayCommand]
    private async Task SpecialApprovalAsync()
    {
        if (_applicationId <= 0)
        {
            await _serviceProvider.GetRequiredService<Services.Core.IDialogService>()
                .DisplayAlertAsync("提示", "请先保存申请再发起一事一议", "确定");
            return;
        }

        try
        {
            await NavigateToPageAsync<Pages.SocialAssistance.SpecialApprovalFormPage, ApplicationScopedParameter>(new ApplicationScopedParameter(_applicationId));
        }
        catch (Exception ex)
        {
            _logger.Error($"打开一事一议申报表失败: {ex.Message}");
            await _serviceProvider.GetRequiredService<Services.Core.IDialogService>()
                .DisplayAlertAsync("错误", $"打开一事一议申报表失败: {ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// 「输出档案」：进入档案制作输出页（四件套动作集中地）
    /// </summary>
    [RelayCommand]
    private async Task GoOutputAsync()
    {
        await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveOutputPage>();
    }
}