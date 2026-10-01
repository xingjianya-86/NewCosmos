using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.NearRelative;

/// <summary>
/// 近亲属备案服务实现
/// 业务规则：
///  1. 一份备案 = 1 名工作人员 + N 名救助对象（links 不设上限）
///  2. 无年月维度——全量名册；月报表输出忽略年月，任何月份均输出全部有效备案
///  3. 保存只影响本条备案，绝不动他人数据；事务保护失败全回滚
///  4. 编辑带版本校验（updated_at），防双窗口并发互踩
/// </summary>
public class NearRelativeService : BaseService, INearRelativeService
{
    protected override string ServiceName => "NearRelativeService";

    private readonly IDatabaseService _db;

    public NearRelativeService(IDatabaseService db, ILoggerService logger)
        : base(logger)
    {
        _db = db;
    }

    #region 查询

    public async Task<Result<List<NearRelativeBrief>>> GetBriefsAsync(string? town = null, CancellationToken ct = default)
    {
        try
        {
            const string sql = @"
                SELECT s.id, s.staff_name, s.staff_id_card, s.work_unit,
                       (SELECT COUNT(*) FROM nc_biz_near_relative_links l
                        WHERE l.staff_id = s.id AND l.deleted_at IS NULL) AS link_count
                FROM nc_biz_near_relative_staffs s
                WHERE s.deleted_at IS NULL
                ORDER BY s.created_at DESC";
            var parameters = new List<object>();
            var querySql = sql;
            if (!string.IsNullOrWhiteSpace(town) && town != "全部")
            {
                querySql = sql.Replace("WHERE s.deleted_at IS NULL", "WHERE s.deleted_at IS NULL AND s.town = $1");
                parameters.Add(town);
            }

            var result = await _db.QueryAsync<NearRelativeBriefRow>(querySql, ct, parameters.ToArray());
            if (result.IsFailure)
                return Result.Failure<List<NearRelativeBrief>>(result.ErrorCode!, result.Message!);

            return Result.Success(result.Value.Select(r => new NearRelativeBrief
            {
                Id = r.Id,
                StaffName = r.StaffName,
                WorkUnit = r.WorkUnit,
                LinkCount = r.LinkCount
            }).ToList());
        }
        catch (Exception ex)
        {
            LogException(ex, "查询近亲属备案列表失败");
            return Result.FromException<List<NearRelativeBrief>>(ex);
        }
    }

    public async Task<Result<NearRelativeEntry>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        LogInfo($"按ID查询近亲属备案: id={id}");
        try
        {
            var staffResult = await _db.QuerySingleAsync<NearRelativeStaff>(
                "SELECT * FROM nc_biz_near_relative_staffs WHERE id = $1 AND deleted_at IS NULL", ct, id);
            if (staffResult.IsFailure || staffResult.Value == null)
                return Result.Failure<NearRelativeEntry>(ErrorCodes.NOT_FOUND, "备案不存在或已删除");

            var linksResult = await _db.QueryAsync<NearRelativeLink>(
                @"SELECT * FROM nc_biz_near_relative_links
                  WHERE staff_id = $1 AND deleted_at IS NULL
                  ORDER BY sort_order, id", ct, id);
            if (linksResult.IsFailure)
                return Result.Failure<NearRelativeEntry>(linksResult.ErrorCode!, linksResult.Message!);

            return Result.Success(new NearRelativeEntry
            {
                Staff = staffResult.Value,
                Links = linksResult.Value
            });
        }
        catch (Exception ex)
        {
            LogException(ex, "查询近亲属备案详情失败");
            return Result.FromException<NearRelativeEntry>(ex);
        }
    }

    #endregion

    #region 写操作

