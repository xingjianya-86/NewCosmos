using NewCosmos.Helpers;
using NewCosmos.Models.Enums;
using NewCosmos.Services.Core;

namespace NewCosmos.Services.Utilities;

/// <summary>
/// 业务时间线计算结    /// </summary>
public class TimelineResult
{
    public int Year { get; set; }
    public int Month { get; set; }
    public TimelineType Type { get; set; }

    public DateTime CycleStartDate { get; set; }
    public DateTime CycleEndDate { get; set; }

    public DateTime AcceptanceDeadline { get; set; }
    public DateTime MeetingDate { get; set; }
    public DateTime PublicityStartDate { get; set; }
    public DateTime PublicityEndDate { get; set; }
    public DateTime AuditDate { get; set; }

    /// <summary>C线：入户调查核实窗口起日（普通=截止前10个工作日、特殊群体=截止前5个工作日）</summary>
    public DateTime InvestigationStartDate { get; set; }

    /// <summary>C线：入户调查核实截止日（公示开始前一个工作日）</summary>
    public DateTime InvestigationDeadline { get; set; }

    public bool IsHolidayAffected { get; set; }

    public string DateRangeText => Type switch
    {
        TimelineType.EconomicReview =>
            $"{CycleStartDate:MM月dd日} {CycleEndDate:MM月dd日}",
        TimelineType.BusinessProcess =>
            $"{CycleStartDate:MM月dd日} {CycleEndDate:MM月dd日}",
        TimelineType.TempRelief =>
            $"{CycleStartDate:MM月dd日} {CycleEndDate:MM月dd日}",
        _ => string.Empty
    };
}

/// <summary>
/// 业务时间线服    /// A线（经济核查）：每月10号结算，上月11日~本月10    /// B线（业务线）：每月15号结算，受理窗口上月15日~本月15日，会议7    /// </summary>
public interface IBusinessTimelineService
{
    Task<TimelineResult> CalculateTimelineAsync(int year, int month, TimelineType type);
    Task<TimelineResult> GetCurrentTimelineAsync(TimelineType type);

    /// <summary>
    /// 按指定日期取所处周期：超过当月周期截止日时自动滚入下一个月（与 GetCurrentTimelineAsync 同语义）。
    /// 用于档案补打等需按“该档案自身业务日期”锚定周期、避免每次按当天重算导致编号/卷号漂移的场景。
    /// </summary>
    TimelineResult GetTimelineForDate(DateTime date, TimelineType type);

    /// <summary>
    /// C线（临时救助验收线）：按年月直算。simplified=true 时入户调查核实期为5个工作日（特殊群体），否则10个。
    /// </summary>
    Task<TimelineResult> CalculateTempReliefAsync(int year, int month, bool simplified);

    /// <summary>C线当前周期：今天 &gt; 12日时自动滚入下一个月</summary>
    Task<TimelineResult> GetCurrentTempReliefAsync(bool simplified);

    /// <summary>
    /// 临时救助验收日期 = 公示结束的次一个工作日；公示结束为空时回退当前C线窗口结束日。
    /// </summary>
    DateTime GetTempReliefAcceptanceDate(DateTime? publicizeEnd);

    /// <summary>
    /// 按申请日期归属 C 线窗口（公示固定每月10日~12日）：取第一个"申请日 ≤ 该月窗口调查截止日"的月份（9号及以前归当月，10号起顺延下月），否则顺延到下月。
    /// </summary>
    Task<TimelineResult> CalculateTempReliefForApplyDateAsync(DateTime applyDate, bool simplified);

    /// <summary>C线窗口归属（同步版）：供表单申请日期约束与保存校验在同步上下文中调用。</summary>
    TimelineResult CalculateTempReliefForApplyDate(DateTime applyDate, bool simplified);

    /// <summary>
    /// C线文书「入户调查时间」= min(今日的最近工作日, 该窗口调查截止日=公示开始前一个工作日)。
    /// 今日为工作日取今日（如9号打印→9号，恰为公示前一天）；今日为周末/节假日往回取过去最近工作日；
    /// 晚于截止日（补打历史档案）兜底为截止日。特殊5/普通10两档上限一致（截止日与档位无关）。
    /// </summary>
    DateTime GetTempReliefInvestigationPrintDate(TimelineResult cLine);
}

public class BusinessTimelineService : BaseService, IBusinessTimelineService
{
    protected override string ServiceName => "BusinessTimelineService";

    private readonly IHolidayService _holidayService;

    // A线参数
    private const int A_END_DAY = 10;
    // B线参数（结算日由 BusinessCycleHelper.SettleDay 动态提供，不再硬编码）
    private const int B_MEETING_DAY = 7;
    private const int B_ACCEPTANCE_DEADLINE_OFFSET = -3;  // 会议前3个工作日
    private const int B_PUBLICITY_DAYS = 7;

