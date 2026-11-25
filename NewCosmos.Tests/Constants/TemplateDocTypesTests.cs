using NewCosmos.Constants;
using Xunit;

namespace NewCosmos.Tests.Constants;

/// <summary>
/// 模板文档类型词表（方案B 精准定位，2026-11-18）：
/// ① 类型判定（IsCover/IsSupporterPledge 按 doc_type 精确匹配，取代原按名子串）；
/// ② 词表自洽（All 无重复/无空值，代码引用的常量都在 All 内 —— 保证上传下拉选得全）。
/// </summary>
public class TemplateDocTypesTests
{
    [Theory]
    [InlineData("封面", true)]
    [InlineData("目录", false)]
    [InlineData("变更告知书", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsCover_按文档类型精确判定(string? docType, bool expected)
        => Assert.Equal(expected, TemplateDocTypes.IsCover(docType));

    [Theory]
    [InlineData("赡养费承诺书", true)]
    [InlineData("授权承诺书", false)]      // 类型不同即不命中（原按名子串会误伤"授权承诺书"吗？不会——但词表精确匹配杜绝一切子串巧合）
    [InlineData("承诺书", false)]
    [InlineData(null, false)]
    public void IsSupporterPledge_按文档类型精确判定(string? docType, bool expected)
        => Assert.Equal(expected, TemplateDocTypes.IsSupporterPledge(docType));

    [Fact]
    public void All_无重复值()
    {
        var dup = TemplateDocTypes.All.GroupBy(x => x, StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dup.Count == 0, $"词表存在重复值: {string.Join(",", dup)}");
    }

    [Fact]
    public void All_无空值()
        => Assert.All(TemplateDocTypes.All, v => Assert.False(string.IsNullOrWhiteSpace(v)));

    [Fact]
    public void All_覆盖代码引用的全部常量()
    {
        var referenced = new[]
        {
            TemplateDocTypes.Cover, TemplateDocTypes.Directory, TemplateDocTypes.Survey,
            TemplateDocTypes.NoLandProof, TemplateDocTypes.VillageLandNote,
            TemplateDocTypes.SpecialApprovalDeclaration, TemplateDocTypes.SpecialApprovalApplication,
            TemplateDocTypes.SpecialApprovalRecord, TemplateDocTypes.NearRelative,
            TemplateDocTypes.ChangeNotice, TemplateDocTypes.MemberChangeTable,
            TemplateDocTypes.GraceApproval, TemplateDocTypes.PeriodicReviewApproval,
            TemplateDocTypes.DestituteCancelFiling, TemplateDocTypes.DeathStatement,
            TemplateDocTypes.SupporterPledge, TemplateDocTypes.TempReliefAuditApproval,
            TemplateDocTypes.MeetingMinutes
        };
        foreach (var docType in referenced)
            Assert.Contains(docType, TemplateDocTypes.All);
    }

    [Fact]
    public void SpecialApprovalForms_恰为三表且互不相同()
    {
        Assert.Equal(3, TemplateDocTypes.SpecialApprovalForms.Length);
        Assert.Equal(3, TemplateDocTypes.SpecialApprovalForms.Distinct().Count());
        Assert.Contains(TemplateDocTypes.SpecialApprovalDeclaration, TemplateDocTypes.SpecialApprovalForms);
        Assert.Contains(TemplateDocTypes.SpecialApprovalApplication, TemplateDocTypes.SpecialApprovalForms);
        Assert.Contains(TemplateDocTypes.SpecialApprovalRecord, TemplateDocTypes.SpecialApprovalForms);
    }
}
