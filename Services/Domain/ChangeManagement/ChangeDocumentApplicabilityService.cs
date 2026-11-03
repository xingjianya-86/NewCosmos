using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Results;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.SocialAssistance;

namespace NewCosmos.Services.Domain.ChangeManagement;

/// <summary>
/// 变动文书事实快照：按人员属性/变动事实判定《变动文书》清单适用性的唯一口径。
/// 档案输出（ArchiveOutputViewModel 清单过滤）与申请表（ApplicationFormViewModel 预勾选名单）
/// 共用本快照，防两处规则漂移（曾因补打侧无预勾选名单导致全量默认打印）。
/// </summary>
public sealed class ChangeDocumentFacts
{
    public long ApplicationId { get; init; }

    /// <summary>档案分类认定结果（单人保排除等判定用）</summary>
    public string? Classification { get; init; }

    /// <summary>渐退期活跃记录存在（nc_biz_grace_periods）</summary>
    public bool HasActiveGracePeriod { get; init; }

    /// <summary>渐退封顶减发（原额 &gt; 渐退现发且现发 &gt; 0，与预勾选原判定同口径）</summary>
    public bool HasGrantReduce { get; init; }

    /// <summary>成员增减变动记录存在（与增减员调整表取数同源）</summary>
    public bool HasMemberChange { get; init; }

    /// <summary>链上有「保障金变更登记」记录（告知书"待遇调整"场景与「资金变更」分类依据）</summary>
    public bool HasFundChangeRegistration { get; init; }

    /// <summary>复核/成员变更收入超标停保（triggered_stop 且新分类为停保码）</summary>
    public bool IsIncomeStopChange { get; init; }

    /// <summary>单人户户主死亡整户停止</summary>
    public bool IsHouseholdDeathStop { get; init; }

    /// <summary>链上有经济复核类记录（change_type ∈ FundChange/CategoryStop/CategoryAdd——
    /// 经济复核主记录、跨类停旧建新、渐退超限减发、保障金变更登记；纯户主/成员死亡记录不算复核）。
    /// 「经济复核」分类与《档案_定期复核审批表》的适用依据。</summary>
    public bool HasEconomicReviewRecord { get; init; }

    /// <summary>链上有户主变更/户主死亡记录（停旧建新与单人户整户停均命中）</summary>
    public bool HasHeadChange { get; init; }

    /// <summary>
    /// 死亡停保旧档：本档已停保（Stopped）且链上有户主死亡/户主变更记录——户主死亡被停止的旧档
    ///（单人户整户停或停旧建新的旧档，家庭已不存在）。此类档案的变动文书只保留《档案_变更告知书》
    ///（对齐户主死亡变更流程的强白名单口径：封面/目录/变更告知书）——绝户档案不出入户调查表
    ///（无从调查）、增员减员调整表（家庭不存在）、定期复核审批表（无复核行为）。
    /// </summary>
    public bool IsDeathStoppedArchive { get; init; }

    /// <summary>
    /// 归档终结拒保：本档已停保（Stopped）且档案自身分类为停保码（收入超标/不予认定族）。
    /// 首次申请即被拒的独立新档（CompleteArchiveAsync 直接置 Stopped，**不写变更记录**）——
    /// 装配端《ArchiveProductionViewModel》告知书方向②有同口径兜底（按档案自身停保码出"不予认定"），
    /// 而 IsIncomeStopChange 恒 false。清单门若不同步，此类档案在「变动文书」清单里会被过滤漏打
    ///（整档路径靠停保码命中模板不受影响；模板 241 挂上变动叶子后本判定即成为它的过滤依据）。
    /// </summary>
    public bool IsArchiveRefusedStop { get; init; }

