using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.System;

namespace NewCosmos.Services.Domain.SocialAssistance;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

/// <summary>
/// 分类判定服务实现（完整版，集成 CosmosOLD 逻辑）
/// </summary>
public class ClassificationService : BaseService, IClassificationService
{
    protected override string ServiceName => "ClassificationService";
    private readonly IStandardConfigService _standardConfigService;
    private readonly ICapabilityAssessmentService _capabilityAssessmentService;
    private readonly ICollegeStudentService _collegeStudentService;

    public ClassificationService(
        ILoggerService logger,
        IStandardConfigService standardConfigService,
        ICapabilityAssessmentService capabilityAssessmentService,
        ICollegeStudentService collegeStudentService) : base(logger)
    {
        _standardConfigService = standardConfigService;
        _capabilityAssessmentService = capabilityAssessmentService;
        _collegeStudentService = collegeStudentService;
    }

    /// <summary>
    /// 执行完整分类判定（含赡养人、照料人）
    /// </summary>
    public async Task<Result<ClassificationResult>> DetermineClassificationAsync(
        ApplicationEntity application,
        List<FamilyMember> members,
        List<Supporter> supporters,
        List<Caregiver> caregivers,
        CancellationToken ct = default)
    {
        ValidateNotNull(application, nameof(application));
        ValidateNotNull(members, nameof(members));

        LogInfo($"执行分类判定");

        // 过滤赡养抚养扶养义务人——不计入家庭人数，不参与劳动力/年龄/重病重残判定；
        // 同时剔除户主行——户主以主表为准单独判定，成员表若存户主行会重复计数（分类施保人数/金额虚高）
        var householdMembers = new List<FamilyMember>();
        foreach (var m in members)
        {
            var category = MemberCategoryHelper.Normalize(m.MemberCategory, m.IsApplicant, m.RelationshipToHead);
            if (category == MemberCategoryConstants.SUPPORT) continue;
            if (IsHouseholdHeadMember(m, application, category)) continue;
            householdMembers.Add(m);
        }

        // 在读大学生（大学生档案 status=Studying）免于劳动力判定：按身份证匹配户内成员与户主。
        // 按身份证关联（application_id 无回写回路不可作依据）；查询失败只记警告、按"无豁免"继续，不阻断判定。
        var studyingIdCards = await LoadStudyingIdCardsAsync(application, householdMembers, ct);

        var result = new ClassificationResult();
        var isRural = ClassificationConstants.HukouType.IsHukouRural(application.HukouType ?? "");
        var familySize = application.FamilySize > 0 ? application.FamilySize : householdMembers.Count + 1;
        // 【年值基准】判定基于年值权威口径直接推导精确月值（不经过已舍入的 TotalFamilyIncome），
        // 与落库的月人均（年÷人数÷12）同源，避免边缘值判定与档案显示不一致。
        var annualIncome = application.TotalAnnualIncome;
        var totalIncome = annualIncome > 0 ? annualIncome / 12m : application.TotalFamilyIncome;
        var perCapitaIncome = familySize > 0 ? totalIncome / familySize : totalIncome;
        var rigidExpenditure = application.RigidExpenditure;

        // 判定健康状况（包含户主）
        var headAge = CalculateAgeFromIdCard(application.ApplicantIdCard ?? "");
        var headLevelKey = application.DisabilityLevel ?? "";
        var headTypeKey = application.DisabilityType ?? "";
        var hasHeadSevereDisease = DictionaryConstants.HealthStatus.HasSevereDisease(application.HealthStatus ?? "");
        var hasHeadSevereDisability = DictionaryConstants.HealthStatus.HasSevereDisability(application.HealthStatus ?? "")
            || ClassificationConstants.DisabilityLevel.IsSevereForAssistance(headLevelKey, headTypeKey);
        // 户主在读大学生（大学生档案 status=Studying）同样豁免劳动力判定
        var headIdCard = (application.ApplicantIdCard ?? "").Trim().ToUpperInvariant();
        var headIsStudying = headIdCard.Length > 0 && studyingIdCards.Contains(headIdCard);
        // 劳动力口径：未成年（<18）与 60 岁以上（老年）不计入劳动力；劳动年龄内按健康状况判定；
        // 重残（一、二级任意类型及三级智力/精神）无劳动能力；在读大学生免于劳动力判定
        var headHasLaborAbility = headAge is >= AgeConstants.ELDERLY_THRESHOLD or < AgeConstants.MINOR_THRESHOLD
            ? false
            : (application.HealthStatus ?? "") is HealthStatusConstants.HEALTHY or HealthStatusConstants.FAIR_OR_WEAK
              && !ClassificationConstants.DisabilityLevel.IsSevereForAssistance(headLevelKey, headTypeKey)
              && !headIsStudying;

        var hasSevereDisease = CheckHasSevereDisease(householdMembers) || hasHeadSevereDisease;
        var hasSevereDisability = CheckHasSevereDisability(householdMembers) || hasHeadSevereDisability;
        var hasSevereCondition = hasSevereDisease || hasSevereDisability;
        // 单人保口径：一、二级任意残疾类型及三级智力/精神计入重残；其余三级/四级不计（等级未知回退健康状况/标记）
        var singleRescueCandidates = BuildSingleRescueCandidates(application, householdMembers, headAge);
        var hasSingleRescueCondition = singleRescueCandidates.Count > 0;
        var hasLaborAbility = CalculateHasLaborAbility(householdMembers, studyingIdCards) || headHasLaborAbility;
        var allAbove60 = headAge >= AgeConstants.ELDERLY_THRESHOLD && CheckAllMembersOver60(householdMembers);

        // 在读大学生豁免劳动力判定的痕迹（供审核追溯判定路径）
        foreach (var m in householdMembers)
        {
            var mid = (m.IdCard ?? "").Trim().ToUpperInvariant();
            if (mid.Length > 0 && studyingIdCards.Contains(mid))
                result.DeterminationDetails.Add($"{m.Name}（在读大学生）免于劳动力判定");
        }
        if (headIsStudying)
            result.DeterminationDetails.Add($"{application.ApplicantName}（在读大学生）免于劳动力判定");

        // 判定特困条件
        var isSingleHousehold = familySize == 1;
        var hasNoSupport = !supporters.Any(s => s.IsSupportAbility == true);
        var hasCaregiver = caregivers.Any();

        // 获取标准（月标准，与月收入统一单位）
        // 【月收入体系】直接使用月标准，不再乘以12
        var standard = await GetSubsistenceStandardAsync(isRural, ct);
        var lowIncomeThreshold = standard * ClassificationConstants.LowIncomeMultiplier;
        var rigidExpenditureRatio = totalIncome > 0 ? rigidExpenditure / totalIncome : 0;

        LogInfo($"收入判定: 人均={perCapitaIncome:F2}");

        // ── 三层判定逻辑 ──

        if (perCapitaIncome >= lowIncomeThreshold)
        {
            // 第一层：收入超标区间
            DetermineHighIncomeClassification(result, isRural, perCapitaIncome, standard, lowIncomeThreshold,
                rigidExpenditure, familySize, allAbove60, hasSevereCondition);
        }
        else if (perCapitaIncome >= standard)
        {
            // 第二层：最低生活保障边缘区间
            DetermineLowIncomeClassification(result, isRural, hasSevereCondition, hasSingleRescueCondition, hasLaborAbility,
                perCapitaIncome, standard, familySize, singleRescueCandidates);
        }
        else
        {
            // 第三层：低保区间
            DetermineSubsistenceClassification(result, isRural, isSingleHousehold, hasNoSupport, hasCaregiver,
                allAbove60, hasSevereDisability, hasSevereCondition, hasLaborAbility,
                perCapitaIncome, standard, familySize, application.DestituteSupportType ?? "", caregivers);
        }

        result.Description = GetClassificationDescription(result.Classification);

        // 计算保障金额
        var guaranteeResult = await CalculateGuaranteeAmountAsync(
            result.Classification, familySize, isRural, totalIncome, ct);
        result.GuaranteeAmount = guaranteeResult.IsSuccess ? guaranteeResult.Value : 0;

        // 检查渐退期（oldClassification 优先取变更链上游原分类，无则取当前分类）
        var oldForGrace = !string.IsNullOrEmpty(application.OriginalClassificationResult)
            ? application.OriginalClassificationResult!
            : application.ClassificationResult ?? "";
        var gracePeriod = CheckGracePeriodEligibility(
            oldForGrace, result.Classification,
            perCapitaIncome, standard);
        result.GracePeriod = gracePeriod;
        result.ShouldTriggerGracePeriod = gracePeriod.IsEligible;

        // 计算分类施保（仅最低生活保障/单人保享受；最低生活保障边缘、特困人员、刚性支出困难家庭不享受）
        if (result.IsEligible
            && !ClassificationConstants.IsCodeStop(result.Classification)
            && !ClassificationConstants.IsCodeLowIncome(result.Classification)
            && !ClassificationConstants.IsCodeDestitute(result.Classification)
            && !ClassificationConstants.IsCodeRigidExpenditure(result.Classification))
        {
            var subsidyResult = await CalculateClassifiedSubsidyAsync(isRural, application, householdMembers, ct);
            result.ClassifiedSubsidy = subsidyResult;
        }

        LogInfo($"分类判定完成: {result.Classification} ({result.Description})");

        return Result.Success(result);
    }

