using NewCosmos.Helpers;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 申请编号格式（SA+yyyyMMdd+4位序号）单一来源 ApplicationNoHelper 测试：
/// 取号、改期重号、申请日期锚点三方共用，格式口径改动只允许发生在该类。
/// </summary>
public class ApplicationNoHelperTests
{
    [Xunit.Fact]
    public void BuildPrefix_拼日期前缀()
        => Assert.Equal("SA20261106", ApplicationNoHelper.BuildPrefix(new DateTime(2026, 11, 6)));

    [Xunit.Fact]
    public void BuildNo_序号四位补零()
        => Assert.Equal("SA202611100004",
            ApplicationNoHelper.BuildNo(new DateTime(2026, 11, 10), 4));

    [Xunit.Fact]
    public void BuildNo_序号为零_仍补四位()
        => Assert.Equal("SA202611060000",
            ApplicationNoHelper.BuildNo(new DateTime(2026, 11, 6), 0));

    [Xunit.Fact]
    public void BuildNo_序号超9999_自然进位不截断()
        // 取号 SQL（前缀内 MAX+1）与锚点解析均按"前缀后全部数字"处理，5 位序号兼容
        => Assert.Equal("SA2026110610000",
            ApplicationNoHelper.BuildNo(new DateTime(2026, 11, 6), 10000));

    [Xunit.Fact]
    public void TryParseDate_合法编号_往返一致()
    {
        var date = new DateTime(2026, 11, 10);
        var no = ApplicationNoHelper.BuildNo(date, 4);
        Assert.Equal(date, ApplicationNoHelper.TryParseDate(no));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SB202611060001")]   // 非 SA 前缀（临时救助 LZ / 高龄 GA 同理不适用）
    [InlineData("SA20261")]           // 长度不足 10
    [InlineData("SA202613010001")]    // 月份 13 非法
    [InlineData("SA2026AB010001")]    // 日期段非数字
    public void TryParseDate_非法输入_返回null(string? no)
        => Assert.Null(ApplicationNoHelper.TryParseDate(no));

    [Xunit.Fact]
    public void ResolveApplicationDate_编号日期优先于创建时间_锚点行为不变()
    {
        // 回归锚点改造：ArchiveAnchorHelper 编号日期段解析已收口到 ApplicationNoHelper，
        // 解析结果必须与历史口径一致（编号日期 > 创建时间 > 首审 > 补全 > 更新 > 今天）
        var no = ApplicationNoHelper.BuildNo(new DateTime(2026, 11, 6), 4);
        var resolved = ArchiveAnchorHelper.ResolveApplicationDate(
            no,
            createdAt: new DateTime(2026, 11, 10, 16, 11, 25),
            firstApprovedAt: new DateTime(2026, 11, 11, 9, 7, 57),
            dataCompletedAt: null,
            updatedAt: new DateTime(2026, 11, 11, 10, 0, 0));
        Assert.Equal(new DateTime(2026, 11, 6), resolved);
    }

    [Xunit.Fact]
    public void ResolveApplicationDate_非SA编号_回退创建时间()
    {
        var resolved = ArchiveAnchorHelper.ResolveApplicationDate(
            "SR-20261110161125",
            createdAt: new DateTime(2026, 11, 10, 16, 11, 25),
            firstApprovedAt: null,
            dataCompletedAt: null,
            updatedAt: new DateTime(2026, 11, 11, 10, 0, 0));
        Assert.Equal(new DateTime(2026, 11, 10, 16, 11, 25), resolved);
    }

    [Xunit.Fact]
    public void ResolveApplicationDate_全空_回退今天()
    {
        var today = DateTime.Today;
        var resolved = ArchiveAnchorHelper.ResolveApplicationDate(
            null, default, null, null, default);
        Assert.Equal(today, resolved);
    }

    // ── 改期联动：入户日期钳进窗口 [新申请日, min(会议日, 今天)] ──

    [Xunit.Fact]
    public void ClampSurveyDate_低于下限_取下限()
        // 入户早于新申请日 → 时序倒挂，钳到申请日
        => Assert.Equal(new DateTime(2026, 11, 8),
            BatchDeferralHelper.ClampSurveyDate(new DateTime(2026, 11, 6), new DateTime(2026, 11, 8), new DateTime(2026, 11, 9)));

    [Xunit.Fact]
    public void ClampSurveyDate_高于上限_取上限()
        // 入户晚于 min(会议日, 今天) → 钳到上限（保 Step4 公示/不倒填未来校验通过）
        => Assert.Equal(new DateTime(2026, 11, 9),
            BatchDeferralHelper.ClampSurveyDate(new DateTime(2026, 11, 11), new DateTime(2026, 11, 6), new DateTime(2026, 11, 9)));

    [Xunit.Fact]
    public void ClampSurveyDate_窗口内_原样保留()
        => Assert.Equal(new DateTime(2026, 11, 7),
            BatchDeferralHelper.ClampSurveyDate(new DateTime(2026, 11, 7), new DateTime(2026, 11, 6), new DateTime(2026, 11, 9)));

    [Xunit.Fact]
    public void ClampSurveyDate_窗口倒挂_防御取下限()
        => Assert.Equal(new DateTime(2026, 11, 8),
            BatchDeferralHelper.ClampSurveyDate(new DateTime(2026, 11, 7), new DateTime(2026, 11, 8), new DateTime(2026, 11, 6)));
}
