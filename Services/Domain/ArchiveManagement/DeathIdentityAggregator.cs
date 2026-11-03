using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.DeathStatement;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>身份聚合结果（本村命中 + 跨村计数）</summary>
public sealed class DeathIdentityAggregateResult
{
    public List<DeathIdentityItem> Items { get; } = new();
    public int CrossVillageCount { get; set; }
}

/// <summary>
/// 身份八源聚合器（2026-11 死亡说明简化·3c 进阶：从 DeathStatementService 拆出的独立类）。
/// 八源：低收入人口 / 普惠高龄 / 残疾人 / 大学生 / 临时救助 / 近亲属备案 / 追缴在办 / 历史导入台账。
/// 各源查询按身份证命中（= ANY 不适用于单值，直接等值 + LIMIT），跨村记录只计数不进表。
/// 无村字段的记录视为不可判定 → 计入本村（见 VillageScopeResolver.InScope 注释）。
/// 注册见 MauiProgram（AGENTS §6：新增服务必须注册 DI）。
/// </summary>
public class DeathIdentityAggregator : BaseService
{
    protected override string ServiceName => "DeathIdentityAggregator";

    private readonly IDatabaseService _db;
    private readonly IDictCacheService _dictCache;

    public DeathIdentityAggregator(IDatabaseService db, IDictCacheService dictCache, ILoggerService logger)
        : base(logger)
    {
        _db = db;
        _dictCache = dictCache;
    }