    // C线参数（临时救助验收线）
    private const int C_PUBLICITY_START_DAY = 10;        // 公示固定开始日（日历日，不顺延）
    private const int C_PUBLICITY_END_DAY = 12;          // 公示固定结束日（日历日，不顺延）
    private const int C_INVESTIGATION_WORKDAYS_NORMAL = 10;      // 普通群体：入户调查核实10个工作日
    private const int C_INVESTIGATION_WORKDAYS_SIMPLIFIED = 5;   // 特殊群体：入户调查核实5个工作日

    public BusinessTimelineService(IHolidayService holidayService, ILoggerService logger)
        : base(logger)
    {
        _holidayService = holidayService;
    }

    public Task<TimelineResult> CalculateTimelineAsync(int year, int month, TimelineType type)
    {
        return Task.FromResult(CalculateCore(year, month, type));
    }

    public Task<TimelineResult> GetCurrentTimelineAsync(TimelineType type)
    {
        return Task.FromResult(GetCurrentCore(type));
    }

    public TimelineResult GetTimelineForDate(DateTime date, TimelineType type) => GetForDateCore(date, type);

    public Task<TimelineResult> CalculateTempReliefAsync(int year, int month, bool simplified)
    {
        return Task.FromResult(CalculateCLine(year, month, simplified));
    }

    public Task<TimelineResult> GetCurrentTempReliefAsync(bool simplified)
    {
        return Task.FromResult(GetCurrentTempReliefCore(simplified));
    }

    public DateTime GetTempReliefAcceptanceDate(DateTime? publicizeEnd)
    {
        if (publicizeEnd.HasValue && publicizeEnd.Value.Date != DateTime.MinValue.Date)
            return _holidayService.GetNextWorkDay(publicizeEnd.Value);

        // 公示结束为空：回退当前C线窗口结束日的次一个工作日
        var current = GetCurrentTempReliefCore(simplified: false);
        return _holidayService.GetNextWorkDay(current.PublicityEndDate);
    }

    /// <inheritdoc />
    public DateTime GetTempReliefInvestigationPrintDate(TimelineResult cLine)
    {
        // 今日的最近工作日：工作日取今日，周末/节假日往回取（调查事实已完成，不倒填未来）
        var today = DateTime.Today;
        var recentWorkday = _holidayService.IsWorkDay(today)
            ? today
            : _holidayService.GetPreviousWorkDay(today);

        // 上限 = 该窗口调查截止（公示开始前一个工作日）：晚于截止（补打历史档案）则兜底为截止日
        var deadline = cLine.InvestigationDeadline.Date;
        return recentWorkday > deadline ? deadline : recentWorkday;
    }

    public Task<TimelineResult> CalculateTempReliefForApplyDateAsync(DateTime applyDate, bool simplified)
    {
        return Task.FromResult(CalculateCLineForApplyDate(applyDate, simplified));
    }

    public TimelineResult CalculateTempReliefForApplyDate(DateTime applyDate, bool simplified)
        => CalculateCLineForApplyDate(applyDate, simplified);

    /// <summary>
    /// 申请日归属窗口：从申请日所在月起，取第一个 applyDate ≤ InvestigationDeadline（调查截止=公示前一工作日，
    /// 即9号及以前归当月、10号起顺延下月）的 C 线窗口；
    /// 最多顺延 24 个月（防御），仍无则回退申请日所在月。
    /// </summary>
    private TimelineResult CalculateCLineForApplyDate(DateTime applyDate, bool simplified)
    {
        var date = applyDate.Date;
        var year = date.Year;
        var month = date.Month;
        for (var i = 0; i < 24; i++)
        {
            var line = CalculateCLine(year, month, simplified);
            if (date <= line.InvestigationDeadline.Date)
                return line;

            month++;
            if (month > 12) { month = 1; year++; }
        }
        return CalculateCLine(date.Year, date.Month, simplified);
    }

    /// <summary>按指定年月直算时间轴（纯计算，无滚动判断）</summary>
    private TimelineResult CalculateCore(int year, int month, TimelineType type)
    {
        return type switch
        {
            TimelineType.EconomicReview => CalculateALine(year, month),
            TimelineType.BusinessProcess => CalculateBLine(year, month),
            TimelineType.TempRelief => CalculateCLine(year, month, simplified: false),
            _ => throw new ArgumentException($"未知的时间线类型: {type}")
        };
    }

    /// <summary>当前C线周期：今天超过公示结束日（12日）时自动滚入下一个月</summary>
    private TimelineResult GetCurrentTempReliefCore(bool simplified)
    {
        var now = DateTime.Today;
        var result = CalculateCLine(now.Year, now.Month, simplified);
        if (now.Date > result.CycleEndDate.Date)
        {
            var nextMonth = now.Month == 12 ? 1 : now.Month + 1;
            var nextYear = now.Month == 12 ? now.Year + 1 : now.Year;
            return CalculateCLine(nextYear, nextMonth, simplified);
        }
        return result;
    }

    /// <summary>
    /// 取当前所处周期：今天超过当月周期截止日时自动滚入下一个月（A/B 线统一语义）
    /// </summary>
    private TimelineResult GetCurrentCore(TimelineType type) => GetForDateCore(DateTime.Today, type);

