using System.Text;
using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.StateMachine;
using NewCosmos.Services.System;

namespace NewCosmos.Services.Domain.ChangeManagement;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

/// <summary>
/// 变更上下文
/// </summary>
public class ChangeContext
{
    public long ApplicationId { get; set; }
    public string ChangeType { get; set; } = string.Empty;

    /// <summary>变更类别（DictionaryConstants.ChangeCategory.*；默认 null 不写）</summary>
    public string? ChangeCategory { get; set; }

    public string ChangeReason { get; set; } = string.Empty;
    public string ChangeReasonType { get; set; } = string.Empty;
    public DateTime ChangeDate { get; set; } = DateTime.Today;
    public string OperatorName { get; set; } = string.Empty;
}

/// <summary>
/// 经济复核上下文
/// </summary>
public class EconomicReviewContext
{
    public long ApplicationId { get; set; }
    public decimal NewTotalFamilyIncome { get; set; }
    public decimal NewPerCapitaIncome { get; set; }
    public decimal NewTotalAnnualIncome { get; set; }
    public decimal NewPerCapitaAnnualIncome { get; set; }
    public decimal NewRigidExpenditure { get; set; }
    public int NewFamilySize { get; set; }
    public string ReviewReason { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;

    /// <summary>
    /// 是否为家庭信息修正模式（需要额外校验周期限制）
    /// </summary>
    public bool IsFamilyCorrection { get; set; }

    // ── 覆写前捕获的变更前旧值 ──
    // 经济复核入口（表单 ExecuteReviewSaveAsync）优先传向导入口（LoadApplicationAsync）固化的快照
    //（_entryApplicationSnapshot）：步骤保存/Step5 判定落库已把新经济数据写回本档，
    // 此后读库拿到的"旧值"实为覆写后值（Before 快照 / old_per_capita_income 失真）。
    // 任一未提供时回退读 application（与历史行为一致）。

    /// <summary>覆写前的家庭人数</summary>
    public int? OldFamilySize { get; set; }

    /// <summary>覆写前的家庭月总收入</summary>
    public decimal? OldTotalFamilyIncome { get; set; }

    /// <summary>覆写前的家庭年收入</summary>
    public decimal? OldTotalAnnualIncome { get; set; }

    /// <summary>覆写前的月人均收入</summary>
    public decimal? OldPerCapitaIncome { get; set; }

    /// <summary>覆写前的刚性支出数值（口径同上）</summary>
    public decimal? OldRigidExpenditure { get; set; }

    /// <summary>
    /// 覆写前的分类结果。必须在 Step5「分类判定」落库之前捕获（表单加载时刻），
    /// 否则 needRebuild 的分类比较读到的是覆写后值，恒判"无变化"。
    /// </summary>
    public string? OldClassification { get; set; }

    /// <summary>
    /// 覆写前的保障金合计（户月 + 分类施保 + 照料费）。同上必须在判定落库前捕获，
    /// 否则 needRebuild 的金额比较恒判"无变化"，真正有变化的复核不重建。
    /// </summary>
    public decimal? OldGuaranteeAmount { get; set; }

    /// <summary>
    /// 覆写前的收入分项旧值（与 Application 列同名同口径：务工/经营/财产/转移/其他/刚性为月值，赡养/土地/补贴为年值）
    /// </summary>
    public IncomeComponentValues? OldComponents { get; set; }
}

/// <summary>
/// 收入分项值（列口径与 nc_biz_applications 主表一致：前六项月值、后三项年值；读取方展示时统一换算年值）
/// </summary>
public class IncomeComponentValues
{
    public decimal WorkIncomeTotal { get; set; }
    public decimal BusinessIncomeTotal { get; set; }
    public decimal PropertyIncomeTotal { get; set; }
    public decimal TransferIncomeTotal { get; set; }
    public decimal OtherIncomeTotal { get; set; }
    public decimal RigidExpenditure { get; set; }
    public decimal AlimonyIncome { get; set; }
    public decimal LandIncomeTotal { get; set; }
    public decimal SubsidyTotal { get; set; }
}

/// <summary>
/// 家庭成员变更上下文（增员/减员后重新认定，停旧建新）
/// </summary>
public class MemberChangeContext
{
    public long ApplicationId { get; set; }