    /// <summary>
    /// 执行分类判定（简化版，不含赡养人/照料人）
    /// </summary>
    public async Task<Result<ClassificationResult>> DetermineClassificationAsync(
        ApplicationEntity application,
        List<FamilyMember> members,
        CancellationToken ct = default)
    {
        return await DetermineClassificationAsync(application, members, new List<Supporter>(), new List<Caregiver>(), ct);
    }

    public async Task<Result<ClassificationResult>> ClassifyAsync(
        string hukouType, decimal perCapitaIncome, int familySize,
        List<string> healthConditions, CancellationToken ct = default)
    {
        LogInfo($"执行分类: hukouType={hukouType}");
        var isRural = ClassificationConstants.HukouType.IsHukouRural(hukouType);
        var classification = isRural ? ClassificationConstants.RuralSubsistence : ClassificationConstants.UrbanSubsistence;
        var standard = await GetSubsistenceStandardAsync(isRural, ct);
        var amount = await CalculateGuaranteeAmountAsync(classification, familySize, isRural, 0, ct);

        var result = new ClassificationResult
        {
            Classification = classification,
            ClassificationCode = classification,
            Description = GetClassificationDescription(classification),
            IsEligible = true,
            GuaranteeAmount = amount.IsSuccess ? amount.Value : standard * familySize
        };
        return Result.Success(result);
    }

