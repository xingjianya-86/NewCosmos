using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.DeathStatement;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.UserManagement;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <inheritdoc />
public class DeathStatementService : BaseService, IDeathStatementService
{
    protected override string ServiceName => "DeathStatementService";

    private readonly IDatabaseService _db;
    private readonly IOrganizationService _organizationService;
    private readonly IDictCacheService _dictCache;

    public DeathStatementService(
        IDatabaseService db,
        IOrganizationService organizationService,
        IDictCacheService dictCache,
        ILoggerService logger)
        : base(logger)
    {
        _db = db;
        _organizationService = organizationService;
        _dictCache = dictCache;
    }

    #region 本村范围

    /// <inheritdoc />
    public async Task<Result<VillageScope>> ResolveScopeAsync(CancellationToken ct = default)
    {
        try
        {
            var orgId = App.CurrentUserOrganizationId;
            if (orgId is null or <= 0)
                return Result.Failure<VillageScope>(ErrorCodes.VALIDATION_FAILED, "当前账号未配置组织单位，无法按村查询");

            var orgResult = await _organizationService.GetByIdAsync(orgId.Value, ct);
            if (orgResult.IsFailure || orgResult.Value == null)
                return Result.Failure<VillageScope>(orgResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    orgResult.Message ?? "查询所属组织单位失败");

            var org = orgResult.Value;
            var orgName = string.IsNullOrWhiteSpace(org.Name) ? (org.VillageName ?? org.TownName ?? string.Empty) : org.Name;
            var orgPhone = org.Phone ?? string.Empty;

            // ① 村级：账号绑了村 → 严格按本村
            if (org.VillageId is > 0 && !string.IsNullOrWhiteSpace(org.VillageName))
            {
                var coreName = VillageScopeResolver.Normalize(org.VillageName);
                if (coreName.Length > 0)
                {
                    return Result.Success(new VillageScope
                    {
                        IsTownScope = false,
                        VillageId = org.VillageId.Value,
                        VillageName = org.VillageName.Trim(),
                        VillageCoreName = coreName,
                        TownId = org.TownId,
                        TownName = org.TownName ?? string.Empty,
                        ProvinceName = org.ProvinceName ?? string.Empty,
                        CityName = org.CityName ?? string.Empty,
                        DistrictName = org.CountyName ?? string.Empty,
                        OrgName = orgName,
                        OrgPhone = orgPhone
                    });
                }
            }

            // ② 镇级降级：只绑到乡镇（如镇民政办）→ 按全镇过滤，功能对镇级账号可用
            if (org.TownId is > 0 && !string.IsNullOrWhiteSpace(org.TownName))
            {
                LogInfo($"账号未绑定村社区，降级为镇级范围: {org.TownName}");
                return Result.Success(new VillageScope
                {
                    IsTownScope = true,
                    VillageId = 0,
                    VillageName = string.Empty,
                    VillageCoreName = string.Empty,
                    TownId = org.TownId,
                    TownName = org.TownName.Trim(),
                    ProvinceName = org.ProvinceName ?? string.Empty,
                    CityName = org.CityName ?? string.Empty,
                    DistrictName = org.CountyName ?? string.Empty,
                    OrgName = orgName,
                    OrgPhone = orgPhone
                });
            }

            // ③ 两级都没有 → 显式失败（禁止静默放开全库）
            return Result.Failure<VillageScope>(ErrorCodes.VALIDATION_FAILED,
                "当前账号既未绑定村/社区也未绑定乡镇，请联系管理员在「经办机构管理」补齐区划后再使用");
        }
        catch (Exception ex)
        {
            LogException(ex, "ResolveScopeAsync");
            return Result.FromException<VillageScope>(ex);
        }
    }

