using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 水印上下文与经纬度偏差测试（2026-11-15 入户排查/公示核查水印）：
/// 信息条文本行组装（缺项省略）与 GPS 小幅度随机偏差（±30 米）的范围约束。
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
        Assert.Equal("林口县柳树镇复兴村村民委员会", lines[1]);
        Assert.Equal("2026年11月15日 14时30分", lines[2]);
        Assert.Equal("北纬45.12345° 东经126.54321°", lines[3]);
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
        Assert.Equal("2026年11月15日 08时05分", lines[1]);
        Assert.DoesNotContain(lines, l => l.StartsWith("北纬", StringComparison.Ordinal));
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
        Assert.Equal("2026年1月2日 03时04分", lines[1]);
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

    // ── GeoLocationHelper.ApplyJitter（±GpsJitterMeters 米均匀偏差） ──

    [Fact]
    public void ApplyJitter_StayWithinConfiguredRadius()
    {
        var origin = (Lat: 45.21354, Lng: 126.54321);
        var maxMeters = PhotoWatermarkConstants.GpsJitterMeters;

        for (var i = 0; i < 2000; i++)
        {
            var (lat, lng) = GeoLocationHelper.ApplyJitter(origin.Lat, origin.Lng, maxMeters);
            var distance = DistanceMeters(origin.Lat, origin.Lng, lat, lng);
            Assert.True(distance <= maxMeters + 0.5,
                $"偏差 {distance:F1} 米超过配置半径 {maxMeters} 米");
        }
    }

    [Fact]
    public void ApplyJitter_ZeroRadius_ReturnsOriginal()
    {
        var (lat, lng) = GeoLocationHelper.ApplyJitter(45.2, 126.5, 0);
        Assert.Equal(45.2, lat, 10);
        Assert.Equal(126.5, lng, 10);
    }

    [Fact]
    public void ApplyJitter_NotAlwaysIdentity()
    {
        // 2000 次全落原点即视为偏差未生效（概率可忽略）
        var origin = (Lat: 45.21354, Lng: 126.54321);
        var moved = false;
        for (var i = 0; i < 2000 && !moved; i++)
        {
            var (lat, lng) = GeoLocationHelper.ApplyJitter(origin.Lat, origin.Lng, 30);
            moved = lat != origin.Lat || lng != origin.Lng;
        }
        Assert.True(moved);
    }

    /// <summary>平面近似距离（当地尺度下足够判定半径约束）。</summary>
    private static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
    {
        var dLat = (lat2 - lat1) * 111320.0;
        var dLng = (lng2 - lng1) * 111320.0 * Math.Cos(lat1 * Math.PI / 180.0);
        return Math.Sqrt(dLat * dLat + dLng * dLng);
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
