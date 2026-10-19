using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using ApplicationEntity = NewCosmos.Models.Entities.Application;

namespace NewCosmos.Services.Domain.NearRelative;

/// <summary>
/// 近亲属备案服务实现
/// 业务规则：
///  1. 一份备案 = 1 名工作人员 + N 名救助对象（links 不设上限）
///  2. 无年月维度——全量名册；月报表输出忽略年月，任何月份均输出全部有效备案
///  3. 保存只影响本条备案，绝不动他人数据；事务保护失败全回滚
///  4. 编辑带版本校验（updated_at），防双窗口并发互踩
///  5. 档案联动：工作人员/救助对象身份证自动带出；对象行按申请表一一对应（回填 application_id）
/// </summary>
public class NearRelativeService : BaseService, INearRelativeService
{
    protected override string ServiceName => "NearRelativeService";

    private readonly IDatabaseService _db;
    private readonly IDictCacheService _dictCache;

    public NearRelativeService(IDatabaseService db, IDictCacheService dictCache, ILoggerService logger)
        : base(logger)
    {
        _db = db;
        _dictCache = dictCache;
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
                    return Result.Failure<long>(ErrorCodes.DUPLICATE_ID_CARD, "该身份证已存在有效备案，请载入编辑或修改身份证号");

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

    #region 档案联动（输入身份证自动带出 / 编辑载入按档案刷新）

    /// <summary>
    /// 申请表带出所需显式列（宽表禁用 SELECT *，§8）
    /// </summary>
    private const string ApplicationFillColumns =
        "id, application_no, status, applicant_name, applicant_id_card, gender, hukou_address, " +
        "town, community, address, family_size, confirmed_family_size, classification_result, " +
        "household_monthly_guarantee_amount, total_guarantee_amount, first_approved_at, " +
        "application_reason, application_reason_detail, submit_at, created_at";

    public async Task<Result<NearRelativeEntry?>> GetByStaffIdCardAsync(string idCard, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idCard))
            return Result.Success<NearRelativeEntry?>(null);

        try
        {
            var staffResult = await _db.QuerySingleAsync<NearRelativeStaff>(
                @"SELECT id, staff_name, staff_id_card, staff_phone, work_unit, position, town,
                         created_at, updated_at, created_by, updated_by
                  FROM nc_biz_near_relative_staffs
                  WHERE staff_id_card = $1 AND deleted_at IS NULL
                  ORDER BY id LIMIT 1", ct, idCard.Trim());
            if (staffResult.IsFailure)
                return Result.Failure<NearRelativeEntry?>(staffResult.ErrorCode!, staffResult.Message!);
            if (staffResult.Value == null)
                return Result.Success<NearRelativeEntry?>(null);

            var detail = await GetByIdAsync(staffResult.Value.Id, ct);
            if (detail.IsFailure)
                return Result.Failure<NearRelativeEntry?>(detail.ErrorCode!, detail.Message!);
            return Result.Success<NearRelativeEntry?>(detail.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "按身份证查已有近亲属备案失败");
            return Result.FromException<NearRelativeEntry?>(ex);
        }
    }

