namespace NewCosmos.Constants;

/// <summary>
/// 打印业务类型（PrintNavigationData.BusinessType / PrintQueueService.PrintJobRequest.BusinessType /
/// PrintExecuteService.PrintPermissionMap 的键）。此前 30+ 处字面量散落（"FamilyApplication"/"Recovery"/…），
/// 改名即静默失效（权限映射、留痕归类、档案分类解析全依赖这些键）——统一从本类引用。
/// 注：已有专属常量的域继续用其自身常量（TempReliefConstants.BusinessType / NearRelativeConstants.BusinessType），
/// 二者值必须与本类一致。
/// </summary>
public static class PrintBusinessTypes
{
    /// <summary>救助申请家庭档案（低保/边缘/特困等五类通用档案）</summary>
    public const string FamilyApplication = "FamilyApplication";

    /// <summary>后补追缴（追缴资金办理单）</summary>
    public const string Recovery = "Recovery";

    /// <summary>高龄津贴（登记/停发/复核文书）</summary>
    public const string ElderlyBenefits = "ElderlyBenefits";

    /// <summary>家庭经济状况核对（快速核查授权承诺书等）</summary>
    public const string AssetVerification = "AssetVerification";

    /// <summary>核对月报表</summary>
    public const string AssetVerificationMonthlyReport = "AssetVerificationMonthlyReport";

    /// <summary>低收入证明（证明类文书留痕归类）</summary>
    public const string LowIncomeProof = "LowIncomeProof";

    /// <summary>死亡情况说明</summary>
    public const string DeathStatement = "DeathStatement";

    /// <summary>动态管理记录（变更/复核历史留痕原样补打）</summary>
    public const string DynamicRecord = "DynamicRecord";

    /// <summary>临时救助（与 TempReliefConstants.BusinessType 同值）</summary>
    public const string TempRelief = "TempRelief";

    /// <summary>近亲属备案（与 NearRelativeConstants.BusinessType 同值）</summary>
    public const string NearRelative = "NearRelative";

    /// <summary>档案照片（照片打印/补传留痕）</summary>
    public const string ArchivePhoto = "ArchivePhoto";

    /// <summary>经济复核（档案制作中心的复核装配上下文）</summary>
    public const string EconomicReview = "EconomicReview";
}
