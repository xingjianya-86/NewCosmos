using NewCosmos.Constants;
using System.Text.Json;

namespace NewCosmos.Helpers;

/// <summary>
/// 节假日接口响应解析器（纯静态、无 IO、无 DB —— 可直接进单元测试，见 HolidayApiParserTests）。
/// 支持两种数据源格式：
///   1. timor.tech        —— holiday 为字典（key=MM-dd），值含 holiday(bool)/name/date/wage
///   2. holiday-cn（备用）—— days 为数组，含 name/date/isOffDay，另带 papers（国务院公文链接）
/// 两者都归一化为同一产出：节假日行集合（日期 + HOLIDAY/MAKEUP 类型 + 节日名）。
/// </summary>
public static class HolidayApiParser
{
    /// <summary>归一化后的节假日行</summary>
    public readonly record struct HolidayRow(DateOnly Date, string DateType, string Name);

    /// <summary>解析结果：是否为"合法但该年尚未发布"的空数据</summary>
    public sealed class ParseResult
    {
        public List<HolidayRow> Rows { get; } = new();

        /// <summary>接口返回结构合法但不含任何数据（该年安排尚未发布）</summary>
        public bool IsEmptyYear { get; set; }

        /// <summary>响应结构无法识别或字段损坏（区别于"尚未发布"）</summary>
        public bool IsMalformed { get; set; }

        /// <summary>备用源特有：国务院公文链接（可作审计线索记入日志），主源为 null</summary>
        public string? PaperUrl { get; set; }
    }

    #region timor.tech

    private sealed class TimorResponse
    {
        public int code { get; set; }
        public Dictionary<string, TimorDay>? holiday { get; set; }
    }

    private sealed class TimorDay
    {
        public bool holiday { get; set; }
        public string name { get; set; } = string.Empty;
        public string date { get; set; } = string.Empty;
    }

    /// <summary>解析 timor.tech 响应。code!=0 视为无效响应（该源用非 0 code 表示错误/未收录）。</summary>
    public static ParseResult ParseTimor(string json)
    {
        var result = new ParseResult();
        TimorResponse? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TimorResponse>(json);
        }
        catch (JsonException)
        {
            result.IsMalformed = true;
            return result;
        }

        if (payload == null || payload.code != 0 || payload.holiday == null)
        {
            // timor 对未收录年份返回 code=0 + 空 holiday；非 0 code 属于异常响应
            result.IsMalformed = true;
            return result;
        }

        if (payload.holiday.Count == 0)
        {
            result.IsEmptyYear = true;
            return result;
        }

        foreach (var day in payload.holiday.Values)
        {
            if (!DateOnly.TryParse(day.date, out var date)) continue;
            var dateType = day.holiday
                ? DutyConstants.HolidayRowTypes.HOLIDAY
                : DutyConstants.HolidayRowTypes.MAKEUP;
            result.Rows.Add(new HolidayRow(date, dateType, day.name?.Trim() ?? string.Empty));
        }

        if (result.Rows.Count == 0) result.IsMalformed = true;
        return result;
    }

    #endregion

    #region holiday-cn（备用源）

    private sealed class HolidayCnResponse
    {
        public int? year { get; set; }
        public List<string>? papers { get; set; }
        public List<HolidayCnDay>? days { get; set; }
    }

    private sealed class HolidayCnDay
    {
        public string name { get; set; } = string.Empty;
        public string date { get; set; } = string.Empty;
        public bool isOffDay { get; set; }
    }

    /// <summary>
    /// 解析 holiday-cn 响应。
    /// 注意其 days 只覆盖"与法定假日相连的调休日"，普通周末不在内 —— 这与 timor 一致，语义无冲突。
    /// 节日名较粗（春节各日均名为"春节"，不含除夕/初一细分），属该源固有限制。
    /// </summary>
    public static ParseResult ParseHolidayCn(string json, int expectedYear)
    {
        var result = new ParseResult();
        HolidayCnResponse? payload;
        try
        {
            payload = JsonSerializer.Deserialize<HolidayCnResponse>(json);
        }
        catch (JsonException)
        {
            result.IsMalformed = true;
            return result;
        }

        if (payload == null)
        {
            result.IsMalformed = true;
            return result;
        }

        // 校验 year 字段：备用源返回的可能是错误年份或占位文件
        if (payload.year.HasValue && payload.year.Value != expectedYear)
        {
            result.IsEmptyYear = true;
            return result;
        }

        if (payload.days == null || payload.days.Count == 0)
        {
            result.IsEmptyYear = true;
            return result;
        }

        // 取第一公文链接作审计线索（若无则留 null，不视为错误）
        result.PaperUrl = payload.papers?.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));

        foreach (var day in payload.days)
        {
            if (!DateOnly.TryParse(day.date, out var date)) continue;
            // 只保留目标年份的行：某些源会把跨年的调休日放在相邻年份文件里
            if (date.Year != expectedYear) continue;
            var dateType = day.isOffDay
                ? DutyConstants.HolidayRowTypes.HOLIDAY
                : DutyConstants.HolidayRowTypes.MAKEUP;
            result.Rows.Add(new HolidayRow(date, dateType, day.name?.Trim() ?? string.Empty));
        }

        // 过滤后为空（例如文件只有其他年份的数据）也属"该年尚未发布"
        if (result.Rows.Count == 0)
        {
            result.IsEmptyYear = true;
            result.PaperUrl = null;
        }

        return result;
    }

    #endregion
}