    /// <summary>
    /// 《档案_变更告知书》适用门（与装配端四个方向逐一对齐，**模板唯一、方向由装配端决定**）：
    ///  ① 户主死亡整户停 → IsHouseholdDeathStop；
    ///  ② 停保/不予认定 → IsIncomeStopChange，或归档终结拒保兜底 IsArchiveRefusedStop；
    ///  ③ 待遇调整——两个**互斥**子场景，装配端按 **登记优先** 顺序判定：
    ///     a. 保障金变更登记 → HasFundChangeRegistration（②③受单人保排除：单人保不产生停保，也不写登记记录）；
    ///     b. 渐退封顶减发 → HasGrantReduce（原额 &gt; 渐退现发 &gt; 0；渐退活跃时登记被服务端跳过，
    ///        两者业务互斥，见 ChangeService.RebuildAndReview.cs 渐退期互斥注释）。
    ///        单人保虽可触发封顶减发，业务口径同样**排除**（2026-11 拍板）→ 归入同一单人保排除。
    /// 与档案输出原三重门逐字同口径（死亡是客观事件，不受单人保排除限制）。
    /// </summary>
    public bool NoticeApplicable =>
        IsHouseholdDeathStop
        || IsArchiveRefusedStop
        || (!ClassificationConstants.IsCodeSingleRescue(Classification ?? string.Empty)
            && (IsIncomeStopChange || HasFundChangeRegistration || HasGrantReduce));
}

/// <summary>变动文书适用性判定（事实快照 + 分类→事实映射的单点实现）</summary>
public interface IChangeDocumentApplicabilityService
{
    /// <summary>
    /// 查询档案的变动文书事实快照。
    /// 全部子查询成功才返回 Success；任一失败返回 Failure——调用方降级为"清单不做过滤/预选仅保底"，
    /// 宁多打不漏打（DB 抖动不得误杀适用文书）。
    /// </summary>
    Task<Result<ChangeDocumentFacts>> GetFactsAsync(long applicationId, string? classification, CancellationToken ct = default);

    /// <summary>
    /// 判定模板在《变动文书》清单中是否适用：模板命中的变动分类（DocumentOperationCategories）中
    /// **任一**分类的事实成立即适用（OR 语义）；未映射的新分类保守放行；未命中任何变动分类的模板
    /// （整档材料等）不在本过滤范围，一律适用。事实查询失败的降级（全放行）由调用方以 facts 缺省处理。
    /// </summary>
    bool IsTemplateApplicable(IReadOnlyCollection<string>? templateCategories, ChangeDocumentFacts facts);
}

/// <inheritdoc />
public class ChangeDocumentApplicabilityService : IChangeDocumentApplicabilityService
{
    private readonly IDatabaseService _db;
    private readonly IGracePeriodService _gracePeriodService;
    private readonly IChangeService _changeService;

    public ChangeDocumentApplicabilityService(
        IDatabaseService db,
        IGracePeriodService gracePeriodService,
        IChangeService changeService)
    {
        _db = db;
        _gracePeriodService = gracePeriodService;
        _changeService = changeService;
    }

