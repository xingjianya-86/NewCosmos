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

    public Task<Result<List<PdfVerificationResult>>> BatchVerifyAsync(
        IEnumerable<string> pdfFilePaths,
        IEnumerable<string> expectedNames,
        CancellationToken ct = default)
    {
        LogInfo($"执行PDF批量核查");

        var results = new List<PdfVerificationResult>();
        var filePathsList = pdfFilePaths.ToList();
        var namesList = expectedNames.ToList();

        if (filePathsList.Count == 0)
            return Task.FromResult(Result.Success(results));

        foreach (var path in filePathsList)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var fileName = Path.GetFileNameWithoutExtension(path);
            var matched = false;
            var matchedName = string.Empty;

            var parts = fileName.Split('_', StringSplitOptions.RemoveEmptyEntries);
            var nameToken = parts.Length >= 3 ? parts[1] : null;

            if (nameToken != null)
            {
                foreach (var name in namesList)
                {
                    if (!string.IsNullOrWhiteSpace(name) && nameToken.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        matched = true;
                        matchedName = name;
                        break;
                    }
                }
            }

            results.Add(new PdfVerificationResult
            {
                IsValid = matched,
                FileName = Path.GetFileName(path),
                ExpectedName = matchedName,
                NameFoundInFileName = matched,
                FilePath = path,
                VerifiedAt = DateTime.Now,
                Message = matched ? "验证通过: " + matchedName : "验证失败: 未匹配到预期姓名"
            });
        }

        var passCount = results.Count(r => r.IsValid);
        LogInfo("PDF验证: 总数=" + results.Count + ", 通过=" + passCount + ", 失败=" + (results.Count - passCount));

        Logger.LogBusiness("PDF批量核查完成");
        return Task.FromResult(Result.Success(results));
    }

    public async Task<Result<AssetCheckItemResult?>> FindCheckByNameAsync(
        string name, CancellationToken ct = default)
    {
        var trimmedName = name.Trim();
        LogInfo("核查搜索: Name=" + trimmedName);

        var sql = "SELECT id, applicant_name, applicant_id_card, relationship, status, batch_id, head_id_card FROM nc_biz_asset_checks WHERE TRIM(applicant_name) ILIKE TRIM($1) AND status IN ('0', '1', '2', '3') ORDER BY id DESC LIMIT 1";

        try
        {
            var result = await _db.QuerySingleAsync<AssetCheckItemResult>(sql, ct, trimmedName);
            if (result.IsFailure)
                return Result.Failure<AssetCheckItemResult?>(result.ErrorCode!, result.Message!);

            LogInfo("结果: " + (result.Value != null ? "找到" : "未找到"));
            return Result.Success<AssetCheckItemResult?>(result.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "查询失败");
            return Result.Failure<AssetCheckItemResult?>(ErrorCodes.UNKNOWN_ERROR, "核查记录查询失败: " + ex.Message);
        }
    }

    public async Task<Result<bool>> HasLowIncomeIdentityAsync(
        string name, string idCard, CancellationToken ct = default)
    {
        LogInfo("核查低收入身份: Name=" + DataMasker.MaskName(name));

        // 语义：此人（可能是户主，也可能是普通家庭成员）是否具有低收入身份。
        // 原实现只查 4 张家庭表的户主身份证——非户主成员一律漏判（假阴性）。
        // 现同时核对人员表（覆盖全部成员）与家庭表（兜底人员表缺失户主行的情况）。
        // EXISTS 短路 + ::int 单值返回，仍是一次往返。
        var sql = @"SELECT (
               EXISTS (SELECT 1 FROM nc_biz_rural_subsistence_persons  WHERE id_card = $1)
            OR EXISTS (SELECT 1 FROM nc_biz_urban_subsistence_persons  WHERE id_card = $1)
            OR EXISTS (SELECT 1 FROM nc_biz_low_income_edge_persons    WHERE id_card = $1)
            OR EXISTS (SELECT 1 FROM nc_biz_destitute_persons          WHERE id_card = $1)
            OR EXISTS (SELECT 1 FROM nc_biz_rural_subsistence_families WHERE applicant_id_card = $1)
            OR EXISTS (SELECT 1 FROM nc_biz_urban_subsistence_families WHERE applicant_id_card = $1)
            OR EXISTS (SELECT 1 FROM nc_biz_low_income_edge_families   WHERE applicant_id_card = $1)
            OR EXISTS (SELECT 1 FROM nc_biz_destitute_families         WHERE applicant_id_card = $1)
        )::int";
        try
        {
            var result = await _db.ExecuteScalarAsync(sql, ct, idCard);
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

    public async Task<Result> UpdateCheckStatusAsync(
        long checkId, string status, CancellationToken ct = default)
    {
        LogInfo("更新核查状态: CheckId=" + checkId + ", Status=" + status);

        var sql = "UPDATE nc_biz_asset_checks SET status = $1, updated_at = NOW() WHERE head_id_card = (SELECT head_id_card FROM nc_biz_asset_checks WHERE id = $2) AND deleted_at IS NULL";

        try
        {
            var result = await _db.ExecuteNonQueryAsync(sql, ct, status, checkId);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);

            LogInfo("状态更新成功: " + result.Value);
            Logger.LogBusiness("核查状态更新（按户）",
                ("CheckId", checkId),
                ("Status", status),
                ("AffectedRows", result.Value));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "状态更新失败");
            return Result.Failure(ErrorCodes.UNKNOWN_ERROR, "更新核查状态失败: " + ex.Message);
        }
    }

    public async Task<Result> MarkIncludedByIdCardAsync(
        string idCard, CancellationToken ct = default)
    {
        LogInfo("按身份证升级核查状态为已纳入: IdCard=" + DataMasker.MaskIdCard(idCard));

        // 整户口径：与 UpdateCheckStatusAsync 一致，同户主（head_id_card）的全部成员行一并处理；
        // 仅升级未终态任务（0 已提交 / 1 有报告），已纳入(2)/已拒绝(3)不动
        var sql = "UPDATE nc_biz_asset_checks SET status = '2', updated_at = NOW() WHERE head_id_card = $1 AND status IN ('0', '1') AND deleted_at IS NULL";

        try
        {
            var result = await _db.ExecuteNonQueryAsync(sql, ct, idCard);
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

        var sql = "INSERT INTO nc_biz_asset_check_reports (check_id, batch_id, applicant_name, applicant_id_card, report_file_name, report_data, file_hash, is_valid, report_date, notes, created_by) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, CURRENT_DATE, $9, $10) RETURNING id";

        try
        {
            var result = await _db.ExecuteScalarAsync(sql, ct, checkId, batchId, applicantName, applicantIdCard, fileName, fileData, fileHash, isValid, notes, App.CurrentUserId);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);

            LogInfo("PDF上传成功: Id=" + result.Value);
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "上传失败");
            return Result.Failure(ErrorCodes.UNKNOWN_ERROR, "上传PDF报告失败: " + ex.Message);
        }
    }

    public async Task<Result<bool>> ReportExistsByFileHashAsync(
        string fileHash, CancellationToken ct = default)
    {
        var sql = @"SELECT COUNT(*) FROM nc_biz_asset_check_reports WHERE file_hash = $1";

        try
        {
            var result = await _db.ExecuteScalarAsync(sql, ct, fileHash);
            if (result.IsFailure)
                return Result.Failure<bool>(result.ErrorCode!, result.Message!);

            return Result.Success(Convert.ToInt64(result.Value) > 0);
        }
        catch (Exception ex)
        {
            LogException(ex, "哈希检查失败");
            return Result.Failure<bool>(ErrorCodes.UNKNOWN_ERROR, "文件哈希检查失败: " + ex.Message);
        }
    }

    public async Task<Result<bool>> ReportExistsByCheckIdAsync(
        long checkId, CancellationToken ct = default)
    {
        var sql = @"SELECT COUNT(*) FROM nc_biz_asset_check_reports WHERE check_id = $1";

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

    public async Task<Result<List<PdfVerificationRecord>>> GetVerificationHistoryAsync(
        int limit = 50, CancellationToken ct = default)
    {
        LogInfo("获取验证历史记录: Limit=" + limit);

        var sql = "SELECT id, report_file_name AS file_name, applicant_name AS expected_name, is_valid, created_by AS verified_by, created_at AS verified_at, notes FROM nc_biz_asset_check_reports ORDER BY created_at DESC LIMIT $1";

        try
        {
            var result = await _db.QueryAsync<PdfVerificationRecord>(sql, ct, limit);
            if (result.IsFailure)
                return Result.Failure<List<PdfVerificationRecord>>(result.ErrorCode!, result.Message!);

            LogInfo("验证历史获取成功: " + result.Value.Count);
            return Result.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "历史查询失败");
            return Result.Failure<List<PdfVerificationRecord>>(ErrorCodes.UNKNOWN_ERROR, "获取验证历史失败: " + ex.Message);
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
        var sql = "SELECT report_data FROM nc_biz_asset_check_reports WHERE check_id = $1 ORDER BY created_at DESC LIMIT 1";

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
