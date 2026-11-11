using System.Globalization;

namespace NewCosmos.Helpers;

/// <summary>
/// 申请编号（格式 <c>SA + yyyyMMdd + 4位序号</c>）的唯一维护点：
/// 取号（ApplicationService.GetNextApplicationNoAsync）、改期重号（ChangeApplicationDateAsync）、
/// 申请日期锚点解析（ArchiveAnchorHelper.ResolveApplicationDate 第一优先级）必须共用本类。
/// 编号日期段是"申请日期"的权威来源——任何格式改动只允许改这里，禁止调用点各自拼 <c>SA{date:yyyyMMdd}</c>。
/// </summary>
public static class ApplicationNoHelper
{
    /// <summary>编号前缀（社会救助申请；临时救助 LZ、高龄 GA 不走本类）</summary>
    public const string Prefix = "SA";

    /// <summary>编号最小长度：前缀 2 位 + 日期段 8 位</summary>
    public const int MinLength = 10;

    /// <summary>日期段格式（与取号 SQL 的 SUBSTRING(... FROM 11) 口径一致）</summary>
    private const string DateSegmentFormat = "yyyyMMdd";

    /// <summary>拼日期前缀：2026-11-06 → "SA20261106"</summary>
    public static string BuildPrefix(DateTime date) => $"{Prefix}{date:yyyyMMdd}";

    /// <summary>
    /// 拼完整编号：前缀 + 4 位序号（序号 &gt; 9999 时自然进位为 5 位；
    /// 取号 SQL 与锚点解析均按"前缀后全部数字"处理，不受位数影响）。
    /// </summary>
    public static string BuildNo(DateTime date, int sequence) => $"{BuildPrefix(date)}{sequence:D4}";

    /// <summary>
    /// 解析编号内嵌日期；非 SA 格式、长度不足或日期段非法返回 null。
    /// 解析口径与历史锚点实现完全一致（Length ≥ 10 + StartsWith("SA") + yyyyMMdd 精确解析）。
    /// </summary>
    public static DateTime? TryParseDate(string? applicationNo)
    {
        if (string.IsNullOrEmpty(applicationNo)
            || applicationNo.Length < MinLength
            || !applicationNo.StartsWith(Prefix, StringComparison.Ordinal))
            return null;

        return DateTime.TryParseExact(applicationNo.Substring(2, 8), DateSegmentFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}
