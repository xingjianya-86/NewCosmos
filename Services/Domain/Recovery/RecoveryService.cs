using System.Globalization;
using System.Text;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.Recovery;

/// <summary>
/// 后补追缴服务实现
/// </summary>
public class RecoveryService : BaseService, IRecoveryService
{
    private readonly IDatabaseService _db;

    protected override string ServiceName => "RecoveryService";

    public RecoveryService(ILoggerService logger, IDatabaseService db) : base(logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc/>
    public async Task<Result<List<StoppedPersonDto>>> SearchStoppedPersonsAsync(
        string keyword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Result.Failure<List<StoppedPersonDto>>(ErrorCodes.VALIDATION_FAILED, "请输入搜索关键词");
        }

        try
        {
            LogInfo($"搜索停止人员: keywordLength={(keyword ?? string.Empty).Length}");

            var sql = @"
                -- 农村低保停止人员
                SELECT 
                    'RuralSubsistence' as source_type,
                    a.id as source_id,
                    a.applicant_name as person_name,
                    a.applicant_id_card as id_card,
                    COALESCE(a.household_monthly_guarantee_amount, 0) as monthly_amount,
                    '农村低保' as source_display
                FROM nc_biz_applications a
                WHERE a.status = $2
                  AND a.source_table = 'nc_biz_rural_subsistence_families'
                  AND (a.applicant_name LIKE '%' || $1 || '%' OR a.applicant_id_card = $1)

                UNION ALL

                -- 城市低保停止人员
                SELECT 
                    'UrbanSubsistence' as source_type,
                    a.id as source_id,
                    a.applicant_name as person_name,
                    a.applicant_id_card as id_card,
                    COALESCE(a.household_monthly_guarantee_amount, 0) as monthly_amount,
                    '城市低保' as source_display
                FROM nc_biz_applications a
                WHERE a.status = $2
                  AND a.source_table = 'nc_biz_urban_subsistence_families'
                  AND (a.applicant_name LIKE '%' || $1 || '%' OR a.applicant_id_card = $1)

                UNION ALL

                -- 刚性支出停止人员
                SELECT 
                    'RigidExpenditure' as source_type,
                    a.id as source_id,
                    a.applicant_name as person_name,
                    a.applicant_id_card as id_card,
                    COALESCE(a.rigid_expenditure, 0) as monthly_amount,
                    '刚性支出' as source_display
                FROM nc_biz_applications a
                WHERE a.status = $2
                  AND a.source_table = 'nc_biz_rigid_expenditure_families'
                  AND (a.applicant_name LIKE '%' || $1 || '%' OR a.applicant_id_card = $1)

                UNION ALL

                -- 临时救助停止人员
                SELECT 
                    'TempRelief' as source_type,
                    t.id as source_id,
                    t.applicant_name as person_name,
                    t.applicant_id_card as id_card,
                    COALESCE(t.confirm_amount, 0) as monthly_amount,
                    '临时救助' as source_display
                FROM nc_biz_temp_relief_applications t
                WHERE t.status = $2
                  AND (t.applicant_name LIKE '%' || $1 || '%' OR t.applicant_id_card = $1)

                UNION ALL

                -- 高龄停止人员
                SELECT 
                    'Elderly' as source_type,
                    e.id as source_id,
                    e.name as person_name,
                    e.id_card as id_card,
                    COALESCE(e.issue_amount, 0) as monthly_amount,
                    '高龄' as source_display
                FROM nc_biz_elderly_applications e
                WHERE e.status = $2
                  AND (e.name LIKE '%' || $1 || '%' OR e.id_card = $1)

                ORDER BY person_name
                LIMIT 50";

            var result = await _db.QueryAsync<StoppedPersonDto>(sql, ct, keyword, ApplicationStatusCodes.STOPPED);
            
            if (result.IsSuccess)
            {
                LogInfo($"搜索到 {result.Value.Count} 条停止人员记录");
            }

            return result;
        }
        catch (Exception ex)
        {
            LogException(ex, "搜索停止人员");
            return Result.FromException<List<StoppedPersonDto>>(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<decimal>> GetDefaultMonthlyAmountAsync(
        string sourceType, long sourceId, CancellationToken ct = default)
    {
        try
        {
            LogInfo($"获取默认月保障额: sourceType={sourceType}, sourceId={sourceId}");

            string sql;
            object[] parameters;

            switch (sourceType)
            {
                case RecoveryConstants.SOURCE_TYPE_RURAL_SUBSISTENCE:
                case RecoveryConstants.SOURCE_TYPE_URBAN_SUBSISTENCE:
                    sql = "SELECT COALESCE(household_monthly_guarantee_amount, 0) FROM nc_biz_applications WHERE id = $1";
                    parameters = new object[] { sourceId };
                    break;

                case RecoveryConstants.SOURCE_TYPE_RIGID_EXPENDITURE:
                    sql = "SELECT COALESCE(rigid_expenditure, 0) FROM nc_biz_applications WHERE id = $1";
                    parameters = new object[] { sourceId };
                    break;

                case RecoveryConstants.SOURCE_TYPE_TEMP_RELIEF:
                    sql = "SELECT COALESCE(confirm_amount, 0) FROM nc_biz_temp_relief_applications WHERE id = $1";
                    parameters = new object[] { sourceId };
                    break;

                case RecoveryConstants.SOURCE_TYPE_ELDERLY:
                    sql = "SELECT COALESCE(issue_amount, 0) FROM nc_biz_elderly_applications WHERE id = $1";
                    parameters = new object[] { sourceId };
                    break;

                default:
                    return Result.Failure<decimal>(ErrorCodes.VALIDATION_FAILED, $"不支持的来源类型: {sourceType}");
            }

            var result = await _db.ExecuteScalarAsync<decimal>(sql, ct, parameters);
            
            if (result.IsSuccess)
            {
                LogInfo($"获取到月保障额: {result.Value}");
            }

            return result;
        }
        catch (Exception ex)
        {
            LogException(ex, "获取默认月保障额");
            return Result.FromException<decimal>(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<RecoveryArchiveLookupDto?>> LookupArchiveByIdCardAsync(
        string idCard, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idCard))
        {
            return Result.Failure<RecoveryArchiveLookupDto?>(ErrorCodes.VALIDATION_FAILED, "请输入身份证号");
        }

        try
        {
            // 取数口径镜像 SearchStoppedPersonsAsync（同样的表/列/月保障额列），
            // 差异仅两点：① 不限定 status（改为 is_stopped 优先排序，非停保状态也能带出）；
            // ② 多出 stop_reason（追缴原因来源）。$1=身份证 $2=停保状态码，全部位置参数。
            // 同一来源多条档案（停发流程会新建一条金额为 0 的停发记录行）时，
            // 优先取带保障标准的那条——0 金额行是停发动作的产物，不携带可依据的标准。
            var sql = @"
                SELECT source_type, source_id, person_name, id_card,
                       monthly_amount, stop_reason, is_stopped
                FROM (
                    SELECT 1 AS prio, 'RuralSubsistence' AS source_type, a.id AS source_id,
                           a.applicant_name AS person_name, a.applicant_id_card AS id_card,
                           COALESCE(a.household_monthly_guarantee_amount, 0) AS monthly_amount,
                           a.stop_reason AS stop_reason, (a.status = $2) AS is_stopped, a.created_at
                    FROM nc_biz_applications a
                    WHERE a.applicant_id_card = $1
                      AND a.source_table = 'nc_biz_rural_subsistence_families'

                    UNION ALL

                    SELECT 2, 'UrbanSubsistence', a.id,
                           a.applicant_name, a.applicant_id_card,
                           COALESCE(a.household_monthly_guarantee_amount, 0),
                           a.stop_reason, (a.status = $2), a.created_at
                    FROM nc_biz_applications a
                    WHERE a.applicant_id_card = $1
                      AND a.source_table = 'nc_biz_urban_subsistence_families'

                    UNION ALL

                    SELECT 3, 'RigidExpenditure', a.id,
                           a.applicant_name, a.applicant_id_card,
                           COALESCE(a.rigid_expenditure, 0),
                           a.stop_reason, (a.status = $2), a.created_at
                    FROM nc_biz_applications a
                    WHERE a.applicant_id_card = $1
                      AND a.source_table = 'nc_biz_rigid_expenditure_families'

                    UNION ALL

                    SELECT 4, 'TempRelief', t.id,
                           t.applicant_name, t.applicant_id_card,
                           COALESCE(t.confirm_amount, 0),
                           t.stop_reason, (t.status = $2), t.created_at
                    FROM nc_biz_temp_relief_applications t
                    WHERE t.applicant_id_card = $1

                    UNION ALL

                    SELECT 5, 'Elderly', e.id,
                           e.name, e.id_card,
                           COALESCE(e.issue_amount, 0),
                           e.stop_reason, (e.status = $2), e.created_at
                    FROM nc_biz_elderly_applications e
                    WHERE e.id_card = $1
                ) hits
                ORDER BY is_stopped DESC,
                         prio ASC,
                         (monthly_amount > 0) DESC,
                         created_at DESC NULLS LAST,
                         source_id DESC
                LIMIT 1";

            var result = await _db.QuerySingleAsync<RecoveryArchiveLookupDto>(sql, ct,
                idCard.Trim(), ApplicationStatusCodes.STOPPED);

            // 无行 = 未命中（Success(null)），由界面提示转手工填写；失败才上抛
            if (result.IsSuccess && result.Value != null)
            {
                LogInfo($"手工追缴查档命中: sourceType={result.Value.SourceType}, " +
                        $"stopped={result.Value.IsStopped}, " +
                        $"monthly={result.Value.MonthlyAmount}");
            }

            return result.IsSuccess
                ? Result.Success<RecoveryArchiveLookupDto?>(result.Value)
                : Result.Failure<RecoveryArchiveLookupDto?>(result.ErrorCode!, result.Message!);
        }
        catch (Exception ex)
        {
            LogException(ex, "手工追缴按身份证查档");
            return Result.FromException<RecoveryArchiveLookupDto?>(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<long>> SaveRecoveryRecordAsync(
        RecoveryRecordDto dto, CancellationToken ct = default)
    {
        try
        {
            LogInfo($"保存追缴记录（搜索录入）: personName={DataMasker.MaskName(dto.PersonName ?? string.Empty)}, sourceType={dto.SourceType}");

            var months = CalculateRecoveryMonths(dto.StartMonth ?? string.Empty, dto.EndMonth ?? string.Empty);
            var amount = ResolveRecoveryAmount(dto.RecoveryAmount, dto.MonthlyAmount, months);
            var amountSource = NormalizeAmountSource(dto.AmountSource);

            var sql = @"
                INSERT INTO nc_biz_recovery_records 
                (input_mode, source_type, source_id, person_name, id_card, recovery_reason,
                 monthly_amount, recovery_months, recovery_amount, recovered_amount,
                 amount_source, amount_note, auto_amount,
                 start_month, end_month, status, created_by, created_at, updated_at)
                VALUES 
                ('Search', $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $16, $15, NOW(), NOW())
                RETURNING id";

            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var result = await _db.ExecuteScalarAsync<long?>(sql, ct,
                dto.SourceType,
                dto.SourceId,
                dto.PersonName,
                dto.IdCard,
                dto.RecoveryReason,
                dto.MonthlyAmount,
                months,
                amount,
                dto.RecoveredAmount,
                amountSource,
                dto.AmountNote,
                dto.AutoAmount,
                dto.StartMonth,
                dto.EndMonth,
                string.IsNullOrWhiteSpace(dto.CreatedBy) ? "System" : dto.CreatedBy,
                ApplicationStatusCodes.DRAFT);

            if (!result.IsSuccess || !result.Value.HasValue)
            {
                LogWarn($"追缴记录保存失败: {result.Message}");
                return Result.Failure<long>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message ?? "保存追缴记录失败");
            }

            // 编号随保存一次分配（同一环境事务内；失败不阻断保存，打印时 GetOrAssignRecoveryCodeAsync 兜底补分配）
            var codeResult = await GetOrAssignRecoveryCodeAsync(result.Value.Value, ct);
            if (codeResult.IsFailure)
                LogWarn($"追缴编号分配失败（打印时将重试）: {codeResult.Message}");

            var segments = await ReplaceSegmentsAsync(result.Value.Value, dto.Segments, ct);
            if (!segments.IsSuccess)
            {
                return Result.Failure<long>(segments.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, segments.Message ?? "保存追缴分段明细失败");
            }

            await tx.CommitAsync(ct);

            LogInfo($"追缴记录保存成功，ID: {result.Value.Value}, 金额来源={amountSource}");

            return Result.Success(result.Value.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "保存追缴记录");
            return Result.FromException<long>(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<long>> SaveManualRecoveryRecordAsync(
        ManualRecoveryRecordDto dto, CancellationToken ct = default)
    {
        try
        {
            LogInfo($"保存追缴记录（手工录入）: personName={DataMasker.MaskName(dto.PersonName ?? string.Empty)}, sourceType={dto.SourceType}");

            var months = CalculateRecoveryMonths(dto.StartMonth ?? string.Empty, dto.EndMonth ?? string.Empty);
            var amount = ResolveRecoveryAmount(dto.RecoveryAmount, dto.MonthlyAmount, months);
            var amountSource = NormalizeAmountSource(dto.AmountSource);

            var sql = @"
                INSERT INTO nc_biz_recovery_records 
                (input_mode, source_type, source_id, person_name, id_card, recovery_reason,
                 monthly_amount, recovery_months, recovery_amount, recovered_amount,
                 amount_source, amount_note, auto_amount,
                 start_month, end_month, status, created_by, created_at, updated_at)
                VALUES 
                ('Manual', $1, NULL, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $15, $14, NOW(), NOW())
                RETURNING id";

            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var result = await _db.ExecuteScalarAsync<long?>(sql, ct,
                dto.SourceType,
                dto.PersonName,
                dto.IdCard,
                dto.RecoveryReason,
                dto.MonthlyAmount,
                months,
                amount,
                dto.RecoveredAmount,
                amountSource,
                dto.AmountNote,
                dto.AutoAmount,
                dto.StartMonth,
                dto.EndMonth,
                string.IsNullOrWhiteSpace(dto.CreatedBy) ? "System" : dto.CreatedBy,
                ApplicationStatusCodes.DRAFT);

            if (!result.IsSuccess || !result.Value.HasValue)
            {
                LogWarn($"手工追缴记录保存失败: {result.Message}");
                return Result.Failure<long>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message ?? "保存手工追缴记录失败");
            }

            // 编号随保存一次分配（同一环境事务内；失败不阻断保存，打印时 GetOrAssignRecoveryCodeAsync 兜底补分配）
            var codeResult = await GetOrAssignRecoveryCodeAsync(result.Value.Value, ct);
            if (codeResult.IsFailure)
                LogWarn($"追缴编号分配失败（打印时将重试）: {codeResult.Message}");

            var segments = await ReplaceSegmentsAsync(result.Value.Value, dto.Segments, ct);
            if (!segments.IsSuccess)
            {
                return Result.Failure<long>(segments.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, segments.Message ?? "保存追缴分段明细失败");
            }

            await tx.CommitAsync(ct);

            LogInfo($"手工追缴记录保存成功，ID: {result.Value.Value}, 金额来源={amountSource}");

            return Result.Success(result.Value.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "保存手工追缴记录");
            return Result.FromException<long>(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<RecoveryRecord>> GetRecoveryRecordByIdAsync(
        long id, CancellationToken ct = default)
    {
        try
        {
            LogInfo($"获取追缴记录详情: id={id}");

            var sql = @"
                SELECT id, input_mode, source_type, source_id, person_name, id_card,
                       recovery_reason, monthly_amount, recovery_months, recovery_amount,
                       recovered_amount, start_month, end_month, status, created_by,
                       amount_source, amount_note, auto_amount,
                       created_at, updated_at
                FROM nc_biz_recovery_records
                WHERE id = $1";

            var result = await _db.QuerySingleAsync<RecoveryRecord>(sql, ct, id);

            if (result.IsSuccess && result.Value != null)
            {
                LogInfo($"获取到追缴记录: {DataMasker.MaskName(result.Value.PersonName ?? string.Empty)}");
            }

            return result;
        }
        catch (Exception ex)
        {
            LogException(ex, "获取追缴记录详情");
            return Result.FromException<RecoveryRecord>(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<List<RecoveryRecord>>> GetRecoveryRecordsAsync(
        string? keyword = null, string? status = null, int limit = 100, CancellationToken ct = default)
    {
        try
        {
            var kw = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
            var st = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
            LogInfo($"查询追缴记录列表: keywordLength={(kw ?? string.Empty).Length}, status={st}, limit={limit}");

            var sql = @"
                SELECT id, input_mode, source_type, source_id, person_name, id_card,
                       recovery_reason, monthly_amount, recovery_months, recovery_amount,
                       recovered_amount, start_month, end_month, status, created_by,
                       amount_source, amount_note, auto_amount,
                       created_at, updated_at
                FROM nc_biz_recovery_records
                WHERE ($1::text IS NULL OR person_name LIKE '%' || $1 || '%' OR id_card = $1)
                  AND ($2::text IS NULL OR status = $2)
                ORDER BY created_at DESC
                LIMIT $3";

            var result = await _db.QueryAsync<RecoveryRecord>(sql, ct,
                (object?)kw ?? DBNull.Value,
                (object?)st ?? DBNull.Value,
                limit);

            if (result.IsSuccess)
            {
                LogInfo($"查询到 {result.Value.Count} 条追缴记录");
            }

            return result;
        }
        catch (Exception ex)
        {
            LogException(ex, "查询追缴记录列表");
            return Result.FromException<List<RecoveryRecord>>(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteRecoveryRecordAsync(
        long id, CancellationToken ct = default)
    {
        try
        {
            LogInfo($"删除追缴记录: id={id}");

            // 仅草稿状态可删除（删除条件内联状态校验，杜绝误删已确认/已打印记录）
            var sql = @"
                DELETE FROM nc_biz_recovery_records
                WHERE id = $1 AND status = $2
                RETURNING id";

            var result = await _db.ExecuteScalarAsync<long?>(sql, ct, id, ApplicationStatusCodes.DRAFT);

            if (result.IsSuccess && result.Value.HasValue)
            {
                LogInfo($"追缴记录删除成功，ID: {result.Value}");
                return Result.Success(true);
            }

            LogWarn($"追缴记录删除失败或非草稿状态: id={id}");
            return Result.Failure<bool>(ErrorCodes.VALIDATION_FAILED, "仅草稿状态的记录可删除");
        }
        catch (Exception ex)
        {
            LogException(ex, "删除追缴记录");
            return Result.FromException<bool>(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> UpdateRecoveryRecordAsync(
        long id, RecoveryRecordDto dto, CancellationToken ct = default)
    {
        try
        {
            LogInfo($"更新追缴记录（搜索录入）: id={id}, personName={DataMasker.MaskName(dto.PersonName ?? string.Empty)}");

            var months = CalculateRecoveryMonths(dto.StartMonth ?? string.Empty, dto.EndMonth ?? string.Empty);
            var amount = ResolveRecoveryAmount(dto.RecoveryAmount, dto.MonthlyAmount, months);
            var amountSource = NormalizeAmountSource(dto.AmountSource);

            // recovery_amount 仅在算得出金额（>0）时覆盖：月保障额缺失（来源档案无月标准）时保留库中已核定总额，
            // 避免编辑保存把已存金额静默清零。
            var sql = @"
                UPDATE nc_biz_recovery_records SET
                    source_type = $1, source_id = $2, person_name = $3, id_card = $4,
                    recovery_reason = $5, monthly_amount = $6, recovery_months = $7,
                    recovery_amount = CASE WHEN $8::numeric > 0 THEN $8::numeric ELSE recovery_amount END,
                    recovered_amount = $9, start_month = $10,
                    end_month = $11, amount_source = $12, amount_note = $13, auto_amount = $14,
                    updated_at = NOW()
                WHERE id = $15 AND status = $16
                RETURNING id";

            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var result = await _db.ExecuteScalarAsync<long?>(sql, ct,
                dto.SourceType,
                dto.SourceId,
                dto.PersonName,
                dto.IdCard,
                dto.RecoveryReason,
                dto.MonthlyAmount,
                months,
                amount,
                dto.RecoveredAmount,
                dto.StartMonth,
                dto.EndMonth,
                amountSource,
                dto.AmountNote,
                dto.AutoAmount,
                id, ApplicationStatusCodes.DRAFT);

            if (!result.IsSuccess || !result.Value.HasValue)
            {
                LogWarn($"追缴记录更新失败或非草稿状态: id={id}, {result.Message}");
                return Result.Failure<bool>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, "仅草稿状态的记录可更新");
            }

            var segments = await ReplaceSegmentsAsync(result.Value.Value, dto.Segments, ct);
            if (!segments.IsSuccess)
            {
                return Result.Failure<bool>(segments.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, segments.Message ?? "保存追缴分段明细失败");
            }

            await tx.CommitAsync(ct);

            LogInfo($"追缴记录更新成功，ID: {result.Value.Value}, 金额来源={amountSource}");
            return Result.Success(true);
        }
        catch (Exception ex)
        {
            LogException(ex, "更新追缴记录");
            return Result.FromException<bool>(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> UpdateManualRecoveryRecordAsync(
        long id, ManualRecoveryRecordDto dto, CancellationToken ct = default)
    {
        try
        {
            LogInfo($"更新追缴记录（手工录入）: id={id}, personName={dto.PersonName}");

            var months = CalculateRecoveryMonths(dto.StartMonth ?? string.Empty, dto.EndMonth ?? string.Empty);
            var amount = ResolveRecoveryAmount(dto.RecoveryAmount, dto.MonthlyAmount, months);
            var amountSource = NormalizeAmountSource(dto.AmountSource);

            // 同 UpdateRecoveryRecordAsync：算不出金额时保留库中已核定总额，不静默清零
            var sql = @"
                UPDATE nc_biz_recovery_records SET
                    source_type = $1, person_name = $2, id_card = $3,
                    recovery_reason = $4, monthly_amount = $5, recovery_months = $6,
                    recovery_amount = CASE WHEN $7::numeric > 0 THEN $7::numeric ELSE recovery_amount END,
                    recovered_amount = $8, start_month = $9,
                    end_month = $10, amount_source = $11, amount_note = $12, auto_amount = $13,
                    updated_at = NOW()
                WHERE id = $14 AND status = $15
                RETURNING id";

            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var result = await _db.ExecuteScalarAsync<long?>(sql, ct,
                dto.SourceType,
                dto.PersonName,
                dto.IdCard,
                dto.RecoveryReason,
                dto.MonthlyAmount,
                months,
                amount,
                dto.RecoveredAmount,
                dto.StartMonth,
                dto.EndMonth,
                amountSource,
                dto.AmountNote,
                dto.AutoAmount,
                id, ApplicationStatusCodes.DRAFT);

            if (!result.IsSuccess || !result.Value.HasValue)
            {
                LogWarn($"手工追缴记录更新失败或非草稿状态: id={id}, {result.Message}");
                return Result.Failure<bool>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, "仅草稿状态的记录可更新");
            }

            var segments = await ReplaceSegmentsAsync(result.Value.Value, dto.Segments, ct);
            if (!segments.IsSuccess)
            {
                return Result.Failure<bool>(segments.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, segments.Message ?? "保存追缴分段明细失败");
            }

            await tx.CommitAsync(ct);

            LogInfo($"手工追缴记录更新成功，ID: {result.Value.Value}, 金额来源={amountSource}");
            return Result.Success(true);
        }
        catch (Exception ex)
        {
            LogException(ex, "更新手工追缴记录");
            return Result.FromException<bool>(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> UpdateRecoveryRecordStatusAsync(
        long id, string status, CancellationToken ct = default)
    {
        try
        {
            LogInfo($"更新追缴记录状态: id={id}, status={status}");

            // 状态守卫（与本服务其余方法"SQL 内联状态校验"风格一致）：
            // 追缴记录状态机实际只有 Draft→Printed（归档写回），禁止 Printed 回退 Draft 或跳转其他状态。
            if (status != RecoveryConstants.STATUS_PRINTED)
                return Result.Failure<bool>(ErrorCodes.VALIDATION_FAILED,
                    $"追缴记录不支持的目标状态: {status}（仅支持 {RecoveryConstants.STATUS_PRINTED}）");

            var sql = @"
                UPDATE nc_biz_recovery_records 
                SET status = $1, updated_at = NOW()
                WHERE id = $2 AND status = $3";

            var result = await _db.ExecuteNonQueryAsync(sql, ct, status, id, RecoveryConstants.STATUS_DRAFT);

            if (result.IsSuccess && result.Value == 0)
                return Result.Success(true); // 已是 Printed 的幂等重入（重复点归档不报错）

            if (result.IsSuccess)
            {
                LogInfo($"追缴记录状态更新成功");
            }

            return result.IsSuccess ? Result.Success(true) : Result.Failure<bool>(result.ErrorCode, result.Message);
        }
        catch (Exception ex)
        {
            LogException(ex, "更新追缴记录状态");
            return Result.FromException<bool>(ex);
        }
    }

    /// <inheritdoc/>
    public int CalculateRecoveryMonths(string startMonth, string endMonth)
    {
        if (string.IsNullOrEmpty(startMonth) || string.IsNullOrEmpty(endMonth))
            return 0;

        if (!DateTime.TryParseExact(startMonth, "yyyy-MM", null, DateTimeStyles.None, out var start) ||
            !DateTime.TryParseExact(endMonth, "yyyy-MM", null, DateTimeStyles.None, out var end))
            return 0;

        if (end < start)
            return 0;

        return ((end.Year - start.Year) * 12 + end.Month - start.Month) + 1;
    }

    /// <inheritdoc/>
    public decimal CalculateRecoveryAmount(decimal monthlyAmount, int months)
    {
        return monthlyAmount * months;
    }

    /// <inheritdoc/>
    public async Task<Result<List<RecoverySegment>>> GetRecoverySegmentsAsync(
        long recoveryId, CancellationToken ct = default)
    {
        try
        {
            var sql = @"
                SELECT id, recovery_id, segment_no, monthly_amount,
                       segment_start_month, segment_end_month, months, segment_amount, source
                FROM nc_biz_recovery_segments
                WHERE recovery_id = $1
                ORDER BY segment_no";

            return await _db.QueryAsync<RecoverySegment>(sql, ct, recoveryId);
        }
        catch (Exception ex)
        {
            LogException(ex, "查询追缴分段明细");
            return Result.FromException<List<RecoverySegment>>(ex);
        }
    }

    /// <summary>
    /// 金额落库口径（**单点**）：调用方给定的应追缴总额优先；
    /// 未给（&lt;=0）时退化为「月标准 × 月数」（兼容旧调用方）；仍算不出则返回 0，
    /// 由 UPDATE 的 SQL 守卫保留库中已核定总额（不静默清零）。
    /// </summary>
    private static decimal ResolveRecoveryAmount(decimal requestedAmount, decimal monthlyAmount, int months)
    {
        if (requestedAmount > 0)
            return requestedAmount;

        var fallback = monthlyAmount * months;
        return fallback > 0 ? fallback : 0m;
    }

    /// <summary>
    /// 归一金额来源（非法值一律按 Flat，避免脏值入库）。
    /// </summary>
    private static string NormalizeAmountSource(string? amountSource) => amountSource switch
    {
        RecoveryConstants.AMOUNT_SOURCE_SEGMENT => RecoveryConstants.AMOUNT_SOURCE_SEGMENT,
        RecoveryConstants.AMOUNT_SOURCE_MANUAL => RecoveryConstants.AMOUNT_SOURCE_MANUAL,
        _ => RecoveryConstants.AMOUNT_SOURCE_FLAT
    };

    /// <summary>
    /// 覆盖写分段明细（同事务内先删后写；多行 VALUES 一次批写，避免 N+1）。
    /// </summary>
    private async Task<Result<bool>> ReplaceSegmentsAsync(
        long recoveryId, IReadOnlyList<RecoverySegment>? segments, CancellationToken ct)
    {
        var deleted = await _db.ExecuteNonQueryAsync(
            "DELETE FROM nc_biz_recovery_segments WHERE recovery_id = $1", ct, recoveryId);

        if (!deleted.IsSuccess)
        {
            LogWarn($"清理追缴分段明细失败: recoveryId={recoveryId}, {deleted.Message}");
            return Result.Failure<bool>(deleted.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, deleted.Message ?? "清理追缴分段明细失败");
        }

        if (segments == null || segments.Count == 0)
            return Result.Success(true);

        var sql = new StringBuilder(@"
            INSERT INTO nc_biz_recovery_segments
            (recovery_id, segment_no, monthly_amount, segment_start_month, segment_end_month,
             months, segment_amount, source, created_at)
            VALUES ");

        var parameters = new List<object>();
        var p = 1;

        for (var i = 0; i < segments.Count; i++)
        {
            if (i > 0)
                sql.Append(',');

            sql.Append($"(${p},${p + 1},${p + 2},${p + 3},${p + 4},${p + 5},${p + 6},${p + 7},NOW())");

            var segment = segments[i];
            parameters.AddRange(new object[]
            {
                recoveryId,
                i + 1,
                segment.MonthlyAmount,
                segment.SegmentStartMonth ?? string.Empty,
                segment.SegmentEndMonth ?? string.Empty,
                segment.Months,
                segment.SegmentAmount,
                segment.Source ?? string.Empty
            });

            p += 8;
        }

        var inserted = await _db.ExecuteNonQueryAsync(sql.ToString(), ct, parameters.ToArray());

        if (!inserted.IsSuccess)
        {
            LogWarn($"写入追缴分段明细失败: recoveryId={recoveryId}, {inserted.Message}");
            return Result.Failure<bool>(inserted.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, inserted.Message ?? "写入追缴分段明细失败");
        }

        return Result.Success(true);
    }

    /// <inheritdoc/>
    public async Task<Result<string>> GetOrAssignRecoveryCodeAsync(long recordId, CancellationToken ct = default)
    {
        try
        {
            // ① 已有编号直接返回——补打同号的关键：编号随记录固定，不再按打印时刻现算
            //    （旧实现每次打印 COUNT+1：跨月补打变号、并发撞号、TO_CHAR 包列索引失效）
            var existing = await _db.QuerySingleAsync<string>(
                "SELECT COALESCE(recovery_code, '') FROM nc_biz_recovery_records WHERE id = $1", ct, recordId);
            if (existing.IsFailure)
            {
                // recovery_code 列不存在（docs/migrations/20261101_recovery_code.sql 未执行）
                // → 降级为按记录创建月现算（不落库），功能不断但并发/补打保障降级
                if (existing.Message?.Contains("recovery_code", StringComparison.OrdinalIgnoreCase) == true)
                    return await FallbackGenerateByCreatedMonthAsync(recordId, ct);
                return Result.Failure<string>(existing.ErrorCode!, existing.Message!);
            }
            if (!string.IsNullOrEmpty(existing.Value))
                return Result.Success(existing.Value);

            // ② 首次分配：单事务内 advisory 锁串行化同月编号 + 条件回写
            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var monthResult = await _db.QuerySingleAsync<string>(
                "SELECT TO_CHAR(created_at, 'YYYYMM') FROM nc_biz_recovery_records WHERE id = $1 FOR UPDATE",
                ct, recordId);
            if (monthResult.IsFailure || string.IsNullOrEmpty(monthResult.Value))
                return Result.Failure<string>(monthResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    monthResult.Message ?? "读取追缴记录创建月失败");
            var monthKey = monthResult.Value;

            // 同月编号生成串行化（事务级 advisory 锁随外层事务提交/回滚自动释放）
            await _db.ExecuteNonQueryAsync(
                "SELECT pg_advisory_xact_lock(hashtext($1))", ct, $"recovery_code:{monthKey}");

            // 计数按【已分配编号】而非记录行数：串行化下后到者能数到先到者刚写入的编号
            var countResult = await _db.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM nc_biz_recovery_records WHERE recovery_code LIKE $1 || '-%'", ct, monthKey);
            if (countResult.IsFailure)
                return Result.Failure<string>(countResult.ErrorCode!, countResult.Message!);

            var code = $"{monthKey}-{countResult.Value + 1:D3}";

            // 条件回写防并发双分配：0 行 = 其他会话已抢先写入 → 回读其值
            var upd = await _db.ExecuteNonQueryAsync(
                "UPDATE nc_biz_recovery_records SET recovery_code = $1, updated_at = NOW() " +
                "WHERE id = $2 AND recovery_code IS NULL", ct, code, recordId);
            if (upd.IsFailure)
                return Result.Failure<string>(upd.ErrorCode!, upd.Message!);

            if (upd.Value == 0)
            {
                var again = await _db.QuerySingleAsync<string>(
                    "SELECT recovery_code FROM nc_biz_recovery_records WHERE id = $1", ct, recordId);
                if (again.IsSuccess && !string.IsNullOrEmpty(again.Value))
                    return Result.Success(again.Value);
                return Result.Failure<string>(ErrorCodes.DB_QUERY_ERROR, "追缴编号分配冲突且回读失败，请重试");
            }

            await tx.CommitAsync(ct);
            LogInfo($"分配追缴编号: {code}");
            return Result.Success(code);
        }
        catch (Exception ex)
        {
            LogException(ex, "分配追缴编号");
            return Result.FromException<string>(ex);
        }
    }

    /// <summary>
    /// 降级：recovery_code 列缺失（迁移未执行）时按记录创建月现算编号（不落库）。
    /// 月份与计数都在 SQL 侧计算（date_trunc/TO_CHAR 同库时区口径），保证跨月补打取创建月而非打印月。
    /// </summary>
    private async Task<Result<string>> FallbackGenerateByCreatedMonthAsync(long recordId, CancellationToken ct)
    {
        const string sql = @"
            SELECT TO_CHAR(r1.created_at, 'YYYYMM') AS month_key,
                   (SELECT COUNT(*) FROM nc_biz_recovery_records r2
                     WHERE r2.created_at >= date_trunc('month', r1.created_at)
                       AND r2.created_at <  date_trunc('month', r1.created_at) + interval '1 month') AS cnt
            FROM nc_biz_recovery_records r1
            WHERE r1.id = $1";
        var row = await _db.QuerySingleAsync<CodeFallbackRow>(sql, ct, recordId);
        if (row.IsFailure)
            return Result.Failure<string>(row.ErrorCode!, row.Message!);
        if (row.Value == null)
            return Result.Failure<string>(ErrorCodes.RECORD_NOT_FOUND, "追缴记录不存在");

        var code = $"{row.Value.MonthKey}-{row.Value.Cnt + 1:D3}";
        LogWarn($"recovery_code 列缺失（迁移未执行），降级现算编号: {code}（请执行 docs/migrations/20261101_recovery_code.sql）");
        return Result.Success(code);
    }

    private sealed class CodeFallbackRow
    {
        public string MonthKey { get; set; } = string.Empty;
        public long Cnt { get; set; }
    }

    /// <inheritdoc/>
    public async Task<Result<StoppedPersonStats>> GetStoppedPersonStatsAsync(CancellationToken ct = default)
    {
        try
        {
            LogInfo("获取停止人员统计信息");

            var sql = @"
                SELECT 
                    COUNT(*) as total_count,
                    COUNT(CASE WHEN source_type = 'RuralSubsistence' THEN 1 END) as rural_subsistence_count,
                    COUNT(CASE WHEN source_type = 'UrbanSubsistence' THEN 1 END) as urban_subsistence_count,
                    COUNT(CASE WHEN source_type = 'RigidExpenditure' THEN 1 END) as rigid_expenditure_count,
                    COUNT(CASE WHEN source_type = 'TempRelief' THEN 1 END) as temp_relief_count,
                    COUNT(CASE WHEN source_type = 'Elderly' THEN 1 END) as elderly_count
                FROM (
                    -- 农村低保停止人员
                    SELECT 'RuralSubsistence' as source_type
                    FROM nc_biz_applications a
                    WHERE a.status = $1
                      AND a.source_table = 'nc_biz_rural_subsistence_families'

                    UNION ALL

                    -- 城市低保停止人员
                    SELECT 'UrbanSubsistence' as source_type
                    FROM nc_biz_applications a
                    WHERE a.status = $1
                      AND a.source_table = 'nc_biz_urban_subsistence_families'

                    UNION ALL

                    -- 刚性支出停止人员
                    SELECT 'RigidExpenditure' as source_type
                    FROM nc_biz_applications a
                    WHERE a.status = $1
                      AND a.source_table = 'nc_biz_rigid_expenditure_families'

                    UNION ALL

                    -- 临时救助停止人员
                    SELECT 'TempRelief' as source_type
                    FROM nc_biz_temp_relief_applications t
                    WHERE t.status = $1

                    UNION ALL

                    -- 高龄停止人员
                    SELECT 'Elderly' as source_type
                    FROM nc_biz_elderly_applications e
                    WHERE e.status = $1
                ) all_stopped";

            var result = await _db.QuerySingleAsync<StoppedPersonStats>(sql, ct, ApplicationStatusCodes.STOPPED);

            if (result.IsSuccess && result.Value != null)
            {
                LogInfo($"停止人员统计: 总数={result.Value.TotalCount}, 农村低保={result.Value.RuralSubsistenceCount}, 城市低保={result.Value.UrbanSubsistenceCount}, 刚性支出={result.Value.RigidExpenditureCount}");
            }

            return result;
        }
        catch (Exception ex)
        {
            LogException(ex, "获取停止人员统计信息");
            return Result.FromException<StoppedPersonStats>(ex);
        }
    }
}
