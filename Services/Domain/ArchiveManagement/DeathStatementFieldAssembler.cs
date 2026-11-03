using NewCosmos.Constants;
using NewCosmos.Models.DeathStatement;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 字段装配输入 DTO（2026-11 死亡说明简化·3b 下沉）：把原 BuildStatementFields/BuildManualContext
/// 的 20+ 散参收拢为一个不可变快照，装配逻辑与 VM 解耦（VM 只负责采集表单值与组装本 DTO）。
/// </summary>
public sealed class DeathStatementFieldInput
{
    // ── 表单值（「表单优先、context 兜底」的表单侧；null/空白 → 取 Context 对应键）──
    public string? Name { get; init; }
    public string? IdCard { get; init; }
    public DateTime? DeathDateTime { get; init; }
    public string? DeathReason { get; init; }
    public string? DeathCertNo { get; init; }
    public string? DeathPlace { get; init; }
    public string? Relation { get; init; }
    public string? DeclaredRelation { get; init; }
    public string? DeclaredVillage { get; init; }
    public string? DeathGender { get; init; }
    public string? DeathEthnicity { get; init; }
    public string? ReporterName { get; init; }
    public string? ReporterRelation { get; init; }
    public string? ReporterPhone { get; init; }
    public string? SituationDesc { get; init; }
    public string? RemainsArrangement { get; init; }
    public string? BusinessAgent { get; init; }
    public string? BusinessMatter { get; init; }
    public string? DeclaredByName { get; init; }
    public string? DeclaredByIdCard { get; init; }
    public string? DeclaredByPhone { get; init; }
    public string? DeclaredByType { get; init; }
    public string? DeclaredAtText { get; init; }

    // ── 上下文与范围 ──

    /// <summary>服务聚合上下文（带出/汇总后有值；手工录入未汇总时为 null → BuildManualContext 兜底）</summary>
    public DeathStatementContext? Context { get; init; }

    /// <summary>组织范围（省/市/县区/乡镇/村/落款单位）</summary>
    public VillageScope? Scope { get; init; }

    // ── 输出控制 ──

    /// <summary>三联各份「推送单位」（村留存 / 镇政府 / 镇中心卫生院），由调用方按联次注入；
    /// null → 取 <see cref="DefaultPushUnit"/>（预览默认展示的联次）。</summary>
    public string? PushUnit { get; init; }

    /// <summary>预览默认推送单位（VM.CurrentPushUnit() 结果，装配器不依赖 UI 状态）</summary>
    public string DefaultPushUnit { get; init; } = string.Empty;

    /// <summary>经办人显示名（App.CurrentUserFullName，空回退账号名——由 VM 采集后注入）</summary>
    public string OperatorName { get; init; } = string.Empty;
}

