using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.SocialAssistance;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

/// <summary>
/// 分类判定服务接口
/// </summary>
public interface IClassificationService
{
    /// <summary>
    /// 执行完整分类判定（含赡养人、照料人    /// </summary>
    Task<Result<ClassificationResult>> DetermineClassificationAsync(
        ApplicationEntity application, 
        List<FamilyMember> members,
        List<Supporter> supporters,
        List<Caregiver> caregivers,
        CancellationToken ct = default);

    /// <summary>
    /// 执行分类判定（简化版，不含赡养人/照料人）
    /// </summary>
    Task<Result<ClassificationResult>> DetermineClassificationAsync(
        ApplicationEntity application, 
        List<FamilyMember> members, 
        CancellationToken ct = default);

    /// <summary>
    /// 执行分类（参数版    /// </summary>
    Task<Result<ClassificationResult>> ClassifyAsync(
        string hukouType, 
        decimal perCapitaIncome, 
        int familySize, 
        List<string> healthConditions, 
        CancellationToken ct = default);

    /// <summary>
    /// 计算保障金额
    /// </summary>
    Task<Result<decimal>> CalculateGuaranteeAmountAsync(
        string classification, 
        int familySize, 
        bool isRural, 
        decimal totalFamilyIncome = 0,
        CancellationToken ct = default);

    /// <summary>
    /// 检查是否符合渐退期条    /// </summary>
    GracePeriodCheckResult CheckGracePeriodEligibility(
        string oldClassification, 
        string newClassification,
        decimal perCapitaIncome,
        decimal standard);

    /// <summary>
    /// 获取分类描述
    /// </summary>
    string GetClassificationDescription(string classification);

    /// <summary>
    /// 计算分类施保（重病/重残/高龄/未成年；户主与成员各自独立判定，一人 count 一次、types 可叠加）
    /// </summary>
    Task<ClassifiedSubsidyResult> CalculateClassifiedSubsidyAsync(
        bool isRural,
        ApplicationEntity application,
        List<FamilyMember> members,
        CancellationToken ct = default);

    /// <summary>
    /// 计算特困人员照料护理费（读取能力鉴定，按自理能力等级映射三档标准）
    /// </summary>
    Task<decimal> CalculateCareAllowanceAsync(long applicationId, CancellationToken ct = default);
}

/// <summary>
/// 分类判定结果
/// </summary>
public class ClassificationResult
{
    /// <summary>
    /// 分类代码
    /// </summary>
    public string Classification { get; set; } = string.Empty;

    /// <summary>
    /// 分类代码（别名）
    /// </summary>
    public string ClassificationCode { get => Classification; set => Classification = value; }

    /// <summary>
    /// 分类描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 保障金额
    /// </summary>
    public decimal GuaranteeAmount { get; set; }

    /// <summary>
    /// 是否符合条件
    /// </summary>
    public bool IsEligible { get; set; }

    /// <summary>
    /// 不符合原    /// </summary>
    public string IneligibleReason { get; set; } = string.Empty;

    /// <summary>
    /// 是否触发渐退    /// </summary>
    public bool ShouldTriggerGracePeriod { get; set; }

    /// <summary>
    /// 渐退期信    /// </summary>
    public GracePeriodCheckResult GracePeriod { get; set; } = new();

    /// <summary>
    /// 分类施保信息
    /// </summary>
    public ClassifiedSubsidyResult ClassifiedSubsidy { get; set; } = new();

    /// <summary>
    /// 判定依据
    /// </summary>
    public string DeterminationBasis { get; set; } = string.Empty;

    /// <summary>
    /// 判定路径详情
    /// </summary>
    public List<string> DeterminationDetails { get; set; } = new();

    /// <summary>
    /// 是否需要创建单人保草稿
    /// </summary>
    public bool NeedsSingleRescueDraft { get; set; }

    /// <summary>
    /// 需要创建单人保的成员列表
    /// </summary>
    public List<FamilyMember> SingleRescueMembers { get; set; } = new();
}

/// <summary>
/// 渐退期检查结    /// </summary>
public class GracePeriodCheckResult
{
    /// <summary>
    /// 是否符合渐退期条    /// </summary>
    public bool IsEligible { get; set; }

    /// <summary>
    /// 渐退前原分类
    /// </summary>
    public string OriginalClassification { get; set; } = string.Empty;

    /// <summary>
    /// 渐退期月数（默认6个月    /// </summary>
    public int Months { get; set; } = 6;

    /// <summary>
    /// 渐退期开始日    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// 渐退期结束日    /// </summary>
    public DateTime EndDate { get; set; }
}

/// <summary>
/// 分类施保计算结果
/// </summary>
public class ClassifiedSubsidyResult
{
    /// <summary>
    /// 符合条件的类型（重病/重残/高龄/未成年）
    /// </summary>
    public string Types { get; set; } = string.Empty;

    /// <summary>
    /// 符合条件的人    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// 每人标准金额
    /// </summary>
    public decimal PerPersonAmount { get; set; }

    /// <summary>
    /// 分类施保总金    /// </summary>
    public decimal TotalAmount { get; set; }
}
