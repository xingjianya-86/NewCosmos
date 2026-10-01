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
    // 经济复核入口（表单 ExecuteReviewSaveAsync）在调用本方法前已 SaveEconomicDetailsAsync
    // 把新经济数据写回本档，此后读库拿到的"旧值"实为覆写后值（Before 快照 / old_per_capita_income 失真）。
    // 真旧值只能由调用方在覆写前捕获填充；任一未提供时回退读 application（与历史行为一致）。

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

    /// <summary>变更原因代码（MemberChangeReasonConstants）</summary>
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
public class ChangeService : BaseService, IChangeService
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

    public async Task<Result<long>> CreateChangeAsync(ChangeContext context, string beforeJson, string afterJson, CancellationToken ct = default)
    {
        var changeNo = $"CHG{DateTime.Now:yyyyMMddHHmmssfff}";

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            var sql = @"INSERT INTO nc_biz_change_records 
                       (application_id, change_no, change_type, change_category, change_reason, change_reason_type, change_date, operator_name, changed_at)
                       VALUES ($1,$2,$3,$8,$4,$5,$6,$7, NOW()) RETURNING id;";

            var result = await _db.ExecuteScalarAsync(sql, ct,
                context.ApplicationId, changeNo, context.ChangeType,
                context.ChangeReason, context.ChangeReasonType,
                context.ChangeDate, context.OperatorName,
                (object?)context.ChangeCategory);

            // 变更主记录写入失败必须中断：静默返回 0 会让调用方带着无效 ChangeId 提交事务，
            // 审计链条就断了。Result 化后由调用方显式检查并回滚。
            if (result.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    result.Message ?? "创建变更记录失败");
            }

            var changeId = result.Value;

            // 快照是变更审计的核心证据：写入失败即整体失败（避免"变更成功但无快照"断档）
            var beforeSnap = string.IsNullOrEmpty(beforeJson)
                ? Result.Success()
                : await SaveSnapshotAsync(changeId, "Before", beforeJson, ct);
            if (beforeSnap.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(beforeSnap.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    beforeSnap.Message ?? "保存变更快照失败");
            }

            var afterSnap = string.IsNullOrEmpty(afterJson)
                ? Result.Success()
                : await SaveSnapshotAsync(changeId, "After", afterJson, ct);
            if (afterSnap.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(afterSnap.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    afterSnap.Message ?? "保存变更快照失败");
            }

            await tx.CommitAsync(ct);

            LogInfo($"创建变更记录: ChangeId={changeId}");
            return Result.Success(changeId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "创建变更记录");
            return Result.FromException<long>(ex);
        }
    }

    public async Task<Result> SaveSnapshotAsync(long changeId, string snapshotType, string dataJson, CancellationToken ct = default)
    {
        var sql = @"INSERT INTO nc_biz_change_snapshots (change_id, snapshot_type, snapshot_data) VALUES ($1,$2,$3::jsonb);";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, changeId, snapshotType, dataJson);
        // 快照是变更审计的核心证据，写入失败不能吞掉（否则出现"变更成功但无快照"的断档）
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? $"保存变更快照失败: ChangeId={changeId}, Type={snapshotType}");
        return Result.Success();
    }

    /// <summary>
    /// 渐退超限减发：补写 FundChange（old&gt;new、同分类）供月报保障金减发表。
    /// 幂等：同户同 change_reason_type=户主死亡 且 old/new 金额一致的 FundChange 已存在则跳过。
    /// </summary>
    public async Task<Result<long>> CreateGraceCapFundChangeAsync(
        long applicationId,
        string newClassification,
        string oldClassification,
        decimal oldGuaranteeAmount,
        decimal newGuaranteeAmount,
        CancellationToken ct = default)
    {
        if (newGuaranteeAmount >= oldGuaranteeAmount)
            return Result.Success(0L);

        try
        {
            var existSql = @"SELECT id FROM nc_biz_change_records
                WHERE application_id = $1 AND change_type = $2
                  AND change_reason_type = $3
                  AND old_guarantee_amount = $4 AND new_guarantee_amount = $5
                  AND deleted_at IS NULL
                LIMIT 1";
            var exist = await _db.QuerySingleAsync<long?>(existSql, ct,
                applicationId, DictionaryConstants.ChangeType.FUND_CHANGE,
                ChangeReasonTypeConstants.HeadDeceased,
                oldGuaranteeAmount, newGuaranteeAmount);
            if (exist.IsSuccess && exist.Value.HasValue && exist.Value.Value > 0)
                return Result.Success(exist.Value.Value);

            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var changeContext = new ChangeContext
            {
                ApplicationId = applicationId,
                ChangeType = DictionaryConstants.ChangeType.FUND_CHANGE,
                ChangeCategory = DictionaryConstants.ChangeCategory.FUND_CHANGE,
                ChangeReason = "户主死亡进入渐退期，原保障金超户口类型上限封顶减发",
                ChangeReasonType = ChangeReasonTypeConstants.HeadDeceased,
                ChangeDate = DateTime.Today,
                OperatorName = "System"
            };
            var beforeJson = global::System.Text.Json.JsonSerializer.Serialize(new
            {
                Classification = oldClassification,
                GuaranteeAmount = oldGuaranteeAmount
            });
            var afterJson = global::System.Text.Json.JsonSerializer.Serialize(new
            {
                Classification = newClassification,
                GuaranteeAmount = newGuaranteeAmount,
                GraceCapped = true
            });

            var createResult = await CreateChangeAsync(changeContext, beforeJson, afterJson, ct);
            if (createResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(createResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    createResult.Message ?? "创建渐退减发变更记录失败");
            }
            var changeId = createResult.Value;

            var updateSql = @"UPDATE nc_biz_change_records SET
                old_classification = $1, new_classification = $2,
                old_guarantee_amount = $3, new_guarantee_amount = $4,
                triggered_grace_period = TRUE,
                changed_at = NOW()
                WHERE id = $5";
            var updateResult = await _db.ExecuteNonQueryAsync(updateSql, ct,
                oldClassification, newClassification,
                oldGuaranteeAmount, newGuaranteeAmount,
                changeId);
            if (updateResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(updateResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updateResult.Message ?? "回填渐退减发金额字段失败");
            }

            await tx.CommitAsync(ct);

            LogInfo($"渐退超限减发FundChange: ChangeId={changeId}, {oldGuaranteeAmount}→{newGuaranteeAmount}");
            return Result.Success(changeId);
        }
        catch (Exception ex)
        {
            // await using 作用域在 try 内：异常展开时 Dispose 已自动回滚，无需显式回滚
            LogException(ex, "创建渐退减发FundChange");
            return Result.FromException<long>(ex);
        }
    }

    /// <summary>接续链跨类判定读取行（EnsureChainCategoryAddAsync 用：链型/分类/保障金）</summary>
    private sealed class ChainClassificationRow
    {
        public long OriginalApplicationId { get; set; }
        public string? ClassificationResult { get; set; }
        public string? ChainType { get; set; }
        public decimal TotalGuaranteeAmount { get; set; }
    }

    /// <summary>已存在的 CategoryAdd 行（EnsureChainCategoryAddAsync 幂等判定用）</summary>
    private sealed class CategoryAddExistingRow
    {
        public long Id { get; set; }
        public string? NewClassification { get; set; }
    }

    /// <inheritdoc/>
    public async Task<Result<long?>> EnsureChainCategoryAddAsync(long applicationId, string operatorName, CancellationToken ct = default)
    {
        try
        {
            var appSql = @"SELECT original_application_id, classification_result, chain_type, total_guarantee_amount
                           FROM nc_biz_applications
                           WHERE id = $1 AND deleted_at IS NULL";
            var appResult = await _db.QuerySingleAsync<ChainClassificationRow>(appSql, ct, applicationId);
            if (appResult.IsFailure)
                return Result.Failure<long?>(appResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    appResult.Message ?? "读取接续链档案失败");

            var app = appResult.Value;
            // 非链档案（无上游）或分类未判定 → 不涉及跨类新增，显式跳过
            if (app == null || app.OriginalApplicationId <= 0 || string.IsNullOrEmpty(app.ClassificationResult))
                return Result.Success<long?>(null);

            var oldSql = @"SELECT classification_result, total_guarantee_amount
                           FROM nc_biz_applications
                           WHERE id = $1 AND deleted_at IS NULL";
            var oldResult = await _db.QuerySingleAsync<ChainClassificationRow>(oldSql, ct, app.OriginalApplicationId);
            if (oldResult.IsFailure)
                return Result.Failure<long?>(oldResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    oldResult.Message ?? "读取上游档案失败");
            var old = oldResult.Value;
            // 上游档案缺失/未分类 → 无法判断跨类，显式跳过（不猜测、不写入）
            if (old == null || string.IsNullOrEmpty(old.ClassificationResult))
                return Result.Success<long?>(null);

            var newClassification = app.ClassificationResult!;
            var oldClassification = old.ClassificationResult!;

            if (!IsCrossCategoryChange(oldClassification, newClassification))
            {
                // 判定回同大类（草稿期重新判定/上游调整）：软删可能误存的 CategoryAdd，避免月报误记转入
                var purge = await _db.ExecuteNonQueryAsync(
                    @"UPDATE nc_biz_change_records SET deleted_at = NOW()
                      WHERE application_id = $1 AND change_type = $2 AND deleted_at IS NULL",
                    ct, applicationId, DictionaryConstants.ChangeType.CATEGORY_ADD);
                if (purge.IsFailure)
                    return Result.Failure<long?>(purge.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        purge.Message ?? "清理跨类新增记录失败");
                if (purge.Value > 0)
                    LogInfo($"接续链判定回同大类，软删 CategoryAdd: ApplicationId={applicationId}, {oldClassification}→{newClassification}");
                return Result.Success<long?>(null);
            }

            var oldMajorName = ClassificationConstants.ConvertToMajorCategoryName(oldClassification);
            var newMajorName = ClassificationConstants.ConvertToMajorCategoryName(newClassification);
            var changeReason = app.ChainType switch
            {
                ChainTypeConstants.HOUSEHOLD_DEATH => $"户主死亡后 由{oldMajorName}转入{newMajorName}",
                ChainTypeConstants.MEMBER_CHANGE => $"成员变更后 由{oldMajorName}转入{newMajorName}",
                _ => $"由{oldMajorName}转入{newMajorName}"
            };
            var reasonType = app.ChainType switch
            {
                ChainTypeConstants.HOUSEHOLD_DEATH => ChangeReasonTypeConstants.HeadDeceased,
                ChainTypeConstants.MEMBER_CHANGE => ChangeReasonTypeConstants.MemberChange,
                _ => ChangeReasonTypeConstants.CrossCategoryTransfer
            };

            // 幂等：已存在则比对分类，一致跳过，不一致就地更新（月报跨类行按 change_date/id 取最新，同 ID 更新不会重复）
            var existSql = @"SELECT id, new_classification FROM nc_biz_change_records
                             WHERE application_id = $1 AND change_type = $2 AND deleted_at IS NULL
                             ORDER BY id DESC LIMIT 1";
            var exist = await _db.QuerySingleAsync<CategoryAddExistingRow>(existSql, ct,
                applicationId, DictionaryConstants.ChangeType.CATEGORY_ADD);
            if (exist.IsFailure)
                return Result.Failure<long?>(exist.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    exist.Message ?? "查询跨类新增记录失败");

            if (exist.Value != null)
            {
                if (string.Equals(exist.Value.NewClassification, newClassification, StringComparison.Ordinal))
                    return Result.Success<long?>(exist.Value.Id);

                var updateSql = @"UPDATE nc_biz_change_records SET
                    new_classification = $1, new_guarantee_amount = $2,
                    old_classification = $3, old_guarantee_amount = $4,
                    change_reason = $5, change_reason_type = $6,
                    change_date = $7, operator_name = $8, changed_at = NOW()
                    WHERE id = $9";
                var update = await _db.ExecuteNonQueryAsync(updateSql, ct,
                    newClassification, app.TotalGuaranteeAmount,
                    oldClassification, old.TotalGuaranteeAmount,
                    changeReason, reasonType,
                    DateTime.Today, operatorName,
                    exist.Value.Id);
                if (update.IsFailure)
                    return Result.Failure<long?>(update.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        update.Message ?? "更新跨类新增记录失败");
                LogInfo($"接续链 CategoryAdd 更新: ApplicationId={applicationId}, {oldClassification}→{newClassification}, ChangeId={exist.Value.Id}");
                return Result.Success<long?>(exist.Value.Id);
            }

            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var insertSql = @"INSERT INTO nc_biz_change_records
                (application_id, change_no, change_type, change_reason, change_reason_type, change_date,
                 old_classification, new_classification, old_guarantee_amount, new_guarantee_amount,
                 triggered_grace_period, triggered_stop, operator_name, changed_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10, FALSE, FALSE, $11, NOW())
                RETURNING id;";
            var insert = await _db.ExecuteScalarAsync<long>(insertSql, ct,
                applicationId,
                $"CHG{DateTime.Now:yyyyMMddHHmmssfff}",
                DictionaryConstants.ChangeType.CATEGORY_ADD,
                changeReason, reasonType, DateTime.Today,
                oldClassification, newClassification,
                old.TotalGuaranteeAmount, app.TotalGuaranteeAmount,
                operatorName);
            if (insert.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long?>(insert.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    insert.Message ?? "写入跨类新增记录失败");
            }

            await tx.CommitAsync(ct);

            LogInfo($"接续链 CategoryAdd 写入: ApplicationId={applicationId}, {oldClassification}→{newClassification}, ChangeId={insert.Value}");
            return Result.Success<long?>(insert.Value);
        }
        catch (Exception ex)
        {
            // await using 作用域在 try 内：异常展开时 Dispose 已自动回滚
            LogException(ex, "同步接续链跨类新增CategoryAdd");
            return Result.FromException<long?>(ex);
        }
    }

    public async Task<Result> SaveDetailAsync(long changeId, string fieldName, string oldValue, string newValue, CancellationToken ct = default)
    {
        var sql = @"INSERT INTO nc_biz_change_details (change_id, field_name, old_value, new_value) VALUES ($1,$2,$3,$4);";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, changeId, fieldName, oldValue, newValue);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? $"保存变更明细失败: ChangeId={changeId}, Field={fieldName}");
        return Result.Success();
    }

    /// <summary>
    /// 渐退期状态重建（复核/变更后按新分类决定去留）：
    /// - 新分类仍属低收入（IsCodeLowIncome）→ 保留：已激活维持现状（含"渐退期中间政策变动仍符合"的情形），
    ///   判定触发新渐退期则 ActivateAsync 覆盖续接（UPSERT）；
    /// - 新分类非低收入（回低保/特困/刚性/停保等）→ 结清既有渐退期。
    /// 必须在调用方已开启的环境事务内执行。
    /// </summary>
    private async Task<Result> ReconcileGracePeriodAsync(
        long applicationId, string newClassification, GracePeriodCheckResult graceCheck,
        string? oldClassification, decimal? oldGuaranteeAmount, bool forceClear = false,
        CancellationToken ct = default)
    {
        var stillLowIncome = !forceClear && ClassificationConstants.IsCodeLowIncome(newClassification);

        if (!stillLowIncome)
        {
            var clearRes = await _gracePeriodService.ClearAsync(applicationId, ct);
            if (clearRes.IsFailure)
                return Result.Failure(clearRes.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    clearRes.Message ?? "结清渐退期失败");
            return Result.Success();
        }

        if (graceCheck.IsEligible)
        {
            var activateRes = await _gracePeriodService.ActivateAsync(
                applicationId, graceCheck.Months,
                graceCheck.StartDate, graceCheck.EndDate,
                oldClassification, oldGuaranteeAmount,
                graceGrantAmount: null, ct: ct);
            if (activateRes.IsFailure)
                return Result.Failure(activateRes.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    activateRes.Message ?? "激活渐退期失败");
        }

        return Result.Success();
    }

    /// <summary>
    /// 照料护理费解析（H2）：非特困一律 0；特困集中供养 0；特困分散按能力鉴定重算。
    /// 禁止沿用旧档 DB 值——分类离开特困后旧照料费必须清零。
    /// </summary>
    private async Task<decimal> ResolveCaregiverSubsidyAsync(
        string classification, string supportMode, long applicationId, CancellationToken ct)
    {
        if (!ClassificationConstants.IsCodeDestitute(classification))
            return 0m;

        var isCentralized =
            classification is ClassificationConstants.RuralDestituteCentralized
                or ClassificationConstants.UrbanDestituteCentralized
            || string.Equals(supportMode, ClassificationConstants.SupportMode.CENTRALIZED,
                StringComparison.OrdinalIgnoreCase);

        return isCentralized
            ? 0m
            : await _classificationService.CalculateCareAllowanceAsync(applicationId, ct);
    }

    /// <summary>
    /// 补写"停保"伴随变更记录（CategoryStop + triggered_stop=true）：
    /// 档案输出的《档案_变更告知书》按该记录判定（application_id 或 new_application_id 匹配），
    /// 收入超标停保（经济复核/家庭成员变更）必须落这条记录。
    /// 必须在调用方已开启的环境事务内执行。
    /// </summary>
    private async Task<Result> WriteCategoryStopRecordAsync(
        long applicationId, string? oldClassification, string? newClassification,
        decimal oldGuaranteeAmount, decimal newGuaranteeAmount,
        bool triggeredGracePeriod, string reason, string reasonType,
        string operatorName, long newApplicationId, CancellationToken ct)
    {
        var sql = @"INSERT INTO nc_biz_change_records
            (application_id, change_no, change_type, change_reason, change_reason_type, change_date,
             old_classification, new_classification, old_guarantee_amount, new_guarantee_amount,
             triggered_grace_period, triggered_stop, operator_name, new_application_id, changed_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,NOW());";
        var result = await _db.ExecuteNonQueryAsync(sql, ct,
            applicationId,
            $"CHG{DateTime.Now:yyyyMMddHHmmssfff}",
            DictionaryConstants.ChangeType.CATEGORY_STOP,
            reason, reasonType, DateTime.Today,
            (object?)oldClassification, (object?)newClassification,
            (object?)oldGuaranteeAmount, (object?)newGuaranteeAmount,
            triggeredGracePeriod, true,
            operatorName,
            newApplicationId);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "写入停保变更记录失败");
        return Result.Success();
    }

    /// <summary>
    /// 停旧建新：复制旧档案（主表+家庭成员+经济明细+入户调查+照料人）→ 新档案（original_application_id=旧ID、Draft），
    /// 旧档案置 Stopped 并结清渐退期；支持对复制后的新档案 UPDATE 覆盖字段（分类/金额/收入等，如经济复核结果）。
    /// 返回新档案 ID 与旧成员ID→新成员ID 映射。同一事务内调用（应处于已开启事务的上下文）。
    /// </summary>
    private async Task<Result<(long NewApplicationId, Dictionary<long, long> MemberIdMap)>> StopAndRebuildAsync(
        long sourceApplicationId, string sourceType, string chainType, string changeReason, string? operatorName,
        Action<ApplicationEntity>? applyOverrides = null, CancellationToken ct = default)
    {
        // 1. 生成新申请编号
        var appNoResult = await _applicationService.GetNextApplicationNoAsync(ct);
        if (appNoResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(appNoResult.ErrorCode!, appNoResult.Message ?? "生成申请编号失败");

        // 2. 读取源档案与成员（供附属复制 + 成员ID映射）
        var srcResult = await _db.QuerySingleAsync<ApplicationEntity>(
            $"SELECT {ApplicationColumns.Full} FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL", ct, sourceApplicationId);
        if (srcResult.IsFailure || srcResult.Value == null)
            return Result.Failure<(long, Dictionary<long, long>)>(ErrorCodes.APPLICATION_NOT_FOUND, "源档案不存在");

        // 3. 复制主表 → 新档案（application_no=新、status=Draft、original_application_id=旧ID、source_type=给定类型、
        //    清空停保/渐退期字段、data_completed_at=NULL，其余复制源值）
        var newAppInsertSql = @"INSERT INTO nc_biz_applications (
                application_no, applicant_name, applicant_id_card, applicant_phone,
                gender, ethnicity, marital_status, hukou_type, education_level, political_status,
                hukou_address, disability_card_no,
                province, city, district, town, community, address,
                physical_condition, disease_name, secondary_disease_name,
                disability_type, disability_level, health_status,
                employment_status, work_unit, income_source, bank_name, bank_account,
                family_size, confirmed_family_size,
                is_single_rescue, support_mode,
                application_reason, application_reason_detail, caregiver_type, destitute_support_type, support_institution_id,
                work_income_total, business_income_total, property_income_total,
                transfer_income_total, other_income_total, total_family_income, per_capita_income, rigid_expenditure, alimony_income,
                is_eligible, classification_result,
                classified_subsidy_type, classified_subsidy_amount,
                household_monthly_guarantee_amount, person_category_protection_total_amount,
                caregiver_subsidy_amount, total_guarantee_amount,
                status, current_step,
                source_type, source_table, source_id,
                chain_type,
                original_application_id,
                first_approved_at,
                created_at, updated_at, created_by, updated_by,
                city_id, county_id, town_id, village_id,
                hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id,
                family_land_area, self_farmed_land_area, subleased_land_area, contracted_land_area,
                land_income_total, subsidy_total, total_confirmed_land_area, confirmed_person_count
            )
            SELECT $1, applicant_name, applicant_id_card, applicant_phone,
                gender, ethnicity, marital_status, hukou_type, education_level, political_status,
                hukou_address, disability_card_no,
                province, city, district, town, community, address,
                physical_condition, disease_name, secondary_disease_name,
                disability_type, disability_level, health_status,
                employment_status, work_unit, income_source, bank_name, bank_account,
                family_size, confirmed_family_size,
                is_single_rescue, support_mode,
                application_reason, application_reason_detail, caregiver_type, destitute_support_type, support_institution_id,
                work_income_total, business_income_total, property_income_total,
                transfer_income_total, other_income_total, total_family_income, per_capita_income, rigid_expenditure, alimony_income,
                is_eligible, classification_result,
                classified_subsidy_type, classified_subsidy_amount,
                household_monthly_guarantee_amount, person_category_protection_total_amount,
                caregiver_subsidy_amount, total_guarantee_amount,
                $7, $8,
                $2, source_table, source_id,
                $6,
                $3,
                first_approved_at,
                NOW(), NOW(), created_by, $4,
                city_id, county_id, town_id, village_id,
                hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id,
                family_land_area, self_farmed_land_area, subleased_land_area, contracted_land_area,
                land_income_total, subsidy_total, total_confirmed_land_area, confirmed_person_count
            FROM nc_biz_applications WHERE id = $5
            RETURNING id;";
        var newAppResult = await _db.ExecuteScalarAsync<long?>(newAppInsertSql, ct,
            appNoResult.Value, sourceType, sourceApplicationId, operatorName ?? "System", sourceApplicationId, chainType,
            ApplicationStatusCodes.DRAFT, WorkflowSteps.ENTRY_START);
        if (newAppResult.IsFailure || newAppResult.Value is not > 0)
            return Result.Failure<(long, Dictionary<long, long>)>(newAppResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                newAppResult.Message ?? "创建新档案失败");
        var newApplicationId = newAppResult.Value.Value;

        // 4. 复制家庭成员（member_id 映射）
        var oldMembersResult = await _db.QueryAsync<FamilyMember>(
            "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS NULL", ct, sourceApplicationId);
        if (oldMembersResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(oldMembersResult.ErrorCode!, oldMembersResult.Message ?? "源成员查询失败");
        var oldMembers = oldMembersResult.Value ?? new List<FamilyMember>();

        var memberCopySql = @"INSERT INTO nc_biz_family_members (
                application_id, name, id_card, is_applicant, gender, birth_date, age, ethnicity, phone,
                hukou_type, hukou_address, marital_status, education_level, political_status,
                relationship_to_head, health_status, work_capacity, is_disabled,
                disability_type, disability_level, is_severe_disability, employment_status, annual_income,
                member_category, disability_certificate_no, disease_category, disease_name,
                home_province, home_city, home_district, home_town, home_address,
                hukou_province, hukou_city, hukou_district, hukou_town, home_village, secondary_disease,
                person_type, annual_support_fee, is_support_ability, monthly_income_capacity,
                family_size, work_unit, monthly_support_fee,
                support_months, main_income_source, is_severe_disease, is_labor_exempt, created_at, updated_at, deleted_at
            )
            SELECT $1, name, id_card, is_applicant, gender, birth_date, age, ethnicity, phone,
                hukou_type, hukou_address, marital_status, education_level, political_status,
                relationship_to_head, health_status, work_capacity, is_disabled,
                disability_type, disability_level, is_severe_disability, employment_status, annual_income,
                member_category, disability_certificate_no, disease_category, disease_name,
                home_province, home_city, home_district, home_town, home_address,
                hukou_province, hukou_city, hukou_district, hukou_town, home_village, secondary_disease,
                person_type, annual_support_fee, is_support_ability, monthly_income_capacity,
                family_size, work_unit, monthly_support_fee,
                support_months, main_income_source, is_severe_disease, is_labor_exempt, NOW(), NOW(), NULL
            FROM nc_biz_family_members WHERE application_id = $2 AND deleted_at IS NULL;";
        var memberCopyResult = await _db.ExecuteNonQueryAsync(memberCopySql, ct, newApplicationId, sourceApplicationId);
        if (memberCopyResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(memberCopyResult.ErrorCode!, memberCopyResult.Message ?? "复制家庭成员失败");

        // 成员ID映射（按身份证/姓名匹配）
        var newMembersResult = await _db.QueryAsync<FamilyMember>(
            "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS NULL", ct, newApplicationId);
        if (newMembersResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(newMembersResult.ErrorCode!, newMembersResult.Message ?? "新档案成员查询失败");
        var newMembers = newMembersResult.Value ?? new List<FamilyMember>();
        var memberIdMap = new Dictionary<long, long>();
        foreach (var oldMember in oldMembers)
        {
            var match = newMembers.FirstOrDefault(m => !string.IsNullOrEmpty(m.IdCard)
                && string.Equals(m.IdCard, oldMember.IdCard, StringComparison.OrdinalIgnoreCase))
                ?? newMembers.FirstOrDefault(m => string.Equals(m.Name, oldMember.Name, StringComparison.Ordinal));
            if (match != null)
                memberIdMap[oldMember.Id] = match.Id;
        }
        long MapMemberId(long oldId) => oldId > 0 && memberIdMap.TryGetValue(oldId, out var nid) ? nid : 0;

        // 5. 复制经济明细（LoadAll → 重映射 member_id → SaveAll）
        var econLoadResult = await _economicDetailService.LoadAllAsync(sourceApplicationId, ct);
        if (econLoadResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(econLoadResult.ErrorCode!, econLoadResult.Message ?? "经济明细加载失败");
        var econ = econLoadResult.Value;
        foreach (var it in econ.LaborIncomes) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.BusinessIncomes) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.PropertyIncomes) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.TransferIncomes) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.RigidExpenditures) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.FamilyProperties) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.Vehicles) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.Machineries) it.MemberId = MapMemberId(it.MemberId);
        var econSaveResult = await _economicDetailService.SaveAllAsync(newApplicationId,
            econ.LaborIncomes, econ.BusinessIncomes, econ.PropertyIncomes, econ.TransferIncomes,
            econ.OtherIncomes, econ.Subsidies, econ.BreedingIncomes, econ.RigidExpenditures,
            econ.FamilyProperties, econ.Vehicles, econ.Machineries, econ.FinancialAssets, econ.LandRegistrations,
            econ.LandConfirmationGroups, ct);
        if (econSaveResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(econSaveResult.ErrorCode!, econSaveResult.Message ?? "经济明细复制失败");

        // 6. 复制入户调查
        var surveyCopySql = @"INSERT INTO nc_biz_household_surveys
            (application_id, survey_date, surveyor_name, surveyor_organization,
             respondent_name, respondent_relation, survey_notes,
             created_at, updated_at, deleted_at,
             application_reason, application_reason_detail, survey_conclusion)
            SELECT $1, survey_date, surveyor_name, surveyor_organization,
                   respondent_name, respondent_relation, survey_notes,
                   NOW(), NOW(), NULL,
                   application_reason, application_reason_detail, survey_conclusion
            FROM nc_biz_household_surveys
            WHERE application_id = $2 AND deleted_at IS NULL;";
        var surveyCopyResult = await _db.ExecuteNonQueryAsync(surveyCopySql, ct, newApplicationId, sourceApplicationId);
        if (surveyCopyResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(surveyCopyResult.ErrorCode!, surveyCopyResult.Message ?? "复制入户调查失败");

        // 7. 复制照料人（cared_member_id 迁移）
        var caregiversResult = await _db.QueryAsync<Caregiver>(
            "SELECT * FROM nc_biz_caregivers WHERE application_id = $1 AND deleted_at IS NULL", ct, sourceApplicationId);
        if (caregiversResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(caregiversResult.ErrorCode!, caregiversResult.Message ?? "照料人查询失败");
        var caregivers = caregiversResult.Value ?? new List<Caregiver>();
        if (caregivers.Count > 0)
        {
            // 分批多行 VALUES 单语句（19 参数/行 + 行内 NOW()/NULL 字面量；500 行/批防参数上限）
            const int caregiverParamsPerRow = 19;
            const int caregiverBatchSize = 500;
            var caregiverInsertSql = @"INSERT INTO nc_biz_caregivers
                (application_id, cared_member_id, name, id_card, phone, relationship,
                 gender, age, ethnicity, health_status, employment_status, main_income_source,
                 work_unit, position, address,
                 marital_status, hukou_type, education_level, political_status,
                 created_at, deleted_at)
                VALUES ";
            for (var offset = 0; offset < caregivers.Count; offset += caregiverBatchSize)
            {
                var chunk = caregivers.GetRange(offset, Math.Min(caregiverBatchSize, caregivers.Count - offset));
                var (valuesClause, args) = NewCosmos.Helpers.MultiRowValuesBuilder.Build(
                    chunk.Count, caregiverParamsPerRow,
                    r =>
                    {
                        var cg = chunk[r];
                        return new object?[]
                        {
                            newApplicationId, MapMemberId(cg.CaredMemberId),
                            cg.Name, cg.IdCard, cg.Phone, cg.Relationship,
                            cg.Gender, cg.Age, cg.Ethnicity, cg.HealthStatus,
                            cg.EmploymentStatus, cg.MainIncomeSource,
                            cg.WorkUnit, cg.Position, cg.Address,
                            cg.MaritalStatus, cg.HukouType, cg.EducationLevel, cg.PoliticalStatus
                        };
                    },
                    o => "(" + string.Join(",", Enumerable.Range(o, caregiverParamsPerRow).Select(n => "$" + n)) + ",NOW(),NULL)");
                var insertCaregiverResult = await _db.ExecuteNonQueryAsync(caregiverInsertSql + valuesClause + ";", ct, args);
                if (insertCaregiverResult.IsFailure)
                    return Result.Failure<(long, Dictionary<long, long>)>(insertCaregiverResult.ErrorCode!, insertCaregiverResult.Message ?? "复制照料人失败");
            }
        }

        // 8. 应用覆盖字段（经济复核结果等）：UPDATE 新档案
        if (applyOverrides != null)
        {
            var newAppEntity = new ApplicationEntity { Id = newApplicationId };
            applyOverrides(newAppEntity);
            var updateSql = @"UPDATE nc_biz_applications SET
                total_family_income = $1, per_capita_income = $2, rigid_expenditure = $3,
                family_size = $4, classification_result = $5, is_eligible = $6,
                household_monthly_guarantee_amount = $7, total_guarantee_amount = $8,
                classified_subsidy_type = $9, classified_subsidy_amount = $10,
                total_annual_income = $11, per_capita_annual_income = $12,
                caregiver_subsidy_amount = $13,
                updated_at = NOW(), updated_by = $14
                WHERE id = $15;";
            var updResult = await _db.ExecuteNonQueryAsync(updateSql, ct,
                newAppEntity.TotalFamilyIncome, newAppEntity.PerCapitaIncome, newAppEntity.RigidExpenditure,
                newAppEntity.FamilySize, newAppEntity.ClassificationResult, newAppEntity.IsEligible,
                newAppEntity.HouseholdMonthlyGuaranteeAmount, newAppEntity.TotalGuaranteeAmount,
                newAppEntity.ClassifiedSubsidyType, newAppEntity.ClassifiedSubsidyAmount,
                newAppEntity.TotalAnnualIncome, newAppEntity.PerCapitaAnnualIncome,
                newAppEntity.CaregiverSubsidyAmount,
                operatorName ?? "System", newApplicationId);
            if (updResult.IsFailure)
                return Result.Failure<(long, Dictionary<long, long>)>(updResult.ErrorCode!, updResult.Message ?? "更新新档案失败");
        }

        // 9. 停止旧档案 + 结清渐退期
        // 状态写入统一走 ApplicationStatusService：状态机校验（Draft/Refused→Stopped 拒绝，
        // 避免直写产生"从未进入保障却已停保"的脏状态）+ stop_reason/stop_date 落库 + 审计留痕
        var stopResult = await _statusService.StopAsync(
            sourceApplicationId, changeReason, DateTime.Today, operatorName ?? "System",
            allowSubmittedOverride: true, ct: ct);
        if (stopResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(stopResult.ErrorCode!, stopResult.Message ?? "停止旧档案失败");
        var graceClearResult = await _gracePeriodService.ClearAsync(sourceApplicationId, ct);
        if (graceClearResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(graceClearResult.ErrorCode!, graceClearResult.Message ?? "结清旧档案渐退期失败");

        return Result.Success((newApplicationId, memberIdMap));
    }

    /// <summary>
    /// 执行经济复核
    /// </summary>
    public async Task<Result<ChangeResult>> ExecuteEconomicReviewAsync(EconomicReviewContext context, CancellationToken ct = default)
    {
        LogInfo("执行经济复核");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            // 1. 获取当前申请数据（已软删的申请不允许再做经济复核）
            var appSql = $"SELECT {ApplicationColumns.Full} FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL";
            var appResult = await _db.QuerySingleAsync<ApplicationEntity>(appSql, ct, context.ApplicationId);

            if (!appResult.IsSuccess || appResult.Value == null)
            {
                // 提前返回必须先回滚：环境事务的连接由作用域持有，
                // 不回滚会泄漏连接并在数据库端留下 idle in transaction 会话
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }

            var application = appResult.Value;
            // 真旧值必须由调用方在 Step5 分类判定落库之前捕获（表单加载时刻）。
            // 这里读 application 的 ClassificationResult/TotalGuaranteeAmount 已被判定按钮覆写，
            // 直接用会让 needRebuild 的分类/金额比较恒判"无变化"，也会让
            // nc_biz_change_records.old_classification/old_guarantee_amount 记假旧值。
            var oldClassification = context.OldClassification ?? application.ClassificationResult ?? string.Empty;
            var oldGuaranteeAmount = context.OldGuaranteeAmount ?? application.TotalGuaranteeAmount;

            // 家庭信息修正模式：校验档案必须属于当前经济复核周期
            if (context.IsFamilyCorrection)
            {
                var currentTimeline = await _businessTimelineService.GetCurrentTimelineAsync(TimelineType.EconomicReview);
                var cycleStart = currentTimeline.CycleStartDate;
                var cycleEnd = currentTimeline.CycleEndDate;

                // 检查档案状态：必须是已批准状态
                if (application.Status != ApplicationStatusCodes.APPROVED)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(ErrorCodes.ECONOMIC_REVIEW_INVALID_STATUS, "只能修正已批准状态的档案");
                }

                // 检查档案的最近变更记录是否在当前周期内
                var changeHistory = await GetChangeHistoryAsync(context.ApplicationId, ct);
                if (changeHistory.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(changeHistory.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        changeHistory.Message ?? "变更历史查询失败");
                }
                if (changeHistory.Value != null && changeHistory.Value.Count > 0)
                {
                    var latestChange = changeHistory.Value.OrderByDescending(c => c.ChangedAt).First();
                    if (latestChange.ChangedAt < cycleStart || latestChange.ChangedAt > cycleEnd)
                    {
                        await tx.RollbackAsync(ct);
                        return Result.Failure<ChangeResult>(ErrorCodes.ECONOMIC_REVIEW_OUT_OF_PERIOD,
                            $"该档案最近一次变更不在当前复核周期内（{cycleStart:MM月dd日}-{cycleEnd:MM月dd日}）");
                    }
                }
            }

            // 变更链新建档案（户主死亡停旧建新等，original_application_id 非空）：
            // 注入上游档案原分类，供渐退期"低保→低收入"判定识别渐变前原分类；
            // 判定/落库使用上游分类，审计快照仍用本档案变更前分类
            var graceOldClassification = oldClassification;
            if (application.OriginalApplicationId > 0)
            {
                var upstreamResult = await _applicationService.GetByIdAsync(application.OriginalApplicationId, ct);
                if (upstreamResult.IsSuccess && upstreamResult.Value != null
                    && !string.IsNullOrEmpty(upstreamResult.Value.ClassificationResult))
                {
                    graceOldClassification = upstreamResult.Value.ClassificationResult!;
                    application.OriginalClassificationResult = graceOldClassification;
                }
            }

            // 变更前值必须在改写 application 之前落到局部变量，
            // 否则 Before 快照序列化的是改写后的对象，before==after，审计快照失去意义。
            // 经济复核入口（表单）在调本方法前已 SaveEconomicDetailsAsync 把新经济数据写回本档，
            // 读 application 拿到的实为覆写后值——优先取调用方覆写前捕获的 context.Old*（真旧值），
            // 同时修正 nc_biz_change_records.old_per_capita_income 列的覆写后失真。
            var oldTotalIncome = context.OldTotalFamilyIncome ?? application.TotalFamilyIncome;
            var oldPerCapitaIncome = context.OldPerCapitaIncome ?? application.PerCapitaIncome;
            var oldRigidExpenditure = context.OldRigidExpenditure ?? application.RigidExpenditure;
            var oldFamilySize = context.OldFamilySize ?? application.FamilySize;

            // 2. 获取家庭成员（查询失败不能按"空成员"继续——分类判定会漏掉
            //    残疾/疾病等成员型补贴，算出错误金额并写库）
            var membersSql = "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS null";
            var membersResult = await _db.QueryAsync<FamilyMember>(membersSql, ct, context.ApplicationId);
            if (membersResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(membersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    membersResult.Message ?? "家庭成员查询失败");
            }
            var members = membersResult.Value ?? new List<FamilyMember>();

            // 3. 更新经济信息（月值 = 年值÷12 分解显示；年值为权威口径）
            application.TotalAnnualIncome = context.NewTotalAnnualIncome;
            application.PerCapitaAnnualIncome = context.NewPerCapitaAnnualIncome;
            application.TotalFamilyIncome = context.NewTotalFamilyIncome;
            application.PerCapitaIncome = context.NewPerCapitaIncome;
            application.RigidExpenditure = context.NewRigidExpenditure;
            application.FamilySize = context.NewFamilySize;

            // 4. 重新执行分类判定（需传真实赡养抚养扶养义务人，特困判定依赖"无义务人"条件）
            var supportersResult = await _supporterService.GetByApplicationIdAsync(context.ApplicationId, ct);
            if (supportersResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(supportersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    supportersResult.Message ?? "赡养抚养扶养人查询失败");
            }
            var supporters = supportersResult.Value ?? new List<Supporter>();
            var classificationResult = await _classificationService.DetermineClassificationAsync(
                application, members, supporters, new List<Caregiver>(), ct);

            if (!classificationResult.IsSuccess)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.CLASSIFICATION_FAILED, "分类判定失败");
            }

            var newClassification = classificationResult.Value.Classification;
            var newGuaranteeAmount = classificationResult.Value.GuaranteeAmount;
            var newClassifiedAmount = classificationResult.Value.ClassifiedSubsidy.TotalAmount;
            var newClassifiedType = classificationResult.Value.ClassifiedSubsidy.Types;
            var newCaregiverAmount = await ResolveCaregiverSubsidyAsync(
                newClassification, application.SupportMode, context.ApplicationId, ct);

            // 5. 停旧建新判定：保障金额 / 分类 / 停保 三项任一变化才生成新档案。
            //    金额必须用合计口径（户月 + 分类施保 + 照料费，同 applyOverrides 落库式）比较，
            //    不能用 ChangeResult.NewGuaranteeAmount（户月口径，会恒不相等）。
            var newTotalGuaranteeAmount = newGuaranteeAmount + newClassifiedAmount + newCaregiverAmount;
            var triggeredStop = ClassificationConstants.IsCodeStop(newClassification);
            var needRebuild = oldGuaranteeAmount != newTotalGuaranteeAmount
                           || newClassification != oldClassification
                           || triggeredStop;

            // 6. 渐退期状态重建（同事务）：按复核后新分类决定去留——
            //    新分类仍属低收入 → 保留/续接（含"渐退期中间政策变动仍符合"的情形）；
            //    非低收入（回低保/停保等）→ 结清既有渐退期。
            var reconcileResult = await ReconcileGracePeriodAsync(
                application.Id, newClassification, classificationResult.Value.GracePeriod,
                graceOldClassification, oldGuaranteeAmount, ct: ct);
            if (reconcileResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(reconcileResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    reconcileResult.Message ?? "渐退期状态重建失败");
            }
            var triggeredGracePeriod = classificationResult.Value.GracePeriod.IsEligible == true;
            if (triggeredGracePeriod)
            {
                _gracePeriodService.ApplyGracePeriod(application, new GracePeriodInfo
                {
                    IsInGracePeriod = true,
                    GracePeriodMonths = classificationResult.Value.GracePeriod.Months,
                    GracePeriodStartDate = classificationResult.Value.GracePeriod.StartDate,
                    GracePeriodEndDate = classificationResult.Value.GracePeriod.EndDate,
                    OriginalClassificationResult = graceOldClassification,
                    OriginalGuaranteeAmount = oldGuaranteeAmount
                });
            }

            // 7. 停旧建新：金额/分类/停保任一变化才复制为新档案（original_application_id=旧ID、Draft），
            //    旧档案置 Stopped。新档案应用复核后的收入/分类/金额/保障金；
            //    三项均未变（如 0 元保障户复核 0→0）→ 跳过，复核结果留在原档。
            long newApplicationId;
            if (needRebuild)
            {
                var rebuildResult = await StopAndRebuildAsync(
                    context.ApplicationId,
                    DictionaryConstants.ChangeType.FUND_CHANGE,
                    ChainTypeConstants.CATEGORY_REBUILD,
                    $"经济复核后 由 {ClassificationConstants.ConvertToMajorCategoryName(oldClassification ?? "")}转入{ClassificationConstants.ConvertToMajorCategoryName(newClassification ?? "")}",
                    context.OperatorName,
                    applyOverrides: target =>
                    {
                        target.TotalFamilyIncome = application.TotalFamilyIncome;
                        target.PerCapitaIncome = application.PerCapitaIncome;
                        target.TotalAnnualIncome = application.TotalAnnualIncome;
                        target.PerCapitaAnnualIncome = application.PerCapitaAnnualIncome;
                        target.RigidExpenditure = application.RigidExpenditure;
                        target.FamilySize = application.FamilySize;
                        target.ClassificationResult = newClassification;
                        target.IsEligible = classificationResult.Value.IsEligible;
                        target.HouseholdMonthlyGuaranteeAmount = newGuaranteeAmount;
                        target.TotalGuaranteeAmount = newTotalGuaranteeAmount;
                        target.ClassifiedSubsidyType = newClassifiedType;
                        target.ClassifiedSubsidyAmount = newClassifiedAmount;
                        target.CaregiverSubsidyAmount = newCaregiverAmount;
                    },
                    ct);
                if (rebuildResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(rebuildResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        rebuildResult.Message ?? "经济复核停旧建新失败");
                }
                newApplicationId = rebuildResult.Value.NewApplicationId;
            }
            else
            {
                // 金额/分类/停保均未变：不生成新档案。原档保持有效（不置 Stopped、不清渐退期，
                // 上方渐退重建对原档的 Activate/Clear 结果自然保留）；
                // 收入列已在表单阶段写回原档，分类/金额新旧同值无需回写。
                newApplicationId = context.ApplicationId;
                LogInfo($"经济复核无金额/分类/停保变化，跳过停旧建新: ApplicationId={context.ApplicationId}");
            }

            // 8. 创建变更记录（挂在旧档案上，After 快照含新档案ID）
            var changeContext = new ChangeContext
            {
                ApplicationId = context.ApplicationId,
                ChangeType = DictionaryConstants.ChangeType.FUND_CHANGE,
                ChangeReason = context.ReviewReason ?? "经济复核后",
                ChangeDate = DateTime.Today,
                OperatorName = context.OperatorName
            };

            // Before 快照用改写前捕获的局部变量
            var beforeJson = JsonSerializer.Serialize(new
            {
                Classification = oldClassification,
                GuaranteeAmount = oldGuaranteeAmount,
                TotalIncome = oldTotalIncome,
                PerCapitaIncome = oldPerCapitaIncome,
                RigidExpenditure = oldRigidExpenditure,
                FamilySize = oldFamilySize,
                // 表单覆写前捕获的真旧值：Old* 供审计核对，OldTotalAnnualIncome/Components
                // 供档案渐退审批表「变动情况说明」分项对比（旧档行在复核链已被覆写，不再可信）
                OldFamilySize = context.OldFamilySize,
                OldTotalFamilyIncome = context.OldTotalFamilyIncome,
                OldTotalAnnualIncome = context.OldTotalAnnualIncome,
                OldPerCapitaIncome = context.OldPerCapitaIncome,
                Components = context.OldComponents
            });

            var afterJson = JsonSerializer.Serialize(new
            {
                Classification = newClassification,
                GuaranteeAmount = newGuaranteeAmount,
                TotalIncome = application.TotalFamilyIncome,
                PerCapitaIncome = application.PerCapitaIncome,
                RigidExpenditure = application.RigidExpenditure,
                FamilySize = application.FamilySize,
                NewApplicationId = newApplicationId
            });

            var changeIdResult = await CreateChangeAsync(changeContext, beforeJson, afterJson, ct);
            if (changeIdResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(changeIdResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    changeIdResult.Message ?? "创建变更记录失败");
            }
            var changeId = changeIdResult.Value;

            // 填充 nc_biz_change_records 的结构化对比字段（与经济复核审计对应）
            var updateChangeSql = @"UPDATE nc_biz_change_records SET 
                change_reason_type = $1,
                old_classification = $2, new_classification = $3,
                old_per_capita_income = $4, new_per_capita_income = $5,
                old_guarantee_amount = $6, new_guarantee_amount = $7,
                triggered_grace_period = $8, triggered_stop = $9,
                new_application_id = $11,
                changed_at = NOW()
                WHERE id = $10;";
            var updateChangeResult = await _db.ExecuteNonQueryAsync(updateChangeSql, ct,
                ChangeReasonTypeConstants.EconomicReview, oldClassification, newClassification,
                oldPerCapitaIncome, application.PerCapitaIncome,
                oldGuaranteeAmount, newTotalGuaranteeAmount,
                triggeredGracePeriod, triggeredStop,
                changeId,
                // 无重建时留 NULL：写自身 ID 会形成 new_application_id = application_id 的自引用，
                // 档案链归一（DynamicManagementRecordService 递归 CTE）无法区分"无新档"与"新档=旧档"。
                needRebuild ? newApplicationId : null);
            if (updateChangeResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(updateChangeResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updateChangeResult.Message ?? "更新变更记录对比字段失败");
            }

            // 跨大类变更（低保↔低收入/特困/刚性支出 等）：补写"原类别停止 + 新类别新增"两条记录，
            // 供月报"停保汇总表 / 新增救助明细"正确体现。
            if (IsCrossCategoryChange(oldClassification, newClassification))
            {
                var categoryReason = $"经济复核后 由 {ClassificationConstants.ConvertToMajorCategoryName(oldClassification ?? "")}转入{ClassificationConstants.ConvertToMajorCategoryName(newClassification ?? "")}";
                var crossSql = @"INSERT INTO nc_biz_change_records
                    (application_id, change_no, change_type, change_reason, change_reason_type, change_date,
                     old_classification, new_classification, old_guarantee_amount, new_guarantee_amount,
                     triggered_grace_period, triggered_stop, operator_name, new_application_id, changed_at)
                    VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$20,NOW()),
                           ($14,$2,$15,$4,$5,$6,$16,$17,$18,$19,$11,$12,$13,$21,NOW());";
                var crossResult = await _db.ExecuteNonQueryAsync(crossSql, ct,
                    context.ApplicationId,
                    $"CHG{DateTime.Now:yyyyMMddHHmmssfff}",
                    DictionaryConstants.ChangeType.CATEGORY_STOP,
                    categoryReason, ChangeReasonTypeConstants.EconomicReview, DateTime.Today,
                    (object?)oldClassification, (object?)newClassification,
                    (object?)oldGuaranteeAmount, (object?)newTotalGuaranteeAmount,
                    triggeredGracePeriod, triggeredStop,
                    context.OperatorName,
                    newApplicationId,
                    DictionaryConstants.ChangeType.CATEGORY_ADD,
                    (object?)null, (object?)newClassification,
                    (object?)null, (object?)newTotalGuaranteeAmount,
                    (object?)newApplicationId, (object?)null);
                if (crossResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(crossResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        crossResult.Message ?? "写入跨类变更记录失败");
                }
                LogInfo($"跨大类变更记录: {oldClassification} → {newClassification}（Stop/Add，新档案ID={newApplicationId}）");
            }
            else if (triggeredStop)
            {
                // 同大类转停保（如低保→收入超标）：补写 CategoryStop，供《档案_变更告知书》输出
                var stopWrite = await WriteCategoryStopRecordAsync(
                    context.ApplicationId, oldClassification, newClassification,
                    oldGuaranteeAmount, newTotalGuaranteeAmount, triggeredGracePeriod,
                    context.ReviewReason ?? "经济复核后", ChangeReasonTypeConstants.EconomicReview,
                    context.OperatorName, newApplicationId, ct);
                if (stopWrite.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(stopWrite.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        stopWrite.Message ?? "写入停保变更记录失败");
                }
                LogInfo($"经济复核停保记录: {oldClassification} → {newClassification}（新档案ID={newApplicationId}）");
            }

            await tx.CommitAsync(ct);

            LogInfo($"经济复核完成: 原分类={oldClassification} 新分类={newClassification}");

            // 联动：家庭经济/身份变化后，对已建高龄档案/在册发放成员写入"待复核"队列（失败不阻断）
            try
            {
                var reviewResult = await _elderlyApplicationService.TriggerReviewsForHouseholdAsync(
                    context.ApplicationId, NewCosmos.Constants.ElderlyBenefitConstants.ReviewTriggerLowIncomeChange, ct);
                if (reviewResult.IsSuccess && reviewResult.Value > 0)
                    LogInfo($"经济复核联动写入高龄待复核: ApplicationId={context.ApplicationId}, Count={reviewResult.Value}");
            }
            catch (Exception ex)
            {
                LogWarn($"高龄待复核联动失败（不阻断）: ApplicationId={context.ApplicationId}, {ex.Message}");
            }

            return Result.Success(new ChangeResult
            {
                ChangeId = changeId,
                IsSuccess = true,
                OldClassification = oldClassification ?? string.Empty,
                NewClassification = newClassification ?? string.Empty,
                OldGuaranteeAmount = oldGuaranteeAmount,
                NewGuaranteeAmount = newGuaranteeAmount,
                TriggeredGracePeriod = triggeredGracePeriod,
                TriggeredStop = triggeredStop,
                Rebuilt = needRebuild,
                NewApplicationId = newApplicationId
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, ChangeReasonTypeConstants.EconomicReview);
            return Result.FromException<ChangeResult>(ex);
        }
    }

    /// <summary>
    /// 执行家庭成员变更（增员/减员后重新认定）：重判分类 → 渐退期重建 → 停旧建新（chain_type=MemberChange）
    /// → 变更记录（change_category=MemberChange，供"增减员调整表"输出）→ 跨类/停保补记 CategoryStop
    /// （"停保告知书"依赖 triggered_stop=true 的 CategoryStop 记录）
    /// </summary>
    public async Task<Result<ChangeResult>> ExecuteMemberChangeAsync(MemberChangeContext context, CancellationToken ct = default)
    {
        LogInfo("执行家庭成员变更");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            // 1. 当前申请（已软删的申请不允许再做成员变更）
            var appSql = $"SELECT {ApplicationColumns.Full} FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL";
            var appResult = await _db.QuerySingleAsync<ApplicationEntity>(appSql, ct, context.ApplicationId);
            if (!appResult.IsSuccess || appResult.Value == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }

            var application = appResult.Value;
            // 真旧值由调用方在 Step5 分类判定落库之前捕获（表单加载时刻）。
            // 直接读 application 会被判定按钮覆写，导致停旧建新判定恒判"无变化"。
            var oldClassification = context.OldClassification ?? application.ClassificationResult ?? string.Empty;
            var oldGuaranteeAmount = context.OldGuaranteeAmount ?? application.TotalGuaranteeAmount;

            // 变更链新建档案（户主死亡/既往复核停旧建新）：注入上游分类供渐退期"低保→低收入"判定
            var graceOldClassification = oldClassification;
            if (application.OriginalApplicationId > 0)
            {
                var upstreamResult = await _applicationService.GetByIdAsync(application.OriginalApplicationId, ct);
                if (upstreamResult.IsSuccess && upstreamResult.Value != null
                    && !string.IsNullOrEmpty(upstreamResult.Value.ClassificationResult))
                {
                    graceOldClassification = upstreamResult.Value.ClassificationResult!;
                    application.OriginalClassificationResult = graceOldClassification;
                }
            }

            // 变更前值必须在改写 application 之前落到局部变量（Before 快照用）
            var oldTotalIncome = application.TotalFamilyIncome;
            var oldPerCapitaIncome = application.PerCapitaIncome;
            var oldRigidExpenditure = application.RigidExpenditure;
            var oldFamilySize = context.OldFamilySize > 0 ? context.OldFamilySize : application.FamilySize;

            // 2. 家庭成员（表单已保存增删结果；查询失败不能按"空成员"继续，否则漏算成员型补贴）
            var membersSql = "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS null";
            var membersResult = await _db.QueryAsync<FamilyMember>(membersSql, ct, context.ApplicationId);
            if (membersResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(membersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    membersResult.Message ?? "家庭成员查询失败");
            }
            var members = membersResult.Value ?? new List<FamilyMember>();

            // 3. 应用新经济/人数口径（年值权威，月值为分解显示）
            application.TotalAnnualIncome = context.NewTotalAnnualIncome;
            application.PerCapitaAnnualIncome = context.NewPerCapitaAnnualIncome;
            application.TotalFamilyIncome = context.NewTotalFamilyIncome;
            application.PerCapitaIncome = context.NewPerCapitaIncome;
            application.RigidExpenditure = context.NewRigidExpenditure;
            application.FamilySize = context.NewFamilySize;

            // 4. 重新执行分类判定（需传真实赡养抚养扶养义务人，特困判定依赖"无义务人"条件）
            var supportersResult = await _supporterService.GetByApplicationIdAsync(context.ApplicationId, ct);
            if (supportersResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(supportersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    supportersResult.Message ?? "赡养抚养扶养人查询失败");
            }
            var supporters = supportersResult.Value ?? new List<Supporter>();
            var classificationResult = await _classificationService.DetermineClassificationAsync(
                application, members, supporters, new List<Caregiver>(), ct);
            if (!classificationResult.IsSuccess)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.CLASSIFICATION_FAILED, "分类判定失败");
            }

            var newClassification = classificationResult.Value.Classification;
            var newGuaranteeAmount = classificationResult.Value.GuaranteeAmount;
            var newClassifiedAmount = classificationResult.Value.ClassifiedSubsidy.TotalAmount;
            var newClassifiedType = classificationResult.Value.ClassifiedSubsidy.Types;
            var newCaregiverAmount = await ResolveCaregiverSubsidyAsync(
                newClassification, application.SupportMode, context.ApplicationId, ct);

            // 5. 渐退期状态重建（同复核：新分类仍低收入 → 保留/续接；否则结清）
            var reconcileResult = await ReconcileGracePeriodAsync(
                application.Id, newClassification, classificationResult.Value.GracePeriod,
                graceOldClassification, oldGuaranteeAmount, ct: ct);
            if (reconcileResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(reconcileResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    reconcileResult.Message ?? "渐退期状态重建失败");
            }
            var triggeredGracePeriod = classificationResult.Value.GracePeriod.IsEligible == true;
            if (triggeredGracePeriod)
            {
                _gracePeriodService.ApplyGracePeriod(application, new GracePeriodInfo
                {
                    IsInGracePeriod = true,
                    GracePeriodMonths = classificationResult.Value.GracePeriod.Months,
                    GracePeriodStartDate = classificationResult.Value.GracePeriod.StartDate,
                    GracePeriodEndDate = classificationResult.Value.GracePeriod.EndDate,
                    OriginalClassificationResult = graceOldClassification,
                    OriginalGuaranteeAmount = oldGuaranteeAmount
                });
            }

            // 6. 是否停保
            bool triggeredStop = ClassificationConstants.IsCodeStop(newClassification);

            var changeType = string.IsNullOrEmpty(context.ChangeType)
                ? DictionaryConstants.ChangeType.MEMBER_MODIFY
                : context.ChangeType;
            var reasonText = string.IsNullOrWhiteSpace(context.ChangeSummary)
                ? (string.IsNullOrWhiteSpace(context.ChangeReason) ? "家庭成员变更" : context.ChangeReason)
                : $"{context.ChangeReason}（{context.ChangeSummary}）";

            // 7. 停旧建新：旧档案 Stopped，新档案 Draft 承载变更后成员/经济/分类/金额
            var rebuiltReason = $"家庭成员变更后 由 {ClassificationConstants.ConvertToMajorCategoryName(oldClassification ?? "")}转入{ClassificationConstants.ConvertToMajorCategoryName(newClassification ?? "")}";
            var rebuildResult = await StopAndRebuildAsync(
                context.ApplicationId,
                changeType,
                ChainTypeConstants.MEMBER_CHANGE,
                rebuiltReason,
                context.OperatorName,
                applyOverrides: target =>
                {
                    target.TotalFamilyIncome = application.TotalFamilyIncome;
                    target.PerCapitaIncome = application.PerCapitaIncome;
                    target.TotalAnnualIncome = application.TotalAnnualIncome;
                    target.PerCapitaAnnualIncome = application.PerCapitaAnnualIncome;
                    target.RigidExpenditure = application.RigidExpenditure;
                    target.FamilySize = application.FamilySize;
                    target.ClassificationResult = newClassification;
                    target.IsEligible = classificationResult.Value.IsEligible;
                    target.HouseholdMonthlyGuaranteeAmount = newGuaranteeAmount;
                    target.TotalGuaranteeAmount = newGuaranteeAmount + newClassifiedAmount + newCaregiverAmount;
                    target.ClassifiedSubsidyType = newClassifiedType;
                    target.ClassifiedSubsidyAmount = newClassifiedAmount;
                    target.CaregiverSubsidyAmount = newCaregiverAmount;
                },
                ct);
            if (rebuildResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(rebuildResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    rebuildResult.Message ?? "家庭成员变更停旧建新失败");
            }
            var newApplicationId = rebuildResult.Value.NewApplicationId;

            // 8. 变更记录（挂在旧档案上；After 快照含新档案ID）
            var changeContext = new ChangeContext
            {
                ApplicationId = context.ApplicationId,
                ChangeType = changeType,
                ChangeCategory = DictionaryConstants.ChangeCategory.MEMBER_CHANGE,
                ChangeReason = reasonText,
                ChangeReasonType = ChangeReasonTypeConstants.MemberChange,
                ChangeDate = DateTime.Today,
                OperatorName = context.OperatorName
            };

            var beforeJson = JsonSerializer.Serialize(new
            {
                Classification = oldClassification,
                GuaranteeAmount = oldGuaranteeAmount,
                TotalIncome = oldTotalIncome,
                PerCapitaIncome = oldPerCapitaIncome,
                RigidExpenditure = oldRigidExpenditure,
                FamilySize = oldFamilySize
            });

            var entries = context.Entries ?? new List<MemberChangeEntry>();
            var addedDetails = entries
                .Where(e => string.Equals(e.Direction, DictionaryConstants.ChangeType.MEMBER_ADD, StringComparison.OrdinalIgnoreCase))
                .Select(e => new
                {
                    e.Name, e.IdCard, e.RelationshipToHead, e.MemberCategory,
                    e.ReasonCode, e.ReasonName,
                    EventDate = e.EventDate.ToString("yyyy-MM-dd"),
                    e.Remark
                }).ToList();
            var removedDetails = entries
                .Where(e => string.Equals(e.Direction, DictionaryConstants.ChangeType.MEMBER_REMOVE, StringComparison.OrdinalIgnoreCase))
                .Select(e => new
                {
                    e.Name, e.IdCard, e.RelationshipToHead, e.MemberCategory,
                    e.ReasonCode, e.ReasonName,
                    EventDate = e.EventDate.ToString("yyyy-MM-dd"),
                    e.Remark
                }).ToList();

            var afterJson = JsonSerializer.Serialize(new
            {
                Classification = newClassification,
                GuaranteeAmount = newGuaranteeAmount,
                TotalIncome = application.TotalFamilyIncome,
                PerCapitaIncome = application.PerCapitaIncome,
                RigidExpenditure = application.RigidExpenditure,
                FamilySize = application.FamilySize,
                ChangeSummary = context.ChangeSummary,
                AddedMembers = addedDetails,
                RemovedMembers = removedDetails,
                NewApplicationId = newApplicationId
            });

            var changeIdResult = await CreateChangeAsync(changeContext, beforeJson, afterJson, ct);
            if (changeIdResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(changeIdResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    changeIdResult.Message ?? "创建变更记录失败");
            }
            var changeId = changeIdResult.Value;

            // 填充结构化对比字段（与经济复核审计对齐）
            var updateChangeSql = @"UPDATE nc_biz_change_records SET 
                change_reason_type = $1,
                old_classification = $2, new_classification = $3,
                old_per_capita_income = $4, new_per_capita_income = $5,
                old_guarantee_amount = $6, new_guarantee_amount = $7,
                triggered_grace_period = $8, triggered_stop = $9,
                new_application_id = $11,
                changed_at = NOW()
                WHERE id = $10;";
            var updateChangeResult = await _db.ExecuteNonQueryAsync(updateChangeSql, ct,
                ChangeReasonTypeConstants.MemberChange, oldClassification, newClassification,
                oldPerCapitaIncome, application.PerCapitaIncome,
                oldGuaranteeAmount, newGuaranteeAmount,
                triggeredGracePeriod, triggeredStop,
                changeId, newApplicationId);
            if (updateChangeResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(updateChangeResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updateChangeResult.Message ?? "更新变更记录对比字段失败");
            }

            // 逐人变更明细：结构化留痕（nc_biz_change_details），增/减员原因可查询审计
            if (entries.Count > 0)
            {
                var detailSb = new StringBuilder();
                var detailParams = new List<object>(entries.Count * 5);
                for (var i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (i > 0) detailSb.Append(',');
                    var p = i * 5;
                    detailSb.Append($"(${p + 1},${p + 2},${p + 3},${p + 4},${p + 5})");
                    detailParams.Add(changeId);
                    detailParams.Add(e.Direction);
                    detailParams.Add(e.Name);
                    detailParams.Add($"{e.Name}({e.IdCard}) {e.MemberCategory} {e.RelationshipToHead}".Trim());
                    detailParams.Add($"{e.ReasonName}|{e.EventDate:yyyy-MM-dd}|{e.Remark}");
                }
                var detailSql = $"INSERT INTO nc_biz_change_details (change_id, field_name, field_label, old_value, new_value) VALUES {detailSb};";
                var detailResult = await _db.ExecuteNonQueryAsync(detailSql, ct, detailParams.ToArray());
                if (detailResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(detailResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        detailResult.Message ?? "写入变更明细失败");
                }
            }

            // 死亡减员联动：减员原因为"人员死亡"时批量写死亡记录（与户主死亡同表，供统计/公示退出识别）
            // 单语句 CTE 保留每行 WHERE NOT EXISTS 去重语义（同批重复证件号由 DISTINCT ON 收口）
            var deathEntries = entries.Where(e =>
                string.Equals(e.Direction, DictionaryConstants.ChangeType.MEMBER_REMOVE, StringComparison.OrdinalIgnoreCase)
                && MemberChangeReasonConstants.IsDeath(e.ReasonCode)).ToList();
            if (deathEntries.Count > 0)
            {
                const int deathParamsPerRow = 9;
                var (deathValues, deathArgs) = NewCosmos.Helpers.MultiRowValuesBuilder.Build(
                    deathEntries.Count, deathParamsPerRow,
                    i =>
                    {
                        var e = deathEntries[i];
                        return new object?[]
                        {
                            context.ApplicationId, e.MemberId, e.Name, e.IdCard, e.RelationshipToHead,
                            e.EventDate,
                            string.IsNullOrWhiteSpace(e.Remark) ? "成员变更-人员死亡" : e.Remark,
                            e.Remark, context.OperatorName
                        };
                    });
                var deathSql = @"WITH input(application_id, member_id, member_name, member_id_card, relationship_to_head, death_date, death_reason, remark, operator_name) AS (
                        VALUES " + deathValues + @")
                    INSERT INTO nc_biz_death_records
                        (application_id, member_id, member_name, member_id_card, relationship_to_head,
                         death_date, death_reason, is_household_head, remark, operator_name, created_at)
                    SELECT DISTINCT ON (member_id_card)
                        application_id, member_id, member_name, member_id_card, relationship_to_head,
                        death_date, death_reason, false, remark, operator_name, NOW()
                    FROM input i
                    WHERE NOT EXISTS (
                        SELECT 1 FROM nc_biz_death_records d
                        WHERE d.application_id = i.application_id AND d.member_id_card = i.member_id_card)
                    ORDER BY member_id_card";
                var deathResult = await _db.ExecuteNonQueryAsync(deathSql, ct, deathArgs);
                if (deathResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(deathResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        deathResult.Message ?? "写入死亡记录失败");
                }
            }

            // 9. 跨大类变更补写"原类别停止+新类别新增"；同大类但触发停保时补写 CategoryStop
            if (IsCrossCategoryChange(oldClassification, newClassification))
            {
                var categoryReason = $"家庭成员变更后 由 {ClassificationConstants.ConvertToMajorCategoryName(oldClassification ?? "")}转入{ClassificationConstants.ConvertToMajorCategoryName(newClassification ?? "")}";
                var crossSql = @"INSERT INTO nc_biz_change_records
                    (application_id, change_no, change_type, change_reason, change_reason_type, change_date,
                     old_classification, new_classification, old_guarantee_amount, new_guarantee_amount,
                     triggered_grace_period, triggered_stop, operator_name, new_application_id, changed_at)
                    VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$20,NOW()),
                           ($14,$2,$15,$4,$5,$6,$16,$17,$18,$19,$11,$12,$13,$21,NOW());";
                var crossResult = await _db.ExecuteNonQueryAsync(crossSql, ct,
                    context.ApplicationId,
                    $"CHG{DateTime.Now:yyyyMMddHHmmssfff}",
                    DictionaryConstants.ChangeType.CATEGORY_STOP,
                    categoryReason, ChangeReasonTypeConstants.MemberChange, DateTime.Today,
                    (object?)oldClassification, (object?)newClassification,
                    (object?)oldGuaranteeAmount, (object?)newGuaranteeAmount,
                    triggeredGracePeriod, triggeredStop,
                    context.OperatorName,
                    newApplicationId,
                    DictionaryConstants.ChangeType.CATEGORY_ADD,
                    (object?)null, (object?)newClassification,
                    (object?)null, (object?)newGuaranteeAmount,
                    (object?)newApplicationId, (object?)null);
                if (crossResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(crossResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        crossResult.Message ?? "写入跨类变更记录失败");
                }
                LogInfo($"家庭成员变更跨类记录: {oldClassification} → {newClassification}（Stop/Add，新档案ID={newApplicationId}）");
            }
            else if (triggeredStop)
            {
                var stopWrite = await WriteCategoryStopRecordAsync(
                    context.ApplicationId, oldClassification, newClassification,
                    oldGuaranteeAmount, newGuaranteeAmount, triggeredGracePeriod,
                    reasonText, ChangeReasonTypeConstants.MemberChange,
                    context.OperatorName, newApplicationId, ct);
                if (stopWrite.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(stopWrite.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        stopWrite.Message ?? "写入停保变更记录失败");
                }
                LogInfo($"家庭成员变更停保记录: {oldClassification} → {newClassification}（新档案ID={newApplicationId}）");
            }

            await tx.CommitAsync(ct);

            LogInfo($"家庭成员变更完成: 原分类={oldClassification} 新分类={newClassification} 新档案ID={newApplicationId}");

            // 联动：家庭人口变化后，对已建高龄档案/在册发放成员写入"待复核"队列（失败不阻断）
            try
            {
                var reviewResult = await _elderlyApplicationService.TriggerReviewsForHouseholdAsync(
                    context.ApplicationId, NewCosmos.Constants.ElderlyBenefitConstants.ReviewTriggerLowIncomeChange, ct);
                if (reviewResult.IsSuccess && reviewResult.Value > 0)
                    LogInfo($"家庭成员变更联动写入高龄待复核: ApplicationId={context.ApplicationId}, Count={reviewResult.Value}");
            }
            catch (Exception ex)
            {
                LogWarn($"高龄待复核联动失败（不阻断）: ApplicationId={context.ApplicationId}, {ex.Message}");
            }

            return Result.Success(new ChangeResult
            {
                ChangeId = changeId,
                IsSuccess = true,
                OldClassification = oldClassification ?? string.Empty,
                NewClassification = newClassification ?? string.Empty,
                OldGuaranteeAmount = oldGuaranteeAmount,
                NewGuaranteeAmount = newGuaranteeAmount,
                TriggeredGracePeriod = triggeredGracePeriod,
                TriggeredStop = triggeredStop,
                NewApplicationId = newApplicationId
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, ChangeReasonTypeConstants.MemberChange);
            return Result.FromException<ChangeResult>(ex);
        }
    }

    /// <summary>
    /// 判断是否为跨大类变更（低保/低收入/特困/刚性支出 四大类之间跨越）。
    /// 与月报 CategoryCodes 分组一致：低保含单人保（RuralLowIncomeSingle/UrbanLowIncomeSingle）。
    /// 同大类内调整（如低保→低保单人）不算跨类；任一分组无法识别（空/无效码）不算跨类。
    /// </summary>
    private static bool IsCrossCategoryChange(string? oldClassification, string? newClassification)
    {
        var oldGroup = GetCategoryGroup(oldClassification);
        var newGroup = GetCategoryGroup(newClassification);
        return !string.IsNullOrEmpty(oldGroup) && !string.IsNullOrEmpty(newGroup)
            && !string.Equals(oldGroup, newGroup, StringComparison.Ordinal);
    }

    /// <summary>
    /// 分类码 → 四大类分组（与 MonthlyReportService.CategoryCodes 口径一致）。
    /// </summary>
    private static string? GetCategoryGroup(string? classification)
    {
        if (string.IsNullOrEmpty(classification)) return null;
        if (ClassificationConstants.IsCodeSubsistence(classification)) return "最低生活保障";
        if (ClassificationConstants.IsCodeLowIncome(classification)) return "最低生活保障边缘家庭";
        if (ClassificationConstants.IsCodeDestitute(classification)) return "特困人员";
        if (ClassificationConstants.IsCodeRigidExpenditure(classification)) return "刚性支出困难家庭";
        return null;
    }

    /// <summary>
    /// 处理成员死亡
    /// </summary>
    public async Task<Result<ChangeResult>> ProcessMemberDeathAsync(MemberDeathContext context, CancellationToken ct = default)
    {
        LogInfo($"处理成员死亡: ApplicationId={context.ApplicationId}");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            // 1. 获取当前申请数据（已软删的申请不允许再做成员死亡处理）
            var appSql = $"SELECT {ApplicationColumns.Full} FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL";
            var appResult = await _db.QuerySingleAsync<ApplicationEntity>(appSql, ct, context.ApplicationId);

            if (!appResult.IsSuccess || appResult.Value == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }

            var application = appResult.Value;
            var oldClassification = application.ClassificationResult ?? string.Empty;
            var oldGuaranteeAmount = application.TotalGuaranteeAmount;
            // 变更前家庭人数在任何改写发生前捕获，Before 快照直接用它，
            // 不再用 FamilySize + 1 反推（改写后反推得到的是错的）
            var oldFamilySize = application.FamilySize;

            // 2. 标记成员死亡（nc_biz_family_members 无 deleted_by 列，仅置 deleted_at）
            var updateMemberSql = @"UPDATE nc_biz_family_members
                                   SET deleted_at = NOW()
                                   WHERE id = $1 AND application_id = $2;";
            var markResult = await _db.ExecuteNonQueryAsync(updateMemberSql, ct, context.MemberId, context.ApplicationId);
            if (markResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(markResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    markResult.Message ?? "标记成员死亡失败");
            }

            // 3. 创建死亡记录
            var deathSql = @"INSERT INTO nc_biz_death_records
                            (application_id, member_id, member_name, member_id_card, death_date, death_reason, operator_name)
                            VALUES ($1,$2,$3,$4,$5,$6,$7);";
            var deathInsertResult = await _db.ExecuteNonQueryAsync(deathSql, ct,
                context.ApplicationId, context.MemberId, context.MemberName,
                context.MemberIdCard, context.DeathDate, context.DeathReason, context.OperatorName);
            if (deathInsertResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(deathInsertResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    deathInsertResult.Message ?? "创建死亡记录失败");
            }

            // 4. 重新计算家庭人数。
            //    统计失败绝不能回退为"1 人"继续算：家庭人数直接决定保障金额，
            //    按错误人数算出的补贴会写进数据库，必须回滚并失败返回。
            //    剔除赡养抚养扶养人（member_category='Support'），该人群不计入共同生活家庭人数。
            var countSql = @"SELECT COUNT(*) FROM nc_biz_family_members 
                            WHERE application_id = $1 AND deleted_at IS null
                              AND (member_category IS NULL OR member_category <> 'Support')";
            var countResult = await _db.ExecuteScalarAsync(countSql, ct, context.ApplicationId);
            if (countResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(countResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    countResult.Message ?? "家庭人数统计失败");
            }
            var newFamilySize = (int)countResult.Value;

            // 5. 获取剩余成员（同经济复核：查询失败不能按空成员继续做分类判定）
            var membersSql = "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS null";
            var membersResult = await _db.QueryAsync<FamilyMember>(membersSql, ct, context.ApplicationId);
            if (membersResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(membersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    membersResult.Message ?? "家庭成员查询失败");
            }
            var members = membersResult.Value ?? new List<FamilyMember>();

            // 6. 更新家庭人数
            application.FamilySize = newFamilySize;

            // 7. 重新执行分类判定（需传真实赡养抚养扶养义务人，特困判定依赖"无义务人"条件）
            var supportersResult = await _supporterService.GetByApplicationIdAsync(context.ApplicationId, ct);
            if (supportersResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(supportersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    supportersResult.Message ?? "赡养抚养扶养人查询失败");
            }
            var supporters = supportersResult.Value ?? new List<Supporter>();
            var classificationResult = await _classificationService.DetermineClassificationAsync(
                application, members, supporters, new List<Caregiver>(), ct);

            string newClassification = oldClassification;
            decimal newGuaranteeAmount = oldGuaranteeAmount;
            bool triggeredStop = false;

            if (classificationResult.IsSuccess)
            {
                newClassification = classificationResult.Value.Classification;
                newGuaranteeAmount = classificationResult.Value.GuaranteeAmount;
                triggeredStop = ClassificationConstants.IsCodeStop(newClassification);

                // 保障金三项合成（户月 + 分类施保 + 照料），与 GuaranteeAmountService 同口径；
                // 照料费：特困分散按能力鉴定重算，集中/非特困一律 0（禁止沿用旧档 DB 值）
                var newClassifiedAmount = classificationResult.Value.ClassifiedSubsidy.TotalAmount;
                var newClassifiedType = classificationResult.Value.ClassifiedSubsidy.Types;
                var newCaregiverAmount = await ResolveCaregiverSubsidyAsync(
                    newClassification, application.SupportMode, application.Id, ct);

                // 人数变化后重算人均（年值权威；月值 = 年÷人数÷12 一次舍入）
                if (newFamilySize > 0)
                {
                    if (application.TotalAnnualIncome > 0)
                    {
                        application.PerCapitaAnnualIncome = Math.Round(application.TotalAnnualIncome / newFamilySize, 2);
                        application.PerCapitaIncome = Math.Round(application.TotalAnnualIncome / newFamilySize / 12m, 2);
                    }
                    else
                    {
                        application.PerCapitaAnnualIncome = 0m;
                        application.PerCapitaIncome = Math.Round(application.TotalFamilyIncome / newFamilySize, 2);
                    }
                }

                // 更新申请
                application.ClassificationResult = newClassification;
                application.IsEligible = classificationResult.Value.IsEligible;
                application.ClassifiedSubsidyType = newClassifiedType;
                application.ClassifiedSubsidyAmount = newClassifiedAmount;
                application.CaregiverSubsidyAmount = newCaregiverAmount;
                application.HouseholdMonthlyGuaranteeAmount = newGuaranteeAmount;
                application.TotalGuaranteeAmount = newGuaranteeAmount + newClassifiedAmount + newCaregiverAmount;
                application.FamilySize = newFamilySize;
                application.UpdatedAt = DateTime.Now;
                application.UpdatedBy = context.OperatorName;

                // 如果最后成员死亡，停止档案。
                // 状态变更必须过状态机，不能从任意状态直写 "Stopped"：
                // - Draft（草稿）/ Refused（不予受理）从未进入保障，不存在"停保"一说，
                //   直写 Stopped 属于绕过状态机产生脏状态 → 拒绝（INVALID_TRANSITION）并回滚整个死亡处理；
                // - 业务意图确实是"最后成员死亡 → 停保"（死亡是客观事件），因此仅对
                //   Draft/Refused 阻断：Approved/Completed→Stopped 状态机本就允许；
                //   Submitted→Stopped 严格状态机不允许，但阻断会让全员死亡的在途申请
                //   永远无法关闭，故放行并记录警告；
                // - 已是 Stopped 的申请无需重复转换，保持现状。
                if (newFamilySize <= 0)
                {
                    var currentStatus = ApplicationStatusExtensions.FromCode(application.Status ?? string.Empty);
                    if (currentStatus == ApplicationStatus.Stopped)
                    {
                        triggeredStop = true;
                    }
                    else
                    {
                        var transition = ApplicationStateMachine.ValidateTransition(currentStatus, ApplicationStatus.Stopped);
                        if (transition.IsFailure &&
                            currentStatus is ApplicationStatus.Draft or ApplicationStatus.Refused)
                        {
                            await tx.RollbackAsync(ct);
                            return Result.Failure<ChangeResult>(
                                ErrorCodes.INVALID_TRANSITION,
                                transition.Message ?? $"不允许从 {currentStatus.GetDescription()} 转换到 已停保");
                        }
                        if (transition.IsFailure)
                        {
                            LogWarn($"成员死亡触发停保: 状态 {application.Status} → Stopped 不在严格状态机允许范围内，按业务规则（最后成员死亡）放行");
                        }

                        application.Status = ApplicationStatus.Stopped.GetCode();
                        application.StopReason = "最后成员死亡";
                        application.StopDate = DateTime.Today;
                        triggeredStop = true;
                    }
                }

                // 渐退期状态重建（同事务）：按成员死亡后新分类决定去留；
                // 最后成员死亡触发停保 → 强制结清既有渐退期。
                var reconcileRes = await ReconcileGracePeriodAsync(
                    context.ApplicationId, newClassification, classificationResult.Value.GracePeriod,
                    oldClassification, oldGuaranteeAmount,
                    forceClear: newFamilySize <= 0, ct: ct);
                if (reconcileRes.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(reconcileRes.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        reconcileRes.Message ?? "渐退期状态重建失败");
                }

                // 业务字段更新（不含 status/stop_*：状态写入统一走 ApplicationStatusService）
                var updateSql = @"UPDATE nc_biz_applications SET
                    family_size = $1, classification_result = $2, is_eligible = $3,
                    household_monthly_guarantee_amount = $4, total_guarantee_amount = $5,
                    classified_subsidy_type = $6, classified_subsidy_amount = $7,
                    caregiver_subsidy_amount = $8,
                    per_capita_income = $9, per_capita_annual_income = $10,
                    updated_at = $11, updated_by = $12
                    WHERE id = $13;";

                var appUpdateResult = await _db.ExecuteNonQueryAsync(updateSql, ct,
                    application.FamilySize, application.ClassificationResult, application.IsEligible,
                    application.HouseholdMonthlyGuaranteeAmount, application.TotalGuaranteeAmount,
                    application.ClassifiedSubsidyType, application.ClassifiedSubsidyAmount,
                    application.CaregiverSubsidyAmount,
                    application.PerCapitaIncome, application.PerCapitaAnnualIncome,
                    application.UpdatedAt, application.UpdatedBy,
                    application.Id);
                if (appUpdateResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(appUpdateResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        appUpdateResult.Message ?? "更新业务字段失败");
                }

                // 状态写入统一走 ApplicationStatusService（状态机校验 + stop_* 落库 + 审计留痕）
                if (triggeredStop)
                {
                    var deathStop = await _statusService.StopAsync(
                        context.ApplicationId, application.StopReason ?? "最后成员死亡",
                        application.StopDate == default ? DateTime.Today : application.StopDate, context.OperatorName,
                        allowSubmittedOverride: true, ct: ct);
                    if (deathStop.IsFailure)
                    {
                        await tx.RollbackAsync(ct);
                        return Result.Failure<ChangeResult>(deathStop.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                            deathStop.Message ?? "停保失败");
                    }
                }
            }
            else
            {
                // 分类判定失败绝不能按"沿用旧分类"静默提交：家庭人数已因死亡变化，
                // 沿用旧结果会写出与实际不符的补贴，必须回滚并失败返回。
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(classificationResult.ErrorCode ?? ErrorCodes.CLASSIFICATION_FAILED,
                    classificationResult.Message ?? "分类判定失败");
            }

            // 8. 创建变更记录（写入三要素：category=MemberChange、reason 可解析、随后回填金额）
            var changeContext = new ChangeContext
            {
                ApplicationId = context.ApplicationId,
                ChangeType = DictionaryConstants.ChangeType.MEMBER_DEATH,
                ChangeCategory = DictionaryConstants.ChangeCategory.MEMBER_CHANGE,
                ChangeReason = $"成员死亡: {context.MemberName}({context.MemberIdCard})",
                ChangeDate = context.DeathDate,
                OperatorName = context.OperatorName
            };

            // Before 快照使用改写前捕获的 oldFamilySize（此前用 FamilySize + 1 反推，
            // 一旦 FamilySize 与成员表行数不同步，反推值就是错的）
            var beforeJson = JsonSerializer.Serialize(new
            {
                FamilySize = oldFamilySize,
                Classification = oldClassification,
                GuaranteeAmount = oldGuaranteeAmount
            });

            var afterJson = JsonSerializer.Serialize(new
            {
                FamilySize = newFamilySize,
                Classification = newClassification,
                GuaranteeAmount = newGuaranteeAmount,
                DeceasedMember = context.MemberName
            });

            var changeIdResult = await CreateChangeAsync(changeContext, beforeJson, afterJson, ct);
            if (changeIdResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(changeIdResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    changeIdResult.Message ?? "创建变更记录失败");
            }
            var changeId = changeIdResult.Value;

            // 结构化金额回填（增减员调整表 old/new_guarantee 取数）
            var updateDeathChange = await _db.ExecuteNonQueryAsync(
                @"UPDATE nc_biz_change_records SET
                    old_classification = $1, new_classification = $2,
                    old_guarantee_amount = $3, new_guarantee_amount = $4,
                    change_reason_type = $5,
                    changed_at = NOW()
                  WHERE id = $6",
                ct,
                oldClassification, newClassification,
                oldGuaranteeAmount, newGuaranteeAmount,
                ChangeReasonTypeConstants.MemberChange,
                changeId);
            if (updateDeathChange.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(updateDeathChange.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updateDeathChange.Message ?? "回填成员死亡变更金额失败");
            }

            await tx.CommitAsync(ct);

            LogInfo($"成员死亡处理完成: 新家庭人数={newFamilySize}");

            return Result.Success(new ChangeResult
            {
                ChangeId = changeId,
                IsSuccess = true,
                OldClassification = oldClassification,
                NewClassification = newClassification,
                OldGuaranteeAmount = oldGuaranteeAmount,
                NewGuaranteeAmount = newGuaranteeAmount,
                TriggeredStop = triggeredStop
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "成员死亡处理");
            return Result.FromException<ChangeResult>(ex);
        }
    }

    /// <summary>
    /// 执行户主变更（停旧建新：所有信息变更建新档案，旧档案停止，便于追踪变更记录）
    /// </summary>
    public async Task<Result<ChangeResult>> ExecuteHeadChangeAsync(HeadChangeContext context, CancellationToken ct = default)
    {
        LogInfo("执行户主变更（停旧建新）");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            // 1. 获取新户主信息（必须限定归属本申请且未删除——否则传入别户的成员 ID
            //    会把别户成员的姓名/身份证写进本申请的户主字段，数据归属错乱）
            var memberSql = "SELECT * FROM nc_biz_family_members WHERE id = $1 AND application_id = $2 AND deleted_at IS NULL";
            var memberResult = await _db.QuerySingleAsync<FamilyMember>(memberSql, ct, context.NewHeadMemberId, context.ApplicationId);
            if (!memberResult.IsSuccess || memberResult.Value == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.APPLICATION_NOT_FOUND, "新户主成员不存在");
            }
            var newHead = memberResult.Value;

            // 2. 停旧建新：复制旧档案 → 新档案（original_application_id=旧ID、Draft），旧档案停止
            var rebuildResult = await StopAndRebuildAsync(
                context.ApplicationId,
                DictionaryConstants.ChangeType.HOUSEHOLD_HEAD_CHANGE,
                ChainTypeConstants.HEAD_CHANGE,
                "停止",
                context.OperatorName,
                ct: ct);
            if (rebuildResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(rebuildResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    rebuildResult.Message ?? "户主变更停旧建新失败");
            }
            var newApplicationId = rebuildResult.Value.NewApplicationId;

            // 3. 更新新档案申请户主信息（applicant_name 等）
            var updateAppSql = @"UPDATE nc_biz_applications SET
                applicant_name = $1, applicant_id_card = $2, gender = $3,
                ethnicity = $4, marital_status = $5, updated_at = NOW()
                WHERE id = $6;";
            var appHeadResult = await _db.ExecuteNonQueryAsync(updateAppSql, ct,
                newHead.Name, newHead.IdCard, newHead.Gender,
                newHead.Ethnicity, newHead.MaritalStatus,
                newApplicationId);
            if (appHeadResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(appHeadResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    appHeadResult.Message ?? "更新新档案户主信息失败");
            }

            // 4. 更新新档案成员户主关系：新户主→本人/户主，旧户主→家庭成员
            long MapMember(long id) => rebuildResult.Value.MemberIdMap.TryGetValue(id, out var nid) ? nid : id;
            var newHeadMemberId = MapMember(context.NewHeadMemberId);
            var oldHeadMemberId = context.OldHeadMemberId > 0 ? MapMember(context.OldHeadMemberId) : 0;
            if (oldHeadMemberId > 0)
            {
                var updOld = await _db.ExecuteNonQueryAsync(
                    "UPDATE nc_biz_family_members SET relationship_to_head = '家庭成员', updated_at = NOW() WHERE id = $1 AND application_id = $2;",
                    ct, oldHeadMemberId, newApplicationId);
                if (updOld.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(updOld.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        updOld.Message ?? "更新旧户主关系失败");
                }
            }
            var updNew = await _db.ExecuteNonQueryAsync(
                "UPDATE nc_biz_family_members SET relationship_to_head = '本人/户主', is_applicant = true, updated_at = NOW() WHERE id = $1 AND application_id = $2;",
                ct, newHeadMemberId, newApplicationId);
            if (updNew.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(updNew.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updNew.Message ?? "更新新户主关系失败");
            }

            // 5. 创建变更记录（挂在旧档案上，After 快照含新档案ID）
            //    写入三要素：category=MemberChange、reason 可解析、随后回填金额
            var changeContext = new ChangeContext
            {
                ApplicationId = context.ApplicationId,
                ChangeType = DictionaryConstants.ChangeType.HOUSEHOLD_HEAD_CHANGE,
                ChangeCategory = DictionaryConstants.ChangeCategory.MEMBER_CHANGE,
                ChangeReason = string.IsNullOrWhiteSpace(context.ChangeReason)
                    ? $"户主变更: {context.OldHeadName} -> {context.NewHeadName}"
                    : $"户主变更: {context.OldHeadName} -> {context.NewHeadName}；{context.ChangeReason}",
                ChangeDate = DateTime.Today,
                OperatorName = context.OperatorName
            };

            var beforeJson = JsonSerializer.Serialize(new { HeadName = context.OldHeadName });
            var afterJson = JsonSerializer.Serialize(new { HeadName = context.NewHeadName, NewApplicationId = newApplicationId });

            var changeIdResult = await CreateChangeAsync(changeContext, beforeJson, afterJson, ct);
            if (changeIdResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(changeIdResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    changeIdResult.Message ?? "创建变更记录失败");
            }
            var changeId = changeIdResult.Value;

            // 结构化金额回填（增减员调整表取数；户主变更本身金额可能不变）
            var updateHeadChange = await _db.ExecuteNonQueryAsync(
                @"UPDATE nc_biz_change_records SET
                    change_reason_type = $1,
                    new_application_id = $2,
                    changed_at = NOW()
                  WHERE id = $3",
                ct,
                ChangeReasonTypeConstants.MemberChange,
                newApplicationId,
                changeId);
            if (updateHeadChange.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(updateHeadChange.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updateHeadChange.Message ?? "回填户主变更记录失败");
            }

            await tx.CommitAsync(ct);

            LogInfo($"户主变更完成（停旧建新）: 新档案ID={newApplicationId}");

            return Result.Success(new ChangeResult
            {
                ChangeId = changeId,
                IsSuccess = true,
                NewApplicationId = newApplicationId
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "户主变更");
            return Result.FromException<ChangeResult>(ex);
        }
    }

    /// <summary>
    /// 执行户主死亡变更（停旧建新）
    /// 事务内：停旧 → 身份证查重 → 建新（Draft）→ 复制成员/赡养人/经济明细/入户调查/照料人 → 死亡记录 → 变更记录
    /// </summary>
    public async Task<Result<ChangeResult>> ExecuteHouseholdDeathAsync(HouseholdDeathContext context, CancellationToken ct = default)
    {
        LogInfo($"执行户主死亡变更: ApplicationId={context.ApplicationId}, 新户主={DataMasker.MaskName(context.NewHeadName)}");

        if (context.NewHeadMemberId <= 0)
            return Result.Failure<ChangeResult>(ErrorCodes.VALIDATION_FAILED, "请选择新户主");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            // 1. 获取旧档案（已软删的申请不允许再变更）
            var appResult = await _db.QuerySingleAsync<ApplicationEntity>(
                $"SELECT {ApplicationColumns.Full} FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL", ct, context.ApplicationId);
            if (!appResult.IsSuccess || appResult.Value == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }
            var application = appResult.Value;
            var oldClassification = application.ClassificationResult ?? string.Empty;
            var oldGuaranteeAmount = application.TotalGuaranteeAmount;
            var oldFamilySize = application.FamilySize;
            var oldHeadName = application.ApplicantName;
            var oldHeadIdCard = application.ApplicantIdCard;

            // 2. 状态校验：已停保幂等拒绝；不予受理（Refused，从未进入保障）不允许停保建新；
            //    草稿（含导入库建档未归档户）/已提交按业务规则（户主死亡客观事件）放行 LogWarn，
            //    与成员变更/户主变更/经济复核对 Draft 的既有容忍度一致。
            var currentStatus = ApplicationStatusExtensions.FromCode(application.Status ?? string.Empty);
            if (currentStatus == ApplicationStatus.Stopped)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.INVALID_TRANSITION, "该档案已停保，不能重复执行户主死亡变更");
            }
            var transition = ApplicationStateMachine.ValidateTransition(currentStatus, ApplicationStatus.Stopped);
            if (transition.IsFailure && currentStatus is ApplicationStatus.Refused)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(
                    ErrorCodes.INVALID_TRANSITION,
                    transition.Message ?? $"不允许从 {currentStatus.GetDescription()} 转换到 已停保");
            }
            if (transition.IsFailure)
            {
                LogWarn($"户主死亡触发停保: 状态 {application.Status} → Stopped 不在严格状态机允许范围内，按业务规则（户主死亡）放行");
            }

            // 3. 加载全部家庭成员，定位死亡原户主成员。
            //    未显式传入时按 优先级：is_applicant=true / member_category='HouseholdHead' /
            //    身份证号或姓名与旧档案申请人一致（存量数据常存在 is_applicant 未置位的申请人成员）
            var membersResult = await _db.QueryAsync<FamilyMember>(
                "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS NULL", ct, context.ApplicationId);
            if (membersResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(membersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    membersResult.Message ?? "家庭成员查询失败");
            }
            var allMembers = membersResult.Value ?? new List<FamilyMember>();
            var deceasedHead = context.DeceasedHeadMemberId > 0
                ? allMembers.FirstOrDefault(m => m.Id == context.DeceasedHeadMemberId)
                : allMembers.FirstOrDefault(m => m.IsApplicant
                    || string.Equals(m.MemberCategory, MemberCategoryConstants.HOUSEHOLD_HEAD, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrEmpty(oldHeadIdCard)
                        && string.Equals(m.IdCard, oldHeadIdCard, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(oldHeadName)
                        && string.Equals(m.Name, oldHeadName, StringComparison.Ordinal)));
            if (deceasedHead == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.FAMILY_MEMBER_NOT_FOUND, "未找到死亡原户主成员记录");
            }

            // 4. 校验新户主成员（必须属于本申请、未删除、且不是死亡户主本人）
            var newHeadMember = allMembers.FirstOrDefault(m => m.Id == context.NewHeadMemberId);
            if (newHeadMember == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.FAMILY_MEMBER_NOT_FOUND, "新户主成员不存在或已删除");
            }
            if (newHeadMember.Id == deceasedHead.Id)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.VALIDATION_FAILED, "新户主不能是死亡人员本人");
            }
            if (string.IsNullOrWhiteSpace(newHeadMember.IdCard))
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.VALIDATION_FAILED,
                    $"新户主 {DataMasker.MaskName(newHeadMember.Name)} 缺少身份证号，无法建立新档案");
            }

            // 5. 身份证查重（新户主已是其他档案的申请人则明确报错，不绕过——多户挂靠同一身份会污染在保名单）
            var dupResult = await _applicationService.CheckIdCardExistsAsync(newHeadMember.IdCard ?? string.Empty, null, ct);
            if (dupResult.IsSuccess && dupResult.Value)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.DUPLICATE_ID_CARD,
                    $"新户主 {DataMasker.MaskName(newHeadMember.Name)} 的身份证号已存在于其他申请档案");
            }

            // 6. 计算新家庭人数（剔除死亡原户主，且不计赡养抚养扶养人 member_category='Support'）
            var newFamilySize = allMembers.Count(m => m.Id != deceasedHead.Id
                && !string.Equals(m.MemberCategory, MemberCategoryConstants.SUPPORT, StringComparison.OrdinalIgnoreCase));
            if (newFamilySize <= 0)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.VALIDATION_FAILED, "户主死亡后无共同生活成员，无法建立新档案");
            }

            // 6.5 现算幸存家庭收入（1B 变体：死亡只建链不判渐退；年值权威，剔除已故者成员级明细，
            //     土地有确权组走组汇总（LandShareCalculator），否则面积×单价；公式单点走 IncomeCalculationService）
            var econLoadResult = await _economicDetailService.LoadAllAsync(context.ApplicationId, ct);
            if (econLoadResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(econLoadResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    econLoadResult.Message ?? "经济明细加载失败");
            }
            var econ = econLoadResult.Value;

            var survivorSupportersResult = await _supporterService.GetByApplicationIdAsync(context.ApplicationId, ct);
            if (survivorSupportersResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(survivorSupportersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    survivorSupportersResult.Message ?? "赡养义务人加载失败");
            }
            var survivorSupporters = survivorSupportersResult.Value ?? new List<Supporter>();

            var deadMemberId = deceasedHead.Id;
            var laborSurv = econ.LaborIncomes.Where(x => x.MemberId != deadMemberId).ToList();
            var businessSurv = econ.BusinessIncomes.Where(x => x.MemberId != deadMemberId).ToList();
            var propertySurv = econ.PropertyIncomes.Where(x => x.MemberId != deadMemberId).ToList();
            var transferSurv = econ.TransferIncomes.Where(x => x.MemberId != deadMemberId).ToList();
            var rigidSurv = econ.RigidExpenditures.Where(x => x.MemberId != deadMemberId).ToList();

            var workIncome = _incomeCalculationService.CalculateLaborIncome(laborSurv);
            var businessIncome = _incomeCalculationService.CalculateBusinessNetIncome(businessSurv);
            var propertyIncome = propertySurv.Sum(p => p.Amount);
            var transferIncome = transferSurv.Sum(t => t.TotalAmount);
            var otherIncome = econ.OtherIncomes.Sum(o => o.Amount);
            var alimonyIncome = _incomeCalculationService.CalculateAlimonyAnnual(survivorSupporters);
            var subsidyIncome = _incomeCalculationService.CalculateSubsidyIncome(econ.Subsidies);
            var rigidExpenditure = _incomeCalculationService.CalculateRigidExpenditure(rigidSurv);

            decimal landIncome;
            decimal familyLandArea = application.FamilyLandArea;
            decimal selfFarmedArea = application.SelfFarmedLandArea;
            decimal subleasedArea = application.SubleasedLandArea;
            decimal contractedArea = application.ContractedLandArea;
            decimal totalConfirmedLandArea = application.TotalConfirmedLandArea;
            var confirmedPersonCount = application.ConfirmedPersonCount;
            if (econ.LandConfirmationGroups.Count > 0)
            {
                var survivorFamilyNames = new HashSet<string>(StringComparer.Ordinal);
                if (!string.IsNullOrWhiteSpace(newHeadMember.Name))
                    survivorFamilyNames.Add(newHeadMember.Name);
                foreach (var m in allMembers)
                {
                    if (m.Id == deadMemberId) continue;
                    if (string.Equals(m.MemberCategory, MemberCategoryConstants.SUPPORT, StringComparison.OrdinalIgnoreCase)) continue;
                    if (m.IsHouseholdHead || string.Equals(m.MemberCategory, MemberCategoryConstants.SHARED_LIVING, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(m.Name))
                            survivorFamilyNames.Add(m.Name);
                    }
                }

                totalConfirmedLandArea = (decimal)econ.LandConfirmationGroups.Sum(g => g.TotalArea);
                var totalLandShares = (decimal)econ.LandConfirmationGroups.Sum(g => g.TotalShares);
                confirmedPersonCount = econ.LandConfirmationGroups.Sum(g => g.PersonCount);

                decimal selfTotal = 0, subTotal = 0, conTotal = 0;
                foreach (var group in econ.LandConfirmationGroups)
                {
                    foreach (var record in group.Records)
                    {
                        switch (record.LandUsage)
                        {
                            case DictionaryConstants.LandUsage.SELF_FARM: selfTotal += record.LandArea; break;
                            case DictionaryConstants.LandUsage.SUBLEASE: subTotal += record.LandArea; break;
                            case DictionaryConstants.LandUsage.CONTRACT: conTotal += record.LandArea; break;
                        }
                    }
                }

                decimal familyIncome = 0;
                decimal familyLandShares = 0;
                foreach (var group in econ.LandConfirmationGroups)
                {
                    var gShares = (decimal)group.TotalShares;
                    if (gShares <= 0) continue;
                    var effectiveShares = LandShareCalculator.ComputeEffectiveShares(group);
                    decimal familyRatio = 0;
                    foreach (var kv in effectiveShares)
                    {
                        if (survivorFamilyNames.Contains(kv.Key))
                        {
                            familyLandShares += kv.Value;
                            familyRatio += kv.Value;
                        }
                    }
                    familyRatio /= gShares;
                    decimal groupIncome = 0;
                    foreach (var record in group.Records)
                        groupIncome += record.LandValue;
                    familyIncome += Math.Round(groupIncome * familyRatio, 2);
                }
                landIncome = Math.Round(familyIncome, 2);
                var familyRatioAll = totalLandShares > 0 ? familyLandShares / totalLandShares : 0m;
                familyLandArea = totalLandShares > 0
                    ? Math.Round(totalConfirmedLandArea / totalLandShares * familyLandShares, 2)
                    : 0m;
                selfFarmedArea = Math.Round(selfTotal * familyRatioAll, 2);
                subleasedArea = Math.Round(subTotal * familyRatioAll, 2);
                contractedArea = Math.Round(conTotal * familyRatioAll, 2);
            }
            else
            {
                landIncome = _incomeCalculationService.CalculateLandIncome(
                    selfFarmedArea, (double)IncomeTypeConstants.LandUnitPrice.SELF_FARM,
                    subleasedArea, (double)IncomeTypeConstants.LandUnitPrice.SUBLEASE,
                    contractedArea, (double)IncomeTypeConstants.LandUnitPrice.CONTRACT);
            }

            // 年值权威 → 月值/人均一次舍入；渐退期留给表单 Step5 正式判定（1B：死亡瞬间不判、不建）
            var recalculatedAnnualIncome = _incomeCalculationService.CalculateAnnualFamilyIncome(
                workIncome, businessIncome, propertyIncome, transferIncome, otherIncome,
                alimonyIncome, landIncome, subsidyIncome, rigidExpenditure);
            var recalculatedMonthlyIncome = _incomeCalculationService.MonthlyFromAnnual(recalculatedAnnualIncome);
            var newPerCapitaMonthly = _incomeCalculationService.PerCapitaMonthly(recalculatedAnnualIncome, newFamilySize);
            var newPerCapitaAnnual = _incomeCalculationService.CalculatePerCapitaAnnual(recalculatedAnnualIncome, newFamilySize);
            var inGraceBand = false;
            var grantAmount = 0m;

            // 7. 生成新申请编号
            var appNoResult = await _applicationService.GetNextApplicationNoAsync(ct);
            if (appNoResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(appNoResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    appNoResult.Message ?? "生成申请编号失败");
            }

            // 8. 复制主表 → 新档案（Draft、换户主、family_size 重算、original_application_id=旧ID、
            //    清空分类/渐退/停保/银行卡字段——新档案重走审批，旧账号不能沿用给新户主）
            var newAppInsertSql = @"INSERT INTO nc_biz_applications (
                application_no, applicant_name, applicant_id_card, applicant_phone,
                gender, ethnicity, marital_status, hukou_type, education_level, political_status,
                hukou_address, disability_card_no,
                province, city, district, town, community, address,
                physical_condition, disease_name, secondary_disease_name,
                disability_type, disability_level, health_status,
                employment_status, work_unit, income_source,
                family_size, confirmed_family_size,
                is_single_rescue, support_mode,
                application_reason, application_reason_detail, caregiver_type, destitute_support_type, support_institution_id,
                work_income_total, business_income_total, property_income_total,
                transfer_income_total, other_income_total, total_family_income, per_capita_income, rigid_expenditure, alimony_income,
                total_annual_income, per_capita_annual_income,
                is_eligible, classification_result,
                classified_subsidy_type, classified_subsidy_amount,
                household_monthly_guarantee_amount, person_category_protection_total_amount,
                caregiver_subsidy_amount, total_guarantee_amount,
                status, current_step,
                source_type, source_table, source_id,
                chain_type,
                original_application_id,
                first_approved_at,
                created_at, updated_at, created_by, updated_by,
                city_id, county_id, town_id, village_id,
                hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id,
                family_land_area, self_farmed_land_area, subleased_land_area, contracted_land_area,
                land_income_total, subsidy_total, total_confirmed_land_area, confirmed_person_count
            )
            SELECT $1, $2, $3, $4,
                $5, $6, $7, $8, $9, $10,
                hukou_address, disability_card_no,
                province, city, district, town, community, address,
                physical_condition, disease_name, secondary_disease_name,
                disability_type, disability_level, health_status,
                employment_status, work_unit, income_source,
                $11, $12,
                is_single_rescue, support_mode,
                application_reason, application_reason_detail, caregiver_type, destitute_support_type, support_institution_id,
                $22, $23, $24,
                $25, $26, $27, $19, $28, $29,
                $20, $21,
                false, NULL,
                NULL, 0,
                $18, 0, 0, $18,
                $38, $39,
                'HouseholdDeath', 'nc_biz_applications', $13,
                'HouseholdDeath',
                $14,
                first_approved_at,
                NOW(), NOW(), $15, $16,
                city_id, county_id, town_id, village_id,
                hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id,
                $30, $31, $32, $33,
                $34, $35, $36, $37
            FROM nc_biz_applications WHERE id = $17
            RETURNING id;";

            var newAppResult = await _db.ExecuteScalarAsync(newAppInsertSql, ct,
                appNoResult.Value,
                newHeadMember.Name, newHeadMember.IdCard, newHeadMember.Phone,
                newHeadMember.Gender, newHeadMember.Ethnicity, newHeadMember.MaritalStatus, newHeadMember.HukouType,
                newHeadMember.EducationLevel, newHeadMember.PoliticalStatus,
                newFamilySize, newFamilySize,
                context.ApplicationId,
                context.ApplicationId,
                context.OperatorName, context.OperatorName,
                context.ApplicationId,
                grantAmount,
                newPerCapitaMonthly,
                recalculatedAnnualIncome,
                newPerCapitaAnnual,
                workIncome, businessIncome, propertyIncome,
                transferIncome, otherIncome, recalculatedMonthlyIncome,
                rigidExpenditure, alimonyIncome,
                familyLandArea, selfFarmedArea, subleasedArea, contractedArea,
                landIncome, subsidyIncome, totalConfirmedLandArea, confirmedPersonCount,
                ApplicationStatusCodes.DRAFT, WorkflowSteps.ENTRY_START);
            if (newAppResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(newAppResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    newAppResult.Message ?? "创建新档案失败");
            }
            var newApplicationId = newAppResult.Value;

            // 9. 复制家庭成员（剔除死亡原户主；新户主 is_applicant=true、关系='本人/户主'）
            var memberCopySql = @"INSERT INTO nc_biz_family_members (
                application_id, name, id_card, is_applicant, gender, birth_date, age, ethnicity, phone,
                hukou_type, hukou_address, marital_status, education_level, political_status,
                relationship_to_head, health_status, work_capacity, is_disabled,
                disability_type, disability_level, is_severe_disability, employment_status, annual_income,
                member_category, disability_certificate_no, disease_category, disease_name,
                home_province, home_city, home_district, home_town, home_address,
                hukou_province, hukou_city, hukou_district, hukou_town, home_village, secondary_disease,
                person_type, annual_support_fee, is_support_ability, monthly_income_capacity,
                family_size, work_unit, monthly_support_fee,
                support_months, main_income_source, is_severe_disease, is_labor_exempt, created_at, updated_at, deleted_at
            )
            SELECT $1, name, id_card,
                CASE WHEN id = $2 THEN true ELSE is_applicant END,
                gender, birth_date, age, ethnicity, phone,
                hukou_type, hukou_address, marital_status, education_level, political_status,
                CASE WHEN id = $2 THEN '本人/户主' ELSE relationship_to_head END,
                health_status, work_capacity, is_disabled,
                disability_type, disability_level, is_severe_disability, employment_status, annual_income,
                member_category, disability_certificate_no, disease_category, disease_name,
                home_province, home_city, home_district, home_town, home_address,
                hukou_province, hukou_city, hukou_district, hukou_town, home_village, secondary_disease,
                person_type, annual_support_fee, is_support_ability, monthly_income_capacity,
                family_size, work_unit, monthly_support_fee,
                support_months, main_income_source, is_severe_disease, is_labor_exempt, NOW(), NOW(), NULL
            FROM nc_biz_family_members
            WHERE application_id = $3 AND id != $4 AND deleted_at IS NULL;";
            var memberCopyResult = await _db.ExecuteNonQueryAsync(memberCopySql, ct,
                newApplicationId, context.NewHeadMemberId, context.ApplicationId, deceasedHead.Id);
            if (memberCopyResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(memberCopyResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    memberCopyResult.Message ?? "复制家庭成员失败");
            }

            // 构建 旧成员ID → 新成员ID 映射（按身份证号匹配，用于照料人被照料成员与经济明细 member_id 的指向迁移）
            var newMembersResult = await _db.QueryAsync<FamilyMember>(
                "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS NULL", ct, newApplicationId);
            if (newMembersResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(newMembersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    newMembersResult.Message ?? "新档案成员查询失败");
            }
            var newMembers = newMembersResult.Value ?? new List<FamilyMember>();
            var memberIdMap = new Dictionary<long, long>();
            foreach (var oldMember in allMembers.Where(m => m.Id != deceasedHead.Id))
            {
                var match = newMembers.FirstOrDefault(m => !string.IsNullOrEmpty(m.IdCard)
                    && string.Equals(m.IdCard, oldMember.IdCard, StringComparison.OrdinalIgnoreCase))
                    ?? newMembers.FirstOrDefault(m => string.Equals(m.Name, oldMember.Name, StringComparison.Ordinal));
                if (match != null)
                    memberIdMap[oldMember.Id] = match.Id;
            }

            // 10. 复制经济明细（复用 6.5 已加载的 econ → 重映射 member_id → SaveAll；死亡原户主的明细行 member_id 置 0，文本字段保留）
            long MapMemberId(long oldId) => oldId > 0 && memberIdMap.TryGetValue(oldId, out var nid) ? nid : 0;
            foreach (var it in econ.LaborIncomes) it.MemberId = MapMemberId(it.MemberId);
            foreach (var it in econ.BusinessIncomes) it.MemberId = MapMemberId(it.MemberId);
            foreach (var it in econ.PropertyIncomes) it.MemberId = MapMemberId(it.MemberId);
            foreach (var it in econ.TransferIncomes) it.MemberId = MapMemberId(it.MemberId);
            foreach (var it in econ.RigidExpenditures) it.MemberId = MapMemberId(it.MemberId);
            foreach (var it in econ.FamilyProperties) it.MemberId = MapMemberId(it.MemberId);
            foreach (var it in econ.Vehicles) it.MemberId = MapMemberId(it.MemberId);
            foreach (var it in econ.Machineries) it.MemberId = MapMemberId(it.MemberId);

            var econSaveResult = await _economicDetailService.SaveAllAsync(newApplicationId,
                econ.LaborIncomes, econ.BusinessIncomes, econ.PropertyIncomes, econ.TransferIncomes,
                econ.OtherIncomes, econ.Subsidies, econ.BreedingIncomes, econ.RigidExpenditures,
                econ.FamilyProperties, econ.Vehicles, econ.Machineries, econ.FinancialAssets, econ.LandRegistrations,
                econ.LandConfirmationGroups, ct);
            if (econSaveResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(econSaveResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    econSaveResult.Message ?? "经济明细复制失败");
            }

            // 11. 复制入户调查（若存在）
            var surveyCopySql = @"INSERT INTO nc_biz_household_surveys
                (application_id, survey_date, surveyor_name, surveyor_organization,
                 respondent_name, respondent_relation, survey_notes,
                 created_at, updated_at, deleted_at,
                 application_reason, application_reason_detail, survey_conclusion)
                SELECT $1, survey_date, surveyor_name, surveyor_organization,
                       respondent_name, respondent_relation, survey_notes,
                       NOW(), NOW(), NULL,
                       application_reason, application_reason_detail, survey_conclusion
                FROM nc_biz_household_surveys
                WHERE application_id = $2 AND deleted_at IS NULL;";
            var surveyCopyResult = await _db.ExecuteNonQueryAsync(surveyCopySql, ct, newApplicationId, context.ApplicationId);
            if (surveyCopyResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(surveyCopyResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    surveyCopyResult.Message ?? "复制入户调查失败");
            }

            // 12. 复制照料人（cared_member_id 迁移到新成员ID，无法映射置 0）
            var caregiversResult = await _db.QueryAsync<Caregiver>(
                "SELECT * FROM nc_biz_caregivers WHERE application_id = $1 AND deleted_at IS NULL", ct, context.ApplicationId);
            if (caregiversResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(caregiversResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    caregiversResult.Message ?? "照料人查询失败");
            }
            var caregivers = caregiversResult.Value ?? new List<Caregiver>();
            if (caregivers.Count > 0)
            {
                // 分批多行 VALUES 单语句（19 参数/行 + 行内 NOW()/NULL 字面量；500 行/批防参数上限）
                const int caregiverParamsPerRow = 19;
                const int caregiverBatchSize = 500;
                var caregiverInsertSql = @"INSERT INTO nc_biz_caregivers
                    (application_id, cared_member_id, name, id_card, phone, relationship,
                     gender, age, ethnicity, health_status, employment_status, main_income_source,
                     work_unit, position, address,
                     marital_status, hukou_type, education_level, political_status,
                     created_at, deleted_at)
                    VALUES ";
                for (var offset = 0; offset < caregivers.Count; offset += caregiverBatchSize)
                {
                    var chunk = caregivers.GetRange(offset, Math.Min(caregiverBatchSize, caregivers.Count - offset));
                    var (valuesClause, args) = NewCosmos.Helpers.MultiRowValuesBuilder.Build(
                        chunk.Count, caregiverParamsPerRow,
                        r =>
                        {
                            var cg = chunk[r];
                            return new object?[]
                            {
                                newApplicationId, MapMemberId(cg.CaredMemberId),
                                cg.Name, cg.IdCard, cg.Phone, cg.Relationship,
                                cg.Gender, cg.Age, cg.Ethnicity, cg.HealthStatus,
                                cg.EmploymentStatus, cg.MainIncomeSource,
                                cg.WorkUnit, cg.Position, cg.Address,
                                cg.MaritalStatus, cg.HukouType, cg.EducationLevel, cg.PoliticalStatus
                            };
                        },
                        o => "(" + string.Join(",", Enumerable.Range(o, caregiverParamsPerRow).Select(n => "$" + n)) + ",NOW(),NULL)");
                    var insertCaregiverResult = await _db.ExecuteNonQueryAsync(caregiverInsertSql + valuesClause + ";", ct, args);
                    if (insertCaregiverResult.IsFailure)
                    {
                        await tx.RollbackAsync(ct);
                        return Result.Failure<ChangeResult>(insertCaregiverResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                            insertCaregiverResult.Message ?? "复制照料人失败");
                    }
                }
            }

            // 13. 写死亡记录（is_household_head=true，回填新档案ID）
            var deathSql = @"INSERT INTO nc_biz_death_records
                (application_id, member_id, member_name, member_id_card, relationship_to_head,
                 death_date, death_reason, death_certificate_no,
                 is_household_head, new_head_id_card, new_head_name, new_application_id,
                 operator_name, created_by, created_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,true,$9,$10,$11,$12,$12,NOW());";
            var deathInsertResult = await _db.ExecuteNonQueryAsync(deathSql, ct,
                context.ApplicationId, deceasedHead.Id, deceasedHead.Name, deceasedHead.IdCard,
                string.IsNullOrEmpty(deceasedHead.RelationshipToHead) ? "本人/户主" : deceasedHead.RelationshipToHead,
                context.DeathDate, context.DeathReason, context.DeathCertificateNo,
                context.NewHeadIdCard, context.NewHeadName, newApplicationId,
                context.OperatorName);
            if (deathInsertResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(deathInsertResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    deathInsertResult.Message ?? "创建死亡记录失败");
            }

            // 14. 停止旧档案（户主死亡）——状态写入统一走 ApplicationStatusService（状态机校验 + 审计留痕）
            var stopResult = await _statusService.StopAsync(
                context.ApplicationId, ChangeReasonTypeConstants.HeadDeceased, context.DeathDate,
                context.OperatorName, allowSubmittedOverride: true, ct: ct);
            if (stopResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(stopResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    stopResult.Message ?? "停止旧档案失败");
            }

            // 14.1 旧档案停保，结清其渐退期（渐退期仅对在保的低收入降档户有意义）
            var graceClearResult = await _gracePeriodService.ClearAsync(context.ApplicationId, ct);
            if (graceClearResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(graceClearResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    graceClearResult.Message ?? "结清旧档案渐退期失败");
            }

            // 14.2（1B 变体）死亡瞬间不判定/不预建渐退期——渐退资格在表单 Step5 正式分类判定后由确认页决定；
            //     新档此刻 triggered_grace_period=false，收入列已是 6.5 现算的幸存家庭口径。

            // 15. 创建变更记录（挂在旧档上，After 快照含新档ID）
            //     change_category=MemberChange：户主死亡必然人员变动，供"增减员调整表"按类别取数
            //     change_reason 可解析格式：与成员死亡同构，冒号后 "姓名(身份证)" 供 ADJ 减员槽解析
            var changeContext = new ChangeContext
            {
                ApplicationId = context.ApplicationId,
                ChangeType = DictionaryConstants.ChangeType.HOUSEHOLD_DEATH,
                ChangeCategory = DictionaryConstants.ChangeCategory.MEMBER_CHANGE,
                ChangeReason = $"户主死亡: {deceasedHead.Name}({oldHeadIdCard}) → 新户主 {context.NewHeadName}",
                ChangeDate = context.DeathDate,
                OperatorName = context.OperatorName
            };
            var beforeJson = JsonSerializer.Serialize(new
            {
                HeadName = oldHeadName,
                HeadIdCard = oldHeadIdCard,
                FamilySize = oldFamilySize,
                Classification = oldClassification,
                GuaranteeAmount = oldGuaranteeAmount
            });
            var afterJson = JsonSerializer.Serialize(new
            {
                NewHeadName = context.NewHeadName,
                NewApplicationId = newApplicationId,
                FamilySize = newFamilySize,
                DeathDate = context.DeathDate,
                DeathReason = context.DeathReason,
                DeceasedHeadName = deceasedHead.Name
            });
            var changeIdResult = await CreateChangeAsync(changeContext, beforeJson, afterJson, ct);
            if (changeIdResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(changeIdResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    changeIdResult.Message ?? "创建变更记录失败");
            }
            var changeId = changeIdResult.Value;

            // 填充 nc_biz_change_records 的结构化对比字段（户主死亡审计对应）：
            // 旧值一律取旧档保存值（与经济复核 L1073 同源），新值取判定落库值（与新档 INSERT $19 同源）
            var updateChangeSql = @"UPDATE nc_biz_change_records SET
                change_reason_type = $1,
                old_classification = $2, new_classification = $3,
                old_per_capita_income = $4, new_per_capita_income = $5,
                old_guarantee_amount = $6, new_guarantee_amount = $7,
                triggered_stop = $8,
                triggered_grace_period = $9,
                new_application_id = $10,
                original_application_id = $11,
                changed_at = NOW()
                WHERE id = $12;";
            var updateChangeResult = await _db.ExecuteNonQueryAsync(updateChangeSql, ct,
                ChangeReasonTypeConstants.HeadDeceased,
                oldClassification, (object?)null,
                application.PerCapitaIncome, newPerCapitaMonthly,
                oldGuaranteeAmount, grantAmount,
                true, inGraceBand,
                newApplicationId, context.ApplicationId,
                changeId);
            if (updateChangeResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(updateChangeResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updateChangeResult.Message ?? "更新变更记录对比字段失败");
            }

            await tx.CommitAsync(ct);

            LogInfo($"户主死亡变更完成: 旧档案={context.ApplicationId} → 新档案={newApplicationId}, 新家庭人数={newFamilySize}, 幸存家庭年收入={recalculatedAnnualIncome:F2}, 渐退期=Step5待判定");

            return Result.Success(new ChangeResult
            {
                ChangeId = changeId,
                IsSuccess = true,
                OldClassification = oldClassification,
                OldGuaranteeAmount = oldGuaranteeAmount,
                TriggeredStop = true,
                TriggeredGracePeriod = inGraceBand,
                NewApplicationId = newApplicationId
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "户主死亡变更");
            return Result.FromException<ChangeResult>(ex);
        }
    }

    /// <summary>
    /// 获取变更历史
    /// </summary>
    public async Task<Result<List<ChangeRecord>>> GetChangeHistoryAsync(long applicationId, CancellationToken ct = default)
    {
        // nc_biz_change_records 无 created_at 列（只有 changed_at）：
        // 查询改用 changed_at，避免 42703 使环境事务进入 aborted 状态而引发后续 25P02
        var sql = @"SELECT id, application_id, change_no, change_type, change_reason, 
                    change_date, operator_name, changed_at
                    FROM nc_biz_change_records 
                    WHERE application_id = $1 
                    ORDER BY changed_at DESC LIMIT 200;";

        var result = await _db.QueryAsync<ChangeRecord>(sql, ct, applicationId);
        return result.IsSuccess
            ? Result.Success(result.Value ?? new List<ChangeRecord>())
            : Result.Failure<List<ChangeRecord>>(result.ErrorCode!, result.Message!);
    }

    public async Task<Result<TriggeredStopChangeRecord?>> GetLatestTriggeredStopRecordAsync(long applicationId, IReadOnlyCollection<string> newClassifications, CancellationToken ct = default)
    {
        var sql = @"SELECT old_classification, new_classification FROM nc_biz_change_records
                          WHERE (application_id = $1 OR new_application_id = $1)
                            AND triggered_stop = true AND change_type = 'CategoryStop'
                            AND new_classification = ANY($2::text[])
                            AND deleted_at IS NULL
                          ORDER BY changed_at DESC LIMIT 1";
        var result = await _db.QuerySingleAsync<TriggeredStopChangeRecord>(sql, ct, applicationId, newClassifications.ToArray());
        if (result.IsFailure)
            return Result.Failure<TriggeredStopChangeRecord?>(result.ErrorCode!, result.Message!);
        return Result.Success<TriggeredStopChangeRecord?>(result.Value);
    }

    public async Task<Result<bool>> HasTriggeredCategoryStopAsync(long applicationId, IReadOnlyCollection<string> stopCategoryCodes, CancellationToken ct = default)
    {
        var sql = @"SELECT EXISTS(SELECT 1 FROM nc_biz_change_records
                                  WHERE (application_id = $1 OR new_application_id = $1)
                                    AND triggered_stop = true AND change_type = 'CategoryStop'
                                    AND new_classification = ANY($2::text[])
                                    AND deleted_at IS NULL)";
        var result = await _db.ExecuteScalarAsync<bool>(sql, ct, applicationId, stopCategoryCodes.ToArray());
        return result.IsSuccess
            ? Result.Success(result.Value)
            : Result.Failure<bool>(result.ErrorCode!, result.Message!);
    }

    /// <summary>
    /// 查询家庭成员类变更记录（增减员调整表字段装配用，按变更日期倒序）。
    /// 规范：停旧建新/重建链场景下，变更可能挂在旧档（application_id）而新档经 new_application_id 关联；
    /// 存量记录 change_category 可能为空，用 change_type 兜底，保证人员变动类流程都能取到数。
    /// </summary>
    public async Task<Result<List<MemberChangeRecord>>> GetMemberChangeRecordsAsync(long applicationId, int limit, CancellationToken ct = default)
    {
        var sql = @"SELECT id, application_id, new_application_id, change_type, change_reason, change_date,
                           old_classification, new_classification,
                           old_guarantee_amount, new_guarantee_amount
                    FROM nc_biz_change_records
                    WHERE (application_id = $1 OR new_application_id = $1)
                      AND deleted_at IS NULL
                      AND (
                        change_category = 'MemberChange'
                        OR change_type IN ('HouseholdDeath','MemberDeath','MemberRemove','MemberAdd','HouseholdHeadChange')
                      )
                    ORDER BY change_date DESC, id DESC LIMIT $2";
        var result = await _db.QueryAsync<MemberChangeRecord>(sql, ct, applicationId, limit);
        return result.IsSuccess
            ? Result.Success(result.Value ?? new List<MemberChangeRecord>())
            : Result.Failure<List<MemberChangeRecord>>(result.ErrorCode!, result.Message!);
    }

    /// <summary>nc_biz_change_details 行（增减员逐人明细读取用）</summary>
    private sealed class MemberAdjustDetailRow
    {
        public long ChangeId { get; set; }
        public string? FieldName { get; set; }
        public string? FieldLabel { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
    }

    /// <summary>nc_biz_family_members 行（逐人明细补齐展示字段用，含软删除历史行）</summary>
    private sealed class MemberAdjustProfileRow
    {
        public long ApplicationId { get; set; }
        public string? IdCard { get; set; }
        public string? Name { get; set; }
        public string? Gender { get; set; }
        public string? RelationshipToHead { get; set; }
        public string? HealthStatus { get; set; }
        public string? WorkUnit { get; set; }
        public decimal AnnualIncome { get; set; }
        public DateTime? DeletedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public async Task<Result<List<MemberAdjustEntry>>> GetMemberAdjustEntriesAsync(long applicationId, int limit, CancellationToken ct = default)
    {
        var recordsRes = await GetMemberChangeRecordsAsync(applicationId, limit, ct);
        if (recordsRes.IsFailure)
            return Result.Failure<List<MemberAdjustEntry>>(recordsRes.ErrorCode!, recordsRes.Message!);

        var records = recordsRes.Value ?? new List<MemberChangeRecord>();
        if (records.Count == 0)
            return Result.Success(new List<MemberAdjustEntry>());

        var entries = new List<MemberAdjustEntry>();

        // ① 成员增减流程的逐人明细（唯一权威源，含逐人原因）
        var changeIds = records.Select(r => r.Id).Distinct().ToArray();
        var detailSql = @"SELECT change_id, field_name, field_label, old_value, new_value
                          FROM nc_biz_change_details
                          WHERE change_id = ANY($1::bigint[])
                            AND field_name IN ('MemberAdd','MemberRemove')
                          ORDER BY id";
        var detailRes = await _db.QueryAsync<MemberAdjustDetailRow>(detailSql, ct, new object[] { changeIds });
        if (detailRes.IsFailure)
            return Result.Failure<List<MemberAdjustEntry>>(detailRes.ErrorCode!, detailRes.Message!);

        var detailsByChange = (detailRes.Value ?? new List<MemberAdjustDetailRow>())
            .GroupBy(d => d.ChangeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var rec in records)
        {
            if (detailsByChange.TryGetValue(rec.Id, out var rows))
            {
                foreach (var row in rows)
                {
                    if (string.IsNullOrWhiteSpace(row.FieldName)) continue;
                    ParseMemberDetailRow(row, rec, entries);
                }
                continue;
            }

            // ② 成员死亡/户主死亡/旧版减员流程不写明细：按 change_reason "动词: 姓名(身份证)" 解析兜底
            var recType = rec.ChangeType ?? string.Empty;
            if (recType == DictionaryConstants.ChangeType.MEMBER_DEATH ||
                recType == DictionaryConstants.ChangeType.HOUSEHOLD_DEATH ||
                recType == DictionaryConstants.ChangeType.MEMBER_REMOVE)
            {
                if (!TryParseMemberFromReason(rec.ChangeReason, out var dName, out var dIdCard)) continue;
                entries.Add(new MemberAdjustEntry
                {
                    Direction = DictionaryConstants.ChangeType.MEMBER_REMOVE,
                    Name = dName,
                    IdCard = dIdCard,
                    ChangeDate = rec.ChangeDate,
                    ChangeType = recType
                });
            }
        }

        // ③ 回查成员表补齐展示字段（本档案行优先 → 在册行优先 → 其次最新历史行）；
        //    历史原因无证件号的减员行按姓名回填，并补回证件号
        var appIds = records
            .SelectMany(r => new[] { r.ApplicationId, r.NewApplicationId })
            .Append(applicationId)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
        var idCards = entries
            .Select(e => e.IdCard?.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var names = entries
            .Select(e => e.Name?.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (appIds.Length > 0 && (idCards.Length > 0 || names.Length > 0))
        {
            var profileSql = @"SELECT application_id, id_card, name, gender, relationship_to_head,
                                      health_status, work_unit, annual_income, deleted_at, updated_at
                               FROM nc_biz_family_members
                               WHERE application_id = ANY($1::bigint[])
                                 AND (id_card = ANY($2::text[]) OR name = ANY($3::text[]))";
            var profileRes = await _db.QueryAsync<MemberAdjustProfileRow>(profileSql, ct,
                new object[] { appIds, idCards, names });
            if (profileRes.IsFailure)
                return Result.Failure<List<MemberAdjustEntry>>(profileRes.ErrorCode!, profileRes.Message!);

            var rows = profileRes.Value ?? new List<MemberAdjustProfileRow>();
            static IEnumerable<MemberAdjustProfileRow> Prefer(IEnumerable<MemberAdjustProfileRow> group, long currentAppId) =>
                group.OrderByDescending(p => p.ApplicationId == currentAppId)
                     .ThenByDescending(p => p.DeletedAt == null)
                     .ThenByDescending(p => p.UpdatedAt ?? DateTime.MinValue);

            var byIdCard = rows
                .Where(p => !string.IsNullOrWhiteSpace(p.IdCard))
                .GroupBy(p => (p.IdCard ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => Prefer(g, applicationId).First(), StringComparer.OrdinalIgnoreCase);
            var byName = rows
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .GroupBy(p => (p.Name ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => Prefer(g, applicationId).First(), StringComparer.OrdinalIgnoreCase);

            foreach (var e in entries)
            {
                var idKey = e.IdCard?.Trim();
                var nameKey = e.Name?.Trim();
                MemberAdjustProfileRow? p = null;
                if (!string.IsNullOrEmpty(idKey) && byIdCard.TryGetValue(idKey, out var byId))
                    p = byId;
                if (p == null && !string.IsNullOrEmpty(nameKey) && byName.TryGetValue(nameKey, out var byNameHit))
                    p = byNameHit;
                if (p == null) continue;

                e.Gender = p.Gender;
                e.HealthStatus = p.HealthStatus;
                e.WorkUnit = p.WorkUnit;
                e.AnnualIncome = p.AnnualIncome;
                // 关系/姓名以成员表为准（明细登记的是变更当刻的值）
                if (!string.IsNullOrWhiteSpace(p.RelationshipToHead)) e.RelationshipToHead = p.RelationshipToHead!;
                if (!string.IsNullOrWhiteSpace(p.Name)) e.Name = p.Name!;
                if (string.IsNullOrWhiteSpace(e.IdCard) && !string.IsNullOrWhiteSpace(p.IdCard)) e.IdCard = p.IdCard!;
            }
        }

        // ④ 按证件号（无证件号按姓名）去重：同人多次变更只保留最近一次，保持变更日期倒序
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var distinct = new List<MemberAdjustEntry>(entries.Count);
        foreach (var e in entries)
        {
            var key = (!string.IsNullOrWhiteSpace(e.IdCard) ? e.IdCard : e.Name)?.Trim() ?? string.Empty;
            if (key.Length > 0 && !seen.Add(key)) continue;
            distinct.Add(e);
        }

        return Result.Success(distinct);
    }

    /// <summary>
    /// 解析 nc_biz_change_details 一行：
    /// field_label=姓名；old_value="{姓名}({身份证}) {成员分类} {与户主关系}"；new_value="{原因}|{yyyy-MM-dd}|{备注}"
    /// </summary>
    private static void ParseMemberDetailRow(MemberAdjustDetailRow row, MemberChangeRecord rec, List<MemberAdjustEntry> entries)
    {
        var direction = string.Equals(row.FieldName, DictionaryConstants.ChangeType.MEMBER_ADD, StringComparison.OrdinalIgnoreCase)
            ? DictionaryConstants.ChangeType.MEMBER_ADD
            : DictionaryConstants.ChangeType.MEMBER_REMOVE;

        var name = row.FieldLabel?.Trim() ?? string.Empty;
        var idCard = string.Empty;
        var relation = string.Empty;
        var category = string.Empty;

        var oldValue = row.OldValue?.Trim() ?? string.Empty;
        if (oldValue.Length > 0)
        {
            var match = global::System.Text.RegularExpressions.Regex.Match(
                oldValue, @"^(?<name>.*?)\((?<id>[^()]*)\)(?<rest>.*)$");
            if (match.Success)
            {
                if (string.IsNullOrWhiteSpace(name)) name = match.Groups["name"].Value.Trim();
                idCard = match.Groups["id"].Value.Trim();

                var tokens = match.Groups["rest"].Value
                    .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var start = 0;
                if (tokens.Length > 0 && IsMemberCategory(tokens[0])) { category = tokens[0]; start = 1; }
                if (tokens.Length > start) relation = string.Join(' ', tokens.Skip(start));
            }
        }

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(idCard)) return;

        DateTime? eventDate = null;
        var reasonName = string.Empty;
        var newValueParts = (row.NewValue ?? string.Empty).Split('|');
        if (newValueParts.Length > 0) reasonName = newValueParts[0].Trim();
        if (newValueParts.Length > 1 &&
            DateTime.TryParseExact(newValueParts[1].Trim(), "yyyy-MM-dd",
                global::System.Globalization.CultureInfo.InvariantCulture,
                global::System.Globalization.DateTimeStyles.None, out var parsedDate))
        {
            eventDate = parsedDate;
        }

        entries.Add(new MemberAdjustEntry
        {
            Direction = direction,
            Name = name,
            IdCard = idCard,
            RelationshipToHead = relation,
            MemberCategory = category,
            ReasonName = reasonName,
            EventDate = eventDate,
            ChangeDate = rec.ChangeDate,
            ChangeType = rec.ChangeType
        });
    }

    /// <summary>成员分类标识（nc_biz_family_members.member_category 的取值）</summary>
    private static bool IsMemberCategory(string token) =>
        token is MemberCategoryConstants.SHARED_LIVING or MemberCategoryConstants.SUPPORT or MemberCategoryConstants.HOUSEHOLD_HEAD;

    /// <summary>
    /// 从变更原因解析被减员人：
    /// "成员死亡: 张三(身份证)" / "户主死亡: 张三(身份证) → 新户主 李四"（有证件号）；
    /// "户主死亡变更: 贺传波 → 新户主 李建英"（历史原因无证件号 → 只取姓名，IdCard 留空由成员表回填）。
    /// </summary>
    private static bool TryParseMemberFromReason(string? reason, out string name, out string idCard)
    {
        name = string.Empty;
        idCard = string.Empty;
        if (string.IsNullOrWhiteSpace(reason)) return false;

        var colonIdx = reason.IndexOf(':');
        var fullColonIdx = reason.IndexOf('：');
        if (colonIdx < 0) colonIdx = fullColonIdx;
        else if (fullColonIdx >= 0) colonIdx = Math.Min(colonIdx, fullColonIdx);
        if (colonIdx < 0 || colonIdx >= reason.Length - 1) return false;

        var rest = reason.Substring(colonIdx + 1).Trim();
        // 冒号后可能接长句说明，截断到箭头/换行/标点为止
        var stopIdx = rest.IndexOfAny(new[] { '→', '\n', '\r', '，', '。', '；', ',' });
        if (stopIdx > 0) rest = rest[..stopIdx].Trim();

        var parenStart = rest.IndexOf('(');
        var parenEnd = rest.IndexOf(')');
        if (parenStart > 0 && parenEnd > parenStart)
        {
            name = rest[..parenStart].Trim();
            idCard = rest[(parenStart + 1)..parenEnd].Trim();
            return name.Length > 0 && idCard.Length > 0;
        }

        name = rest.Trim();
        return IsPersonName(name);
    }

    /// <summary>姓名形态校验（无证件号兜底时用）：2~8 字中文，不含数字与标点</summary>
    private static bool IsPersonName(string name)
    {
        if (name.Length is < 2 or > 8) return false;
        foreach (var c in name)
        {
            if (c < 0x4E00 || c > 0x9FA5) return false;
        }
        return true;
    }

    /// <summary>
    /// 查询最近一条与渐退退出相关的变更（经济复核/户主死亡/触发停保）。
    /// 档案渐退审批表「退出渐退期情况」分型拼句取数；无匹配则 Value=null（非失败）。
    /// </summary>
    public async Task<Result<GraceExitChangeRecord?>> GetLatestGraceExitChangeAsync(long applicationId, CancellationToken ct = default)
    {
        var sql = @"SELECT change_date, change_reason_type, change_type, change_reason,
                           triggered_stop, new_classification, old_classification
                    FROM nc_biz_change_records
                    WHERE (application_id = $1 OR new_application_id = $1)
                      AND deleted_at IS NULL
                      AND (
                        change_reason_type IN ('经济复核','户主死亡')
                        OR change_type IN ('FundChange','HouseholdDeath','CategoryStop')
                        OR triggered_stop = true
                      )
                    ORDER BY change_date DESC, id DESC LIMIT 1";
        var result = await _db.QuerySingleAsync<GraceExitChangeRecord>(sql, ct, applicationId);
        if (result.IsFailure)
            return Result.Failure<GraceExitChangeRecord?>(result.ErrorCode!, result.Message!);
        return Result.Success<GraceExitChangeRecord?>(result.Value);
    }

    /// <summary>
    /// 查询最近一条复核/变更记录（定期复核审批表复核组字段取数；经济复核或死亡/成员变更等完整流程均覆盖；无匹配则 Value=null 非失败）。
    /// </summary>
    public async Task<Result<ReviewChangeRecord?>> GetLatestReviewChangeRecordAsync(long applicationId, CancellationToken ct = default)
    {
        var sql = @"SELECT id, change_date, change_reason, change_reason_type, change_type,
                           old_classification, new_classification,
                           old_guarantee_amount, new_guarantee_amount
                    FROM nc_biz_change_records
                    WHERE (application_id = $1 OR new_application_id = $1)
                      AND deleted_at IS NULL
                      AND (
                        change_reason_type IN ('经济复核','户主死亡','成员变更')
                        OR change_type IN ('FundChange','CategoryAdd','CategoryStop','HouseholdDeath','MemberDeath','MemberRemove')
                      )
                    ORDER BY change_date DESC, id DESC LIMIT 1";
        var result = await _db.QuerySingleAsync<ReviewChangeRecord>(sql, ct, applicationId);
        if (result.IsFailure)
            return Result.Failure<ReviewChangeRecord?>(result.ErrorCode!, result.Message!);
        return Result.Success<ReviewChangeRecord?>(result.Value);
    }

    /// <summary>
    /// 查询指定变更记录的 Before/After 快照 JSON（无匹配子查询返回 NULL 行，两键均空时 Value=null 非失败）。
    /// </summary>
    public async Task<Result<ChangeSnapshotPair?>> GetChangeSnapshotsAsync(long changeId, CancellationToken ct = default)
    {
        var sql = @"SELECT (SELECT snapshot_data::text FROM nc_biz_change_snapshots
                            WHERE change_id = $1 AND snapshot_type = 'Before') AS before_json,
                           (SELECT snapshot_data::text FROM nc_biz_change_snapshots
                            WHERE change_id = $1 AND snapshot_type = 'After') AS after_json";
        var result = await _db.QuerySingleAsync<ChangeSnapshotPair>(sql, ct, changeId);
        if (result.IsFailure)
            return Result.Failure<ChangeSnapshotPair?>(result.ErrorCode!, result.Message!);
        var pair = result.Value;
        if (pair != null && string.IsNullOrEmpty(pair.BeforeJson) && string.IsNullOrEmpty(pair.AfterJson))
            return Result.Success<ChangeSnapshotPair?>(null);
        return Result.Success<ChangeSnapshotPair?>(pair);
    }

    /// <inheritdoc/>
    public async Task<Result<ChangeSnapshotPair?>> GetLinkedChangeSnapshotsAsync(long applicationId, CancellationToken ct = default)
    {
        // 仅取含快照的关联记录（CategoryAdd/CategoryStop 跨类行本身无快照，会被 EXISTS 排除），
        // 无关联记录时 0 行 → QuerySingleAsync 返回 Success(null)
        var sql = @"SELECT (SELECT s.snapshot_data::text FROM nc_biz_change_snapshots s
                            WHERE s.change_id = c.id AND s.snapshot_type = 'Before') AS before_json,
                           (SELECT s.snapshot_data::text FROM nc_biz_change_snapshots s
                            WHERE s.change_id = c.id AND s.snapshot_type = 'After') AS after_json
                    FROM nc_biz_change_records c
                    WHERE (c.application_id = $1 OR c.new_application_id = $1)
                      AND c.deleted_at IS NULL
                      AND EXISTS (SELECT 1 FROM nc_biz_change_snapshots s WHERE s.change_id = c.id)
                    ORDER BY c.change_date DESC, c.id DESC
                    LIMIT 1";
        var result = await _db.QuerySingleAsync<ChangeSnapshotPair>(sql, ct, applicationId);
        if (result.IsFailure)
            return Result.Failure<ChangeSnapshotPair?>(result.ErrorCode!, result.Message!);
        var pair = result.Value;
        if (pair != null && string.IsNullOrEmpty(pair.BeforeJson) && string.IsNullOrEmpty(pair.AfterJson))
            return Result.Success<ChangeSnapshotPair?>(null);
        return Result.Success<ChangeSnapshotPair?>(pair);
    }

    /// <summary>
    /// 查询最近一条含新旧人均收入的变更记录（渐退期审批表「变动情况说明」经济复核场景取数）。
    /// 经济复核按同档更新，旧人均收入仅存于变更记录；无匹配则 Value=null（非失败）。
    /// </summary>
    public async Task<Result<IncomeComparisonRecord?>> GetLatestIncomeComparisonAsync(long applicationId, CancellationToken ct = default)
    {
        var sql = @"SELECT old_per_capita_income, new_per_capita_income, change_date, change_type
                    FROM nc_biz_change_records
                    WHERE (application_id = $1 OR new_application_id = $1)
                      AND deleted_at IS NULL
                      AND old_per_capita_income IS NOT NULL
                      AND new_per_capita_income IS NOT NULL
                    ORDER BY change_date DESC, id DESC LIMIT 1";
        var result = await _db.QuerySingleAsync<IncomeComparisonRecord>(sql, ct, applicationId);
        if (result.IsFailure)
            return Result.Failure<IncomeComparisonRecord?>(result.ErrorCode!, result.Message!);
        return Result.Success<IncomeComparisonRecord?>(result.Value);
    }

    /// <summary>
    /// 查询最近一条挂在旧档上的、Before 快照含 Components 的变更（渐退审批表分项对比取复核前旧值用）。
    /// 仅经济复核入口写入 Components；死亡/户主变更链无此键 → Value=null（消费方回退旧档行）。
    /// </summary>
    public async Task<Result<BeforeSnapshotOldValues?>> GetLatestBeforeSnapshotAsync(long originalApplicationId, CancellationToken ct = default)
    {
        var sql = @"SELECT s.snapshot_data::text AS snapshot_json
                    FROM nc_biz_change_snapshots s
                    JOIN nc_biz_change_records c ON c.id = s.change_id
                    WHERE c.application_id = $1
                      AND c.deleted_at IS NULL
                      AND s.snapshot_type = 'Before'
                      AND jsonb_typeof(s.snapshot_data -> 'Components') = 'object'
                    ORDER BY c.id DESC LIMIT 1";
        var result = await _db.QuerySingleAsync<SnapshotJsonRow>(sql, ct, originalApplicationId);
        if (result.IsFailure)
            return Result.Failure<BeforeSnapshotOldValues?>(result.ErrorCode!, result.Message!);

        var json = result.Value?.SnapshotJson;
        if (string.IsNullOrEmpty(json))
            return Result.Success<BeforeSnapshotOldValues?>(null);

        try
        {
            // 快照含 Classification/TotalIncome 等额外键，未映射成员默认忽略
            return Result.Success<BeforeSnapshotOldValues?>(JsonSerializer.Deserialize<BeforeSnapshotOldValues>(json));
        }
        catch (JsonException ex)
        {
            LogWarn($"解析 Before 快照旧值失败: OriginalApplicationId={originalApplicationId}, {ex.Message}");
            return Result.Success<BeforeSnapshotOldValues?>(null);
        }
    }

    private sealed class SnapshotJsonRow
    {
        public string? SnapshotJson { get; set; }
    }
}
