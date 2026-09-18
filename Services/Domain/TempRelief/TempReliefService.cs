using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.System;
using System.Text.RegularExpressions;

namespace NewCosmos.Services.Domain.TempRelief;

/// <summary>
/// 临时救助服务实现
/// 业务规则：
///  1. 申请人来源仅限导入台账库（5 个）与低保申请库（nc_biz_applications），不可自由录入
///  2. 大额救助金额不填写；小额救助从配置档位选定额
///  3. 同一身份证同一年度仅一条申请（终止也占名额；软删除释放名额）
/// </summary>
public class TempReliefService : BaseService, ITempReliefService
{
    protected override string ServiceName => "TempReliefService";

    private readonly IDatabaseService _db;
    private readonly IStandardConfigService _standardConfigService;
    private readonly IDictCacheService _dictCache;
    private readonly UserManagement.IOrganizationService _organizationService;

    public TempReliefService(IDatabaseService db, IStandardConfigService standardConfigService, IDictCacheService dictCache, UserManagement.IOrganizationService organizationService, ILoggerService logger)
        : base(logger)
    {
        _db = db;
        _standardConfigService = standardConfigService;
        _dictCache = dictCache;
        _organizationService = organizationService;
    }

    /// <summary>
    /// 按"填报单位"名称查组织并取其联系电话；填报单位为空或组织查不到返回空串。
    /// </summary>
    public async Task<Result<string>> GetReportUnitPhoneAsync(string? reportUnit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reportUnit))
            return Result.Success(string.Empty);
        try
        {
            var org = await _organizationService.GetByNameAsync(reportUnit.Trim(), ct);
            if (org.IsFailure || org.Value == null)
                return Result.Success(string.Empty);
            return Result.Success(org.Value.Phone ?? string.Empty);
        }
        catch (Exception ex)
        {
            LogWarn($"按填报单位取单位电话失败: {ex.Message}");
            return Result.Success(string.Empty);
        }
    }

    #region 候选人检索（仅限白名单表）

    public async Task<Result<List<TempReliefCandidate>>> SearchCandidatesAsync(string keyword, string? excludeIdCard = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return Result.Success(new List<TempReliefCandidate>());

        var results = new List<TempReliefCandidate>();
        var kw = keyword.Trim();

        foreach (var table in TempReliefConstants.AllowedSourceTables)
        {
            var result = await SearchCandidatesInTableAsync(table, kw, excludeIdCard, ct);
            if (result.IsFailure)
                return result;
            results.AddRange(result.Value);
        }

        // 姓名/身份证精确命中排序在前
        return Result.Success(results
            .OrderByDescending(c => string.Equals(c.IdCard, kw, StringComparison.OrdinalIgnoreCase))
            .ThenBy(c => c.Name)
            .Take(50)
            .ToList());
    }

    private async Task<Result<List<TempReliefCandidate>>> SearchCandidatesInTableAsync(string table, string keyword, string? excludeIdCard, CancellationToken ct)
    {
        try
        {
            var sql = "SELECT " + BuildCandidateSelect(table) +
                $" FROM {table} WHERE (applicant_name ILIKE $1 OR applicant_id_card ILIKE $1)";
            var parameters = new List<object> { $"%{keyword}%" };

            if (!string.IsNullOrWhiteSpace(excludeIdCard))
            {
                sql += " AND applicant_id_card != $2";
                parameters.Add(excludeIdCard);
            }

            sql += " LIMIT 20";

            var result = await _db.QueryAsync<TempReliefCandidateRow>(sql, ct, parameters.ToArray());
            if (result.IsFailure)
                return Result.Failure<List<TempReliefCandidate>>(result.ErrorCode!, result.Message!);

            return Result.Success(result.Value.Select(r => r.ToCandidate(table)).ToList());
        }
        catch (Exception ex)
        {
            LogException(ex, $"检索来源表 {table} 失败");
            return Result.FromException<List<TempReliefCandidate>>(ex);
        }
    }

    /// <summary>
    /// 生成候选人查询 SELECT 列（各来源表列名统一）
    /// </summary>
    private static string BuildCandidateSelect(string table) => table switch
    {
        // 农村/城市低保台账无 hukou_address 列，户籍地址缺省（户籍由表单从当前用户组织机构获取）
        "nc_biz_rural_subsistence_families" or
        "nc_biz_urban_subsistence_families" => string.Join(", ",
            "id AS source_family_id",
            "applicant_name AS name",
            "applicant_id_card AS id_card",
            "COALESCE(phone,'') AS phone",
            "COALESCE(province,'') AS province",
            "COALESCE(city,'') AS city",
            "COALESCE(district,'') AS district",
            "COALESCE(address,'') AS detail_address",
            "COALESCE(address,'') AS family_address",
            "'' AS hukou_address",
            "COALESCE(street,'') AS town",
            "COALESCE(community,'') AS village",
            "family_size",
            "COALESCE(bank_name,'') AS bank_name",
            "COALESCE(bank_account,'') AS bank_account",
            "COALESCE(one_card_account,'') AS one_card_account"),
        // 特困人员台账无 address 列，家庭住址存于 hukou_address，无详细地址分列
        "nc_biz_destitute_families" => string.Join(", ",
            "id AS source_family_id",
            "applicant_name AS name",
            "applicant_id_card AS id_card",
            "COALESCE(phone,'') AS phone",
            "COALESCE(province,'') AS province",
            "COALESCE(city,'') AS city",
            "COALESCE(district,'') AS district",
            "'' AS detail_address",
            "COALESCE(hukou_address,'') AS family_address",
            "COALESCE(hukou_address,'') AS hukou_address",
            "COALESCE(street,'') AS town",
            "COALESCE(community,'') AS village",
            "family_size",
            "COALESCE(bank_name,'') AS bank_name",
            "COALESCE(bank_account,'') AS bank_account",
            "COALESCE(one_card_account,'') AS one_card_account"),
        // 低保边缘/刚性支出台账无 family_size 列，只有 guarantee_size；无 hukou_address 列
        "nc_biz_low_income_edge_families" or
        "nc_biz_rigid_expenditure_families" => string.Join(", ",
            "id AS source_family_id",
            "applicant_name AS name",
            "applicant_id_card AS id_card",
            "COALESCE(phone,'') AS phone",
            "COALESCE(province,'') AS province",
            "COALESCE(city,'') AS city",
            "COALESCE(district,'') AS district",
            "COALESCE(address,'') AS detail_address",
            "COALESCE(address,'') AS family_address",
            "'' AS hukou_address",
            "COALESCE(street,'') AS town",
            "COALESCE(community,'') AS village",
            "guarantee_size AS family_size",
            "'' AS bank_name",
            "'' AS bank_account",
            "'' AS one_card_account"),
        "nc_biz_applications" => string.Join(", ",
            "id AS source_family_id",
            "applicant_name AS name",
            "applicant_id_card AS id_card",
            "COALESCE(gender,'') AS gender",
            "COALESCE(applicant_phone,'') AS phone",
            "COALESCE(province,'') AS province",
            "COALESCE(city,'') AS city",
            "COALESCE(district,'') AS district",
            "COALESCE(address,'') AS detail_address",
            "COALESCE(province||city||district||town||community,'') AS family_address",
            "COALESCE(hukou_address,'') AS hukou_address",
            "COALESCE(town,'') AS town",
            "COALESCE(community,'') AS village",
            "NULL AS family_size",
            "COALESCE(bank_name,'') AS bank_name",
            "COALESCE(bank_account,'') AS bank_account",
            "'' AS one_card_account"),
        _ => throw new ArgumentException($"来源表不在白名单内: {table}")
    } + ", " + BuildHukouTypeSelect(table);

    /// <summary>
    /// 户籍类型快照取值（指定字段固定映射）：城乡低保台账无该列，按表名固定；
    /// 其余白名单表直接读 hukou_type 列（农村/城镇，兼容申请库 Rural/Urban）
    /// </summary>
    private static string BuildHukouTypeSelect(string table) => table switch
    {
        "nc_biz_rural_subsistence_families" => "'农村' AS hukou_type",
        "nc_biz_urban_subsistence_families" => "'城镇' AS hukou_type",
        _ => "COALESCE(hukou_type,'') AS hukou_type"
    };

    public async Task<Result<TempReliefCandidate>> GetCandidateDetailAsync(string sourceTable, long sourceFamilyId, string? excludeIdCard = null, CancellationToken ct = default)
    {
        if (!TempReliefConstants.IsAllowedSourceTable(sourceTable))
            return Result.Failure<TempReliefCandidate>(ErrorCodes.VALIDATION_FAILED, $"来源表不在白名单内: {sourceTable}");

        try
        {
            var sql = "SELECT " + BuildCandidateSelect(sourceTable) + $" FROM {sourceTable} WHERE id = $1";
            var result = await _db.QuerySingleAsync<TempReliefCandidateRow>(sql, ct, sourceFamilyId);
            if (result.IsFailure || result.Value == null)
                return Result.Failure<TempReliefCandidate>(ErrorCodes.NOT_FOUND, "未找到该候选人");

            var candidate = result.Value.ToCandidate(sourceTable);

            // 镇/村缺失时从地址文本解析（台账表 street/community 列常为空，地址中携带"XX镇XX村"）；户籍缺失时以家庭住址兜底
            await ResolveTownVillageAsync(candidate, ct);
            if (string.IsNullOrWhiteSpace(candidate.HukouAddress))
            {
                candidate.HukouAddress = candidate.FamilyAddress;
            }

            // 低保申请库来源：家庭类别取认定分类（如 农村低保/特困分散供养），供表单回显与救助原因默认值使用
            if (sourceTable == "nc_biz_applications")
            {
                const string classSql = @"SELECT COALESCE(classification_result,'') AS classification_result
                                          FROM nc_biz_applications WHERE id = $1";
                var clsResult = await _db.QuerySingleAsync<PolicyClassRow>(classSql, ct, sourceFamilyId);
                var cls = clsResult.IsSuccess && clsResult.Value != null ? clsResult.Value.ClassificationResult : string.Empty;
                if (!string.IsNullOrWhiteSpace(cls))
                    candidate.FamilyCategory = ClassificationConstants.ConvertFromCode(cls);
            }
            else
            {
                // 台账来源：家庭表无性别列 → 从成员明细表取户主性别，兜底用身份证第 17 位奇偶推导
                var personsTable = TempReliefConstants.GetPersonsTable(sourceTable);
                if (!string.IsNullOrEmpty(personsTable))
                {
                    const string genderSql = @"
                        SELECT COALESCE(gender,'') AS gender
                        FROM {PERSONS}
                        WHERE family_id = $1 AND relationship = 'Head'
                        ORDER BY id LIMIT 1";
                    var genderResult = await _db.QuerySingleAsync<GenderRow>(
                        genderSql.Replace("{PERSONS}", personsTable), ct, sourceFamilyId);
                    if (genderResult.IsSuccess && !string.IsNullOrWhiteSpace(genderResult.Value?.Gender))
                    {
                        candidate.Gender = genderResult.Value.Gender;
                    }
                    else if (string.IsNullOrWhiteSpace(candidate.Gender))
                    {
                        candidate.Gender = InferGenderFromIdCard(candidate.IdCard);
                    }
                }

                // 台账来源：家庭人数以成员明细表为准（家庭表 family_size 曾出现与明细不符）
                if (!string.IsNullOrEmpty(personsTable))
                {
                    const string countSql = "SELECT COUNT(*) FROM {PERSONS} WHERE family_id = $1";
                    var countResult = await _db.ExecuteScalarAsync<long>(countSql.Replace("{PERSONS}", personsTable), ct, sourceFamilyId);
                    if (countResult.IsSuccess && countResult.Value > 0)
                        candidate.FamilySize = (int)countResult.Value;
                }
            }

            // 是否当年已申请
            var applyYear = DateTime.Today.Year.ToString();
            var annualResult = await CheckAnnualExistsAsync(candidate.IdCard, applyYear, null, ct);
            if (annualResult.IsFailure)
                return Result.Failure<TempReliefCandidate>(annualResult.ErrorCode!, annualResult.Message!);
            candidate.HasAppliedThisYear = annualResult.Value;

            // 家庭成员快照
            var memberResult = await LoadMemberSnapshotAsync(sourceTable, sourceFamilyId, ct);
            if (memberResult.IsFailure)
                return Result.Failure<TempReliefCandidate>(memberResult.ErrorCode!, memberResult.Message!);
            candidate.Members = memberResult.Value;

            return Result.Success(candidate);
        }
        catch (Exception ex)
        {
            LogException(ex, "获取候选人详情失败");
            return Result.FromException<TempReliefCandidate>(ex);
        }
    }

    /// <summary>
    /// 解析候选人所在镇/村：地址文本中提取"XX镇/乡/街道""XX村/社区"；
    /// 提取不到镇但已识别村时，通过地址库（nc_regions_villages→nc_regions_towns）反查镇名。
    /// </summary>
    private async Task ResolveTownVillageAsync(TempReliefCandidate candidate, CancellationToken ct)
    {
        var address = candidate.FamilyAddress;
        if (string.IsNullOrWhiteSpace(address)) return;

        if (string.IsNullOrWhiteSpace(candidate.Town))
        {
            var m = Regex.Match(address, @"([^县]{1,8}(?:镇|乡|街道))");
            if (m.Success) candidate.Town = m.Groups[1].Value;
        }

        if (string.IsNullOrWhiteSpace(candidate.Village))
        {
            string? village = null;
            var m2 = Regex.Match(address, @"(?:镇|乡|街道)([^县]{1,10}?)(村|社区)");
            if (m2.Success) village = m2.Groups[1].Value + m2.Groups[2].Value;
            else
            {
                var m3 = Regex.Match(address, @"([^县]{1,10}?)(村|社区)");
                if (m3.Success) village = m3.Groups[1].Value + m3.Groups[2].Value;
            }
            if (!string.IsNullOrWhiteSpace(village))
                candidate.Village = village;
        }

        // 村已识别而镇缺失：经地址库村表反查所属镇
        if (string.IsNullOrWhiteSpace(candidate.Town) && !string.IsNullOrWhiteSpace(candidate.Village))
        {
            try
            {
                const string sql = @"SELECT t.town_name AS town_name
                                     FROM nc_regions_villages v
                                     JOIN nc_regions_towns t ON t.id = v.town_id
                                     WHERE v.village_name LIKE '%' || $1 || '%'
                                     ORDER BY t.sort_order, v.id
                                     LIMIT 1";
                var result = await _db.QuerySingleAsync<TownNameRow>(sql, ct, candidate.Village);
                if (result.IsSuccess && result.Value != null && !string.IsNullOrWhiteSpace(result.Value.TownName))
                    candidate.Town = result.Value.TownName;
            }
            catch (Exception ex)
            {
                LogWarn($"地址库反查镇失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 加载候选人家庭成员快照：
    ///  - 导入台账家庭表：member_names / member_id_cards 分号分隔，按位对齐（无关系/工作单位/年收入）
    ///  - 低保申请库：nc_biz_family_members 明细
    /// </summary>
    /// <summary>
    /// 台账成员表年收入表达式（成员快照统一输出 annual_income 年值口径）：
    /// 农村/特困台账成员表自带 annual_income 列（年值）；
    /// 城市低保/低保边缘/刚性支出成员表为 monthly_income（月值），按既有口径 ×12 年化
    /// （与 MonthlyReportService、ImportedArchiveService 的表间口径一致）。
    /// </summary>
    private static string GetPersonsAnnualIncomeExpr(string personsTable) => personsTable switch
    {
        "nc_biz_rural_subsistence_persons" or "nc_biz_destitute_persons" => "annual_income",
        "nc_biz_urban_subsistence_persons" or "nc_biz_low_income_edge_persons" or "nc_biz_rigid_expenditure_persons"
            => "COALESCE(monthly_income, 0) * 12",
        _ => "NULL::numeric"
    };

    private async Task<Result<List<TempReliefMemberSnapshot>>> LoadMemberSnapshotAsync(string sourceTable, long sourceFamilyId, CancellationToken ct)
    {
        if (sourceTable == "nc_biz_applications")
        {
            const string sql = @"SELECT name AS member_name,
                    COALESCE(gender,'') AS gender,
                    COALESCE(relationship_to_head,'') AS relation,
                    COALESCE(id_card,'') AS id_card,
                    COALESCE(work_unit,'') AS work_unit,
                    annual_income
                FROM nc_biz_family_members
                WHERE application_id = $1 AND deleted_at IS NULL
                ORDER BY is_applicant DESC, id
                LIMIT 20";
            var result = await _db.QueryAsync<TempReliefMemberRow>(sql, ct, sourceFamilyId);
            if (result.IsFailure)
                return Result.Failure<List<TempReliefMemberSnapshot>>(result.ErrorCode!, result.Message!);

            return Result.Success(result.Value.Select(m => new TempReliefMemberSnapshot
            {
                MemberName = m.MemberName,
                Gender = m.Gender,
                Relation = m.Relation,
                IdCard = m.IdCard,
                WorkUnit = m.WorkUnit,
                AnnualIncome = m.AnnualIncome
            }).ToList());
        }

        // 台账来源：优先从成员明细表（persons）读取完整家庭成员（含户主、性别、关系、年龄、年收入）
        var personsTable = TempReliefConstants.GetPersonsTable(sourceTable);
        if (!string.IsNullOrEmpty(personsTable))
        {
            const string personsSql = @"
                SELECT COALESCE(name,'') AS member_name,
                       COALESCE(gender,'') AS gender,
                       COALESCE(relationship,'') AS relation,
                       COALESCE(id_card,'') AS id_card,
                       {INCOME} AS annual_income
                FROM {PERSONS}
                WHERE family_id = $1
                ORDER BY CASE WHEN relationship = 'Head' THEN 0 ELSE 1 END, id
                LIMIT 20";
            var personsResult = await _db.QueryAsync<TempReliefMemberRow>(
                personsSql.Replace("{PERSONS}", personsTable).Replace("{INCOME}", GetPersonsAnnualIncomeExpr(personsTable)), ct, sourceFamilyId);
            if (personsResult.IsFailure)
                return Result.Failure<List<TempReliefMemberSnapshot>>(personsResult.ErrorCode!, personsResult.Message!);

            if (personsResult.Value.Count > 0)
            {
                return Result.Success(personsResult.Value.Select(m => new TempReliefMemberSnapshot
                {
                    MemberName = m.MemberName,
                    Gender = m.Gender,
                    Relation = TempReliefConstants.GetRelationshipDisplayName(m.Relation),
                    IdCard = m.IdCard,
                    WorkUnit = string.Empty,
                    AnnualIncome = m.AnnualIncome
                }).ToList());
            }
        }

        var namesSql = "SELECT COALESCE(member_names,'') AS member_names, COALESCE(member_id_cards,'') AS member_id_cards FROM " + sourceTable + " WHERE id = $1";
        var namesResult = await _db.QuerySingleAsync<TempReliefMemberNamesRow>(namesSql, ct, sourceFamilyId);
        if (namesResult.IsFailure || namesResult.Value == null)
            return Result.Success(new List<TempReliefMemberSnapshot>());

        var names = SplitSemicolon(namesResult.Value.MemberNames);
        var idCards = SplitSemicolon(namesResult.Value.MemberIdCards);
        var list = new List<TempReliefMemberSnapshot>();
        for (var i = 0; i < names.Count; i++)
        {
            list.Add(new TempReliefMemberSnapshot
            {
                MemberName = names[i],
                IdCard = i < idCards.Count ? idCards[i] : string.Empty
            });
        }

        // 台账成员仅有姓名/身份证 → 按身份证从低保申请库家庭成员明细补全（关系/性别/工作单位/年收入）
        await FillMemberDetailsFromLedgerAsync(list, ct);

        return Result.Success(list);
    }

    /// <summary>
    /// 台账来源成员按身份证从 nc_biz_family_members（经低保管反查最新有效申请）补全明细；
    /// 身份证为空或未在低保管建档的成员保持原样
    /// </summary>
    private async Task FillMemberDetailsFromLedgerAsync(List<TempReliefMemberSnapshot> members, CancellationToken ct)
    {
        var idCards = members
            .Select(m => m.IdCard)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (idCards.Count == 0) return;

        const string sql = @"
            SELECT fm.name AS member_name,
                   COALESCE(fm.gender,'') AS gender,
                   COALESCE(fm.relationship_to_head,'') AS relation,
                   COALESCE(fm.id_card,'') AS id_card,
                   COALESCE(fm.work_unit,'') AS work_unit,
                   fm.annual_income
            FROM nc_biz_family_members fm
            JOIN (
                SELECT id, applicant_id_card,
                       ROW_NUMBER() OVER (PARTITION BY applicant_id_card ORDER BY created_at DESC, id DESC) AS rn
                FROM nc_biz_applications
                WHERE applicant_id_card = ANY($1) AND deleted_at IS NULL
            ) app ON fm.application_id = app.id AND app.rn = 1
            WHERE fm.id_card = ANY($1) AND fm.deleted_at IS NULL";

        var result = await _db.QueryAsync<TempReliefMemberRow>(sql, ct, idCards);
        if (result.IsFailure || result.Value == null) return;

        var byIdCard = new Dictionary<string, TempReliefMemberRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in result.Value)
        {
            if (!string.IsNullOrWhiteSpace(row.IdCard) && !byIdCard.ContainsKey(row.IdCard))
                byIdCard[row.IdCard] = row;
        }

        foreach (var m in members)
        {
            if (string.IsNullOrWhiteSpace(m.IdCard)) continue;
            if (byIdCard.TryGetValue(m.IdCard, out var detail))
            {
                if (string.IsNullOrEmpty(m.Gender)) m.Gender = detail.Gender;
                if (string.IsNullOrEmpty(m.Relation)) m.Relation = detail.Relation;
                if (string.IsNullOrEmpty(m.WorkUnit)) m.WorkUnit = detail.WorkUnit;
                if (!m.AnnualIncome.HasValue) m.AnnualIncome = detail.AnnualIncome;
            }
        }
    }

    /// <summary>
    /// 身份证第 17 位奇偶推导性别（奇男偶女）；证件缺失/位数不足返回空串
    /// </summary>
    private static string InferGenderFromIdCard(string? idCard)
    {
        if (string.IsNullOrWhiteSpace(idCard) || idCard.Length < 17)
            return string.Empty;
        return idCard[16] % 2 == 0 ? "女" : "男";
    }

    private static List<string> SplitSemicolon(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return text.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    #endregion

    #region 默认值检索（政策救助/家庭成员状态）

    /// <summary>
    /// 按身份证检索其已享受/在申的救助政策清单（遍历 6 张白名单来源表精确匹配）
    /// 返回值如：["享受农村最低生活保障", "正在申请最低生活保障"]
    /// </summary>
    public async Task<Result<List<string>>> GetPolicySnapshotAsync(string idCard, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idCard))
            return Result.Success(new List<string>());

        var result = new List<string>();
        foreach (var table in TempReliefConstants.AllowedSourceTables)
        {
            try
            {
                var sql = "SELECT 1 FROM " + table + " WHERE applicant_id_card = $1 LIMIT 1";
                var exists = await _db.ExecuteScalarAsync<long>(sql, ct, idCard);
                if (exists.IsFailure || exists.Value <= 0) continue;

                if (table == "nc_biz_applications")
                {
                    // 低保申请库：取最新有效申请的认定分类全称
                    var classSql = @"SELECT COALESCE(classification_result,'') AS classification_result
                                     FROM nc_biz_applications
                                     WHERE applicant_id_card = $1 AND deleted_at IS NULL
                                     ORDER BY created_at DESC, id DESC LIMIT 1";
                    var cls = await _db.QuerySingleAsync<PolicyClassRow>(classSql, ct, idCard);
                    var name = cls.IsSuccess && cls.Value != null && !string.IsNullOrWhiteSpace(cls.Value.ClassificationResult)
                        ? $"正在申请{ClassificationConstants.ConvertFromCode(cls.Value.ClassificationResult)}"
                        : "正在申请最低生活保障";
                    result.Add(name);
                }
                else
                {
                    result.Add("享受" + TempReliefConstants.GetFamilyCategoryByTable(table));
                }
            }
            catch (Exception ex)
            {
                LogException(ex, $"检索政策救助情况失败（{table}）");
            }
        }

        return Result.Success(result);
    }

    /// <summary>
    /// 按身份证检索家庭成员健康状况汇总文本（源自低保管家庭成员明细）
    /// 全员健康 → "家庭成员身体健康，具备劳动能力"；有患病/残疾成员 → 逐条"成员X患Y、劳动能力…"；无数据返回空字符串
    /// </summary>
    public async Task<Result<string>> GetFamilyMemberStatusAsync(string idCard, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idCard))
            return Result.Success(string.Empty);

        try
        {
            const string sql = @"
                SELECT fm.name AS member_name,
                       fm.is_applicant,
                       COALESCE(fm.health_status,'') AS health_status,
                       COALESCE(fm.work_capacity,'') AS work_capacity,
                       COALESCE(fm.disease_name,'') AS disease_name,
                       COALESCE(fm.disability_type,'') AS disability_type,
                       COALESCE(fm.disability_level,'') AS disability_level,
                       COALESCE(fm.is_disabled,false) AS is_disabled
                FROM nc_biz_family_members fm
                JOIN (
                    SELECT id, applicant_id_card,
                           ROW_NUMBER() OVER (PARTITION BY applicant_id_card ORDER BY created_at DESC, id DESC) AS rn
                    FROM nc_biz_applications
                    WHERE applicant_id_card = $1 AND deleted_at IS NULL
                ) app ON fm.application_id = app.id AND app.rn = 1
                WHERE fm.deleted_at IS NULL
                ORDER BY fm.is_applicant DESC, fm.id
                LIMIT 20";

            var result = await _db.QueryAsync<FamilyStatusRow>(sql, ct, idCard);
            if (result.IsFailure || result.Value == null || result.Value.Count == 0)
                return Result.Success(string.Empty);

            var parts = new List<string>();
            var hasIssue = false;
            foreach (var m in result.Value)
            {
                var role = m.IsApplicant ? "户主" : "成员";
                var issues = new List<string>();

                if (!string.IsNullOrWhiteSpace(m.DisabilityType))
                {
                    var dType = _dictCache.GetValue(DictionaryTypeCodes.DisabilityTypes, m.DisabilityType);
                    var dLevel = string.IsNullOrWhiteSpace(m.DisabilityLevel)
                        ? string.Empty
                        : _dictCache.GetValue(DictionaryTypeCodes.DisabilityLevels, m.DisabilityLevel);
                    issues.Add(string.IsNullOrWhiteSpace(dLevel)
                        ? $"残疾（{dType}）"
                        : $"残疾（{dType}·{dLevel}）");
                }

                if (!string.IsNullOrWhiteSpace(m.DiseaseName))
                    issues.Add($"患{m.DiseaseName}");

                var health = string.IsNullOrWhiteSpace(m.HealthStatus)
                    ? string.Empty
                    : _dictCache.GetValue(DictionaryTypeCodes.HealthStatuses, m.HealthStatus);
                if (!string.IsNullOrWhiteSpace(health) && health != m.HealthStatus && !health.Contains("健康"))
                    issues.Add(health);

                var labor = string.IsNullOrWhiteSpace(m.WorkCapacity)
                    ? string.Empty
                    : _dictCache.GetValue(DictionaryTypeCodes.LaborAbilities, m.WorkCapacity);
                if (!string.IsNullOrWhiteSpace(labor) && labor == m.WorkCapacity)
                    labor = string.Empty; // 字典未命中时不展示代码

                if (issues.Count == 0 && string.IsNullOrWhiteSpace(labor))
                    continue; // 无特殊信息（健康/具备劳动能力）

                hasIssue = true;
                var text = $"{role}{m.MemberName}{string.Join("、", issues)}";
                if (!string.IsNullOrWhiteSpace(labor))
                    text += $"、{labor}";
                parts.Add(text);
            }

            if (!hasIssue)
                return Result.Success("家庭成员身体健康，具备劳动能力");

            return Result.Success(string.Join("；", parts) + "。");
        }
        catch (Exception ex)
        {
            LogException(ex, "检索家庭成员状态失败");
            return Result.Success(string.Empty);
        }
    }

    #endregion

    #region 小额定额档位

    public async Task<Result<List<ConfigStandard>>> GetSmallAmountLevelsAsync(CancellationToken ct = default)
    {
        var result = await _standardConfigService.GetConfigStandardsAsync(TempReliefConstants.StandardType, ct);
        if (result.IsFailure || result.Value == null)
            return Result.Failure<List<ConfigStandard>>(result.ErrorCode ?? ErrorCodes.CONFIG_NOT_FOUND, result.Message ?? "未配置小额定额档位");

        return Result.Success(result.Value.Where(s => s.IsEffective).OrderBy(s => s.StandardValue).ToList());
    }

    #endregion

    #region 查询

    public async Task<Result<TempReliefApplication>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        LogInfo($"按ID查询: {id}");
        var sql = "SELECT * FROM nc_biz_temp_relief_applications WHERE id = $1 AND deleted_at IS NULL";
        return await _db.QuerySingleAsync<TempReliefApplication>(sql, ct, id);
    }

    public async Task<Result<List<TempReliefMember>>> GetMembersByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        var sql = @"SELECT * FROM nc_biz_temp_relief_members
                    WHERE application_id = $1 AND deleted_at IS NULL
                    ORDER BY sort_order, id";
        var result = await _db.QueryAsync<TempReliefMember>(sql, ct, applicationId);
        if (result.IsFailure)
            return Result.Failure<List<TempReliefMember>>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value);
    }

    public async Task<Result<List<TempReliefDisease>>> GetDiseasesByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        var sql = @"SELECT * FROM nc_biz_temp_relief_diseases
                    WHERE application_id = $1 AND deleted_at IS NULL
                    ORDER BY sort_order, id";
        var result = await _db.QueryAsync<TempReliefDisease>(sql, ct, applicationId);
        if (result.IsFailure)
            return Result.Failure<List<TempReliefDisease>>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value);
    }

    public async Task<Result<List<TempReliefAccident>>> GetAccidentsByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        var sql = @"SELECT * FROM nc_biz_temp_relief_accidents
                    WHERE application_id = $1 AND deleted_at IS NULL
                    ORDER BY sort_order, id";
        var result = await _db.QueryAsync<TempReliefAccident>(sql, ct, applicationId);
        if (result.IsFailure)
            return Result.Failure<List<TempReliefAccident>>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value);
    }

    public async Task<Result<List<TempReliefEducation>>> GetEducationsByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        var sql = @"SELECT * FROM nc_biz_temp_relief_educations
                    WHERE application_id = $1 AND deleted_at IS NULL
                    ORDER BY sort_order, id";
        var result = await _db.QueryAsync<TempReliefEducation>(sql, ct, applicationId);
        if (result.IsFailure)
            return Result.Failure<List<TempReliefEducation>>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value);
    }

    public async Task<Result<PagedResult<TempReliefApplication>>> GetPagedAsync(string keyword, string status, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo($"分页查询: keyword={keyword}, status={status}, 第{pageIndex}页, 每页{pageSize}条");

        var conditions = new SqlConditionBuilder()
            .Add("deleted_at IS NULL")
            .AddIf(!string.IsNullOrEmpty(status), "status = {0}", status)
            .AddIf(!string.IsNullOrWhiteSpace(keyword), "(applicant_name ILIKE {0} OR applicant_id_card ILIKE {0})", $"%{keyword}%");

        var where = conditions.ToWhereClause();
        var countSql = $"SELECT COUNT(*) FROM nc_biz_temp_relief_applications{where}";
        var countResult = await _db.ExecuteScalarAsync<long>(countSql, ct, conditions.GetParameters());
        if (countResult.IsFailure)
            return Result.Failure<PagedResult<TempReliefApplication>>(countResult.ErrorCode!, countResult.Message!);

        var offset = (pageIndex - 1) * pageSize;
        var querySql = $"SELECT * FROM nc_biz_temp_relief_applications{where}" +
            $" ORDER BY created_at DESC LIMIT ${conditions.ParamCount + 1} OFFSET ${conditions.ParamCount + 2}";
        var pageParams = new List<object?>(conditions.GetParameters()) { pageSize, offset };

        var listResult = await _db.QueryAsync<TempReliefApplication>(querySql, ct, pageParams.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<TempReliefApplication>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<TempReliefApplication>.FromList(listResult.Value, pageIndex, pageSize, Convert.ToInt32(countResult.Value)));
    }

    public async Task<Result<PagedResult<TempReliefApplication>>> GetArchivedPagedAsync(string? keyword, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo($"已完结分页查询: keyword={keyword}, 第{pageIndex}页, 每页{pageSize}条");

        var conditions = new SqlConditionBuilder()
            .Add("deleted_at IS NULL")
            .Add("status IN ('Confirmed', 'Stopped')")
            .AddIf(!string.IsNullOrWhiteSpace(keyword), "(applicant_name ILIKE {0} OR applicant_id_card ILIKE {0})", $"%{keyword}%");

        var where = conditions.ToWhereClause();
        var countSql = $"SELECT COUNT(*) FROM nc_biz_temp_relief_applications{where}";
        var countResult = await _db.ExecuteScalarAsync<long>(countSql, ct, conditions.GetParameters());
        if (countResult.IsFailure)
            return Result.Failure<PagedResult<TempReliefApplication>>(countResult.ErrorCode!, countResult.Message!);

        var offset = (pageIndex - 1) * pageSize;
        var querySql = $"SELECT * FROM nc_biz_temp_relief_applications{where}" +
            $" ORDER BY updated_at DESC LIMIT ${conditions.ParamCount + 1} OFFSET ${conditions.ParamCount + 2}";
        var pageParams = new List<object?>(conditions.GetParameters()) { pageSize, offset };

        var listResult = await _db.QueryAsync<TempReliefApplication>(querySql, ct, pageParams.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<TempReliefApplication>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<TempReliefApplication>.FromList(listResult.Value, pageIndex, pageSize, Convert.ToInt32(countResult.Value)));
    }

    public async Task<Result<PagedResult<TempReliefApplication>>> GetByMonthPagedAsync(int year, int month, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo($"按月查询已完结: {year}年{month}月, 第{pageIndex}页");

        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1);

        // 业务时间口径 COALESCE(apply_date, report_time, created_at)，与名单业务时间一致；范围比较
        var conditions = new SqlConditionBuilder()
            .Add("deleted_at IS NULL")
            .Add("status IN ('Confirmed', 'Stopped')")
            .Add("COALESCE(apply_date, report_time, created_at) >= {0}", monthStart)
            .Add("COALESCE(apply_date, report_time, created_at) < {0}", monthEnd);

        var where = conditions.ToWhereClause();
        var countSql = $"SELECT COUNT(*) FROM nc_biz_temp_relief_applications{where}";
        var countResult = await _db.ExecuteScalarAsync<long>(countSql, ct, conditions.GetParameters());
        if (countResult.IsFailure)
            return Result.Failure<PagedResult<TempReliefApplication>>(countResult.ErrorCode!, countResult.Message!);

        var offset = (pageIndex - 1) * pageSize;
        var querySql = $"SELECT * FROM nc_biz_temp_relief_applications{where}" +
            $" ORDER BY updated_at DESC LIMIT ${conditions.ParamCount + 1} OFFSET ${conditions.ParamCount + 2}";
        var pageParams = new List<object?>(conditions.GetParameters()) { pageSize, offset };

        var listResult = await _db.QueryAsync<TempReliefApplication>(querySql, ct, pageParams.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<TempReliefApplication>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<TempReliefApplication>.FromList(listResult.Value, pageIndex, pageSize, Convert.ToInt32(countResult.Value)));
    }

    #endregion

    #region 写操作

    public async Task<Result<long>> CreateAsync(TempReliefApplication application, List<TempReliefMember> members, CancellationToken ct = default)
    {
        ValidateNotNull(application, nameof(application));
        ValidateNotNullOrEmpty(application.ApplicantName, nameof(application.ApplicantName));
        ValidateNotNullOrEmpty(application.ApplicantIdCard, nameof(application.ApplicantIdCard));

        LogInfo($"创建临时救助: {DataMasker.MaskName(application.ApplicantName)}, 身份证={DataMasker.MaskIdCard(application.ApplicantIdCard)}, 类型={application.ReliefType}");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);
        try
        {
            var applyDate = application.ApplyDate ?? DateTime.Today;
            var applyYear = applyDate.Year.ToString();
            application.ApplyYear = applyYear;

            var existsResult = await CheckAnnualExistsAsync(application.ApplicantIdCard, applyYear, null, ct);
            if (existsResult.IsSuccess && existsResult.Value)
                return Result.Failure<long>(ErrorCodes.ANNUAL_LIMIT_EXCEEDED, $"该申请人 {applyYear} 年已申请过临时救助，每年仅限一次");

            var appNoResult = await GetNextApplicationNoAsync(ct);
            if (appNoResult.IsFailure)
                return Result.Failure<long>(appNoResult.ErrorCode!, appNoResult.Message!);

            const string sql = @"
                 INSERT INTO nc_biz_temp_relief_applications
                (application_no, relief_type, apply_year, source_type, source_table, source_family_id,
                 applicant_name, applicant_id_card, gender, age, phone,
                 family_province, family_city, family_district, family_town, family_detail, family_address,
                 hukou_province, hukou_city, hukou_district, hukou_town, hukou_address,
                 town, village, family_size, family_category,
                 policy_enjoyed, family_member_status, difficulty_reason,
                 difficulty_type,
                 apply_date,
                 report_unit, report_time, small_amount_level, confirm_amount,
                 publicize_start_date, publicize_end_date, verify_result, acceptance_person,
                 beneficiary_name, beneficiary_id_card, beneficiary_gender, beneficiary_age, beneficiary_relation,
                 status, created_by, hukou_type, bank_name, bank_account, created_at, updated_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,
                        $18,$19,$20,$21,$22,$23,$24,$25,$26,$27,$28,$29,$30,
                        $31,$32,$33,$34,$35,$36,$37,$38,
                        $39,$40,$41,$42,$43,
                        $44,$45,$46,$47,$48,$49,NOW(), NOW())
                RETURNING id";

            var insertResult = await _db.ExecuteScalarAsync<long>(sql, ct,
                appNoResult.Value,
                application.ReliefType, application.ApplyYear,
                application.SourceType, application.SourceTable, application.SourceFamilyId,
                application.ApplicantName, application.ApplicantIdCard,
                application.Gender ?? "", (object?)application.Age, application.Phone ?? "",
                application.FamilyProvince ?? "", application.FamilyCity ?? "",
                application.FamilyDistrict ?? "", application.FamilyTown ?? "",
                application.FamilyDetail ?? "", application.FamilyAddress ?? "",
                application.HukouProvince ?? "", application.HukouCity ?? "",
                application.HukouDistrict ?? "", application.HukouTown ?? "",
                application.HukouAddress ?? "",
                application.Town ?? "", application.Village ?? "",
                (object?)application.FamilySize, application.FamilyCategory ?? "",
                application.PolicyEnjoyed ?? "", application.FamilyMemberStatus ?? "",
                application.DifficultyReason ?? "",
                application.DifficultyType ?? "",
                applyDate,
                application.ReportUnit ?? "", application.ReportTime,
                application.SmallAmountLevel ?? "", (object?)application.ConfirmAmount,
                application.PublicizeStartDate, application.PublicizeEndDate,
                application.VerifyResult ?? "", application.AcceptancePerson ?? "",
                application.BeneficiaryName ?? "", application.BeneficiaryIdCard ?? "",
                application.BeneficiaryGender ?? "", (object?)application.BeneficiaryAge,
                application.BeneficiaryRelation ?? "",
                application.Status ?? TempReliefConstants.StatusDraft,
                application.CreatedBy ?? "System",
                application.HukouType ?? "",
                application.BankName ?? "", application.BankAccount ?? "");

            if (insertResult.IsFailure)
                return Result.Failure<long>(insertResult.ErrorCode!, insertResult.Message!);

            var newId = insertResult.Value;

            if (members != null && members.Count > 0)
            {
                var memberResult = await ReplaceMembersAsync(newId, members, ct);
                if (memberResult.IsFailure)
                    return Result.Failure<long>(memberResult.ErrorCode!, memberResult.Message!);
            }

            var diseaseResult = await ReplaceDiseasesAsync(newId, application.Diseases, ct);
            if (diseaseResult.IsFailure)
                return Result.Failure<long>(diseaseResult.ErrorCode!, diseaseResult.Message!);

            var accidentResult = await ReplaceAccidentsAsync(newId, application.Accidents, ct);
            if (accidentResult.IsFailure)
                return Result.Failure<long>(accidentResult.ErrorCode!, accidentResult.Message!);

            var educationResult = await ReplaceEducationsAsync(newId, application.Educations, ct);
            if (educationResult.IsFailure)
                return Result.Failure<long>(educationResult.ErrorCode!, educationResult.Message!);

            await tx.CommitAsync(ct);

            Logger.LogBusiness("创建临时救助申请",
                ("ApplicationId", newId),
                ("ApplicationNo", appNoResult.Value),
                ("ReliefType", application.ReliefType),
                ("Name", DataMasker.MaskName(application.ApplicantName)));
            return Result.Success(newId);
        }
        catch (Exception ex)
        {
            LogException(ex, "创建临时救助申请失败");
            return Result.FromException<long>(ex);
        }
    }

    public async Task<Result> UpdateAsync(TempReliefApplication application, List<TempReliefMember> members, CancellationToken ct = default)
    {
        ValidateNotNull(application, nameof(application));

        LogInfo($"更新临时救助: id={application.Id}, 姓名={DataMasker.MaskName(application.ApplicantName)}");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);
        try
        {
            var applyDate = application.ApplyDate ?? DateTime.Today;
            var applyYear = applyDate.Year.ToString();
            application.ApplyYear = applyYear;

            var existsResult = await CheckAnnualExistsAsync(application.ApplicantIdCard, applyYear, application.Id, ct);
            if (existsResult.IsSuccess && existsResult.Value)
                return Result.Failure(ErrorCodes.ANNUAL_LIMIT_EXCEEDED, $"该申请人 {applyYear} 年已申请过临时救助，每年仅限一次");

            const string sql = @"
                 UPDATE nc_biz_temp_relief_applications SET
                 relief_type = $1, apply_year = $2, source_type = $3, source_table = $4, source_family_id = $5,
                 applicant_name = $6, applicant_id_card = $7, gender = $8, age = $9, phone = $10,
                 family_province = $11, family_city = $12, family_district = $13, family_town = $14,
                 family_detail = $15, family_address = $16,
                 hukou_province = $17, hukou_city = $18, hukou_district = $19, hukou_town = $20,
                 hukou_address = $21,
                 town = $22, village = $23, family_size = $24, family_category = $25,
                 policy_enjoyed = $26, family_member_status = $27, difficulty_reason = $28,
                 difficulty_type = $29,
                 apply_date = $30,
                 report_unit = $31, report_time = $32, small_amount_level = $33, confirm_amount = $34,
                 publicize_start_date = $35, publicize_end_date = $36, verify_result = $37, acceptance_person = $38,
                 beneficiary_name = $39, beneficiary_id_card = $40, beneficiary_gender = $41,
                 beneficiary_age = $42, beneficiary_relation = $43,
                 hukou_type = $44, bank_name = $45, bank_account = $46,
                 updated_by = $47, updated_at = NOW()
                 WHERE id = $48 AND deleted_at IS NULL";

             var updateResult = await _db.ExecuteNonQueryAsync(sql, ct,
                 application.ReliefType, application.ApplyYear,
                 application.SourceType, application.SourceTable, application.SourceFamilyId,
                 application.ApplicantName, application.ApplicantIdCard,
                 application.Gender ?? "", (object?)application.Age, application.Phone ?? "",
                 application.FamilyProvince ?? "", application.FamilyCity ?? "",
                 application.FamilyDistrict ?? "", application.FamilyTown ?? "",
                 application.FamilyDetail ?? "", application.FamilyAddress ?? "",
                 application.HukouProvince ?? "", application.HukouCity ?? "",
                 application.HukouDistrict ?? "", application.HukouTown ?? "",
                 application.HukouAddress ?? "",
                 application.Town ?? "", application.Village ?? "",
                 (object?)application.FamilySize, application.FamilyCategory ?? "",
                 application.PolicyEnjoyed ?? "", application.FamilyMemberStatus ?? "",
                 application.DifficultyReason ?? "",
                 application.DifficultyType ?? "",
                 applyDate,
                 application.ReportUnit ?? "", application.ReportTime,
                 application.SmallAmountLevel ?? "", (object?)application.ConfirmAmount,
                 application.PublicizeStartDate, application.PublicizeEndDate,
                 application.VerifyResult ?? "", application.AcceptancePerson ?? "",
                 application.BeneficiaryName ?? "", application.BeneficiaryIdCard ?? "",
                 application.BeneficiaryGender ?? "", (object?)application.BeneficiaryAge,
                 application.BeneficiaryRelation ?? "",
                 application.HukouType ?? "",
                 application.BankName ?? "", application.BankAccount ?? "",
                 application.UpdatedBy ?? "System",
                 application.Id);

            if (updateResult.IsFailure)
                return Result.Failure(updateResult.ErrorCode!, updateResult.Message!);
            if (updateResult.Value == 0)
                return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "申请记录不存在或已删除");

            var memberResult = await ReplaceMembersAsync(application.Id, members, ct);
            if (memberResult.IsFailure)
                return Result.Failure(memberResult.ErrorCode!, memberResult.Message!);

            var diseaseResult = await ReplaceDiseasesAsync(application.Id, application.Diseases, ct);
            if (diseaseResult.IsFailure)
                return Result.Failure(diseaseResult.ErrorCode!, diseaseResult.Message!);

            var accidentResult = await ReplaceAccidentsAsync(application.Id, application.Accidents, ct);
            if (accidentResult.IsFailure)
                return Result.Failure(accidentResult.ErrorCode!, accidentResult.Message!);

            var educationResult = await ReplaceEducationsAsync(application.Id, application.Educations, ct);
            if (educationResult.IsFailure)
                return Result.Failure(educationResult.ErrorCode!, educationResult.Message!);

            await tx.CommitAsync(ct);
            Logger.LogBusiness("更新临时救助申请", ("ApplicationId", application.Id));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "更新临时救助申请失败");
            return Result.FromException(ex);
        }
    }

    public async Task<Result> ConfirmAsync(long id, string operatorName, CancellationToken ct = default)
    {
        LogInfo($"确认临时救助: id={id}, operator={operatorName}");

        var sql = @"UPDATE nc_biz_temp_relief_applications SET
                    status = $1, confirmed_at = NOW(), confirmed_by = $2, updated_at = NOW()
                    WHERE id = $3 AND deleted_at IS NULL AND status = 'Draft'";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, TempReliefConstants.StatusConfirmed, operatorName ?? "System", id);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        if (result.Value == 0)
            return Result.Failure(ErrorCodes.INVALID_TRANSITION, "仅草稿状态可确认");

        Logger.LogBusiness("确认临时救助申请", ("ApplicationId", id));
        return Result.Success();
    }

    public async Task<Result> StopAsync(long id, string reason, string operatorName, CancellationToken ct = default)
    {
        LogInfo($"终止临时救助: id={id}, operator={operatorName}");

        var sql = @"UPDATE nc_biz_temp_relief_applications SET
                    status = $1, stopped_at = NOW(), stopped_by = $2, stop_reason = $3, updated_at = NOW()
                    WHERE id = $4 AND deleted_at IS NULL AND status = 'Confirmed'";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, TempReliefConstants.StatusStopped, operatorName ?? "System", reason ?? string.Empty, id);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        if (result.Value == 0)
            return Result.Failure(ErrorCodes.INVALID_TRANSITION, "仅已确认状态可终止");

        Logger.LogBusiness("终止临时救助申请", ("ApplicationId", id), ("Reason", reason ?? string.Empty));
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(long id, CancellationToken ct = default)
    {
        LogInfo($"删除临时救助: id={id}");

        var sql = @"UPDATE nc_biz_temp_relief_applications SET
                    deleted_at = NOW(), updated_at = NOW()
                    WHERE id = $1 AND deleted_at IS NULL AND status = 'Draft'";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, id);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        if (result.Value == 0)
            return Result.Failure(ErrorCodes.INVALID_TRANSITION, "仅草稿状态可删除");

        Logger.LogBusiness("删除临时救助申请", ("ApplicationId", id));
        return Result.Success();
    }

    public async Task<Result<bool>> CheckAnnualExistsAsync(string idCard, string applyYear, long? excludeId = null, CancellationToken ct = default)
    {
        ValidateNotNullOrEmpty(idCard, nameof(idCard));
        ValidateNotNullOrEmpty(applyYear, nameof(applyYear));

        string sql;
        object[] parameters;

        if (excludeId.HasValue)
        {
            sql = @"SELECT COUNT(*) FROM nc_biz_temp_relief_applications
                    WHERE applicant_id_card = $1 AND apply_year = $2 AND id != $3 AND deleted_at IS NULL";
            parameters = new object[] { idCard, applyYear, excludeId.Value };
        }
        else
        {
            sql = @"SELECT COUNT(*) FROM nc_biz_temp_relief_applications
                    WHERE applicant_id_card = $1 AND apply_year = $2 AND deleted_at IS NULL";
            parameters = new object[] { idCard, applyYear };
        }

        var result = await _db.ExecuteScalarAsync<long>(sql, ct, parameters);
        if (result.IsFailure)
            return Result.Failure<bool>(result.ErrorCode!, result.Message!);

        return Result.Success(result.Value > 0);
    }

    #endregion

    #region 私有辅助

    /// <summary>
    /// 生成申请编号：LZ{yyyyMMdd}{seq:D4}
    /// </summary>
    private async Task<Result<string>> GetNextApplicationNoAsync(CancellationToken ct = default)
    {
        var prefix = $"{TempReliefConstants.ApplicationNoPrefix}{DateTime.Now:yyyyMMdd}";

        var sql = @"SELECT COALESCE(MAX(CAST(SUBSTRING(application_no FROM 11) AS INTEGER)), 0) + 1
                    FROM nc_biz_temp_relief_applications
                    WHERE application_no LIKE $1";
        var result = await _db.ExecuteScalarAsync<long>(sql, ct, $"{prefix}%");
        if (result.IsFailure)
            return Result.Failure<string>(result.ErrorCode!, result.Message!);

        var seq = result.Value;
        var appNo = $"{prefix}{seq:D4}";
        return Result.Success(appNo);
    }

    /// <summary>
    /// 重建家庭成员明细（先删后插，保留户主在前）
    /// </summary>
    private async Task<Result> ReplaceMembersAsync(long applicationId, List<TempReliefMember> members, CancellationToken ct)
    {
        var deleteResult = await _db.ExecuteNonQueryAsync(
            "DELETE FROM nc_biz_temp_relief_members WHERE application_id = $1", ct, applicationId);
        if (deleteResult.IsFailure)
            return Result.Failure(deleteResult.ErrorCode!, deleteResult.Message!);

        if (members == null || members.Count == 0)
            return Result.Success();

        const string sql = @"
            INSERT INTO nc_biz_temp_relief_members
            (application_id, member_name, gender, relation, id_card, work_unit, annual_income, sort_order, created_at, updated_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,NOW(),NOW())";

        var sorted = members
            .Select((m, idx) => new { Member = m, Index = idx })
            .OrderByDescending(x => string.Equals(x.Member.MemberName, string.Empty, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(x => x.Index)
            .ToList();

        foreach (var item in sorted)
        {
            var m = item.Member;
            var result = await _db.ExecuteNonQueryAsync(sql, ct,
                applicationId, m.MemberName, m.Gender ?? "", m.Relation ?? "", m.IdCard ?? "",
                m.WorkUnit ?? "", (object?)m.AnnualIncome, item.Index);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);
        }

        return Result.Success();
    }

    private static DateTime? OrNull(DateTime d) => d == default ? null : d;

    private async Task<Result> ReplaceDiseasesAsync(long applicationId, List<TempReliefDisease> diseases, CancellationToken ct)
    {
        var deleteResult = await _db.ExecuteNonQueryAsync(
            "DELETE FROM nc_biz_temp_relief_diseases WHERE application_id = $1", ct, applicationId);
        if (deleteResult.IsFailure)
            return Result.Failure(deleteResult.ErrorCode!, deleteResult.Message!);

        if (diseases == null || diseases.Count == 0)
            return Result.Success();

        const string sql = @"
            INSERT INTO nc_biz_temp_relief_diseases
            (application_id, disease_name, disease_code, member_name, hospital, treat_start_date, treat_end_date,
             medical_total, insurance_paid, self_paid, sort_order, created_at, updated_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,NOW(),NOW())";

        var sorted = diseases
            .Select((d, idx) => new { Item = d, Index = idx })
            .OrderByDescending(x => string.IsNullOrWhiteSpace(x.Item.DiseaseName) ? 0 : 1)
            .ThenBy(x => x.Index)
            .ToList();

        foreach (var item in sorted)
        {
            var d = item.Item;
            var result = await _db.ExecuteNonQueryAsync(sql, ct,
                applicationId, d.DiseaseName ?? "", d.DiseaseCode ?? "", d.MemberName ?? "", d.Hospital ?? "",
                OrNull(d.TreatStartDate), OrNull(d.TreatEndDate),
                (object?)d.MedicalTotal, (object?)d.InsurancePaid, (object?)d.SelfPaid, item.Index);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);
        }

        return Result.Success();
    }

    private async Task<Result> ReplaceAccidentsAsync(long applicationId, List<TempReliefAccident> accidents, CancellationToken ct)
    {
        var deleteResult = await _db.ExecuteNonQueryAsync(
            "DELETE FROM nc_biz_temp_relief_accidents WHERE application_id = $1", ct, applicationId);
        if (deleteResult.IsFailure)
            return Result.Failure(deleteResult.ErrorCode!, deleteResult.Message!);

        if (accidents == null || accidents.Count == 0)
            return Result.Success();

        const string sql = @"
            INSERT INTO nc_biz_temp_relief_accidents
            (application_id, accident_type, member_name, happen_date, happen_place, injury_situation,
             property_loss, compensation_paid, responsibility_desc, material_desc, sort_order, created_at, updated_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,NOW(),NOW())";

        var sorted = accidents
            .Select((a, idx) => new { Item = a, Index = idx })
            .OrderByDescending(x => string.IsNullOrWhiteSpace(x.Item.AccidentType) ? 0 : 1)
            .ThenBy(x => x.Index)
            .ToList();

        foreach (var item in sorted)
        {
            var a = item.Item;
            var result = await _db.ExecuteNonQueryAsync(sql, ct,
                applicationId, a.AccidentType ?? "", a.MemberName ?? "", OrNull(a.HappenDate), a.HappenPlace ?? "", a.InjurySituation ?? "",
                (object?)a.PropertyLoss, (object?)a.CompensationPaid,
                a.ResponsibilityDesc ?? "", a.MaterialDesc ?? "", item.Index);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);
        }

        return Result.Success();
    }

    private async Task<Result> ReplaceEducationsAsync(long applicationId, List<TempReliefEducation> educations, CancellationToken ct)
    {
        var deleteResult = await _db.ExecuteNonQueryAsync(
            "DELETE FROM nc_biz_temp_relief_educations WHERE application_id = $1", ct, applicationId);
        if (deleteResult.IsFailure)
            return Result.Failure(deleteResult.ErrorCode!, deleteResult.Message!);

        if (educations == null || educations.Count == 0)
            return Result.Success();

        const string sql = @"
            INSERT INTO nc_biz_temp_relief_educations
            (application_id, student_name, education_stage, school_name,
             tuition_fee, fee_date, school_duration, sort_order, created_at, updated_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,NOW(),NOW())";

        var sorted = educations
            .Select((e, idx) => new { Item = e, Index = idx })
            .OrderByDescending(x => string.IsNullOrWhiteSpace(x.Item.StudentName) ? 0 : 1)
            .ThenBy(x => x.Index)
            .ToList();

        foreach (var item in sorted)
        {
            var e = item.Item;
            var result = await _db.ExecuteNonQueryAsync(sql, ct,
                applicationId, e.StudentName ?? "", e.EducationStage ?? "", e.SchoolName ?? "",
                (object?)e.TuitionFee, OrNull(e.FeeDate), (object?)e.SchoolDuration, item.Index);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);
        }

        return Result.Success();
    }

    #endregion
}

