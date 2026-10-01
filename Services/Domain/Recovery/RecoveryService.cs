using System.Globalization;
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
    public async Task<Result<long>> SaveRecoveryRecordAsync(
        RecoveryRecordDto dto, CancellationToken ct = default)
    {
        try
        {
            LogInfo($"保存追缴记录（搜索录入）: personName={DataMasker.MaskName(dto.PersonName ?? string.Empty)}, sourceType={dto.SourceType}");

            var months = CalculateRecoveryMonths(dto.StartMonth ?? string.Empty, dto.EndMonth ?? string.Empty);
            var amount = CalculateRecoveryAmount(dto.MonthlyAmount, months);

            var sql = @"
                INSERT INTO nc_biz_recovery_records 
                (input_mode, source_type, source_id, person_name, id_card, recovery_reason,
                 monthly_amount, recovery_months, recovery_amount, recovered_amount,
                 start_month, end_month, status, created_by, created_at, updated_at)
                VALUES 
                ('Search', $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $13, $12, NOW(), NOW())
                RETURNING id";

            var result = await _db.ExecuteScalarAsync<long>(sql, ct,
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
                "System", ApplicationStatusCodes.DRAFT); // TODO: 从当前用户获取

            if (result.IsSuccess)
            {
                LogInfo($"追缴记录保存成功，ID: {result.Value}");
            }

            return result;
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
            var amount = CalculateRecoveryAmount(dto.MonthlyAmount, months);

            var sql = @"
                INSERT INTO nc_biz_recovery_records 
                (input_mode, source_type, source_id, person_name, id_card, recovery_reason,
                 monthly_amount, recovery_months, recovery_amount, recovered_amount,
                 start_month, end_month, status, created_by, created_at, updated_at)
                VALUES 
                ('Manual', $1, NULL, $2, $3, $4, $5, $6, $7, $8, $9, $10, $12, $11, NOW(), NOW())
                RETURNING id";

            var result = await _db.ExecuteScalarAsync<long>(sql, ct,
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
                "System", ApplicationStatusCodes.DRAFT); // TODO: 从当前用户获取

            if (result.IsSuccess)
            {
                LogInfo($"手工追缴记录保存成功，ID: {result.Value}");
            }

            return result;
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
            var amount = CalculateRecoveryAmount(dto.MonthlyAmount, months);

            var sql = @"
                UPDATE nc_biz_recovery_records SET
                    source_type = $1, source_id = $2, person_name = $3, id_card = $4,
                    recovery_reason = $5, monthly_amount = $6, recovery_months = $7,
                    recovery_amount = $8, recovered_amount = $9, start_month = $10,
                    end_month = $11, updated_at = NOW()
                WHERE id = $12 AND status = $13
                RETURNING id";

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
                id, ApplicationStatusCodes.DRAFT);

            if (result.IsSuccess && result.Value.HasValue)
            {
                LogInfo($"追缴记录更新成功，ID: {result.Value}");
                return Result.Success(true);
            }

            LogWarn($"追缴记录更新失败或非草稿状态: id={id}");
            return Result.Failure<bool>(ErrorCodes.VALIDATION_FAILED, "仅草稿状态的记录可更新");
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
            var amount = CalculateRecoveryAmount(dto.MonthlyAmount, months);

            var sql = @"
                UPDATE nc_biz_recovery_records SET
                    source_type = $1, person_name = $2, id_card = $3,
                    recovery_reason = $4, monthly_amount = $5, recovery_months = $6,
                    recovery_amount = $7, recovered_amount = $8, start_month = $9,
                    end_month = $10, updated_at = NOW()
                WHERE id = $11 AND status = $12
                RETURNING id";

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
                id, ApplicationStatusCodes.DRAFT);

            if (result.IsSuccess && result.Value.HasValue)
            {
                LogInfo($"手工追缴记录更新成功，ID: {result.Value}");
                return Result.Success(true);
            }

            LogWarn($"手工追缴记录更新失败或非草稿状态: id={id}");
            return Result.Failure<bool>(ErrorCodes.VALIDATION_FAILED, "仅草稿状态的记录可更新");
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

            var sql = @"
                UPDATE nc_biz_recovery_records 
                SET status = $1, updated_at = NOW()
                WHERE id = $2";

            var result = await _db.ExecuteNonQueryAsync(sql, ct, status, id);

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
    public async Task<Result<string>> GenerateRecoveryCodeAsync(DateTime month, CancellationToken ct = default)
    {
        try
        {
            var monthKey = month.ToString("yyyyMM");
            var sql = "SELECT COUNT(*) FROM nc_biz_recovery_records WHERE TO_CHAR(created_at, 'YYYYMM') = $1";
            var result = await _db.ExecuteScalarAsync<long>(sql, ct, monthKey);

            if (result.IsSuccess)
            {
                var code = $"{monthKey}-{result.Value + 1:D3}";
                LogInfo($"生成追缴编码: {code}");
                return Result.Success(code);
            }

            LogWarn($"生成追缴编码失败: {result.Message}");
            return Result.Failure<string>(result.ErrorCode!, result.Message ?? "生成追缴编码失败");
        }
        catch (Exception ex)
        {
            LogException(ex, "生成追缴编码");
            return Result.FromException<string>(ex);
        }
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
