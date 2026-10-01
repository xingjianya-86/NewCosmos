using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Domain.SocialAssistance;

namespace NewCosmos.Services.Domain.Reporting;

/// <summary>
/// 月报表服务接口
/// </summary>
public interface IMonthlyReportService
{
    /// <summary>
    /// 生成月报表（按 B 线周期：[上月(结算日+1)日, 本月(结算日+1)日)，默认结算日 15）。已存在则先删除同 (year,month) 记录再插入（支持覆盖重新生成）。
    /// </summary>
    Task<Result<MonthlyReport>> GenerateAsync(int year, int month, string town, CancellationToken ct = default);

    /// <summary>
    /// 获取月报表
    /// </summary>
    Task<Result<MonthlyReport>> GetAsync(int year, int month, CancellationToken ct = default);

    /// <summary>
    /// 获取报表列表
    /// </summary>
    Task<Result<List<MonthlyReport>>> GetListAsync(int? year = null, CancellationToken ct = default);

    /// <summary>
    /// 导出报表
    /// </summary>
    Task<Result<byte[]>> ExportAsync(int year, int month, string format = "Excel", CancellationToken ct = default);

    /// <summary>
    /// 获取统计数据（基于 nc_biz_applications 状态流转口径）
    /// </summary>
    Task<Result<MonthlyStatistics>> GetStatisticsAsync(int year, int month, CancellationToken ct = default);

    /// <summary>
    /// 获取报表摘要列表
    /// </summary>
    Task<Result<PagedResult<MonthlyReportSummary>>> GetSummaryListAsync(int pageIndex, int pageSize, int? year = null, CancellationToken ct = default);

    /// <summary>
    /// 获取报表历史
    /// </summary>
    Task<Result<List<MonthlyReportSummary>>> GetReportHistoryAsync(int? year = null, CancellationToken ct = default);

    /// <summary>
    /// 获取报表历史（带筛选）
    /// </summary>
    Task<Result<List<MonthlyReportSummary>>> GetReportHistoryAsync(int? year, int? month, string town, string status, CancellationToken ct = default);

