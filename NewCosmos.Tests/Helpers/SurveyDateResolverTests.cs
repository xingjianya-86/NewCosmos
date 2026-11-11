using NewCosmos.Helpers;
using NewCosmos.Models.Enums;
using NewCosmos.Services.Utilities;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 入户调查日期口径（SurveyDateResolver）测试——2026-11-11 从 ArchiveProductionViewModel /
/// TemplateFieldBuilder 两份同构拷贝抽成单点后的回归护栏，规则见
/// docs\20261016_申请日期锚点改按调查日与文书日期口径.md §4。
/// </summary>
public class SurveyDateResolverTests
{
    /// <summary>B 线时间轴：会议日（= 公示前一工作日）给定值。</summary>
    private static TimelineResult Tl(DateTime meetingDate) =>
        new() { Year = 2026, Month = 11, Type = TimelineType.BusinessProcess, MeetingDate = meetingDate };

    [Fact]
    public void Resolve_有调查日且在窗口内_取调查日()
    {
        // 申请 11-01、会议 11-09、调查 11-05 → 11-05
        var r = SurveyDateResolver.Resolve(Tl(new DateTime(2026, 11, 9)), new DateTime(2026, 11, 5), new DateTime(2026, 11, 1));
        Assert.Equal(new DateTime(2026, 11, 5), r);
    }

    [Fact]
    public void Resolve_无调查记录_取申请日当天()
    {
        var r = SurveyDateResolver.Resolve(Tl(new DateTime(2026, 11, 9)), null, new DateTime(2026, 11, 3));
        Assert.Equal(new DateTime(2026, 11, 3), r);
    }

    [Fact]
    public void Resolve_调查日晚于会议日_钳到会议日_不落公示期()
    {
        // 上限铁律：入户不得晚于会议日
        var r = SurveyDateResolver.Resolve(Tl(new DateTime(2026, 11, 9)), new DateTime(2026, 11, 20), new DateTime(2026, 11, 1));
        Assert.Equal(new DateTime(2026, 11, 9), r);
    }

    [Fact]
    public void Resolve_调查日早于申请日_钳到申请日()
    {
        // 正常窗口下限：调查不早于建档
        var r = SurveyDateResolver.Resolve(Tl(new DateTime(2026, 11, 9)), new DateTime(2026, 10, 28), new DateTime(2026, 11, 1));
        Assert.Equal(new DateTime(2026, 11, 1), r);
    }

    [Fact]
    public void Resolve_倒挂窗口_上限优先放弃下限()
    {
        // 申请 10-12 > 会议 10-08、调查 10-08 → 打印 10-08（旧实现顶到 10-12 恰落公示期）
        var r = SurveyDateResolver.Resolve(Tl(new DateTime(2026, 10, 8)), new DateTime(2026, 10, 8), new DateTime(2026, 10, 12));
        Assert.Equal(new DateTime(2026, 10, 8), r);
    }

    [Fact]
    public void Resolve_倒挂窗口且调查晚于会议_仍钳会议日()
    {
        var r = SurveyDateResolver.Resolve(Tl(new DateTime(2026, 10, 8)), new DateTime(2026, 10, 15), new DateTime(2026, 10, 12));
        Assert.Equal(new DateTime(2026, 10, 8), r);
    }

    [Fact]
    public void Resolve_倒挂窗口无调查记录_取会议日而非申请日()
    {
        // 保证不落公示期：无调查记录时也不顶到申请日
        var r = SurveyDateResolver.Resolve(Tl(new DateTime(2026, 10, 8)), null, new DateTime(2026, 10, 12));
        Assert.Equal(new DateTime(2026, 10, 8), r);
    }

    [Fact]
    public void Resolve_调查日为default值_按无调查记录处理()
    {
        var r = SurveyDateResolver.Resolve(Tl(new DateTime(2026, 11, 9)), default, new DateTime(2026, 11, 3));
        Assert.Equal(new DateTime(2026, 11, 3), r);
    }
}
