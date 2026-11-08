using NewCosmos.Helpers;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 文本级地址拼装唯一入口（<see cref="AddressResolver.BuildAddressFromText"/>）——
/// 2026-11-02「入户调查表/定期复核表地址混乱」修复的口径锚点。
/// 样本取自生产库实测六形态（district=林口县, town=柳树镇）。
/// </summary>
public class AddressResolverTests
{
    // ── 主口径：县+镇+村+门牌，已含则跳过 ──────────────────────────

    [Fact]
    public void detail已是全地址_只清后缀_不重复叠加()
    {
        // 生产 id=105：address=「林口县柳树镇柞木村委会」，community=「柞木村村民委员会」
        // 旧裸拼 = 柳树镇 + 柞木村村民委员会 + 林口县柳树镇柞木村委会（截图那格的混乱）
        var result = AddressResolver.BuildAddressFromText(
            "林口县", "柳树镇", "柞木村村民委员会", "林口县柳树镇柞木村委会");

        Assert.Equal("林口县柳树镇柞木村", result);
    }

    [Fact]
    public void detail只有镇和村_补县()
    {
        // 生产 id=83：address=「柳树镇土甸子村」
        var result = AddressResolver.BuildAddressFromText(
            "林口县", "柳树镇", "土甸子村村民委员会", "柳树镇土甸子村");

        Assert.Equal("林口县柳树镇土甸子村", result);
    }

    [Fact]
    public void detail只有村名_补县和镇()
    {
        // 生产常见形态：address=「土甸子村委会」（库中 10 行）
        var result = AddressResolver.BuildAddressFromText(
            "林口县", "柳树镇", "土甸子村村民委员会", "土甸子村委会");

        Assert.Equal("林口县柳树镇土甸子村", result);
    }

    [Fact]
    public void detail只有门牌_三级全补()
    {
        // 生产形态：address=「工农屯245号」（不含县/镇/村字样）
        var result = AddressResolver.BuildAddressFromText(
            "林口县", "柳树镇", "柞木村村民委员会", "工农屯245号");

        Assert.Equal("林口县柳树镇柞木村工农屯245号", result);
    }

    [Fact]
    public void detail为空_不再整格变空()
    {
        // 生产 id=526：address 为空 → 旧分步路径（村名当 code）输出空串
        var result = AddressResolver.BuildAddressFromText(
            "林口县", "柳树镇", "土侭子村委会", "");

        Assert.Equal("林口县柳树镇土侭子村", result);
    }

    [Theory]
    [InlineData("-")]   // 库中 3 行存了占位横杠
    [InlineData("—")]
    [InlineData(null)]
    [InlineData("   ")]
    public void detail是占位横杠或空白_等同空值(string? detail)
    {
        var result = AddressResolver.BuildAddressFromText(
            "林口县", "柳树镇", "柞木村村民委员会", detail);

        Assert.Equal("林口县柳树镇柞木村", result);
    }

    // ── 村名后缀规范化（地址用途，区别于 StripVillageSuffix 的丢字口径）──

    [Theory]
    [InlineData("柞木村村民委员会", "柞木村")]      // 去「村民委员会」，村字已在其前
    [InlineData("土甸子村委会", "土甸子村")]        // 「村委会」→「村」，否则丢掉「村」字
    [InlineData("土甸子村村民委员会", "土甸子村")]
    [InlineData("东关居民委员会", "东关社区")]      // 「居民委员会」→「社区」
    [InlineData("柳树村", "柳树村")]                // 无后缀原样
    [InlineData("", "")]
    public void 村名后缀规范化_不丢字(string input, string expected)
    {
        // 通过 detail 侧观察规范化结果（community 参与同一函数）
        var result = AddressResolver.BuildAddressFromText("", "", "", input);
        Assert.Equal(expected, result);
    }

    // ── 缺级场景：某级为空则跳过该级 ────────────────────────────────

    [Fact]
    public void 区县为空_只拼镇村门牌()
    {
        var result = AddressResolver.BuildAddressFromText("", "柳树镇", "柞木村村民委员会", "111号");
        Assert.Equal("柳树镇柞木村111号", result);
    }

    [Fact]
    public void 村名为空_不产生空段()
    {
        var result = AddressResolver.BuildAddressFromText("林口县", "柳树镇", "", "111号");
        Assert.Equal("林口县柳树镇111号", result);
    }

    [Fact]
    public void 全部为空_返回空串不抛()
    {
        Assert.Equal("", AddressResolver.BuildAddressFromText(null, null, null, null));
        Assert.Equal("", AddressResolver.BuildAddressFromText("", "", "", ""));
    }

    [Fact]
    public void 各级带空白_自动修剪()
    {
        var result = AddressResolver.BuildAddressFromText(" 林口县 ", " 柳树镇 ", " 柞木村村民委员会 ", " 111号 ");
        Assert.Equal("林口县柳树镇柞木村111号", result);
    }

    // ── 不重复叠加：detail 已含某级时该级不补 ────────────────────────

    [Theory]
    [InlineData("林口县柳树镇柞木村", "林口县柳树镇柞木村")]                  // 全含 → 原样
    [InlineData("林口县工农屯245号", "林口县柳树镇柞木村工农屯245号")]        // 只含县 → 镇村插在县之后（不乱序）
    [InlineData("柳树镇工农屯245号", "林口县柳树镇柞木村工农屯245号")]        // 只含镇 → 县插最前、村插镇后
    public void detail已含的级不再重复_缺级按游标插入(string detail, string expected)
    {
        var result = AddressResolver.BuildAddressFromText("林口县", "柳树镇", "柞木村村民委员会", detail);
        Assert.Equal(expected, result);
    }

