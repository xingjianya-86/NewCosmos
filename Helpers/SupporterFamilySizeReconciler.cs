namespace NewCosmos.Helpers;

/// <summary>
/// 赡养人「家庭人口」双编辑口调和（纯逻辑，供 NewCosmos.Tests 单测）。
///
/// 背景：Step2 成员卡的 <c>FamilyMember.FamilySize</c> 与 Step3 赡养明细的
/// <c>Supporter.SupporterFamilySize</c> 落在 **同一列** <c>nc_biz_family_members.family_size</c>
/// （Supporter.Id == FamilyMember.Id，同一行）。保存顺序是 SaveFamilyMembers 先写、
/// SaveSupporters 后写 → 后者覆盖前者，Step2 的输入会被改回。
///
/// 「家庭人口」语义 = **赡养人自己家庭的人口，与本户认定无关**（用户 2026-11-11 口径确认），
/// 所以两侧是同一个值的两个编辑口，必须保持一致。
///
/// 判定唯一依据是**加载基线**（<c>_loadedFamilySizeByIdCard</c>）：只比两侧现值无法分辨
/// 是 Step2 改过还是 Step3 改过——两种情况都表现为"两边不等"。
///
/// 规则：
/// <list type="number">
/// <item>两侧已同值 → 不动（覆盖本会话新增：BuildSupporterFromMember 已同值播种）。</item>
/// <item>Step2 侧为空（null）→ 以 Step3 为准（含"清空"与"基线 NULL vs 兜底 1"两种归一化差异）。</item>
/// <item>Step2 偏离基线（含无基线=本会话新增、以及**两侧都偏离**的冲突）→ Step2 为准。</item>
/// <item>其余（仅 Step3 偏离基线）→ Step3 为准。</item>
/// </list>
/// 调用方保证：先跑 <c>MergeSupportMembersIntoSupportersAsync()</c> 让两侧配对齐全，再调本方法。
/// </summary>
public static class SupporterFamilySizeReconciler
{
    /// <summary>调和结果：两侧应写入的值 + 是否发生改动。</summary>
    public readonly record struct Result(int? MemberValue, int SupporterValue, bool Changed);

    /// <param name="hasBaseline">该身份证是否有加载基线（false = 本会话新增）。</param>
    /// <param name="baseline">加载时的 family_size（Step2 侧原值，可为 null）。</param>
    /// <param name="memberValue">Step2 成员卡当前值（可为 null）。</param>
    /// <param name="supporterValue">Step3 赡养明细当前值（实体为非空 int，0/负数按 1 兜底）。</param>
    public static Result Resolve(bool hasBaseline, int? baseline, int? memberValue, int supporterValue)
    {
        // 支持侧 0/负数按 1 兜底，与 ApplicationFormViewModel.Initialization 加载口径一致
        var normalizedSupporter = supporterValue > 0 ? supporterValue : 1;

        // 规则1：两侧同值 → 无需动
        if (memberValue == normalizedSupporter)
            return new Result(memberValue, normalizedSupporter, false);

        // 规则2：Step2 为空 → 以 Step3 为准（顺带把 null 归一成可落库的值）
        if (memberValue is null)
            return new Result(normalizedSupporter, normalizedSupporter, true);

        var memberTouched = !hasBaseline || memberValue != baseline;

        // 规则3：Step2 偏离基线（含新增与"两侧都改"冲突）→ Step2 为准
        if (memberTouched)
            return new Result(memberValue, memberValue.Value, true);

        // 规则4：仅 Step3 偏离基线 → Step3 为准
        return new Result(normalizedSupporter, normalizedSupporter, true);
    }
}