    /// <summary>
    /// 计算保障金额
    /// </summary>
    public async Task<Result<decimal>> CalculateGuaranteeAmountAsync(
        string classification, int familySize, bool isRural,
        decimal totalFamilyIncome = 0, CancellationToken ct = default)
    {
        // 月标准（用于保障金额计算，结果是月金额）
        var monthlyStandard = await GetSubsistenceStandardAsync(isRural, ct);

        var singleRescueAmount = await GetSingleRescueAmountAsync(isRural, ct);

        var amount = classification switch
        {
            // 低保：补差模式 = 低保标准×家庭人数 − 家庭月总收入，有小数进一（取整到元）
            // 注意：禁止先除人数求人均再乘回——decimal 尾数会让 Ceiling 多算 1 元
            //（实测月总收入 1894/3 人：人均 631.333...，(632−人均)×3=2.0000...001→Ceiling=3，
            //  而 632×3−1894=2 才是正确补差）。totalFamilyIncome 为月总收入（年值÷12）。
            ClassificationConstants.RuralSubsistence or ClassificationConstants.UrbanSubsistence
                => Math.Ceiling(monthlyStandard * familySize - totalFamilyIncome),

            // 最低生活保障边缘：不发放保障金
            ClassificationConstants.RuralLowIncome or ClassificationConstants.UrbanLowIncome
                => 0m,

            // 单人保：固定金额
            ClassificationConstants.RuralLowIncomeSingle or ClassificationConstants.UrbanLowIncomeSingle
                => singleRescueAmount,

            // 特困分散：从配置读取分散供养标准
            ClassificationConstants.RuralDestituteScattered or ClassificationConstants.UrbanDestituteScattered
                => await GetDestituteStandardAsync(isRural, "Scattered", ct),

            // 特困集中：从配置读取集中供养标准
            ClassificationConstants.RuralDestituteCentralized or ClassificationConstants.UrbanDestituteCentralized
                => await GetDestituteStandardAsync(isRural, "Centralized", ct),

            // 刚性支出：不发放保障金
            ClassificationConstants.RuralRigidExpenditure or ClassificationConstants.UrbanRigidExpenditure
                => 0m,

            _ => 0m
        };

        // 确保金额不为负数
        if (amount < 0) amount = 0;

        return Result.Success(amount);
    }

    /// <summary>
    /// 检查渐退期资格
    /// </summary>
    public GracePeriodCheckResult CheckGracePeriodEligibility(
        string oldClassification, string newClassification,
        decimal perCapitaIncome, decimal standard)
    {
        var result = new GracePeriodCheckResult();

        // 检查是否为最低生活保障→最低生活保障边缘
        var wasSubsistence = ClassificationConstants.IsCodeSubsistence(oldClassification);
        var nowLowIncome = ClassificationConstants.IsCodeLowIncome(newClassification)
                          && !ClassificationConstants.IsCodeSubsistence(newClassification);

        if (!wasSubsistence || !nowLowIncome)
            return result;

        // 检查收入区间
        var lowIncomeThreshold = standard * ClassificationConstants.LowIncomeMultiplier;
        var incomeInRange = perCapitaIncome >= standard && perCapitaIncome < lowIncomeThreshold;

        if (incomeInRange)
        {
            result.IsEligible = true;
            result.OriginalClassification = oldClassification;
            result.Months = GracePeriodConstants.DEFAULT_MONTHS;

            // 计算渐退期日期
            var today = DateTime.Today;
            var nextMonth = new DateTime(today.Year, today.Month, 1).AddMonths(1);
            result.StartDate = nextMonth;
            result.EndDate = nextMonth.AddMonths(result.Months).AddDays(-1);
        }

        return result;
    }

    public string GetClassificationDescription(string classification)
    {
        return ClassificationConstants.ConvertFromCode(classification);
    }