    public async Task<Result<ChangeDocumentFacts>> GetFactsAsync(long applicationId, string? classification, CancellationToken ct = default)
    {
        if (applicationId <= 0)
            return Result.Failure<ChangeDocumentFacts>(ErrorCodes.VALIDATION_FAILED, "档案ID无效，无法判定变动文书适用性");

        try
        {
            // 1. 渐退期（活跃记录 + 封顶减发；与申请表预勾选判定同口径）
            var graceRes = await _gracePeriodService.GetActiveAsync(applicationId, ct);
            if (graceRes.IsFailure)
                return Result.Failure<ChangeDocumentFacts>(graceRes.ErrorCode!, graceRes.Message!);
            var grace = graceRes.Value;
            var hasGrace = grace != null;
            var hasGrantReduce = grace is { } g
                && g.OriginalGuaranteeAmount is decimal original
                && g.GraceGrantAmount is decimal grant
                && original > grant && grant > 0;

            // 2. 成员增减变动记录（增减员调整表/「人员变更」分类依据，与预勾选原判定同源）
            var memberRes = await _changeService.GetMemberChangeRecordsAsync(applicationId, 5, ct);
            if (memberRes.IsFailure)
                return Result.Failure<ChangeDocumentFacts>(memberRes.ErrorCode!, memberRes.Message!);
            var hasMemberChange = memberRes.Value is { Count: > 0 };

            // 3. 保障金变更登记（告知书"待遇调整"场景与「资金变更」分类依据）
            var fundRegRes = await _changeService.HasFundChangeRegistrationAsync(applicationId, ct);
            if (fundRegRes.IsFailure)
                return Result.Failure<ChangeDocumentFacts>(fundRegRes.ErrorCode!, fundRegRes.Message!);

            // 4. 复核/成员变更收入超标停保（告知书"停保"场景依据）
            var stopRes = await _changeService.HasTriggeredCategoryStopAsync(
                applicationId, ClassificationConstants.StopCategoryCodes, ct);
            if (stopRes.IsFailure)
                return Result.Failure<ChangeDocumentFacts>(stopRes.ErrorCode!, stopRes.Message!);

            // 5. 单人户户主死亡整户停止（告知书"终止"场景依据）
            var deathRes = await _changeService.GetLatestHouseholdDeathStopRecordAsync(applicationId, ct);
            if (deathRes.IsFailure)
                return Result.Failure<ChangeDocumentFacts>(deathRes.ErrorCode!, deathRes.Message!);

            // 6. 本档状态与档案自身分类（死亡停保旧档、归档终结拒保判定用；均以库中当前值为权威）
            var statusRes = await QueryArchiveStateAsync(applicationId, ct);
            if (statusRes.IsFailure)
                return Result.Failure<ChangeDocumentFacts>(statusRes.ErrorCode!, statusRes.Message!);
            var archiveState = statusRes.Value;
            var isArchiveStopped = string.Equals(
                archiveState?.Status, ApplicationStatus.Stopped.GetCode(), StringComparison.Ordinal);
            // 归档终结拒保兜底口径与装配端一致：以档案自身 classification_result 为准（调用方传入值仅作缺省回退）
            var ownClassification = string.IsNullOrEmpty(archiveState?.ClassificationResult)
                ? classification
                : archiveState!.ClassificationResult;

            // 7. 链上变更类型命中（经济复核类 FundChange/CategoryStop/CategoryAdd / 户主变更·户主死亡；
            //    记录可能挂旧档+new_application_id，须链感知）
            var typesRes = await QueryLinkedChangeTypesAsync(applicationId, ct);
            if (typesRes.IsFailure)
                return Result.Failure<ChangeDocumentFacts>(typesRes.ErrorCode!, typesRes.Message!);
            var types = typesRes.Value ?? new HashSet<string>(StringComparer.Ordinal);

            var hasEconomicReview = types.Contains(DictionaryConstants.ChangeType.FUND_CHANGE)
                                 || types.Contains(DictionaryConstants.ChangeType.CATEGORY_STOP)
                                 || types.Contains(DictionaryConstants.ChangeType.CATEGORY_ADD);
            var hasHeadChange = types.Contains(DictionaryConstants.ChangeType.HOUSEHOLD_HEAD_CHANGE)
                             || types.Contains(DictionaryConstants.ChangeType.HOUSEHOLD_DEATH);

            return Result.Success(new ChangeDocumentFacts
            {
                ApplicationId = applicationId,
                Classification = ownClassification,
                HasActiveGracePeriod = hasGrace,
                HasGrantReduce = hasGrantReduce,
                HasMemberChange = hasMemberChange,
                HasFundChangeRegistration = fundRegRes.Value,
                IsIncomeStopChange = stopRes.Value,
                IsHouseholdDeathStop = deathRes.Value != null,
                HasEconomicReviewRecord = hasEconomicReview,
                HasHeadChange = hasHeadChange,
                IsDeathStoppedArchive = hasHeadChange && isArchiveStopped,
                // 归档终结拒保兜底：已停保 + 档案自身分类为停保码（与装配端告知书方向②同口径）
                IsArchiveRefusedStop = isArchiveStopped
                    && ClassificationConstants.IsCodeStop(ownClassification ?? string.Empty)
            });
        }
        catch (Exception ex)
        {
            return Result.FromException<ChangeDocumentFacts>(ex);
        }
    }

    /// <summary>
    /// 链感知取本档关联（application_id 或 new_application_id）变更记录的 change_type 去重集合。
    /// 批量条件走 = ANY($2)（§8），一次查询覆盖三类判定，不做循环逐行 SQL。
    /// </summary>
    private async Task<Result<HashSet<string>>> QueryLinkedChangeTypesAsync(long applicationId, CancellationToken ct)
    {
        const string sql = @"SELECT DISTINCT change_type
                             FROM nc_biz_change_records
                             WHERE (application_id = $1 OR new_application_id = $1)
                               AND deleted_at IS NULL
                               AND change_type = ANY($2)";
        var wanted = new[]
        {
            DictionaryConstants.ChangeType.FUND_CHANGE,
            DictionaryConstants.ChangeType.CATEGORY_STOP,
            DictionaryConstants.ChangeType.CATEGORY_ADD,
            DictionaryConstants.ChangeType.HOUSEHOLD_HEAD_CHANGE,
            DictionaryConstants.ChangeType.HOUSEHOLD_DEATH
        };
        var result = await _db.QueryAsync<ChangeTypeRow>(sql, ct, applicationId, wanted);
        if (result.IsFailure)
            return Result.Failure<HashSet<string>>(result.ErrorCode!, result.Message!);
        return Result.Success(new HashSet<string>(
            (result.Value ?? new List<ChangeTypeRow>()).Select(r => r.ChangeType ?? string.Empty),
            StringComparer.Ordinal));
    }

