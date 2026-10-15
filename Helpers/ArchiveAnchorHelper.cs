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
}
