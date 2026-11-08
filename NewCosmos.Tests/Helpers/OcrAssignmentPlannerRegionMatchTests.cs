using NewCosmos.Helpers;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 户口本 OCR 地址文本 → 户籍级联逐级解析的匹配规则测试。
/// 规则：候选里取"出现位置最靠前、同位置最长"的名称；村级做后缀归一化
/// （区划库存「万寿村委会」，户口本常写「万寿村」，必须能对上）。
/// </summary>
public class OcrAssignmentPlannerRegionMatchTests
{
    [Fact]
    public void MatchRegionOption_MatchesCityAfterProvincePrefix()
    {
        // 户口本常带省名前缀，城市候选不含省 → 仍能定位到市
        var hit = OcrAssignmentPlanner.MatchRegionOption(
            new[] { "牡丹江市", "哈尔滨市" }, "黑龙江省牡丹江市林口县柳树镇柳树村");
        Assert.NotNull(hit);
        Assert.Equal("牡丹江市", hit!.Value.Name);
        Assert.Equal(4, hit.Value.Index); // "黑龙江省"占前 4 个字
        Assert.Equal(4, hit.Value.Length); // 只占用"牡丹江市"4 字，后面的县继续解析
    }

    [Fact]
    public void MatchRegionOption_PrefersLongestAtSamePosition()
    {
        // 同一起点有「林口」和「林口县」→ 取更长的「林口县」，防止把县名截断成地名
        var hit = OcrAssignmentPlanner.MatchRegionOption(new[] { "林口", "林口县" }, "林口县柳树镇");
        Assert.NotNull(hit);
        Assert.Equal("林口县", hit!.Value.Name);
        Assert.Equal(0, hit.Value.Index);
        Assert.Equal(3, hit.Value.Length);
    }

    [Fact]
    public void MatchRegionOption_PrefersEarliestPosition()
    {
        // 靠前的候选优先（地址按书写顺序推进）
        var hit = OcrAssignmentPlanner.MatchRegionOption(new[] { "柳树镇", "林口县" }, "林口县柳树镇");
        Assert.NotNull(hit);
        Assert.Equal("林口县", hit!.Value.Name);
        Assert.Equal(0, hit.Value.Index);
    }

    [Fact]
    public void MatchRegionOption_NoMatch_ReturnsNull()
    {
        Assert.Null(OcrAssignmentPlanner.MatchRegionOption(new[] { "牡丹江市" }, "北京市朝阳区"));
    }

    [Fact]
    public void MatchRegionOption_EmptyInputs_ReturnNull()
    {
        Assert.Null(OcrAssignmentPlanner.MatchRegionOption(Array.Empty<string>(), "林口县"));
        Assert.Null(OcrAssignmentPlanner.MatchRegionOption(new[] { "林口县" }, string.Empty));
    }

    [Fact]
    public void MatchRegionOption_VillageSuffix_MapsCunToCunWeiHui()
    {
        // 核心场景：区划库「万寿村委会」，户口本写「万寿村」→ 必须命中，且只占用"万寿村"3 字
        var hit = OcrAssignmentPlanner.MatchRegionOption(
            new[] { "万寿村委会", "双河村委会" }, "万寿村101号");
        Assert.NotNull(hit);
        Assert.Equal("万寿村委会", hit!.Value.Name);
        Assert.Equal(0, hit.Value.Index);
        Assert.Equal(3, hit.Value.Length);
    }

    [Fact]
    public void MatchRegionOption_VillageSuffix_MapsCunMinWeiYuanHui()
    {
        var hit = OcrAssignmentPlanner.MatchRegionOption(
            new[] { "石青村民委员会" }, "石青村99号");
        Assert.NotNull(hit);
        Assert.Equal("石青村民委员会", hit!.Value.Name);
        Assert.Equal(3, hit.Value.Length);
    }

    [Fact]
    public void MatchRegionOption_VillageSuffix_JuWeiHui()
    {
        var hit = OcrAssignmentPlanner.MatchRegionOption(
            new[] { "柳树镇镇直第一居民委员会" }, "柳树镇镇直第一居委会3号");
        Assert.NotNull(hit);
        Assert.Equal("柳树镇镇直第一居民委员会", hit!.Value.Name);
        Assert.True(hit.Value.Length > 0);
    }

    [Fact]
    public void MatchRegionOption_ExactNameBeatsVariant()
    {
        // 全称精确命中优先于变体：文本里同时含全称时按全称算
        var hit = OcrAssignmentPlanner.MatchRegionOption(
            new[] { "万寿村委会" }, "万寿村委会101号");
        Assert.NotNull(hit);
        Assert.Equal("万寿村委会", hit!.Value.Name);
        Assert.Equal(5, hit.Value.Length);
    }

    [Fact]
    public void MatchRegionOption_NonVillageNoVariantStripping()
    {
        // 市/县不做后缀剥离：库内"林口县"不会退化成"林口"去乱匹配
        var hit = OcrAssignmentPlanner.MatchRegionOption(new[] { "林口县" }, "林口镇某村");
        Assert.Null(hit);
    }