    /// <summary>主变更类型（DictionaryConstants.ChangeType.MEMBER_ADD/MEMBER_REMOVE/MEMBER_MODIFY）</summary>
    public string ChangeType { get; set; } = DictionaryConstants.ChangeType.MEMBER_MODIFY;

    /// <summary>变更摘要（如 "新增 张三；减员 李四"），并入变更原因</summary>
    public string ChangeSummary { get; set; } = string.Empty;

    /// <summary>变更原因（表单"原因详情"，必填）</summary>
    public string ChangeReason { get; set; } = string.Empty;

    /// <summary>逐人变更明细（增员/减员原因登记；写入快照与 nc_biz_change_details，死亡减员联动死亡记录）</summary>
    public List<MemberChangeEntry> Entries { get; set; } = new();

    /// <summary>变更前家庭人数（表单加载时口径，用于 Before 快照）</summary>
    public int OldFamilySize { get; set; }

    /// <summary>
    /// 覆写前的分类结果。必须在 Step5「分类判定」落库之前捕获（表单加载时刻），
    /// 否则停旧建新判定的分类/金额比较读到覆写后值，恒判"无变化"。
    /// </summary>
    public string? OldClassification { get; set; }

    /// <summary>覆写前的保障金合计（户月 + 分类施保 + 照料费）。同上必须在判定落库前捕获。</summary>
    public decimal? OldGuaranteeAmount { get; set; }

    /// <summary>覆写前的家庭月总收入（表单入口快照；null 回退读库——本事务 SaveEconomicDetailsAsync 已先写回）</summary>
    public decimal? OldTotalFamilyIncome { get; set; }

    /// <summary>覆写前的月人均收入（同上；写入 Before 快照与 nc_biz_change_records.old_per_capita_income）</summary>
    public decimal? OldPerCapitaIncome { get; set; }

    /// <summary>覆写前的刚性支出（同上）</summary>
    public decimal? OldRigidExpenditure { get; set; }

    public decimal NewTotalFamilyIncome { get; set; }
    public decimal NewPerCapitaIncome { get; set; }
    public decimal NewTotalAnnualIncome { get; set; }
    public decimal NewPerCapitaAnnualIncome { get; set; }
    public decimal NewRigidExpenditure { get; set; }
    public int NewFamilySize { get; set; }
    public string OperatorName { get; set; } = string.Empty;
}

/// <summary>
/// 家庭成员变更明细（增员/减员逐人原因登记）
/// </summary>
public class MemberChangeEntry
{
    /// <summary>方向：DictionaryConstants.ChangeType.MEMBER_ADD / MEMBER_REMOVE</summary>
    public string Direction { get; set; } = DictionaryConstants.ChangeType.MEMBER_ADD;

    /// <summary>成员ID（减员为旧档案成员行ID；增员为 0）</summary>
    public long MemberId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string RelationshipToHead { get; set; } = string.Empty;
    public string MemberCategory { get; set; } = string.Empty;

    /// <summary>变更原因代码（字典 item_key，分类 ChangeReasons/MemberAddReasons）</summary>
    public string ReasonCode { get; set; } = string.Empty;

    /// <summary>变更原因名称（冗余存储，供输出与审计直读）</summary>
    public string ReasonName { get; set; } = string.Empty;

    /// <summary>事由日期（死亡减员=死亡日期，其余=生效日期）</summary>
    public DateTime EventDate { get; set; } = DateTime.Today;