    /// <summary>
    /// 归属判定单点：**村级**按 village_id / community 文本；**镇级降级**按 town_id / town 文本。
    /// 无任何归属字段 → 不可判定 → 计入当前范围（规范 §3.1）。
    /// </summary>
    private static bool InScope(VillageScope scope, int? villageId, int? hukouVillageId,
        int? townId, int? hukouTownId, string? community, string? townText)
    {
        if (scope.IsTownScope)
        {
            if (townId is > 0) return townId.Value == scope.TownId;
            if (hukouTownId is > 0) return hukouTownId.Value == scope.TownId;

            var town = VillageScopeResolver.Normalize(townText);
            return town.Length == 0
                || string.Equals(town, VillageScopeResolver.Normalize(scope.TownName), StringComparison.OrdinalIgnoreCase);
        }

        if (villageId is > 0) return villageId.Value == scope.VillageId;
        if (hukouVillageId is > 0) return hukouVillageId.Value == scope.VillageId;

        var core = VillageScopeResolver.Normalize(community);
        return core.Length == 0
            || string.Equals(core, scope.VillageCoreName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>档案类表归属（现住址 village → 户籍 village → community 文本；镇级同理用 town 三档）</summary>
    private static bool ArchiveInScope(VillageScope scope, int? villageId, int? hukouVillageId,
        int? townId, int? hukouTownId, string? community, string? townText)
        => InScope(scope, villageId, hukouVillageId, townId, hukouTownId, community, townText);

    /// <summary>纯文本归属表（临时救助：village/town 都是文本）</summary>
    private static bool TextInScope(VillageScope scope, string? villageText, string? townText)
        => InScope(scope, null, null, null, null, villageText, townText);

    /// <summary>申报台账 SQL 归属片段（参数固定 $1 = 范围ID）</summary>
    private static string ScopeDeclarationSql(VillageScope scope)
        => scope.IsTownScope ? "town_id = $1" : "village_id = $1";

    /// <summary>档案 SQL 归属片段（$1 = 范围ID，$2 = 归一化名称）</summary>
    private static string ScopeApplicationSql(VillageScope scope, string alias)
        => scope.IsTownScope
            ? $"({alias}.hukou_town_id = $1 OR {alias}.town_id = $1 OR {VillageScopeResolver.NameMatchSql($"{alias}.town", 2)})"
            : $"({alias}.hukou_village_id = $1 OR {alias}.village_id = $1 OR {VillageScopeResolver.NameMatchSql($"{alias}.community", 2)})";

    #endregion

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
                       COALESCE(d.operator_name, '') AS declared_by_name,
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

            foreach (var item in items)
            {
                item.HasDeclaration = item.DeclarationId.HasValue;
                item.StatusName = DeathDeclarationConstants.GetStatusName(item.Status);
                item.Badge = !item.HasDeclaration
                    ? "未申报"
                    : item.Status == DeathDeclarationConstants.StatusRevoked ? "已作废" : "已申报";
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

    #endregion

    #region 文书上下文（身份七源 + 提醒 + 联动）

    /// <inheritdoc />
    public async Task<Result<DeathStatementContext>> BuildContextAsync(
        VillageScope scope, DeathSearchItem item, CancellationToken ct = default)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.IdCard))
            return Result.Failure<DeathStatementContext>(ErrorCodes.VALIDATION_FAILED, "请先选择死亡人员");

        try
        {
            var context = new DeathStatementContext
            {
                Name = item.Name,
                IdCard = item.IdCard.Trim(),
                DeathDateText = item.DeathDateText,
                DeathTimeText = item.DeathTimeText,
                DeathReason = item.DeathReason,
                DeathCertNo = item.DeathCertNo,
                Relation = item.Relation,
                DeclarationId = item.DeclarationId,
                DeathRecordId = item.DeathRecordId,
                ApplicationId = item.ApplicationId,
                DeclaredByName = item.DeclaredByName,
                DeclaredByType = item.DeclaredByType,
                DeclaredAtText = item.DeclaredAtText,
                Status = item.Status,
                StatusName = item.StatusName,
                HasDeclaration = item.DeclarationId.HasValue,
                VillageName = string.IsNullOrWhiteSpace(item.VillageName) ? scope.VillageName : item.VillageName,
                TownName = scope.TownName
            };
            context.DeathDateTimeText = FormatDeathDateTime(context.DeathDateText, context.DeathTimeText);

            // ① 死亡事实补全：申报行可能带更多信息（地点/证明编号/申报人其余字段）
            var declareResult = await LoadDeclarationAsync(context, ct);
            if (declareResult.IsFailure)
                return Result.Failure<DeathStatementContext>(declareResult.ErrorCode!, declareResult.Message!);

            // ② 身份七源聚合（仅本村命中，跨村只计数）
            var identityResult = await GetIdentitiesAsync(context.IdCard, scope, ct);
            if (identityResult.IsFailure)
                return Result.Failure<DeathStatementContext>(identityResult.ErrorCode!, identityResult.Message!);
            context.Identities = identityResult.Value.Items;
            context.CrossVillageCount = identityResult.Value.CrossVillageCount;

            // ③ 档案信息回填（住址/分类/关联档案）
            await FillArchiveInfoAsync(context, scope, ct);

            // ④ 数据提醒（11 条规则）
            var reminderResult = await BuildRemindersAsync(context, ct);
            if (reminderResult.IsFailure)
                return Result.Failure<DeathStatementContext>(reminderResult.ErrorCode!, reminderResult.Message!);
            context.Reminders = reminderResult.Value;

            // ⑤ 跨流程联动卡片（只读视图 + 预填）
            var linkResult = await BuildLinksAsync(context, ct);
            if (linkResult.IsFailure)
                return Result.Failure<DeathStatementContext>(linkResult.ErrorCode!, linkResult.Message!);
            context.Links = linkResult.Value;

            LogInfo($"文书上下文构建完成: {DataMasker.MaskName(context.Name)} 身份 {context.Identities.Count} 条 / " +
                    $"提醒 {context.Reminders.Count} 条 / 联动 {context.Links.Count} 条 / 跨村剔除 {context.CrossVillageCount}");
            return Result.Success(context);
        }
        catch (Exception ex)
        {
            LogException(ex, "BuildContextAsync");
            return Result.FromException<DeathStatementContext>(ex);
        }
    }