    [Fact]
    public void MatchRegionOption_ChainedLevels_ParsesFullAddress()
    {
        // 端到端：市→县→镇→村 逐级截断，最终剩余为门牌号
        var address = "黑龙江省牡丹江市林口县柳树镇万寿村99号";
        var rest = address;

        var city = OcrAssignmentPlanner.MatchRegionOption(new[] { "牡丹江市" }, rest);
        Assert.NotNull(city);
        rest = rest[(city!.Value.Index + city.Value.Length)..];

        var county = OcrAssignmentPlanner.MatchRegionOption(new[] { "林口县" }, rest);
        Assert.NotNull(county);
        rest = rest[(county!.Value.Index + county.Value.Length)..];

        var town = OcrAssignmentPlanner.MatchRegionOption(new[] { "柳树镇" }, rest);
        Assert.NotNull(town);
        rest = rest[(town!.Value.Index + town.Value.Length)..];

        var village = OcrAssignmentPlanner.MatchRegionOption(new[] { "万寿村委会" }, rest);
        Assert.NotNull(village);
        Assert.Equal("万寿村委会", village!.Value.Name);
        rest = rest[(village.Value.Index + village.Value.Length)..];

        Assert.Equal("99号", rest);
    }

    [Fact]
    public void MatchRegionOption_ChainedLevels_AddressWithoutCityPrefix()
    {
        // 户口本常见写法：省→县，没有地级市；从县开始逐级仍应解析出镇/村
        var rest = "黑龙江省林口县柳树镇万寿村101号";

        var county = OcrAssignmentPlanner.MatchRegionOption(new[] { "林口县" }, rest);
        Assert.NotNull(county);
        rest = rest[(county!.Value.Index + county.Value.Length)..];

        var town = OcrAssignmentPlanner.MatchRegionOption(new[] { "柳树镇" }, rest);
        Assert.NotNull(town);
        rest = rest[(town!.Value.Index + town.Value.Length)..];

        var village = OcrAssignmentPlanner.MatchRegionOption(new[] { "万寿村委会" }, rest);
        Assert.NotNull(village);
        rest = rest[(village!.Value.Index + village.Value.Length)..];

        Assert.Equal("101号", rest);
    }

    [Fact]
    public void MatchRegionOption_OutOfScopeCity_ReturnsNull()
    {
        // 候选已按管辖范围过滤：范围外城市不在候选里 → 不匹配（不猜测）
        Assert.Null(OcrAssignmentPlanner.MatchRegionOption(new[] { "牡丹江市" }, "黑龙江省哈尔滨市南岗区"));
    }

    #region 村级严格匹配（旧村名/自然屯：歧义不猜）

    [Fact]
    public void MatchVillageOption_NaturalVillageSuffix_MatchesByStem()
    {
        // 库内「柞木村委会」，户口本写「柞木屯245号」：词干「柞木」唯一 → 命中
        var hit = OcrAssignmentPlanner.MatchVillageOption(new[] { "柞木村委会" }, "柞木屯245号");
        Assert.NotNull(hit);
        Assert.Equal("柞木村委会", hit!.Value.Name);
        Assert.Equal(0, hit.Value.Index);
        Assert.Equal(2, hit.Value.Length);   // 只占用「柞木」，后面的「屯245号」留给详细地址
    }

    [Fact]
    public void MatchVillageOption_UnknownOldName_ReturnsNull()
    {
        // 自然屯名与行政村名无字面关系（工农屯 ≠ 柞木村）→ 不猜，交上层整体放弃
        Assert.Null(OcrAssignmentPlanner.MatchVillageOption(new[] { "柞木村委会", "万寿村委会" }, "工农屯245号"));
    }

    [Fact]
    public void MatchVillageOption_AmbiguousStem_ReturnsNull()
    {
        // 「石青村委会」「石青村」两个候选共用词干「石青」：唯一性不足 → 不猜
        Assert.Null(OcrAssignmentPlanner.MatchVillageOption(
            new[] { "石青村委会", "石青村" }, "石青5组"));
    }

    [Fact]
    public void MatchVillageOption_ExactBeatsAmbiguity()
    {
        // 全称精确命中优先，即使存在共用词干的候选也不算歧义
        var hit = OcrAssignmentPlanner.MatchVillageOption(
            new[] { "石青村委会", "石青沟村委会" }, "石青村委会5组");
        Assert.NotNull(hit);
        Assert.Equal("石青村委会", hit!.Value.Name);
        Assert.Equal(5, hit.Value.Length);
    }

    [Fact]
    public void MatchVillageOption_CunToCunWeiHui_StillMatches()
    {
        // 现有后缀归一能力不回归：库「万寿村委会」↔ 户口本「万寿村」
        var hit = OcrAssignmentPlanner.MatchVillageOption(new[] { "万寿村委会" }, "万寿村101号");
        Assert.NotNull(hit);
        Assert.Equal("万寿村委会", hit!.Value.Name);
        Assert.Equal(3, hit.Value.Length);
    }

    [Fact]
    public void MatchVillageOption_FullCommitteeName_ConsumesWholeName()
    {
        // 户口本写全称「柞木村民委员会99号」：应吃掉整段名称，剩余「99号」才是门牌
        var hit = OcrAssignmentPlanner.MatchVillageOption(new[] { "柞木村委会" }, "柞木村民委员会99号");
        Assert.NotNull(hit);
        Assert.Equal("柞木村委会", hit!.Value.Name);
        Assert.Equal(7, hit.Value.Length);
    }

    #endregion
}
