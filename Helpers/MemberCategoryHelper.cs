using NewCosmos.Constants;

namespace NewCosmos.Helpers;

/// <summary>
/// 家庭成员分类（member_category）归一化辅助类。
/// 历史/导入数据存在三种取值形态：代码值（SharedLiving/Support/HouseholdHead）、
/// 中文显示值（共同生活成员/赡养抚养扶养/户主）、空值。
/// 代码统一按代码值比较，加载/保存前须经本类归一化，否则分类不一致的成员会在
/// 界面被静默过滤、分类判定被遗漏（曾导致成员重复录入与整单保存失败）。
/// </summary>
public static class MemberCategoryHelper
{
    /// <summary>
    /// 归一化 member_category 为代码值。未知代码值原样保留。
    /// </summary>
    /// <param name="category">数据库原始值</param>
    /// <param name="isApplicant">是否申请人（is_applicant=true），用于把户主判定为 HouseholdHead</param>
    /// <param name="relationshipToHead">与户主关系</param>
    public static string Normalize(string? category, bool isApplicant = false, string? relationshipToHead = null)
    {
        var isHead = isApplicant || IsHeadRelationship(relationshipToHead);

        if (string.IsNullOrWhiteSpace(category))
            return isHead ? MemberCategoryConstants.HOUSEHOLD_HEAD : MemberCategoryConstants.SHARED_LIVING;

        return category switch
        {
            MemberCategoryConstants.SUPPORT_DISPLAY => MemberCategoryConstants.SUPPORT,
            "户主" or "本人" or "本人/户主" or "Head" or "Self" => MemberCategoryConstants.HOUSEHOLD_HEAD,
            MemberCategoryConstants.SHARED_LIVING_DISPLAY =>
                isHead ? MemberCategoryConstants.HOUSEHOLD_HEAD : MemberCategoryConstants.SHARED_LIVING,
            _ => category // 已是代码值或未知值，原样保留
        };
    }

    /// <summary>
    /// 是否为户主关系
    /// </summary>
    private static bool IsHeadRelationship(string? relationshipToHead)
    {
        if (string.IsNullOrWhiteSpace(relationshipToHead))
            return false;

        return relationshipToHead is "户主" or "本人" or "本人/户主" or "Head" or "Self"
            || string.Equals(relationshipToHead, DictionaryConstants.FamilyRelationship.HEAD, StringComparison.OrdinalIgnoreCase);
    }
}
