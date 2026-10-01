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

    /// <summary>表名白名单统一走 Helpers.TableNameValidator（原本地白名单为其子集，已删除）。</summary>
    private static void ValidateTableName(string tableName)
    {
        TableNameValidator.ValidateOrThrow(tableName);
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

            // §6 加载失败必须显式失败：任一源表查询失败 → 整体 Failure，绝不吞掉返回空集合
            //（空集会与"该来源无记录"不可区分，调用方将当作"人不存在"继续业务流）
            var failed = completedTasks.FirstOrDefault(t => t.IsFailure);
            if (failed != null)
                return Result.Failure<List<PersonSearchResult>>(failed.ErrorCode!, failed.Message!);

            var results = completedTasks.SelectMany(r => r.Value ?? new List<PersonSearchResult>()).ToList();

            // 逐人补查家庭成员：无共享连接/事务，可并行执行（连接池上限内安全）。
            // 原先串行 2-3 次往返 × 命中条数；并行后整体等待时间 ≈ 单条最慢耗时。
            // §6：补查失败显式失败（空集会被当"无成员"保存回去，静默清空真实成员）。
            var enrichTasks = results.Select(person => Task.Run(async () =>
            {
                return await EnrichFamilyMembersAsync(person, ct);
            }, ct)).ToArray();
            await Task.WhenAll(enrichTasks);

            var enrichFailed = enrichTasks.Select(t => t.Result).FirstOrDefault(r => r.IsFailure);
            if (enrichFailed != null)
                return Result.Failure<List<PersonSearchResult>>(
                    enrichFailed.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    enrichFailed.Message ?? "家庭成员补查失败");

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
    /// §6：成员查询失败返回 Failure 并中止本次补查——绝不按"无成员"落库。
    /// </summary>
    private async Task<Result> EnrichFamilyMembersAsync(PersonSearchResult person, CancellationToken ct)
    {
        if (person.ApplicationId.HasValue)
        {
            var membersResult = await GetFamilyMembersAsync(
                person.ApplicationId.Value, ct);
            if (membersResult.IsFailure)
                return Result.Failure(membersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, membersResult.Message!);
            person.FamilyMembers = membersResult.Value!;

            if (person.FamilyMembers.Count == 0)
            {
                if (person.SourceTable.Contains("_persons") && person.RecordId > 0)
                {
                    var familyIdResult = await GetFamilyIdFromPersonAsync(
                        person.SourceTable, person.RecordId ?? 0, ct);
                    if (familyIdResult.IsFailure)
                        return Result.Failure(familyIdResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, familyIdResult.Message!);
                    if (familyIdResult.Value.HasValue)
                    {
                        var fromPersons = await GetFamilyMembersFromPersonsAsync(
                            person.SourceTable, familyIdResult.Value.Value, ct);
                        if (fromPersons.IsFailure)
                            return Result.Failure(fromPersons.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, fromPersons.Message!);
                        person.FamilyMembers = fromPersons.Value!;
                        LogInfo($"从{person.SourceDisplay}人员表补充导入家庭成员: {person.FamilyMembers.Count} 人");
                    }
                }
                else if (person.SourceTable == "nc_biz_applications")
                {
                    var familyResult = await GetFamilyMembersFromPersonTablesByIdCardAsync(
                        person.IdCard, ct);
                    if (familyResult.IsFailure)
                        return Result.Failure(familyResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, familyResult.Message!);
                    if (familyResult.Value!.Count > 0)
                    {
                        person.FamilyMembers = familyResult.Value;
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
            var familyIdResult = await GetFamilyIdFromPersonAsync(
                person.SourceTable, person.RecordId ?? 0, ct);
            if (familyIdResult.IsFailure)
                return Result.Failure(familyIdResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, familyIdResult.Message!);
            if (familyIdResult.Value.HasValue)
            {
                var fromPersons = await GetFamilyMembersFromPersonsAsync(
                    person.SourceTable, familyIdResult.Value.Value, ct);
                if (fromPersons.IsFailure)
                    return Result.Failure(fromPersons.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, fromPersons.Message!);
                person.FamilyMembers = fromPersons.Value!;
                LogInfo($"从{person.SourceDisplay}导入家庭成员: {person.FamilyMembers.Count} 人");
            }
            else
            {
                LogWarn($"人员表 {person.SourceDisplay} 中身份证 {DataMasker.MaskIdCard(person.IdCard)} 的 family_id 为空，无法关联家庭成员。可能原因：1)导入时未执行家庭关联 2)head_id_card 与 applicant_id_card 不匹配 3)家庭记录不存在");
            }
        }

        return Result.Success();
    }

    private async Task<Result<List<FamilyMember>>> GetFamilyMembersAsync(
        long applicationId, CancellationToken ct)
    {
        var sql = @"SELECT * FROM nc_biz_family_members 
                    WHERE application_id = $1 AND deleted_at IS NULL
                    ORDER BY is_applicant DESC, id ASC";

        var result = await _db.QueryAsync<FamilyMember>(sql, ct, applicationId);
        if (result.IsFailure)
            return Result.Failure<List<FamilyMember>>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"查询家庭成员失败: {result.Message}");
        return Result.Success(result.Value ?? new List<FamilyMember>());
    }

    private async Task<Result<long?>> GetFamilyIdFromPersonAsync(
        string personsTable, long personId, CancellationToken ct)
    {
        ValidateTableName(personsTable);
        var sql = $"SELECT family_id FROM {personsTable} WHERE id = $1";
        // 用泛型 ExecuteScalarAsync<long?>：无行 / family_id 为 SQL NULL 时返回 null，
        // 而不是被压成 0。这样调用方 familyId.HasValue 才能正确走"family_id 为空"的
        // 诊断告警分支，也不会再拿 family_id = 0 去做一次注定空结果的成员查询。
        var result = await _db.ExecuteScalarAsync<long?>(sql, ct, personId);
        if (result.IsFailure)
            return Result.Failure<long?>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"查询人员家庭关联失败: {result.Message}");
        return Result.Success<long?>(result.Value);
    }

    private async Task<Result<List<FamilyMember>>> GetFamilyMembersFromPersonsAsync(
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
        if (result.IsFailure)
            return Result.Failure<List<FamilyMember>>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"从人员表查询家庭成员失败: {result.Message}");
        if (result.Value == null)
            return Result.Success(new List<FamilyMember>());

        return Result.Success(result.Value.Select(dto => MapToFamilyMember(dto)).ToList());
    }

    private async Task<Result<List<FamilyMember>>> GetFamilyMembersFromPersonTablesByIdCardAsync(
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
            var familyIdResult = await GetFamilyIdFromPersonTableByIdCardAsync(table, idCard, ct);
            if (familyIdResult.IsFailure)
                return Result.Failure<List<FamilyMember>>(
                    familyIdResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    familyIdResult.Message!);
            if (familyIdResult.Value.HasValue)
            {
                LogInfo($"{table} 找到身份证: {DataMasker.MaskIdCard(idCard)}");
                var members = await GetFamilyMembersFromPersonsAsync(table, familyIdResult.Value.Value, ct);
                if (members.IsFailure)
                    return members;
                if (members.Value!.Count > 0)
                {
                    return members;
                }
            }
        }

        return Result.Success(new List<FamilyMember>());
    }

    private async Task<Result<long?>> GetFamilyIdFromPersonTableByIdCardAsync(
        string personsTable, string idCard, CancellationToken ct)
    {
        ValidateTableName(personsTable);
        var sql = $"SELECT family_id FROM {personsTable} WHERE id_card = $1 LIMIT 1";
        // 同 GetFamilyIdFromPersonAsync：无行 / NULL 需返回 null 而非 0
        var result = await _db.ExecuteScalarAsync<long?>(sql, ct, idCard);
        if (result.IsFailure)
            return Result.Failure<long?>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"查询人员家庭关联失败: {result.Message}");
        return Result.Success<long?>(result.Value);
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

    private async Task<Result<List<PersonSearchResult>>> ExecuteSingleQueryAsync(
        string table, string display, string columns, string where,
        string idCard, string join, CancellationToken ct)
    {
        ValidateTableName(table);
        var joinClause = string.IsNullOrWhiteSpace(join) ? "" : $" {join}";
        var sql = $"SELECT '{table}' as source_table, '{display}' as source_display, {columns} FROM {table} p {joinClause} WHERE {where}";

        Debug.WriteLine($"执行SQL: {table}");
        var result = await _db.QueryAsync<PersonSearchResult>(sql, ct, idCard);

        if (result.IsFailure)
        {
            LogWarn($"[PersonSearch] {table} 查询失败: {result.Message}");
            return Result.Failure<List<PersonSearchResult>>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"人员搜索源表 {table} 查询失败: {result.Message}");
        }

        LogInfo($"[PersonSearch] {table} 查询成功: {result.Value?.Count ?? 0} 条记录");
        return Result.Success(result.Value ?? new List<PersonSearchResult>());
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
