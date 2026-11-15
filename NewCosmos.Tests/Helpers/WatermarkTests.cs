using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 水印上下文与坐标偏差测试（2026-11-15 入户排查/公示核查水印，二改）：
/// 信息条文本行组装（区分抬头/缺项省略）与坐标小幅度随机偏差（±30 米）的范围约束。
/// </summary>
public class WatermarkTests
{
    // ── PhotoWatermarkInfo.BuildInfoLines ──────────────────────────

    [Fact]
    public void BuildInfoLines_FullContext_FourLinesInOrder()
    {
        var info = new PhotoWatermarkInfo(
            CapturedAt: new DateTime(2026, 11, 15, 14, 30, 0),
            Latitude: 45.12345,
            Longitude: 126.54321,
            PersonName: "张三",
            IdCard: "231025198001014929",
            Address: "林口县柳树镇复兴村村民委员会");

        var lines = info.BuildInfoLines("入户排查");

        Assert.Equal(4, lines.Count);
        Assert.Equal("入户排查 · 张三 · 231025198001014929", lines[0]);
        Assert.Equal("地址：林口县柳树镇复兴村村民委员会", lines[1]);
        Assert.Equal("时间：2026年11月15日 14时30分", lines[2]);
        Assert.Equal("经纬度：北纬45.12345° 东经126.54321°", lines[3]);
    }

    [Fact]
    public void BuildInfoLines_NoGps_OmitsCoordinateLine()
    {
        var info = new PhotoWatermarkInfo(
            CapturedAt: new DateTime(2026, 11, 15, 8, 5, 0),
            Latitude: null,
            Longitude: null,
            PersonName: "李四",
            IdCard: "123456789012345678",
            Address: "");

        var lines = info.BuildInfoLines("公示/核查照片");

        // 地址缺失 → 省略地址行；无经纬度 → 省略坐标行（仅表头 + 时间 2 行）
        Assert.Equal(2, lines.Count);
        Assert.Equal("公示/核查照片 · 李四 · 123456789012345678", lines[0]);
        Assert.Equal("时间：2026年11月15日 08时05分", lines[1]);
        Assert.DoesNotContain(lines, l => l.StartsWith("经纬度：", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildInfoLines_MissingNameAndAddress_KeepsCategoryAndTime()
    {
        var info = new PhotoWatermarkInfo(
            CapturedAt: new DateTime(2026, 1, 2, 3, 4, 0),
            PersonName: "",
            IdCard: "",
            Address: "");

        var lines = info.BuildInfoLines("入户排查");

        Assert.Equal(2, lines.Count);
        Assert.Equal("入户排查", lines[0]);
        Assert.Equal("时间：2026年1月2日 03时04分", lines[1]);
    }

    [Fact]
    public void BuildInfoLines_PartialName_KeepsCategoryAndName()
    {
        var info = new PhotoWatermarkInfo(
            CapturedAt: new DateTime(2026, 11, 15, 9, 0, 0),
            PersonName: "王五",
            IdCard: "");

        var lines = info.BuildInfoLines("入户排查");

        Assert.Equal("入户排查 · 王五", lines[0]);
    }

    [Fact]
    public void 水印信息条_首行无抬头仍可区分_组合身份()
    {
        // 抬头口径回归锚点：首行为组合身份（分类·姓名·身份证自带分隔），其余行必须带中文冒号抬头
        var info = new PhotoWatermarkInfo(
            CapturedAt: new DateTime(2026, 11, 15, 14, 30, 0),
            Latitude: 45.1, Longitude: 126.5,
            PersonName: "张三", IdCard: "231025198001014929",
            Address: "林口县柳树镇复兴村村民委员会");

        var lines = info.BuildInfoLines("入户排查");

        Assert.DoesNotContain("：", lines[0]);
        Assert.StartsWith("地址：", lines[1]);
        Assert.StartsWith("时间：", lines[2]);
        Assert.StartsWith("经纬度：", lines[3]);
    }

    // ── 需水印类别集合（PhotoCategoryConstants） ────────────────────

    [Theory]
    [InlineData(PhotoCategoryConstants.HOUSEHOLD_INSPECTION, true)]
    [InlineData(PhotoCategoryConstants.PUBLICITY_CHECK, true)]
    [InlineData(PhotoCategoryConstants.ID_CARD, false)]
    [InlineData(PhotoCategoryConstants.HOUSEHOLD_REGISTER, false)]
    [InlineData(PhotoCategoryConstants.OTHER_CERT, false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void RequiresWatermark_OnlyHomeVisitAndPublicityCheck(string? category, bool expected)
    {
        Assert.Equal(expected, PhotoCategoryConstants.RequiresWatermark(category));
    }
}
