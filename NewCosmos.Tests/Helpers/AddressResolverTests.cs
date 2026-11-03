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
}
