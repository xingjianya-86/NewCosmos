namespace NewCosmos.Constants;

/// <summary>
/// 变更原因类型（nc_biz_change_records.change_reason_type）
/// </summary>
public static class ChangeReasonTypeConstants
{
    /// <summary>经济复核：家庭收入变化导致的保障金调整（增发=收入减少/减发=收入增加）</summary>
    public const string EconomicReview = "经济复核";

    /// <summary>户主死亡</summary>
    public const string HeadDeceased = "户主死亡";

    /// <summary>家庭成员变更：增员/减员导致的重新认定（停旧建新）</summary>
    public const string MemberChange = "成员变更";

    /// <summary>跨大类转入（接续链 Step5 分类判定补写 CategoryAdd 时，非死亡/成员变更链的兜底原因类型）</summary>
    public const string CrossCategoryTransfer = "跨大类转入";
}