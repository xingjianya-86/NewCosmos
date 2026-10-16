using ApplicationEntity = NewCosmos.Models.Entities.Application;

namespace NewCosmos.Helpers;

/// <summary>
/// 档案时间锚定日期（唯一事实来源）：数据补全完成 → 首次审批 → 更新 → 创建。
/// 档案产出（公示起止/审核确认日/档案生效/卷号/入户调查日期）与表单「入户调查日期避公示」
/// 校验必须共用本类——两套锚点会算出不同的 B 线公示窗口，校验放行的日期打印时仍会落进公示期。
/// 任何日期解析改动只允许改这里，禁止在调用点各自重抄。
/// </summary>
public static class ArchiveAnchorHelper
{
    /// <summary>
    /// 档案时间锚定日期：数据补全完成 → 首次审批 → 更新 → 创建。
    /// 用于按该档案自身业务时间取 B 线周期（档案编号/卷号/公示/审核/生效/调查日期），
    /// 避免档案补打时按“当天”重算导致编号/卷号漂移。
    /// </summary>
    public static DateTime ResolveArchiveAnchorDate(ApplicationEntity app)
        => ResolveArchiveAnchorDate(
            app.DataCompletedAt ?? default,
            app.FirstApprovedAt ?? default,
            app.UpdatedAt,
            ResolveApplicationDate(app));

    /// <summary>
    /// 值参重载：调用方只有原始列（无实体）时使用，如 ApplicationStatusService 的时序告警。
    /// 顺序必须与实体版一致：数据补全完成 → 首次审批 → 更新 → 申请日期。
    /// </summary>
    public static DateTime ResolveArchiveAnchorDate(
        DateTime dataCompletedAt, DateTime firstApprovedAt, DateTime updatedAt, DateTime applicationDate)
    {
        if (dataCompletedAt != default) return dataCompletedAt;
        if (firstApprovedAt != default) return firstApprovedAt;
        if (updatedAt != default) return updatedAt;
        return applicationDate;
    }

    /// <summary>
    /// 申请日期（业务受理时间）解析（稳定口径）：
    /// 申请编号内嵌日期（SA+yyyyMMdd，创建时生成）→ 创建时间 → 首次审批 → 补全完成 → 更新 → 今天。
    /// 记录 created_at 为 NULL 时实体初值会被填成加载时刻（UtcNow），直接打印会导致每次补打都漂移，
    /// 故以申请编号日期优先。
    /// </summary>
    public static DateTime ResolveApplicationDate(ApplicationEntity app)
        => ResolveApplicationDate(app.ApplicationNo, app.CreatedAt, app.FirstApprovedAt,
            app.DataCompletedAt, app.UpdatedAt);

    /// <summary>
    /// 值参重载：调用方只有原始列（无实体）时使用，解析顺序与实体版一致。
    /// </summary>
    public static DateTime ResolveApplicationDate(
        string? applicationNo, DateTime createdAt, DateTime? firstApprovedAt,
        DateTime? dataCompletedAt, DateTime updatedAt)
    {
        var no = applicationNo;
        if (!string.IsNullOrEmpty(no) && no.Length >= 10
            && no.StartsWith("SA", StringComparison.Ordinal)
            && DateTime.TryParseExact(no.Substring(2, 8), "yyyyMMdd", null,
                System.Globalization.DateTimeStyles.None, out var fromNo))
        {
            return fromNo;
        }
        if (createdAt != default) return createdAt;
        if (firstApprovedAt is { } approved && approved != default) return approved;
        if (dataCompletedAt is { } completed && completed != default) return completed;
        if (updatedAt != default) return updatedAt;
        return DateTime.Today;
    }

    /// <summary>
    /// 业务锚点（申请日期·2026-10-16 新口径）：**调查日期优先**，仅当调查日归属周期与受理周期相差
    /// 不超过一个周期（周期差 ∈ {0, +1}）时生效；其余回退受理锚点（<see cref="ResolveApplicationDate"/>）。
    /// - 周期差 0：调查日与受理日同周期 → 批次不变，文书「申请日期」取调查日；
    /// - 周期差 +1：调查日晚于受理批次（如建档 8-24、入户 9-21 跨过会议日）→ 业务顺延下一周期；
    /// - 其余（超前/滞后 ≥2 个周期，含调查日为 2016/2019 等历史错位数据）→ 回退受理锚点，
    ///   防止文书打出远年公示/生效/卷号。
    /// 受理/调查周期由调用方用 IBusinessTimelineService.GetTimelineForDate 求出后传入（本类保持纯函数）。
    /// UI 侧配套「时间排斥原则」（Step4 调查日期硬校验）保证新数据的调查日落在受理批次窗口内，
    /// 窗口内则两锚点必同批次；本方法的周期差护栏是打印侧兜底（存量/豁免数据）。
    /// </summary>
    public static DateTime ResolveBusinessAnchorDate(
        DateTime applicationDate,
        DateTime? surveyDate,
        (int Year, int Month) applicationCycle,
        (int Year, int Month) surveyCycle)
    {
        if (surveyDate is not { } survey || survey == default) return applicationDate;
        var gap = (surveyCycle.Year * 12 + surveyCycle.Month)
                - (applicationCycle.Year * 12 + applicationCycle.Month);
        return gap is 0 or 1 ? survey.Date : applicationDate;
    }
}