    /// <summary>
    /// 计算分类施保（重病/重残/高龄/未成年；户主与成员各自独立判定，一人 count 一次、types 可叠加）
    /// </summary>
    public async Task<ClassifiedSubsidyResult> CalculateClassifiedSubsidyAsync(
        bool isRural, ApplicationEntity application,
        List<FamilyMember> members, CancellationToken ct = default)
    {
        // 从数据库读取每人标准
        var perPersonAmount = await GetClassifiedSubsidyPerPersonAsync(isRural, ct);

        var types = new List<string>();
        int count = 0;

        // ── 户主本人纳入分类施保判定（户主信息在主表 nc_biz_applications，不在家庭成员表）──
        bool headEligible = false;

        // ① 重病：主表 is_severe_disease 标记 或 健康状况为重病
        if (application.IsSevereDisease
            || DictionaryConstants.HealthStatus.HasSevereDisease(application.HealthStatus ?? ""))
        {
            if (!types.Contains("重病")) types.Add("重病");
            headEligible = true;
        }

        // ② 重残：健康状况为重残 或 残疾等级属重度（一、二级任意类型；三级智力/精神）
        if (DictionaryConstants.HealthStatus.HasSevereDisability(application.HealthStatus ?? "")
            || ClassificationConstants.DisabilityLevel.IsSevereForAssistance(
                application.DisabilityLevel ?? "", application.DisabilityType))
        {
            if (!types.Contains("重残")) types.Add("重残");
            headEligible = true;
        }

        // ③ 高龄：>=60 岁
        var headAge = CalculateAgeFromIdCard(application.ApplicantIdCard ?? "");
        if (headAge >= AgeConstants.ELDERLY_THRESHOLD)
        {
            if (!types.Contains("高龄")) types.Add("高龄");
            headEligible = true;
        }

        // ④ 未成年：<18 岁
        if (headAge < AgeConstants.MINOR_THRESHOLD)
        {
            if (!types.Contains("未成年")) types.Add("未成年");
            headEligible = true;
        }

        // 一人 count 只计 1 次，types 可叠加
        if (headEligible) count++;

        // ── 家庭成员逐人判定（types 叠加 / count 每人一次）──
        // 户主已在主表计过一次（上方①~④），成员表若存户主行（is_applicant=true / 关系=本人）会双计
        foreach (var member in members)
        {
            if (IsHouseholdHeadMember(member, application)) continue;

            bool eligible = false;

            if (member.IsSevereDisease || DictionaryConstants.HealthStatus.HasSevereDisease(member.HealthStatus ?? ""))
            {
                if (!types.Contains("重病")) types.Add("重病");
                eligible = true;
            }

            if (member.IsSevereDisability
                || DictionaryConstants.HealthStatus.HasSevereDisability(member.HealthStatus ?? "")
                || ClassificationConstants.DisabilityLevel.IsSevereForAssistance(
                    member.DisabilityLevelKeyResolved, member.DisabilityType))
            {
                if (!types.Contains("重残")) types.Add("重残");
                eligible = true;
            }

            if (member.Age >= AgeConstants.ELDERLY_THRESHOLD)
            {
                if (!types.Contains("高龄")) types.Add("高龄");
                eligible = true;
            }

            if (member.Age < AgeConstants.MINOR_THRESHOLD)
            {
                if (!types.Contains("未成年")) types.Add("未成年");
                eligible = true;
            }

            if (eligible) count++;
        }

        return new ClassifiedSubsidyResult
        {
            Types = types.Count > 0 ? string.Join("、", types) : "",
            Count = count,
            PerPersonAmount = perPersonAmount,
            TotalAmount = count * perPersonAmount
        };
    }

    // ── 私有方法：判定逻辑 ──

