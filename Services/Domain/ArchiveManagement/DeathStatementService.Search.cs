using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.DeathStatement;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.UserManagement;


namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 本村范围搜索（申报台账 + 档案死亡登记 + 未申报候选）。
/// 2026-11 死亡说明简化·阶段3c：从 DeathStatementService.cs 纯移动拆出（零行为变更，接口与 DI 不动）。
/// </summary>
public partial class DeathStatementService
{

    #region 搜索

    /// <summary>范围数值ID（村级=村ID，镇级=乡镇ID；查询参数 $1 固定用它）</summary>
    private static int ScopeId(VillageScope scope) => scope.IsTownScope ? scope.TownId ?? 0 : scope.VillageId;

    /// <summary>范围归一化名称（村级=村名核心，镇级=镇名；查询参数 $2 固定用它）</summary>
    private static string ScopeName(VillageScope scope) => scope.IsTownScope ? scope.TownName : scope.VillageCoreName;

    /// <inheritdoc />
    public async Task<Result<List<DeathSearchItem>>> SearchAsync(
        VillageScope scope, string? keyword, DateTime fromDate, DateTime toDate,
        bool showAll, CancellationToken ct = default)
    {
        try
        {
            var kw = keyword?.Trim() ?? string.Empty;
            var from = fromDate.Date;
            var to = toDate.Date.AddDays(1);
            var items = new List<DeathSearchItem>();

            // ① 主源：申报台账（DISTINCT ON 取每个死者最新一条，ORDER BY 前缀必须一致）
            var declSql = @"
                SELECT DISTINCT ON (deceased_id_card)
                       id AS declaration_id,
                       NULL::bigint AS death_record_id,
                       application_id,
                       deceased_name AS name,
                       deceased_id_card AS id_card,
                       TO_CHAR(death_date, 'YYYY-MM-DD') AS death_date_text,
                       TO_CHAR(death_time, 'HH24:MI') AS death_time_text,
                       declared_by_name,
                       declared_by_type,
                       TO_CHAR(declared_at, 'YYYY-MM-DD HH24:MI') AS declared_at_text,
                       status,
                       '申报台账' AS source,
                       COALESCE(deceased_relation, '') AS relation,
                       COALESCE(death_reason, '') AS death_reason,
                       COALESCE(death_certificate_no, '') AS death_cert_no,
                       COALESCE(village_name, '') AS village_name
                FROM nc_biz_death_declarations
                WHERE " + ScopeDeclarationSql(scope) + @"
                  AND deleted_at IS NULL
                  AND ($4::boolean OR status <> 'Revoked')
                  AND death_date >= $2 AND death_date < $3
                  AND ($5 = '' OR deceased_name ILIKE '%' || $5 || '%' OR deceased_id_card = $5)
                ORDER BY deceased_id_card, declared_at DESC
                LIMIT 50";

            var declResult = await _db.QueryAsync<DeathSearchItem>(declSql, ct,
                ScopeId(scope), from, to, showAll, kw);
            if (declResult.IsFailure)
                return Result.Failure<List<DeathSearchItem>>(declResult.ErrorCode!, declResult.Message!);
            items.AddRange(declResult.Value ?? []);

            // ② 补充候选：本范围档案死亡登记但尚无有效申报 → 标「未申报」
            var recordSql = @"
                SELECT d.id AS death_record_id,
                       d.application_id,
                       d.member_name AS name,
                       d.member_id_card AS id_card,
                       TO_CHAR(d.death_date, 'YYYY-MM-DD') AS death_date_text,
                       COALESCE(TO_CHAR(d.death_time, 'HH24:MI'), '') AS death_time_text,
                       -- 档案死亡登记无「申报人」概念：原先拿 operator_name（操作账号名）顶替，
                       -- 导致带出后申报人姓名显示成登录账号（如 xiaoyao0720）。返回空串，
                       -- 由表单默认值（当前登录人姓名字段）接管，用户可改。
                       '' AS declared_by_name,
                       '' AS declared_by_type,
                       COALESCE(TO_CHAR(d.created_at, 'YYYY-MM-DD HH24:MI'), '') AS declared_at_text,
                       '' AS status,
                       '档案死亡登记' AS source,
                       COALESCE(d.relationship_to_head, '') AS relation,
                       COALESCE(d.death_reason, '') AS death_reason,
                       COALESCE(d.death_certificate_no, '') AS death_cert_no,
                       COALESCE(a.community, '') AS village_name
                FROM nc_biz_death_records d
                JOIN nc_biz_applications a ON a.id = d.application_id
                WHERE " + ScopeApplicationSql(scope, "a") + @"
                  AND d.death_date >= $3 AND d.death_date < $4
                  AND ($5 = '' OR d.member_name ILIKE '%' || $5 || '%' OR d.member_id_card = $5)
                  AND NOT EXISTS (
                        SELECT 1 FROM nc_biz_death_declarations x
                        WHERE x.deceased_id_card = d.member_id_card
                          AND x.deleted_at IS NULL
                          AND x.status <> 'Revoked')
                ORDER BY d.death_date DESC, d.id DESC
                LIMIT 50";

            var recordResult = await _db.QueryAsync<DeathSearchItem>(recordSql, ct,
                ScopeId(scope), ScopeName(scope), from, to, kw);
            if (recordResult.IsFailure)
                return Result.Failure<List<DeathSearchItem>>(recordResult.ErrorCode!, recordResult.Message!);

            var known = new HashSet<string>(items.Where(i => !string.IsNullOrEmpty(i.IdCard))
                .Select(i => i.IdCard), StringComparer.Ordinal);
            foreach (var row in recordResult.Value ?? [])
            {
                if (!string.IsNullOrEmpty(row.IdCard) && known.Contains(row.IdCard))
                    continue;
                items.Add(row);
            }

            // ③ 导入库档案候选（5 库 UNION ALL）：本范围在册、关键字命中、且从未申报/登记死亡的人。
            //    补上"人只在历史导入台账（如特困库）、无申报无登记"搜不到的缺口（2026-10-31）；
            //    候选无死亡日期字段 → 不受日期范围过滤；仅关键字非空时执行（无关键字列出任意在册人员对死亡申报无意义）。
            //    排除已有有效申报/死亡登记者（防"未登记死亡"假角标），与来源② NOT EXISTS 语义一致。
            if (kw.Length > 0)
            {
                var arms = DeathDeclarationConstants.ImportCandidateLibraries
                    .Select((lib, rank) => BuildImportCandidateArm(scope, lib.Persons, lib.Families, lib.Display, rank));
                var importSql = @"SELECT declaration_id, death_record_id, application_id, name, id_card,
                                         death_date_text, death_time_text, declared_by_name, declared_by_type,
                                         declared_at_text, status, source, relation, death_reason, death_cert_no, village_name
                                  FROM (" + string.Join("\nUNION ALL\n", arms.Select(a => $"({a})")) + @") t
                                  ORDER BY source_rank, person_id
                                  LIMIT 20";

                var importResult = await _db.QueryAsync<DeathSearchItem>(importSql, ct,
                    ScopeName(scope), $"%{kw}%");
                if (importResult.IsFailure)
                    return Result.Failure<List<DeathSearchItem>>(importResult.ErrorCode!, importResult.Message!);

                // 去重：known.Add 失败 = 与①②重复或跨库同人（保留优先级高者——臂按优先级排序，先到先得）
                foreach (var row in importResult.Value ?? [])
                {
                    if (string.IsNullOrEmpty(row.IdCard) || !known.Add(row.IdCard))
                        continue;
                    row.IsImportCandidate = true;
                    items.Add(row);
                }
            }

            foreach (var item in items)
            {
                // 导入库候选：无申报/无登记，角标与副标题走候选口径（不能落进"未申报"分支——那条副标题说"档案已登记死亡"）
                if (item.IsImportCandidate)
                {
                    item.HasDeclaration = false;
                    item.StatusName = string.Empty;
                    item.Badge = "未登记死亡";
                    item.Subtitle = "导入档案在册人员 · 尚无死亡申报/登记";
                    continue;
                }

                item.HasDeclaration = item.DeclarationId.HasValue;
                item.StatusName = DeathDeclarationConstants.GetStatusName(item.Status);
                item.Badge = !item.HasDeclaration
                    ? "未申报"
                    : item.Status == DeathDeclarationConstants.StatusRevoked ? "已作废"
                    : item.Status == DeathDeclarationConstants.StatusArchived ? "已归档"
                    : "已申报";
                item.Subtitle = item.HasDeclaration
                    ? $"{item.DeclaredByType} {item.DeclaredByName} · {item.DeclaredAtText} 申报"
                    : "档案已登记死亡，尚无申报记录";
            }

            items = items.OrderByDescending(i => i.DeathDateText).ThenByDescending(i => i.DeathTimeText).ToList();
            return Result.Success(items);
        }
        catch (Exception ex)
        {
            LogException(ex, "SearchAsync");
            return Result.FromException<List<DeathSearchItem>>(ex);
        }
    }