    public string Remark { get; set; } = string.Empty;
}

/// <summary>
/// 成员死亡上下文
/// </summary>
public class MemberDeathContext
{
    public long ApplicationId { get; set; }
    public long MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string MemberIdCard { get; set; } = string.Empty;
    public DateTime DeathDate { get; set; } = DateTime.Today;
    public string DeathReason { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
}

/// <summary>
/// 户主变更上下文
/// </summary>
public class HeadChangeContext
{
    public long ApplicationId { get; set; }
    public long OldHeadMemberId { get; set; }
    public long NewHeadMemberId { get; set; }
    public string OldHeadName { get; set; } = string.Empty;
    public string NewHeadName { get; set; } = string.Empty;
    public string ChangeReason { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
}

/// <summary>
/// 户主死亡变更上下文（停旧建新）
/// </summary>
public class HouseholdDeathContext
{
    /// <summary>
    /// 旧档案申请ID
    /// </summary>
    public long ApplicationId { get; set; }

    /// <summary>
    /// 死亡原户主成员ID（0 时由服务端按"申请人/户主"自动定位）
    /// </summary>
    public long DeceasedHeadMemberId { get; set; }

    /// <summary>
    /// 死亡原户主姓名
    /// </summary>
    public string DeceasedHeadName { get; set; } = string.Empty;

    /// <summary>
    /// 死亡原户主身份证号
    /// </summary>
    public string DeceasedHeadIdCard { get; set; } = string.Empty;

    /// <summary>
    /// 新户主成员ID（在原档案中的 member id）
    /// </summary>
    public long NewHeadMemberId { get; set; }

    /// <summary>
    /// 新户主姓名
    /// </summary>
    public string NewHeadName { get; set; } = string.Empty;

    /// <summary>
    /// 新户主身份证号
    /// </summary>
    public string NewHeadIdCard { get; set; } = string.Empty;

    /// <summary>
    /// 死亡日期
    /// </summary>
    public DateTime DeathDate { get; set; } = DateTime.Today;

    /// <summary>
    /// 死亡原因
    /// </summary>
    public string DeathReason { get; set; } = string.Empty;

    /// <summary>
    /// 死亡证明编号
    /// </summary>
    public string DeathCertificateNo { get; set; } = string.Empty;

    /// <summary>
    /// 操作人
    /// </summary>
    public string OperatorName { get; set; } = string.Empty;
}

/// <summary>
/// 变更结果
/// </summary>
public class ChangeResult
{
    public long ChangeId { get; set; }
    public bool IsSuccess { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string OldClassification { get; set; } = string.Empty;
    public string NewClassification { get; set; } = string.Empty;
    public decimal OldGuaranteeAmount { get; set; }
    public decimal NewGuaranteeAmount { get; set; }
    public bool TriggeredGracePeriod { get; set; }
    public bool TriggeredStop { get; set; }

    /// <summary>
    /// 是否已停旧建新生成新档案。
    /// false = 保障金额/分类/停保三项均未变化，复核结果留在原档（原档保持有效，未生成新档案）。
    /// </summary>
    public bool Rebuilt { get; set; }

    /// <summary>
    /// 户主死亡停旧建新后生成的新档案申请ID（其余变更场景为 0）
    /// </summary>
    public long NewApplicationId { get; set; }
}

/// <summary>
/// 变更服务接口
/// </summary>
public interface IChangeService
{
    /// <summary>
    /// 创建变更记录
    /// </summary>
    Task<Result<long>> CreateChangeAsync(ChangeContext context, string beforeJson, string afterJson, CancellationToken ct = default);

    /// <summary>
    /// 渐退超限减发：补写 change_type=FundChange（old&gt;new、同分类），供月报保障金减发表捕获。幂等。
    /// </summary>
    Task<Result<long>> CreateGraceCapFundChangeAsync(
        long applicationId,
        string newClassification,
        string oldClassification,
        decimal oldGuaranteeAmount,
        decimal newGuaranteeAmount,
        CancellationToken ct = default);