    /// <summary>死亡事实与申报字段补全（无申报行时保持手工录入值）</summary>
    private async Task<Result> LoadDeclarationAsync(DeathStatementContext context, CancellationToken ct)
    {
        if (context.DeclarationId.HasValue)
        {
            var sql = @"SELECT id AS declaration_id, application_id, deceased_name AS name,
                               deceased_id_card AS id_card, deceased_relation AS declared_relation,
                               death_date, TO_CHAR(death_time,'HH24:MI') AS death_time_text,
                               COALESCE(death_reason,'') AS death_reason,
                               COALESCE(death_certificate_no,'') AS death_cert_no,
                               COALESCE(death_place,'') AS death_place,
                               declared_by_name, COALESCE(declared_by_id_card,'') AS declared_by_id_card,
                               COALESCE(declared_by_phone,'') AS declared_by_phone, declared_by_type,
                               TO_CHAR(declared_at,'YYYY-MM-DD HH24:MI') AS declared_at_text,
                               status, COALESCE(village_name,'') AS village_name,
                               COALESCE(reporter_name,'') AS reporter_name,
                               COALESCE(reporter_relation,'') AS reporter_relation,
                               COALESCE(reporter_phone,'') AS reporter_phone,
                               COALESCE(situation_desc,'') AS situation_desc,
                               COALESCE(remains_arrangement,'') AS remains_arrangement,
                               COALESCE(business_agent,'') AS business_agent,
                               COALESCE(business_matter,'') AS business_matter
                        FROM nc_biz_death_declarations
                        WHERE id = $1 AND deleted_at IS NULL";
            var row = await _db.QuerySingleAsync<DeathStatementRow>(sql, ct, context.DeclarationId.Value);
            if (row.IsFailure)
                return Result.Failure(row.ErrorCode!, row.Message!);
            if (row.Value == null)
                return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "该申报记录已不存在");

            var d = row.Value;
            context.Name = string.IsNullOrWhiteSpace(d.Name) ? context.Name : d.Name;
            context.DeathDateText = d.DeathDate?.ToString("yyyy-MM-dd") ?? context.DeathDateText;
            context.DeathTimeText = d.DeathTimeText ?? context.DeathTimeText;
            context.DeathDateTimeText = FormatDeathDateTime(context.DeathDateText, context.DeathTimeText);
            context.DeathReason = d.DeathReason ?? string.Empty;
            context.DeathCertNo = d.DeathCertNo ?? string.Empty;
            context.DeathPlace = d.DeathPlace ?? string.Empty;
            context.DeclaredRelation = d.DeclaredRelation ?? string.Empty;
            context.DeclaredByName = d.DeclaredByName ?? string.Empty;
            context.DeclaredByIdCard = d.DeclaredByIdCard ?? string.Empty;
            context.DeclaredByPhone = d.DeclaredByPhone ?? string.Empty;
            context.DeclaredByType = d.DeclaredByType ?? string.Empty;
            context.DeclaredAtText = d.DeclaredAtText ?? string.Empty;
            context.Status = d.Status ?? string.Empty;
            context.StatusName = DeathDeclarationConstants.GetStatusName(d.Status);
            context.ReporterName = d.ReporterName ?? string.Empty;
            context.ReporterRelation = d.ReporterRelation ?? string.Empty;
            context.ReporterPhone = d.ReporterPhone ?? string.Empty;
            context.SituationDesc = d.SituationDesc ?? string.Empty;
            context.RemainsArrangement = d.RemainsArrangement ?? string.Empty;
            context.BusinessAgent = d.BusinessAgent ?? string.Empty;
            context.BusinessMatter = d.BusinessMatter ?? string.Empty;
            context.ApplicationId = context.ApplicationId ?? d.ApplicationId;
            if (!string.IsNullOrWhiteSpace(d.VillageName)) context.VillageName = d.VillageName;
        }
        else if (context.DeathRecordId.HasValue)
        {
            // 档案死亡登记候选：历史行可能没有确切时刻（death_time IS NULL → 进提醒）
            var sql = @"SELECT d.id AS death_record_id, d.application_id, d.member_name AS name,
                               d.member_id_card AS id_card, d.death_date,
                               COALESCE(TO_CHAR(d.death_time,'HH24:MI'),'') AS death_time_text,
                               COALESCE(d.death_reason,'') AS death_reason,
                               COALESCE(d.death_certificate_no,'') AS death_cert_no,
                               COALESCE(d.relationship_to_head,'') AS relation
                        FROM nc_biz_death_records d WHERE d.id = $1";
            var row = await _db.QuerySingleAsync<DeathStatementRow>(sql, ct, context.DeathRecordId.Value);
            if (row.IsFailure)
                return Result.Failure(row.ErrorCode!, row.Message!);
            if (row.Value == null)
                return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "该死亡登记记录已不存在");