    // ── CleanLevel：占位横杠识别 ────────────────────────────────────

    [Theory]
    [InlineData("-", "")]
    [InlineData("—", "")]
    [InlineData("  -  ", "")]
    [InlineData(null, "")]
    [InlineData("林口县", "林口县")]
    public void CleanLevel_横杠与空视为无值(string? input, string expected)
        => Assert.Equal(expected, AddressResolver.CleanLevel(input));

    // ── 「XX镇本级」：镇直/无村社区，不是村级名称 ──────────────────
    // 临时救助 family_address 统一（2026-11-05）实测 2 行存了「柳树镇本级」，
    // 不清会拼出「林口县柳树镇柳树镇本级290号」。

    [Theory]
    [InlineData("290号", "林口县柳树镇290号")]
    [InlineData("-", "林口县柳树镇")]
    [InlineData("", "林口县柳树镇")]
    public void 村名是镇本级_按无村拼装(string detail, string expected)
    {
        var result = AddressResolver.BuildAddressFromText("林口县", "柳树镇", "柳树镇本级", detail);
        Assert.Equal(expected, result);
    }

    // ── 临时救助存量样本（nc_biz_temp_relief_applications，2026-11-05 统一）──
    // family_address 由来源台账原文（省全量 / 只到村 / 「镇直」五花八门）改为按分列拼装。

    [Theory]
    [InlineData("柳宝村村民委员会", "22号", "林口县柳树镇柳宝村22号")]        // id=22
    [InlineData("三道村村民委员会", "丰谷屯61号", "林口县柳树镇三道村丰谷屯61号")] // id=21
    [InlineData("柳西村委会", "-", "林口县柳树镇柳西村")]                     // id=23（detail 占位横杠）
    [InlineData("土甸子村村民委员会", "257号", "林口县柳树镇土甸子村257号")]   // id=24（原「林口县柳树镇镇直」）
    public void 临时救助家庭住址_统一为县镇村门牌(string village, string detail, string expected)
    {
        var result = AddressResolver.BuildAddressFromText("林口县", "柳树镇", village, detail);
        Assert.Equal(expected, result);
    }

    // ── StripLeadingRegion：身份证背面住址剥省/市前缀 ───────────────
    // 身份证住址是「省+市+县+镇+村+门牌」全量形态，原样当详细地址会把省市带进
    // family_address，与其余行的「县+镇+村+门牌」口径不一致。

    [Theory]
    [InlineData("黑龙江省牡丹江市林口县柳树镇三道村丰谷屯61号", "林口县柳树镇三道村丰谷屯61号")]
    [InlineData("牡丹江市林口县柳树镇三道村61号", "林口县柳树镇三道村61号")]   // 无省前缀 → 只剥市
    [InlineData("林口县柳树镇三道村61号", "林口县柳树镇三道村61号")]           // 已无省市 → 原样
    [InlineData("丰谷屯61号", "丰谷屯61号")]                                    // 本就是门牌 → 原样
    [InlineData("", "")]
    [InlineData(null, "")]
    public void StripLeadingRegion_只剥开头省市区级(string? input, string expected)
        => Assert.Equal(expected, AddressResolver.StripLeadingRegion(input, "黑龙江省", "牡丹江市"));

    [Fact]
    public void OCR身份证住址_剥省市后拼装_与其余行同口径()
    {
        var detail = AddressResolver.StripLeadingRegion(
            "黑龙江省牡丹江市林口县柳树镇三道村丰谷屯61号", "黑龙江省", "牡丹江市");

        Assert.Equal("林口县柳树镇三道村丰谷屯61号",
            AddressResolver.BuildAddressFromText("林口县", "柳树镇", "三道村村民委员会", detail));
    }

    // ── 家庭经济状况核对存量样本（nc_biz_asset_checks，2026-11-05 统一）──
    // 写入口 QuickAssetVerificationViewModel.FamilyAddress 与存量迁移同走此拼装。

    [Theory]
    // 镇直：区划名「柳树镇镇直第一居民委员会」自带镇前缀——不剥会每次重存再插一个「柳树镇」
    [InlineData("柳树镇镇直第一居民委员会", "205号", "林口县柳树镇镇直第一社区205号")]
    [InlineData("柳树镇镇直第一居民委员会", "林口县柳树镇镇直第一社区205号", "林口县柳树镇镇直第一社区205号")] // 迁移后回填幂等
    // 下拉村与文本村不一致（老行编码错挂柳树村）→ 文本为准，不叠加
    [InlineData("柳树村委会", "林口县柳树镇土甸子村石青屯", "林口县柳树镇土甸子村石青屯")]
    // detail 自带「村」字样（荣华村57号）→ 按已含吸收、不插下拉村（与迁移口径一致）
    [InlineData("柞木村委会", "荣华村57号", "林口县柳树镇荣华村57号")]
    [InlineData("柞木村委会", "林口县柳树镇柞木村荣华屯", "林口县柳树镇柞木村荣华屯")] // 迁移后回填幂等
    [InlineData("柳树村委会", "161号", "林口县柳树镇柳树村161号")]                    // 裸门牌行补三级
    public void 资产核查地址_统一为县镇村门牌(string village, string detail, string expected)
    {
        var result = AddressResolver.BuildAddressFromText("林口县", "柳树镇", village, detail);
        Assert.Equal(expected, result);
    }
}