    /// <summary>查询本档当前状态与档案自身分类（死亡停保旧档 / 归档终结拒保判定用；0 行返回 Success(null)）</summary>
    private async Task<Result<ArchiveStateRow?>> QueryArchiveStateAsync(long applicationId, CancellationToken ct)
    {
        const string sql = @"SELECT status, classification_result FROM nc_biz_applications
                             WHERE id = $1 AND deleted_at IS NULL";
        var result = await _db.QuerySingleAsync<ArchiveStateRow>(sql, ct, applicationId);
        if (result.IsFailure)
            return Result.Failure<ArchiveStateRow?>(result.ErrorCode!, result.Message!);
        return Result.Success<ArchiveStateRow?>(result.Value);
    }

    /// <summary>单列 change_type 映射行（snake_case → PascalCase 由映射层转换）</summary>
    private sealed class ChangeTypeRow
    {
        public string? ChangeType { get; set; }
    }

    /// <summary>status + classification_result 映射行（snake_case → PascalCase 由映射层转换）</summary>
    private sealed class ArchiveStateRow
    {
        public string? Status { get; set; }
        public string? ClassificationResult { get; set; }
    }

    public bool IsTemplateApplicable(IReadOnlyCollection<string>? templateCategories, ChangeDocumentFacts facts)
    {
        if (templateCategories == null || templateCategories.Count == 0) return true;

        var hitChangeCategory = false;
        foreach (var category in templateCategories)
        {
            if (Array.IndexOf(ArchiveCategoryResolver.DocumentOperationCategories, category) < 0)
                continue; // 档案分类码等非变动分类不参与本过滤
            hitChangeCategory = true;

            // 死亡停保旧档（绝户/停旧建新旧档）：家庭已不存在，变动文书只保留《档案_变更告知书》
            //（对齐户主死亡变更流程强白名单口径：封面/目录/变更告知书——绝户无从入户调查、
            //  无增减员事实、无复核行为；其余模板一律不出，防"死人档案打活人文书"）
            if (facts.IsDeathStoppedArchive)
            {
                if (category == DocumentTemplateNames.ChangeNotice && facts.NoticeApplicable)
                    return true;
                continue;
            }

            var applicable = category switch
            {
                DocumentTemplateNames.GraceApproval => facts.HasActiveGracePeriod,
                DocumentTemplateNames.MemberChangeTable => facts.HasMemberChange,
                DocumentTemplateNames.ChangeNotice => facts.NoticeApplicable,
                ChangeCategories.PeriodicReviewApproval => facts.HasEconomicReviewRecord,
                ChangeCategories.EconomicReview => facts.HasEconomicReviewRecord,
                ChangeCategories.FundChange => facts.HasFundChangeRegistration,
                ChangeCategories.MemberChange => facts.HasMemberChange,
                ChangeCategories.HeadChange => facts.HasHeadChange,
                _ => true // 未映射的新变动分类保守放行，防漏打
            };
            if (applicable) return true; // OR 语义：任一命中分类事实成立即适用
        }

        // 未命中任何变动分类 → 不属本过滤范围（整档材料等）
        return !hitChangeCategory;
    }

    /// <summary>变动文书分类叶子（无模板名常量的分类码单点维护；与 TemplateCategoryProvider 变更组叶子对齐）</summary>
    private static class ChangeCategories
    {
        public const string EconomicReview = "经济复核";
        public const string FundChange = "资金变更";
        public const string MemberChange = "人员变更";
        public const string HeadChange = "户主变更";
        public const string PeriodicReviewApproval = "档案_定期复核审批表";
    }
}
