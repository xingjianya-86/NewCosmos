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

    /// <summary>
    /// 保障金变更登记（动态管理停旧建新后，旧/新档同属低保群体且户月保障金差 ≥ 阈值时补写的 FundChange）。
    /// 金额一律为户月保障金口径（不含分类施保/照料费，分类施保变动另行保存）；
    /// 是月报"保障金增发/减发表"与《档案_增员减员调整表》增发/减发格的权威取数来源。
    /// 故意不复用 EconomicReview/MemberChange/HeadDeceased——主变更记录仍带各自的 reason_type 供审计，
    /// 本记录仅表达"已登记的保障金调整"，取数/统计按本类型单独识别。
    /// </summary>
    public const string FundChangeRegistration = "保障金变更登记";

    /// <summary>
    /// 分类施保变更（增/减双向通用，挂旧档 + new_application_id 指向新档）。
    /// 与 ClassifiedSubsidyReduce 的区别：不限定"户主死亡进渐退期"，经济复核/成员变更/户主死亡三分支的
    /// 分类施保金额变动一律登记（分类施保为固定档位标准，不套保障金 10 元阈值）。
    /// </summary>
    public const string ClassifiedSubsidyChange = "分类施保变更";
}