    /// <summary>
    /// 获取报表历史（分页）
    /// </summary>
    Task<Result<PagedResult<MonthlyReportSummary>>> GetReportHistoryAsync(int? year, int? month, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 删除指定年月的月报记录（历史页）
    /// </summary>
    Task<Result<bool>> DeleteAsync(int year, int month, CancellationToken ct = default);

    /// <summary>
    /// 获取统计周期范围（B 线：[上月(结算日+1)日, 本月(结算日+1)日)，默认结算日 15）
    /// </summary>
    Task<Result<MonthlyCycleRange>> GetCycleRangeAsync(int year, int month, CancellationToken ct = default);

    /// <summary>
    /// 乡镇筛选选项（有业务数据的乡镇去重列表，用于下拉）
    /// </summary>
    Task<Result<List<string>>> GetTownOptionsAsync(CancellationToken ct = default);

    /// <summary>
    /// 新增救助明细（周期内 status=Approved 且 first_approved_at∈周期的申请）
    /// </summary>
    Task<Result<List<MonthlyAddedRow>>> GetAddedRowsAsync(int year, int month, string town, string? category = null, CancellationToken ct = default);

    /// <summary>
    /// 停保汇总（status=ApplicationStatusCodes.STOPPED 且 stop_date∈周期的申请）
    /// </summary>
    Task<Result<List<MonthlyStoppedRow>>> GetStoppedRowsAsync(int year, int month, string town, string? category = null, CancellationToken ct = default);

    /// <summary>
    /// 保障金增发表（FundChange 且 old&lt;new，仅低保）
    /// </summary>
    Task<Result<List<MonthlyAmountChangeRow>>> GetIncreaseRowsAsync(int year, int month, string town, string? category = null, CancellationToken ct = default);

    /// <summary>
    /// 保障金减发表（FundChange 且 old&gt;new，仅低保）
    /// </summary>
    Task<Result<List<MonthlyAmountChangeRow>>> GetDecreaseRowsAsync(int year, int month, string town, string? category = null, CancellationToken ct = default);

    /// <summary>
    /// 分类施保金减发（低保导入库中本月年满 18 周岁的成员，成员级名单）
    /// </summary>
    Task<Result<List<MonthlyShiBaoRow>>> GetShiBaoReductionRowsAsync(int year, int month, CancellationToken ct = default);

    /// <summary>
    /// 分类施保增发（低保导入库中下月年满 60 周岁且未享受分类施保的成员所在家庭，户级每户一页）。
    /// 首次调用实时计算并落库快照（nc_biz_monthly_classified_adds），当月已落库后直接读库返回（锁死快照，重复打印一致）。
    /// </summary>
    Task<Result<List<MonthlyClassifiedAddRow>>> GetClassifiedSubsidyAddRowsAsync(int year, int month, CancellationToken ct = default);

    /// <summary>
    /// 显式落库分类施保增发明细快照（幂等：同 year+month+applicant_id_card 冲突则忽略）。返回是否产生新写入。
    /// </summary>
    Task<Result<bool>> SaveClassifiedSubsidyAddsAsync(int year, int month, string town, List<MonthlyClassifiedAddRow> rows, CancellationToken ct = default);

    /// <summary>
    /// 分类施保增发明细历史溯源（读 nc_biz_monthly_classified_adds 快照，按 applicant_name 排序）
    /// </summary>
    Task<Result<List<MonthlyClassifiedAddRow>>> GetClassifiedSubsidyAddsHistoryAsync(int year, int month, CancellationToken ct = default);

    /// <summary>
    /// 人员变动_自然减员月报表（nc_biz_death_records 死亡日期∈周期）
    /// </summary>
    Task<Result<List<MonthlyDeathRow>>> GetDeathRowsAsync(int year, int month, string town, string? category = null, CancellationToken ct = default);

    /// <summary>
    /// 退出对象兜底纠治表（本月停保 + 渐退期满 + 经济复核降档退出对象；核查渐退期/纳入低保/特困/边缘/刚性/直接退出）
    /// </summary>
    Task<Result<List<MonthlyExitRectificationRow>>> GetExitRectificationRowsAsync(int year, int month, string town, CancellationToken ct = default);

    /// <summary>
    /// 临时救助新增汇总（status=ApplicationStatusCodes.CONFIRMED 且 confirmed_at∈周期的临时救助申请，户级名单）
    /// </summary>
    Task<Result<List<MonthlyTempReliefRow>>> GetTempReliefSummaryRowsAsync(int year, int month, string town, CancellationToken ct = default);

    /// <summary>
    /// 获取会议记录（year/month/town 唯一；不存在时返回含自动带出新增成员信息的空记录）。
    /// specialApproval=true 时（一事一议模板）仅带出特殊审批关联申请，普通会议记录排除特殊审批。
    /// </summary>
    Task<Result<MonthlyMeeting>> GetMeetingAsync(int year, int month, string town, bool specialApproval = false, CancellationToken ct = default);

    /// <summary>
    /// 保存会议记录（存在则更新，不存在则插入）
    /// </summary>
    Task<Result<MonthlyMeeting>> SaveMeetingAsync(MonthlyMeeting meeting, CancellationToken ct = default);

    /// <summary>
    /// 保存会议记录出席/缺席历史快照（每次生成时自动存一条）
    /// </summary>
    Task<Result<bool>> SaveMeetingAttendanceHistoryAsync(int year, int month, string town, string attendees, string absentees, string createdBy, CancellationToken ct = default);

    /// <summary>
    /// 获取最新一条出席/缺席历史（默认回填弹窗；无记录返回空快照）
    /// </summary>
    Task<Result<MonthlyMeetingAttendance>> GetLatestMeetingAttendanceAsync(int year, int month, string town, CancellationToken ct = default);
}

/// <summary>
/// 会议记录出席/缺席历史快照
/// </summary>
public class MonthlyMeetingAttendance
{
    public long Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string Town { get; set; } = "全部";
    public string Attendees { get; set; } = string.Empty;
    public string Absentees { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 月报表摘要（用于列表展示）
/// </summary>
public class MonthlyReportSummary
{
    public long Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string Town { get; set; } = string.Empty;
    public int TotalArchives { get; set; }
    public int NewArchives { get; set; }
    public int StoppedArchives { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Generated";
    public DateTime GeneratedAt { get; set; }
    public string GeneratedBy { get; set; } = string.Empty;
}

/// <summary>
/// 月报表实体
/// </summary>
public class MonthlyReport
{
    public long Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string Town { get; set; } = string.Empty;
    public string ReportType { get; set; } = "Monthly";
    public string Status { get; set; } = "Generated";
    public int TotalArchives { get; set; }
    public int NewArchives { get; set; }
    public int StoppedArchives { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal NewAmount { get; set; }
    public int RuralSubsistenceCount { get; set; }
    public int UrbanSubsistenceCount { get; set; }
    public int RuralLowIncomeCount { get; set; }
    public int UrbanLowIncomeCount { get; set; }
    public int RuralDestituteCount { get; set; }
    public int UrbanDestituteCount { get; set; }
    public int RigidExpenditureCount { get; set; }
    public string GeneratedBy { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 月度统计数据
/// </summary>
public class MonthlyStatistics
{
    public int TotalArchives { get; set; }
    public int NewArchives { get; set; }
    public int StoppedArchives { get; set; }
    public int ChangedArchives { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AverageAmount { get; set; }
    public Dictionary<string, int> ClassificationCounts { get; set; } = new();
    public Dictionary<string, decimal> ClassificationAmounts { get; set; } = new();
}

/// <summary>
/// 统计周期范围（B 线）
/// </summary>
public class MonthlyCycleRange
{
    /// <summary>周期起始（含）：上月(结算日+1) 日 00:00（默认上月16日）</summary>
    public DateTime Start { get; set; }

    /// <summary>周期结束（不含）：本月(结算日+1) 日 00:00（默认本月16日）</summary>
    public DateTime End { get; set; }

    /// <summary>显示文本，如 2026-07-16 ~ 2026-08-15（结算日=15）</summary>
    public string Display => $"{Start:yyyy-MM-dd} ~ {End.AddDays(-1):yyyy-MM-dd}";
}

/// <summary>
/// 新增救助明细行（表 1）
/// </summary>
public class MonthlyAddedRow
{
    public long ApplicationId { get; set; }                    // 申请ID（一事一议标记用）
    public string AuditDate { get; set; } = string.Empty;   // 审批时间（=首次审批 first_approved_at；跨类新增行为 change_date）
    public string Name { get; set; } = string.Empty;         // 户主姓名
    public string IdCard { get; set; } = string.Empty;       // 身份证号
    public string Gender { get; set; } = string.Empty;       // 性别
    public string Address { get; set; } = string.Empty;      // 家庭住址（完整拼接）
    public string Town { get; set; } = string.Empty;         // 乡镇（会议记录村名前缀用，如"柳树镇"）
    public string Community { get; set; } = string.Empty;    // 行政村/社区（村名前缀用，去"村委会"后缀）
    public string FamilySize { get; set; } = string.Empty;   // 享受人口
    public string Classification { get; set; } = string.Empty; // 享受分类（显示名）
    public string CategoryCode { get; set; } = string.Empty;   // 分类代码（过滤/分页用）
    public bool IsRural { get; set; }                          // 农村=城市/农村双页入口
    public string ReasonDetail { get; set; } = string.Empty;   // 家庭情况说明（申请原因详情长文）
    public string MaritalStatus { get; set; } = string.Empty;  // 婚姻状况（低收入模板，中文）
    public string HealthStatus { get; set; } = string.Empty;   // 健康状况（低收入模板，中文）
    public string BankAccount { get; set; } = string.Empty;    // 银行账号（低收入模板）
    public decimal AnnualIncome { get; set; }                  // 年总收入（低收入模板）
    public string Age { get; set; } = string.Empty;            // 年龄（低收入模板，身份证实算）
    public string Relation { get; set; } = string.Empty;       // 家庭关系（低收入模板，取户主关系）
    public FamilySituationContext? FamilyContext { get; set; }   // 现场重建"家庭情况说明"的数据快照
}

/// <summary>
/// 停保汇总行（表 2）
/// </summary>
public class MonthlyStoppedRow
{
    public string HeadName { get; set; } = string.Empty;
    public string HeadIdCard { get; set; } = string.Empty;
    public string HeadBirth { get; set; } = string.Empty;   // 出生日期（身份证提取）
    public string FamilySize { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;     // 享受类别（低保/低收入…）
    public string Classification { get; set; } = string.Empty; // 户分类施保（显示名）
    public string MonthAmount { get; set; } = string.Empty;  // 户月保障金
    public string StopReason { get; set; } = string.Empty;   // 停止原因
    public string StopDate { get; set; } = string.Empty;     // 停止日期
    public string EnjoyDate { get; set; } = string.Empty;    // 享受日期（=审批时间 first_approved_at）
    public string CategoryCode { get; set; } = string.Empty;

    /// <summary>业务类别：停止 / 减员（最低生活保障边缘家庭"停止、减员汇总表"专用列）</summary>
    public string BizType { get; set; } = string.Empty;

    public bool IsRural { get; set; }
}

/// <summary>
/// 增减发行（表 3/4）
/// </summary>
public class MonthlyAmountChangeRow
{
    public string HeadName { get; set; } = string.Empty;
    public string HeadBirth { get; set; } = string.Empty;
    public string FamilySize { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string EnjoyDate { get; set; } = string.Empty;   // 享受时间（=审批时间 first_approved_at）
    public string OldAmount { get; set; } = string.Empty;
    public string NewAmount { get; set; } = string.Empty;
    public string Classification { get; set; } = string.Empty; // 新分类施保显示名
    public string ChangeAmount { get; set; } = string.Empty;   // 增减金额绝对值
    public string Reason { get; set; } = string.Empty;         // 增减理由（change_reason）
    public int DecreasePerson { get; set; }                    // 减发人口数（快照 FamilySize 差，减发表专用）
    public decimal ClassifiedSubsidyAmount { get; set; }       // 现分类施保金额（减发表专用）
    public string OldFamilySize { get; set; } = string.Empty;  // 原家庭人口（从老档案/死亡前 Before 快照计算，减发表专用）
    public string CategoryCode { get; set; } = string.Empty;
    public bool IsRural { get; set; }
}

/// <summary>
/// 施保金减发行（低保导入库本月年满 18 周岁的成员，成员级）
/// </summary>
public class MonthlyShiBaoRow
{
    public string HeadName { get; set; } = string.Empty;        // 享受户主
    public string MemberName { get; set; } = string.Empty;      // 减发成员姓名
    public string Address { get; set; } = string.Empty;         // 家庭住址（完整拼接）
    public string Category { get; set; } = string.Empty;        // 享受类别（农村低保/城市低保）
    public string EnjoyDate { get; set; } = string.Empty;       // 享受日期（=审批时间 first_approved_at）
    public string OriginalAmount { get; set; } = string.Empty;  // 原享受低保金
    public string OriginalClassified { get; set; } = string.Empty; // 原享受分类施保
    public string Reason { get; set; } = string.Empty;          // 减发分类施保理由
    public string DecreaseAmount { get; set; } = string.Empty;  // 减发金额
    public string CurrentAmount { get; set; } = string.Empty;   // 现享受低保金
    public string CurrentClassified { get; set; } = string.Empty; // 现享受分类施保
    public bool IsRural { get; set; }
}

/// <summary>
/// 分类施保增发户（低保导入库下月年满 60 周岁且未享受分类施保的成员所在家庭，户级每户一页）
/// </summary>
public class MonthlyClassifiedAddRow
{
    public string ApplicantName { get; set; } = string.Empty;      // 户主姓名
    public string ApplicantGender { get; set; } = string.Empty;    // 户主性别
    public string ApplicantBirthDate { get; set; } = string.Empty; // 户主出生日期（yyyy年MM月dd日）
    public string Nationality { get; set; } = string.Empty;        // 民族（中文）
    public string HealthStatus { get; set; } = string.Empty;       // 户主健康状况（中文）
    public string Classification { get; set; } = string.Empty;     // 救助类别（农村低保/城市低保）
    public string FamilySize { get; set; } = string.Empty;         // 家庭人口
    public string Phone { get; set; } = string.Empty;              // 联系电话
    public string Address { get; set; } = string.Empty;            // 家庭住址
    public string ApplicantIdCard { get; set; } = string.Empty;    // 身份证号
    public string ClassifiedType { get; set; } = string.Empty;       // 分类施保类型（重病/重残/高龄/未成年，去重顿号连接；由 GetClassifiedSubsidyAddRowsAsync 按成员实际判定填充）
    public List<MonthlyClassifiedMember> Members { get; set; } = new(); // 家庭成员 1~6
    public int Count { get; set; }                                 // 分类施保符合人数
    public decimal Amount { get; set; }                            // 分类施保标准加发额度
    public bool IsRural { get; set; }                              // 是否农村低保（落库/读库用）
}

/// <summary>
/// 分类施保增发户家庭成员明细（模板 15 家庭成员 1~6 行）
/// </summary>
public class MonthlyClassifiedMember
{
    public string Name { get; set; } = string.Empty;       // 姓名
    public string Gender { get; set; } = string.Empty;     // 性别
    public string BirthDate { get; set; } = string.Empty;  // 出生日期（yyyy年MM月dd日）
    public string Relation { get; set; } = string.Empty;   // 与户主关系（中文）
    public string Health { get; set; } = string.Empty;     // 健康状况（中文）
    public decimal MonthlyIncome { get; set; }             // 成员月收入（年值/12）
    public decimal AnnualIncome { get; set; }              // 成员年收入（与档案模板15 收入口径一致）
}

/// <summary>
/// 自然减员行（表 5）
/// </summary>
public class MonthlyDeathRow
{
    public string HeadName { get; set; } = string.Empty;
    public string HeadIdCard { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string DeceasedName { get; set; } = string.Empty;
    public string DeceasedDate { get; set; } = string.Empty;
    public string EnjoyDate { get; set; } = string.Empty;  // 享受时间（=审批时间 first_approved_at）
    public string NewHeadName { get; set; } = string.Empty; // 新户主姓名
    public string CategoryCode { get; set; } = string.Empty;
    public bool IsRural { get; set; }
}

/// <summary>
/// 退出对象兜底纠治行（牡丹江市社会救助退出对象政策兜底纠治情况备案表）
/// </summary>
public class MonthlyExitRectificationRow
{
    public string Name { get; set; } = string.Empty;          // 姓名（户主/退出人）
    public string IdCard { get; set; } = string.Empty;        // 身份证号
    public string Category { get; set; } = string.Empty;      // 类别（退出前享受类别显示名）
    public string FamilySize { get; set; } = string.Empty;    // 家庭人口
    public string StopMonth { get; set; } = string.Empty;     // 停止月份（yyyy年M月）
    public string StopReason { get; set; } = string.Empty;    // 退保主要原因
    public string GracePeriod { get; set; } = string.Empty;   // 给予渐退期（是/否）
    public string IntoDibao { get; set; } = string.Empty;     // 纳入低保（是/否）
    public string IntoTeku { get; set; } = string.Empty;      // 纳入特困（是/否）
    public string IntoEdge { get; set; } = string.Empty;      // 纳入低保边缘家庭（是/否）
    public string IntoRigid { get; set; } = string.Empty;     // 纳入刚性支出困难家庭（是/否）
    public string DirectExit { get; set; } = string.Empty;    // 直接退出（是/否）
}

/// <summary>
/// 临时救助新增汇总行（月报_临时救助新增汇总表）
/// </summary>
public class MonthlyTempReliefRow
{
    public string Name { get; set; } = string.Empty;          // 姓名（申请人）
    public string IdCard { get; set; } = string.Empty;        // 身份证号
    public string Age { get; set; } = string.Empty;           // 年龄
    public string Gender { get; set; } = string.Empty;        // 性别
    public string FamilySize { get; set; } = string.Empty;    // 家庭人口
    public string Address { get; set; } = string.Empty;       // 家庭住址
    public string Category { get; set; } = string.Empty;      // 个人类别（family_category）
    public string Phone { get; set; } = string.Empty;         // 联系方式
    public string Amount { get; set; } = string.Empty;        // 救助金额（小额；大额为空）
    public string AuditDate { get; set; } = string.Empty;     // 审批时间（小额；大额为空）
    public string Reason { get; set; } = string.Empty;        // 申请理由（按困难类型逐条生成，块间换行）
    public string SelfPay { get; set; } = string.Empty;       // 自付金额合计（疾病+意外+教育）
    public string BankAccount { get; set; } = string.Empty;   // 一卡通账号（最低生活保障申请 bank_account）
}

/// <summary>
/// 会议记录（表 6）
/// </summary>
public class MonthlyMeeting
{
    public long Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string Town { get; set; } = "全部";
    public string MeetingTime { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string Recorder { get; set; } = string.Empty;
    public string Attendees { get; set; } = string.Empty;
    public string Absentees { get; set; } = string.Empty;
    public string MemberInfo { get; set; } = string.Empty;
    public string ApplyCategory { get; set; } = string.Empty;
    public string MemberNames { get; set; } = string.Empty;  // 新增成员姓名列表（顿号连接，决议段 {人员名单} 用，非 DB 列）
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}