    /// <summary>
    /// 成员行是否为户主本人。户主信息权威在主表 nc_biz_applications，
    /// 历史/导入数据的成员表可能同时存有户主行（is_applicant=true、member_category=户主、
    /// 关系=本人、或身份证与申请人相同），判定与计数时须跳过，否则户主被双计。
    /// </summary>
    private static bool IsHouseholdHeadMember(FamilyMember member, ApplicationEntity application, string? normalizedCategory = null)
    {
        if (member.IsApplicant) return true;

        var category = normalizedCategory ?? MemberCategoryHelper.Normalize(
            member.MemberCategory, member.IsApplicant, member.RelationshipToHead);
        if (string.Equals(category, MemberCategoryConstants.HOUSEHOLD_HEAD, StringComparison.OrdinalIgnoreCase))
            return true;

        var memberIdCard = member.IdCard?.Trim();
        var applicantIdCard = application.ApplicantIdCard?.Trim();
        return !string.IsNullOrEmpty(memberIdCard)
            && string.Equals(memberIdCard, applicantIdCard, StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// 第一层：收入超标区间判定（刚性支出困难家庭）
    /// </summary>
    private void DetermineHighIncomeClassification(
        ClassificationResult result, bool isRural,
        decimal perCapitaIncome, decimal standard, decimal lowIncomeThreshold,
        decimal rigidExpenditure, int familySize,
        bool allAbove60, bool hasSevereCondition)
    {
        var perCapitaRigidExpenditure = rigidExpenditure / familySize;
        var rigidExpenditureRatio = perCapitaIncome > 0 ? perCapitaRigidExpenditure / perCapitaIncome : 0;
        var incomeAfterDeduction = perCapitaIncome - perCapitaRigidExpenditure;

        // 【规则】刚性支出占收入比例 >50% 且 <60%
        bool hasHighRigidExpenditure = rigidExpenditureRatio > 0.5m && rigidExpenditureRatio < 0.6m;
        // 【规则】家庭收入 < 低保标准的2倍
        bool incomeBelowDoubleStandard = perCapitaIncome < standard * 2;
        // 【规则】扣减后家庭收入不得低于低保标准
        bool incomeAfterDeductionMet = incomeAfterDeduction >= standard;

        result.DeterminationDetails.Add($"刚性支出占比: {rigidExpenditureRatio:P2}");
        result.DeterminationDetails.Add($"扣除刚性支出后人均: {incomeAfterDeduction:F2}");

        if (hasHighRigidExpenditure && incomeBelowDoubleStandard && incomeAfterDeductionMet)
        {
            result.Classification = isRural
                ? ClassificationConstants.RuralRigidExpenditure
                : ClassificationConstants.UrbanRigidExpenditure;
            result.IsEligible = true;
            result.DeterminationBasis = "刚性支出占收入50%-60%且家庭收入低于低保标准2倍，扣减后不低于低保标准";
        }
        else
        {
            result.Classification = isRural
                ? ClassificationConstants.RuralIncomeExceeded
                : ClassificationConstants.UrbanIncomeExceeded;
            result.IsEligible = false;
            result.IneligibleReason = "不符合刚性支出困难家庭条件";
        }

        result.DeterminationDetails.Add($"家庭人数: {familySize}");
        result.DeterminationDetails.Add($"收入超标判定完成");
    }

    /// <summary>
    /// 第二层：最低生活保障边缘区间判定
    /// </summary>
    private void DetermineLowIncomeClassification(
        ClassificationResult result, bool isRural,
        bool hasSevereCondition, bool hasSingleRescueCondition, bool hasLaborAbility,
        decimal perCapitaIncome, decimal standard, int familySize,
        List<FamilyMember> singleRescueCandidates)
    {
        // 【规则】劳动力口径与低保层统一：无劳动力（未成年/老年/免劳豁免）且无重病重残即符合低保边缘家庭
        if (!hasSevereCondition && hasLaborAbility)
        {
            // 没有重病重残，且有劳动力（劳动年龄内健康/一般成员）→ 不符合
            result.Classification = isRural
                ? ClassificationConstants.IneligibleWithLabor
                : ClassificationConstants.IneligibleWithLabor;
            result.IsEligible = false;
            result.IneligibleReason = "最低生活保障边缘区间但无重病重残且有劳动力";
        }
        else if (hasSevereCondition)
        {
            // 户主或者家庭成员有重病或重残 → 最低生活保障边缘家庭 + 单人保提示（三级/四级残疾不计重残）
            result.Classification = isRural
                ? ClassificationConstants.RuralLowIncome
                : ClassificationConstants.UrbanLowIncome;
            result.IsEligible = true;

            if (hasSingleRescueCondition)
            {
                result.DeterminationBasis = "最低生活保障边缘区间，有重病、重残（含三级智力/精神）成员符合单人保政策";

                // 标记需要创建单人保草稿（候选含户主本人）
                result.NeedsSingleRescueDraft = true;
                result.SingleRescueMembers = singleRescueCandidates;

                var severeMemberNames = singleRescueCandidates.Select(m => m.Name).ToList();
                if (severeMemberNames.Count > 0)
                {
                    result.DeterminationDetails.Add($"⚠️ 需要新增单人保: 户主/家庭成员 {string.Join("、", severeMemberNames)} 符合单人保政策");
                }
            }
            else
            {
                result.DeterminationBasis = "最低生活保障边缘区间（成员残疾等级未达重度，不符合单人保条件）";
            }
        }
        else
        {
            // 年龄>=60岁 → 最低生活保障边缘家庭
            result.Classification = isRural
                ? ClassificationConstants.RuralLowIncome
                : ClassificationConstants.UrbanLowIncome;
            result.IsEligible = true;
            result.DeterminationBasis = "最低生活保障边缘区间且年龄>=60岁，按最低生活保障边缘家庭保障";
        }

        result.DeterminationDetails.Add($"家庭人数: {familySize}");
        result.DeterminationDetails.Add($"最低生活保障边缘区间判定完成");
    }

    /// <summary>
    /// 第三层：低保区间判定
    /// </summary>
    private void DetermineSubsistenceClassification(
        ClassificationResult result, bool isRural,
        bool isSingleHousehold, bool hasNoSupport, bool hasCaregiver,
        bool allAbove60, bool hasSevereDisability, bool hasSevereCondition,
        bool hasLaborAbility,
        decimal perCapitaIncome, decimal standard, int familySize,
        string destituteSupportType = "",
        List<Caregiver> caregivers = null)
    {
        // 【规则】特困判定条件：
        // 1. 单人户（家庭成员只有一个人）
        // 2. 年龄>=60岁 或 (年龄<60岁且重残)
        // 3. 收入低于低保标准（已在第三层，自动满足）
        // 4. 无法定赡养、抚养、扶养义务人，或其义务人无履行义务能力（无子女等赡养抚养扶养人）
        bool isEligibleForDestitute = isSingleHousehold && (allAbove60 || hasSevereDisability) && hasNoSupport;

        if (isEligibleForDestitute)
        {
            // 特困供养 - 根据 DestituteSupportType 区分集中/分散
            bool isCentralized = destituteSupportType == "Centralized"
                || destituteSupportType == ClassificationConstants.SupportMode.CENTRALIZED;

            result.Classification = (isRural, isCentralized) switch
            {
                (true, true) => ClassificationConstants.RuralDestituteCentralized,
                (true, false) => ClassificationConstants.RuralDestituteScattered,
                (false, true) => ClassificationConstants.UrbanDestituteCentralized,
                (false, false) => ClassificationConstants.UrbanDestituteScattered,
            };
            result.IsEligible = true;
            result.DeterminationBasis = isCentralized
                ? "单人户且无赡养抚养扶养义务人，符合特困条件（集中供养）"
                : "单人户且无赡养抚养扶养义务人，符合特困条件（分散供养）";

            result.DeterminationDetails.Add($"单人户: {isSingleHousehold}");
            result.DeterminationDetails.Add($"年龄>=60: {allAbove60}");
            result.DeterminationDetails.Add($"重残: {hasSevereDisability}");
            result.DeterminationDetails.Add($"无赡养抚养扶养义务人(或义务人无能力): {hasNoSupport}");
        }
        else
        {
            // 普通低保判定
            bool healthConditionMet = allAbove60 || hasSevereCondition;

            if (!healthConditionMet && hasLaborAbility)
            {
                // 无重病重残且有劳动力 → 不符合认定条件
                result.Classification = ClassificationConstants.IneligibleWithLabor;
                result.IsEligible = false;
                result.IneligibleReason = "有劳动力且无重病重残，不符合低保条件";
            }
            else
            {
                // 其他情况 → 低保
                result.Classification = isRural
                    ? ClassificationConstants.RuralSubsistence
                    : ClassificationConstants.UrbanSubsistence;
                result.IsEligible = true;
                result.DeterminationBasis = "收入低于低保标准且符合健康条件";
            }
        }

        result.DeterminationDetails.Add($"家庭人数: {familySize}");
        result.DeterminationDetails.Add($"低保区间判定完成");
        // 标准为月标准，展示统一折为年值（与年值基准口径一致）
        result.DeterminationDetails.Add($"低保标准: ¥{standard * 12m:F2}/年");
    }

    // ── 私有方法：辅助判定 ──

    /// <summary>
    /// 加载在读大学生身份证集合（大学生档案 status=Studying，按身份证匹配户内成员与户主）。
    /// 查询失败只记警告、返回空集合（按"无豁免"继续判定），不阻断分类流程。
    /// </summary>
    private async Task<HashSet<string>> LoadStudyingIdCardsAsync(
        ApplicationEntity application, List<FamilyMember> householdMembers, CancellationToken ct)
    {
        var idCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in householdMembers)
        {
            var idc = (m.IdCard ?? "").Trim();
            if (idc.Length > 0) idCards.Add(idc);
        }
        var headIdCard = (application.ApplicantIdCard ?? "").Trim();
        if (headIdCard.Length > 0) idCards.Add(headIdCard);

        if (idCards.Count == 0) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var lookup = await _collegeStudentService.GetStudyingIdCardsAsync(idCards.ToList(), ct);
        if (lookup.IsSuccess && lookup.Value != null)
            return new HashSet<string>(lookup.Value, StringComparer.OrdinalIgnoreCase);

        LogWarn($"在读大学生身份证查询失败，本次判定不应用学生劳动力豁免: {lookup.ErrorCode} {lookup.Message}");
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private bool CalculateHasLaborAbility(List<FamilyMember> members, HashSet<string> studyingIdCards)
    {
        var laborAbilities = new[] { HealthStatusConstants.HEALTHY, HealthStatusConstants.FAIR_OR_WEAK };
        foreach (var member in members)
        {
            // 因照顾本户重病/重残亲属而免于劳动力判定的成员不计入"有劳动力"
            if (member.IsLaborExempt)
                continue;
            // 在读大学生（大学生档案 status=Studying）免于劳动力判定
            var mid = (member.IdCard ?? "").Trim().ToUpperInvariant();
            if (mid.Length > 0 && studyingIdCards.Contains(mid))
                continue;
            // 重残（一、二级任意类型及三级智力/精神）无劳动能力
            if (ClassificationConstants.DisabilityLevel.IsSevereForAssistance(
                    member.DisabilityLevelKeyResolved, member.DisabilityType))
                continue;
            // 未成年（<18）不计入劳动力
            if (member.Age is < AgeConstants.MINOR_THRESHOLD)
                continue;
            // 60 岁以上（老年）不计入劳动力
            if (member.Age is >= AgeConstants.ELDERLY_THRESHOLD)
                continue;
            if (laborAbilities.Contains(member.HealthStatus ?? ""))
                return true;
        }
        return false;
    }

    private bool CheckHasSevereDisease(List<FamilyMember> members)
    {
        foreach (var member in members)
        {
            if (member.IsSevereDisease || DictionaryConstants.HealthStatus.HasSevereDisease(member.HealthStatus ?? ""))
                return true;
        }
        return false;
    }

    private bool CheckHasSevereDisability(List<FamilyMember> members)
    {
        foreach (var member in members)
        {
            if (member.IsSevereDisability
                || DictionaryConstants.HealthStatus.HasSevereDisability(member.HealthStatus ?? "")
                || ClassificationConstants.DisabilityLevel.IsSevereForAssistance(
                    member.DisabilityLevelKeyResolved, member.DisabilityType))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 单人保口径的重残判定：一、二级任意残疾类型及三级智力/精神计入重残；
    /// 其余三级/四级不计（即使健康状况/标记为重残）；等级未知（空值）时按标记与健康状况回退。
    /// </summary>
    private static bool HasSevereDisabilityForSingleRescue(
        string levelKey, string? disabilityTypeKey, bool isSevereDisability, string? healthStatus)
    {
        if (ClassificationConstants.DisabilityLevel.IsSevereForAssistance(levelKey, disabilityTypeKey))
            return true;
        if (ClassificationConstants.DisabilityLevel.IsNonSevere(levelKey))
            return false;

        return isSevereDisability
            || DictionaryConstants.HealthStatus.HasSevereDisability(healthStatus ?? "");
    }

    /// <summary>
    /// 单人保候选人员：家庭成员中重病、重残（含三级智力/精神）者；户主本人符合时置顶纳入。
    /// </summary>
    private static List<FamilyMember> BuildSingleRescueCandidates(
        ApplicationEntity application, List<FamilyMember> householdMembers, int headAge)
    {
        var candidates = householdMembers
            .Where(m => m.IsSevereDisease
                || DictionaryConstants.HealthStatus.HasSevereDisease(m.HealthStatus ?? "")
                || HasSevereDisabilityForSingleRescue(
                    m.DisabilityLevelKeyResolved, m.DisabilityType, m.IsSevereDisability, m.HealthStatus))
            .ToList();

        if (HasHeadSingleRescueCondition(application))
            candidates.Insert(0, BuildHeadSingleRescueMember(application, headAge));

        return candidates;
    }

    private static bool HasHeadSingleRescueCondition(ApplicationEntity application) =>
        application.IsSevereDisease
        || DictionaryConstants.HealthStatus.HasSevereDisease(application.HealthStatus ?? "")
        || HasSevereDisabilityForSingleRescue(
            application.DisabilityLevel ?? "",
            application.DisabilityType,
            ClassificationConstants.DisabilityLevel.IsSevere(application.DisabilityLevel ?? ""),
            application.HealthStatus);

    /// <summary>由主表（户主）字段合成家庭成员对象，供单人保草稿创建使用</summary>
    private static FamilyMember BuildHeadSingleRescueMember(ApplicationEntity application, int headAge)
    {
        var levelKey = application.DisabilityLevel ?? "";
        var idCard = application.ApplicantIdCard ?? "";
        return new FamilyMember
        {
            Name = application.ApplicantName ?? "",
            IdCard = idCard,
            Gender = application.Gender ?? "",
            Age = headAge > 0 ? headAge : (int?)null,
            BirthDate = IdCardValidator.ExtractBirthDate(idCard) ?? default,
            Ethnicity = application.Ethnicity ?? "",
            Phone = application.ApplicantPhone ?? "",
            MaritalStatus = application.MaritalStatus ?? "",
            EducationLevel = application.EducationLevel ?? "",
            PoliticalStatus = application.PoliticalStatus ?? "",
            HealthStatus = application.HealthStatus ?? "",
            DiseaseCategory = application.DiseaseName ?? "",
            DiseaseName = application.SecondaryDiseaseName ?? "",
            DiseaseCode = application.DiseaseCode ?? "",
            DisabilityType = application.DisabilityType ?? "",
            DisabilityLevel = levelKey,
            DisabilityLevelKey = levelKey,
            IsSevereDisease = application.IsSevereDisease,
            IsDisabled = !string.IsNullOrEmpty(application.DisabilityType),
            IsSevereDisability = ClassificationConstants.DisabilityLevel.IsSevereForAssistance(
                levelKey, application.DisabilityType),
            // 户籍信息
            HukouType = application.HukouType ?? "",
            HukouAddress = application.HukouAddress ?? "",
            HukouProvince = application.HukouAddressInfo?.Province ?? "",
            HukouCity = application.HukouAddressInfo?.City ?? "",
            HukouDistrict = application.HukouAddressInfo?.District ?? "",
            HukouTown = application.HukouAddressInfo?.Town ?? "",
            // 家庭住址
            HomeProvince = application.Province ?? "",
            HomeCity = application.City ?? "",
            HomeDistrict = application.District ?? "",
            HomeTown = application.Town ?? "",
            HomeVillage = application.Village ?? "",
            HomeAddress = application.Address ?? "",
            // 就业/收入
            EmploymentStatus = application.EmploymentStatus ?? "",
            WorkUnit = application.WorkUnit ?? "",
            MainIncomeSource = application.IncomeSource ?? "",
            // 关系
            RelationshipToHead = "本人/户主",
            IsHouseholdHead = true,
            MemberCategory = MemberCategoryConstants.HOUSEHOLD_HEAD
        };
    }

    private bool CheckAllMembersOver60(List<FamilyMember> members)
    {
        // 如果没有家庭成员，只看户主年龄（由调用方判断）
        if (members.Count == 0) return true;

        foreach (var member in members)
        {
            if (member.Age < AgeConstants.ELDERLY_THRESHOLD)
                return false;
        }
        return true;
    }

    private async Task<decimal> GetSubsistenceStandardAsync(bool isRural, CancellationToken ct = default)
    {
        var type = isRural ? "RuralSubsistenceStandard" : "UrbanSubsistenceStandard";
        var hukou = isRural ? "Rural" : "Urban";
        var result = await _standardConfigService.GetStandardValueAsync(type, hukou, ct: ct);
        if (!result.IsSuccess || result.Value <= 0)
            throw new InvalidOperationException(
                $"低保标准配置缺失或无效（{type}/{hukou}）：{result.Message}。" +
                "请在「数据中心-标准配置管理」中维护有效标准后再试，系统禁止使用过期回退值计算保障金。");
        return result.Value;
    }

    private async Task<decimal> GetSingleRescueAmountAsync(bool isRural, CancellationToken ct = default)
    {
        var type = isRural ? "RuralSingleRescue" : "UrbanSingleRescue";
        var hukou = isRural ? "Rural" : "Urban";
        var result = await _standardConfigService.GetStandardValueAsync(type, hukou, ct: ct);
        if (!result.IsSuccess || result.Value <= 0)
            throw new InvalidOperationException(
                $"单人保金额配置缺失或无效（{type}/{hukou}）：{result.Message}。" +
                "请在「数据中心-标准配置管理」中维护有效标准后再试。");
        return result.Value;
    }

    /// <summary>
    /// 获取特困供养标准（从配置读取；配置缺失即失败，不使用过期回退值）
    /// </summary>
    private async Task<decimal> GetDestituteStandardAsync(bool isRural, string supportMode, CancellationToken ct = default)
    {
        var type = isRural ? "RuralDestituteStandard" : "UrbanDestituteStandard";
        var hukou = isRural ? "Rural" : "Urban";
        var result = await _standardConfigService.GetStandardValueAsync(type, hukou, supportMode, ct);
        if (!result.IsSuccess || result.Value <= 0)
            throw new InvalidOperationException(
                $"特困供养标准配置缺失或无效（{type}/{hukou}/{supportMode}）：{result.Message}。" +
                "请在「数据中心-标准配置管理」中维护有效标准后再试。");
        return result.Value;
    }

    private async Task<decimal> GetClassifiedSubsidyPerPersonAsync(bool isRural, CancellationToken ct = default)
    {
        var hukou = isRural ? "Rural" : "Urban";
        var result = await _standardConfigService.GetStandardValueAsync("ClassifiedSubsidyStandard", hukou, ct: ct);
        if (!result.IsSuccess || result.Value <= 0)
            throw new InvalidOperationException(
                $"分类补贴标准配置缺失或无效（ClassifiedSubsidyStandard/{hukou}）：{result.Message}。" +
                "请在「数据中心-标准配置管理」中维护有效标准后再试。");
        return result.Value;
    }

    /// <summary>
    /// 计算特困人员照料护理费：读取能力鉴定，按自理能力等级 5 档映射三档标准
    /// 全自理→全自理档；轻度/中度失能→半自理档；重度/完全失能→无法自理档
    /// 无能力鉴定记录时返回 0（不清除既有值）
    /// </summary>
    public async Task<decimal> CalculateCareAllowanceAsync(long applicationId, CancellationToken ct = default)
    {
        var assessmentResult = await _capabilityAssessmentService.GetByApplicationIdAsync(applicationId, ct);
        if (!assessmentResult.IsSuccess || assessmentResult.Value is null)
        {
            LogInfo($"照料护理费：申请 {applicationId} 无能力鉴定记录，返回 0");
            return 0m;
        }

        var selfCareLevel = assessmentResult.Value.SelfCareLevel;
        var standardType = selfCareLevel switch
        {
            DictionaryConstants.CapabilityLevel.FULL_SELF_CARE => "CaregiverAllowanceFull",
            DictionaryConstants.CapabilityLevel.MILD_DISABILITY
                or DictionaryConstants.CapabilityLevel.MODERATE_DISABILITY => "CaregiverAllowancePartial",
            DictionaryConstants.CapabilityLevel.SEVERE_DISABILITY
                or DictionaryConstants.CapabilityLevel.COMPLETE_DISABILITY => "CaregiverAllowanceNone",
            _ => null
        };

        if (standardType == null)
        {
            LogInfo($"照料护理费：未识别的自理能力等级 {selfCareLevel}，返回 0");
            return 0m;
        }

        var result = await _standardConfigService.GetStandardValueAsync(standardType, ct: ct);
        if (result.IsSuccess)
            return result.Value;

        LogWarn($"照料护理费读取失败: {result.Message}");
        return 0m;
    }

    private static int CalculateAgeFromIdCard(string idCard)
    {
        var age = Helpers.IdCardValidator.ExtractAgeBasic(idCard);
        return age ?? 0;
    }
}
