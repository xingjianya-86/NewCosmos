using NewCosmos.Services.Core;

namespace NewCosmos.Services.Utilities;

/// <summary>
/// 节假日服务：判定法定节假日 / 调休补班日 / 工作日。
/// 数据源为内存不可变快照（默认内置 2026 种子兜底），由
/// HolidayManageService.RefreshCacheAsync 从 nc_sys_holidays 表加载后整体替换；
/// 快照为空时退化为"周末=假日"判定（与历史行为一致）。
/// 注意：本服务的同步 API 签名被 BusinessTimelineService 等多处消费方依赖，禁止改为异步。
/// </summary>
public interface IHolidayService
{
    /// <summary>是否为假日（法定节假日，或周末且非调休补班日）</summary>
    bool IsHoliday(DateTime date);

    /// <summary>是否为工作日（非假日）</summary>
    bool IsWorkDay(DateTime date);

    DateTime GetNextWorkDay(DateTime date);
    DateTime GetPreviousWorkDay(DateTime date);
    bool IsWeekend(DateTime date);

    /// <summary>是否为国务院规定的法定放假日（含调休连放的周末，以 nc_sys_holidays 数据为准）</summary>
    bool IsLegalHoliday(DateTime date);

    /// <summary>是否为调休补班日（周末但需要上班）</summary>
    bool IsMakeupWorkday(DateTime date);

    /// <summary>
    /// 整体替换节假日快照（volatile 引用替换，天然线程安全）。
    /// 由 HolidayManageService 在数据库节假日数据更新后调用。
    /// </summary>
    void ReplaceSnapshot(IReadOnlyCollection<DateOnly> legalHolidays, IReadOnlyCollection<DateOnly> makeupWorkdays,
        IReadOnlyDictionary<DateOnly, string>? legalHolidayNames = null);

    /// <summary>法定放假日对应节日名（如 "春节"/"元旦"）；非放假日或名称缺失返回 null</summary>
    string? GetLegalHolidayName(DateTime date);

    /// <summary>当前快照是否为内置种子兜底（数据库加载尚未发生）</summary>
    bool IsUsingFallbackSnapshot { get; }
}

public class HolidayService : BaseService, IHolidayService
{
    protected override string ServiceName => "HolidayService";

    public HolidayService(ILoggerService logger) : base(logger)
    {
        _snapshot = BuildFallbackSnapshot();
    }

    #region 快照

    /// <summary>
    /// 不可变节假日快照：构造时预建查找集合，volatile 引用整体替换。
    /// </summary>
    private sealed class HolidaySnapshot
    {
        public static readonly HolidaySnapshot Empty = new(
            new DateOnly[] { }, new DateOnly[] { }, null, isFallback: false);

        public HolidaySnapshot(IReadOnlyCollection<DateOnly> legalHolidays, IReadOnlyCollection<DateOnly> makeupWorkdays,
            IReadOnlyDictionary<DateOnly, string>? legalHolidayNames, bool isFallback)
        {
            LegalHolidays = legalHolidays;
            MakeupWorkdays = makeupWorkdays;
            IsFallback = isFallback;
            HolidaySet = legalHolidays.ToHashSet();
            MakeupSet = makeupWorkdays.ToHashSet();
            HolidayNames = legalHolidayNames ?? new Dictionary<DateOnly, string>();
        }

        public IReadOnlyCollection<DateOnly> LegalHolidays { get; }
        public IReadOnlyCollection<DateOnly> MakeupWorkdays { get; }
        public bool IsFallback { get; }
        public HashSet<DateOnly> HolidaySet { get; }
        public HashSet<DateOnly> MakeupSet { get; }
        public IReadOnlyDictionary<DateOnly, string> HolidayNames { get; }
    }

    private volatile HolidaySnapshot _snapshot;

    public bool IsUsingFallbackSnapshot => _snapshot.IsFallback;

