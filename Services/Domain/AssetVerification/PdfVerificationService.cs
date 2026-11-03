using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NewCosmos.Services.Domain.AssetVerification;

public class PdfVerificationService : BaseService, IPdfVerificationService
{
    protected override string ServiceName => "PdfVerificationService";
    private readonly IDatabaseService _db;

    public PdfVerificationService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<Result<bool>> HasLowIncomeIdentityAsync(
        string name, string idCard, CancellationToken ct = default)
    {
        LogInfo("核查低收入身份: Name=" + DataMasker.MaskName(name));

        // 语义：此人（可能是户主，也可能是普通家庭成员）是否具有低收入身份。
        // ① 历史导入台账（低保/边缘/特困 人员+家庭表，覆盖全部成员）；
        // ② 当前库在享档案（nc_biz_applications，Approved/Completed，且分类属于受保护范围：
        //    低保含单人/边缘/特困；刚性支出、其他困难不算）——
        //    原实现只查导入台账，2026-10 起大量人员只在当前库建档（如仲立亮 app95），
        //    会被误判为"无低收入身份"→ 月报核查状态回写成"有报告未建档"（实测错例）。
        // ③ 当前库家庭成员行（非户主、非赡养义务人）同样计入。
        var sql = @"SELECT (
               EXISTS (SELECT 1 FROM nc_biz_rural_subsistence_persons  WHERE id_card = $1)
            OR EXISTS (SELECT 1 FROM nc_biz_urban_subsistence_persons  WHERE id_card = $1)
            OR EXISTS (SELECT 1 FROM nc_biz_low_income_edge_persons    WHERE id_card = $1)
            OR EXISTS (SELECT 1 FROM nc_biz_destitute_persons          WHERE id_card = $1)
            OR EXISTS (SELECT 1 FROM nc_biz_rural_subsistence_families WHERE applicant_id_card = $1)
            OR EXISTS (SELECT 1 FROM nc_biz_urban_subsistence_families WHERE applicant_id_card = $1)
            OR EXISTS (SELECT 1 FROM nc_biz_low_income_edge_families   WHERE applicant_id_card = $1)
            OR EXISTS (SELECT 1 FROM nc_biz_destitute_families         WHERE applicant_id_card = $1)
            OR EXISTS (
                SELECT 1 FROM nc_biz_applications a
                WHERE a.applicant_id_card = $1
                  AND a.deleted_at IS NULL
                  AND a.status = ANY($2::text[])
                  AND a.classification_result = ANY($3::text[])
            )
            OR EXISTS (
                SELECT 1 FROM nc_biz_family_members m
                JOIN nc_biz_applications a ON a.id = m.application_id
                WHERE m.id_card = $1
                  AND m.deleted_at IS NULL
                  AND a.deleted_at IS NULL
                  AND a.status = ANY($2::text[])
                  AND a.classification_result = ANY($3::text[])
                  AND (m.is_applicant = TRUE OR COALESCE(m.member_category, '') <> 'Support')
            )
        )::int";
        try
        {
            var activeStatuses = new[] { ApplicationStatusCodes.APPROVED, ApplicationStatusCodes.COMPLETED };
            var result = await _db.ExecuteScalarAsync(sql, ct, idCard, activeStatuses,
                ClassificationConstants.ProtectedLowIncomeCategoryCodes);
            if (result.IsFailure)
                return Result.Failure<bool>(result.ErrorCode!, result.Message!);
            var has = Convert.ToInt64(result.Value) > 0;
            LogInfo("查询结果: " + has);
            return Result.Success(has);
        }
        catch (Exception ex)
        {
            LogException(ex, "身份查询失败");
            return Result.Failure<bool>(ErrorCodes.UNKNOWN_ERROR, "低收入身份查询失败: " + ex.Message);
        }
    }

    // UpdateCheckStatusAsync（按户更新核查状态）副本已删除（2026-11）：唯一调用方走
    // IAssetVerificationService.UpdateCheckStatusAsync（带终态守卫），本副本零调用方且无守卫。

    public async Task<Result> MarkIncludedByIdCardAsync(
        string idCard, CancellationToken ct = default)
    {
        LogInfo("按身份证升级核查状态为已纳入: IdCard=" + DataMasker.MaskIdCard(idCard));

        // 整户口径：与 UpdateCheckStatusAsync 一致，同户主（head_id_card）的全部成员行一并处理；
        // 仅升级未终态任务（0 已提交 / 1 有报告），已纳入(2)/已拒绝(3)不动
        var sql = "UPDATE nc_biz_asset_checks SET status = $2, updated_at = NOW() WHERE head_id_card = $1 AND status = ANY($3) AND deleted_at IS NULL";

        try
        {
            var result = await _db.ExecuteNonQueryAsync(sql, ct, idCard, AssetCheckStatusConstants.INCLUDED,
                new List<string> { AssetCheckStatusConstants.SUBMITTED, AssetCheckStatusConstants.VERIFIED });
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);

            LogInfo("核查状态联动完成: 户内更新行数=" + result.Value);
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "核查状态联动失败");
            return Result.Failure(ErrorCodes.UNKNOWN_ERROR, "核查状态联动失败: " + ex.Message);
        }
    }

    public async Task<Result> UploadReportAsync(
        long checkId, string batchId, string applicantName, string applicantIdCard,
        string fileName, byte[] fileData, string fileHash, bool isValid, string notes,
        CancellationToken ct = default)
    {
        LogInfo("上传PDF报告: CheckId=" + checkId + ", FileName=" + fileName);

        try
        {
            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var headResult = await _db.ExecuteScalarAsync<string>(
                "SELECT head_id_card FROM nc_biz_asset_checks WHERE id = $1 AND deleted_at IS NULL",
                ct, checkId);
            if (headResult.IsFailure)
                return Result.Failure(headResult.ErrorCode!, headResult.Message!);
            if (string.IsNullOrWhiteSpace(headResult.Value))
                return Result.Failure(ErrorCodes.NOT_FOUND, "核查记录不存在或缺少户主身份证，无法上传报告");

            var headIdCard = headResult.Value!.Trim();

            // 同户旧的当前版本转历史（每户只保留一条 is_current）
            var supersede = await _db.ExecuteNonQueryAsync(
                @"UPDATE nc_biz_asset_check_reports
                  SET is_current = FALSE, superseded_at = NOW()
                  WHERE head_id_card = $1 AND is_current",
                ct, headIdCard);
            if (supersede.IsFailure)
                return Result.Failure(supersede.ErrorCode!, supersede.Message!);

            var versionResult = await _db.ExecuteScalarAsync<int>(
                "SELECT COALESCE(MAX(version), 0) + 1 FROM nc_biz_asset_check_reports WHERE head_id_card = $1",
                ct, headIdCard);
            if (versionResult.IsFailure)
                return Result.Failure(versionResult.ErrorCode!, versionResult.Message!);
            var version = versionResult.Value;

            var sql = @"INSERT INTO nc_biz_asset_check_reports
                            (check_id, batch_id, applicant_name, applicant_id_card, report_file_name,
                             report_data, file_hash, is_valid, report_date, notes, created_by,
                             head_id_card, version, is_current, report_size)
                        VALUES ($1,$2,$3,$4,$5,$6,$7,$8,CURRENT_DATE,$9,$10,$11,$12,TRUE,$13)
                        RETURNING id";
            var result = await _db.ExecuteScalarAsync(sql, ct,
                checkId, batchId, applicantName, applicantIdCard, fileName,
                fileData, fileHash, isValid, notes, App.CurrentUserId,
                headIdCard, version, fileData.Length);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);

            await tx.CommitAsync(ct);
            LogInfo($"PDF上传成功: Id={result.Value}, HeadIdCard={DataMasker.MaskIdCard(headIdCard)}, Version={version}");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "上传失败");
            return Result.Failure(ErrorCodes.UNKNOWN_ERROR, "上传PDF报告失败: " + ex.Message);
        }
    }

    /// <summary>
    /// 同一户内是否已存在该 PDF（按 head_id_card 作用域）。
    /// 不同户出现相同文件仍应各自落库；同户重复文件才跳过，避免"有状态、没报告"。
    /// </summary>
    public async Task<Result<ReportHashMatch?>> FindHouseholdReportByHashAsync(
        long checkId, string fileHash, CancellationToken ct = default)
    {
        var sql = @"SELECT r.id AS report_id, r.is_current, r.version
                    FROM nc_biz_asset_check_reports r
                    JOIN nc_biz_asset_checks c ON c.id = r.check_id
                    WHERE r.file_hash = $2
                      AND c.deleted_at IS NULL
                      AND c.head_id_card = (SELECT head_id_card FROM nc_biz_asset_checks WHERE id = $1)
                    ORDER BY r.is_current DESC, r.version DESC
                    LIMIT 1";
        try
        {
            var result = await _db.QuerySingleAsync<ReportHashMatch>(sql, ct, checkId, fileHash);
            if (result.IsFailure)
                return Result.Failure<ReportHashMatch?>(result.ErrorCode!, result.Message!);
            return Result.Success<ReportHashMatch?>(result.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "同户同 hash 报告查询失败");
            return Result.Failure<ReportHashMatch?>(ErrorCodes.UNKNOWN_ERROR, "同户同 hash 报告查询失败: " + ex.Message);
        }
    }

    /// <summary>
    /// 把指定历史报告恢复为同户当前版本：旧当前版本转历史，不新增报告行（重复上传“一样则替换”）。
    /// </summary>
    public async Task<Result> SetCurrentReportAsync(long reportId, CancellationToken ct = default)
    {
        try
        {
            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var headResult = await _db.QuerySingleAsync<ReportHeadRow>(
                @"SELECT r.id, c.head_id_card
                  FROM nc_biz_asset_check_reports r
                  JOIN nc_biz_asset_checks c ON c.id = r.check_id
                  WHERE r.id = $1", ct, reportId);
            if (headResult.IsFailure)
                return Result.Failure(headResult.ErrorCode!, headResult.Message!);
            if (headResult.Value == null || string.IsNullOrWhiteSpace(headResult.Value.HeadIdCard))
                return Result.Failure(ErrorCodes.NOT_FOUND, "报告不存在或缺少户主身份证");

            var headIdCard = headResult.Value.HeadIdCard!.Trim();

            var clear = await _db.ExecuteNonQueryAsync(
                @"UPDATE nc_biz_asset_check_reports r
                  SET is_current = FALSE, superseded_at = NOW()
                  FROM nc_biz_asset_checks c
                  WHERE c.id = r.check_id
                    AND c.head_id_card = $1
                    AND r.id <> $2
                    AND r.is_current", ct, headIdCard, reportId);
            if (clear.IsFailure)
                return Result.Failure(clear.ErrorCode!, clear.Message!);

            var setCurrent = await _db.ExecuteNonQueryAsync(
                @"UPDATE nc_biz_asset_check_reports
                  SET is_current = TRUE, superseded_at = NULL
                  WHERE id = $1", ct, reportId);
            if (setCurrent.IsFailure)
                return Result.Failure(setCurrent.ErrorCode!, setCurrent.Message!);

            await tx.CommitAsync(ct);
            Logger.LogBusiness("核查报告恢复为当前版本",
                ("ReportId", reportId.ToString()), ("HeadIdCard", DataMasker.MaskIdCard(headIdCard)));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "恢复报告当前版本失败");
            return Result.FromException(ex);
        }
    }

    private sealed class ReportHeadRow
    {
        public long Id { get; set; }
        public string? HeadIdCard { get; set; }
    }

    /// <summary>
    /// 上传报告后的状态更新：只允许 0/1 → 1/2，已纳入(2)/不予认定(3) 终态不被覆盖。
    /// 与 UpdateCheckStatusAsync（人工不予认定等显式操作）区分。
    /// </summary>
    public async Task<Result> UpdateCheckStatusFromUploadAsync(
        long checkId, string status, CancellationToken ct = default)
    {
        if (status is not (AssetCheckStatusConstants.VERIFIED or AssetCheckStatusConstants.INCLUDED))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, $"上传状态只能为 1/2，当前: {status}");

        var sql = @"UPDATE nc_biz_asset_checks
                    SET status = $1, updated_at = NOW()
                    WHERE head_id_card = (SELECT head_id_card FROM nc_biz_asset_checks WHERE id = $2)
                      AND deleted_at IS NULL
                      AND status = ANY($3::text[])";
        var allowedSourceStatuses = new List<string>
        {
            AssetCheckStatusConstants.SUBMITTED, AssetCheckStatusConstants.VERIFIED
        };

        try
        {
            var result = await _db.ExecuteNonQueryAsync(sql, ct, status, checkId, allowedSourceStatuses);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "上传状态更新失败");
            return Result.Failure(ErrorCodes.UNKNOWN_ERROR, "上传状态更新失败: " + ex.Message);
        }
    }

    public async Task<Result<bool>> ReportExistsByCheckIdAsync(
        long checkId, CancellationToken ct = default)
    {
        // 按户读取：核查报告可能挂在同户任一成员行（历史批次按姓名匹配到配偶），
        // 档案输出/月度审核按户主 check_id 查询时必须能命中，否则误显"未上传"。
        var sql = @"SELECT COUNT(*) FROM nc_biz_asset_check_reports
                    WHERE is_current
                      AND check_id IN (
                        SELECT id FROM nc_biz_asset_checks
                        WHERE head_id_card = (SELECT head_id_card FROM nc_biz_asset_checks WHERE id = $1)
                          AND deleted_at IS NULL)";

        try
        {
            var result = await _db.ExecuteScalarAsync(sql, ct, checkId);
            if (result.IsFailure)
                return Result.Failure<bool>(result.ErrorCode!, result.Message!);

            return Result.Success(Convert.ToInt64(result.Value) > 0);
        }
        catch (Exception ex)
        {
            LogException(ex, "检查ID查询失败");
            return Result.Failure<bool>(ErrorCodes.UNKNOWN_ERROR, "检查ID查询失败: " + ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<Result<List<PdfVerificationRecord>>> GetReportHistoryByCheckIdAsync(
        long checkId, CancellationToken ct = default)
    {
        var sql = @"SELECT r.id, r.report_file_name AS file_name, r.applicant_name AS expected_name,
                           r.is_valid, r.created_by AS verified_by, r.created_at AS verified_at,
                           r.notes, r.version, r.is_current, r.report_size
                    FROM nc_biz_asset_check_reports r
                    WHERE r.check_id IN (
                        SELECT id FROM nc_biz_asset_checks
                        WHERE head_id_card = (SELECT head_id_card FROM nc_biz_asset_checks WHERE id = $1)
                          AND deleted_at IS NULL)
                    ORDER BY r.version DESC";
        try
        {
            var result = await _db.QueryAsync<PdfVerificationRecord>(sql, ct, checkId);
            if (result.IsFailure)
                return Result.Failure<List<PdfVerificationRecord>>(result.ErrorCode!, result.Message!);
            return Result.Success(result.Value ?? new List<PdfVerificationRecord>());
        }
        catch (Exception ex)
        {
            LogException(ex, "报告版本历史查询失败");
            return Result.Failure<List<PdfVerificationRecord>>(ErrorCodes.UNKNOWN_ERROR, "报告版本历史查询失败: " + ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<Result<byte[]>> GetReportDataByReportIdAsync(
        long reportId, CancellationToken ct = default)
    {
        var sql = "SELECT report_data FROM nc_biz_asset_check_reports WHERE id = $1";
        try
        {
            var result = await _db.QuerySingleAsync<ReportDataDto>(sql, ct, reportId);
            if (result.IsFailure)
                return Result.Failure<byte[]>(result.ErrorCode!, result.Message!);
            if (result.Value?.ReportData == null)
                return Result.Failure<byte[]>(ErrorCodes.FILE_NOT_FOUND, "该报告版本不存在");
            return Result.Success(result.Value.ReportData);
        }
        catch (Exception ex)
        {
            LogException(ex, "获取报告版本数据失败");
            return Result.Failure<byte[]>(ErrorCodes.UNKNOWN_ERROR, "获取报告版本数据失败: " + ex.Message);
        }
    }

    private static string ComputeFileHash(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexStringLower(hashBytes);
    }

    public async Task<Result<byte[]>> GetReportDataByCheckIdAsync(
        long checkId, CancellationToken ct = default)
    {
        // 按户读取：同户任一成员行的报告都视为本户报告，取最新一份。
        var sql = @"SELECT report_data FROM nc_biz_asset_check_reports
                    WHERE is_current
                      AND check_id IN (
                        SELECT id FROM nc_biz_asset_checks
                        WHERE head_id_card = (SELECT head_id_card FROM nc_biz_asset_checks WHERE id = $1)
                          AND deleted_at IS NULL)
                    ORDER BY version DESC, created_at DESC LIMIT 1";

        try
        {
            var result = await _db.QuerySingleAsync<ReportDataDto>(sql, ct, checkId);
            if (result.IsFailure)
                return Result.Failure<byte[]>(result.ErrorCode!, result.Message!);

            if (result.Value?.ReportData == null)
                return Result.Failure<byte[]>(ErrorCodes.FILE_NOT_FOUND, "该人员暂无已上传的核查报告");

            return Result.Success(result.Value.ReportData);
        }
        catch (Exception ex)
        {
            LogException(ex, "获取报告数据失败");
            return Result.Failure<byte[]>(ErrorCodes.UNKNOWN_ERROR, "获取报告数据失败: " + ex.Message);
        }
    }

    private class ReportDataDto
    {
        public byte[]? ReportData { get; set; }
    }
}