/// <summary>
/// 死亡情况说明文书字段装配（2026-11 死亡说明简化·3b 下沉：原 DeathStatementViewModel.BuildStatementFields
/// + BuildManualContext 纯移动至此，零行为变更）。
///
/// 文书字段单点装配（ASCII 键；身份/提醒槽位全部显式供给，杜绝占位符残留）。
/// ⚠️ 行为敏感点：「表单值优先、context 兜底（FormOrCtx）」是 2026-10-30 修复——
/// 原五键+死者段全读 _context 旧快照，带出后用户改表单不出现在输出（如改了申报人姓名仍印带出时的旧值）。
/// 带出时 ApplyContext 已把 context 值灌入表单，两者一致；用户随后编辑表单即以表单为准。逐键搬迁勿改语义。
/// </summary>
public static class DeathStatementFieldAssembler
{
    /// <summary>文书字段装配（输出与原 VM 版逐键一致）。</summary>
    public static Dictionary<string, string> BuildStatementFields(DeathStatementFieldInput input)
    {
        var c = input.Context ?? BuildManualContext(input);
        var scope = input.Scope;

        // 落款五段：省/市/县区来自组织机构，乡镇/村来自范围与申报（镇级降级时村取表单手填值）
        var province = scope?.ProvinceName ?? string.Empty;
        var city = scope?.CityName ?? string.Empty;
        var district = scope?.DistrictName ?? string.Empty;
        var town = string.IsNullOrWhiteSpace(c.TownName) ? (scope?.TownName ?? string.Empty) : c.TownName;
        var village = !string.IsNullOrWhiteSpace(input.DeclaredVillage)
            ? input.DeclaredVillage.Trim()
            : (c.VillageName ?? string.Empty);

        // 性别/民族：表单值（档案带出或身份证推导）优先，上下文次之
        var gender = !string.IsNullOrWhiteSpace(input.DeathGender) ? input.DeathGender : (c.Gender ?? string.Empty);
        var ethnicity = !string.IsNullOrWhiteSpace(input.DeathEthnicity) ? input.DeathEthnicity : (c.Ethnicity ?? string.Empty);
        var push = string.IsNullOrWhiteSpace(input.PushUnit) ? input.DefaultPushUnit : input.PushUnit.Trim();

        // 表单值优先、context 兜底（见类注释的行为敏感点说明）
        string FormOrCtx(string? formVal, string? ctxVal) =>
            string.IsNullOrWhiteSpace(formVal) ? (ctxVal ?? string.Empty) : formVal.Trim();

        var hasFormDeathTime = input.DeathDateTime.HasValue;
        var deathDateText = hasFormDeathTime ? input.DeathDateTime!.Value.ToString("yyyy-MM-dd") : c.DeathDateText;
        var deathTimeText = hasFormDeathTime ? input.DeathDateTime!.Value.ToString(@"hh\:mm") : c.DeathTimeText;
        var deathDateTimeText = hasFormDeathTime
            ? DeathStatementService.FormatDeathDateTime(deathDateText, deathTimeText)
            : c.DeathDateTimeText;

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FieldKeys.DEATH_NAME] = FormOrCtx(input.Name, c.Name),
            [FieldKeys.DEATH_ID_CARD] = FormOrCtx(input.IdCard, c.IdCard),
            [FieldKeys.DEATH_DATE] = deathDateText,
            [FieldKeys.DEATH_TIME] = deathTimeText,
            [FieldKeys.DEATH_DATETIME_TEXT] = deathDateTimeText,
            [FieldKeys.DEATH_REASON] = FormOrCtx(input.DeathReason, c.DeathReason),
            [FieldKeys.DEATH_CERT_NO] = FormOrCtx(input.DeathCertNo, c.DeathCertNo),
            [FieldKeys.DEATH_PLACE] = FormOrCtx(input.DeathPlace, c.DeathPlace),
            [FieldKeys.DEATH_RELATION] = FormOrCtx(input.Relation, c.Relation),
            [FieldKeys.DECLARED_RELATION] = FormOrCtx(input.DeclaredRelation, c.DeclaredRelation),

            // 落款五段（模板：{所在省}{所在市}{所在县区}{所在乡镇}{所在村}村民委员会）
            [FieldKeys.PROVINCE] = province,
            [FieldKeys.CITY] = city,
            [FieldKeys.DISTRICT] = district,
            [FieldKeys.DEATH_TOWN] = town,
            [FieldKeys.DEATH_VILLAGE] = village,

            // 核实说明要素
            [FieldKeys.DEATH_GENDER] = gender,
            [FieldKeys.DEATH_ETHNICITY] = ethnicity,
            [FieldKeys.PUSH_UNIT] = push,
            [FieldKeys.DEATH_REPORTER_NAME] = input.ReporterName ?? string.Empty,
            [FieldKeys.DEATH_REPORTER_RELATION] = input.ReporterRelation ?? string.Empty,
            [FieldKeys.DEATH_REPORTER_PHONE] = input.ReporterPhone ?? string.Empty,
            [FieldKeys.DEATH_SITUATION_DESC] = input.SituationDesc ?? string.Empty,
            [FieldKeys.DEATH_REMAINS_ARRANGEMENT] = input.RemainsArrangement ?? string.Empty,
            [FieldKeys.BUSINESS_AGENT] = input.BusinessAgent ?? string.Empty,
            [FieldKeys.BUSINESS_MATTER] = input.BusinessMatter ?? string.Empty,

