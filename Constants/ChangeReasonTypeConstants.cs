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

    /// <summary>
    /// 户主死亡进入渐退期的分类施保减除附属记录。
    /// 故意不复用 HeadDeceased——复核情况/渐退退出取数按 ('经济复核','户主死亡') 取"最近一条"，
    /// 本记录 id 更大且同日期，会造成旧档文书误读本记录。
    /// </summary>
    public const string ClassifiedSubsidyReduce = "分类施保减除";
}