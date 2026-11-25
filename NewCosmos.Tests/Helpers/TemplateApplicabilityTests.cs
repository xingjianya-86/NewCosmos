using NewCosmos.Constants;
using NewCosmos.Helpers;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 模板适用性事实门单点（方案B，2026-11-18）：
/// 取代原 ArchiveOutputViewModel 内 4 段 Name.Contains 文件名门槛——按 doc_type + 数据事实判定。
/// </summary>
public class TemplateApplicabilityTests
{
    private static Dictionary<string, string> Field(string key, string value)
        => new() { [key] = value };

    // ── 土地二选一（适用性规则相反）──

    [Fact]
    public void 无土地证明_有地时不出()
    {
        var fields = Field(FieldKeys.FAMILY_LAND_AREA, "3.5");
        Assert.False(TemplateApplicability.IsApplicable(TemplateDocTypes.NoLandProof, fields));
    }

    [Fact]
    public void 无土地证明_无地时出()
    {
        Assert.True(TemplateApplicability.IsApplicable(TemplateDocTypes.NoLandProof, Field(FieldKeys.FAMILY_LAND_AREA, "0")));
        Assert.True(TemplateApplicability.IsApplicable(TemplateDocTypes.NoLandProof, Field(FieldKeys.FAMILY_LAND_AREA, "")));
        Assert.True(TemplateApplicability.IsApplicable(TemplateDocTypes.NoLandProof, null));
    }

    [Fact]
    public void 村级土地说明_与无土地证明规则相反()
    {
        var hasLand = Field(FieldKeys.FAMILY_LAND_AREA, "2");
        var noLand = Field(FieldKeys.FAMILY_LAND_AREA, "0");
        Assert.True(TemplateApplicability.IsApplicable(TemplateDocTypes.VillageLandNote, hasLand));
        Assert.False(TemplateApplicability.IsApplicable(TemplateDocTypes.VillageLandNote, noLand));
    }

    // ── 一事一议三表（同一事实门）──

    [Theory]
    [InlineData("一事一议申报表")]
    [InlineData("一事一议申请表")]
    [InlineData("一事一议备案表")]
    public void 一事一议三表_有申报数据才出(string docType)
    {
        var fields = Field(FieldKeys.SPECIAL_APPROVAL_MATTERS, "流浪人群，情况特殊");
        Assert.True(TemplateApplicability.IsApplicable(docType, fields));
        Assert.False(TemplateApplicability.IsApplicable(docType, Field(FieldKeys.SPECIAL_APPROVAL_MATTERS, "")));
        Assert.False(TemplateApplicability.IsApplicable(docType, null));
    }

    [Fact]
    public void 一事一议_会议记录类型不走此门()
    {
        // 原 Name.Contains("一事一议") 会命中 月报_会议记录_一事一议，须靠 !Contains("会议记录") 补丁排除；
        // 类型化后 doc_type=会议记录 天然不进一事一议门，且无数据时也恒适用（月报不经此清单）。
        Assert.True(TemplateApplicability.IsApplicable(TemplateDocTypes.MeetingMinutes, null));
    }

    // ── 近亲属两表（同一事实门）──

    [Theory]
    [InlineData("近亲属")]
    public void 近亲属两表_有备案才出(string docType)
    {
        Assert.True(TemplateApplicability.IsApplicable(docType, Field(FieldKeys.NEAR_RELATIVE_HAS_DATA, "1")));
        Assert.False(TemplateApplicability.IsApplicable(docType, Field(FieldKeys.NEAR_RELATIVE_HAS_DATA, "0")));
        Assert.False(TemplateApplicability.IsApplicable(docType, null));
    }

    // ── 兜底：未列入门槛的类型恒适用 ──

    [Theory]
    [InlineData("封面")]
    [InlineData("目录")]
    [InlineData("入户调查")]
    [InlineData("变更告知书")]
    [InlineData("月报明细")]
    [InlineData("")]
    [InlineData(null)]
    public void 未列入门槛的类型_恒适用(string? docType)
        => Assert.True(TemplateApplicability.IsApplicable(docType, null));
}