    /// <summary>
    /// 接续链档案 Step5 分类判定后同步跨大类 CategoryAdd 记录（月报「新增救助明细」跨类新增行数据源，
    /// 同时使本档变更记录列表可见「新类别新增」）。仅链档案（original_application_id 非空）且新分类与
    /// 上游档案跨四大类时写入；幂等——已存在且分类一致跳过，判定值变化就地更新，判定回同大类则软删。
    /// change_date = 判定日；triggered_grace_period/triggered_stop 恒 false（死亡停保已由链上
    /// HouseholdDeath 记录承载，置 true 会使月报「退出对象纠治表」误标经济复核降档事件）。
    /// </summary>
    Task<Result<long?>> EnsureChainCategoryAddAsync(long applicationId, string operatorName, CancellationToken ct = default);

    /// <summary>
    /// 户主死亡链进入渐退期后同步分类施保减发记录（change_type=ClassifiedSubsidyReduce，挂旧档）：
    /// 原分类施保（上游档）&gt; 渐退后现分类施保时写入，供变更历史展示「分类施保减除」、
    /// 统计/退出纠治表排除（避免一次死亡事件计两次）与月报「分类施保金减发人员表」取数。
    /// 幂等——同户同类记录金额一致跳过、变化就地更新；条件消失（回同额）软删。
    /// change_category=NULL（不入增减员调整表）、无快照（不抢链上死亡快照的 latest）、
    /// change_reason_type='分类施保减除'（避开复核情况/渐退退出按 '户主死亡' 取最近一条的误读）、
    /// triggered_grace_period/triggered_stop 恒 false。
    /// </summary>
    Task<Result<long?>> EnsureChainClassifiedSubsidyReduceAsync(long applicationId, string operatorName, CancellationToken ct = default);

    /// <summary>
    /// 查询与本档案关联（application_id 或 new_application_id）且含快照的最近变更记录的 Before/After 快照。
    /// 定期复核审批表「死亡原因/家庭人口」取数兜底：停旧建新链的 CategoryAdd 行自身无快照
    /// （经济复核跨类行/Step5 跨类补写行），快照在链上发起记录（如户主死亡记录）；无匹配时 Value 为 null 非失败。
    /// </summary>
    Task<Result<ChangeSnapshotPair?>> GetLinkedChangeSnapshotsAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 保存快照
    /// </summary>
    Task<Result> SaveSnapshotAsync(long changeId, string snapshotType, string dataJson, CancellationToken ct = default);

    /// <summary>
    /// 保存变更明细
    /// </summary>
    Task<Result> SaveDetailAsync(long changeId, string fieldName, string oldValue, string newValue, CancellationToken ct = default);

    /// <summary>
    /// 执行经济复核
    /// </summary>
    Task<Result<ChangeResult>> ExecuteEconomicReviewAsync(EconomicReviewContext context, CancellationToken ct = default);

    /// <summary>
    /// 执行家庭成员变更（增员/减员后重新认定，停旧建新）
    /// </summary>
    Task<Result<ChangeResult>> ExecuteMemberChangeAsync(MemberChangeContext context, CancellationToken ct = default);

    /// <summary>
    /// 处理成员死亡
    /// </summary>
    Task<Result<ChangeResult>> ProcessMemberDeathAsync(MemberDeathContext context, CancellationToken ct = default);

    /// <summary>
    /// 执行户主变更
    /// </summary>
    Task<Result<ChangeResult>> ExecuteHeadChangeAsync(HeadChangeContext context, CancellationToken ct = default);

    /// <summary>
    /// 执行户主死亡变更（停旧建新：旧档案停止，以新户主建立新档案并继承全部子表数据）
    /// </summary>
    Task<Result<ChangeResult>> ExecuteHouseholdDeathAsync(HouseholdDeathContext context, CancellationToken ct = default);

    /// <summary>
    /// 获取变更历史（按变更时间倒序，最多 200 条）
    /// </summary>
    Task<Result<List<ChangeRecord>>> GetChangeHistoryAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 查询最近一条触发停保的 CategoryStop 变更记录（档案告知书字段装配用；无则 Value 为 null）
    /// </summary>
    Task<Result<TriggeredStopChangeRecord?>> GetLatestTriggeredStopRecordAsync(long applicationId, IReadOnlyCollection<string> newClassifications, CancellationToken ct = default);