    /// <inheritdoc />
    public void ReplaceSnapshot(IReadOnlyCollection<DateOnly> legalHolidays, IReadOnlyCollection<DateOnly> makeupWorkdays,
        IReadOnlyDictionary<DateOnly, string>? legalHolidayNames = null)
    {
        _snapshot = new HolidaySnapshot(
            legalHolidays?.ToArray() ?? new DateOnly[] { },
            makeupWorkdays?.ToArray() ?? new DateOnly[] { },
            legalHolidayNames,
            isFallback: false);
        LogInfo($"节假日快照已更新: 放假日 {legalHolidays?.Count ?? 0} 天, 补班日 {makeupWorkdays?.Count ?? 0} 天");
    }

    /// <inheritdoc />
    public string? GetLegalHolidayName(DateTime date)
    {
        var d = DateOnly.FromDateTime(date);
        return _snapshot.HolidayNames.TryGetValue(d, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : null;
    }

    #endregion

    #region 内置种子兜底（2026 年，数据来源 timor.tech / 国务院办公厅安排）

    /// <summary>
    /// 内置 2026 年法定节假日（快照为空时兜底，语义与历史硬编码一致）。
    /// </summary>
    private static readonly DateOnly[] FallbackHolidays2026 =
    {
        // 元旦
        new(2026, 1, 1), new(2026, 1, 2), new(2026, 1, 3),
        // 春节（2/15-2/23）
        new(2026, 2, 15), new(2026, 2, 16), new(2026, 2, 17), new(2026, 2, 18),
        new(2026, 2, 19), new(2026, 2, 20), new(2026, 2, 21), new(2026, 2, 22), new(2026, 2, 23),
        // 清明节（4/4-4/6）
        new(2026, 4, 4), new(2026, 4, 5), new(2026, 4, 6),
        // 劳动节（5/1-5/5）
        new(2026, 5, 1), new(2026, 5, 2), new(2026, 5, 3), new(2026, 5, 4), new(2026, 5, 5),
        // 端午节（6/19-6/21）
        new(2026, 6, 19), new(2026, 6, 20), new(2026, 6, 21),
        // 中秋节（9/25-9/27）
        new(2026, 9, 25), new(2026, 9, 26), new(2026, 9, 27),
        // 国庆节（10/1-10/7）
        new(2026, 10, 1), new(2026, 10, 2), new(2026, 10, 3),
        new(2026, 10, 4), new(2026, 10, 5), new(2026, 10, 6), new(2026, 10, 7),
    };

    /// <summary>内置 2026 年调休补班日（周末上班）</summary>
    private static readonly DateOnly[] FallbackMakeupWorkdays2026 =
    {
        new(2026, 1, 4),   // 元旦后补班
        new(2026, 2, 14),  // 春节前补班
        new(2026, 2, 28),  // 春节后补班
        new(2026, 5, 9),   // 劳动节后补班
        new(2026, 9, 20),  // 中秋节前补班
        new(2026, 10, 10), // 国庆节后补班
    };

    private static HolidaySnapshot BuildFallbackSnapshot() =>
        new(FallbackHolidays2026, FallbackMakeupWorkdays2026, legalHolidayNames: null, isFallback: true);

    #endregion

    #region 判定 API

    public bool IsLegalHoliday(DateTime date) => _snapshot.HolidaySet.Contains(DateOnly.FromDateTime(date));

    public bool IsMakeupWorkday(DateTime date) => _snapshot.MakeupSet.Contains(DateOnly.FromDateTime(date));

    public bool IsWeekend(DateTime date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public bool IsHoliday(DateTime date)
    {
        var d = DateOnly.FromDateTime(date);
        var snapshot = _snapshot;
        if (snapshot.HolidaySet.Contains(d)) return true;
        // 周末默认为假日，调休补班日除外
        if (IsWeekend(date) && !snapshot.MakeupSet.Contains(d)) return true;
        return false;
    }

    public bool IsWorkDay(DateTime date) => !IsHoliday(date);

    public DateTime GetNextWorkDay(DateTime date)
    {
        var next = date.AddDays(1);
        while (!IsWorkDay(next)) next = next.AddDays(1);
        return next;
    }

    public DateTime GetPreviousWorkDay(DateTime date)
    {
        var prev = date.AddDays(-1);
        while (!IsWorkDay(prev)) prev = prev.AddDays(-1);
        return prev;
    }

    #endregion
}