    /// <summary>按指定日期取所处周期（超过当月周期截止日自动滚入下月）</summary>
    private TimelineResult GetForDateCore(DateTime date, TimelineType type)
    {
        var d = date.Date;
        var result = CalculateCore(d.Year, d.Month, type);

        if (d > result.CycleEndDate.Date)
        {
            var nextMonth = d.Month == 12 ? 1 : d.Month + 1;
            var nextYear = d.Month == 12 ? d.Year + 1 : d.Year;
            return CalculateCore(nextYear, nextMonth, type);
        }

        return result;
    }

    private TimelineResult CalculateALine(int year, int month)
    {
        var endDay = A_END_DAY;
        var endDate = new DateTime(year, month, endDay);

        // 遇周末提前到前一个工作日
        if (!_holidayService.IsWorkDay(endDate))
            endDate = _holidayService.GetPreviousWorkDay(endDate);

        var startDate = new DateTime(year, month, 1).AddMonths(-1).AddDays(10); // 上月11?
        return new TimelineResult
        {
            Year = year,
            Month = month,
            Type = TimelineType.EconomicReview,
            CycleStartDate = startDate,
            CycleEndDate = endDate
        };
    }

    private TimelineResult CalculateBLine(int year, int month)
    {
        bool isHolidayAffected = false;
        var endDay = BusinessCycleHelper.SettleDay;

        // 检查结算日前5天至结算日是否有节假日（如20号结算则检查15~20号）
        for (int d = Math.Max(1, endDay - 5); d <= endDay; d++)
        {
            if (_holidayService.IsHoliday(new DateTime(year, month, d)))
            {
                isHolidayAffected = true;
                endDay = Math.Max(1, endDay - 5);
                break;
            }
        }

        var endDate = new DateTime(year, month, endDay);
        if (!_holidayService.IsWorkDay(endDate))
            endDate = _holidayService.GetPreviousWorkDay(endDate);

        // 受理窗口：上月(结算日+1)日~本月(结算日+1)日
        var startDate = new DateTime(year, month, 1).AddMonths(-1).AddDays(BusinessCycleHelper.SettleDay);
        // 会议日：本月7日，避开周末
        var meetingDate = new DateTime(year, month, B_MEETING_DAY);
        if (!_holidayService.IsWorkDay(meetingDate))
            meetingDate = _holidayService.GetNextWorkDay(meetingDate);

        // 受理截止日：会议前3 个工作日
        var acceptanceDeadline = meetingDate;
        for (int i = 0; i < Math.Abs(B_ACCEPTANCE_DEADLINE_OFFSET); i++)
            acceptanceDeadline = _holidayService.GetPreviousWorkDay(acceptanceDeadline);

        // 公示期：会议次日 ~ +6天（?天）
        var publicityStart = _holidayService.GetNextWorkDay(meetingDate);
        var publicityEnd = publicityStart.AddDays(B_PUBLICITY_DAYS - 1);

        // 审核确认日：公示结束次日
        var auditDate = _holidayService.GetNextWorkDay(publicityEnd);

        return new TimelineResult
        {
            Year = year,
            Month = month,
            Type = TimelineType.BusinessProcess,
            CycleStartDate = startDate,
            CycleEndDate = endDate,
            AcceptanceDeadline = acceptanceDeadline,
            MeetingDate = meetingDate,
            PublicityStartDate = publicityStart,
            PublicityEndDate = publicityEnd,
            AuditDate = auditDate,
            IsHolidayAffected = isHolidayAffected
        };
    }

    /// <summary>
    /// C线（临时救助验收线）：公示固定每月10日~12日（日历日，不顺延）；
    /// 入户调查核实截止 = 公示开始前一个工作日，起日 = 截止往前 (N-1) 个工作日（普通N=10、特殊群体N=5）；
    /// 验收日期 = 公示结束（12日）的次一个工作日。
    /// </summary>
    private TimelineResult CalculateCLine(int year, int month, bool simplified)
    {
        var publicityStart = new DateTime(year, month, C_PUBLICITY_START_DAY);
        var publicityEnd = new DateTime(year, month, C_PUBLICITY_END_DAY);

        // 调查核实截止：公示开始前一个工作日
        var deadline = _holidayService.GetPreviousWorkDay(publicityStart);

        // 起日：截止往前 (N-1) 个工作日
        var workdays = simplified ? C_INVESTIGATION_WORKDAYS_SIMPLIFIED : C_INVESTIGATION_WORKDAYS_NORMAL;
        var investigationStart = deadline;
        for (var i = 1; i < workdays; i++)
            investigationStart = _holidayService.GetPreviousWorkDay(investigationStart);

        // 验收日：公示结束次一个工作日
        var acceptanceDate = _holidayService.GetNextWorkDay(publicityEnd);

        return new TimelineResult
        {
            Year = year,
            Month = month,
            Type = TimelineType.TempRelief,
            CycleStartDate = publicityStart,
            CycleEndDate = publicityEnd,
            PublicityStartDate = publicityStart,
            PublicityEndDate = publicityEnd,
            InvestigationStartDate = investigationStart,
            InvestigationDeadline = deadline,
            AuditDate = acceptanceDate
        };
    }
}
