using NewCosmos.Services.Core;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 保障金额计算服务接口
/// </summary>
public interface IGuaranteeAmountService
{
    /// <summary>
    /// 计算保障金总额 = 户月保障金额 + 分类施保金额 + 照料护理补贴
    /// </summary>
    decimal CalculateTotalGuaranteeAmount(
        decimal householdMonthlyAmount,
        decimal classifiedSubsidyAmount,
        decimal caregiverSubsidyAmount);
}
