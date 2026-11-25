using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using Xunit;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 户主解析单点（2026-11-19）：户主权威在主表，成员表只是镜像。
/// 覆盖：虚拟户主合成、主表身份证优先于脏 is_applicant、请求ID优先、主表身份缺失返回 null。
/// </summary>
public class HouseholdHeadResolverTests
{
    private static ApplicationEntity App(string name, string idCard) => new()
    {
        Id = 56,
        ApplicantName = name,
        ApplicantIdCard = idCard,
        Gender = "男"
    };

    private static FamilyMember Member(long id, string name, string idCard, bool isApplicant = false, string category = MemberCategoryConstants.SHARED_LIVING)
        => new() { Id = id, Name = name, IdCard = idCard, IsApplicant = isApplicant, MemberCategory = category };

    [Fact]
    public void 成员表无户主行_合成虚拟户主()
    {
        var app = App("倪德奎", "231025196005224918");
        var members = new List<FamilyMember> { Member(619, "张桂秋", "231025196701074925") };

        var result = HouseholdHeadResolver.Resolve(app, members);

        Assert.True(result.IsVirtual);
        Assert.NotNull(result.Head);
        Assert.Equal(0, result.Head!.Id);
        Assert.Equal("倪德奎", result.Head.Name);
        Assert.Equal("231025196005224918", result.Head.IdCard);
        Assert.True(result.Head.IsApplicant);
        Assert.Equal(MemberCategoryConstants.HOUSEHOLD_HEAD, result.Head.MemberCategory);
        Assert.Equal("本人/户主", result.Head.RelationshipToHead);
    }

    [Fact]
    public void 主表身份证优先于脏is_applicant_取现任户主而非已故原户主()
    {
        var app = App("佟淑华", "231025194804255228");
        var members = new List<FamilyMember>
        {
            Member(388, "吴克学", "23102519531006491X", isApplicant: true),
            Member(340, "佟淑华", "231025194804255228", isApplicant: true)
        };

        var result = HouseholdHeadResolver.Resolve(app, members);

        Assert.False(result.IsVirtual);
        Assert.Equal(340, result.Head!.Id);
        Assert.Equal("佟淑华", result.Head.Name);
    }

    [Fact]
    public void 显式请求的成员ID优先()
    {
        var app = App("佟淑华", "231025194804255228");
        var members = new List<FamilyMember>
        {
            Member(340, "佟淑华", "231025194804255228", isApplicant: true),
            Member(388, "吴克学", "23102519531006491X")
        };

        var result = HouseholdHeadResolver.Resolve(app, members, requestedMemberId: 388);

        Assert.Equal(388, result.Head!.Id);
        Assert.False(result.IsVirtual);
    }

    [Fact]
    public void 身份证缺失时按类别兜底()
    {
        var app = App("王某某", "");
        var members = new List<FamilyMember>
        {
            Member(1, "王某某", "231025196005224918", category: MemberCategoryConstants.HOUSEHOLD_HEAD)
        };

        var result = HouseholdHeadResolver.Resolve(app, members);

        Assert.False(result.IsVirtual);
        Assert.Equal(1, result.Head!.Id);
    }

    [Fact]
    public void 主表无姓名或身份证_无法确定户主_返回null()
    {
        var app = App("", "");
        var members = new List<FamilyMember> { Member(1, "某人", "") };

        var result = HouseholdHeadResolver.Resolve(app, members);

        Assert.Null(result.Head);
        Assert.False(result.IsVirtual);
    }
}
