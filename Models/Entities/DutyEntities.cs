using NewCosmos.Constants;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 节假日记录（nc_sys_holidays）
/// </summary>
public class SysHoliday
{
    public long Id { get; set; }
    public DateTime HolidayDate { get; set; }
    /// <summary>HOLIDAY=法定放假日 / MAKEUP=调休补班日</summary>
    public string DateType { get; set; } = DutyConstants.HolidayRowTypes.HOLIDAY;
    public string Name { get; set; } = string.Empty;
    public int? Year { get; set; }
    /// <summary>API / MANUAL / SEED</summary>
    public string Source { get; set; } = DutyConstants.Sources.MANUAL;
    public DateTime? UpdatedAt { get; set; }

    public string DateTypeName => DutyConstants.HolidayRowTypes.DisplayName(DateType);
    public string SourceName => DutyConstants.Sources.DisplayName(Source);
    public string DateDisplay => HolidayDate.ToString("yyyy-MM-dd");
    public string WeekDisplay => HolidayDate.ToString("dddd", new System.Globalization.CultureInfo("zh-CN"));

    /// <summary>是否为调休补班日（UI 徽章着色用）</summary>
    public bool IsMakeup => DateType == DutyConstants.HolidayRowTypes.MAKEUP;
}

/// <summary>
/// 值班成员（nc_duty_members）
/// </summary>
public class DutyMember
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    /// <summary>入职时间（空=不限制；值班日当天须已入职才参与轮转）</summary>
    public DateTime? JoinDate { get; set; }
    /// <summary>离职时间（空=在职；值班日当天须未离职才参与轮转）</summary>
    public DateTime? ExitDate { get; set; }
    public int SortOrder { get; set; }
    public string Remark { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// 成员分组归属（nc_duty_member_groups）
/// </summary>
public class DutyGroupAssignment
{
    public long Id { get; set; }
    public long MemberId { get; set; }
    public string GroupCode { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public string GroupName => DutyConstants.Groups.DisplayName(GroupCode);
}

/// <summary>
/// 值班成员视图（成员 + 分组归属，用于列表展示与编辑回填）
/// </summary>
public class DutyMemberView
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    /// <summary>入职时间（空=不限制）</summary>
    public DateTime? JoinDate { get; set; }
    /// <summary>离职时间（空=在职）</summary>
    public DateTime? ExitDate { get; set; }
    public int SortOrder { get; set; }
    public string Remark { get; set; } = string.Empty;
    public List<DutyGroupAssignment> Groups { get; set; } = new();

    /// <summary>所属组显示文本，如"领导组、男生组"</summary>
    public string GroupsDisplay => Groups.Count == 0
        ? "未分组"
        : string.Join("、", Groups.OrderBy(g => g.SortOrder).Select(g => g.GroupName));

    /// <summary>是否停用（UI 徽章用）</summary>
    public bool IsInactive => !IsActive;

    /// <summary>是否填写了电话（UI 显隐用）</summary>
    public bool HasPhone => !string.IsNullOrWhiteSpace(Phone);

    /// <summary>入职时间显示（空→"—"）</summary>
    public string JoinDateDisplay => JoinDate?.ToString("yyyy-MM-dd") ?? "—";

    /// <summary>离职时间显示（空→"在职"）</summary>
    public string ExitDateDisplay => ExitDate?.ToString("yyyy-MM-dd") ?? "在职";

    /// <summary>
    /// 指定值班日是否可参与轮转：启用 ∧ 已入职 ∧ 未离职（按日精确判定）。
    /// </summary>
    public bool AvailableOn(DateTime date)
    {
        if (!IsActive) return false;
        if (JoinDate.HasValue && JoinDate.Value > date) return false;
        if (ExitDate.HasValue && ExitDate.Value < date) return false;
        return true;
    }

    /// <summary>是否属于指定组</summary>
    public bool InGroup(string groupCode) => Groups.Any(g => g.GroupCode == groupCode);

    /// <summary>在指定组内的轮转顺序号（不在该组时返回 null）</summary>
    public int? GetGroupSortOrder(string groupCode) =>
        Groups.FirstOrDefault(g => g.GroupCode == groupCode)?.SortOrder;
}

/// <summary>
/// 值班成员保存请求（新建/编辑）
/// </summary>
public class DutyMemberSave
{
    /// <summary>0=新建，否则编辑指定成员</summary>
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    /// <summary>入职时间（空=不限制）</summary>
    public DateTime? JoinDate { get; set; }
    /// <summary>离职时间（空=在职）</summary>
    public DateTime? ExitDate { get; set; }
    public string Remark { get; set; } = string.Empty;
    /// <summary>所属组（新建时组内顺序=该组末尾追加；编辑时保留原有组内顺序，新增组追加末尾）</summary>
    public List<string> GroupCodes { get; set; } = new();
}

/// <summary>
/// 请假记录（nc_duty_leaves）
/// </summary>
public class DutyLeave
{
    public long Id { get; set; }
    public long MemberId { get; set; }
    /// <summary>请假开始日期（含）</summary>
    public DateTime StartDate { get; set; }
    /// <summary>请假结束日期（含）</summary>
    public DateTime EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
}

/// <summary>
/// 请假记录视图（含成员姓名，用于列表展示）
/// </summary>
public class DutyLeaveView
{
    public long Id { get; set; }
    public long MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }

    public string StartDateDisplay => StartDate.ToString("yyyy-MM-dd");
    public string EndDateDisplay => EndDate.ToString("yyyy-MM-dd");
    public string RangeDisplay => $"{StartDate:yyyy-MM-dd} ~ {EndDate:yyyy-MM-dd}";
    /// <summary>请假天数（含首尾）</summary>
    public int Days => (int)(EndDate.Date - StartDate.Date).TotalDays + 1;
    public bool IsOngoing => StartDate.Date <= DateTime.Today && EndDate.Date >= DateTime.Today;

    /// <summary>是否填写了事由（UI 显隐用）</summary>
    public bool HasReason => !string.IsNullOrWhiteSpace(Reason);
}

