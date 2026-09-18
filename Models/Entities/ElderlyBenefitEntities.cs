using NewCosmos.Constants;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 普惠高龄补贴登记实体（对应 nc_biz_elderly_applications 表）
/// </summary>
public class ElderlyApplication
{
    public long Id { get; set; }

    /// <summary>申请编号</summary>
    public string ApplicationNo { get; set; } = string.Empty;

    /// <summary>申请人姓名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>身份证号码</summary>
    public string IdCard { get; set; } = string.Empty;

    /// <summary>性别（身份证自动计算）</summary>
    public string Gender { get; set; } = string.Empty;

    /// <summary>出生日期（身份证自动计算）</summary>
    public DateTime? BirthDate { get; set; }

    /// <summary>联系电话</summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>户籍地址</summary>
    public string HukouAddress { get; set; } = string.Empty;

    /// <summary>户籍所属村</summary>
    public string HukouVillage { get; set; } = string.Empty;

    /// <summary>户籍城市ID</summary>
    public int? HukouCityId { get; set; }

    /// <summary>户籍区县ID</summary>
    public int? HukouCountyId { get; set; }

    /// <summary>户籍乡镇ID</summary>
    public int? HukouTownId { get; set; }

    /// <summary>户籍村/社区ID</summary>
    public int? HukouVillageId { get; set; }

    /// <summary>户籍详细地址</summary>
    public string HukouDetailAddress { get; set; } = string.Empty;

    /// <summary>家庭住址</summary>
    public string FamilyAddress { get; set; } = string.Empty;

    /// <summary>家庭城市ID</summary>
    public int? FamilyCityId { get; set; }

    /// <summary>家庭区县ID</summary>
    public int? FamilyCountyId { get; set; }

    /// <summary>家庭乡镇ID</summary>
    public int? FamilyTownId { get; set; }

    /// <summary>家庭村/社区ID</summary>
    public int? FamilyVillageId { get; set; }

    /// <summary>家庭详细地址（门牌号）</summary>
    public string DetailAddress { get; set; } = string.Empty;

    /// <summary>社保卡开户行</summary>
    public string BankName { get; set; } = string.Empty;

    /// <summary>社保卡账号</summary>
    public string BankAccount { get; set; } = string.Empty;

    /// <summary>代办人姓名</summary>
    public string AgentName { get; set; } = string.Empty;

    /// <summary>代办人与申请人关系</summary>
    public string AgentRelation { get; set; } = string.Empty;

    /// <summary>代领人姓名</summary>
    public string AgentReceiveName { get; set; } = string.Empty;

    /// <summary>代领人与申请人关系</summary>
    public string AgentReceiveRelation { get; set; } = string.Empty;

    /// <summary>代领人社保卡开户行</summary>
    public string AgentReceiveBankName { get; set; } = string.Empty;

    /// <summary>代领人社保卡账号</summary>
    public string AgentReceiveBankAccount { get; set; } = string.Empty;

    /// <summary>代领原因</summary>
    public string AgentReceiveReason { get; set; } = string.Empty;

    /// <summary>计发年月（受理次月，YYYY-MM）</summary>
    public string IssueStartMonth { get; set; } = string.Empty;

    /// <summary>计发金额（元/月，=受理时月标准）</summary>
    public decimal IssueAmount { get; set; }

    /// <summary>享受类别（CAT1/CAT2/CAT3/CAT4）</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>身份比对结果</summary>
    public string IdentityFlag { get; set; } = string.Empty;

    /// <summary>命中导入库表名</summary>
    public string IdentitySource { get; set; } = string.Empty;

    /// <summary>享受类别是否人工修正</summary>
    public bool IsCategoryManual { get; set; }

    /// <summary>补发起算月（YYYY-MM，实际采用值）</summary>
    public string PaybackStartMonth { get; set; } = string.Empty;

    /// <summary>补发止算月（YYYY-MM，实际采用值）</summary>
    public string PaybackEndMonth { get; set; } = string.Empty;