    /// <summary>
    /// 是否存在触发停保的 CategoryStop 变更记录（档案输出追加变更告知书判定用）
    /// </summary>
    Task<Result<bool>> HasTriggeredCategoryStopAsync(long applicationId, IReadOnlyCollection<string> stopCategoryCodes, CancellationToken ct = default);

    /// <summary>
    /// 查询家庭成员类变更记录（增减员调整表字段装配用，按变更日期倒序）
    /// </summary>
    Task<Result<List<MemberChangeRecord>>> GetMemberChangeRecordsAsync(long applicationId, int limit, CancellationToken ct = default);

    /// <summary>
    /// 查询逐人增/减员明细（增减员调整表字段装配用，按变更日期倒序、按身份证号去重）。
    /// 数据源两路：①成员增减流程写入的 nc_biz_change_details（field_name=MemberAdd/MemberRemove，含逐人原因）；
    /// ②成员死亡/户主死亡流程不写明细，按 change_reason 的 "动词: 姓名(身份证)" 解析兜底。
    /// 两类人员均回查 nc_biz_family_members 补齐性别/家庭关系/身体状况/工作单位/年收入
    /// （减员行可能只剩软删除历史行，故查询不带 deleted_at 过滤，优先取在册行）。
    /// </summary>
    Task<Result<List<MemberAdjustEntry>>> GetMemberAdjustEntriesAsync(long applicationId, int limit, CancellationToken ct = default);

    /// <summary>
    /// 查询最近一条与渐退退出相关的变更（经济复核/户主死亡/触发停保，档案渐退审批表退出说明用；无则 Value 为 null）
    /// </summary>
    Task<Result<GraceExitChangeRecord?>> GetLatestGraceExitChangeAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 查询最近一条含新旧人均收入的变更记录（渐退审批表「变动情况说明」经济复核场景取人均收入新旧值用；无则 Value 为 null）
    /// </summary>
    Task<Result<IncomeComparisonRecord?>> GetLatestIncomeComparisonAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 查询最近一条挂在旧档上的、Before 快照含 Components 的变更（渐退审批表分项对比取复核前旧值用；无则 Value 为 null）
    /// </summary>
    Task<Result<BeforeSnapshotOldValues?>> GetLatestBeforeSnapshotAsync(long originalApplicationId, CancellationToken ct = default);

