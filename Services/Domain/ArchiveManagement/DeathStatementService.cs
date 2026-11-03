using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.DeathStatement;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.UserManagement;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <inheritdoc />
public partial class DeathStatementService : BaseService, IDeathStatementService
{
    protected override string ServiceName => "DeathStatementService";

    private readonly IDatabaseService _db;
    private readonly IOrganizationService _organizationService;
    private readonly IDictCacheService _dictCache;
    private readonly DeathIdentityAggregator _identityAggregator;

    public DeathStatementService(
        IDatabaseService db,
        IOrganizationService organizationService,
        IDictCacheService dictCache,
        DeathIdentityAggregator identityAggregator,
        ILoggerService logger)
        : base(logger)
    {
        _db = db;
        _organizationService = organizationService;
        _dictCache = dictCache;
        _identityAggregator = identityAggregator;
    }

    /// <summary>字典显示名（key 空 → 空串；主文件档案信息回填用）</summary>
    private string DictText(string category, string? key) =>
        string.IsNullOrEmpty(key) ? string.Empty : _dictCache.GetValue(category, key);

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

    /// <summary>归属判定三方法（InScope/ArchiveInScope/TextInScope）已移入 VillageScopeResolver
    ///（2026-11 身份聚合器与申报服务共用，避免聚合器反向依赖服务）。</summary>

    /// <summary>申报台账 SQL 归属片段（参数固定 $1 = 范围ID）</summary>
    private static string ScopeDeclarationSql(VillageScope scope)
        => scope.IsTownScope ? "town_id = $1" : "village_id = $1";

    /// <summary>档案 SQL 归属片段（$1 = 范围ID，$2 = 归一化名称）</summary>
    private static string ScopeApplicationSql(VillageScope scope, string alias)
        => scope.IsTownScope
            ? $"({alias}.hukou_town_id = $1 OR {alias}.town_id = $1 OR {VillageScopeResolver.NameMatchSql($"{alias}.town", 2)})"
            : $"({alias}.hukou_village_id = $1 OR {alias}.village_id = $1 OR {VillageScopeResolver.NameMatchSql($"{alias}.community", 2)})";

    #endregion

    #region 文书上下文（身份八源 + 提醒 + 联动）

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
                ImportSourceLabel = item.ImportSourceLabel ?? string.Empty,
                VillageName = string.IsNullOrWhiteSpace(item.VillageName) ? scope.VillageName : item.VillageName,
                TownName = scope.TownName
            };
            context.DeathDateTimeText = FormatDeathDateTime(context.DeathDateText, context.DeathTimeText);

            // ① 死亡事实补全：申报行可能带更多信息（地点/证明编号/申报人其余字段）
            var declareResult = await LoadDeclarationAsync(context, ct);
            if (declareResult.IsFailure)
                return Result.Failure<DeathStatementContext>(declareResult.ErrorCode!, declareResult.Message!);

            // ② 身份八源聚合（仅本村命中，跨村只计数）——2026-11 拆入独立服务 DeathIdentityAggregator
            var identityResult = await _identityAggregator.AggregateIdentitiesAsync(context.IdCard, scope, ct);
            if (identityResult.IsFailure)
                return Result.Failure<DeathStatementContext>(identityResult.ErrorCode!, identityResult.Message!);
            context.Identities = identityResult.Value.Items;
            context.CrossVillageCount = identityResult.Value.CrossVillageCount;

            // ③ 档案信息回填（住址/分类/关联档案）
            await FillArchiveInfoAsync(context, scope, ct);

            // ④ 数据提醒（12 条规则）
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
                           COALESCE(a.district,'') AS district,
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
                       VillageScopeResolver.ArchiveInScope(scope, r.VillageId, r.HukouVillageId, r.TownId, r.HukouTownId, r.Community, r.Town))
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
        // 地址统一走文本级唯一入口（县+镇+村+门牌，已含则跳过；原 Town+Community+Address 裸拼会重复）
        context.Address = AddressResolver.BuildAddressFromText(picked.District, picked.Town, picked.Community, picked.Address);
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

    #region 行映射类（snake_case → PascalCase 由映射层完成）

    private sealed class DeclareInsertRow
    {
        public long Id { get; set; }
        public DateTime DeclaredAt { get; set; }
    }

    /// <summary>死亡联动待办行（snake_case → PascalCase 由映射层完成；is_done = 目标域实时判定）</summary>
    private sealed class LinkageTaskRow
    {
        public long Id { get; set; }
        public long DeclarationId { get; set; }
        public string? DeceasedIdCard { get; set; }
        public string? DeceasedName { get; set; }
        public string? TaskDomain { get; set; }
        public string? PermissionCode { get; set; }
        public long? RefApplicationId { get; set; }
        public long? RefElderlyId { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? DeathDateText { get; set; }
        public string? DeathTimeText { get; set; }
        public string? DeathReason { get; set; }
        public string? DeathCertNo { get; set; }
        public bool IsDone { get; set; }
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

    private sealed class HeadDeathStateRow
    {
        public bool HasNew { get; set; }
        public bool StopOnly { get; set; }
    }

    private sealed class ArchiveInfoRow
    {
        public long ApplicationId { get; set; }
        public string? ClassificationResult { get; set; }
        public string? Community { get; set; }
        public string? Town { get; set; }
        public string? District { get; set; }
        public string? Address { get; set; }
        public string? Status { get; set; }
        public string? Gender { get; set; }
        public string? Ethnicity { get; set; }
        public int? VillageId { get; set; }
        public int? HukouVillageId { get; set; }
        public int? TownId { get; set; }
        public int? HukouTownId { get; set; }
    }

    #endregion
}