    /// <summary>
    /// 八源聚合入口（原 DeathStatementService.GetIdentitiesAsync 纯移动，零行为变更）。
    /// </summary>
    public async Task<Result<DeathIdentityAggregateResult>> AggregateIdentitiesAsync(string idCard, VillageScope scope, CancellationToken ct = default)
    {
        var result = new DeathIdentityAggregateResult();

        // ① 低收入人口（12 分类：低保/单人保/边缘/特困/刚性/其他困难/收入超标）
        var archiveSql = @"
            SELECT a.id AS application_id,
                   COALESCE(a.classification_result,'') AS classification_result,
                   COALESCE(a.status,'') AS status,
                   COALESCE(a.total_guarantee_amount,0) AS total_guarantee_amount,
                   a.village_id, a.hukou_village_id, a.town_id, a.hukou_town_id,
                   COALESCE(a.community,'') AS community, COALESCE(a.town,'') AS town_text,
                   CASE WHEN a.applicant_id_card = $1 THEN TRUE ELSE FALSE END AS is_head
            FROM nc_biz_applications a
            WHERE a.deleted_at IS NULL
              AND (a.applicant_id_card = $1
                   OR EXISTS (SELECT 1 FROM nc_biz_family_members m
                              WHERE m.application_id = a.id AND m.id_card = $1 AND m.deleted_at IS NULL))
            LIMIT 20";
        var archives = await _db.QueryAsync<ArchiveIdentityRow>(archiveSql, ct, idCard);
        if (archives.IsFailure)
            return Result.Failure<DeathIdentityAggregateResult>(archives.ErrorCode!, $"身份聚合失败（低收入人口源）：{archives.Message}");

        foreach (var a in archives.Value ?? [])
        {
            var inVillage = VillageScopeResolver.ArchiveInScope(scope, a.VillageId, a.HukouVillageId, a.TownId, a.HukouTownId, a.Community, a.TownText);
            if (!inVillage)
            {
                result.CrossVillageCount++;
                continue;
            }
            result.Items.Add(new DeathIdentityItem
            {
                SourceKey = "Archive",
                TypeLabel = string.IsNullOrEmpty(a.ClassificationResult)
                    ? "低收入人口档案"
                    : ClassificationConstants.ConvertToFullName(a.ClassificationResult),
                StatusLabel = ArchiveStatusName(a.Status),
                Period = a.TotalGuaranteeAmount > 0 ? $"月保障 {a.TotalGuaranteeAmount:F2} 元" : string.Empty,
                SourceLabel = "低收入人口认定",
                Note = a.IsHead ? "户主" : "家庭成员",
                ApplicationId = a.ApplicationId,
                InVillage = true
            });
        }

        // ② 普惠高龄
        var elderlySql = @"
            SELECT e.id, COALESCE(e.name,'') AS name, COALESCE(e.status,'') AS status,
                   COALESCE(e.issue_start_month,'') AS issue_start_month, e.issue_amount,
                   e.death_date, COALESCE(e.stop_reason,'') AS stop_reason,
                   e.family_village_id, e.hukou_village_id, e.family_town_id, e.hukou_town_id
            FROM nc_biz_elderly_applications e
            WHERE e.id_card = $1 AND e.deleted_at IS NULL
            LIMIT 10";
        var elderly = await _db.QueryAsync<ElderlyIdentityRow>(elderlySql, ct, idCard);
        if (elderly.IsFailure)
            return Result.Failure<DeathIdentityAggregateResult>(elderly.ErrorCode!, $"身份聚合失败（普惠高龄源）：{elderly.Message}");

        foreach (var e in elderly.Value ?? [])
        {
            var inVillage = VillageScopeResolver.InScope(scope, e.FamilyVillageId, e.HukouVillageId, e.FamilyTownId, e.HukouTownId, null, null);
            if (!inVillage)
            {
                result.CrossVillageCount++;
                continue;
            }
            result.Items.Add(new DeathIdentityItem
            {
                SourceKey = "Elderly",
                TypeLabel = "普惠高龄津贴",
                StatusLabel = ElderlyBenefitConstants.GetStatusName(e.Status),
                Period = string.IsNullOrEmpty(e.IssueStartMonth) ? string.Empty : $"{e.IssueStartMonth} 起",
                SourceLabel = "普惠高龄",
                Note = e.IssueAmount > 0 ? $"{e.IssueAmount:F2} 元/月" : string.Empty,
                SourceRecordId = e.Id,
                InVillage = true
            });
        }

        // ③ 残疾人（档案成员字段）
        var disabilitySql = @"
            SELECT COALESCE(m.disability_level,'') AS disability_level,
                   COALESCE(m.disability_type,'') AS disability_type,
                   COALESCE(m.disability_certificate_no,'') AS disability_certificate_no,
                   m.application_id, a.village_id, a.hukou_village_id, a.town_id, a.hukou_town_id,
                   COALESCE(a.community,'') AS community, COALESCE(a.town,'') AS town_text
            FROM nc_biz_family_members m
            JOIN nc_biz_applications a ON a.id = m.application_id
            WHERE m.id_card = $1 AND m.is_disabled = TRUE
              AND m.deleted_at IS NULL AND a.deleted_at IS NULL
            LIMIT 10";
        var disabilities = await _db.QueryAsync<DisabilityIdentityRow>(disabilitySql, ct, idCard);
        if (disabilities.IsFailure)
            return Result.Failure<DeathIdentityAggregateResult>(disabilities.ErrorCode!, $"身份聚合失败（残疾人源）：{disabilities.Message}");

        foreach (var d in disabilities.Value ?? [])
        {
            var inVillage = VillageScopeResolver.ArchiveInScope(scope, d.VillageId, d.HukouVillageId, d.TownId, d.HukouTownId, d.Community, d.TownText);
            if (!inVillage)
            {
                result.CrossVillageCount++;
                continue;
            }
            var level = string.IsNullOrEmpty(d.DisabilityLevel)
                ? "等级未登记"
                : DictText(DictionaryTypeCodes.DisabilityLevels, d.DisabilityLevel);
            result.Items.Add(new DeathIdentityItem
            {
                SourceKey = "Disability",
                TypeLabel = "残疾人",
                StatusLabel = level,
                Period = string.IsNullOrEmpty(d.DisabilityCertificateNo) ? "残疾证号缺失" : $"残疾证 …{Last4(d.DisabilityCertificateNo)}",
                SourceLabel = "低收入人口档案",
                Note = DictText(DictionaryTypeCodes.DisabilityTypes, d.DisabilityType),
                ApplicationId = d.ApplicationId,
                InVillage = true
            });
        }

        // ④ 大学生在读
        var collegeSql = @"
            SELECT c.id, COALESCE(c.student_name,'') AS student_name, COALESCE(c.status,'') AS status,
                   c.graduation_year, COALESCE(c.school_name,'') AS school_name, c.application_id,
                   a.village_id, a.hukou_village_id, a.town_id, a.hukou_town_id,
                   COALESCE(a.community,'') AS community, COALESCE(a.town,'') AS town_text
            FROM nc_biz_college_students c
            JOIN nc_biz_applications a ON a.id = c.application_id
            WHERE c.id_card = $1 AND c.deleted_at IS NULL AND a.deleted_at IS NULL
            LIMIT 10";
        var colleges = await _db.QueryAsync<CollegeIdentityRow>(collegeSql, ct, idCard);
        if (colleges.IsFailure)
            return Result.Failure<DeathIdentityAggregateResult>(colleges.ErrorCode!, $"身份聚合失败（大学生源）：{colleges.Message}");

        foreach (var c in colleges.Value ?? [])
        {
            var inVillage = VillageScopeResolver.ArchiveInScope(scope, c.VillageId, c.HukouVillageId, c.TownId, c.HukouTownId, c.Community, c.TownText);
            if (!inVillage)
            {
                result.CrossVillageCount++;
                continue;
            }
            result.Items.Add(new DeathIdentityItem
            {
                SourceKey = "College",
                TypeLabel = "在校大学生",
                StatusLabel = CollegeStudentConstants.GetStatusDisplay(c.Status),
                Period = c.GraduationYear > 0 ? $"{c.GraduationYear} 年毕业" : string.Empty,
                SourceLabel = "大学生管理",
                Note = c.SchoolName,
                ApplicationId = c.ApplicationId,
                SourceRecordId = c.Id,
                InVillage = true
            });
        }

        // ⑤ 临时救助（村字段为公示表文本）
        var tempSql = @"
            SELECT t.id, COALESCE(t.applicant_name,'') AS applicant_name,
                   COALESCE(t.beneficiary_name,'') AS beneficiary_name,
                   COALESCE(t.status,'') AS status, t.apply_date,
                   COALESCE(t.family_category,'') AS family_category,
                   COALESCE(t.village,'') AS village, COALESCE(t.town,'') AS town,
                   COALESCE(t.relief_type,'') AS relief_type, COALESCE(t.confirm_amount,0) AS confirm_amount
            FROM nc_biz_temp_relief_applications t
            WHERE t.deleted_at IS NULL
              AND (t.applicant_id_card = $1 OR t.beneficiary_id_card = $1)
            LIMIT 10";
        var temps = await _db.QueryAsync<TempReliefIdentityRow>(tempSql, ct, idCard);
        if (temps.IsFailure)
            return Result.Failure<DeathIdentityAggregateResult>(temps.ErrorCode!, $"身份聚合失败（临时救助源）：{temps.Message}");

        foreach (var t in temps.Value ?? [])
        {
            var inVillage = VillageScopeResolver.TextInScope(scope, t.Village, t.Town);
            if (!inVillage)
            {
                result.CrossVillageCount++;
                continue;
            }
            result.Items.Add(new DeathIdentityItem
            {
                SourceKey = "TempRelief",
                TypeLabel = "临时救助",
                StatusLabel = TempReliefConstants.GetStatusName(t.Status),
                Period = t.ApplyDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                SourceLabel = "临时救助",
                Note = t.ConfirmAmount > 0 ? $"救助金 {t.ConfirmAmount:F2} 元" : t.FamilyCategory,
                InVillage = true
            });
        }

        // ⑥ 近亲属备案（死者为备案对象行）
        var nearSql = @"
            SELECT l.id, l.application_id, COALESCE(l.name,'') AS name, COALESCE(l.relation,'') AS relation,
                   COALESCE(l.help_type,'') AS help_type, l.month_amount,
                   a.village_id, a.hukou_village_id, a.town_id, a.hukou_town_id,
                   COALESCE(a.community,'') AS community, COALESCE(a.town,'') AS town_text
            FROM nc_biz_near_relative_links l
            LEFT JOIN nc_biz_applications a ON a.id = l.application_id
            WHERE l.id_card = $1 AND l.deleted_at IS NULL
              AND (a.id IS NULL OR a.deleted_at IS NULL)
            LIMIT 10";
        var nears = await _db.QueryAsync<NearRelativeIdentityRow>(nearSql, ct, idCard);
        if (nears.IsFailure)
            return Result.Failure<DeathIdentityAggregateResult>(nears.ErrorCode!, $"身份聚合失败（近亲属备案源）：{nears.Message}");

        foreach (var n in nears.Value ?? [])
        {
            var inVillage = !n.ApplicationId.HasValue
                ? true
                : VillageScopeResolver.ArchiveInScope(scope, n.VillageId, n.HukouVillageId, n.TownId, n.HukouTownId, n.Community, n.TownText);
            if (!inVillage)
            {
                result.CrossVillageCount++;
                continue;
            }
            result.Items.Add(new DeathIdentityItem
            {
                SourceKey = "NearRelative",
                TypeLabel = "近亲属备案对象",
                StatusLabel = string.IsNullOrEmpty(n.HelpType) ? "已备案" : n.HelpType,
                Period = n.MonthAmount > 0 ? $"月保障 {n.MonthAmount:F2} 元" : string.Empty,
                SourceLabel = "近亲属备案",
                Note = string.IsNullOrEmpty(n.Relation) ? string.Empty : $"与工作人员关系：{n.Relation}",
                ApplicationId = n.ApplicationId,
                InVillage = true
            });
        }

        // ⑦ 追缴在办（表无村字段 → 按身份证命中即计入本村）
        var recoverySql = @"
            SELECT id, COALESCE(person_name,'') AS person_name, COALESCE(status,'') AS status,
                   COALESCE(start_month,'') AS start_month, COALESCE(end_month,'') AS end_month,
                   COALESCE(recovery_amount,0) AS recovery_amount, COALESCE(source_type,'') AS source_type,
                   source_id
            FROM nc_biz_recovery_records
            WHERE id_card = $1
            LIMIT 10";
        var recoveries = await _db.QueryAsync<RecoveryIdentityRow>(recoverySql, ct, idCard);
        if (recoveries.IsFailure)
            return Result.Failure<DeathIdentityAggregateResult>(recoveries.ErrorCode!, $"身份聚合失败（后补追缴源）：{recoveries.Message}");

        foreach (var r in recoveries.Value ?? [])
        {
            result.Items.Add(new DeathIdentityItem
            {
                SourceKey = "Recovery",
                TypeLabel = "后补追缴",
                StatusLabel = RecoveryConstants.GetStatusDisplayName(r.Status),
                Period = string.IsNullOrEmpty(r.StartMonth) ? string.Empty : $"{r.StartMonth} ~ {r.EndMonth}",
                SourceLabel = "后补追缴",
                Note = r.RecoveryAmount > 0 ? $"应追缴 {r.RecoveryAmount:F2} 元" : string.Empty,
                ApplicationId = r.SourceId,
                SourceRecordId = r.Id,
                InVillage = true
            });
        }

        // ⑧ 历史导入台账（5 库：农村低保/城市低保/低保边缘/特困供养/刚性支出）。
        //    台账只有文本村字段、无 applicationId → 只识别身份、不建联动卡、不派五域任务
        //    （BuildLinksAsync 与派活补齐均按 SourceKey 白名单过滤，本源天然不被识别）。
        //    已有低收入在册档案（源①）命中时**不重复出账**：台账是同一身份的历史副本，
        //    在册档案才是当前事实，避免清单里出现"在享 + 历史台账"两条同源身份。
        var hasArchiveIdentity = result.Items.Any(i => i.SourceKey == "Archive");
        if (!hasArchiveIdentity)
        {
            var importSql = BuildImportIdentitySql();
            var imports = await _db.QueryAsync<ImportIdentityRow>(importSql, ct, idCard);
            if (imports.IsFailure)
                return Result.Failure<DeathIdentityAggregateResult>(imports.ErrorCode!, $"身份聚合失败（历史导入档案源）：{imports.Message}");

            var seenImportIdCards = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in imports.Value ?? [])
            {
                // 跨库同人保留先命中者（SQL 已按 source_rank 升序）
                if (!seenImportIdCards.Add(row.IdCard))
                    continue;

                // 村范围在 C# 侧判定：跨村要计数进提醒，不能在 SQL 里过滤掉
                if (!VillageScopeResolver.TextInScope(scope, row.Community, row.Street))
                {
                    result.CrossVillageCount++;
                    continue;
                }

                result.Items.Add(new DeathIdentityItem
                {
                    SourceKey = "ImportArchive",
                    TypeLabel = row.LibraryName,
                    StatusLabel = "历史导入台账",
                    Period = string.Empty,
                    SourceLabel = "历史导入档案",
                    Note = row.Relationship,
                    SourceRecordId = row.Id,
                    InVillage = true
                });
            }
        }

