using NewCosmos.Models.Enums;
using NewCosmos.Services.Utilities;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 临时救助逐笔 5 天时间曲线（2026-11-13 口径）测试：
/// 以申请日 D 为锚——公示起 = D+1、公示止 = D+3（恒 3 天）、验收日 = D+4（= 公示止 + 1 日历日）；
/// 入户调查窗口 = [D, 公示起]。
/// 同时覆盖存量兼容（旧公示止 = 验收日 A − 1 → 新验收日仍 = A，零漂移）与调查打印值钳制。
/// </summary>
public class TempReliefCurveTests
{
    #region 默认曲线：D → D+1 / D+3 / D+4

    [Xunit.Fact]
    public void Curve_默认_公示次日开始_恒三天_验收公示止次日()
    {
        var d = new DateTime(2026, 11, 10);
        var curve = BusinessTimelineService.BuildTempReliefCurveCore(d);

        Assert.Equal(TimelineType.TempRelief, curve.Type);
        Assert.Equal(d, curve.CycleStartDate);
        Assert.Equal(new DateTime(2026, 11, 11), curve.PublicityStartDate);
        Assert.Equal(new DateTime(2026, 11, 13), curve.PublicityEndDate);
        Assert.Equal(new DateTime(2026, 11, 14), curve.AuditDate);
        Assert.Equal(new DateTime(2026, 11, 14), curve.CycleEndDate);
        Assert.Equal(d, curve.InvestigationStartDate);
        Assert.Equal(curve.PublicityStartDate, curve.InvestigationDeadline);
    }

    [Xunit.Fact]
    public void Curve_公示期恒为三天()
    {
        var curve = BusinessTimelineService.BuildTempReliefCurveCore(new DateTime(2026, 11, 1));

        Assert.Equal(2, (curve.PublicityEndDate - curve.PublicityStartDate).Days);
        Assert.Equal(1, (curve.AuditDate - curve.PublicityEndDate).Days);
        Assert.Equal(4, (curve.AuditDate - curve.CycleStartDate).Days);
    }

    [Xunit.Fact]
    public void Curve_跨月_公示与验收按日历日接续()
    {
        // 11-29 申请 → 公示 11-30~12-02、验收 12-03（批次标签仍按申请日归 12 月）
        var curve = BusinessTimelineService.BuildTempReliefCurveCore(new DateTime(2026, 11, 29));

        Assert.Equal(new DateTime(2026, 11, 30), curve.PublicityStartDate);
        Assert.Equal(new DateTime(2026, 12, 2), curve.PublicityEndDate);
        Assert.Equal(new DateTime(2026, 12, 3), curve.AuditDate);
    }

    #endregion

    #region 存量兼容：库中公示起回放

    [Xunit.Fact]
    public void Curve_传入库中公示起_公示止与验收随之推导()
    {
        // 存量档：库中公示起 10-08（旧口径 A−3 附近），不回算，按库中值回放
        var curve = BusinessTimelineService.BuildTempReliefCurveCore(
            new DateTime(2026, 10, 1), new DateTime(2026, 10, 8));

        Assert.Equal(new DateTime(2026, 10, 8), curve.PublicityStartDate);
        Assert.Equal(new DateTime(2026, 10, 10), curve.PublicityEndDate);
        Assert.Equal(new DateTime(2026, 10, 11), curve.AuditDate);
    }

    [Xunit.Fact]
    public void Curve_旧口径公示止等于验收日前一天_新验收日零漂移()
    {
        // 旧口径：公示起 = A−3、公示止 = A−1；新验收日 = 公示止 + 1 = A，与旧算法完全一致
        var a = new DateTime(2026, 11, 13);
        var oldStart = a.AddDays(-3);
        var curve = BusinessTimelineService.BuildTempReliefCurveCore(oldStart, oldStart);

        Assert.Equal(a.AddDays(-1), curve.PublicityEndDate);
        Assert.Equal(a, curve.AuditDate);
    }

    #endregion

    #region 入户调查打印值钳制

    [Xunit.Fact]
    public void Clamp_早于窗口起日_取起日()
    {
        var windowStart = new DateTime(2026, 11, 11);
        var deadline = new DateTime(2026, 11, 12);
        var result = BusinessTimelineService.ClampInvestigationPrintDate(
            new DateTime(2026, 11, 9), windowStart, deadline);

        Assert.Equal(windowStart, result);
    }

    [Xunit.Fact]
    public void Clamp_晚于截止_取截止()
    {
        var windowStart = new DateTime(2026, 11, 11);
        var deadline = new DateTime(2026, 11, 12);
        var result = BusinessTimelineService.ClampInvestigationPrintDate(
            new DateTime(2026, 12, 1), windowStart, deadline);

        Assert.Equal(deadline, result);
    }

    [Xunit.Fact]
    public void Clamp_落在窗口内_取当日()
    {
        var windowStart = new DateTime(2026, 11, 10);
        var deadline = new DateTime(2026, 11, 11);
        var today = new DateTime(2026, 11, 11);
        var result = BusinessTimelineService.ClampInvestigationPrintDate(today, windowStart, deadline);

        Assert.Equal(today, result);
    }

    #endregion
}