    /// <summary>系统自动起算月（特殊情况追溯用）</summary>
    public string AutoStartMonth { get; set; } = string.Empty;

    /// <summary>系统自动止算月（特殊情况追溯用）</summary>
    public string AutoEndMonth { get; set; } = string.Empty;

    /// <summary>补发总月数</summary>
    public int PaybackMonths { get; set; }

    /// <summary>补发总金额（元）</summary>
    public decimal PaybackAmount { get; set; }

    /// <summary>补发原因（未及时登记/查找不到本人/短暂失联/其他，可选）</summary>
    public string PaybackReason { get; set; } = string.Empty;

    /// <summary>是否特殊情况处理</summary>
    public bool IsSpecialCase { get; set; }

    /// <summary>特殊情况原因说明</summary>
    public string SpecialReason { get; set; } = string.Empty;

    /// <summary>状态（Draft/Confirmed/Stopped）</summary>
    public string Status { get; set; } = ElderlyBenefitConstants.StatusDraft;

    /// <summary>受理日期</summary>
    public DateTime? ApplyDate { get; set; }

    public DateTime? ConfirmedAt { get; set; }
    public string ConfirmedBy { get; set; } = string.Empty;
    public DateTime? StoppedAt { get; set; }
    public string StoppedBy { get; set; } = string.Empty;
    public string StopReason { get; set; } = string.Empty;

    /// <summary>应停发时间</summary>
    public DateTime? DueStopDate { get; set; }

    /// <summary>实际停发时间（默认=系统停发时间）</summary>
    public DateTime? ActualStopDate { get; set; }

    /// <summary>是否需要追缴</summary>
    public bool IsRecover { get; set; }

    /// <summary>追缴起算月（YYYY-MM）</summary>
    public string RecoverStartMonth { get; set; } = string.Empty;

    /// <summary>追缴止算月（YYYY-MM）</summary>
    public string RecoverEndMonth { get; set; } = string.Empty;

    /// <summary>追缴金额（元）</summary>
    public decimal RecoverAmount { get; set; }

    /// <summary>死亡日期（停发原因为去世时填写）</summary>
    public DateTime? DeathDate { get; set; }

    /// <summary>备注</summary>
    public string Remark { get; set; } = string.Empty;

    /// <summary>数据来源类型（如 ElderlySubsidyHistory，NULL=手动新增）</summary>
    public string? SourceType { get; set; }

    /// <summary>来源记录ID（如导入库记录ID）</summary>
    public long? SourceId { get; set; }

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime? DeletedAt { get; set; }

    /// <summary>享受类别显示名（供界面绑定）</summary>
    public string CategoryName => ElderlyBenefitConstants.GetCategoryName(Category);

    /// <summary>状态中文名（供界面绑定）</summary>
    public string StatusName => ElderlyBenefitConstants.GetStatusName(Status);

    /// <summary>当前年龄（按出生日期计算，供界面绑定）</summary>
    public int? Age
    {
        get
        {
            if (BirthDate == null) return null;
            var today = DateTime.Today;
            var age = today.Year - BirthDate.Value.Year;
            if (BirthDate.Value.Date > today.AddYears(-age)) age--;
            return age;
        }
    }

    /// <summary>补发起算月显示（2025年1月）</summary>
    public string PaybackStartDisplay => FormatMonth(PaybackStartMonth);

    /// <summary>补发止算月显示（2025年1月）</summary>
    public string PaybackEndDisplay => FormatMonth(PaybackEndMonth);

    /// <summary>计发年月显示（2026年8月）</summary>
    public string IssueStartMonthDisplay => FormatMonth(IssueStartMonth);

    /// <summary>补发金额显示（100元）</summary>
    public string PaybackAmountDisplay => FormatYuan(PaybackAmount);

    /// <summary>计发金额显示（25元/月）</summary>
    public string IssueAmountDisplay => FormatYuan(IssueAmount) + "/月";

    /// <summary>月补贴标准显示（25元/月）</summary>
    public string MonthlyAmountDisplay => FormatYuan(MonthlyAmountRef) + "/月";