/// <summary>
/// 请假保存请求
/// </summary>
public class DutyLeaveSave
{
    public long MemberId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 班务调整记录（nc_duty_shift_changes）：串班(SWAP=相互对调两天的班)与代班(SUBSTITUTE=单向顶班一次)共用
/// </summary>
public class DutyShiftChange
{
    public long Id { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public long? FromMemberId { get; set; }
    public string FromMemberName { get; set; } = string.Empty;
    public DateTime DutyDate { get; set; }
    public long? ToMemberId { get; set; }
    public string ToMemberName { get; set; } = string.Empty;
    /// <summary>对方被换出的班日（仅 SWAP 有值；SUBSTITUTE 为空）</summary>
    public DateTime? ToDutyDate { get; set; }
    public string GroupCode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}

/// <summary>
/// 班务调整记录视图（列表展示）
/// </summary>
public class DutyShiftChangeView
{
    public long Id { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public long? FromMemberId { get; set; }
    public string FromMemberName { get; set; } = string.Empty;
    public DateTime DutyDate { get; set; }
    public long? ToMemberId { get; set; }
    public string ToMemberName { get; set; } = string.Empty;
    /// <summary>对方被换出的班日（仅 SWAP 有值）</summary>
    public DateTime? ToDutyDate { get; set; }
    public string GroupCode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? CreatedAt { get; set; }

    public string ChangeTypeName => DutyConstants.ChangeTypes.DisplayName(ChangeType);
    public string GroupName => DutyConstants.Groups.DisplayName(GroupCode);
    public string DutyDateDisplay => DutyDate.ToString("yyyy-MM-dd");
    public string ToDutyDateDisplay => ToDutyDate?.ToString("yyyy-MM-dd") ?? string.Empty;
    public string CreatedAtDisplay => CreatedAt?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;

    /// <summary>甲/乙方当前手机号（实时取自成员表，用于联系人展示）</summary>
    public string FromMemberPhone { get; set; } = string.Empty;
    public string ToMemberPhone { get; set; } = string.Empty;

    /// <summary>调整描述：串班="张三(09-25) 139xxx ↔ 李四(09-27) 188xxx"；代班="李四 代 张三(09-25) 139xxx"</summary>
    public string ChangeDirection => ChangeType == DutyConstants.ChangeTypes.SWAP
        ? $"{FromMemberName}{FormatPhone(FromMemberPhone)}({DutyDate:MM-dd}) ↔ {ToMemberName}{FormatPhone(ToMemberPhone)}({ToDutyDate:MM-dd})"
        : $"{ToMemberName}{FormatPhone(ToMemberPhone)} 代 {FromMemberName}{FormatPhone(FromMemberPhone)}({DutyDate:MM-dd})";

    private static string FormatPhone(string phone) =>
        string.IsNullOrWhiteSpace(phone) ? string.Empty : $" {phone.Trim()}";

    /// <summary>是否填写了事由（UI 显隐用）</summary>
    public bool HasReason => !string.IsNullOrWhiteSpace(Reason);
    /// <summary>可否撤销（UI 撤销按钮显隐）</summary>
    public bool CanCancel => !IsCancelled;

    /// <summary>重放状态（DutyConstants.ChangeReplayStatuses；由 GetShiftChangesAsync 实时计算，不落库）</summary>
    public string ReplayStatus { get; set; } = DutyConstants.ChangeReplayStatuses.Applied;
    /// <summary>状态角标文本（如"待生效（缺 2026-10）"）</summary>
    public string StatusText { get; set; } = "已生效";
    /// <summary>状态角标底色（#RRGGBB）</summary>
    public string StatusColorHex { get; set; } = "#2E7D32";
}

/// <summary>
/// 班务调整保存请求
/// </summary>
public class DutyShiftChangeSave
{
    /// <summary>调整类型：DutyConstants.ChangeTypes.SWAP / SUBSTITUTE</summary>
    public string ChangeType { get; set; } = string.Empty;
    /// <summary>甲方人员ID（串班=对调方一/代班=被代者）</summary>
    public long FromMemberId { get; set; }
    /// <summary>甲方班次日（限今天及以后）</summary>
    public DateTime DutyDate { get; set; }
    /// <summary>乙方人员ID（串班=对调方二/代班=代班者）</summary>
    public long ToMemberId { get; set; }
    /// <summary>乙方班次日（仅 SWAP 必填：乙方被换出的班日；SUBSTITUTE 为空）</summary>
    public DateTime? ToDutyDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 成员未来班次（串班/代班面板选班行）
/// </summary>
public class DutyFutureSchedule
{
    public long ScheduleId { get; set; }
    public DateTime DutyDate { get; set; }
    public string DateType { get; set; } = string.Empty;
    public string GroupCode { get; set; } = string.Empty;
    public long MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    /// <summary>该班次是否已被未撤销的串班/代班调整过</summary>
    public bool IsAdjusted { get; set; }

