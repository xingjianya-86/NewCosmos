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

    /// <summary>C线：入户调查窗口起日（验收日前5天，日历日）</summary>
    public DateTime InvestigationStartDate { get; set; }

    /// <summary>C线：入户调查窗口截止日（= 公示起日）</summary>
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
/// 业务时间线服    /// A线（经济核查）：每月10号结算，上月11日~本月10    /// B线（业务线）：结算日由 BusinessCycleHelper.SettleDay 提供（app.ini BCycleSettleDay，默认15）；
/// 周期起点 = 上月(结算日+1)日，终点 = 本月结算日（遇法定节假日提前5天、周末再前移至工作日）；
/// 会议7日（顺延工作日），受理截止 = 会议前3个工作日，公示 = 会议次日起7天。
/// </summary>
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
    /// 单档案文书窗口（B 线专用，A/C 线直接转发 <see cref="GetTimelineForDate"/>）：
    /// 申请日期晚于该月会议日 → 顺延下一周期（该申请赶不上本月会议与公示批次）。
    /// 档案产出、目录、卷号、表单调查日期默认值与「调查避公示」校验统一走本方法，
    /// 保证文书上公示起日 ≥ 申请日、调查日不早于申请日；禁止调用点各自写顺延运算。
    /// </summary>
    TimelineResult GetTimelineForApplicationDate(DateTime applicationDate, TimelineType type);

    /// <summary>
    /// C线（临时救助验收线）：按年月直算。验收日固定每月13号（非工作日顺延至其后最近工作日），
    /// 公示止=验收日前1天、公示起=验收日前3天（均日历日）；入户调查窗口=[验收日前5天, 公示起]。
    /// </summary>
    Task<TimelineResult> CalculateTempReliefAsync(int year, int month);

    /// <summary>C线当前周期：今天超过验收日时自动滚入下一个月</summary>
    Task<TimelineResult> GetCurrentTempReliefAsync();

    /// <summary>
    /// 临时救助验收日期 = 公示起止所属月份的 C 线验收日（13号，非工作日顺延至其后最近工作日）；
    /// 公示结束为空时回退当前C线窗口的验收日。
    /// </summary>
    DateTime GetTempReliefAcceptanceDate(DateTime? publicizeEnd);

    /// <summary>
    /// 按申请日期归属 C 线窗口：公示起止与验收日均取【申请日期所在月份】的 C 线窗口（不滚月）。
    /// 申请日期越界（晚于该月入户调查窗口起日）时由表单录入侧自动钳到窗口起日，本方法不处理。
    /// </summary>
    Task<TimelineResult> CalculateTempReliefForApplyDateAsync(DateTime applyDate);

    /// <summary>C线窗口（同步版）：供表单申请日期约束与保存校验在同步上下文中调用。</summary>
    TimelineResult CalculateTempReliefForApplyDate(DateTime applyDate);

    /// <summary>
    /// C线文书「入户调查时间」= clamp(今日的最近工作日, [该窗口入户调查窗口起日, 窗口截止=公示起日])。
    /// 今日为工作日取今日；今日为周末/节假日往回取过去最近工作日；早于窗口起日（窗口未开启）取起日；
    /// 晚于窗口截止（补打历史档案）兜底为截止日。
    /// </summary>
    DateTime GetTempReliefInvestigationPrintDate(TimelineResult cLine);

    /// <summary>
    /// B线「入户调查日期」规避上限 = 公示开始前一个工作日。
    /// 调查必须早于公示；等于或晚于该日的调查日期打印出来即形成「调查在公示期内」的时序矛盾。
    /// 表单默认值、Step4 校验与档案产出共用本方法，禁止调用点各自写日期运算。
    /// </summary>
    DateTime GetPrePublicityWorkday(TimelineResult bLine);
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
    private const int C_ACCEPTANCE_DAY = 13;             // 验收日固定日历日（非工作日顺延至其后最近工作日）
    private const int C_PUBLICITY_END_OFFSET = -1;       // 公示止 = 验收日 − 1（日历日，不排工作日）
    private const int C_PUBLICITY_START_OFFSET = -3;     // 公示起 = 验收日 − 3（日历日，不排工作日）
    private const int C_INVESTIGATION_START_OFFSET = -5; // 入户调查窗口起日 = 验收日 − 5（日历日）

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

    /// <inheritdoc />
    public TimelineResult GetTimelineForApplicationDate(DateTime applicationDate, TimelineType type)
    {
        var line = GetForDateCore(applicationDate, type);

        // B 线：申请日超过会议日说明赶不上本月会议/公示批次，顺延下一周期
        if (type == TimelineType.BusinessProcess && applicationDate.Date > line.MeetingDate.Date)
        {
            var nextMonth = line.Month == 12 ? 1 : line.Month + 1;
            var nextYear = line.Month == 12 ? line.Year + 1 : line.Year;
            line = CalculateBLine(nextYear, nextMonth);
        }

        return line;
    }

    public Task<TimelineResult> CalculateTempReliefAsync(int year, int month)
    {
        return Task.FromResult(CalculateCLine(year, month));
    }

    public Task<TimelineResult> GetCurrentTempReliefAsync()
    {
        return Task.FromResult(GetCurrentTempReliefCore());
    }

    public DateTime GetTempReliefAcceptanceDate(DateTime? publicizeEnd)
    {
        if (publicizeEnd.HasValue && publicizeEnd.Value.Date != DateTime.MinValue.Date)
            return CalculateCLine(publicizeEnd.Value.Year, publicizeEnd.Value.Month).AuditDate;

        // 公示结束为空：回退当前C线窗口的验收日
        return GetCurrentTempReliefCore().AuditDate;
    }

    /// <inheritdoc />
    public DateTime GetTempReliefInvestigationPrintDate(TimelineResult cLine)
    {
        // 今日的最近工作日：工作日取今日，周末/节假日往回取（调查事实已完成，不倒填未来）
        var today = DateTime.Today;
        var recentWorkday = _holidayService.IsWorkDay(today)
            ? today
            : _holidayService.GetPreviousWorkDay(today);

        // 钳入窗口 [起日=验收日前5天, 截止=公示起日]：
        // 早于起日（窗口未开启）取起日；晚于截止（补打历史档案）兜底为截止日。
        var start = cLine.InvestigationStartDate.Date;
        var deadline = cLine.InvestigationDeadline.Date;
        if (recentWorkday < start) return start;
        if (recentWorkday > deadline) return deadline;
        return recentWorkday;
    }

    /// <inheritdoc />
    public DateTime GetPrePublicityWorkday(TimelineResult bLine)
        => _holidayService.GetPreviousWorkDay(bLine.PublicityStartDate.Date);

    public Task<TimelineResult> CalculateTempReliefForApplyDateAsync(DateTime applyDate)
    {
        return Task.FromResult(CalculateCLineForApplyDate(applyDate));
    }

    public TimelineResult CalculateTempReliefForApplyDate(DateTime applyDate)
        => CalculateCLineForApplyDate(applyDate);

    /// <summary>
    /// 申请日归属窗口：直接取申请日期所在月份的 C 线窗口（不滚月）。
    /// 申请日期越界（晚于该月入户调查窗口起日）时不在此处理——由表单录入侧自动钳到窗口起日。
    /// </summary>
    private TimelineResult CalculateCLineForApplyDate(DateTime applyDate)
        => CalculateCLine(applyDate.Date.Year, applyDate.Date.Month);

    /// <summary>按指定年月直算时间轴（纯计算，无滚动判断）</summary>
    private TimelineResult CalculateCore(int year, int month, TimelineType type)
    {
        return type switch
        {
            TimelineType.EconomicReview => CalculateALine(year, month),
            TimelineType.BusinessProcess => CalculateBLine(year, month),
            TimelineType.TempRelief => CalculateCLine(year, month),
            _ => throw new ArgumentException($"未知的时间线类型: {type}")
        };
    }

    /// <summary>当前C线周期：今天超过验收日（CycleEndDate）时自动滚入下一个月</summary>
    private TimelineResult GetCurrentTempReliefCore()
    {
        var now = DateTime.Today;
        var result = CalculateCLine(now.Year, now.Month);
        if (now.Date > result.CycleEndDate.Date)
        {
            var nextMonth = now.Month == 12 ? 1 : now.Month + 1;
            var nextYear = now.Month == 12 ? now.Year + 1 : now.Year;
            return CalculateCLine(nextYear, nextMonth);
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

        // 检查结算日前5天至结算日是否有**法定节假日**（如20号结算则检查15~20号）。
        // 只认法定节假日：周末由下方 GetPreviousWorkDay 统一处理；
        // 若把周末也算进本窗口，6 天窗口内必含周末 → 惩罚恒触发 → 结算日永远被压到 settle-5。
        for (int d = Math.Max(1, endDay - 5); d <= endDay; d++)
        {
            if (_holidayService.IsLegalHoliday(new DateTime(year, month, d)))
            {
                isHolidayAffected = true;
                endDay = Math.Max(1, endDay - 5);
                break;
            }
        }

        var endDate = new DateTime(year, month, endDay);
        if (!_holidayService.IsWorkDay(endDate))
            endDate = _holidayService.GetPreviousWorkDay(endDate);

        // 受理窗口：上月(结算日+1)日~本月结算日（周期起止与 BusinessCycleHelper 同源）
        var startDate = BusinessCycleHelper.GetStart(year, month);
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
    /// C线（临时救助验收线）：验收日固定每月13号（非工作日顺延至其后最近工作日）；
    /// 公示止 = 验收日前1天、公示起 = 验收日前3天（均日历日，不排工作日）；
    /// 入户调查窗口 = [验收日前5天, 公示起日]（"入户调查不得晚于公示起"硬校验兜底）。
    /// </summary>
    private TimelineResult CalculateCLine(int year, int month)
    {
        // 验收日 = 13号；非工作日顺延至其后最近工作日（保证必为工作日）
        var acceptance = new DateTime(year, month, C_ACCEPTANCE_DAY);
        if (!_holidayService.IsWorkDay(acceptance))
            acceptance = _holidayService.GetNextWorkDay(acceptance);

        // 公示起止：验收日前后推（日历日，不考虑工作日）
        var publicityEnd = acceptance.AddDays(C_PUBLICITY_END_OFFSET);
        var publicityStart = acceptance.AddDays(C_PUBLICITY_START_OFFSET);

        // 入户调查窗口起日（日历日）；硬约束「不得晚于公示起日」
        var investigationStart = acceptance.AddDays(C_INVESTIGATION_START_OFFSET);
        if (investigationStart > publicityStart)
            investigationStart = publicityStart;

        return new TimelineResult
        {
            Year = year,
            Month = month,
            Type = TimelineType.TempRelief,
            CycleStartDate = investigationStart,
            CycleEndDate = acceptance,
            PublicityStartDate = publicityStart,
            PublicityEndDate = publicityEnd,
            InvestigationStartDate = investigationStart,
            InvestigationDeadline = publicityStart,
            AuditDate = acceptance
        };
    }
}