    /// <summary>
    /// 查询最近一条复核/变更记录（档案_定期复核审批表「复核情况/复核时间/待遇变化」取数用）。
    /// 覆盖经济复核、户主死亡、成员变更等完整流程（同档案可能只有死亡类记录，如卢永华户主死亡链）；无匹配则 Value 为 null。
    /// </summary>
    Task<Result<ReviewChangeRecord?>> GetLatestReviewChangeRecordAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 查询指定变更记录的 Before/After 快照 JSON（定期复核审批表「复核情况」取家庭人口与死亡原因用；
    /// 经济复核链的 Before 快照含 Components、死亡/成员变更链不含，故不复用 GetLatestBeforeSnapshotAsync；无则 Value 为 null）。
    /// </summary>
    Task<Result<ChangeSnapshotPair?>> GetChangeSnapshotsAsync(long changeId, CancellationToken ct = default);
}

/// <summary>
/// 变更记录
/// </summary>
public class ChangeRecord
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public string ChangeNo { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
    public string ChangeReason { get; set; } = string.Empty;
    public DateTime ChangeDate { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ChangedAt { get; set; }

    /// <summary>变更类型显示名（列表绑定用；未知类型回退原值）</summary>
    public string ChangeTypeDisplay => ChangeType switch
    {
        DictionaryConstants.ChangeType.FUND_CHANGE => "经济复核",
        DictionaryConstants.ChangeType.MEMBER_ATTRIBUTE => "成员属性变更",
        DictionaryConstants.ChangeType.MEMBER_ADD => "成员增加",
        DictionaryConstants.ChangeType.MEMBER_REMOVE => "成员减少",
        DictionaryConstants.ChangeType.MEMBER_DEATH => "成员死亡",
        DictionaryConstants.ChangeType.MEMBER_MODIFY => "成员修改",
        DictionaryConstants.ChangeType.HOUSEHOLD_DEATH => "户主死亡",
        DictionaryConstants.ChangeType.HOUSEHOLD_HEAD_CHANGE => "户主变更",
        DictionaryConstants.ChangeType.DRAFT_SAVE => "草稿保存",
        DictionaryConstants.ChangeType.CATEGORY_STOP => "原类别停止",
        DictionaryConstants.ChangeType.CATEGORY_ADD => "新类别新增",
        DictionaryConstants.ChangeType.CLASSIFIED_SUBSIDY_REDUCE => "分类施保减除",
        _ => ChangeType
    };
}

/// <summary>
/// 触发停保的变更记录（档案告知书区分"停止告知/不予认定告知"用）
/// </summary>
public class TriggeredStopChangeRecord
{
    public string? OldClassification { get; set; }
    public string? NewClassification { get; set; }
}

/// <summary>
/// 家庭成员类变更记录行（增减员调整表字段装配用）
/// </summary>
public class MemberChangeRecord
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public long NewApplicationId { get; set; }
    public string? ChangeType { get; set; }
    public string? ChangeReason { get; set; }
    public DateTime ChangeDate { get; set; }
    public string? OldClassification { get; set; }
    public string? NewClassification { get; set; }
    public decimal? OldGuaranteeAmount { get; set; }
    public decimal? NewGuaranteeAmount { get; set; }
}

/// <summary>
/// 逐人增/减员明细行（增减员调整表字段装配用）
/// </summary>
public class MemberAdjustEntry
{
    /// <summary>方向：DictionaryConstants.ChangeType.MEMBER_ADD / MEMBER_REMOVE</summary>
    public string Direction { get; set; } = DictionaryConstants.ChangeType.MEMBER_ADD;

    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string RelationshipToHead { get; set; } = string.Empty;
    public string MemberCategory { get; set; } = string.Empty;

    /// <summary>逐人变更原因名称（成员增减流程登记；死亡类记录为空，由调用方兜底）</summary>
    public string ReasonName { get; set; } = string.Empty;

    /// <summary>事由日期（死亡减员=死亡日期，其余=生效日期）</summary>
    public DateTime? EventDate { get; set; }

    /// <summary>所属变更日期（倒序排序与兜底原因用）</summary>
    public DateTime ChangeDate { get; set; }

    /// <summary>所属变更类型（死亡类兜底原因文案用）</summary>
    public string? ChangeType { get; set; }