        return Result.Success(result);
    }

    private static string BuildImportIdentitySql()
    {
        var arms = new List<string>(DeathDeclarationConstants.ImportCandidateLibraries.Length);
        for (var i = 0; i < DeathDeclarationConstants.ImportCandidateLibraries.Length; i++)
        {
            var (persons, families, display) = DeathDeclarationConstants.ImportCandidateLibraries[i];
            arms.Add($"""
                SELECT {i + 1} AS source_rank,
                       '{display}' AS library_name,
                       p.id, p.id_card,
                       COALESCE({DeathDeclarationConstants.ImportRelationshipDisplaySql}, '') AS relationship,
                       COALESCE(f.community, '') AS community,
                       COALESCE(f.street, '') AS street
                  FROM {persons} p
                  LEFT JOIN {families} f ON f.applicant_id_card = p.head_id_card
                 WHERE p.id_card = $1
                 LIMIT 5
                """);
        }

        return $"""
            SELECT source_rank, library_name, id, id_card, relationship, community, street
              FROM (
                {string.Join("\n                UNION ALL\n                ", arms.Select(a => $"({a})"))}
              ) u
             ORDER BY source_rank, id
             LIMIT 10
            """;
    }

    private string DictText(string category, string? key) =>
        string.IsNullOrEmpty(key) ? string.Empty : _dictCache.GetValue(category, key);

    private static string Last4(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value[^Math.Min(4, value.Length)..];

    /// <summary>档案状态 → 中文（本页展示用；库内状态码事实来源仍为 ApplicationStatusCodes）</summary>
    private static string ArchiveStatusName(string? status) => status switch
    {
        ApplicationStatusCodes.DRAFT => "草稿",
        ApplicationStatusCodes.SUBMITTED => "已提交",
        ApplicationStatusCodes.APPROVED => "在享",
        ApplicationStatusCodes.COMPLETED => "已办结",
        ApplicationStatusCodes.REFUSED => "已拒绝",
        ApplicationStatusCodes.STOPPED => "已停保",
        _ => string.Empty
    };

    /// <summary>追缴状态 → 中文（RecoveryConstants.STATUS_*）</summary>
    private static string RecoveryStatusName(string? status) => status switch
    {
        RecoveryConstants.STATUS_DRAFT => "办理中",
        RecoveryConstants.STATUS_CONFIRMED => "已确认",
        RecoveryConstants.STATUS_PRINTED => "已出文书",
        _ => string.Empty
    };

    private sealed class ArchiveIdentityRow
    {
        public long ApplicationId { get; set; }
        public string ClassificationResult { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public decimal TotalGuaranteeAmount { get; set; }
        public int? VillageId { get; set; }
        public int? HukouVillageId { get; set; }
        public int? TownId { get; set; }
        public int? HukouTownId { get; set; }
        public string Community { get; set; } = string.Empty;
        public string TownText { get; set; } = string.Empty;
        public bool IsHead { get; set; }
    }

    private sealed class ElderlyIdentityRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string IssueStartMonth { get; set; } = string.Empty;
        public decimal IssueAmount { get; set; }
        public DateTime? DeathDate { get; set; }
        public string StopReason { get; set; } = string.Empty;
        public int? FamilyVillageId { get; set; }
        public int? HukouVillageId { get; set; }
        public int? FamilyTownId { get; set; }
        public int? HukouTownId { get; set; }
    }

    private sealed class DisabilityIdentityRow
    {
        public string DisabilityLevel { get; set; } = string.Empty;
        public string DisabilityType { get; set; } = string.Empty;
        public string DisabilityCertificateNo { get; set; } = string.Empty;
        public long ApplicationId { get; set; }
        public int? VillageId { get; set; }
        public int? HukouVillageId { get; set; }
        public int? TownId { get; set; }
        public int? HukouTownId { get; set; }
        public string Community { get; set; } = string.Empty;
        public string TownText { get; set; } = string.Empty;
    }

    private sealed class CollegeIdentityRow
    {
        public long Id { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int GraduationYear { get; set; }
        public string SchoolName { get; set; } = string.Empty;
        public long ApplicationId { get; set; }
        public int? VillageId { get; set; }
        public int? HukouVillageId { get; set; }
        public int? TownId { get; set; }
        public int? HukouTownId { get; set; }
        public string Community { get; set; } = string.Empty;
        public string TownText { get; set; } = string.Empty;
    }

    private sealed class TempReliefIdentityRow
    {
        public long Id { get; set; }
        public string ApplicantName { get; set; } = string.Empty;
        public string BeneficiaryName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime? ApplyDate { get; set; }
        public string FamilyCategory { get; set; } = string.Empty;
        public string Village { get; set; } = string.Empty;
        public string Town { get; set; } = string.Empty;
        public string ReliefType { get; set; } = string.Empty;
        public decimal ConfirmAmount { get; set; }
    }

    private sealed class NearRelativeIdentityRow
    {
        public long Id { get; set; }
        public long? ApplicationId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Relation { get; set; } = string.Empty;
        public string HelpType { get; set; } = string.Empty;
        public decimal? MonthAmount { get; set; }
        public int? VillageId { get; set; }
        public int? HukouVillageId { get; set; }
        public int? TownId { get; set; }
        public int? HukouTownId { get; set; }
        public string Community { get; set; } = string.Empty;
        public string TownText { get; set; } = string.Empty;
    }

    private sealed class RecoveryIdentityRow
    {
        public long Id { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StartMonth { get; set; } = string.Empty;
        public string EndMonth { get; set; } = string.Empty;
        public decimal RecoveryAmount { get; set; }
        public string SourceType { get; set; } = string.Empty;
        public long? SourceId { get; set; }
    }

    /// <summary>历史导入台账行（第⑧源：5 库 UNION ALL，列名与 BuildImportIdentitySql 一一对应）</summary>
    private sealed class ImportIdentityRow
    {
        public int SourceRank { get; set; }
        public string LibraryName { get; set; } = string.Empty;
        public long Id { get; set; }
        public string IdCard { get; set; } = string.Empty;
        public string Relationship { get; set; } = string.Empty;
        public string Community { get; set; } = string.Empty;
        public string Street { get; set; } = string.Empty;
    }

}
