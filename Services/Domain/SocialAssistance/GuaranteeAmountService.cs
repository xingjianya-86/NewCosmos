using NewCosmos.Services.Core;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 保障金额计算服务实现
/// 说明：低保补差/单人保/特困标准/照料补贴/分类施保的计算实际由
/// ClassificationService 基于配置（nc_config_*）统一实现，本服务仅保留
/// "保障金总额 = 户月保障 + 分类施保 + 照料护理费"的纯汇总方法
/// （其余旧版硬编码实现已删除，避免与配置口径冲突）。
/// </summary>
public class GuaranteeAmountService : BaseService, IGuaranteeAmountService
{
    protected override string ServiceName => "GuaranteeAmountService";

    public GuaranteeAmountService(ILoggerService logger) : base(logger)
    {
    }

    /// <summary>
    /// 计算保障金总额 = 户月保障金额 + 分类施保金额 + 照料护理补贴
    /// </summary>
    public decimal CalculateTotalGuaranteeAmount(
        decimal householdMonthlyAmount,
        decimal classifiedSubsidyAmount,
        decimal caregiverSubsidyAmount)
    {
        var total = householdMonthlyAmount + classifiedSubsidyAmount + caregiverSubsidyAmount;

        LogInfo($"保障金总额计算: 户月保障={householdMonthlyAmount:F2}, 分类施保={classifiedSubsidyAmount:F2}, " +
                $"照料补贴={caregiverSubsidyAmount:F2}");

        return total;
    }
}
