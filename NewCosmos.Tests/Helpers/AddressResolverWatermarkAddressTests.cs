using NewCosmos.Helpers;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 水印地址拼装（<see cref="AddressResolver.BuildWatermarkAddress"/>）——
/// 2026-11-15 入户排查/公示核查水印：县+镇+村 → 「林口县柳树镇XXX村村民委员会」，
/// 覆盖区划代号转出的规范村名、老文本列的委员会形态、临时救助「镇+村」一体形态与全路径兜底。
/// </summary>
public class AddressResolverWatermarkAddressTests
{
    [Fact]
    public void 规范村名_补村民委员会后缀()
    {
        Assert.Equal("林口县柳树镇柞木村村民委员会",
            AddressResolver.BuildWatermarkAddress("林口县", "柳树镇", "柞木村"));
    }

    [Fact]
    public void 已带村民委员会_原样保留不重复()
    {
        Assert.Equal("林口县柳树镇柞木村村民委员会",
            AddressResolver.BuildWatermarkAddress("林口县", "柳树镇", "柞木村村民委员会"));
    }

    [Fact]
    public void 村委会形态_转成村民委员会()
    {
        Assert.Equal("林口县柳树镇土甸子村村民委员会",
            AddressResolver.BuildWatermarkAddress("林口县", "柳树镇", "土甸子村委会"));
    }

    [Fact]
    public void 社区形态_补居民委员会()
    {
        Assert.Equal("林口县柳树镇东关社区居民委员会",
            AddressResolver.BuildWatermarkAddress("林口县", "柳树镇", "东关社区"));
    }

    [Fact]
    public void 镇列已含村_跳过村列不重复()
    {
        // 临时救助 family_town 存「柳树镇柞木村」形态
        Assert.Equal("林口县柳树镇柞木村村民委员会",
            AddressResolver.BuildWatermarkAddress("林口县", "柳树镇柞木村", "柞木村"));
    }

    [Fact]
    public void 镇列已含县名_县不重复()
    {
        Assert.Equal("林口县柳树镇柞木村村民委员会",
            AddressResolver.BuildWatermarkAddress("林口县", "林口县柳树镇", "柞木村"));
    }

    [Fact]
    public void 村级已是全路径_直接使用清空上级()
    {
        // 文本列存全量形态的兜底：村列自带县镇 → 不再拼一次
        Assert.Equal("林口县柳树镇复兴村38号",
            AddressResolver.BuildWatermarkAddress("林口县", "柳树镇", "林口县柳树镇复兴村38号"));
    }

    [Fact]
    public void 本级_视为无村()
    {
        Assert.Equal("林口县柳树镇",
            AddressResolver.BuildWatermarkAddress("林口县", "柳树镇", "柳树镇本级"));
    }

    [Fact]
    public void 空值占位符_跳过对应级()
    {
        // "-" 是库中占位空值（CleanLevel 口径）：村为占位 → 只出县镇
        Assert.Equal("林口县柳树镇",
            AddressResolver.BuildWatermarkAddress("林口县", "柳树镇", "-"));
        Assert.Equal("柳树镇柞木村村民委员会",
            AddressResolver.BuildWatermarkAddress(null, "柳树镇", "柞木村"));
    }

    [Fact]
    public void 全空_返回空串()
    {
        Assert.Equal(string.Empty, AddressResolver.BuildWatermarkAddress(null, "", "-"));
    }
}