    public async Task<Result<NearRelativeStaffLookup?>> GetStaffWorkInfoByIdCardAsync(string idCard, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idCard))
            return Result.Success<NearRelativeStaffLookup?>(null);

        var trimmed = idCard.Trim();
        try
        {
            // ① 系统用户表（启用用户；姓名/电话/职务/机构名/机构所属乡镇）
            const string userSql = @"
                SELECT u.full_name, COALESCE(u.phone, '') AS phone, COALESCE(u.position, '') AS position,
                       COALESCE(o.name, '') AS work_unit, COALESCE(rt.town_name, '') AS town_name
                FROM nc_sys_users u
                LEFT JOIN nc_sys_organizations o ON u.organization_id = o.id
                LEFT JOIN nc_regions_towns rt ON o.town_id = rt.id
                WHERE u.identity_card = $1 AND u.is_active = true
                ORDER BY u.id LIMIT 1";
            var userResult = await _db.QuerySingleAsync<StaffWorkInfoRow>(userSql, ct, trimmed);
            if (userResult.IsFailure)
                return Result.Failure<NearRelativeStaffLookup?>(userResult.ErrorCode!, userResult.Message!);

            // ② 申请档案照料人（在享优先、其次最新一条；乡镇取关联申请的档案乡镇）
            const string caregiverSql = @"
                SELECT c.name AS full_name, COALESCE(c.phone, '') AS phone,
                       COALESCE(c.work_unit, '') AS work_unit, COALESCE(c.position, '') AS position,
                       COALESCE(a.town, '') AS town_name
                FROM nc_biz_caregivers c
                JOIN nc_biz_applications a ON a.id = c.application_id
                WHERE c.id_card = $1 AND c.deleted_at IS NULL AND a.deleted_at IS NULL
                ORDER BY (a.status = ANY($2::text[])) DESC, c.created_at DESC, c.id DESC
                LIMIT 1";
            var caregiverResult = await _db.QuerySingleAsync<StaffWorkInfoRow>(caregiverSql, ct, trimmed,
                new[] { ApplicationStatusCodes.APPROVED, ApplicationStatusCodes.COMPLETED });
            if (caregiverResult.IsFailure)
                return Result.Failure<NearRelativeStaffLookup?>(caregiverResult.ErrorCode!, caregiverResult.Message!);

            // ③ 特困历史导入档案照料人（仅姓名+所属街道，无电话/工作单位）
            const string destituteSql = @"
                SELECT COALESCE(p.caregiver_name, '') AS full_name, COALESCE(f.street, '') AS town_name
                FROM nc_biz_destitute_persons p
                LEFT JOIN nc_biz_destitute_families f ON f.id = p.family_id
                WHERE p.caregiver_id_card = $1
                ORDER BY p.id DESC
                LIMIT 1";
            var destituteResult = await _db.QuerySingleAsync<StaffWorkInfoRow>(destituteSql, ct, trimmed);
            if (destituteResult.IsFailure)
                return Result.Failure<NearRelativeStaffLookup?>(destituteResult.ErrorCode!, destituteResult.Message!);

            var lookup = new NearRelativeStaffLookup
            {
                FoundInUsers = userResult.Value != null,
                FoundInCaregivers = caregiverResult.Value != null,
                FoundInDestituteArchive = destituteResult.Value != null,
            };
            if (!lookup.HasAny)
            {
                LogInfo($"系统用户/档案照料人均未匹配经办人员工作信息（身份证 {DataMasker.MaskIdCard(trimmed)}）");
                return Result.Success<NearRelativeStaffLookup?>(null);
            }

            // 合并：系统用户优先全量覆盖；照料人（申请档案 → 特困历史档案）逐字段补空
            FillFromRow(lookup.Staff, userResult.Value, overwrite: true);
            FillFromRow(lookup.Staff, caregiverResult.Value, overwrite: false);
            FillFromRow(lookup.Staff, destituteResult.Value, overwrite: false);
            lookup.Staff.StaffIdCard = trimmed;

            LogInfo($"经办人员工作信息命中来源：用户={lookup.FoundInUsers}, 申请档案照料人={lookup.FoundInCaregivers}, " +
                    $"特困历史档案={lookup.FoundInDestituteArchive}（身份证 {DataMasker.MaskIdCard(trimmed)}）");
            return Result.Success<NearRelativeStaffLookup?>(lookup);
        }
        catch (Exception ex)
        {
            LogException(ex, "按身份证检索经办人员工作信息失败");
            return Result.FromException<NearRelativeStaffLookup?>(ex);
        }
    }

    /// <summary>
    /// 单来源行合并进目标：overwrite=true 全量覆盖（系统用户优先）；false 仅补空字段（照料人补空）。
    /// </summary>
    private static void FillFromRow(NearRelativeStaff target, StaffWorkInfoRow? row, bool overwrite)
    {
        if (row == null) return;

        if (overwrite)
        {
            target.StaffName = row.FullName ?? string.Empty;
            target.StaffPhone = row.Phone ?? string.Empty;
            target.WorkUnit = row.WorkUnit ?? string.Empty;
            target.Position = row.Position ?? string.Empty;
            target.Town = row.TownName ?? string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(target.StaffName) && !string.IsNullOrWhiteSpace(row.FullName))
            target.StaffName = row.FullName;
        if (string.IsNullOrWhiteSpace(target.StaffPhone) && !string.IsNullOrWhiteSpace(row.Phone))
            target.StaffPhone = row.Phone;
        if (string.IsNullOrWhiteSpace(target.WorkUnit) && !string.IsNullOrWhiteSpace(row.WorkUnit))
            target.WorkUnit = row.WorkUnit;
        if (string.IsNullOrWhiteSpace(target.Position) && !string.IsNullOrWhiteSpace(row.Position))
            target.Position = row.Position;
        if (string.IsNullOrWhiteSpace(target.Town) && !string.IsNullOrWhiteSpace(row.TownName))
            target.Town = row.TownName;
    }

    public async Task<Result<NearRelativeArchiveFill?>> BuildLinkByIdCardAsync(string idCard, long? excludeStaffId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idCard))
            return Result.Success<NearRelativeArchiveFill?>(null);

        try
        {
            // 在享（Approved/Completed）优先，其次最新一条（ORDER BY 布尔 DESC：true 在前）
            var sql = $@"
                SELECT {ApplicationFillColumns}
                FROM nc_biz_applications
                WHERE applicant_id_card = $1 AND deleted_at IS NULL
                ORDER BY (status = ANY($2::text[])) DESC, created_at DESC
                LIMIT 1";
            var result = await _db.QuerySingleAsync<ApplicationEntity>(sql, ct, idCard.Trim(),
                new[] { ApplicationStatusCodes.APPROVED, ApplicationStatusCodes.COMPLETED });
            if (result.IsFailure)
                return Result.Failure<NearRelativeArchiveFill?>(result.ErrorCode!, result.Message!);
            if (result.Value == null)
                return Result.Success<NearRelativeArchiveFill?>(null);

            var fillResult = await BuildFillAsync(result.Value, excludeStaffId, ct);
            if (fillResult.IsFailure)
                return Result.Failure<NearRelativeArchiveFill?>(fillResult.ErrorCode!, fillResult.Message!);

            LogInfo($"按身份证带出对象行: 申请ID={result.Value.Id}（{DataMasker.MaskIdCard(idCard)}）");
            return Result.Success<NearRelativeArchiveFill?>(fillResult.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "按身份证查救助档案失败");
            return Result.FromException<NearRelativeArchiveFill?>(ex);
        }
    }

    public async Task<Result<NearRelativeArchiveFill?>> BuildLinkByApplicationAsync(long applicationId, long? excludeStaffId = null, CancellationToken ct = default)
    {
        if (applicationId <= 0)
            return Result.Success<NearRelativeArchiveFill?>(null);

        try
        {
            var result = await _db.QuerySingleAsync<ApplicationEntity>(
                $"SELECT {ApplicationFillColumns} FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL",
                ct, applicationId);
            if (result.IsFailure)
                return Result.Failure<NearRelativeArchiveFill?>(result.ErrorCode!, result.Message!);
            if (result.Value == null)
                return Result.Success<NearRelativeArchiveFill?>(null);

            var fillResult = await BuildFillAsync(result.Value, excludeStaffId, ct);
            if (fillResult.IsFailure)
                return Result.Failure<NearRelativeArchiveFill?>(fillResult.ErrorCode!, fillResult.Message!);
            return Result.Success<NearRelativeArchiveFill?>(fillResult.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "按申请ID带出对象行失败");
            return Result.FromException<NearRelativeArchiveFill?>(ex);
        }
    }

    public async Task<Result<List<NearRelativeArchiveFill>>> RefreshLinksFromArchiveAsync(
        IReadOnlyCollection<long> applicationIds, long? excludeStaffId = null, CancellationToken ct = default)
    {
        var ids = applicationIds?.Where(id => id > 0).Distinct().ToList() ?? new List<long>();
        if (ids.Count == 0)
            return Result.Success(new List<NearRelativeArchiveFill>());

        try
        {
            var appResult = await _db.QueryAsync<ApplicationEntity>(
                $"SELECT {ApplicationFillColumns} FROM nc_biz_applications WHERE id = ANY($1) AND deleted_at IS NULL",
                ct, ids);
            if (appResult.IsFailure)
                return Result.Failure<List<NearRelativeArchiveFill>>(appResult.ErrorCode!, appResult.Message!);
            if (appResult.Value.Count == 0)
                return Result.Success(new List<NearRelativeArchiveFill>());

            var occupiedResult = await QueryOccupiedStaffAsync(ids, excludeStaffId, ct);
            if (occupiedResult.IsFailure)
                return Result.Failure<List<NearRelativeArchiveFill>>(occupiedResult.ErrorCode!, occupiedResult.Message!);

            var fills = appResult.Value.Select(app => BuildFill(app, occupiedResult.Value)).ToList();
            LogInfo($"近亲属备案按档案刷新: 申请 {ids.Count} 个，命中 {fills.Count} 个");
            return Result.Success(fills);
        }
        catch (Exception ex)
        {
            LogException(ex, "按档案批量刷新对象行失败");
            return Result.FromException<List<NearRelativeArchiveFill>>(ex);
        }
    }

    /// <summary>
    /// 单条映射 + 占用检查（该申请是否已被其他工作人员的备案占用）
    /// </summary>
    private async Task<Result<NearRelativeArchiveFill>> BuildFillAsync(ApplicationEntity app, long? excludeStaffId, CancellationToken ct)
    {
        var occupiedResult = await QueryOccupiedStaffAsync(new[] { app.Id }, excludeStaffId, ct);
        if (occupiedResult.IsFailure)
            return Result.Failure<NearRelativeArchiveFill>(occupiedResult.ErrorCode!, occupiedResult.Message!);
        return Result.Success(BuildFill(app, occupiedResult.Value));
    }

    /// <summary>
    /// 申请表 → 对象行映射（档案来源字段单点实现）。
    /// 与工作人员关系/家庭关系/家庭主要困难/经济调查/乡镇意见/动态记录无档案来源，保持手工录入。
    /// </summary>
    private NearRelativeLink MapApplicationToLink(ApplicationEntity app)
    {
        var birthDate = IdCardValidator.ExtractBirthDate(app.ApplicantIdCard ?? string.Empty);
        var address = $"{app.Town}{app.Community}{app.Address}".Trim();
        var familySize = app.FamilySize > 0
            ? app.FamilySize
            : (app.ConfirmedFamilySize > 0 ? app.ConfirmedFamilySize : (int?)null);

        var reasonDisplay = string.IsNullOrWhiteSpace(app.ApplicationReason)
            ? string.Empty
            : _dictCache.GetValue(DictionaryTypeCodes.ApplicationReasons, app.ApplicationReason);
        var applyReason = !string.IsNullOrWhiteSpace(app.ApplicationReasonDetail)
            ? app.ApplicationReasonDetail
            : reasonDisplay;

        var applyTime = app.SubmitAt > DateTime.MinValue ? app.SubmitAt : app.CreatedAt;

        return new NearRelativeLink
        {
            ApplicationId = app.Id,
            Name = app.ApplicantName ?? string.Empty,
            IdCard = app.ApplicantIdCard ?? string.Empty,
            Gender = app.Gender ?? string.Empty,
            BirthDate = birthDate is { } b ? $"{b.Year}年{b.Month}月" : string.Empty,
            HukouAddress = app.HukouAddress ?? string.Empty,
            ResidenceAddress = address,
            FamilyAddress = address,
            FamilySize = familySize,
            HelpType = ClassificationConstants.ConvertFromCode(app.ClassificationResult ?? string.Empty),
            MonthAmount = app.HouseholdMonthlyGuaranteeAmount,
            ReportAmount = app.TotalGuaranteeAmount,
            StartTime = app.FirstApprovedAt is { } fa ? $"{fa.Year}年{fa.Month}月" : string.Empty,
            ApplyReason = applyReason,
            ApplyTime = applyTime > DateTime.MinValue
                ? $"{applyTime.Year}年{applyTime.Month}月{applyTime.Day}日"
                : string.Empty,
        };
    }

    private NearRelativeArchiveFill BuildFill(ApplicationEntity app, IReadOnlyDictionary<long, string> occupiedByApp)
    {
        occupiedByApp.TryGetValue(app.Id, out var occupiedBy);
        var occupiedName = occupiedBy ?? string.Empty;
        var statusCode = app.Status ?? string.Empty;
        return new NearRelativeArchiveFill
        {
            ApplicationId = app.Id,
            ApplicationNo = app.ApplicationNo ?? string.Empty,
            Status = statusCode,
            StatusDisplay = ApplicationStatusExtensions.FromCode(statusCode).GetDescription(),
            ApplicationCreatedAt = app.CreatedAt,
            Link = MapApplicationToLink(app),
            OccupiedByOtherStaff = !string.IsNullOrEmpty(occupiedName),
            OccupiedByStaffName = occupiedName,
        };
    }

    /// <summary>查这些申请是否已被其他工作人员的备案占用（application_id → 占用方工作人员姓名，取最早一条）</summary>
    private async Task<Result<Dictionary<long, string>>> QueryOccupiedStaffAsync(
        IReadOnlyCollection<long> appIds, long? excludeStaffId, CancellationToken ct)
    {
        var sql = @"SELECT l.application_id, s.staff_name
                    FROM nc_biz_near_relative_links l
                    JOIN nc_biz_near_relative_staffs s ON s.id = l.staff_id
                    WHERE l.application_id = ANY($1) AND l.deleted_at IS NULL
                      AND ($2 = 0 OR l.staff_id <> $2)
                    ORDER BY l.application_id, l.id";
        var result = await _db.QueryAsync<OccupiedStaffRow>(sql, ct, appIds.ToList(), excludeStaffId ?? 0);
        if (result.IsFailure)
            return Result.Failure<Dictionary<long, string>>(result.ErrorCode!, result.Message!);

        var dict = new Dictionary<long, string>();
        foreach (var row in result.Value)
        {
            if (!dict.ContainsKey(row.ApplicationId))
                dict[row.ApplicationId] = row.StaffName ?? string.Empty;
        }
        return Result.Success(dict);
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

        // 多行 VALUES 一次写入（原逐行 INSERT 是 N+1）；23 个数据列/行，时间列用 NOW() 字面量
        const int ParamsPerRow = 23;
        var (valuesClause, args) = MultiRowValuesBuilder.Build(
            links.Count,
            ParamsPerRow,
            r =>
            {
                var l = links[r];
                return new object?[]
                {
                    staffId, (object?)l.ApplicationId,
                    l.Relation ?? "", l.Name ?? "", l.IdCard ?? "", l.Gender ?? "", l.BirthDate ?? "",
                    l.FamilyAddress ?? "", l.HukouAddress ?? "", l.ResidenceAddress ?? "",
                    (object?)l.FamilySize, l.HelpType ?? "",
                    (object?)l.MonthAmount, (object?)l.ReportAmount, l.StartTime ?? "", l.FamilyRelation ?? "",
                    l.ApplyReason ?? "", l.ApplyTime ?? "",
                    l.FamilyDifficulty ?? "", l.EconomyInvestigate ?? "", l.TownOpinion ?? "", l.DynamicRecord ?? "",
                    r
                };
            },
            start => "(" + string.Join(",", Enumerable.Range(start, ParamsPerRow).Select(n => "$" + n)) + ",NOW(),NOW())");

        var sql = $@"
            INSERT INTO nc_biz_near_relative_links
            (staff_id, application_id, relation, name, id_card, gender, birth_date,
             family_address, hukou_address, residence_address, family_size, help_type,
             month_amount, report_amount, start_time, family_relation, apply_reason, apply_time,
             family_difficulty, economy_investigate, town_opinion, dynamic_record, sort_order,
             created_at, updated_at)
            VALUES {valuesClause}";

        var result = await _db.ExecuteNonQueryAsync(sql, ct, args);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
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

    /// <summary>系统用户表带出工作信息用行</summary>
    private class StaffWorkInfoRow
    {
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string WorkUnit { get; set; } = string.Empty;
        public string TownName { get; set; } = string.Empty;
    }

    /// <summary>申请占用检查用行（application_id → 占用方工作人员姓名）</summary>
    private class OccupiedStaffRow
    {
        public long ApplicationId { get; set; }
        public string StaffName { get; set; } = string.Empty;
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
