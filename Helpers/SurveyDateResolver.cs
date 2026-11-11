using NewCosmos.Services.Utilities;

namespace NewCosmos.Helpers;

/// <summary>
/// 入户调查日期（文书打印口径）**唯一实现**——原先在 <c>ArchiveProductionViewModel</c> 与
/// <c>TemplateFieldBuilder</c> 各有一份同构拷贝（2026-10-16 同步），2026-11-11 资产核查链路
/// （224 部省协查函换绑 SURVEY_DATE）需要第三处，故抽到本类单点，两处旧实现改为转调。
///
/// 规则（与 2026-10-16 口径一致）：
/// <list type="bullet">
/// <item>优先用实际入户调查日；无调查记录（补录/未录入）→ 申请日当天（倒挂窗口取会议日）。</item>
/// <item><b>上限铁律</b>：入户不得落进公示期、不得晚于会议日——上限永远生效。</item>
/// <item><b>倒挂窗口</b>（申请日 &gt; 会议日）：两界冲突时**上限优先**，放弃"不早于申请日"下限。</item>
/// <item>正常窗口维持 [申请日, 会议日] 双钳（调查不早于建档的自愈保留）。</item>
/// </list>
/// 本类只返回 <see cref="DateTime"/>，格式由调用方按各自模板 config 的 format 决定。
/// </summary>
public static class SurveyDateResolver
{
    /// <param name="timeline">B 线时间轴（取会议日=公示前一工作日）。</param>
    /// <param name="actualSurvey">实际入户调查日；无调查记录传 null。</param>
    /// <param name="applicationDate">业务锚点申请日期（= 调查日新口径的业务申请日期）。</param>
    public static DateTime Resolve(TimelineResult timeline, DateTime? actualSurvey, DateTime applicationDate)
    {
        var minDate = applicationDate.Date;
        var maxDate = timeline.MeetingDate.Date;
        var baseDate = actualSurvey.HasValue && actualSurvey.Value.Date != default
            ? actualSurvey.Value.Date
            : (maxDate < minDate ? maxDate : minDate); // 无调查记录：默认申请日当天（倒挂窗口取会议日，保证不落公示期）

        if (maxDate < minDate)
        {
            // 倒挂窗口：只保上限（公示外铁律），不抬到申请日
            if (baseDate > maxDate) baseDate = maxDate;
        }
        else
        {
            if (baseDate < minDate) baseDate = minDate;
            if (baseDate > maxDate) baseDate = maxDate;
        }

        return baseDate;
    }
}