    private decimal MonthlyAmountRef => PaybackMonths > 0 ? Math.Round(PaybackAmount / PaybackMonths, 2) : 0m;

    private static string FormatMonth(string month)
    {
        if (string.IsNullOrWhiteSpace(month) || month.Length < 7) return month;
        if (DateTime.TryParseExact(month, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
        {
            return $"{dt.Year}年{dt.Month}月";
        }
        return month;
    }

    private static string FormatYuan(decimal amount) => $"{amount:F2}元";
}

/// <summary>
/// 普惠高龄补贴补发分段明细实体（对应 nc_biz_elderly_payback_segments 表）
/// </summary>
public class ElderlyPaybackSegment
{
    public long Id { get; set; }

    /// <summary>关联登记表ID</summary>
    public long ApplicationId { get; set; }

    /// <summary>该段享受类别代码</summary>
    public string CategoryCode { get; set; } = string.Empty;

    /// <summary>该段月标准金额（元/月）</summary>
    public decimal MonthlyAmount { get; set; }

    /// <summary>该段起算月（YYYY-MM）</summary>
    public string SegmentStartMonth { get; set; } = string.Empty;

    /// <summary>该段止算月（YYYY-MM）</summary>
    public string SegmentEndMonth { get; set; } = string.Empty;

    /// <summary>该段月数</summary>
    public int Months { get; set; }

    /// <summary>该段金额（元）</summary>
    public decimal SegmentAmount { get; set; }

    /// <summary>该段类别显示名（供界面绑定）</summary>
    public string CategoryName => ElderlyBenefitConstants.GetCategoryName(CategoryCode);
}

/// <summary>
/// 判类 + 补发计算的评估结果（受理时身份证联动展示用）
/// </summary>
public class ElderlyEvaluateResult
{
    /// <summary>性别</summary>
    public string Gender { get; set; } = string.Empty;

    /// <summary>出生日期</summary>
    public DateTime? BirthDate { get; set; }

    /// <summary>当前年龄</summary>
    public int Age { get; set; }

    /// <summary>享受类别</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>享受类别显示名</summary>
    public string CategoryName => ElderlyBenefitConstants.GetCategoryName(Category);

    /// <summary>月补贴标准（元/月）</summary>
    public decimal MonthlyAmount { get; set; }

    /// <summary>计发年月（受理次月，YYYY-MM）</summary>
    public string IssueStartMonth { get; set; } = string.Empty;

    /// <summary>计发金额（元/月）</summary>
    public decimal IssueAmount { get; set; }

    /// <summary>身份比对结果</summary>
    public string IdentityFlag { get; set; } = string.Empty;

    /// <summary>命中导入库表名</summary>
    public string IdentitySource { get; set; } = string.Empty;

    /// <summary>补发计算明细</summary>
    public ElderlyPaybackResult Payback { get; set; } = new();
}

/// <summary>
/// 补发分段计算结果
/// </summary>
public class ElderlyPaybackResult
{
    /// <summary>补发起算月（YYYY-MM）</summary>
    public string StartMonth { get; set; } = string.Empty;

    /// <summary>补发止算月（YYYY-MM）</summary>
    public string EndMonth { get; set; } = string.Empty;

    /// <summary>补发总月数</summary>
    public int TotalMonths { get; set; }

    /// <summary>补发总金额（元）</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>分段明细</summary>
    public List<ElderlyPaybackSegment> Segments { get; set; } = new();
}

/// <summary>
/// 普惠高龄导入库搜索结果项（nc_biz_elderly_subsidy_history）
/// </summary>
public class ElderlyImportedSearchItem
{
    /// <summary>导入库记录ID</summary>
    public long Id { get; set; }

    /// <summary>老人姓名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>身份证号码</summary>
    public string IdCard { get; set; } = string.Empty;

    /// <summary>联系电话</summary>
    public string? Phone { get; set; }

    /// <summary>户籍地址</summary>
    public string? Address { get; set; }

    /// <summary>年龄</summary>
    public int? Age { get; set; }

    /// <summary>人员类型（系统分类）</summary>
    public string? PersonType { get; set; }

    /// <summary>原始类型（Excel导入）</summary>
    public string? OriginalType { get; set; }

    /// <summary>数据年份</summary>
    public int? DataYear { get; set; }

    /// <summary>性别（从身份证计算）</summary>
    public string? Gender { get; set; }

    /// <summary>出生日期（从身份证计算）</summary>
    public DateTime? BirthDate { get; set; }

    /// <summary>发放金额（元）</summary>
    public decimal? SubsidyAmount { get; set; }

    /// <summary>银行卡号</summary>
    public string? BankAccount { get; set; }

    /// <summary>导入时间</summary>
    public DateTime? ImportedAt { get; set; }

    /// <summary>是否来自导入库（用于区分当前库记录）</summary>
    public bool IsFromImportLibrary => true;

    /// <summary>来源显示文本</summary>
    public string SourceDisplay => $"历史导入 ({DataYear ?? 0}年)";
}

/// <summary>
/// 首页普惠高龄待办提醒统计（已完结档案中年满80周岁的户主/共同生活成员分流计数）
/// </summary>
public class ElderlyPendingCounts
{
    /// <summary>待新增：无在享登记（正式表无 Confirmed 且历史名册无记录）</summary>
    public int PendingNew { get; set; }

    /// <summary>待停旧增新：正式表 Confirmed 在享，或历史名册（nc_biz_elderly_subsidy_history）有记录</summary>
    public int PendingTransfer { get; set; }

    /// <summary>合计</summary>
    public int Total => PendingNew + PendingTransfer;
}

/// <summary>
/// 普惠高龄下月待办明细项（已完结档案中年满80周岁的户主/共同生活成员，分流到待新增/需停旧增新）
/// </summary>
public class ElderlyPendingItem
{
    /// <summary>待办类型：New=待新增；Transfer=需停旧增新</summary>
    public string PendingType { get; set; } = ElderlyBenefitConstants.PendingTypeNew;

    /// <summary>姓名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>身份证号</summary>
    public string IdCard { get; set; } = string.Empty;

    /// <summary>出生日期</summary>
    public DateTime? BirthDate { get; set; }

    /// <summary>年龄（周岁）</summary>
    public int Age { get; set; }

    /// <summary>性别</summary>
    public string Gender { get; set; } = string.Empty;

    /// <summary>家庭成员身份（户主 / 共同生活成员）</summary>
    public string MemberRole { get; set; } = string.Empty;

    /// <summary>来源档案ID（nc_biz_applications.id，已完结档案）</summary>
    public long SourceApplicationId { get; set; }

    /// <summary>来源档案申请编号</summary>
    public string SourceApplicationNo { get; set; } = string.Empty;

    /// <summary>正式表 Confirmed 在享记录ID（Transfer 且已建档时 > 0）</summary>
    public long ElderlyApplicationId { get; set; }

    /// <summary>正式表在享记录申请编号</summary>
    public string ElderlyApplicationNo { get; set; } = string.Empty;

    /// <summary>历史名册记录ID（Transfer 且历史名册有记录时 > 0，未建档可据此迁移）</summary>
    public long HistoryId { get; set; }

    /// <summary>是否需停旧增新</summary>
    public bool IsTransfer => string.Equals(PendingType, ElderlyBenefitConstants.PendingTypeTransfer, StringComparison.Ordinal);

    /// <summary>状态标注（来源/在享情况，供列表展示）</summary>
    public string StatusLabel
    {
        get
        {
            if (IsTransfer)
            {
                if (ElderlyApplicationId > 0)
                    return $"在享登记 {ElderlyApplicationNo}";
                if (HistoryId > 0)
                    return "历史名册在享";
                return "需先停发后新增";
            }
            return "待新增登记";
        }
    }
}

/// <summary>
/// 高龄津贴类别复核记录（对应 nc_biz_elderly_reviews 表）。
/// 既是「待复核队列」（status=Pending），也是复核办理留痕（status=Completed）。
/// </summary>
public class ElderlyReview
{
    public long Id { get; set; }

    /// <summary>复核编号（FC+日期+序号）</summary>
    public string ReviewNo { get; set; } = string.Empty;

    /// <summary>身份证号</summary>
    public string IdCard { get; set; } = string.Empty;

    /// <summary>姓名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>状态（Pending/Completed）</summary>
    public string Status { get; set; } = ElderlyBenefitConstants.ReviewStatusPending;

    /// <summary>触发来源（Manual/ArchiveReview/LowIncomeChange/Import）</summary>
    public string TriggerSource { get; set; } = string.Empty;

    /// <summary>触发来源记录ID（低收入档案ID/变更记录ID）</summary>
    public long? TriggerRef { get; set; }

    /// <summary>触发说明</summary>
    public string TriggerReason { get; set; } = string.Empty;

    /// <summary>名册记录ID（名册未建档人员）</summary>
    public long? SourceHistoryId { get; set; }

    public long? OldApplicationId { get; set; }
    public string OldApplicationNo { get; set; } = string.Empty;
    public string OldCategory { get; set; } = string.Empty;
    public decimal? OldMonthlyAmount { get; set; }

    public long? NewApplicationId { get; set; }
    public string NewApplicationNo { get; set; } = string.Empty;
    public string NewCategory { get; set; } = string.Empty;
    public decimal? NewMonthlyAmount { get; set; }

    /// <summary>重评身份比对结果</summary>
    public string IdentityFlag { get; set; } = string.Empty;

    /// <summary>复核时年龄（周岁）</summary>
    public int? AgeAtReview { get; set; }

    /// <summary>应生效月（满90/100当月，yyyy-MM）</summary>
    public string EffectiveMonth { get; set; } = string.Empty;

    /// <summary>新档计发起始月（复核次月，yyyy-MM）</summary>
    public string IssueStartMonth { get; set; } = string.Empty;

    /// <summary>复核结果（Changed/NoChange）</summary>
    public string ReviewResult { get; set; } = string.Empty;

    /// <summary>调整原因（AGE/IDENTITY/OTHER）</summary>
    public string AdjustReasonCode { get; set; } = string.Empty;

    /// <summary>复核意见</summary>
    public string ReviewOpinion { get; set; } = string.Empty;

    public string ReviewedBy { get; set; } = string.Empty;
    public DateTime? ReviewedAt { get; set; }

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>状态中文名（供界面绑定）</summary>
    public string StatusName => ElderlyBenefitConstants.GetReviewStatusName(Status);

    /// <summary>复核结果中文名（供界面绑定）</summary>
    public string ReviewResultName => ElderlyBenefitConstants.GetReviewResultName(ReviewResult);

    /// <summary>触发来源中文名（供界面绑定）</summary>
    public string TriggerSourceName => ElderlyBenefitConstants.GetReviewTriggerName(TriggerSource);

    /// <summary>旧/新类别展示（旧 → 新）</summary>
    public string CategoryChangeDisplay =>
        string.IsNullOrEmpty(NewCategory)
            ? ElderlyBenefitConstants.GetCategoryName(OldCategory)
            : $"{ElderlyBenefitConstants.GetCategoryName(OldCategory)} → {ElderlyBenefitConstants.GetCategoryName(NewCategory)}";

    /// <summary>旧/新月标准展示（旧 → 新，元/月）</summary>
    public string AmountChangeDisplay =>
        string.IsNullOrEmpty(NewCategory)
            ? (OldMonthlyAmount.HasValue ? $"{OldMonthlyAmount.Value:F2} 元/月" : string.Empty)
            : $"{OldMonthlyAmount ?? 0m:F2} → {NewMonthlyAmount ?? 0m:F2} 元/月";
}

/// <summary>
/// 高龄津贴类别复核评估结果（重评身份+年龄，与旧类别比较；不落库的瞬态对象）。
/// </summary>
public class ElderlyReviewEvaluation
{
    /// <summary>来源：是否仅有导入名册（当前库未建档）</summary>
    public bool IsHistoryOnly { get; set; }

    /// <summary>在享档案ID（名册未建档时为 0/空）</summary>
    public long? ApplicationId { get; set; }

    public string ApplicationNo { get; set; } = string.Empty;

    /// <summary>名册记录ID（仅名册人员）</summary>
    public long? HistoryId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    public int Age { get; set; }

    /// <summary>旧身份比对结果（名册人员由 person_type 还原）</summary>
    public string OldIdentityFlag { get; set; } = string.Empty;

    /// <summary>重评身份比对结果</summary>
    public string IdentityFlag { get; set; } = string.Empty;

    /// <summary>命中的身份来源表</summary>
    public string IdentitySource { get; set; } = string.Empty;

    public string OldCategory { get; set; } = string.Empty;
    public decimal OldMonthlyAmount { get; set; }

    public string NewCategory { get; set; } = string.Empty;
    public decimal NewMonthlyAmount { get; set; }

    /// <summary>类别是否变化</summary>
    public bool HasChange => !string.Equals(OldCategory, NewCategory, StringComparison.Ordinal);

    /// <summary>是否升档（新标准高于旧标准）</summary>
    public bool IsUpgrade => NewMonthlyAmount > OldMonthlyAmount;

    /// <summary>调整原因（AGE/IDENTITY/OTHER）</summary>
    public string AdjustReasonCode { get; set; } = string.Empty;

    /// <summary>身份变动方向：是否新增高补贴身份（用于调整备案表勾选）</summary>
    public bool IdentityAdd { get; set; }

    /// <summary>应生效月（满90/100周岁当月，yyyy-MM；非年龄原因时为空）</summary>
    public string EffectiveMonth { get; set; } = string.Empty;

    /// <summary>新档计发起始月（复核次月，yyyy-MM）</summary>
    public string IssueStartMonth { get; set; } = string.Empty;

    public string OldCategoryName => ElderlyBenefitConstants.GetCategoryName(OldCategory);
    public string NewCategoryName => ElderlyBenefitConstants.GetCategoryName(NewCategory);
}

/// <summary>
/// 月报「满90周岁调整备案表」名单行：本月满90周岁的在享/名册人员，逐人一页。
/// 旧类别取 89 岁档（在享用档案类别；名册按 person_type 推断），新类别固定 CAT3。
/// </summary>
public class ElderlyAge90Row
{
    /// <summary>在享档案ID（名册人员为空）</summary>
    public long? ApplicationId { get; set; }

    /// <summary>名册记录ID（在享人员为空）</summary>
    public long? HistoryId { get; set; }

    public bool IsHistoryOnly => ApplicationId is null or 0;

    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }

    /// <summary>满90周岁当月时的年龄（=90）</summary>
    public int Age { get; set; } = ElderlyBenefitConstants.Threshold90;

    /// <summary>身份比对结果（名册由 person_type 推断）</summary>
    public string IdentityFlag { get; set; } = string.Empty;

    public string HukouAddress { get; set; } = string.Empty;
    public string FamilyAddress { get; set; } = string.Empty;

    /// <summary>旧类别（89岁档）</summary>
    public string OldCategory { get; set; } = string.Empty;
    public decimal OldMonthlyAmount { get; set; }

    /// <summary>新类别（90档 CAT3）</summary>
    public string NewCategory { get; set; } = ElderlyBenefitConstants.Cat90To99;
    public decimal NewMonthlyAmount { get; set; }

    /// <summary>应生效月（满90周岁当月，yyyy-MM）</summary>
    public string EffectiveMonth { get; set; } = string.Empty;

    public string OldCategoryName => ElderlyBenefitConstants.GetCategoryName(OldCategory);
    public string NewCategoryName => ElderlyBenefitConstants.GetCategoryName(NewCategory);
}
