using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using System.Diagnostics;

namespace NewCosmos.Services.Domain.SocialAssistance;

public class PersonSearchService : BaseService, IPersonSearchService
{
    protected override string ServiceName => "PersonSearchService";
    private readonly IDatabaseService _db;
    private readonly IDictCacheService _dictCache;

    private static readonly HashSet<string> AllowedTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "nc_biz_rural_subsistence_persons",
        "nc_biz_urban_subsistence_persons",
        "nc_biz_low_income_edge_persons",
        "nc_biz_destitute_persons",
        "nc_biz_applications",
        "nc_biz_rural_subsistence_families",
        "nc_biz_urban_subsistence_families",
        "nc_biz_low_income_edge_families",
        "nc_biz_destitute_families"
    };

    private static void ValidateTableName(string tableName)
    {
        if (!AllowedTables.Contains(tableName))
            throw new InvalidOperationException($"无效的表名: {tableName}");
    }

    public PersonSearchService(IDatabaseService db, IDictCacheService dictCache, ILoggerService logger) : base(logger)
    {
        _db = db;
        _dictCache = dictCache;
    }

    public async Task<Result<List<PersonSearchResult>>> SearchByIdCardAsync(
        string idCard, CancellationToken ct = default)
    {
        Debug.WriteLine($"执行");

        var personTables = new (string Table, string Display, string Columns, string Where, string Join)[]
        {
            ("nc_biz_rural_subsistence_persons", "最低生活保障人员",
             "p.id as record_id, NULL::bigint as application_id, p.name, p.id_card, p.relationship, COALESCE(f.phone, '') as phone, COALESCE(f.city, '') as city, COALESCE(f.district, '') as district, ''::varchar as town, ''::varchar as village",
             "p.id_card = $1",
             "LEFT JOIN nc_biz_rural_subsistence_families f ON p.family_id = f.id"),
            ("nc_biz_urban_subsistence_persons", "最低生活保障人员",
             "p.id as record_id, NULL::bigint as application_id, p.name, p.id_card, p.relationship, COALESCE(f.phone, '') as phone, COALESCE(f.city, '') as city, COALESCE(f.district, '') as district, ''::varchar as town, ''::varchar as village",
             "p.id_card = $1",
             "LEFT JOIN nc_biz_urban_subsistence_families f ON p.family_id = f.id"),
            ("nc_biz_low_income_edge_persons", "最低生活保障边缘人员",
             "p.id as record_id, NULL::bigint as application_id, p.name, p.id_card, p.relationship, COALESCE(f.phone, '') as phone, COALESCE(f.city, '') as city, COALESCE(f.district, '') as district, ''::varchar as town, ''::varchar as village",
             "p.id_card = $1",
             "LEFT JOIN nc_biz_low_income_edge_families f ON p.family_id = f.id"),
            ("nc_biz_destitute_persons", "特困人员",
             "p.id as record_id, p.family_id as application_id, p.name, p.id_card, p.relationship, COALESCE(f.phone, '') as phone, COALESCE(f.city, '') as city, COALESCE(f.district, '') as district, COALESCE(f.street, '') as town, ''::varchar as village",
             "p.id_card = $1",
             "LEFT JOIN nc_biz_destitute_families f ON p.family_id = f.id"),
            ("nc_biz_applications", "申请表",
             "p.id as record_id, p.id as application_id, p.applicant_name as name, p.applicant_id_card as id_card, ''::varchar as relationship, p.applicant_phone as phone, COALESCE(p.city, '') as city, COALESCE(p.district, '') as district, COALESCE(p.town, '') as town, COALESCE(p.community, '') as village",
             "p.applicant_id_card = $1 AND p.deleted_at IS NULL",
             ""),
        };

        try
        {
            var tasks = personTables.Select(t =>
                ExecuteSingleQueryAsync(t.Table, t.Display, t.Columns, t.Where, idCard, t.Join, ct)).ToArray();

            var completedTasks = await Task.WhenAll(tasks);

            var results = completedTasks.SelectMany(r => r).ToList();

            // 逐人补查家庭成员：无共享连接/事务，可并行执行（连接池上限内安全）。
            // 原先串行 2-3 次往返 × 命中条数；并行后整体等待时间 ≈ 单条最慢耗时。
            var enrichTasks = results.Select(person => Task.Run(async () =>
            {
                try
                {
                    await EnrichFamilyMembersAsync(person, ct);
                }
                catch (Exception ex)
                {
                    LogWarn($"警告: {ex.Message}");
                }
            }, ct)).ToArray();
            await Task.WhenAll(enrichTasks);

            Debug.WriteLine($"[PersonSearch] 查询完成，共找到 {results.Count} 条记录");
            return Result.Success(results);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PersonSearch] 异常: {ex.GetType()}");
            throw;
        }
    }

    /// <summary>
    /// 为单条检索结果补查家庭成员（逻辑与旧串行循环一致，提取为独立方法以便并行）。
    /// </summary>
    private async Task EnrichFamilyMembersAsync(PersonSearchResult person, CancellationToken ct)
    {
        if (person.ApplicationId.HasValue)
        {
            person.FamilyMembers = await GetFamilyMembersAsync(
                person.ApplicationId.Value, ct);

            if (person.FamilyMembers.Count == 0)
            {
                if (person.SourceTable.Contains("_persons") && person.RecordId > 0)
                {
                    var familyId = await GetFamilyIdFromPersonAsync(
                        person.SourceTable, person.RecordId ?? 0, ct);
                    if (familyId.HasValue)
                    {
                        person.FamilyMembers = await GetFamilyMembersFromPersonsAsync(
                            person.SourceTable, familyId.Value, ct);
                        LogInfo($"从{person.SourceDisplay}人员表补充导入家庭成员: {person.FamilyMembers.Count} 人");
                    }
                }
                else if (person.SourceTable == "nc_biz_applications")
                {
                    var familyResult = await GetFamilyMembersFromPersonTablesByIdCardAsync(
                        person.IdCard, ct);
                    if (familyResult.Count > 0)
                    {
                        person.FamilyMembers = familyResult;
                        LogInfo($"从人员表反查导入家庭成员: {person.FamilyMembers.Count} 人（身份证: {DataMasker.MaskIdCard(person.IdCard)}）");
                    }
                    else
                    {
                        LogInfo($"申请表无家庭成员记录，人员表也无匹配: {person.FamilyMembers.Count} 人");
                    }
                }
                else
                {
                    LogInfo($"从申请表导入家庭成员: {person.FamilyMembers.Count} 人");
                }
            }
            else
            {
                LogInfo($"从申请表导入家庭成员: {person.FamilyMembers.Count} 人");
            }
        }
        else if (person.SourceTable.Contains("_persons") && person.RecordId > 0)
        {
            var familyId = await GetFamilyIdFromPersonAsync(
                person.SourceTable, person.RecordId ?? 0, ct);
            if (familyId.HasValue)
            {
                person.FamilyMembers = await GetFamilyMembersFromPersonsAsync(
                    person.SourceTable, familyId.Value, ct);
                LogInfo($"从{person.SourceDisplay}导入家庭成员: {person.FamilyMembers.Count} 人");
            }
            else
            {
                LogWarn($"人员表 {person.SourceDisplay} 中身份证 {DataMasker.MaskIdCard(person.IdCard)} 的 family_id 为空，无法关联家庭成员。可能原因：1)导入时未执行家庭关联 2)head_id_card 与 applicant_id_card 不匹配 3)家庭记录不存在");
            }
        }
    }

    private async Task<List<FamilyMember>> GetFamilyMembersAsync(
        long applicationId, CancellationToken ct)
    {
        var sql = @"SELECT * FROM nc_biz_family_members 
                    WHERE application_id = $1 AND deleted_at IS NULL
                    ORDER BY is_applicant DESC, id ASC";

        var result = await _db.QueryAsync<FamilyMember>(sql, ct, applicationId);
        return result.IsSuccess && result.Value != null
            ? result.Value
            : new List<FamilyMember>();
    }

    private async Task<long?> GetFamilyIdFromPersonAsync(
        string personsTable, long personId, CancellationToken ct)
    {
        ValidateTableName(personsTable);
        var sql = $"SELECT family_id FROM {personsTable} WHERE id = $1";
        // 用泛型 ExecuteScalarAsync<long?>：无行 / family_id 为 SQL NULL 时返回 null，
        // 而不是被压成 0。这样调用方 familyId.HasValue 才能正确走"family_id 为空"的
        // 诊断告警分支，也不会再拿 family_id = 0 去做一次注定空结果的成员查询。
        var result = await _db.ExecuteScalarAsync<long?>(sql, ct, personId);
        return result.IsSuccess ? result.Value : null;
    }

    private async Task<List<FamilyMember>> GetFamilyMembersFromPersonsAsync(
        string personsTable, long familyId, CancellationToken ct)
    {
        ValidateTableName(personsTable);

        var familyTable = personsTable.Replace("_persons", "_families");
        ValidateTableName(familyTable);

        var joinClause = $"LEFT JOIN {familyTable} f ON p.family_id = f.id";

        var sql = $@"SELECT p.name, p.id_card, p.relationship, COALESCE(f.phone, '') as phone,
                     p.is_disabled, p.disability_type, p.disability_level, p.disease_type, p.health_status
                     FROM {personsTable} p {joinClause}
                     WHERE p.family_id = $1
                     ORDER BY
                         CASE WHEN p.relationship LIKE '%户主%'
                               OR p.relationship LIKE '%本人%'
                              THEN 0 ELSE 1 END,
                         p.id_card";

        var result = await _db.QueryAsync<FamilyMemberDto>(sql, ct, familyId);
        if (!result.IsSuccess || result.Value == null)
            return new List<FamilyMember>();

        return result.Value.Select(dto => MapToFamilyMember(dto)).ToList();
    }

    private async Task<List<FamilyMember>> GetFamilyMembersFromPersonTablesByIdCardAsync(
        string idCard, CancellationToken ct)
    {
        var personTables = new[]
        {
            "nc_biz_rural_subsistence_persons",
            "nc_biz_urban_subsistence_persons",
            "nc_biz_low_income_edge_persons",
            "nc_biz_destitute_persons"
        };

        foreach (var table in personTables)
        {
            var familyId = await GetFamilyIdFromPersonTableByIdCardAsync(table, idCard, ct);
            if (familyId.HasValue)
            {
                LogInfo($"{table} 找到身份证: {DataMasker.MaskIdCard(idCard)}");
                var members = await GetFamilyMembersFromPersonsAsync(table, familyId.Value, ct);
                if (members.Count > 0)
                {
                    return members;
                }
            }
        }

        return new List<FamilyMember>();
    }

    private async Task<long?> GetFamilyIdFromPersonTableByIdCardAsync(
        string personsTable, string idCard, CancellationToken ct)
    {
        ValidateTableName(personsTable);
        var sql = $"SELECT family_id FROM {personsTable} WHERE id_card = $1 LIMIT 1";
        // 同 GetFamilyIdFromPersonAsync：无行 / NULL 需返回 null 而非 0
        var result = await _db.ExecuteScalarAsync<long?>(sql, ct, idCard);
        return result.IsSuccess ? result.Value : null;
    }

    private static FamilyMember MapToFamilyMember(FamilyMemberDto dto)
    {
        return new FamilyMember
        {
            Name = dto.Name ?? string.Empty,
            IdCard = dto.IdCard ?? string.Empty,
            RelationshipToHead = MapRelationship(dto.Relationship),
            Phone = dto.Phone ?? string.Empty,
            MemberCategory = MemberCategoryConstants.SHARED_LIVING,
            IsHouseholdHead = IsHeadRelationship(dto.Relationship),
            IsApplicant = IsHeadRelationship(dto.Relationship),
            IsDisabled = dto.IsDisabled,
            DisabilityType = dto.DisabilityType ?? string.Empty,
            DisabilityLevel = dto.DisabilityLevel ?? string.Empty,
            DiseaseName = dto.DiseaseType ?? string.Empty,
            HealthStatus = dto.HealthStatus ?? string.Empty,
        };
    }

    private static string MapRelationship(string sourceRelation)
    {
        if (string.IsNullOrWhiteSpace(sourceRelation))
            return DictionaryConstants.FamilyRelationship.OTHER;

        if (sourceRelation == DictionaryConstants.FamilyRelationship.HEAD ||
            sourceRelation == DictionaryConstants.FamilyRelationship.SPOUSE ||
            sourceRelation == DictionaryConstants.FamilyRelationship.SON ||
            sourceRelation == DictionaryConstants.FamilyRelationship.DAUGHTER ||
            sourceRelation == DictionaryConstants.FamilyRelationship.GRANDCHILD ||
            sourceRelation == DictionaryConstants.FamilyRelationship.PARENT ||
            sourceRelation == DictionaryConstants.FamilyRelationship.GRANDPARENT ||
            sourceRelation == DictionaryConstants.FamilyRelationship.SIBLING ||
            sourceRelation == DictionaryConstants.FamilyRelationship.OTHER)
        {
            return sourceRelation;
        }

        return sourceRelation switch
        {
            "户主" or "本人" or "本人/户主" => DictionaryConstants.FamilyRelationship.HEAD,
            "配偶" => DictionaryConstants.FamilyRelationship.SPOUSE,
            "子" or "婿" => DictionaryConstants.FamilyRelationship.SON,
            "女" or "媳" => DictionaryConstants.FamilyRelationship.DAUGHTER,
            "（外）孙子女" => DictionaryConstants.FamilyRelationship.GRANDCHILD,
            "父母/岳父母/公婆" => DictionaryConstants.FamilyRelationship.PARENT,
            "祖父母/外祖父母" => DictionaryConstants.FamilyRelationship.GRANDPARENT,
            "兄弟姐妹" => DictionaryConstants.FamilyRelationship.SIBLING,
            _ => DictionaryConstants.FamilyRelationship.OTHER
        };
    }

    private static bool IsHeadRelationship(string relationship)
    {
        if (string.IsNullOrWhiteSpace(relationship))
            return false;

        return relationship.Contains(DictionaryConstants.FamilyRelationship.HEAD) ||
               relationship == "户主" ||
               relationship == "本人" ||
               relationship == "本人/户主";
    }

    private async Task<List<PersonSearchResult>> ExecuteSingleQueryAsync(
        string table, string display, string columns, string where,
        string idCard, string join, CancellationToken ct)
    {
        ValidateTableName(table);
        var joinClause = string.IsNullOrWhiteSpace(join) ? "" : $" {join}";
        var sql = $"SELECT '{table}' as source_table, '{display}' as source_display, {columns} FROM {table} p {joinClause} WHERE {where}";

        Debug.WriteLine($"执行SQL: {table}");
        var result = await _db.QueryAsync<PersonSearchResult>(sql, ct, idCard);

        Debug.WriteLine($"[PersonSearch] {table}: IsSuccess={result.IsSuccess}");

        if (!result.IsSuccess)
        {
            LogWarn($"[PersonSearch] {table} 查询失败: {result.Message}");
        }
        else
        {
            LogInfo($"[PersonSearch] {table} 查询成功: {result.Value?.Count ?? 0} 条记录");
        }

        return result.Value ?? new List<PersonSearchResult>();
    }

    private class FamilyMemberDto
    {
        public string Name { get; set; } = string.Empty;
        public string IdCard { get; set; } = string.Empty;
        public string Relationship { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public bool IsDisabled { get; set; }
        public string DisabilityType { get; set; } = string.Empty;
        public string DisabilityLevel { get; set; } = string.Empty;
        public string DiseaseType { get; set; } = string.Empty;
        public string HealthStatus { get; set; } = string.Empty;
    }
}