    public async Task<Result<long>> SaveAsync(NearRelativeEntry entry, CancellationToken ct = default)
    {
        if (entry == null || entry.Staff == null)
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "备案数据不能为空");
        if (string.IsNullOrWhiteSpace(entry.Staff.StaffName))
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "请填写工作人员姓名");
        if (string.IsNullOrWhiteSpace(entry.Staff.StaffIdCard))
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "请填写工作人员身份证号");

        LogInfo($"保存近亲属备案: id={entry.Staff.Id}, 姓名={DataMasker.MaskName(entry.Staff.StaffName)}, " +
                $"身份证={DataMasker.MaskIdCard(entry.Staff.StaffIdCard)}, 对象数={entry.Links?.Count ?? 0}");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);
        try
        {
            long staffId;
            if (entry.Staff.Id == 0)
            {
                var existsResult = await CheckIdCardExistsAsync(entry.Staff.StaffIdCard, null, ct);
                if (existsResult.IsFailure)
                    return Result.Failure<long>(existsResult.ErrorCode!, existsResult.Message!);
                if (existsResult.Value)
                    return Result.Failure<long>(ErrorCodes.DUPLICATE_ID_CARD, "该身份证已存在有效备案，请选择已有备案进行编辑");

                staffId = await InsertStaffAsync(entry.Staff, ct);
            }
            else
            {
                var versionResult = await CheckVersionAsync(entry.Staff.Id, entry.Staff.UpdatedAt, ct);
                if (versionResult.IsFailure)
                    return Result.Failure<long>(versionResult.ErrorCode!, versionResult.Message!);

                staffId = entry.Staff.Id;
                var updateResult = await UpdateStaffAsync(entry.Staff, ct);
                if (updateResult.IsFailure)
                    return Result.Failure<long>(updateResult.ErrorCode!, updateResult.Message!);
            }

            var checkResult = await CheckApplicationAccessAsync(entry.Links, entry.Staff.Id, ct);
            if (checkResult.IsFailure)
                return Result.Failure<long>(checkResult.ErrorCode!, checkResult.Message!);

            var rebuildResult = await RebuildLinksAsync(staffId, entry.Links, ct);
            if (rebuildResult.IsFailure)
                return Result.Failure<long>(rebuildResult.ErrorCode!, rebuildResult.Message!);

            await tx.CommitAsync(ct);

            Logger.LogBusiness("保存近亲属备案",
                ("StaffId", staffId),
                ("StaffName", DataMasker.MaskName(entry.Staff.StaffName)),
                ("LinkCount", entry.Links?.Count ?? 0));
            return Result.Success(staffId);
        }
        catch (Exception ex)
        {
            LogException(ex, "保存近亲属备案失败");
            return Result.FromException<long>(ex);
        }
    }

    public async Task<Result> DeleteAsync(long id, CancellationToken ct = default)
    {
        LogInfo($"删除近亲属备案: id={id}");
        try
        {
            await using var tx = await _db.BeginTransactionScopeAsync(ct);
            var staffResult = await _db.ExecuteNonQueryAsync(
                "UPDATE nc_biz_near_relative_staffs SET deleted_at = NOW(), updated_at = NOW() WHERE id = $1 AND deleted_at IS NULL", ct, id);
            if (staffResult.IsFailure)
                return Result.Failure(staffResult.ErrorCode!, staffResult.Message!);
            if (staffResult.Value == 0)
                return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "备案不存在或已删除");

            var linksResult = await _db.ExecuteNonQueryAsync(
                "UPDATE nc_biz_near_relative_links SET deleted_at = NOW(), updated_at = NOW() WHERE staff_id = $1 AND deleted_at IS NULL", ct, id);
            if (linksResult.IsFailure)
                return Result.Failure(linksResult.ErrorCode!, linksResult.Message!);

            await tx.CommitAsync(ct);
            Logger.LogBusiness("删除近亲属备案", ("StaffId", id));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "删除近亲属备案失败");
            return Result.FromException(ex);
        }
    }

    #endregion

    #region 打印/档案取数

    public async Task<Result<List<NearRelativeEntry>>> GetAllForPrintAsync(string? town = null, CancellationToken ct = default)
    {
        try
        {
            string staffSql;
            object[] staffParams;
            if (string.IsNullOrWhiteSpace(town) || town == "全部")
            {
                staffSql = "SELECT * FROM nc_biz_near_relative_staffs WHERE deleted_at IS NULL ORDER BY town, staff_name, id LIMIT 5000";
                staffParams = Array.Empty<object>();
            }
            else
            {
                staffSql = "SELECT * FROM nc_biz_near_relative_staffs WHERE deleted_at IS NULL AND town = $1 ORDER BY town, staff_name, id LIMIT 5000";
                staffParams = new object[] { town };
            }

            var staffResult = await _db.QueryAsync<NearRelativeStaff>(staffSql, ct, staffParams);
            if (staffResult.IsFailure)
                return Result.Failure<List<NearRelativeEntry>>(staffResult.ErrorCode!, staffResult.Message!);

            var staffs = staffResult.Value;
            if (staffs.Count == 0)
                return Result.Success(new List<NearRelativeEntry>());

            var staffIds = staffs.Select(s => s.Id).ToList();
            var linksResult = await _db.QueryAsync<NearRelativeLink>(
                @"SELECT * FROM nc_biz_near_relative_links
                  WHERE staff_id = ANY($1) AND deleted_at IS NULL
                  ORDER BY staff_id, sort_order, id", ct, staffIds);
            if (linksResult.IsFailure)
                return Result.Failure<List<NearRelativeEntry>>(linksResult.ErrorCode!, linksResult.Message!);

            var linksByStaff = linksResult.Value.GroupBy(l => l.StaffId).ToDictionary(g => g.Key, g => g.ToList());
            var entries = staffs.Select(s => new NearRelativeEntry
            {
                Staff = s,
                Links = linksByStaff.TryGetValue(s.Id, out var links) ? links : new List<NearRelativeLink>()
            }).ToList();

            LogInfo($"近亲属备案全量取数: {entries.Count} 条备案（忽略年月）");
            return Result.Success(entries);
        }
        catch (Exception ex)
        {
            LogException(ex, "近亲属备案全量取数失败");
            return Result.FromException<List<NearRelativeEntry>>(ex);
        }
    }

    public async Task<Result<List<NearRelativePair>>> GetPairsByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        try
        {
            var linksResult = await _db.QueryAsync<NearRelativeLink>(
                @"SELECT * FROM nc_biz_near_relative_links
                  WHERE application_id = $1 AND deleted_at IS NULL
                  ORDER BY staff_id, sort_order, id", ct, applicationId);
            if (linksResult.IsFailure)
                return Result.Failure<List<NearRelativePair>>(linksResult.ErrorCode!, linksResult.Message!);

            var links = linksResult.Value;
            if (links.Count == 0)
                return Result.Success(new List<NearRelativePair>());

            var staffIds = links.Select(l => l.StaffId).Distinct().ToList();
            var staffResult = await _db.QueryAsync<NearRelativeStaff>(
                "SELECT * FROM nc_biz_near_relative_staffs WHERE id = ANY($1) AND deleted_at IS NULL", ct, staffIds);
            if (staffResult.IsFailure)
                return Result.Failure<List<NearRelativePair>>(staffResult.ErrorCode!, staffResult.Message!);

            var staffById = staffResult.Value.ToDictionary(s => s.Id, s => s);
            var pairs = links
                .Where(l => staffById.ContainsKey(l.StaffId))
                .Select(l => new NearRelativePair { Staff = staffById[l.StaffId], Link = l })
                .ToList();

            LogInfo($"按申请查近亲属备案: applicationId={applicationId}, 关联对={pairs.Count}");
            return Result.Success(pairs);
        }
        catch (Exception ex)
        {
            LogException(ex, "按申请查近亲属备案失败");
            return Result.FromException<List<NearRelativePair>>(ex);
        }
    }

    #endregion

    #region 私有辅助

    private async Task<Result<bool>> CheckIdCardExistsAsync(string idCard, long? excludeId, CancellationToken ct)
    {
        object[] parameters;
        string sql;
        if (excludeId.HasValue)
        {
            sql = "SELECT COUNT(*) FROM nc_biz_near_relative_staffs WHERE staff_id_card = $1 AND id != $2 AND deleted_at IS NULL";
            parameters = new object[] { idCard, excludeId.Value };
        }
        else
        {
            sql = "SELECT COUNT(*) FROM nc_biz_near_relative_staffs WHERE staff_id_card = $1 AND deleted_at IS NULL";
            parameters = new object[] { idCard };
        }
        var result = await _db.ExecuteScalarAsync<long>(sql, ct, parameters);
        if (result.IsFailure)
            return Result.Failure<bool>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value > 0);
    }

    /// <summary>
    /// 版本校验：读取库中当前 updated_at，与页面加载时比对，不一致则拒绝（防并发覆盖他人修改）
    /// </summary>
    private async Task<Result> CheckVersionAsync(long id, DateTime? loadedUpdatedAt, CancellationToken ct)
    {
        var current = await _db.QuerySingleAsync<UpdatedAtRow>(
            "SELECT updated_at FROM nc_biz_near_relative_staffs WHERE id = $1 AND deleted_at IS NULL", ct, id);
        if (current.IsFailure || current.Value == null)
            return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "备案不存在或已删除");

        if (!loadedUpdatedAt.HasValue)
            return Result.Failure(ErrorCodes.CONCURRENCY_CONFLICT, "备案已被他人更新，请刷新后再保存");

        var tolerance = TimeSpan.FromSeconds(2);
        if ((current.Value.UpdatedAt - loadedUpdatedAt.Value).Duration() > tolerance)
            return Result.Failure(ErrorCodes.CONCURRENCY_CONFLICT, "备案已被他人更新，请刷新后再保存");

        return Result.Success();
    }

    /// <summary>
    /// 对象归属校验：一个救助对象至多被一名工作人员备案（application_id 唯一）。
    /// 组内自查重复关联 + 跨工作人员查占（排除本备案旧行），避免撞唯一索引时暴露 23505 原始报错。
    /// </summary>
    private async Task<Result> CheckApplicationAccessAsync(List<NearRelativeLink>? links, long excludeStaffId, CancellationToken ct)
    {
        if (links == null || links.Count == 0)
            return Result.Success();

        var appliedIds = links
            .Where(l => l.ApplicationId.HasValue && l.ApplicationId > 0)
            .Select(l => l.ApplicationId!.Value)
            .ToList();
        if (appliedIds.Count == 0)
            return Result.Success();

        var distinctIds = appliedIds.Distinct().ToList();
        if (distinctIds.Count < appliedIds.Count)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "同一备案内重复关联同一救助对象");

        var occupiedResult = await _db.QueryAsync<ApplicationIdRow>(
            @"SELECT application_id FROM nc_biz_near_relative_links
              WHERE application_id = ANY($1) AND staff_id != $2 AND deleted_at IS NULL",
            ct, distinctIds, excludeStaffId);
        if (occupiedResult.IsFailure)
            return Result.Failure(occupiedResult.ErrorCode!, occupiedResult.Message!);
        if (occupiedResult.Value.Count > 0)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "该救助对象已被其他工作人员备案，请先解除原关联");

        return Result.Success();
    }

    private async Task<long> InsertStaffAsync(NearRelativeStaff s, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO nc_biz_near_relative_staffs
            (staff_name, staff_id_card, staff_phone, work_unit, position, town, created_by, created_at, updated_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,NOW(),NOW())
            RETURNING id";
        var result = await _db.ExecuteScalarAsync<long>(sql, ct,
            s.StaffName, s.StaffIdCard, s.StaffPhone ?? "", s.WorkUnit ?? "", s.Position ?? "",
            s.Town ?? "", s.CreatedBy ?? "System");
        if (result.IsFailure)
            throw new InvalidOperationException(result.Message ?? "插入近亲属备案失败");
        return result.Value;
    }

    private async Task<Result> UpdateStaffAsync(NearRelativeStaff s, CancellationToken ct)
    {
        const string sql = @"
            UPDATE nc_biz_near_relative_staffs SET
             staff_name = $1, staff_id_card = $2, staff_phone = $3, work_unit = $4,
             position = $5, town = $6, updated_by = $7, updated_at = NOW()
            WHERE id = $8 AND deleted_at IS NULL";
        var result = await _db.ExecuteNonQueryAsync(sql, ct,
            s.StaffName, s.StaffIdCard, s.StaffPhone ?? "", s.WorkUnit ?? "",
            s.Position ?? "", s.Town ?? "", s.UpdatedBy ?? "System", s.Id);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        if (result.Value == 0)
            return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "备案不存在或已删除");
        return Result.Success();
    }

    /// <summary>
    /// 重建对象明细（物理删旧 + 按序插入；细表由主表级联管理，无历史追溯需求）
    /// </summary>
    private async Task<Result> RebuildLinksAsync(long staffId, List<NearRelativeLink>? links, CancellationToken ct)
    {
        var deleteResult = await _db.ExecuteNonQueryAsync(
            "DELETE FROM nc_biz_near_relative_links WHERE staff_id = $1", ct, staffId);
        if (deleteResult.IsFailure)
            return Result.Failure(deleteResult.ErrorCode!, deleteResult.Message!);

        if (links == null || links.Count == 0)
            return Result.Success();

        const string sql = @"
            INSERT INTO nc_biz_near_relative_links
            (staff_id, application_id, relation, name, id_card, gender, birth_date,
             family_address, hukou_address, residence_address, family_size, help_type,
             month_amount, report_amount, start_time, family_relation, apply_reason, apply_time,
             family_difficulty, economy_investigate, town_opinion, dynamic_record, sort_order,
             created_at, updated_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,$21,$22,$23,NOW(),NOW())";

        for (var i = 0; i < links.Count; i++)
        {
            var l = links[i];
            var result = await _db.ExecuteNonQueryAsync(sql, ct,
                staffId, (object?)l.ApplicationId,
                l.Relation ?? "", l.Name ?? "", l.IdCard ?? "", l.Gender ?? "", l.BirthDate ?? "",
                l.FamilyAddress ?? "", l.HukouAddress ?? "", l.ResidenceAddress ?? "",
                (object?)l.FamilySize, l.HelpType ?? "",
                (object?)l.MonthAmount, (object?)l.ReportAmount, l.StartTime ?? "", l.FamilyRelation ?? "",
                l.ApplyReason ?? "", l.ApplyTime ?? "",
                l.FamilyDifficulty ?? "", l.EconomyInvestigate ?? "", l.TownOpinion ?? "", l.DynamicRecord ?? "",
                i);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);
        }
        return Result.Success();
    }

    #endregion

    private class UpdatedAtRow
    {
        public DateTime UpdatedAt { get; set; }
    }

    private class ApplicationIdRow
    {
        public long ApplicationId { get; set; }
    }

    private class NearRelativeBriefRow
    {
        public long Id { get; set; }
        public string StaffName { get; set; } = string.Empty;
        public string StaffIdCard { get; set; } = string.Empty;
        public string WorkUnit { get; set; } = string.Empty;
        public int LinkCount { get; set; }
    }
}
