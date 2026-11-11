using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Enums;
using NewCosmos.Services.Utilities;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// B 线批次顺延判定（BatchDeferralHelper）测试：
/// 锚点 = 入户调查日期优先（ArchiveAnchorHelper 周期差护栏），顺延 = 比较两个权威时间轴结果。
/// 典型场景取自 齐文斌档：建档/入户 2026-11-10 晚于 11 月会议日（7 号周六顺延到 11-09）→ 12 月批次。
/// </summary>
public class BatchDeferralHelperTests
{
    private static TimelineResult Cycle(int year, int month) =>
        new() { Year = year, Month = month, Type = TimelineType.BusinessProcess };

    #region 锚点：调查日期优先 + 周期差护栏

    [Xunit.Fact]
    public void ResolveAnchorDate_无调查日_返回受理日()
    {
        var appDate = new DateTime(2026, 11, 10);
        var anchor = BatchDeferralHelper.ResolveAnchorDate(appDate, null, (2026, 11), (2026, 11));
        Assert.Equal(appDate, anchor);
    }

    [Xunit.Fact]
    public void ResolveAnchorDate_调查日与受理日同周期_取调查日()
    {
        var anchor = BatchDeferralHelper.ResolveAnchorDate(
            new DateTime(2026, 11, 4), new DateTime(2026, 11, 10), (2026, 11), (2026, 11));
        Assert.Equal(new DateTime(2026, 11, 10), anchor);
    }

    [Xunit.Fact]
    public void ResolveAnchorDate_调查日晚一个周期_仍取调查日()
    {
        // 建档 8-24、入户 9-21 跨过会议日 → 周期差 +1，业务顺延下一周期（口径见 ArchiveAnchorHelper）
        var anchor = BatchDeferralHelper.ResolveAnchorDate(
            new DateTime(2026, 8, 24), new DateTime(2026, 9, 21), (2026, 8), (2026, 9));
        Assert.Equal(new DateTime(2026, 9, 21), anchor);
    }

    [Xunit.Fact]
    public void ResolveAnchorDate_调查日超前一个周期_回退受理日()
    {
        var appDate = new DateTime(2026, 9, 21);
        var anchor = BatchDeferralHelper.ResolveAnchorDate(
            appDate, new DateTime(2026, 8, 24), (2026, 9), (2026, 8));
        Assert.Equal(appDate, anchor);
    }

    [Xunit.Fact]
    public void ResolveAnchorDate_调查日晚两个周期_回退受理日()
    {
        // 周期差 ≥2（历史错位数据）→ 护栏回退受理日，防止文书打出远年公示/卷号
        var appDate = new DateTime(2026, 11, 10);
        var anchor = BatchDeferralHelper.ResolveAnchorDate(
            appDate, new DateTime(2027, 1, 5), (2026, 11), (2027, 1));
        Assert.Equal(appDate, anchor);
    }

    [Xunit.Fact]
    public void ResolveAnchorDate_调查日为default_回退受理日()
    {
        var appDate = new DateTime(2026, 11, 10);
        var anchor = BatchDeferralHelper.ResolveAnchorDate(appDate, default, (2026, 11), (2026, 11));
        Assert.Equal(appDate, anchor);
    }

    #endregion

    #region 顺延判定：两个权威时间轴结果的年月比较

    [Xunit.Fact]
    public void IsDeferred_同批次_不顺延()
        => Assert.False(BatchDeferralHelper.IsDeferred(Cycle(2026, 11), Cycle(2026, 11)));

    [Xunit.Fact]
    public void IsDeferred_顺延到次月_判定顺延()
        => Assert.True(BatchDeferralHelper.IsDeferred(Cycle(2026, 11), Cycle(2026, 12)));

    [Xunit.Fact]
    public void IsDeferred_跨年顺延_判定顺延()
        => Assert.True(BatchDeferralHelper.IsDeferred(Cycle(2026, 12), Cycle(2027, 1)));

    #endregion

    #region Resolve：锚点接线 + 顺延结果

    [Xunit.Fact]
    public void Resolve_调查日晚于会议日_顺延次月且锚点取调查日()
    {
        // 齐文斌场景：受理=入户=2026-11-10（均在 11 月周期内），批次顺延到 12 月
        var stub = new StubTimelineService
        {
            ForDate = _ => Cycle(2026, 11),
            ForApplicationDate = _ => Cycle(2026, 12)
        };
        var surveyDate = new DateTime(2026, 11, 10);

        var info = BatchDeferralHelper.Resolve(stub, new DateTime(2026, 11, 10), surveyDate);

        Assert.True(info.IsDeferred);
        Assert.Equal(surveyDate, info.AnchorDate);
        Assert.Equal(surveyDate, stub.AnchorPassedToApplicationDate);
        Assert.Equal(2026, info.Batch.Year);
        Assert.Equal(12, info.Batch.Month);
    }