    /// <summary>
    /// 单库候选臂（$1 = 范围归一化名，$2 = 关键字模糊模式）：
    /// 本范围在册（家庭表 street/community 文本归一化匹配，与来源② ScopeApplicationSql 同口径）
    /// + 关键字命中姓名/身份证（ILIKE 片段，同 ImportedArchiveService 搜索口径）
    /// + 从未有效申报/登记死亡（防假角标）。
    /// 列清单与来源①对齐（无申报三ID为 NULL），外层按 source_rank/person_id 排序去优先级。
    /// </summary>
    private static string BuildImportCandidateArm(VillageScope scope, string personsTable, string familiesTable, string display, int sourceRank)
    {
        var scopeColumn = scope.IsTownScope ? "f.street" : "f.community";
        return $"""
            SELECT NULL::bigint AS declaration_id,
                   NULL::bigint AS death_record_id,
                   NULL::bigint AS application_id,
                   p.name, p.id_card,
                   '' AS death_date_text,
                   '' AS death_time_text,
                   '' AS declared_by_name,
                   '' AS declared_by_type,
                   '' AS declared_at_text,
                   '' AS status,
                   '{display}' AS source,
                   COALESCE({DeathDeclarationConstants.ImportRelationshipDisplaySql}, '') AS relation,
                   '' AS death_reason,
                   '' AS death_cert_no,
                   COALESCE(f.community, '') AS village_name,
                   {sourceRank} AS source_rank,
                   p.id AS person_id
              FROM {personsTable} p
              LEFT JOIN {familiesTable} f ON f.applicant_id_card = p.head_id_card
             WHERE {VillageScopeResolver.NameMatchSql(scopeColumn, 1)}
               AND (p.name ILIKE $2 OR p.id_card ILIKE $2)
               AND NOT EXISTS (SELECT 1 FROM nc_biz_death_declarations x
                               WHERE x.deceased_id_card = p.id_card AND x.deleted_at IS NULL AND x.status <> 'Revoked')
               AND NOT EXISTS (SELECT 1 FROM nc_biz_death_records r WHERE r.member_id_card = p.id_card)
             LIMIT 5
            """;
    }

    #endregion
}