/// <summary>
/// 候选人查询行（各来源表统一列）
/// </summary>
public class TempReliefCandidateRow
{
    public long SourceFamilyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string DetailAddress { get; set; } = string.Empty;
    public string FamilyAddress { get; set; } = string.Empty;
    public string HukouAddress { get; set; } = string.Empty;
    public string Town { get; set; } = string.Empty;
    public string Village { get; set; } = string.Empty;
    public int? FamilySize { get; set; }
    public string HukouType { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string BankAccount { get; set; } = string.Empty;
    public string OneCardAccount { get; set; } = string.Empty;

    public TempReliefCandidate ToCandidate(string sourceTable) => new()
    {
        SourceFamilyId = SourceFamilyId,
        SourceTable = sourceTable,
        HukouType = HukouType,
        Name = Name,
        IdCard = IdCard,
        Gender = Gender,
        Phone = Phone,
        Province = Province,
        City = City,
        District = District,
        DetailAddress = DetailAddress,
        FamilyAddress = FamilyAddress,
        HukouAddress = HukouAddress,
        Town = Town,
        Village = Village,
        FamilySize = FamilySize,
        BankName = BankName,
        BankAccount = BankAccount,
        OneCardAccount = OneCardAccount
    };
}

/// <summary>
/// 家庭成员查询行（低保申请库）
/// </summary>
public class TempReliefMemberRow
{
    public string MemberName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string Relation { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string WorkUnit { get; set; } = string.Empty;
    public decimal? AnnualIncome { get; set; }
}

/// <summary>
/// 导入台账家庭表成员名单查询行
/// </summary>
public class TempReliefMemberNamesRow
{
    public string MemberNames { get; set; } = string.Empty;
    public string MemberIdCards { get; set; } = string.Empty;
}

/// <summary>
/// 低保申请库认定分类查询行
/// </summary>
public class PolicyClassRow
{
    public string ClassificationResult { get; set; } = string.Empty;
}

/// <summary>
/// 地址库镇名查询行（村→镇反查）
/// </summary>
public class TownNameRow
{
    public string TownName { get; set; } = string.Empty;
}

/// <summary>
/// 台账成员明细户主性别查询行
/// </summary>
public class GenderRow
{
    public string Gender { get; set; } = string.Empty;
}

/// <summary>
/// 家庭成员健康状况查询行（状态汇总用）
/// </summary>
public class FamilyStatusRow
{
    public string MemberName { get; set; } = string.Empty;
    public bool IsApplicant { get; set; }
    public string HealthStatus { get; set; } = string.Empty;
    public string WorkCapacity { get; set; } = string.Empty;
    public string DiseaseName { get; set; } = string.Empty;
    public string DisabilityType { get; set; } = string.Empty;
    public string DisabilityLevel { get; set; } = string.Empty;
    public bool IsDisabled { get; set; }
}
