using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.System;

namespace NewCosmos.Services.Domain.ElderlyBenefits;

public partial class ElderlyApplicationService
{

    // ═══════════════════════════════════════════════════════════════════
    //  类别复核（身份 + 年龄重评，停旧增新）
    // ═══════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<Result<int>> TriggerReviewsForHouseholdAsync(long applicationId, string triggerSource, CancellationToken ct = default)
    {
        try
        {
            // 申请人 + 共同生活成员（赡养抚养人不参与）；排除死亡登记成员
            const string appSql = "SELECT applicant_id_card FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL";
            var appResult = await _db.QuerySingleAsync<string?>(appSql, ct, applicationId);
            if (appResult.IsFailure)
                return Result.Failure<int>(appResult.ErrorCode!, appResult.Message!);

            const string memberSql = @"
                SELECT DISTINCT m.id_card
                FROM nc_biz_family_members m
                WHERE m.application_id = $1 AND m.deleted_at IS NULL
                  AND m.id_card IS NOT NULL AND length(m.id_card) = 18
                  AND (m.is_applicant OR m.member_category = 'SharedLiving')
                  AND NOT EXISTS (
                      SELECT 1 FROM nc_biz_death_records d
                      WHERE d.application_id = m.application_id AND d.member_id_card = m.id_card)";
            var membersResult = await _db.QueryAsync<string>(memberSql, ct, applicationId);
            if (membersResult.IsFailure)
                return Result.Failure<int>(membersResult.ErrorCode!, membersResult.Message!);

            var cards = new List<string>();
            if (!string.IsNullOrWhiteSpace(appResult.Value)) cards.Add(appResult.Value!);
            cards.AddRange(membersResult.Value ?? new List<string>());

            return await TriggerReviewsForIdCardsAsync(cards, triggerSource, applicationId,
                "低收入家庭信息变更", ct);
        }
        catch (Exception ex)
        {
            LogException(ex, "TriggerReviewsForHouseholdAsync");
            return Result.FromException<int>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<int>> ScanAndEnqueueIdentityUpgradesAsync(CancellationToken ct = default)
    {
        try
        {
            // 粗筛宁宽勿漏：候选只保证"来源人员 + 身份命中 + 无 Pending"，
            // 是否真有类别/金额变化由 TriggerReviewsForIdCardsAsync → EvaluateReviewAsync
            // （内部走 MatchIdentityAsync 重探 + HasChange 判定）精确兜底。
            // 身份命中 = 5 张导入 persons 表任一 ∪ Approved 救助档案的户主/成员（二级探测，
            // 与 ElderlyCategoryService.MatchIdentityAsync 的两级口径对齐）。
            const string sql = @"
                SELECT DISTINCT cand.id_card
                FROM (
                    SELECT h.id_card FROM nc_biz_elderly_subsidy_history h
                    WHERE h.status = $1 AND h.id_card IS NOT NULL AND length(h.id_card) = 18
                    UNION
                    SELECT e.id_card FROM nc_biz_elderly_applications e
                    WHERE e.status = $2 AND e.deleted_at IS NULL
                      AND e.id_card IS NOT NULL AND length(e.id_card) = 18
                ) cand
                WHERE (
                    EXISTS (SELECT 1 FROM nc_biz_urban_subsistence_persons p WHERE p.id_card = cand.id_card)
                    OR EXISTS (SELECT 1 FROM nc_biz_rural_subsistence_persons p WHERE p.id_card = cand.id_card)
                    OR EXISTS (SELECT 1 FROM nc_biz_low_income_edge_persons p WHERE p.id_card = cand.id_card)
                    OR EXISTS (SELECT 1 FROM nc_biz_destitute_persons p WHERE p.id_card = cand.id_card)
                    OR EXISTS (SELECT 1 FROM nc_biz_low_income_persons p WHERE p.id_card = cand.id_card)
                    OR EXISTS (SELECT 1 FROM nc_biz_applications a
                               WHERE a.deleted_at IS NULL AND a.status = $3
                                 AND (a.applicant_id_card = cand.id_card
                                      OR EXISTS (SELECT 1 FROM nc_biz_family_members fm
                                                 WHERE fm.application_id = a.id
                                                   AND fm.id_card = cand.id_card
                                                   AND fm.deleted_at IS NULL))
                               LIMIT 1)
                )
                AND NOT EXISTS (SELECT 1 FROM nc_biz_elderly_reviews r
                                WHERE r.id_card = cand.id_card AND r.status = $4 AND r.deleted_at IS NULL)
                ORDER BY cand.id_card
                LIMIT 1000";

            var scanResult = await _db.QueryAsync<string>(sql, ct,
                ElderlyBenefitConstants.StatusActive,
                ElderlyBenefitConstants.StatusConfirmed,
                ApplicationStatusCodes.APPROVED,
                ElderlyBenefitConstants.ReviewStatusPending);
            if (scanResult.IsFailure)
                return Result.Failure<int>(scanResult.ErrorCode!, scanResult.Message!);

            var cards = scanResult.Value ?? new List<string>();
            if (cards.Count == 0)
            {
                LogInfo("身份升级自检: 无候选人员");
                return Result.Success(0);
            }

            LogInfo($"身份升级自检: 候选 {cards.Count} 人，开始评估入队");

            var enqueueResult = await TriggerReviewsForIdCardsAsync(cards,
                ElderlyBenefitConstants.ReviewTriggerImport, null, "身份升级自检", ct);
            if (enqueueResult.IsFailure)
                return Result.Failure<int>(enqueueResult.ErrorCode!, enqueueResult.Message!);

            if (enqueueResult.Value > 0)
            {
                Logger.LogBusiness("身份升级自检-写入待复核队列",
                    ("Candidates", cards.Count),
                    ("Enqueued", enqueueResult.Value));
            }
            else
            {
                LogInfo($"身份升级自检完成: 候选 {cards.Count} 人，均无需变更");
            }
            return Result.Success(enqueueResult.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "ScanAndEnqueueIdentityUpgradesAsync");
            return Result.FromException<int>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<int>> TriggerReviewsForIdCardsAsync(IEnumerable<string> idCards, string triggerSource,
        long? triggerRef, string? triggerReason, CancellationToken ct = default)
    {
        var cards = idCards?
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? new List<string>();
        if (cards.Count == 0) return Result.Success(0);

        // 第一阶段：逐人评估（内部含身份匹配/档案查询，单人失败继续的语义必须保留）
        var evals = new List<(string Card, ElderlyReviewEvaluation Eval)>();
        foreach (var card in cards)
        {
            try
            {
                var evalResult = await EvaluateReviewAsync(null, card, null, ct);
                if (evalResult.IsFailure || evalResult.Value == null)
                {
                    LogDebug($"复核联动跳过（无法评估）: {DataMasker.MaskIdCard(card)} {evalResult.Message}");
                    continue;
                }
                var eval = evalResult.Value;
                if (!eval.HasChange) continue;
                evals.Add((card, eval));
            }
            catch (Exception ex)
            {
                LogWarn($"复核联动单人评估失败（继续）: {DataMasker.MaskIdCard(card)}, {ex.Message}");
            }
        }
        if (evals.Count == 0) return Result.Success(0);

        // 第二阶段批查①：在享档案状态（原每人 1 次 SQL → 1 次批查）
        var appIds = evals
            .Where(e => !e.Eval.IsHistoryOnly && e.Eval.ApplicationId is > 0)
            .Select(e => e.Eval.ApplicationId!.Value)
            .Distinct()
            .ToArray();
        var statusMap = new Dictionary<long, string>();
        if (appIds.Length > 0)
        {
            const string statusSql = "SELECT id, status FROM nc_biz_elderly_applications WHERE id = ANY($1) AND deleted_at IS NULL";
            var statusResult = await _db.QueryAsync<IdStatusRow>(statusSql, ct, appIds);
            if (statusResult.IsFailure)
                return Result.Failure<int>(statusResult.ErrorCode!, statusResult.Message!);
            foreach (var row in statusResult.Value ?? new List<IdStatusRow>())
                statusMap[row.Id] = row.Status ?? string.Empty;
        }

        // 第二阶段批查②：待复核去重（原每人 1 次 SQL → 1 次批查）
        const string pendingSql = @"SELECT DISTINCT id_card FROM nc_biz_elderly_reviews
                                    WHERE id_card = ANY($1) AND status = $2 AND deleted_at IS NULL";
        var pendingResult = await _db.QueryAsync<PendingCardRow>(pendingSql, ct, cards.ToArray(),
            ElderlyBenefitConstants.ReviewStatusPending);
        if (pendingResult.IsFailure)
            return Result.Failure<int>(pendingResult.ErrorCode!, pendingResult.Message!);
        var pendingCards = new HashSet<string>(
            (pendingResult.Value ?? new List<PendingCardRow>()).Select(r => r.IdCard ?? string.Empty),
            StringComparer.Ordinal);

        // 第三阶段：过滤后逐人落队（去重/状态语义与原实现一致）
        var created = 0;
        foreach (var (card, eval) in evals)
        {
            try
            {
                // 在享档案必须为 Confirmed（已停发档案不入队）
                if (!eval.IsHistoryOnly && eval.ApplicationId is > 0)
                {
                    if (!statusMap.TryGetValue(eval.ApplicationId.Value, out var status)
                        || !string.Equals(status, ElderlyBenefitConstants.StatusConfirmed, StringComparison.Ordinal))
                        continue;
                }

                // 待复核去重：同一身份证仅保留一条 Pending
                if (pendingCards.Contains(card)) continue;

                await InsertPendingReviewAsync(eval, triggerSource, triggerRef, triggerReason, ct);
                created++;
            }
            catch (Exception ex)
            {
                LogWarn($"复核联动单人生成失败（继续）: {DataMasker.MaskIdCard(card)}, {ex.Message}");
            }
        }

        if (created > 0)
        {
            Logger.LogBusiness("复核联动写入待复核队列",
                ("TriggerSource", triggerSource),
                ("TriggerRef", triggerRef?.ToString() ?? ""),
                ("Created", created));
        }
        return Result.Success(created);
    }

    /// <inheritdoc />
    public async Task<Result<ElderlyReviewEvaluation>> EvaluateReviewAsync(long? applicationId, string? idCard, long? historyId, CancellationToken ct = default)
    {
        try
        {
            if (applicationId is > 0)
                return await BuildEvaluationFromApplicationAsync(applicationId.Value, ct);

            if (historyId is > 0)
            {
                var row = await LoadHistoryReviewRowAsync(historyId.Value, null, ct);
                if (row == null)
                    return Result.Failure<ElderlyReviewEvaluation>(ErrorCodes.RECORD_NOT_FOUND, "名册记录不存在");

                // 在享优先：队列入队时点可能尚无正式档案，办理时已存在在享档（如先手工登记再复核）。
                // 命中则按在享档评估，否则会走名册补建旧档 → 同证出现第二条在享档案。
                var confirmedAppId = await FindConfirmedApplicationIdAsync(row.IdCard, ct);
                if (confirmedAppId is > 0)
                    return await BuildEvaluationFromApplicationAsync(confirmedAppId.Value, ct);

                return await BuildEvaluationFromHistoryAsync(row, ct);
            }

            if (string.IsNullOrWhiteSpace(idCard))
                return Result.Failure<ElderlyReviewEvaluation>(ErrorCodes.VALIDATION_FAILED, "请提供身份证号或在享档案");

            // 先查当前库（优先在享），命中则按当前档案评估；否则回落名册
            const string appSql = @"
                SELECT id FROM nc_biz_elderly_applications
                WHERE id_card = $1 AND deleted_at IS NULL
                ORDER BY CASE WHEN status = $2 THEN 0 ELSE 1 END, id DESC
                LIMIT 1";
            var appIdResult = await _db.QuerySingleAsync<long?>(appSql, ct, idCard.Trim(), ElderlyBenefitConstants.StatusConfirmed);
            if (appIdResult.IsFailure)
                return Result.Failure<ElderlyReviewEvaluation>(appIdResult.ErrorCode!, appIdResult.Message!);
            if (appIdResult.Value is > 0)
                return await BuildEvaluationFromApplicationAsync(appIdResult.Value.Value, ct);

            var historyRow = await LoadHistoryReviewRowAsync(null, idCard.Trim(), ct);
            if (historyRow == null)
                return Result.Failure<ElderlyReviewEvaluation>(ErrorCodes.RECORD_NOT_FOUND, "未找到该人员的高龄档案或名册记录");
            return await BuildEvaluationFromHistoryAsync(historyRow, ct);
        }
        catch (Exception ex)
        {
            LogException(ex, "EvaluateReviewAsync");
            return Result.FromException<ElderlyReviewEvaluation>(ex);
        }
    }

    /// <summary>
    /// 查同身份证的在享（Confirmed）档案 id，无则返回 null。
    /// 在享优先原则：复核评估先看正式在享档，名册仅在无在享档时兜底；
    /// 否则会按名册补建出第二条在享档案（同证重复，受 uq_elderly_confirmed_idcard 约束）。
    /// </summary>
    private async Task<long?> FindConfirmedApplicationIdAsync(string? idCard, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idCard)) return null;
        var result = await _db.QuerySingleAsync<long?>(
            "SELECT id FROM nc_biz_elderly_applications WHERE id_card = $1 AND status = $2 AND deleted_at IS NULL ORDER BY id DESC LIMIT 1",
            ct, idCard.Trim(), ElderlyBenefitConstants.StatusConfirmed);
        return result.IsSuccess ? result.Value : null;
    }

    /// <inheritdoc />
    public async Task<Result<List<ElderlyReview>>> GetPendingReviewsAsync(CancellationToken ct = default)
    {
        // 上限保护：待复核队列超千条属异常积压，防无界列表（调用方为全量绑定列表，无分页）
        const string sql = @"SELECT * FROM nc_biz_elderly_reviews
                     WHERE status = $1 AND deleted_at IS NULL
                     ORDER BY created_at, id LIMIT 1000";
        var result = await _db.QueryAsync<ElderlyReview>(sql, ct, ElderlyBenefitConstants.ReviewStatusPending);
        if (result.IsFailure)
            return Result.Failure<List<ElderlyReview>>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value ?? new List<ElderlyReview>());
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<ElderlyReview>>> GetReviewsPagedAsync(string keyword, string status, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        var conditions = new SqlConditionBuilder()
            .Add("deleted_at IS NULL")
            .AddIf(!string.IsNullOrEmpty(status), "status = {0}", status)
            .AddIf(!string.IsNullOrWhiteSpace(keyword), "(name ILIKE {0} OR id_card ILIKE {0})", $"%{keyword}%");

        var where = conditions.ToWhereClause();
        var countSql = $"SELECT COUNT(*) FROM nc_biz_elderly_reviews{where}";
        var querySql = $"SELECT * FROM nc_biz_elderly_reviews{where}";

        var countResult = await _db.ExecuteScalarAsync<long>(countSql, ct, conditions.GetParameters());
        if (countResult.IsFailure)
            return Result.Failure<PagedResult<ElderlyReview>>(countResult.ErrorCode!, countResult.Message!);

        var offset = (pageIndex - 1) * pageSize;
        querySql += $" ORDER BY COALESCE(reviewed_at, created_at) DESC, id DESC LIMIT ${conditions.ParamCount + 1} OFFSET ${conditions.ParamCount + 2}";
        var pageParams = new List<object?>(conditions.GetParameters()) { pageSize, offset };

        var listResult = await _db.QueryAsync<ElderlyReview>(querySql, ct, pageParams.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<ElderlyReview>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<ElderlyReview>.FromList(listResult.Value, pageIndex, pageSize, Convert.ToInt32(countResult.Value)));
    }

    /// <inheritdoc />
    public async Task<Result<List<ElderlyReview>>> GetReviewsByPersonAsync(string idCard, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idCard)) return Result.Success(new List<ElderlyReview>());
        const string sql = @"SELECT * FROM nc_biz_elderly_reviews
                             WHERE id_card = $1 AND deleted_at IS NULL
                             ORDER BY COALESCE(reviewed_at, created_at) DESC, id DESC";
        var result = await _db.QueryAsync<ElderlyReview>(sql, ct, idCard.Trim());
        if (result.IsFailure)
            return Result.Failure<List<ElderlyReview>>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value ?? new List<ElderlyReview>());
    }

    /// <inheritdoc />
    public async Task<Result<long>> EnsureReviewHistoryArchiveAsync(ElderlyReviewEvaluation eval, string operatorName, CancellationToken ct = default)
    {
        if (eval is null || string.IsNullOrWhiteSpace(eval.IdCard))
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "复核评估信息不完整，无法补建档案");

        try
        {
            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            // 补建（内部已按同证在享档幂等查重），与下方标记同事务
            var backfill = await BackfillHistoryArchiveAsync(eval, operatorName, ct);
            if (backfill.IsFailure)
                return Result.Failure<long>(backfill.ErrorCode!, backfill.Message!);

            // 名册行标记"已并入当前库"：名册侧不再把它当待建档对象，防重复入口。
            // 失败不阻断（档案已建，下次进来按在享档复用），仅告警。
            var mark = await MarkHistoryMigratedAsync(eval.IdCard, ct);
            if (mark.IsFailure)
                LogWarn($"复核补建后标记名册行失败（档案已建，不阻断）: {mark.Message}");

            await tx.CommitAsync(ct);
            Logger.LogBusiness("复核前置补建档案并标记名册",
                ("IdCard", DataMasker.MaskIdCard(eval.IdCard)),
                ("ApplicationId", backfill.Value),
                ("MarkedHistoryRows", mark.Value));
            return Result.Success(backfill.Value);
        }
        catch (OperationCanceledException)
        {
            return Result.Failure<long>(ErrorCodes.CANCELLED, "操作已取消");
        }
        catch (Exception ex)
        {
            LogException(ex, "EnsureReviewHistoryArchiveAsync");
            return Result.FromException<long>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ElderlyReviewCompletionInfo>> GetReviewIncompleteFieldsAsync(long applicationId, CancellationToken ct = default)
    {
        // 必填项与表单 Validate（ElderlyApplicationFormViewModel）同口径：
        // 联系电话 / 户籍市-县-乡-村 / 开户行 / 社保卡账号
        const string sql = @"SELECT phone, hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id,
                                    bank_name, bank_account, COALESCE(source_type, '') AS source_type
                             FROM nc_biz_elderly_applications
                             WHERE id = $1 AND deleted_at IS NULL";
        var result = await _db.QuerySingleAsync<ReviewCompletionRow>(sql, ct, applicationId);
        if (result.IsFailure)
            return Result.Failure<ElderlyReviewCompletionInfo>(result.ErrorCode!, result.Message!);
        if (result.Value == null)
            return Result.Failure<ElderlyReviewCompletionInfo>(ErrorCodes.RECORD_NOT_FOUND, "档案不存在");

        var row = result.Value;
        var info = new ElderlyReviewCompletionInfo { SourceType = row.SourceType ?? string.Empty };
        if (string.IsNullOrWhiteSpace(row.Phone)) info.MissingFields.Add("联系电话");
        if (row.HukouCityId is null or <= 0) info.MissingFields.Add("户籍市");
        if (row.HukouCountyId is null or <= 0) info.MissingFields.Add("户籍区县");
        if (row.HukouTownId is null or <= 0) info.MissingFields.Add("户籍乡镇");
        if (row.HukouVillageId is null or <= 0) info.MissingFields.Add("户籍村/社区");
        if (string.IsNullOrWhiteSpace(row.BankName)) info.MissingFields.Add("开户行");
        if (string.IsNullOrWhiteSpace(row.BankAccount)) info.MissingFields.Add("社保卡账号");
        return Result.Success(info);
    }

    /// <summary>复核完整性检查行（snake_case 列自动映射）</summary>
    private sealed class ReviewCompletionRow
    {
        public string? Phone { get; set; }
        public long? HukouCityId { get; set; }
        public long? HukouCountyId { get; set; }
        public long? HukouTownId { get; set; }
        public long? HukouVillageId { get; set; }
        public string? BankName { get; set; }
        public string? BankAccount { get; set; }
        public string? SourceType { get; set; }
    }

    /// <inheritdoc />
    public async Task<Result<long>> ConfirmReviewAsync(long? reviewId, long? applicationId, string? idCard, long? historyId,
        string reviewOpinion, string operatorName, CancellationToken ct = default)
    {
        var evalResult = reviewId is > 0
            ? await EvaluateReviewByPendingAsync(reviewId.Value, ct)
            : await EvaluateReviewAsync(applicationId, idCard, historyId, ct);
        if (evalResult.IsFailure || evalResult.Value == null)
            return Result.Failure<long>(evalResult.ErrorCode ?? ErrorCodes.REVIEW_EVALUATE_FAILED,
                evalResult.Message ?? "复核评估失败");
        var eval = evalResult.Value;

        // 护栏：按名册评估（IsHistoryOnly）但该身份证已存在在享档时，改用在享档为旧档。
        // 否则"名册补建旧档"会与既有在享档并存 → 同证两条 Confirmed（在享检索出现重复人员）。
        // 同时覆盖 Age90 月报入队等直接由队列字段构造评估、不经 EvaluateReviewAsync 的分支。
        if (eval.IsHistoryOnly)
        {
            var confirmedAppId = await FindConfirmedApplicationIdAsync(eval.IdCard, ct);
            if (confirmedAppId is > 0)
            {
                var fromApplication = await BuildEvaluationFromApplicationAsync(confirmedAppId.Value, ct);
                if (fromApplication.IsSuccess && fromApplication.Value != null)
                    eval = fromApplication.Value;
            }
        }

        // 在享档案必须为 Confirmed（名册人员除外）
        if (!eval.IsHistoryOnly && eval.ApplicationId is > 0)
        {
            var statusResult = await _db.QuerySingleAsync<string>(
                "SELECT status FROM nc_biz_elderly_applications WHERE id = $1 AND deleted_at IS NULL",
                ct, eval.ApplicationId.Value);
            if (statusResult.IsFailure || !string.Equals(statusResult.Value, ElderlyBenefitConstants.StatusConfirmed, StringComparison.Ordinal))
                return Result.Failure<long>(ErrorCodes.ELDERLY_REVIEW_INVALID_STATUS, "仅支持对在享（已确认）的高龄档案办理复核");
        }

        await using var tx = await _db.BeginTransactionScopeAsync(ct);
        try
        {
            if (!eval.HasChange)
            {
                await UpsertCompletedReviewAsync(reviewId, eval, ElderlyBenefitConstants.ReviewResultNoChange,
                    0, 0, operatorName, reviewOpinion, ct);
                await tx.CommitAsync(ct);
                Logger.LogBusiness("高龄类别复核-无需调整",
                    ("IdCard", DataMasker.MaskIdCard(eval.IdCard)),
                    ("Category", eval.OldCategory),
                    ("Operator", operatorName));
                return Result.Success(eval.ApplicationId ?? 0);
            }

            // 名册未建档：先补建旧档案（承载旧类别），再停旧增新
            long oldAppId;
            if (eval.IsHistoryOnly)
            {
                var backfill = await BackfillHistoryArchiveAsync(eval, operatorName, ct);
                if (backfill.IsFailure)
                    return Result.Failure<long>(backfill.ErrorCode!, backfill.Message!);
                oldAppId = backfill.Value;
            }
            else
            {
                oldAppId = eval.ApplicationId ?? 0;
                if (oldAppId <= 0)
                    return Result.Failure<long>(ErrorCodes.RECORD_NOT_FOUND, "未找到待复核的在享档案");
            }

            // 停旧档（stop_reason=REVIEW，报表/统计排除）
            var stopResult = await StopOldApplicationForReviewAsync(oldAppId, operatorName, ct);
            if (stopResult.IsFailure)
                return Result.Failure<long>(stopResult.ErrorCode!, stopResult.Message!);

            // 建新档（Confirmed，次月起按新档计发，不补差）
            var newAppResult = await CreateReviewedApplicationAsync(oldAppId, eval, operatorName, ct);
            if (newAppResult.IsFailure)
                return Result.Failure<long>(newAppResult.ErrorCode!, newAppResult.Message!);
            var newAppId = newAppResult.Value;

            // 名册联动标记（按身份证兜底将 Active 标记 Stopped）
            await MarkHistoryStoppedAsync(oldAppId, ct);

            await UpsertCompletedReviewAsync(reviewId, eval, ElderlyBenefitConstants.ReviewResultChanged,
                oldAppId, newAppId, operatorName, reviewOpinion, ct);

            await tx.CommitAsync(ct);
            Logger.LogBusiness("高龄类别复核-已变更（停旧增新）",
                ("IdCard", DataMasker.MaskIdCard(eval.IdCard)),
                ("OldApplicationId", oldAppId),
                ("NewApplicationId", newAppId),
                ("OldCategory", eval.OldCategory),
                ("NewCategory", eval.NewCategory),
                ("Backfilled", eval.IsHistoryOnly),
                ("Operator", operatorName));
            return Result.Success(newAppId);
        }
        catch (Exception ex)
        {
            LogException(ex, "ConfirmReviewAsync");
            return Result.FromException<long>(ex);
        }
    }

    // ── 复核：评估构建 ──

    private async Task<Result<ElderlyReviewEvaluation>> BuildEvaluationFromApplicationAsync(long applicationId, CancellationToken ct)
    {
        var appResult = await GetByIdAsync(applicationId, ct);
        if (appResult.IsFailure || appResult.Value == null)
            return Result.Failure<ElderlyReviewEvaluation>(ErrorCodes.RECORD_NOT_FOUND, "高龄档案不存在");

        var app = appResult.Value;
        var birthDate = app.BirthDate ?? IdCardValidator.ExtractBirthDate(app.IdCard);
        if (birthDate == null)
            return Result.Failure<ElderlyReviewEvaluation>(ErrorCodes.VALIDATION_FAILED, "档案缺少出生日期，无法复核");

        var age = ComputeAge(birthDate.Value, DateTime.Today);
        var identityResult = await _categoryService.MatchIdentityAsync(app.IdCard, ct);
        if (identityResult.IsFailure)
            return Result.Failure<ElderlyReviewEvaluation>(identityResult.ErrorCode!, identityResult.Message!);
        var identityFlag = identityResult.Value.IdentityFlag;
        var newCategory = ElderlyPaybackCalculator.GetCategoryForAge(age, identityFlag);
        var newAmountResult = await GetMonthlyAmountAsync(newCategory, ct);
        if (newAmountResult.IsFailure)
            return Result.Failure<ElderlyReviewEvaluation>(newAmountResult.ErrorCode!, newAmountResult.Message!);

        var oldAmount = app.IssueAmount > 0 ? app.IssueAmount : await GetAmountOrZeroAsync(app.Category, ct);
        var oldIdentity = string.IsNullOrWhiteSpace(app.IdentityFlag) ? ElderlyBenefitConstants.IdentityNone : app.IdentityFlag;

        return Result.Success(BuildEvaluationCore(
            isHistoryOnly: false,
            applicationId: app.Id, applicationNo: app.ApplicationNo ?? string.Empty, historyId: null,
            name: app.Name, idCard: app.IdCard, gender: app.Gender, birthDate: birthDate.Value, age: age,
            oldIdentity: oldIdentity, identityFlag: identityFlag, identitySource: identityResult.Value.SourceTable,
            oldCategory: app.Category, oldAmount: oldAmount,
            newCategory: newCategory, newAmount: newAmountResult.Value));
    }

    private async Task<Result<ElderlyReviewEvaluation>> BuildEvaluationFromHistoryAsync(ElderlyHistoryReviewRow row, CancellationToken ct)
    {
        var birthDate = row.BirthDate;
        if (birthDate == null)
            return Result.Failure<ElderlyReviewEvaluation>(ErrorCodes.VALIDATION_FAILED, "名册缺少出生日期，无法复核");

        var age = ComputeAge(birthDate.Value, DateTime.Today);
        var oldIdentity = ElderlyBenefitConstants.GetIdentityFlagFromHistoryPersonType(row.PersonType);
        var oldCategory = ElderlyPaybackCalculator.GetCategoryForAge(age, oldIdentity);

        var identityResult = await _categoryService.MatchIdentityAsync(row.IdCard, ct);
        if (identityResult.IsFailure)
            return Result.Failure<ElderlyReviewEvaluation>(identityResult.ErrorCode!, identityResult.Message!);
        var identityFlag = identityResult.Value.IdentityFlag;
        var newCategory = ElderlyPaybackCalculator.GetCategoryForAge(age, identityFlag);
        var newAmountResult = await GetMonthlyAmountAsync(newCategory, ct);
        if (newAmountResult.IsFailure)
            return Result.Failure<ElderlyReviewEvaluation>(newAmountResult.ErrorCode!, newAmountResult.Message!);

        var oldAmount = row.SubsidyAmount ?? await GetAmountOrZeroAsync(oldCategory, ct);

        return Result.Success(BuildEvaluationCore(
            isHistoryOnly: true,
            applicationId: null, applicationNo: string.Empty, historyId: row.Id,
            name: row.Name, idCard: row.IdCard, gender: row.Gender, birthDate: birthDate.Value, age: age,
            oldIdentity: oldIdentity, identityFlag: identityFlag, identitySource: identityResult.Value.SourceTable,
            oldCategory: oldCategory, oldAmount: oldAmount,
            newCategory: newCategory, newAmount: newAmountResult.Value));
    }

    /// <summary>评估核心：判定调整原因/方向，计算应生效月与次月起计月</summary>
    private static ElderlyReviewEvaluation BuildEvaluationCore(
        bool isHistoryOnly, long? applicationId, string applicationNo, long? historyId,
        string name, string idCard, string? gender, DateTime birthDate, int age,
        string oldIdentity, string identityFlag, string identitySource,
        string oldCategory, decimal oldAmount, string newCategory, decimal newAmount)
    {
        var identityChanged = !string.Equals(
            string.IsNullOrWhiteSpace(oldIdentity) ? ElderlyBenefitConstants.IdentityNone : oldIdentity,
            string.IsNullOrWhiteSpace(identityFlag) ? ElderlyBenefitConstants.IdentityNone : identityFlag,
            StringComparison.Ordinal);

        var oldHigh = ElderlyBenefitConstants.IsHighSubsidyIdentity(oldIdentity);
        var newHigh = ElderlyBenefitConstants.IsHighSubsidyIdentity(identityFlag);

        var reason = identityChanged
            ? ElderlyBenefitConstants.AdjustReasonIdentity
            : (!string.Equals(oldCategory, newCategory, StringComparison.Ordinal)
                ? ElderlyBenefitConstants.AdjustReasonAge
                : ElderlyBenefitConstants.AdjustReasonOther);

        var effectiveMonth = string.Empty;
        if (reason == ElderlyBenefitConstants.AdjustReasonAge)
        {
            var threshold = newCategory == ElderlyBenefitConstants.Cat100Plus ? ElderlyBenefitConstants.Threshold100
                : newCategory == ElderlyBenefitConstants.Cat90To99 ? ElderlyBenefitConstants.Threshold90
                : 0;
            if (threshold > 0)
                effectiveMonth = birthDate.AddYears(threshold).ToString("yyyy-MM");
        }

        var issueStartMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1).ToString("yyyy-MM");

        return new ElderlyReviewEvaluation
        {
            IsHistoryOnly = isHistoryOnly,
            ApplicationId = applicationId,
            ApplicationNo = applicationNo,
            HistoryId = historyId,
            Name = name ?? string.Empty,
            IdCard = idCard ?? string.Empty,
            Gender = gender ?? string.Empty,
            BirthDate = birthDate,
            Age = age,
            OldIdentityFlag = string.IsNullOrWhiteSpace(oldIdentity) ? ElderlyBenefitConstants.IdentityNone : oldIdentity,
            IdentityFlag = identityFlag ?? ElderlyBenefitConstants.IdentityNone,
            IdentitySource = identitySource ?? string.Empty,
            OldCategory = oldCategory ?? string.Empty,
            OldMonthlyAmount = oldAmount,
            NewCategory = newCategory ?? string.Empty,
            NewMonthlyAmount = newAmount,
            AdjustReasonCode = reason,
            IdentityAdd = newHigh && !oldHigh,
            EffectiveMonth = effectiveMonth,
            IssueStartMonth = issueStartMonth
        };
    }

    private async Task<decimal> GetAmountOrZeroAsync(string? category, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(category)) return 0m;
        var result = await GetMonthlyAmountAsync(category, ct);
        return result.IsSuccess ? result.Value : 0m;
    }

    private static int ComputeAge(DateTime birthDate, DateTime onDate)
    {
        var age = onDate.Year - birthDate.Year;
        if (birthDate.Date > onDate.AddYears(-age)) age--;
        return age;
    }

    private async Task<ElderlyHistoryReviewRow?> LoadHistoryReviewRowAsync(long? historyId, string? idCard, CancellationToken ct)
    {
        const string byIdSql = @"
            SELECT id, name, id_card, gender, birth_date, phone, address, bank_account,
                   person_type, subsidy_amount, data_year
            FROM nc_biz_elderly_subsidy_history WHERE id = $1";
        const string byCardSql = @"
            SELECT id, name, id_card, gender, birth_date, phone, address, bank_account,
                   person_type, subsidy_amount, data_year
            FROM nc_biz_elderly_subsidy_history WHERE id_card = $1 AND status = $2
            ORDER BY data_year DESC NULLS LAST, id DESC LIMIT 1";
        // 名册检索仅命中在册发放（Active）；byId 不限制（队列记录可能随后被标记，仍需可评估）
        var result = historyId is > 0
            ? await _db.QuerySingleAsync<ElderlyHistoryReviewRow>(byIdSql, ct, historyId.Value)
            : await _db.QuerySingleAsync<ElderlyHistoryReviewRow>(byCardSql, ct, idCard, ElderlyBenefitConstants.StatusActive);
        if (result.IsFailure || result.Value == null) return null;
        return result.Value;
    }

    // ── 复核：落库 ──

    private async Task InsertPendingReviewAsync(ElderlyReviewEvaluation eval, string triggerSource,
        long? triggerRef, string? triggerReason, CancellationToken ct)
    {
        var reviewNo = await GetNextReviewNoAsync(ct);
        const string sql = @"
            INSERT INTO nc_biz_elderly_reviews
            (review_no, id_card, name, status, trigger_source, trigger_ref, trigger_reason, source_history_id,
             old_application_id, old_application_no, old_category, old_monthly_amount,
             new_category, new_monthly_amount, identity_flag, age_at_review, effective_month, issue_start_month,
             adjust_reason_code, created_at, updated_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,NOW(),NOW())";
        var result = await _db.ExecuteNonQueryAsync(sql, ct,
            reviewNo, eval.IdCard, eval.Name, ElderlyBenefitConstants.ReviewStatusPending,
            triggerSource, triggerRef, triggerReason ?? string.Empty, eval.HistoryId,
            eval.ApplicationId, eval.ApplicationNo, eval.OldCategory, eval.OldMonthlyAmount,
            eval.NewCategory, eval.NewMonthlyAmount, eval.IdentityFlag, eval.Age,
            eval.EffectiveMonth, eval.IssueStartMonth, eval.AdjustReasonCode);
        if (result.IsFailure)
        {
            LogWarn($"写入待复核失败: {DataMasker.MaskIdCard(eval.IdCard)}, {result.Message}");
            return;
        }
        Logger.LogBusiness("写入待复核",
            ("IdCard", DataMasker.MaskIdCard(eval.IdCard)),
            ("Old", eval.OldCategory), ("New", eval.NewCategory),
            ("Trigger", triggerSource));
    }

    private async Task UpsertCompletedReviewAsync(long? reviewId, ElderlyReviewEvaluation eval, string reviewResult,
        long oldAppId, long newAppId, string operatorName, string reviewOpinion, CancellationToken ct)
    {
        var hasChange = reviewResult == ElderlyBenefitConstants.ReviewResultChanged;
        var oldAppNo = string.Empty;
        var newAppNo = string.Empty;
        if (oldAppId > 0)
        {
            var r = await _db.QuerySingleAsync<string?>(
                "SELECT application_no FROM nc_biz_elderly_applications WHERE id = $1", ct, oldAppId);
            if (r.IsSuccess) oldAppNo = r.Value ?? string.Empty;
        }
        if (newAppId > 0)
        {
            var r = await _db.QuerySingleAsync<string?>(
                "SELECT application_no FROM nc_biz_elderly_applications WHERE id = $1", ct, newAppId);
            if (r.IsSuccess) newAppNo = r.Value ?? string.Empty;
        }

        const string updateSql = @"
            UPDATE nc_biz_elderly_reviews SET
                status = $1, source_history_id = $2,
                old_application_id = $3, old_application_no = $4, old_category = $5, old_monthly_amount = $6,
                new_application_id = $7, new_application_no = $8, new_category = $9, new_monthly_amount = $10,
                identity_flag = $11, age_at_review = $12, effective_month = $13, issue_start_month = $14,
                review_result = $15, adjust_reason_code = $16, review_opinion = $17,
                reviewed_by = $18, reviewed_at = NOW(), updated_at = NOW()
            WHERE id = $19 AND deleted_at IS NULL";

        var oldCat = eval.OldCategory;
        var newCat = hasChange ? eval.NewCategory : string.Empty;
        var newAmount = hasChange ? eval.NewMonthlyAmount : (decimal?)null;

        if (reviewId is > 0)
        {
            var updateResult = await _db.ExecuteNonQueryAsync(updateSql, ct,
                ElderlyBenefitConstants.ReviewStatusCompleted, eval.HistoryId,
                oldAppId > 0 ? oldAppId : (object?)eval.ApplicationId, oldAppNo, oldCat, eval.OldMonthlyAmount,
                newAppId > 0 ? newAppId : null, newAppNo, newCat, newAmount,
                eval.IdentityFlag, eval.Age, eval.EffectiveMonth, eval.IssueStartMonth,
                reviewResult, eval.AdjustReasonCode, reviewOpinion ?? string.Empty,
                operatorName ?? "System", reviewId.Value);
            if (updateResult.IsFailure)
                LogWarn($"更新复核记录失败: {updateResult.Message}");
            return;
        }

        // 人工搜索无队列记录 → 直接补一条已完成记录
        var reviewNo = await GetNextReviewNoAsync(ct);
        const string insertSql = @"
            INSERT INTO nc_biz_elderly_reviews
            (review_no, id_card, name, status, trigger_source, trigger_ref, trigger_reason, source_history_id,
             old_application_id, old_application_no, old_category, old_monthly_amount,
             new_application_id, new_application_no, new_category, new_monthly_amount,
             identity_flag, age_at_review, effective_month, issue_start_month,
             review_result, adjust_reason_code, review_opinion, reviewed_by, reviewed_at, created_at, updated_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,$21,$22,$23,$24,NOW(),NOW(),NOW())";
        var insertResult = await _db.ExecuteNonQueryAsync(insertSql, ct,
            reviewNo, eval.IdCard, eval.Name, ElderlyBenefitConstants.ReviewStatusCompleted,
            ElderlyBenefitConstants.ReviewTriggerManual, null, string.Empty, eval.HistoryId,
            oldAppId > 0 ? oldAppId : (object?)eval.ApplicationId, oldAppNo, oldCat, eval.OldMonthlyAmount,
            newAppId > 0 ? newAppId : null, newAppNo, newCat, newAmount,
            eval.IdentityFlag, eval.Age, eval.EffectiveMonth, eval.IssueStartMonth,
            reviewResult, eval.AdjustReasonCode, reviewOpinion ?? string.Empty, operatorName ?? "System");
        if (insertResult.IsFailure)
            LogWarn($"写入复核记录失败: {insertResult.Message}");
    }

    /// <summary>名册未建档人员补建旧类别档案（source_type=ElderlyReview，不产生"本月新增"）</summary>
    private async Task<Result<long>> BackfillHistoryArchiveAsync(ElderlyReviewEvaluation eval, string operatorName, CancellationToken ct)
    {
        // 幂等护栏：同证已有在享档直接复用（停发补建/上次复核补建等场景），
        // 否则 INSERT 会撞 uq_elderly_confirmed_idcard（同证仅允许 1 条 Confirmed）
        var confirmedAppId = await FindConfirmedApplicationIdAsync(eval.IdCard, ct);
        if (confirmedAppId is > 0)
            return Result.Success(confirmedAppId.Value);

        // 优先按队列携带的名册行取电话/银行/地址，缺 HistoryId（按身份证入口）时按身份证兜底
        var row = eval.HistoryId is > 0 ? await LoadHistoryReviewRowAsync(eval.HistoryId, null, ct) : null;
        row ??= await LoadHistoryReviewRowAsync(null, eval.IdCard, ct);

        var appNoResult = await GetNextApplicationNoAsync(ct);
        if (appNoResult.IsFailure)
            return Result.Failure<long>(appNoResult.ErrorCode!, appNoResult.Message!);

        var currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).ToString("yyyy-MM");
        const string sql = @"
            INSERT INTO nc_biz_elderly_applications
            (application_no, name, id_card, gender, birth_date, phone, bank_account,
             hukou_address, family_address,
             issue_start_month, issue_amount, category, identity_flag, identity_source,
             status, apply_date, confirmed_at, confirmed_by, remark, source_type, source_id,
             created_by, created_at, updated_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,
                    $21,$15,NOW(),$16,$17,$18,$19,$20,NOW(),NOW())
            RETURNING id";
        var result = await _db.ExecuteScalarAsync<long>(sql, ct,
            appNoResult.Value, eval.Name, eval.IdCard, eval.Gender, eval.BirthDate,
            row?.Phone ?? string.Empty, row?.BankAccount ?? string.Empty,
            row?.Address ?? string.Empty, row?.Address ?? string.Empty,
            currentMonth, eval.OldMonthlyAmount, eval.OldCategory, eval.OldIdentityFlag, "nc_biz_elderly_subsidy_history",
            DateTime.Today, operatorName ?? "System",
            $"复核补建（原类别 {ElderlyBenefitConstants.GetCategoryName(eval.OldCategory)}）",
            ElderlyBenefitConstants.SourceTypeElderlyReview, eval.HistoryId, operatorName ?? "System",
            ElderlyBenefitConstants.StatusConfirmed);
        if (result.IsFailure)
            return Result.Failure<long>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value);
    }

    private async Task<Result> StopOldApplicationForReviewAsync(long oldAppId, string operatorName, CancellationToken ct)
    {
        const string sql = @"
            UPDATE nc_biz_elderly_applications SET
                status = $1, stopped_at = NOW(), actual_stop_date = NOW(), due_stop_date = NOW(),
                stopped_by = $2, stop_reason = $3, remark = $4, updated_at = NOW()
            WHERE id = $5 AND deleted_at IS NULL";
        var result = await _db.ExecuteNonQueryAsync(sql, ct,
            ElderlyBenefitConstants.StatusStopped, operatorName ?? "System",
            ElderlyBenefitConstants.StopReasonReview, "类别复核停旧增新", oldAppId);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        if (result.Value == 0)
            return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "旧龄档案不存在或已删除");
        return Result.Success();
    }

    /// <summary>复制旧档人/址/银行字段，按新类别新建 Confirmed 档案（次月起计发，不补差）</summary>
    private async Task<Result<long>> CreateReviewedApplicationAsync(long oldAppId, ElderlyReviewEvaluation eval, string operatorName, CancellationToken ct)
    {
        var appNoResult = await GetNextApplicationNoAsync(ct);
        if (appNoResult.IsFailure)
            return Result.Failure<long>(appNoResult.ErrorCode!, appNoResult.Message!);

        var remark = $"类别复核调整，由{ElderlyBenefitConstants.GetCategoryName(eval.OldCategory)}" +
                     $"调整为{ElderlyBenefitConstants.GetCategoryName(eval.NewCategory)}";
        const string sql = @"
            INSERT INTO nc_biz_elderly_applications
            (application_no, name, id_card, gender, birth_date, phone,
             hukou_address, hukou_village, hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id, hukou_detail_address,
             family_address, family_city_id, family_county_id, family_town_id, family_village_id, detail_address,
             bank_name, bank_account, agent_name, agent_relation,
             agent_receive_name, agent_receive_relation, agent_receive_bank_name, agent_receive_bank_account, agent_receive_reason,
             issue_start_month, issue_amount, category, identity_flag, identity_source, is_category_manual,
             status, apply_date, confirmed_at, confirmed_by, remark, source_type, source_id,
             created_by, created_at, updated_at)
            SELECT $1, name, id_card, gender, birth_date, phone,
             hukou_address, hukou_village, hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id, hukou_detail_address,
             family_address, family_city_id, family_county_id, family_town_id, family_village_id, detail_address,
             bank_name, bank_account, agent_name, agent_relation,
             agent_receive_name, agent_receive_relation, agent_receive_bank_name, agent_receive_bank_account, agent_receive_reason,
             $2, $3, $4, $5, $6, false,
             $13, $7::date, NOW(), $8, $9, $10, $11, $8, NOW(), NOW()
            FROM nc_biz_elderly_applications WHERE id = $12 AND deleted_at IS NULL
            RETURNING id";
        var result = await _db.ExecuteScalarAsync<long>(sql, ct,
            appNoResult.Value, eval.IssueStartMonth, eval.NewMonthlyAmount,
            eval.NewCategory, eval.IdentityFlag, eval.IdentitySource ?? string.Empty,
            DateTime.Today, operatorName ?? "System", remark,
            ElderlyBenefitConstants.SourceTypeElderlyReview, oldAppId, oldAppId,
            ElderlyBenefitConstants.StatusConfirmed);
        if (result.IsFailure)
            return Result.Failure<long>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value);
    }

    private async Task<string> GetNextReviewNoAsync(CancellationToken ct)
    {
        var prefix = $"{ElderlyBenefitConstants.ReviewNoPrefix}{DateTime.Now:yyyyMMdd}";
        var sql = @"SELECT COALESCE(MAX(CAST(SUBSTRING(review_no FROM 11) AS INTEGER)), 0) + 1
                    FROM nc_biz_elderly_reviews WHERE review_no LIKE $1";
        var result = await _db.ExecuteScalarAsync<long>(sql, ct, $"{prefix}%");
        var seq = result.IsSuccess ? result.Value : 1;
        return $"{prefix}{seq:D4}";
    }

    #region 月报：满90周岁调整备案表

    /// <inheritdoc />
    public async Task<Result<List<ElderlyAge90Row>>> GetAge90AdjustRowsAsync(int year, int month, CancellationToken ct = default)
    {
        try
        {
            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1);
            var rows = new List<ElderlyAge90Row>();
            var newAmount = await GetAmountOrZeroAsync(ElderlyBenefitConstants.Cat90To99, ct);

            // 类别金额按类别只查一次（原实现每行 1 次 SQL，类别集合计数很小）
            var amountCache = new Dictionary<string, decimal>(StringComparer.Ordinal);
            async Task<decimal> GetAmountCachedAsync(string? category, CancellationToken token)
            {
                if (string.IsNullOrWhiteSpace(category)) return 0m;
                if (amountCache.TryGetValue(category, out var cached)) return cached;
                var value = await GetAmountOrZeroAsync(category, token);
                amountCache[category] = value;
                return value;
            }

            // 在享：本月满90周岁且尚未按90档计发（CAT1/CAT2）
            const string appSql = @"
                SELECT id, name, id_card, gender, birth_date, category, identity_flag,
                       issue_amount, hukou_address, family_address, detail_address
                FROM nc_biz_elderly_applications
                WHERE deleted_at IS NULL AND status = $3
                  AND category IN ($4,$5)
                  AND birth_date IS NOT NULL
                  AND (birth_date + INTERVAL '90 years') >= $1
                  AND (birth_date + INTERVAL '90 years') < $2
                ORDER BY birth_date, id";
            var appResult = await _db.QueryAsync<Age90AppRow>(appSql, ct, monthStart, monthEnd,
                ElderlyBenefitConstants.StatusConfirmed, ElderlyBenefitConstants.CatLowSubsidy, ElderlyBenefitConstants.CatOtherElderly);
            if (appResult.IsFailure)
                return Result.Failure<List<ElderlyAge90Row>>(appResult.ErrorCode!, appResult.Message!);
            foreach (var a in appResult.Value ?? new List<Age90AppRow>())
            {
                var oldAmount = a.IssueAmount > 0 ? a.IssueAmount : await GetAmountCachedAsync(a.Category, ct);
                rows.Add(new ElderlyAge90Row
                {
                    ApplicationId = a.Id,
                    Name = a.Name,
                    IdCard = a.IdCard,
                    Gender = a.Gender,
                    BirthDate = a.BirthDate,
                    IdentityFlag = string.IsNullOrWhiteSpace(a.IdentityFlag) ? ElderlyBenefitConstants.IdentityNone : a.IdentityFlag,
                    HukouAddress = a.HukouAddress ?? string.Empty,
                    FamilyAddress = CombineAddress(a.FamilyAddress, a.DetailAddress),
                    OldCategory = a.Category,
                    OldMonthlyAmount = oldAmount,
                    NewCategory = ElderlyBenefitConstants.Cat90To99,
                    NewMonthlyAmount = newAmount,
                    EffectiveMonth = a.BirthDate!.Value.AddYears(90).ToString("yyyy-MM")
                });
            }

            // 名册未建档：本月满90周岁（旧类别按 person_type 的 89 岁档）
            const string histSql = @"
                SELECT h.id, h.name, h.id_card, h.gender, h.birth_date, h.person_type, h.address, h.subsidy_amount
                FROM nc_biz_elderly_subsidy_history h
                WHERE h.status = $3
                  AND h.birth_date IS NOT NULL
                  AND (h.birth_date + INTERVAL '90 years') >= $1
                  AND (h.birth_date + INTERVAL '90 years') < $2
                  AND NOT EXISTS (SELECT 1 FROM nc_biz_elderly_applications e
                                  WHERE e.id_card = h.id_card AND e.deleted_at IS NULL)
                ORDER BY h.birth_date, h.id";
            var histResult = await _db.QueryAsync<Age90HistRow>(histSql, ct, monthStart, monthEnd, ElderlyBenefitConstants.StatusActive);
            if (histResult.IsFailure)
                return Result.Failure<List<ElderlyAge90Row>>(histResult.ErrorCode!, histResult.Message!);
            foreach (var h in histResult.Value ?? new List<Age90HistRow>())
            {
                var identity = ElderlyBenefitConstants.GetIdentityFlagFromHistoryPersonType(h.PersonType);
                var oldCategory = ElderlyPaybackCalculator.GetCategoryForAge(ElderlyBenefitConstants.Threshold90 - 1, identity);
                var oldAmount = await GetAmountCachedAsync(oldCategory, ct);
                rows.Add(new ElderlyAge90Row
                {
                    HistoryId = h.Id,
                    Name = h.Name,
                    IdCard = h.IdCard,
                    Gender = h.Gender,
                    BirthDate = h.BirthDate,
                    IdentityFlag = identity,
                    HukouAddress = h.Address ?? string.Empty,
                    FamilyAddress = h.Address ?? string.Empty,
                    OldCategory = oldCategory,
                    OldMonthlyAmount = oldAmount,
                    NewCategory = ElderlyBenefitConstants.Cat90To99,
                    NewMonthlyAmount = newAmount,
                    EffectiveMonth = h.BirthDate!.Value.AddYears(90).ToString("yyyy-MM")
                });
            }

            LogInfo($"满90周岁名单: {year}-{month} 共 {rows.Count} 人（在享={rows.Count(r => !r.IsHistoryOnly)}，名册={rows.Count(r => r.IsHistoryOnly)}）");
            return Result.Success(rows);
        }
        catch (Exception ex)
        {
            LogException(ex, "GetAge90AdjustRowsAsync");
            return Result.FromException<List<ElderlyAge90Row>>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<int>> EnqueueAge90ReviewsAsync(int year, int month, CancellationToken ct = default)
    {
        var rowsResult = await GetAge90AdjustRowsAsync(year, month, ct);
        if (rowsResult.IsFailure)
            return Result.Failure<int>(rowsResult.ErrorCode!, rowsResult.Message!);
        var rows = rowsResult.Value ?? new List<ElderlyAge90Row>();
        if (rows.Count == 0) return Result.Success(0);

        var nextMonth = new DateTime(year, month, 1).AddMonths(1).ToString("yyyy-MM");
        var created = 0;
        foreach (var row in rows)
        {
            try
            {
                var exists = await _db.ExecuteScalarAsync<long?>(
                    "SELECT id FROM nc_biz_elderly_reviews WHERE id_card = $1 AND status = $2 AND deleted_at IS NULL LIMIT 1",
                    ct, row.IdCard, ElderlyBenefitConstants.ReviewStatusPending);
                if (exists.IsSuccess && exists.Value is > 0) continue;

                var eval = new ElderlyReviewEvaluation
                {
                    IsHistoryOnly = row.IsHistoryOnly,
                    ApplicationId = row.ApplicationId,
                    HistoryId = row.HistoryId,
                    Name = row.Name,
                    IdCard = row.IdCard,
                    Gender = row.Gender,
                    BirthDate = row.BirthDate,
                    Age = row.Age,
                    OldIdentityFlag = row.IdentityFlag,
                    IdentityFlag = row.IdentityFlag,
                    IdentitySource = string.Empty,
                    OldCategory = row.OldCategory,
                    OldMonthlyAmount = row.OldMonthlyAmount,
                    NewCategory = row.NewCategory,
                    NewMonthlyAmount = row.NewMonthlyAmount,
                    AdjustReasonCode = ElderlyBenefitConstants.AdjustReasonAge,
                    IdentityAdd = false,
                    EffectiveMonth = row.EffectiveMonth,
                    IssueStartMonth = nextMonth
                };
                await InsertPendingReviewAsync(eval, ElderlyBenefitConstants.ReviewTriggerAge90, null,
                    $"{year}年{month}月满90周岁自动入队", ct);
                created++;
            }
            catch (Exception ex)
            {
                LogWarn($"满90周岁入队失败（继续）: {DataMasker.MaskIdCard(row.IdCard)}, {ex.Message}");
            }
        }

        if (created > 0)
            Logger.LogBusiness("满90周岁月报自动入队", ("Year", year), ("Month", month), ("Created", created));
        return Result.Success(created);
    }

    /// <inheritdoc />
    public async Task<Result<ElderlyReviewEvaluation>> EvaluateReviewByPendingAsync(long reviewId, CancellationToken ct = default)
    {
        var rowResult = await _db.QuerySingleAsync<ElderlyReview>(
            "SELECT * FROM nc_biz_elderly_reviews WHERE id = $1 AND deleted_at IS NULL", ct, reviewId);
        if (rowResult.IsFailure || rowResult.Value == null)
            return Result.Failure<ElderlyReviewEvaluation>(ErrorCodes.RECORD_NOT_FOUND, "复核记录不存在");

        var row = rowResult.Value;
        // 月报自动入队：采用队列已算好的旧→新（名册旧类别不能用当前年龄重推）
        if (string.Equals(row.TriggerSource, ElderlyBenefitConstants.ReviewTriggerAge90, StringComparison.Ordinal))
        {
            var eval = new ElderlyReviewEvaluation
            {
                IsHistoryOnly = row.SourceHistoryId is > 0 && row.OldApplicationId is null or 0,
                ApplicationId = row.OldApplicationId,
                HistoryId = row.SourceHistoryId,
                Name = row.Name,
                IdCard = row.IdCard,
                Gender = IdCardValidator.ExtractGender(row.IdCard) ?? string.Empty,
                BirthDate = IdCardValidator.ExtractBirthDate(row.IdCard),
                Age = row.AgeAtReview ?? ElderlyBenefitConstants.Threshold90,
                OldIdentityFlag = row.IdentityFlag,
                IdentityFlag = row.IdentityFlag,
                OldCategory = row.OldCategory,
                OldMonthlyAmount = row.OldMonthlyAmount ?? 0m,
                NewCategory = string.IsNullOrEmpty(row.NewCategory) ? ElderlyBenefitConstants.Cat90To99 : row.NewCategory,
                NewMonthlyAmount = row.NewMonthlyAmount ?? 0m,
                AdjustReasonCode = string.IsNullOrEmpty(row.AdjustReasonCode) ? ElderlyBenefitConstants.AdjustReasonAge : row.AdjustReasonCode,
                EffectiveMonth = row.EffectiveMonth,
                IssueStartMonth = row.IssueStartMonth
            };

            // 在享优先：补建/手工建档后队列仍记 IsHistoryOnly，重评须按当前库档显示，
            // 否则补全返回后提示条仍报"未建档"、档案编号不显示。
            // 只修正 ApplicationId/IsHistoryOnly，Old/New 沿用队列值（金额口径不变，不用当前年龄重推）。
            if (eval.IsHistoryOnly)
            {
                var confirmedAppId = await FindConfirmedApplicationIdAsync(row.IdCard, ct);
                if (confirmedAppId is > 0)
                {
                    eval.ApplicationId = confirmedAppId;
                    eval.IsHistoryOnly = false;
                }
            }

            return Result.Success(eval);
        }

        return await EvaluateReviewAsync(row.OldApplicationId, row.IdCard, row.SourceHistoryId, ct);
    }

    /// <summary>家庭住址拼接（区划地址 + 门牌号）</summary>
    private static string CombineAddress(string? familyAddress, string? detailAddress)
    {
        if (string.IsNullOrWhiteSpace(detailAddress)) return familyAddress ?? string.Empty;
        if (string.IsNullOrWhiteSpace(familyAddress)) return detailAddress;
        return familyAddress + detailAddress;
    }

    /// <summary>满90周岁-在享档案行（snake_case 自动映射）</summary>
    private sealed class Age90AppRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IdCard { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public DateTime? BirthDate { get; set; }
        public string Category { get; set; } = string.Empty;
        public string? IdentityFlag { get; set; }
        public decimal IssueAmount { get; set; }
        public string? HukouAddress { get; set; }
        public string? FamilyAddress { get; set; }
        public string? DetailAddress { get; set; }
    }

    /// <summary>满90周岁-名册行（snake_case 自动映射）</summary>
    private sealed class Age90HistRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IdCard { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public DateTime? BirthDate { get; set; }
        public string PersonType { get; set; } = string.Empty;
        public string? Address { get; set; }
        public decimal? SubsidyAmount { get; set; }
    }

    #endregion

    /// <summary>名册复核源行（snake_case 列自动映射）</summary>
    private sealed class ElderlyHistoryReviewRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IdCard { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public DateTime? BirthDate { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string BankAccount { get; set; } = string.Empty;
        public string PersonType { get; set; } = string.Empty;
        public decimal? SubsidyAmount { get; set; }
        public int? DataYear { get; set; }
    }

    /// <summary>复核联动批查：档案 id → 状态</summary>
    private sealed class IdStatusRow
    {
        public long Id { get; set; }
        public string? Status { get; set; }
    }

    /// <summary>复核联动批查：已存在 Pending 的身份证</summary>
    private sealed class PendingCardRow
    {
        public string? IdCard { get; set; }
    }
}