            var d = row.Value;
            context.DeathDateText = d.DeathDate?.ToString("yyyy-MM-dd") ?? context.DeathDateText;
            context.DeathTimeText = d.DeathTimeText ?? context.DeathTimeText;
            context.DeathDateTimeText = FormatDeathDateTime(context.DeathDateText, context.DeathTimeText);
            context.DeathReason = d.DeathReason ?? string.Empty;
            context.DeathCertNo = d.DeathCertNo ?? string.Empty;
            context.Relation = d.Relation ?? string.Empty;
            context.ApplicationId = context.ApplicationId ?? d.ApplicationId;
        }

        return Result.Success();
    }

    /// <summary>档案信息回填：住址 / 分类 / 关联档案ID</summary>
    private async Task FillArchiveInfoAsync(DeathStatementContext context, VillageScope scope, CancellationToken ct)
    {
        var sql = @"SELECT a.id AS application_id, a.classification_result,
                           COALESCE(a.community,'') AS community, COALESCE(a.town,'') AS town,
                           COALESCE(a.address,'') AS address, COALESCE(a.status,'') AS status,
                           COALESCE(a.gender,'') AS gender, COALESCE(a.ethnicity,'') AS ethnicity,
                           a.village_id, a.hukou_village_id, a.town_id, a.hukou_town_id
                    FROM nc_biz_applications a
                    WHERE a.deleted_at IS NULL
                      AND (a.applicant_id_card = $1
                           OR EXISTS (SELECT 1 FROM nc_biz_family_members m
                                      WHERE m.application_id = a.id AND m.id_card = $1 AND m.deleted_at IS NULL))
                    ORDER BY a.id DESC
                    LIMIT 5";

        var rows = await _db.QueryAsync<ArchiveInfoRow>(sql, ct, context.IdCard);
        if (rows.IsFailure || rows.Value == null || rows.Value.Count == 0)
            return;

        ArchiveInfoRow? picked = null;
        if (context.ApplicationId.HasValue)
            picked = rows.Value.FirstOrDefault(r => r.ApplicationId == context.ApplicationId.Value);
        picked ??= rows.Value.FirstOrDefault(r =>
                       ArchiveInScope(scope, r.VillageId, r.HukouVillageId, r.TownId, r.HukouTownId, r.Community, r.Town))
                   ?? rows.Value[0];

        context.ApplicationId = picked.ApplicationId;
        context.Classification = string.IsNullOrEmpty(picked.ClassificationResult)
            ? string.Empty
            : ClassificationConstants.ConvertToFullName(picked.ClassificationResult);
        // 性别/民族：核实说明模板字段（民族是字典 key，转显示名；性别档案值可能为"男/女"原样）
        context.Gender = picked.Gender ?? string.Empty;
        context.Ethnicity = string.IsNullOrEmpty(picked.Ethnicity)
            ? string.Empty
            : (string.Equals(picked.Ethnicity, "男", StringComparison.Ordinal) || string.Equals(picked.Ethnicity, "女", StringComparison.Ordinal)
                ? picked.Ethnicity
                : DictText(DictionaryTypeCodes.Ethnicities, picked.Ethnicity));
        if (!string.IsNullOrWhiteSpace(picked.Town)) context.TownName = picked.Town;
        context.Address = BuildAddress(picked.Town, picked.Community, picked.Address);
    }

    private static string BuildAddress(string? town, string? community, string? address)
    {
        var parts = new[] { town?.Trim(), community?.Trim(), address?.Trim() }
            .Where(p => !string.IsNullOrEmpty(p));
        return string.Join("", parts);
    }

    /// <summary>死亡时间整段拼装（单点：date + time → 「yyyy年M月d日 HH:mm」）</summary>
    public static string FormatDeathDateTime(string? deathDateText, string? deathTimeText)
    {
        if (string.IsNullOrWhiteSpace(deathDateText))
            return string.Empty;

        var dateText = deathDateText;
        if (DateTime.TryParse(deathDateText, out var date))
            dateText = $"{date.Year}年{date.Month}月{date.Day}日";

        return string.IsNullOrWhiteSpace(deathTimeText) ? dateText : $"{dateText} {deathTimeText}";
    }

    #endregion

    #region 身份七源聚合

    /// <summary>身份聚合结果（本村命中 + 跨村计数）</summary>
    private sealed class IdentityResult
    {
        public List<DeathIdentityItem> Items { get; } = new();
        public int CrossVillageCount { get; set; }
    }

    /// <summary>
    /// 七源聚合：低收入人口 / 普惠高龄 / 残疾人 / 大学生 / 临时救助 / 近亲属备案 / 追缴在办。
    /// 各源查询按身份证命中（= ANY 不适用于单值，直接等值 + LIMIT），跨村记录只计数不进表。
    /// 无村字段的记录视为不可判定 → 计入本村（见 MatchScope 注释）。
    /// </summary>
    private async Task<Result<IdentityResult>> GetIdentitiesAsync(string idCard, VillageScope scope, CancellationToken ct)
    {
        var result = new IdentityResult();

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
            return Result.Failure<IdentityResult>(archives.ErrorCode!, $"身份聚合失败（低收入人口源）：{archives.Message}");

        foreach (var a in archives.Value ?? [])
        {
            var inVillage = ArchiveInScope(scope, a.VillageId, a.HukouVillageId, a.TownId, a.HukouTownId, a.Community, a.TownText);
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
            return Result.Failure<IdentityResult>(elderly.ErrorCode!, $"身份聚合失败（普惠高龄源）：{elderly.Message}");

        foreach (var e in elderly.Value ?? [])
        {
            var inVillage = InScope(scope, e.FamilyVillageId, e.HukouVillageId, e.FamilyTownId, e.HukouTownId, null, null);
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
            return Result.Failure<IdentityResult>(disabilities.ErrorCode!, $"身份聚合失败（残疾人源）：{disabilities.Message}");

        foreach (var d in disabilities.Value ?? [])
        {
            var inVillage = ArchiveInScope(scope, d.VillageId, d.HukouVillageId, d.TownId, d.HukouTownId, d.Community, d.TownText);
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
            return Result.Failure<IdentityResult>(colleges.ErrorCode!, $"身份聚合失败（大学生源）：{colleges.Message}");

        foreach (var c in colleges.Value ?? [])
        {
            var inVillage = ArchiveInScope(scope, c.VillageId, c.HukouVillageId, c.TownId, c.HukouTownId, c.Community, c.TownText);
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
            return Result.Failure<IdentityResult>(temps.ErrorCode!, $"身份聚合失败（临时救助源）：{temps.Message}");

        foreach (var t in temps.Value ?? [])
        {
            var inVillage = TextInScope(scope, t.Village, t.Town);
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
            return Result.Failure<IdentityResult>(nears.ErrorCode!, $"身份聚合失败（近亲属备案源）：{nears.Message}");

        foreach (var n in nears.Value ?? [])
        {
            var inVillage = !n.ApplicationId.HasValue
                ? true
                : ArchiveInScope(scope, n.VillageId, n.HukouVillageId, n.TownId, n.HukouTownId, n.Community, n.TownText);
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
            return Result.Failure<IdentityResult>(recoveries.ErrorCode!, $"身份聚合失败（后补追缴源）：{recoveries.Message}");

        foreach (var r in recoveries.Value ?? [])
        {
            result.Items.Add(new DeathIdentityItem
            {
                SourceKey = "Recovery",
                TypeLabel = "后补追缴",
                StatusLabel = RecoveryStatusName(r.Status),
                Period = string.IsNullOrEmpty(r.StartMonth) ? string.Empty : $"{r.StartMonth} ~ {r.EndMonth}",
                SourceLabel = "后补追缴",
                Note = r.RecoveryAmount > 0 ? $"应追缴 {r.RecoveryAmount:F2} 元" : string.Empty,
                ApplicationId = r.SourceId,
                SourceRecordId = r.Id,
                InVillage = true
            });
        }

        return Result.Success(result);
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

    #endregion

    #region 数据提醒（11 条规则）

    private async Task<Result<List<DeathReminderItem>>> BuildRemindersAsync(
        DeathStatementContext context, CancellationToken ct)
    {
        var list = new List<DeathReminderItem>();

        // R1 高龄在享且未登记死亡 → 需办停发
        var elderlyActive = context.Identities.Any(i =>
            i.SourceKey == "Elderly" && i.StatusLabel == ElderlyBenefitConstants.GetStatusName(ElderlyBenefitConstants.StatusConfirmed));
        if (elderlyActive)
        {
            list.Add(new DeathReminderItem
            {
                Level = "警告", Code = "ELDERLY_ACTIVE",
                Message = "该人员的普惠高龄津贴仍为「已确认」在享状态，且尚未登记死亡",
                SuggestAction = "前往「高龄津贴发放 → 停发登记」办理（原因选「去世」）", NavKey = "ElderlyStop"
            });
        }

        // R2 低收入档案在享但未登记死亡 → 需办减员/户主死亡变更
        var hasDeathRecord = await HasDeathRecordAsync(context.IdCard, ct);
        var archiveActive = context.Identities.Any(i =>
            i.SourceKey == "Archive" && (i.StatusLabel == "在享" || i.StatusLabel == "已提交" || i.StatusLabel == "已办结"));
        if (archiveActive && !hasDeathRecord)
        {
            var isHead = context.Identities.Any(i => i.SourceKey == "Archive" && i.Note == "户主");
            list.Add(new DeathReminderItem
            {
                Level = "警告", Code = "ARCHIVE_NOT_CHANGED",
                Message = isHead
                    ? "该人员为在享档案户主，档案尚未办理户主死亡变更（停旧建新）"
                    : "该人员在享档案尚未办理成员减员（减员原因：人员死亡）",
                SuggestAction = "前往「保障对象动态管理」办理变更", NavKey = isHead ? "HouseholdDeath" : "MemberChange"
            });
        }

        // R3 缺死亡证明编号
        if (string.IsNullOrWhiteSpace(context.DeathCertNo))
        {
            list.Add(new DeathReminderItem
            {
                Level = "警告", Code = "NO_CERT_NO",
                Message = "死亡证明编号为空，无法凭编号核验死亡事实",
                SuggestAction = "补录死亡证明编号后重新出具", NavKey = "None"
            });
        }

        // R4 疑似重复申报（同身份证存在多条有效申报）
        var validDeclareCount = await CountValidDeclarationsAsync(context.IdCard, ct);
        if (validDeclareCount > 1)
        {
            list.Add(new DeathReminderItem
            {
                Level = "警告", Code = "DUPLICATE_DECLARE",
                Message = $"该死者存在 {validDeclareCount} 条有效申报记录，疑似重复申报",
                SuggestAction = "核对后作废多余申报", NavKey = "None"
            });
        }

        // R5 跨村数据（只计数，不输出明细）
        if (context.CrossVillageCount > 0)
        {
            list.Add(new DeathReminderItem
            {
                Level = "警告", Code = "CROSS_VILLAGE",
                Message = $"另有 {context.CrossVillageCount} 条身份记录挂在其他村社区，已排除出本说明",
                SuggestAction = "如属本村，请到档案/区划中修正村字段", NavKey = "None"
            });
        }

        // R6 大学生仍在读
        if (context.Identities.Any(i => i.SourceKey == "College" &&
                i.StatusLabel == CollegeStudentConstants.GetStatusDisplay(CollegeStudentConstants.StatusStudying)))
        {
            list.Add(new DeathReminderItem
            {
                Level = "提示", Code = "COLLEGE_STUDYING",
                Message = "关联的在校大学生档案状态仍为「在读」",
                SuggestAction = "前往「大学生管理」更新状态", NavKey = "College"
            });
        }

        // R7 追缴未结案
        if (context.Identities.Any(i => i.SourceKey == "Recovery" &&
                i.StatusLabel == RecoveryStatusName(RecoveryConstants.STATUS_DRAFT)))
        {
            list.Add(new DeathReminderItem
            {
                Level = "提示", Code = "RECOVERY_OPEN",
                Message = "该人员存在办理中的后补追缴记录",
                SuggestAction = "前往「后补追缴」确认是否结案", NavKey = "Recovery"
            });
        }

        // R8 残疾信息缺失
        if (context.Identities.Any(i => i.SourceKey == "Disability" &&
                (i.StatusLabel == "等级未登记" || i.Period == "残疾证号缺失")))
        {
            list.Add(new DeathReminderItem
            {
                Level = "提示", Code = "DISABILITY_INCOMPLETE",
                Message = "残疾人档案的等级或残疾证号不完整",
                SuggestAction = "在档案成员信息中补全残疾信息", NavKey = "None"
            });
        }

        // R9 身份证校验不通过
        if (!IdCardValidator.IsValid(context.IdCard))
        {
            list.Add(new DeathReminderItem
            {
                Level = "警告", Code = "IDCARD_INVALID",
                Message = $"身份证号校验不通过（{context.IdCard}），跨表身份聚合可能不完整",
                SuggestAction = "核对并更正身份证号", NavKey = "None"
            });
        }

        // R10 库内无身份命中
        if (context.Identities.Count == 0 && context.CrossVillageCount == 0)
        {
            list.Add(new DeathReminderItem
            {
                Level = "提示", Code = "NO_IDENTITY",
                Message = "系统内未查到该人员的任何在册身份记录",
                SuggestAction = "可正常出具情况说明（文书将注明系统无记录）", NavKey = "None"
            });
        }

        // R11 死亡确切时刻缺失（历史登记未采集）
        if (string.IsNullOrWhiteSpace(context.DeathTimeText))
        {
            list.Add(new DeathReminderItem
            {
                Level = "警告", Code = "NO_DEATH_TIME",
                Message = "该死亡记录未采集确切时刻（历史数据），文书将只到日期",
                SuggestAction = "提交一条新申报补录确切时刻", NavKey = "Declare"
            });
        }

        return Result.Success(list);
    }

    private async Task<bool> HasDeathRecordAsync(string idCard, CancellationToken ct)
    {
        var sql = "SELECT COUNT(*) FROM nc_biz_death_records WHERE member_id_card = $1";
        var result = await _db.ExecuteScalarAsync<long>(sql, ct, idCard);
        if (result.IsFailure)
        {
            LogWarn($"死亡登记存在性查询失败: {result.Message}");
            return false;
        }
        return result.Value > 0;
    }

    private async Task<int> CountValidDeclarationsAsync(string idCard, CancellationToken ct)
    {
        var sql = @"SELECT COUNT(*) FROM nc_biz_death_declarations
                    WHERE deceased_id_card = $1 AND deleted_at IS NULL AND status <> 'Revoked'";
        var result = await _db.ExecuteScalarAsync<long>(sql, ct, idCard);
        if (result.IsFailure)
        {
            LogWarn($"有效申报计数查询失败: {result.Message}");
            return 0;
        }
        return (int)result.Value;
    }

    #endregion

    #region 跨流程联动（只读视图 + 预填跳转）

    private async Task<Result<List<DeathLinkAction>>> BuildLinksAsync(
        DeathStatementContext context, CancellationToken ct)
    {
        var links = new List<DeathLinkAction>();
        var deathDate = context.DeathDateText;
        var deathTime = context.DeathTimeText;

        // ① 普惠高龄在享 → 停发登记（追缴金额只提示不自动算，由经办在停发页核定）
        var elderly = context.Identities.FirstOrDefault(i => i.SourceKey == "Elderly" &&
            i.StatusLabel == ElderlyBenefitConstants.GetStatusName(ElderlyBenefitConstants.StatusConfirmed));
        if (elderly != null)
        {
            links.Add(new DeathLinkAction
            {
                Domain = "Elderly",
                Title = "普惠高龄在享 · 需办理停发",
                StateText = "停发原因应选「去世」，死亡日期/时刻已预填；是否追缴多发金额由经办人工核定",
                IsDone = false, ActionText = "去办理", NavKey = "ElderlyStop",
                ElderlyApplicationId = elderly.SourceRecordId,
                DeathDateText = deathDate, DeathTimeText = deathTime,
                DeathReason = context.DeathReason, DeathCertNo = context.DeathCertNo
            });
        }
        else if (context.Identities.Any(i => i.SourceKey == "Elderly"))
        {
            links.Add(new DeathLinkAction
            {
                Domain = "Elderly", Title = "普惠高龄津贴", StateText = "已停发（非在享）",
                IsDone = true, ActionText = "已完成", NavKey = "None"
            });
        }

        // ② 低收入档案 → 户主死亡变更 / 成员减员
        var archiveRows = context.Identities.Where(i => i.SourceKey == "Archive").ToList();
        if (archiveRows.Count > 0)
        {
            var hasDeathRecord = await HasDeathRecordAsync(context.IdCard, ct);
            var isHead = archiveRows.Any(i => i.Note == "户主");

            if (isHead)
            {
                var headDone = await IsHeadDeathDoneAsync(context.IdCard, ct);
                links.Add(new DeathLinkAction
                {
                    Domain = "ArchiveHead",
                    Title = "在享档案户主 · 需办理户主死亡变更（停旧建新）",
                    StateText = headDone ? "已生成新档案（停旧建新完成）" : "旧档案未停止、新档案未生成",
                    IsDone = headDone, ActionText = headDone ? "已完成" : "去办理",
                    NavKey = headDone ? "None" : "HouseholdDeath",
                    ApplicationId = context.ApplicationId,
                    DeathDateText = deathDate, DeathTimeText = deathTime,
                    DeathReason = context.DeathReason, DeathCertNo = context.DeathCertNo
                });
            }
            else
            {
                links.Add(new DeathLinkAction
                {
                    Domain = "ArchiveMember",
                    Title = "在享档案成员 · 需办理减员（原因：人员死亡）",
                    StateText = hasDeathRecord ? "已登记死亡记录" : "尚未登记死亡/减员",
                    IsDone = hasDeathRecord, ActionText = hasDeathRecord ? "已完成" : "去办理",
                    NavKey = hasDeathRecord ? "None" : "MemberChange",
                    ApplicationId = context.ApplicationId,
                    DeathDateText = deathDate, DeathTimeText = deathTime,
                    DeathReason = context.DeathReason, DeathCertNo = context.DeathCertNo
                });
            }
        }

        // ③ 大学生在读 → 退出登记
        var collegeStudying = context.Identities.Any(i => i.SourceKey == "College" &&
            i.StatusLabel == CollegeStudentConstants.GetStatusDisplay(CollegeStudentConstants.StatusStudying));
        if (collegeStudying || context.Identities.Any(i => i.SourceKey == "College"))
        {
            links.Add(new DeathLinkAction
            {
                Domain = "College",
                Title = "在校大学生档案",
                StateText = collegeStudying ? "状态仍为在读" : "状态已更新",
                IsDone = !collegeStudying, ActionText = collegeStudying ? "去办理" : "已完成",
                NavKey = collegeStudying ? "College" : "None"
            });
        }

        // ④ 追缴在办 → 结案确认
        var recoveryOpen = context.Identities.FirstOrDefault(i => i.SourceKey == "Recovery" &&
            i.StatusLabel == RecoveryStatusName(RecoveryConstants.STATUS_DRAFT));
        links.Add(new DeathLinkAction
        {
            Domain = "Recovery",
            Title = "后补追缴记录",
            StateText = recoveryOpen != null ? "办理中，需确认是否结案" : "无办理中的追缴记录",
            IsDone = recoveryOpen == null, ActionText = recoveryOpen == null ? "无需处理" : "去办理",
            NavKey = recoveryOpen == null ? "None" : "Recovery"
        });

        // ⑤ 近亲属备案对象 → 备案编辑
        var near = context.Identities.FirstOrDefault(i => i.SourceKey == "NearRelative");
        if (near != null)
        {
            links.Add(new DeathLinkAction
            {
                Domain = "NearRelative",
                Title = "近亲属备案对象",
                StateText = "备案对象已死亡，需更新/注销备案",
                IsDone = false, ActionText = "去办理", NavKey = "NearRelative",
                ApplicationId = near.ApplicationId
            });
        }

        return Result.Success(links);
    }

    private async Task<bool> IsHeadDeathDoneAsync(string idCard, CancellationToken ct)
    {
        var sql = @"SELECT COUNT(*) FROM nc_biz_death_records
                    WHERE member_id_card = $1 AND new_application_id IS NOT NULL";
        var result = await _db.ExecuteScalarAsync<long>(sql, ct, idCard);
        if (result.IsFailure)
        {
            LogWarn($"户主死亡完成态查询失败: {result.Message}");
            return false;
        }
        return result.Value > 0;
    }

    #endregion

    #region 申报台账读写

    /// <summary>首页横幅：单条 SQL 判定本村有效申报中至少有一项未办联动的数量</summary>
    public async Task<Result<int>> GetPendingLinkageCountAsync(CancellationToken ct = default)
    {
        try
        {
            var scopeResult = await ResolveScopeAsync(ct);
            if (scopeResult.IsFailure)
            {
                LogWarn($"横幅计数跳过（范围解析失败）: {scopeResult.Message}");
                return Result.Success(0);
            }
            var scope = scopeResult.Value;

            var activeArchiveStatuses = new[]
            {
                ApplicationStatusCodes.SUBMITTED, ApplicationStatusCodes.APPROVED, ApplicationStatusCodes.COMPLETED
            };
            var openRecoveryStatuses = new[] { RecoveryConstants.STATUS_DRAFT };

            // 归属片段按范围层级分叉；本查询里 $1=范围ID、$4=范围名称（$2/$3/$5/$6 为各状态常量）
            var declScope = ScopeDeclarationSql(scope);
            var elderlyScope = scope.IsTownScope
                ? "(e.family_town_id = $1 OR e.hukou_town_id = $1)"
                : "(e.family_village_id = $1 OR e.hukou_village_id = $1)";
            var appScope = ScopeApplicationSql(scope, "a").Replace("$2", "$4");

            var sql = @"
                WITH d AS (
                    SELECT id, deceased_id_card
                    FROM nc_biz_death_declarations
                    WHERE " + declScope + @" AND deleted_at IS NULL AND status <> 'Revoked'
                      AND declared_at >= NOW() - INTERVAL '12 months'
                    ORDER BY declared_at DESC
                    LIMIT 200
                )
                SELECT COUNT(*) FROM d
                WHERE EXISTS (SELECT 1 FROM nc_biz_elderly_applications e
                              WHERE e.id_card = d.deceased_id_card
                                AND e.status = $2 AND e.deleted_at IS NULL
                                AND " + elderlyScope + @")
                   OR EXISTS (SELECT 1 FROM nc_biz_applications a
                              WHERE a.deleted_at IS NULL AND a.status = ANY($3)
                                AND " + appScope + @"
                                AND (a.applicant_id_card = d.deceased_id_card
                                     OR EXISTS (SELECT 1 FROM nc_biz_family_members m
                                                WHERE m.application_id = a.id AND m.id_card = d.deceased_id_card
                                                  AND m.deleted_at IS NULL))
                                AND NOT EXISTS (SELECT 1 FROM nc_biz_death_records r
                                                WHERE r.member_id_card = d.deceased_id_card))
                   OR EXISTS (SELECT 1 FROM nc_biz_college_students c
                              WHERE c.id_card = d.deceased_id_card AND c.status = $5 AND c.deleted_at IS NULL)
                   OR EXISTS (SELECT 1 FROM nc_biz_recovery_records rc
                              WHERE rc.id_card = d.deceased_id_card AND rc.status = ANY($6))";

            var result = await _db.ExecuteScalarAsync<long>(sql, ct,
                ScopeId(scope),
                ElderlyBenefitConstants.StatusConfirmed,
                activeArchiveStatuses,
                ScopeName(scope),
                CollegeStudentConstants.StatusStudying,
                openRecoveryStatuses);

            if (result.IsFailure)
                return Result.Failure<int>(result.ErrorCode!, result.Message!);
            return Result.Success((int)result.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "GetPendingLinkageCountAsync");
            return Result.FromException<int>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<int>> CountRecentValidAsync(string idCard, DateTime deathDate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idCard))
            return Result.Success(0);

        try
        {
            var countSql = @"SELECT COUNT(*) FROM nc_biz_death_declarations
                             WHERE deceased_id_card = $1 AND deleted_at IS NULL AND status <> 'Revoked'
                               AND death_date >= $2 AND death_date < $3";
            var countResult = await _db.ExecuteScalarAsync<long>(countSql, ct,
                idCard.Trim(), deathDate.Date.AddDays(-DeathDeclarationConstants.DuplicateWindowDays),
                deathDate.Date.AddDays(DeathDeclarationConstants.DuplicateWindowDays + 1));
            if (countResult.IsFailure)
                return Result.Failure<int>(countResult.ErrorCode!, countResult.Message!);
            return Result.Success((int)countResult.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "CountRecentValidAsync");
            return Result.FromException<int>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<DeathDeclareResult>> DeclareAsync(DeathDeclareRequest request, CancellationToken ct = default)
    {
        if (request == null)
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "申报内容不能为空");
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "请填写死者姓名");
        if (string.IsNullOrWhiteSpace(request.IdCard))
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "请填写死者身份证号");
        if (!IdCardValidator.IsValid(request.IdCard.Trim()))
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "死者身份证号校验不通过，请核对后重新填写");
        if (request.DeathDate == default)
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "请选择死亡日期");
        if (request.DeathDate.Date > DateTime.Today)
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "死亡日期不能晚于今天");
        if (string.IsNullOrWhiteSpace(request.DeclaredByName))
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "请填写申报人姓名");
        if (string.IsNullOrWhiteSpace(request.DeclaredByType))
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "请选择申报人类型");

        try
        {
            var idCard = request.IdCard.Trim();

            // 同身份证窗口期内的有效申报计数（提交前已由调用方弹确认，此处回传供提示）
            var recentResult = await CountRecentValidAsync(idCard, request.DeathDate, ct);
            if (recentResult.IsFailure)
                return Result.Failure<DeathDeclareResult>(recentResult.ErrorCode!, recentResult.Message!);
            var recentValid = recentResult.Value;

            var insertSql = @"INSERT INTO nc_biz_death_declarations
                    (deceased_name, deceased_id_card, deceased_relation,
                     death_date, death_time, death_reason, death_certificate_no, death_place,
                     declared_by_name, declared_by_id_card, declared_by_phone, declared_by_type,
                     declared_at, declare_channel, status,
                     application_id, village_id, town_id, village_name,
                     operator_name, remark, created_by, created_at,
                     reporter_name, reporter_relation, reporter_phone,
                     situation_desc, remains_arrangement, business_agent, business_matter)
                  VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12, NOW(), $13, $14, $15, $16, $17, $18, $19, $20, $19, NOW(),
                          $21,$22,$23,$24,$25,$26,$27)
                  RETURNING id, declared_at";

            var row = await _db.QuerySingleAsync<DeclareInsertRow>(insertSql, ct,
                request.Name.Trim(),
                idCard,
                NullIfEmpty(request.Relation),
                request.DeathDate.Date,
                TimeSpan.FromHours(request.DeathTime.Hours).Add(TimeSpan.FromMinutes(request.DeathTime.Minutes)),
                NullIfEmpty(request.DeathReason),
                NullIfEmpty(request.DeathCertNo),
                NullIfEmpty(request.DeathPlace),
                request.DeclaredByName.Trim(),
                NullIfEmpty(request.DeclaredByIdCard),
                NullIfEmpty(request.DeclaredByPhone),
                request.DeclaredByType,
                string.IsNullOrWhiteSpace(request.Channel) ? DeathDeclarationConstants.ChannelPc : request.Channel,
                DeathDeclarationConstants.StatusPending,
                request.ApplicationId,
                request.VillageId,
                request.TownId,
                NullIfEmpty(request.VillageName),
                request.OperatorName,
                NullIfEmpty(request.Remark),
                NullIfEmpty(request.ReporterName),
                NullIfEmpty(request.ReporterRelation),
                NullIfEmpty(request.ReporterPhone),
                NullIfEmpty(request.SituationDesc),
                NullIfEmpty(request.RemainsArrangement),
                NullIfEmpty(request.BusinessAgent),
                NullIfEmpty(request.BusinessMatter));

            if (row.IsFailure || row.Value == null)
                return Result.Failure<DeathDeclareResult>(row.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    row.Message ?? "申报写入失败");

            Logger.LogBusiness("死亡申报提交",
                ("Deceased", DataMasker.MaskName(request.Name)),
                ("IdCard", DataMasker.MaskIdCard(idCard)),
                ("DeathDate", request.DeathDate.ToString("yyyy-MM-dd")),
                ("DeclaredBy", DataMasker.MaskName(request.DeclaredByName)),
                ("Type", request.DeclaredByType),
                ("Channel", request.Channel ?? "PC"));

            return Result.Success(new DeathDeclareResult
            {
                Id = row.Value.Id,
                DeclaredAt = row.Value.DeclaredAt,
                RecentValidCount = recentValid
            });
        }
        catch (Exception ex)
        {
            LogException(ex, "DeclareAsync");
            return Result.FromException<DeathDeclareResult>(ex);
        }
    }

    private static object? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <inheritdoc />
    public async Task<Result> VerifyAsync(long declarationId, CancellationToken ct = default)
    {
        try
        {
            var sql = @"UPDATE nc_biz_death_declarations
                        SET status = $1, updated_at = NOW()
                        WHERE id = $2 AND deleted_at IS NULL AND status <> $1";
            var result = await _db.ExecuteNonQueryAsync(sql, ct,
                DeathDeclarationConstants.StatusVerified, declarationId);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);
            if (result.Value <= 0)
                return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "申报记录不存在或已核实");

            Logger.LogBusiness("死亡申报核实", ("DeclarationId", declarationId.ToString()));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "VerifyAsync");
            return Result.FromException(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result> RevokeAsync(long declarationId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "请填写作废原因");

        try
        {
            var sql = @"UPDATE nc_biz_death_declarations
                        SET status = $1,
                            remark = COALESCE(remark, '') || CASE WHEN COALESCE(remark,'') = '' THEN '' ELSE E'\n' END
                                     || '[作废] ' || $3,
                            updated_at = NOW()
                        WHERE id = $2 AND deleted_at IS NULL";
            var result = await _db.ExecuteNonQueryAsync(sql, ct,
                DeathDeclarationConstants.StatusRevoked, declarationId, reason.Trim());
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);
            if (result.Value <= 0)
                return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "申报记录不存在");

            Logger.LogBusiness("死亡申报作废",
                ("DeclarationId", declarationId.ToString()), ("Reason", reason.Trim()));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "RevokeAsync");
            return Result.FromException(ex);
        }
    }

    #endregion

    #region 行映射类（snake_case → PascalCase 由映射层完成）

    private sealed class DeclareInsertRow
    {
        public long Id { get; set; }
        public DateTime DeclaredAt { get; set; }
    }

    private sealed class DeathStatementRow
    {
        public long? DeclarationId { get; set; }
        public long? DeathRecordId { get; set; }
        public long? ApplicationId { get; set; }
        public string? Name { get; set; }
        public string? IdCard { get; set; }
        public string? DeclaredRelation { get; set; }
        public DateTime? DeathDate { get; set; }
        public string? DeathTimeText { get; set; }
        public string? DeathReason { get; set; }
        public string? DeathCertNo { get; set; }
        public string? DeathPlace { get; set; }
        public string? DeclaredByName { get; set; }
        public string? DeclaredByIdCard { get; set; }
        public string? DeclaredByPhone { get; set; }
        public string? DeclaredByType { get; set; }
        public string? DeclaredAtText { get; set; }
        public string? Status { get; set; }
        public string? VillageName { get; set; }
        public string? TownName { get; set; }
        public string? Relation { get; set; }
        public string? ReporterName { get; set; }
        public string? ReporterRelation { get; set; }
        public string? ReporterPhone { get; set; }
        public string? SituationDesc { get; set; }
        public string? RemainsArrangement { get; set; }
        public string? BusinessAgent { get; set; }
        public string? BusinessMatter { get; set; }
    }

    private sealed class ArchiveInfoRow
    {
        public long ApplicationId { get; set; }
        public string? ClassificationResult { get; set; }
        public string? Community { get; set; }
        public string? Town { get; set; }
        public string? Address { get; set; }
        public string? Status { get; set; }
        public string? Gender { get; set; }
        public string? Ethnicity { get; set; }
        public int? VillageId { get; set; }
        public int? HukouVillageId { get; set; }
        public int? TownId { get; set; }
        public int? HukouTownId { get; set; }
    }

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

    #endregion
}
