using NewCosmos.Constants;

namespace NewCosmos.Helpers;

/// <summary>
/// 家庭成员索引槽位与委托代理字段构建器（模板1 授权承诺书等）：
/// 将"户主 + 其余家庭成员"写入 FieldData 的 FAMILY_MEMBER_*_1..6 槽位（户主恒为索引 1），
/// 并按是否有代理写入 AGENT_* 字段。供档案制作与资产核查等入口共用，避免同套槽位逻辑分叉。
/// </summary>
public static class FamilyFieldBuilder
{
    /// <summary>
    /// 单个成员槽位（Name/IdCard/CertType/Relation 均为展示值，由调用方转换）
    /// </summary>
    public readonly record struct FamilySlot(string Name, string IdCard, string CertType, string Relation);

    /// <summary>
    /// 向字段字典写入家庭成员索引槽位与委托代理字段。
    /// </summary>
    /// <param name="fields">目标字段字典（原地修改）</param>
    /// <param name="members">成员列表，第 1 位必须是户主，最多取前 6 人（模板1 槽位 1..6）</param>
    public static void Build(
        Dictionary<string, string> fields,
        IReadOnlyList<FamilySlot> members,
        bool hasAgent,
        string? agentName,
        string? agentIdCard,
        string? agentCertType,
        string? agentRelation)
    {
        if (fields == null) return;

        for (var i = 0; i < members.Count && i < 6; i++)
        {
            var m = members[i];
            var suffix = i + 1;
            fields[$"{FieldKeys.FAMILY_MEMBER_NAME}_{suffix}"] = m.Name ?? "";
            fields[$"{FieldKeys.FAMILY_MEMBER_ID_CARD}_{suffix}"] = m.IdCard ?? "";
            fields[$"{FieldKeys.FAMILY_MEMBER_CERT_TYPE}_{suffix}"] = m.CertType ?? "";
            fields[$"{FieldKeys.FAMILY_MEMBER_RELATION}_{suffix}"] = m.Relation ?? "";
        }

        fields[FieldKeys.AGENT_NAME] = hasAgent ? agentName ?? "" : "";
        fields[FieldKeys.AGENT_ID_CARD] = hasAgent ? agentIdCard ?? "" : "";
        fields[FieldKeys.AGENT_CERT_TYPE] = hasAgent ? agentCertType ?? "" : "";
        fields[FieldKeys.AGENT_RELATION] = hasAgent ? agentRelation ?? "" : "";
    }
}
