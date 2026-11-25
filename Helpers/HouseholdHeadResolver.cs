using NewCosmos.Constants;
using NewCosmos.Models.Entities;

namespace NewCosmos.Helpers;

/// <summary>户主解析结果（<see cref="IsVirtual"/> = 成员表无户主行，按主表申请人合成的虚拟户主 Id=0）。</summary>
public readonly record struct HouseholdHeadResolution(FamilyMember? Head, bool IsVirtual);

/// <summary>
/// 户主解析单点（2026-11-19）：户主权威在主表 <c>nc_biz_applications</c>，成员表只是镜像。
/// 匹配优先级：显式请求的成员ID → 身份证与主表申请人一致 → is_applicant → member_category='HouseholdHead' → 姓名一致。
/// 成员表无户主行时用主表申请人字段合成虚拟户主（Id=0），供户主死亡等流程使用；
/// 主表无姓名或身份证时返回 null（无法确定户主身份）。
/// 纯逻辑，无 IO，可由单测覆盖（NewCosmos.Tests\Helpers\HouseholdHeadResolverTests）。
/// </summary>
public static class HouseholdHeadResolver
{
    public static HouseholdHeadResolution Resolve(
        NewCosmos.Models.Entities.Application application,
        IReadOnlyList<FamilyMember> members,
        long requestedMemberId = 0)
    {
        ArgumentNullException.ThrowIfNull(application);
        var allMembers = members ?? Array.Empty<FamilyMember>();

        // 显式传入的成员 ID 优先；脏 ID（不属于本申请）时继续走兜底规则，不直接失败
        if (requestedMemberId > 0)
        {
            var requested = allMembers.FirstOrDefault(m => m.Id == requestedMemberId);
            if (requested != null)
                return new HouseholdHeadResolution(requested, false);
        }

        var headIdCard = (application.ApplicantIdCard ?? string.Empty).Trim();
        var headName = (application.ApplicantName ?? string.Empty).Trim();

        // 主表身份最权威：一档多 is_applicant（历史户主变更未清标记）时不能先取 is_applicant
        var member = (!string.IsNullOrEmpty(headIdCard)
                ? allMembers.FirstOrDefault(m =>
                    string.Equals((m.IdCard ?? string.Empty).Trim(), headIdCard, StringComparison.OrdinalIgnoreCase))
                : null)
            ?? allMembers.FirstOrDefault(m => m.IsApplicant)
            ?? allMembers.FirstOrDefault(m =>
                string.Equals(m.MemberCategory, MemberCategoryConstants.HOUSEHOLD_HEAD, StringComparison.OrdinalIgnoreCase))
            ?? (!string.IsNullOrEmpty(headName)
                ? allMembers.FirstOrDefault(m =>
                    string.Equals((m.Name ?? string.Empty).Trim(), headName, StringComparison.Ordinal))
                : null);
        if (member != null)
            return new HouseholdHeadResolution(member, false);

        // 成员表无户主行：用主表申请人身份合成虚拟户主（姓名/身份证缺失则无法确定死亡人员）
        if (string.IsNullOrEmpty(headIdCard) || string.IsNullOrEmpty(headName))
            return new HouseholdHeadResolution(null, false);

        return new HouseholdHeadResolution(new FamilyMember
        {
            Id = 0,
            ApplicationId = application.Id,
            Name = headName,
            IdCard = headIdCard,
            IsApplicant = true,
            IsHouseholdHead = true,
            MemberCategory = MemberCategoryConstants.HOUSEHOLD_HEAD,
            RelationshipToHead = "本人/户主",
            Gender = string.IsNullOrWhiteSpace(application.Gender)
                ? (IdCardValidator.ExtractGender(headIdCard) ?? string.Empty)
                : application.Gender,
            BirthDate = IdCardValidator.ExtractBirthDate(headIdCard) ?? default,
            Age = IdCardValidator.ExtractAgeBasic(headIdCard),
            Ethnicity = application.Ethnicity ?? string.Empty,
            MaritalStatus = application.MaritalStatus ?? string.Empty,
            Phone = application.ApplicantPhone ?? string.Empty,
            HukouType = application.HukouType ?? string.Empty,
            HukouAddress = application.HukouAddress ?? string.Empty
        }, true);
    }
}