    public string DutyDateDisplay => DutyDate.ToString("yyyy-MM-dd");
    public string DateTypeName => DutyConstants.DateTypes.DisplayName(DateType);
    public string GroupName => DutyConstants.Groups.DisplayName(GroupCode);
    /// <summary>列表显示行：日期 + 类型（+已调整标记）</summary>
    public string Display => $"{DutyDate:yyyy-MM-dd} {DateTypeName}{(IsAdjusted ? "（已调整）" : string.Empty)}";
}

/// <summary>
/// 轮转游标（nc_duty_rotation_state）
/// </summary>
public class DutyRotationState
{
    public long Id { get; set; }
    public string GroupCode { get; set; } = string.Empty;
    public string DateType { get; set; } = string.Empty;
    public long? LastMemberId { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// 轮转游标视图（用于游标维护界面展示）
/// </summary>
public class DutyRotationStateView
{
    public string GroupCode { get; set; } = string.Empty;
    public string DateType { get; set; } = string.Empty;
    public long? LastMemberId { get; set; }
    public string LastMemberName { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }

    public string GroupName => DutyConstants.Groups.DisplayName(GroupCode);
    public string DateTypeName => DutyConstants.DateTypes.DisplayName(DateType);
    public string LastMemberDisplay => string.IsNullOrEmpty(LastMemberName) ? "（尚未轮转）" : LastMemberName;
}

/// <summary>
/// 值班班次（nc_duty_schedules）
/// </summary>
public class DutySchedule
{
    public long Id { get; set; }
    public DateTime DutyDate { get; set; }
    public string DateType { get; set; } = string.Empty;
    public string GroupCode { get; set; } = string.Empty;
    public long? MemberId { get; set; }
    /// <summary>轮转消耗成员ID（生成时写入，班务调整不修改——跨月接续与重生成确定性的事实依据）</summary>
    public long? RotationMemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    /// <summary>值班人当前手机号（LEFT JOIN 成员表实时取值；成员删除或未填时为空）</summary>
    public string Phone { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public bool IsAdjusted { get; set; }
    public string Remark { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }

    public string GroupName => DutyConstants.Groups.DisplayName(GroupCode);
    public string DateTypeName => DutyConstants.DateTypes.DisplayName(DateType);
}

/// <summary>
/// 值班班次单元格（月视图一行内的一格：某天某组）
/// </summary>
public class DutyScheduleCell
{
    /// <summary>班次ID（未生成时为 0）</summary>
    public long ScheduleId { get; set; }
    /// <summary>班次日期（用于班务调整跳转预填）</summary>
    public DateTime DutyDate { get; set; }
    /// <summary>值班人员ID（未生成或班次空缺时为 null）</summary>
    public long? MemberId { get; set; }
    public string GroupCode { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string MemberName { get; set; } = string.Empty;
    /// <summary>值班人当前手机号（实时取自成员表；未填时为空）</summary>
    public string Phone { get; set; } = string.Empty;
    /// <summary>是否被班务调整过（串班/代班/撤销重放，值班表显示"调"角标）</summary>
    public bool IsAdjusted { get; set; }
    /// <summary>是否为当天需要出班的组（中层在工作日/休息日不显示）</summary>
    public bool IsRequired { get; set; } = true;

    public string DisplayText => IsRequired
        ? (string.IsNullOrEmpty(MemberName) ? "—" : MemberName)
        : "·";
    public bool HasSchedule => ScheduleId > 0;
    /// <summary>是否有值班人（班务调整入口判空用）</summary>
    public bool HasMember => !string.IsNullOrEmpty(MemberName);
    /// <summary>是否已填手机号（UI 手机号行显隐）</summary>
    public bool HasPhone => !string.IsNullOrEmpty(Phone);
}

/// <summary>
/// 月视图行：某一天的值班安排
/// </summary>
public class DutyDayRow
{
    public DateTime DutyDate { get; set; }
    public string DateType { get; set; } = string.Empty;

    public string DayLabel => DutyDate.Day.ToString();
    public string DateDisplay => DutyDate.ToString("MM-dd");
    /// <summary>星期缩写（一/六/日），去「星期」前缀</summary>
    public string WeekLabel => DutyDate.ToString("ddd", new System.Globalization.CultureInfo("zh-CN")).Replace("星期", string.Empty);
    /// <summary>法定节假日名称（如 春节/元旦；非节假日为空），用于打印表"{星期与农历N}"以节日名替代星期</summary>
    public string HolidayName { get; set; } = string.Empty;
    public string DateTypeName => DutyConstants.DateTypes.DisplayName(DateType);
    public bool IsRestDay => DateType == DutyConstants.DateTypes.RESTDAY;
    public bool IsHoliday => DateType == DutyConstants.DateTypes.HOLIDAY;
    public bool IsWorkday => DateType == DutyConstants.DateTypes.WORKDAY;

    /// <summary>该日各组的值班单元格（顺序与 DutyConstants.Groups.DisplayOrder 一致）</summary>
    public List<DutyScheduleCell> Cells { get; set; } = new();

    /// <summary>便捷访问：领导组单元格（XAML 固定列绑定用）</summary>
    public DutyScheduleCell LeaderCell => GetCell(DutyConstants.Groups.LEADER) ?? new DutyScheduleCell { GroupCode = DutyConstants.Groups.LEADER, GroupName = DutyConstants.Groups.DisplayName(DutyConstants.Groups.LEADER) };

    /// <summary>便捷访问：中层管理组单元格</summary>
    public DutyScheduleCell MiddleCell => GetCell(DutyConstants.Groups.MIDDLE) ?? new DutyScheduleCell { GroupCode = DutyConstants.Groups.MIDDLE, GroupName = DutyConstants.Groups.DisplayName(DutyConstants.Groups.MIDDLE), IsRequired = false };

    /// <summary>便捷访问：男生组单元格</summary>
    public DutyScheduleCell MaleCell => GetCell(DutyConstants.Groups.MALE) ?? new DutyScheduleCell { GroupCode = DutyConstants.Groups.MALE, GroupName = DutyConstants.Groups.DisplayName(DutyConstants.Groups.MALE) };

    /// <summary>便捷访问：女生组单元格</summary>
    public DutyScheduleCell FemaleCell => GetCell(DutyConstants.Groups.FEMALE) ?? new DutyScheduleCell { GroupCode = DutyConstants.Groups.FEMALE, GroupName = DutyConstants.Groups.DisplayName(DutyConstants.Groups.FEMALE) };

    public DutyScheduleCell? GetCell(string groupCode) =>
        Cells.FirstOrDefault(c => c.GroupCode == groupCode);

    /// <summary>该日已安排的班次（有 ScheduleId 的单元格）</summary>
    public IEnumerable<DutyScheduleCell> ScheduledCells => Cells.Where(c => c.HasSchedule);
}

/// <summary>
/// 月度值班表视图
/// </summary>
public class DutyMonthSchedule
{
    public int Year { get; set; }
    public int Month { get; set; }
    /// <summary>该月是否已生成固化班次</summary>
    public bool IsGenerated { get; set; }
    /// <summary>休息日带班领导两天同人开关（生成语义）</summary>
    public bool RestdayLeaderSamePair { get; set; }
    public List<DutyDayRow> Days { get; set; } = new();

    public string Title => $"{Year} 年 {Month} 月值班表";
    public string GenerationStatusText => IsGenerated ? "已生成" : "未生成";
    public int ScheduleCount => Days.Sum(d => d.ScheduledCells.Count());
}

/// <summary>
/// 值班表生成结果统计
/// </summary>
public class DutyGenerationSummary
{
    public int WorkdayCount { get; set; }
    public int RestdayCount { get; set; }
    public int HolidayCount { get; set; }
    public int ScheduleCount { get; set; }
    /// <summary>同人一天一班冲突导致顺延的次数</summary>
    public int ConflictSkippedCount { get; set; }

    /// <summary>请假/入职离职边界导致顺延的次数</summary>
    public int UnavailableSkippedCount { get; set; }

    /// <summary>轮转锚点起始日（>1 表示本月自该日起生成；0=整月生成）</summary>
    public int AnchorDay { get; set; }

    /// <summary>本次生成中重放成功的串班/代班条数</summary>
    public int ReplayedChangeCount { get; set; }

    /// <summary>因对方月份未生成而待生效的串班/代班条数</summary>
    public int PendingChangeCount { get; set; }

    /// <summary>待生效缺的月份（yyyy-MM，升序去重；供"补齐生成"用）</summary>
    public List<string> PendingChangeMonths { get; set; } = new();

    /// <summary>因轮转已变化（或命中请假）而跳过重放的条数（需人工改期/撤销）</summary>
    public int SkippedChangeCount { get; set; }
}

/// <summary>
/// 节假日接口下载结果统计
/// </summary>
public class HolidayDownloadSummary
{
    public int Year { get; set; }
    public int HolidayCount { get; set; }
    public int MakeupCount { get; set; }
}

/// <summary>
/// 值班成员批量导入 DTO（队列名单模式：每组一列名单，从上到下即轮转顺序；全量对齐语义）
/// </summary>

/// <summary>单个组的导入名单条目：显式序号 + 姓名</summary>
public class DutyRosterEntry
{
    /// <summary>组内序号（即轮转 sort_order；按值升序轮转，允许跳号，组内不得重复）</summary>
    public int Seq { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>单个组的导入名单（条目按序号升序 = 轮转顺序）</summary>
public class DutyGroupRoster
{
    public string GroupCode { get; set; } = string.Empty;
    /// <summary>名单条目（序号 + 姓名）</summary>
    public List<DutyRosterEntry> Entries { get; set; } = new();

    public string GroupName => DutyConstants.Groups.DisplayName(GroupCode);
    public int MemberCount => Entries.Count;
    /// <summary>顺位预览（按序号升序），如 "1.张三、2.李四、3.王五…"</summary>
    public string NamesPreview => string.Join("、",
        Entries.OrderBy(e => e.Seq).Take(10).Select(e => $"{e.Seq}.{e.Name}"))
        + (Entries.Count > 10 ? "…" : string.Empty);
}

/// <summary>
/// 值班成员导入预览：各组名单 + 与库内现状的差异汇总
/// </summary>
public class DutyMemberImportPreview
{
    public List<DutyGroupRoster> Rosters { get; set; } = new();
    /// <summary>去重后 Excel 名单总人数</summary>
    public int TotalMembers { get; set; }
    /// <summary>库内无、名单有 → 将新增</summary>
    public int NewCount { get; set; }
    /// <summary>库内有且在名单中 → 将对齐归属/顺序</summary>
    public int UpdateCount { get; set; }
    /// <summary>库内在组但名单未列 → 将从组移除的 (成员, 组) 次数</summary>
    public int RemoveCount { get; set; }
    /// <summary>整列名单为空 = 该组将被清空（强确认警示）</summary>
    public List<string> ClearedGroupNames { get; set; } = new();
    /// <summary>校验/解析错误（列内重复姓名等）</summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>轮转锚点起始日期（轮转设置页；空=未设置，导入不动游标）</summary>
    public DateTime? AnchorDate { get; set; }

    /// <summary>各轮转线的当前序列：组编码 → (日期类型 → 下个值班的成员名；线留空=从名单头开始)</summary>
    public Dictionary<string, Dictionary<string, string>> CurrentSeqByLine { get; set; } = new();

    public bool IsValid => Errors.Count == 0;
    public string SummaryText => $"名单共 {TotalMembers} 人：新增 {NewCount} / 对齐 {UpdateCount} / 移出 {RemoveCount} 处归属"
        + (ClearedGroupNames.Count > 0 ? $"；⚠ 将清空组：{string.Join("、", ClearedGroupNames)}" : string.Empty)
        + (AnchorDate.HasValue ? $"；锚点 {AnchorDate:yyyy-MM-dd} 起按当前序列轮转" : string.Empty);
}

/// <summary>
/// 值班成员导入执行结果（全量对齐后的变更报告）
/// </summary>
public class DutyMemberImportResult
{
    public bool Success { get; set; }
    public int NewCount { get; set; }
    public int UpdateCount { get; set; }
    /// <summary>从组移除的归属次数</summary>
    public int RemoveCount { get; set; }
    /// <summary>已设置的轮转锚点（空=本次导入未设置）</summary>
    public DateTime? AnchorDate { get; set; }
    public List<string> Errors { get; set; } = new();
    public string Message { get; set; } = string.Empty;

    public string SummaryText => $"导入完成：新增 {NewCount} 人，对齐 {UpdateCount} 人，移出 {RemoveCount} 处归属"
        + (AnchorDate.HasValue ? $"；轮转锚点已设为 {AnchorDate:yyyy-MM-dd}（生成该月值班表时将从此日起）" : string.Empty);
}