    // ── 成员表回填（减员可能只剩软删除历史行；查不到时保持空）──
    public string? Gender { get; set; }
    public string? HealthStatus { get; set; }
    public string? WorkUnit { get; set; }
    public decimal AnnualIncome { get; set; }
}

/// <summary>
/// 渐退退出相关变更行（渐退期审批表「退出渐退期情况」分型拼句用）
/// </summary>
public class GraceExitChangeRecord
{
    public DateTime? ChangeDate { get; set; }
    public string? ChangeReasonType { get; set; }
    public string? ChangeType { get; set; }
    public string? ChangeReason { get; set; }
    public bool? TriggeredStop { get; set; }
    public string? NewClassification { get; set; }
    public string? OldClassification { get; set; }
}

/// <summary>
/// 最近一条复核/变更行（档案_定期复核审批表「复核情况/复核时间/待遇变化」取数用）
/// </summary>
public class ReviewChangeRecord
{
    public long Id { get; set; }
    public DateTime? ChangeDate { get; set; }
    public string? ChangeReason { get; set; }
    public string? ChangeReasonType { get; set; }
    public string? ChangeType { get; set; }
    public string? OldClassification { get; set; }
    public string? NewClassification { get; set; }
    public decimal? OldGuaranteeAmount { get; set; }
    public decimal? NewGuaranteeAmount { get; set; }
    // 复核前人均月收入（NULL=无历史收入记录，打印端按 0 显示，见 ArchiveProductionViewModel.BuildReviewChangeDetailAsync）
    public decimal? OldPerCapitaIncome { get; set; }
}

/// <summary>
/// 指定变更记录的 Before/After 快照 JSON（复核情况取家庭人口、死亡原因等；两键可能均为 null）
/// </summary>
public class ChangeSnapshotPair
{
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
}

/// <summary>
/// 变更新旧人均收入行（渐退期审批表「变动情况说明」经济复核场景取数用）
/// </summary>
public class IncomeComparisonRecord
{
    public decimal? OldPerCapitaIncome { get; set; }
    public decimal? NewPerCapitaIncome { get; set; }
    public DateTime? ChangeDate { get; set; }
    public string? ChangeType { get; set; }
}

/// <summary>
/// Before 快照中捕获的变更前旧值（经济复核表单覆写前写入，供渐退审批表人口/总额/分项对比）
/// </summary>
public class BeforeSnapshotOldValues
{
    public int? OldFamilySize { get; set; }
    public decimal? OldTotalFamilyIncome { get; set; }
    public decimal? OldTotalAnnualIncome { get; set; }
    public decimal? OldPerCapitaIncome { get; set; }
    public IncomeComponentValues? Components { get; set; }
}

/// <summary>
/// 变更服务实现。
/// [宽表豁免] 本文件对 nc_biz_applications / nc_biz_family_members / nc_biz_caregivers 的读取保留
/// <c>SELECT *</c>：这些是"整实体读取 → 分类判定/复制落库"的读改写路径，显式列必须穷尽全部被持久化列，
/// 否则漏列会被按实体默认值回写造成静默数据丢失；且实体属性数（如成员 53）多于 Schema YAML 列数（34），
/// 以 Schema 生成列清单并不可靠。故保留全列读取，仅列表/宽表分页查询按规范显式列 + LIMIT。
/// </summary>
public partial class ChangeService : BaseService, IChangeService
{
    protected override string ServiceName => "ChangeService";
    private readonly IDatabaseService _db;
    private readonly IClassificationService _classificationService;
    private readonly IGracePeriodService _gracePeriodService;
    private readonly ISupporterService _supporterService;
    private readonly IApplicationService _applicationService;
    private readonly IEconomicDetailService _economicDetailService;
    private readonly Services.Utilities.IBusinessTimelineService _businessTimelineService;
    private readonly Services.Domain.ElderlyBenefits.IElderlyApplicationService _elderlyApplicationService;
    private readonly IIncomeCalculationService _incomeCalculationService;
    private readonly Services.Core.IApplicationStatusService _statusService;

    public ChangeService(
        IDatabaseService db,
        IClassificationService classificationService,
        IGracePeriodService gracePeriodService,
        ISupporterService supporterService,
        IApplicationService applicationService,
        IEconomicDetailService economicDetailService,
        ILoggerService logger,
        Services.Utilities.IBusinessTimelineService businessTimelineService,
        Services.Domain.ElderlyBenefits.IElderlyApplicationService elderlyApplicationService,
        IIncomeCalculationService incomeCalculationService,
        Services.Core.IApplicationStatusService statusService) : base(logger)
    {
        _db = db;
        _classificationService = classificationService;
        _gracePeriodService = gracePeriodService;
        _supporterService = supporterService;
        _applicationService = applicationService;
        _economicDetailService = economicDetailService;
        _businessTimelineService = businessTimelineService;
        _elderlyApplicationService = elderlyApplicationService;
        _incomeCalculationService = incomeCalculationService;
        _statusService = statusService;
    }

}