            [FieldKeys.DEATH_ADDRESS] = c.Address,
            [FieldKeys.DEATH_ARCHIVE_CLASSIFICATION] = c.Classification,
            [FieldKeys.DEATH_ARCHIVE_ID] = c.ApplicationId?.ToString() ?? string.Empty,
            [FieldKeys.DECLARED_BY_NAME] = FormOrCtx(input.DeclaredByName, c.DeclaredByName),
            [FieldKeys.DECLARED_BY_ID_CARD] = FormOrCtx(input.DeclaredByIdCard, c.DeclaredByIdCard),
            [FieldKeys.DECLARED_BY_PHONE] = FormOrCtx(input.DeclaredByPhone, c.DeclaredByPhone),
            [FieldKeys.DECLARED_BY_TYPE] = FormOrCtx(input.DeclaredByType, c.DeclaredByType),
            [FieldKeys.DECLARED_AT] = FormOrCtx(input.DeclaredAtText, c.DeclaredAtText),
            [FieldKeys.DEATH_ISSUE_UNIT] = scope?.OrgName ?? string.Empty,
            [FieldKeys.DEATH_ISSUE_DATE] = DateTime.Today.ToString("yyyy年M月d日"),
            [FieldKeys.OPERATOR_NAME] = input.OperatorName,
            [FieldKeys.OPERATOR_UNIT] = scope?.OrgName ?? string.Empty,
            [FieldKeys.OPERATOR_UNIT_PHONE] = scope?.OrgPhone ?? string.Empty,
            [FieldKeys.DEATH_IDENTITY_COUNT] = c.Identities.Count.ToString(),
            [FieldKeys.DEATH_REMIND_COUNT] = c.Reminders.Count.ToString()
        };

        // 身份清单槽（_1.._12，每行 5 键；不足位清空防残留）
        for (var i = 1; i <= FieldKeys.DEATH_IDENTITY_SLOTS; i++)
        {
            var item = i <= c.Identities.Count ? c.Identities[i - 1] : null;
            fields[$"{FieldKeys.DEATH_IDENTITY_TYPE_BASE}_{i}"] = item?.TypeLabel ?? string.Empty;
            fields[$"{FieldKeys.DEATH_IDENTITY_STATUS_BASE}_{i}"] = item?.StatusLabel ?? string.Empty;
            fields[$"{FieldKeys.DEATH_IDENTITY_PERIOD_BASE}_{i}"] = item?.Period ?? string.Empty;
            fields[$"{FieldKeys.DEATH_IDENTITY_SOURCE_BASE}_{i}"] = item?.SourceLabel ?? string.Empty;
            fields[$"{FieldKeys.DEATH_IDENTITY_NOTE_BASE}_{i}"] = item?.Note ?? string.Empty;
        }

        // 数据提醒槽（_1.._8，每行 2 键）
        for (var i = 1; i <= FieldKeys.DEATH_REMIND_SLOTS; i++)
        {
            var r = i <= c.Reminders.Count ? c.Reminders[i - 1] : null;
            fields[$"{FieldKeys.DEATH_REMIND_LEVEL_BASE}_{i}"] = r?.Level ?? string.Empty;
            fields[$"{FieldKeys.DEATH_REMIND_TEXT_BASE}_{i}"] = r == null
                ? string.Empty
                : $"{r.Message}{(string.IsNullOrEmpty(r.SuggestAction) ? string.Empty : "（" + r.SuggestAction + "）")}";
        }

        return fields;
    }

    /// <summary>
    /// 手工录入未提交时的上下文（仅表单值，身份/提醒/联动走服务聚合）。
    /// 未选择死亡时刻时回退当前时刻——仅影响文书预览聚合，落库必经申报校验的真实必填把关。
    /// </summary>
    public static DeathStatementContext BuildManualContext(DeathStatementFieldInput input)
    {
        var dt = input.DeathDateTime ?? DateTime.Now;
        var scope = input.Scope;
        return new DeathStatementContext
        {
            Name = input.Name?.Trim() ?? string.Empty,
            IdCard = input.IdCard?.Trim() ?? string.Empty,
            DeathDateText = dt.ToString("yyyy-MM-dd"),
            DeathTimeText = dt.ToString(@"hh\:mm"),
            DeathDateTimeText = DeathStatementService.FormatDeathDateTime(
                dt.ToString("yyyy-MM-dd"), dt.ToString(@"hh\:mm")),
            DeathReason = input.DeathReason ?? string.Empty,
            DeathCertNo = input.DeathCertNo ?? string.Empty,
            DeathPlace = input.DeathPlace ?? string.Empty,
            DeclaredByName = input.DeclaredByName ?? string.Empty,
            DeclaredByIdCard = input.DeclaredByIdCard ?? string.Empty,
            DeclaredByPhone = input.DeclaredByPhone ?? string.Empty,
            DeclaredByType = input.DeclaredByType ?? string.Empty,
            DeclaredRelation = input.DeclaredRelation ?? string.Empty,
            DeclaredAtText = input.DeclaredAtText ?? string.Empty,
            VillageName = scope?.VillageName ?? string.Empty,
            TownName = scope?.TownName ?? string.Empty
        };
    }
}
