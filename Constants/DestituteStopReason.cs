namespace NewCosmos.Constants;

/// <summary>
/// 特困供养人员取消（停止）情况表：停发原因 key 归一 + ☑/□ 勾选文本。
/// 「取消备案表」的停发原因选项与勾选文本口径（去世/户籍迁出/低保、低保边缘、特困资格取消/
/// 不符合每月认证要求/其他）与普惠高龄取消备案表完全一致，故**单点复用** <see cref="ElderlyBenefitConstants.BuildStopReasonCheckText"/>，
/// 此处不重复实现选项与勾选逻辑，仅补充特困退出「全场景」→ 原因 key 的归一映射（各停发入口按场景取 key）。
/// </summary>
public static class DestituteStopReason
{
    // ── 场景常量（供各停发入口引用；值即 ElderlyBenefitConstants 的停发原因 key，保持单点） ──
    /// <summary>去世（户主死亡/最后成员死亡整户停止）</summary>
    public const string Deceased = ElderlyBenefitConstants.StopReasonDeceased;

    /// <summary>户籍迁出本县</summary>
    public const string MovedOut = ElderlyBenefitConstants.StopReasonMovedOut;

    /// <summary>特困资格取消（经济复核/成员变更停旧建新、归档终结判定不合格等不再符合特困条件）</summary>
    public const string QualificationLost = ElderlyBenefitConstants.StopReasonQualificationLost;

    /// <summary>不符合每月认证要求</summary>
    public const string CertificationFail = ElderlyBenefitConstants.StopReasonCertificationFail;

    /// <summary>其他（自由文本走 app.StopReason 填入下划线）</summary>
    public const string Other = ElderlyBenefitConstants.StopReasonOther;

    /// <summary>生成「取消（停止）原因」☑/□ 勾选文本：选中项 ☑、其余 □，各选项一行。
    /// reasonKey 非五项之一（含自由文本/null）时全部 □，与高龄取消备案表口径一致。</summary>
    public static string BuildCheckText(string? reasonKey) =>
        ElderlyBenefitConstants.BuildStopReasonCheckText(reasonKey);

    /// <summary>
    /// 自由文本停发原因 → key 归一（制作中心按 app.StopReason 自由文本回填 ☑/□ 用）。
    /// 技术停保场景的句式启发式映射：死亡→去世、迁出/户籍→迁出、
    /// 资格取消/不符合/复核/超标/终止→特困资格取消，其余→其他。无法判定时返回 null（全 □）。
    /// 显式 key（五项常量）原样返回；退出直出入口应直接传 key，本方法仅作自由文本兜底。
    /// </summary>
    public static string? ResolveKeyFromText(string? stopReason)
    {
        if (string.IsNullOrWhiteSpace(stopReason)) return null;
        var t = stopReason.Trim();
        if (t is Deceased or MovedOut or QualificationLost or CertificationFail or Other) return t;
        if (t.Contains("死亡")) return Deceased;
        if (t.Contains("迁出") || t.Contains("户籍")) return MovedOut;
        if (t.Contains("资格") || t.Contains("不符合") || t.Contains("复核") || t.Contains("超标")
            || t.Contains("终止") || t.Contains("停保") || t.Contains("不予认定"))
            return QualificationLost;
        if (t.Contains("认证")) return CertificationFail;
        return Other;
    }
}