    [Xunit.Fact]
    public void Resolve_未晚于会议日批次_不顺延()
    {
        var stub = new StubTimelineService
        {
            ForDate = _ => Cycle(2026, 11),
            ForApplicationDate = _ => Cycle(2026, 11)
        };

        var info = BatchDeferralHelper.Resolve(stub, new DateTime(2026, 11, 6), new DateTime(2026, 11, 6));

        Assert.False(info.IsDeferred);
        Assert.Equal(11, info.Batch.Month);
    }

    [Xunit.Fact]
    public void Resolve_无调查日_锚点为受理日()
    {
        var appDate = new DateTime(2026, 11, 4);
        var stub = new StubTimelineService
        {
            ForDate = _ => Cycle(2026, 11),
            ForApplicationDate = _ => Cycle(2026, 11)
        };

        var info = BatchDeferralHelper.Resolve(stub, appDate, null);

        Assert.Equal(appDate, info.AnchorDate);
        Assert.Equal(appDate, stub.AnchorPassedToApplicationDate);
    }

    [Xunit.Fact]
    public void Resolve_调查日周期错位超两周期_锚点回退受理日()
    {
        var appDate = new DateTime(2026, 11, 10);
        var stub = new StubTimelineService
        {
            // 受理日属 2026-11 周期、调查日已属 2027-01 周期 → 周期差 +2，护栏回退受理日
            ForDate = d => d >= new DateTime(2027, 1, 5) ? Cycle(2027, 1) : Cycle(2026, 11),
            ForApplicationDate = _ => Cycle(2026, 12)
        };

        var info = BatchDeferralHelper.Resolve(stub, appDate, new DateTime(2027, 1, 5));

        Assert.Equal(appDate, info.AnchorDate);
        Assert.Equal(appDate, stub.AnchorPassedToApplicationDate);
    }

    #endregion

    #region 提示文案格式（Constants 单一来源，§11 禁散落魔法值）

    [Xunit.Fact]
    public void 批次提示格式_列表角标与表单详情_可正确格式化()
    {
        Assert.Equal("本批已顺延至 2026年12月",
            string.Format(BusinessCycleConstants.BatchDeferredChipFormat, 2026, 12));

        var detail = string.Format(BusinessCycleConstants.BatchDeferredDetailFormat,
            2026, 12, new DateTime(2026, 12, 7), new DateTime(2026, 12, 8), new DateTime(2026, 12, 14));
        Assert.Equal("本批已顺延至 2026年12月（会议 12-07 · 公示 12-08 至 12-14）", detail);
    }

    #endregion

    /// <summary>
    /// IBusinessTimelineService 桩：只实现 BatchDeferralHelper 实际调用的两个取窗口方法，
    /// 并记录传给 GetTimelineForApplicationDate 的锚点（顺延规则本体归 BusinessTimelineService，此处不复刻）。
    /// </summary>
    private sealed class StubTimelineService : IBusinessTimelineService
    {
        public Func<DateTime, TimelineResult> ForDate { get; set; } = _ => throw new NotSupportedException();
        public Func<DateTime, TimelineResult> ForApplicationDate { get; set; } = _ => throw new NotSupportedException();
        public DateTime? AnchorPassedToApplicationDate { get; private set; }

        public TimelineResult GetTimelineForDate(DateTime date, TimelineType type) => ForDate(date);

        public TimelineResult GetTimelineForApplicationDate(DateTime applicationDate, TimelineType type)
        {
            AnchorPassedToApplicationDate = applicationDate;
            return ForApplicationDate(applicationDate);
        }

        public Task<TimelineResult> CalculateTimelineAsync(int year, int month, TimelineType type) => throw new NotSupportedException();
        public Task<TimelineResult> GetCurrentTimelineAsync(TimelineType type) => throw new NotSupportedException();
        public Task<TimelineResult> CalculateTempReliefAsync(int year, int month) => throw new NotSupportedException();
        public Task<TimelineResult> GetCurrentTempReliefAsync() => throw new NotSupportedException();
        public DateTime GetTempReliefAcceptanceDate(DateTime? publicizeEnd) => throw new NotSupportedException();
        public Task<TimelineResult> CalculateTempReliefForApplyDateAsync(DateTime applyDate) => throw new NotSupportedException();
        public TimelineResult CalculateTempReliefForApplyDate(DateTime applyDate) => throw new NotSupportedException();
        public DateTime GetTempReliefInvestigationPrintDate(TimelineResult cLine) => throw new NotSupportedException();
        public DateTime GetPrePublicityWorkday(TimelineResult bLine) => throw new NotSupportedException();
    }
}
