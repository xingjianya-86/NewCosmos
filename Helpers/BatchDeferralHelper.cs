using NewCosmos.Models.Enums;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Helpers;

/// <summary>
/// B 线批次顺延判定结果（只读快照）。
/// 文案不放这里：格式串在 <see cref="NewCosmos.Constants.BusinessCycleConstants"/>，
/// 由调用方（工作流列表 / 申请表单）按各自展示密度拼装。
/// </summary>
public sealed class BatchDeferralInfo
{
    /// <summary>业务锚点日期（调查日期优先，周期差护栏回退受理日期）</summary>
    public DateTime AnchorDate { get; init; }

    /// <summary>是否已顺延（锚点的原批次 ≠ 顺延后的实际批次）</summary>
    public bool IsDeferred { get; init; }

    /// <summary>实际所属 B 线批次窗口：会议日 / 公示起止 / 审核确认日（顺延时为顺延后的窗口）</summary>
    public TimelineResult Batch { get; init; } = null!;
}

/// <summary>
/// B 线批次顺延判定（UI 提示的唯一入口）：
/// - 锚点 = <see cref="ArchiveAnchorHelper"/>：入户调查日期优先，仅当调查日与受理日周期差 ∈ {0,+1} 时生效，
///   其余回退受理日期（申请编号内嵌日期 → 创建 → …）——与文书产出、目录卷号同锚点。
/// - 顺延判定 = 比较 <c>GetTimelineForDate(锚点)</c> 与 <c>GetTimelineForApplicationDate(锚点)</c> 的年月：
///   只做"比较两个权威结果"，调用点禁止重抄"申请日 &gt; 会议日 → 顺延下月"运算
///   （顺延规则唯一权威是 BusinessTimelineService.GetTimelineForApplicationDate，AGENTS §14）。
/// 由此保证 UI 提示与实际打出的文书公示窗口、卷号永远同一批次。
/// </summary>
public static class BatchDeferralHelper
{
    /// <summary>
    /// 判定某申请的 B 线批次是否已顺延，并返回实际批次窗口。
    /// </summary>
    /// <param name="timeline">B 线时间轴服务（唯一权威）</param>
    /// <param name="applicationDate">受理（建档）日期；草稿无编号日期时由调用方传今天</param>
    /// <param name="surveyDate">入户调查日期（优先锚点）；无调查记录传 null</param>
    public static BatchDeferralInfo Resolve(
        IBusinessTimelineService timeline,
        DateTime applicationDate,
        DateTime? surveyDate)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        // 受理日与调查日各自的"原批次"（不带顺延），供锚点周期差护栏比较
        var applicationCycleLine = timeline.GetTimelineForDate(applicationDate, TimelineType.BusinessProcess);
        TimelineResult? surveyCycleLine = surveyDate is { } survey && survey != default
            ? timeline.GetTimelineForDate(survey, TimelineType.BusinessProcess)
            : null;

        var anchor = ResolveAnchorDate(
            applicationDate, surveyDate,
            (applicationCycleLine.Year, applicationCycleLine.Month),
            surveyCycleLine == null ? null : (surveyCycleLine.Year, surveyCycleLine.Month));

        var baseLine = timeline.GetTimelineForDate(anchor, TimelineType.BusinessProcess);
        var batch = timeline.GetTimelineForApplicationDate(anchor, TimelineType.BusinessProcess);

        return new BatchDeferralInfo
        {
            AnchorDate = anchor,
            IsDeferred = IsDeferred(baseLine, batch),
            Batch = batch
        };
    }

    /// <summary>
    /// 业务锚点日期：调查日期优先，周期差护栏委托 <see cref="ArchiveAnchorHelper.ResolveBusinessAnchorDate"/>。
    /// 无调查记录（或日期为 default）→ 受理日期。周期差 ≥2、超前周期等异常错位一律回退受理日期。
    /// </summary>
    public static DateTime ResolveAnchorDate(
        DateTime applicationDate,
        DateTime? surveyDate,
        (int Year, int Month) applicationCycle,
        (int Year, int Month)? surveyCycle)
    {
        if (surveyDate is not { } survey || survey == default || surveyCycle is not { } cycle)
            return applicationDate;
        return ArchiveAnchorHelper.ResolveBusinessAnchorDate(applicationDate, surveyDate, applicationCycle, cycle);
    }

    /// <summary>
    /// 是否顺延：锚点的"原批次"与 <c>GetTimelineForApplicationDate</c> 算出的"实际批次"年月不同。
    /// 跨年（12 月 → 次年 1 月）同样判定为顺延。
    /// </summary>
    public static bool IsDeferred(TimelineResult baseLine, TimelineResult batchLine)
        => (baseLine.Year, baseLine.Month) != (batchLine.Year, batchLine.Month);
}
