using NewCosmos.Helpers;
using NewCosmos.Models.Enums;
using NewCosmos.Services.Core;

namespace NewCosmos.Services.Utilities;

/// <summary>
/// 业务时间线计算结
/// </summary>
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

    /// <summary>C线：入户调查窗口起日（逐笔曲线下 = 申请日；存量档按库中公示起回放时仍为申请日）</summary>
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
    /// C线（临时救助验收线）：按年月直算，仅用于<b>月度批次标签与补打窗口</b>：
    /// 验收日锚点 = 每月13号（非工作日顺延至其后最近工作日）。
    /// 2026-11-13 起公示/验收不再取本方法窗口（改逐笔 5 天曲线，见 <see cref="BuildTempReliefCurve"/>）；
    /// 本方法保留仅为「申请日 ≤ A 归本月」批次归属与补打窗口 (上月A, 本月A]。
    /// </summary>
    Task<TimelineResult> CalculateTempReliefAsync(int year, int month);

    /// <summary>C线当前周期：今天超过验收日时自动滚入下一个月</summary>
    Task<TimelineResult> GetCurrentTempReliefAsync();

    /// <summary>
    /// 临时救助验收日期 = <b>公示止次日历日</b>（2026-11-13 起逐笔推导；旧口径公示止 = A−1 时结果恒等于 A，
    /// 存量档案零漂移）；公示结束为空时回退当前C线窗口的验收日。
    /// </summary>
    DateTime GetTempReliefAcceptanceDate(DateTime? publicizeEnd);

    /// <summary>
    /// C线逐笔时间曲线（2026-11-13 起）：以申请日 D 为锚，全周期 5 天——
    /// 公示起 = D+1、公示止 = D+3（恒 3 天）、验收日 = D+4（公示止次日历日）；
    /// 入户调查窗口 = [D, 公示起]（CycleStart = D、CycleEnd = 验收日）。
    /// 传入 <paramref name="publicityStart"/> 时以实际公示起为准（打印/存量档回放），公示止与验收日随之推导。
    /// 与月度批次（申请日 ≤ 当月13号归本月，仅作标签）解耦。
    /// </summary>
    TimelineResult BuildTempReliefCurve(DateTime applyDate, DateTime? publicityStart = null);

    /// <summary>
    /// 按申请日期归属 C 线<b>月度批次</b>标签：取申请日期所在月起第一个「申请日期 ≤ 该月验收日 A」的 C 线窗口；
    /// 超过当月验收日（13 号，非工作日顺延）→ 顺延下一个月，跨年自动接续（2026-10-20 口径，
    /// 废止 2026-10-18 的"不滚月 + 越界钳回"）。申请日期原样返回给表单，本方法不改写申请日期。
    /// 2026-11-13 起该结果仅用于批次标签与补打窗口 (上月A, 本月A]，不再承载公示/验收日期
    /// （公示/验收见逐笔 5 天曲线 <see cref="BuildTempReliefCurve"/>）。
    /// </summary>
    Task<TimelineResult> CalculateTempReliefForApplyDateAsync(DateTime applyDate);

    /// <summary>C线窗口（同步版）：供表单申请日期联动与保存校验在同步上下文中调用。</summary>
    TimelineResult CalculateTempReliefForApplyDate(DateTime applyDate);

    /// <summary>
    /// C线文书「入户调查时间」= clamp(今日的最近工作日, [申请日, 公示起])。
    /// 今日为工作日取今日；今日为周末/节假日往回取过去最近工作日；早于申请日（窗口未开启）取申请日；
    /// 晚于窗口截止（补打历史档案）兜底为公示起日（截止不规避，可能为休息日）。
    /// 存量「公示早于申请」倒挂档：起日钳到截止日，保证返回值不早于公示起。
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

    // C线参数（临时救助逐笔 5 天曲线，2026-11-13 起：公示期不再锚定每月 13 号）
    // 以申请日 D 为锚：公示起 = D+1、公示止 = D+3（恒 3 天）、验收日 = D+4（公示止次日历日）。
    private const int C_PUBLICITY_START_OFFSET = 1;      // 公示起 = 申请日 + 1（日历日）
    private const int C_PUBLICITY_DAYS = 3;              // 公示恒 3 天（起止均含端点）
    private const int C_ACCEPTANCE_OFFSET = 4;           // 验收日 = 申请日 + 4（= 公示止 + 1 日历日）

    // 月度批次/补打窗口锚点（仅作标签与补打过滤：申请日 ≤ 当月 13 号归本月；窗口 (上月A, 本月A]）
    private const int C_ACCEPTANCE_DAY = 13;
    private const int C_BATCH_START_OFFSET = -5;         // 批次区间展示起点 = 验收日 − 5（非工作日顺延）

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
            return publicizeEnd.Value.Date.AddDays(1);   // 公示止次日历日（存量 A−1 → A，零漂移）

        // 公示结束为空：回退当前C线窗口的验收日
        return GetCurrentTempReliefCore().AuditDate;
    }

    /// <inheritdoc />
    public TimelineResult BuildTempReliefCurve(DateTime applyDate, DateTime? publicityStart = null)
        => BuildTempReliefCurveCore(applyDate, publicityStart);

    /// <summary>
    /// 逐笔 5 天曲线纯计算（唯一实现，供服务与测试共用）：
    /// 公示起 = 传入值 ?? 申请日+1；公示止 = 公示起+2（恒 3 天）；验收日 = 公示止+1（日历日）；
    /// 入户调查窗口 = [申请日, 公示起]。
    /// </summary>
    public static TimelineResult BuildTempReliefCurveCore(DateTime applyDate, DateTime? publicityStart = null)
    {
        var d = applyDate.Date;
        var start = publicityStart?.Date ?? d.AddDays(C_PUBLICITY_START_OFFSET);
        var end = start.AddDays(C_PUBLICITY_DAYS - 1);   // 恒 +2 天（3 天整）
        var acceptance = start.AddDays(C_ACCEPTANCE_OFFSET - C_PUBLICITY_START_OFFSET); // 公示止 + 1；默认 = D+4

        return new TimelineResult
        {
            Year = d.Year,
            Month = d.Month,
            Type = TimelineType.TempRelief,
            CycleStartDate = d,
            CycleEndDate = acceptance,
            PublicityStartDate = start,
            PublicityEndDate = end,
            InvestigationStartDate = d,
            InvestigationDeadline = start,
            AuditDate = acceptance
        };
    }

    /// <inheritdoc />
    public DateTime GetTempReliefInvestigationPrintDate(TimelineResult cLine)
    {
        // 今日的最近工作日：工作日取今日，周末/节假日往回取（调查事实已完成，不倒填未来）
        var today = DateTime.Today;
        var recentWorkday = _holidayService.IsWorkDay(today)
            ? today
            : _holidayService.GetPreviousWorkDay(today);

        // 钳入窗口 [申请日, 公示起日]（逐笔曲线：调查不早于申请、不晚于公示起）：
        // 早于起日（窗口未开启）取起日；晚于截止（补打历史档案）兜底为截止日。
        var start = cLine.InvestigationStartDate.Date;
        var deadline = cLine.InvestigationDeadline.Date;
        // 存量倒挂档（历史规则允许公示早于申请）防御：起日不得晚于截止日
        if (start > deadline) start = deadline;
        return ClampInvestigationPrintDate(recentWorkday, start, deadline);
    }

    /// <summary>
    /// 入户调查打印值钳制（纯函数，唯一实现）：早于窗口起日取起日、晚于截止取截止、否则取当日。
    /// </summary>
    public static DateTime ClampInvestigationPrintDate(DateTime recentWorkday, DateTime windowStart, DateTime deadline)
    {
        if (recentWorkday < windowStart) return windowStart;
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
    /// 申请日归属窗口：从申请日所在月起，取第一个「申请日 ≤ 该月验收日 A」的 C 线窗口；
    /// 超过当月验收日 → 顺延下一个月（最多顺延 24 个月防御，仍不满足则回退申请日所在月）。
    /// 申请日期不在本方法改写——旧的"钳回入户调查窗口起日"会把 10-14 之后录入的申请回填成 10-08，
    /// 归属也永远停在本月，2026-10-20 起废止。
    /// </summary>
    private TimelineResult CalculateCLineForApplyDate(DateTime applyDate)
    {
        var date = applyDate.Date;
        var year = date.Year;
        var month = date.Month;
        for (var i = 0; i < 24; i++)
        {
            var line = CalculateCLine(year, month);
            if (date <= line.AuditDate.Date)
                return line;

            month++;
            if (month > 12) { month = 1; year++; }
        }
        return CalculateCLine(date.Year, date.Month);
    }

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

    /// <summary>
    /// 当前C线周期：今天超过验收日（CycleEndDate）时自动滚入下一个月
    /// ——与 CalculateCLineForApplyDate 同一口径（超验收日 A 即滚下月），
    /// 保证"当前时间轴"与"申请日期归属"不再分叉。
    /// </summary>
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
    /// C线月度批次窗口（仅作批次标签与补打过滤，2026-11-13 起不再承载公示/验收）：
    /// 验收日锚点 = 每月13号（非工作日顺延至其后最近工作日）；Cycle = [验收日前5天(顺延工作日), 验收日]。
    /// 公示/验收现行口径见逐笔 5 天曲线 <see cref="BuildTempReliefCurve"/>。
    /// </summary>
    private TimelineResult CalculateCLine(int year, int month)
    {
        // 验收日 = 13号；非工作日顺延至其后最近工作日（保证必为工作日）
        var acceptance = new DateTime(year, month, C_ACCEPTANCE_DAY);
        if (!_holidayService.IsWorkDay(acceptance))
            acceptance = _holidayService.GetNextWorkDay(acceptance);

        // 批次起点 = 验收日前5天（非工作日顺延至其后最近工作日），仅用于批次区间展示
        var batchStart = acceptance.AddDays(C_BATCH_START_OFFSET);
        if (!_holidayService.IsWorkDay(batchStart))
            batchStart = _holidayService.GetNextWorkDay(batchStart);
        if (batchStart > acceptance)
            batchStart = acceptance;

        return new TimelineResult
        {
            Year = year,
            Month = month,
            Type = TimelineType.TempRelief,
            CycleStartDate = batchStart,
            CycleEndDate = acceptance,
            AuditDate = acceptance
        };
    }
}
