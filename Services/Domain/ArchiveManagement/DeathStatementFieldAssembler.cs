using NewCosmos.Constants;
using NewCosmos.Helpers;
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

    // ── 规范化格式五条目（2026-11-08）──
    /// <summary>死亡证明类型（死亡医学证明 / 非正常死亡证明；条目一）</summary>
    public string? CertType { get; init; }

    /// <summary>死亡证明出具机构（条目一）</summary>
    public string? CertIssueOrg { get; init; }

    /// <summary>遗体处置方式（火化 / 安葬 / 其他；条目二组句分流，空则按下方字段推断）</summary>
    public string? DispositionBranch { get; init; }

    public DateTime? CremationDate { get; init; }
    public string? CremationPlace { get; init; }
    public string? CremationCertNo { get; init; }
    public DateTime? BurialDate { get; init; }
    public string? BurialPlace { get; init; }

    /// <summary>家属情况及诉求（条目四，原样输出）</summary>
    public string? FamilySituation { get; init; }

    /// <summary>矛盾纠纷及信访隐患（无 / 有；条目五）</summary>
    public string? DisputeStatus { get; init; }

    /// <summary>矛盾纠纷说明（条目五，勾「有」时附注）</summary>
    public string? DisputeDesc { get; init; }

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

        // ── 规范化格式五条目组句（2026-11-08）：Word 模板无法做条件分支，
        //    每条目输出一个整句占位符；分支/命中判定在本装配器单点完成 ──
        var certNo = FormOrCtx(input.DeathCertNo, c.DeathCertNo);
        var certType = FormOrCtx(input.CertType, c.CertType);
        var certOrg = FormOrCtx(input.CertIssueOrg, c.CertIssueOrg);
        var certStatement = BuildCertStatement(certType, certOrg, certNo);

        var dispositionText = BuildDispositionText(input, c);
        var benefitText = BuildBenefitFlagText(c.Identities);

        var disputeStatus = FormOrCtx(input.DisputeStatus, c.DisputeStatus);
        var disputeDesc = FormOrCtx(input.DisputeDesc, c.DisputeDesc);
        var disputeText = BuildDisputeText(disputeStatus, disputeDesc);

        var deathAgeText = BuildAgeText(FormOrCtx(input.IdCard, c.IdCard), deathDateText);

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FieldKeys.DEATH_NAME] = FormOrCtx(input.Name, c.Name),
            [FieldKeys.DEATH_ID_CARD] = FormOrCtx(input.IdCard, c.IdCard),
            [FieldKeys.DEATH_DATE] = deathDateText,
            [FieldKeys.DEATH_TIME] = deathTimeText,
            [FieldKeys.DEATH_DATETIME_TEXT] = deathDateTimeText,
            [FieldKeys.DEATH_REASON] = FormOrCtx(input.DeathReason, c.DeathReason),
            [FieldKeys.DEATH_CERT_NO] = certNo,
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
            [FieldKeys.DEATH_REMIND_COUNT] = c.Reminders.Count.ToString(),

            // ── 规范化格式五条目（2026-11-08；整句见各 Build* 组句方法，原始键同步供给供自定义模板取数）──
            [FieldKeys.DEATH_AGE] = deathAgeText,
            [FieldKeys.DEATH_CERT_TYPE] = certType,
            [FieldKeys.DEATH_CERT_ISSUE_ORG] = certOrg,
            [FieldKeys.DEATH_CERT_STATEMENT] = certStatement,
            [FieldKeys.CREMATION_DATE] = FormatDateText(input.CremationDate, c.CremationDateText),
            [FieldKeys.CREMATION_PLACE] = FormOrCtx(input.CremationPlace, c.CremationPlace),
            [FieldKeys.CREMATION_CERT_NO] = FormOrCtx(input.CremationCertNo, c.CremationCertNo),
            [FieldKeys.BURIAL_DATE] = FormatDateText(input.BurialDate, c.BurialDateText),
            [FieldKeys.BURIAL_PLACE] = FormOrCtx(input.BurialPlace, c.BurialPlace),
            [FieldKeys.DISPOSITION_TEXT] = dispositionText,
            [FieldKeys.FAMILY_SITUATION] = FormOrCtx(input.FamilySituation, c.FamilySituation),
            [FieldKeys.BENEFIT_FLAG_TEXT] = benefitText,
            [FieldKeys.DISPUTE_STATUS] = disputeStatus,
            [FieldKeys.DISPUTE_DESC] = disputeDesc,
            [FieldKeys.DISPUTE_TEXT] = disputeText
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

    #region 规范化格式组句（2026-11-08；Word 模板无法条件分支 → 每条目一个整句占位符）

    /// <summary>条目一 死亡证明情况：机构/类型/编号按现有信息组句；全空 → 「待办理」（申报常先于死亡证明取得）</summary>
    private static string BuildCertStatement(string certType, string certOrg, string certNo)
    {
        var noPart = string.IsNullOrWhiteSpace(certNo) ? string.Empty : $"（编号：{certNo}）";
        var hasType = !string.IsNullOrWhiteSpace(certType);
        var hasOrg = !string.IsNullOrWhiteSpace(certOrg);
        if (hasType && hasOrg) return $"已由{certOrg}出具《{certType}》{noPart}";
        if (hasType) return $"已出具《{certType}》{noPart}";
        if (hasOrg) return $"已由{certOrg}出具死亡证明{noPart}";
        return string.IsNullOrWhiteSpace(certNo) ? "待办理" : $"死亡证明编号：{certNo}";
    }

    /// <summary>
    /// 条目二 遗体处置情况：按表单选中分支组句（火化 / 安葬 / 其他自由文本）；
    /// 分支为空（带出的存量行）时按字段存在性推断，全空回退遗体安置自由文本。
    /// 安葬日期 ≥ 今天 → 「拟于」（未办先报），否则 → 「已于」。
    /// </summary>
    private static string BuildDispositionText(DeathStatementFieldInput input, DeathStatementContext c)
    {
        var cremationDate = input.CremationDate ?? ParseDate(c.CremationDateText);
        var cremationPlace = FirstNonEmpty(input.CremationPlace, c.CremationPlace);
        var cremationCertNo = FirstNonEmpty(input.CremationCertNo, c.CremationCertNo);
        var burialDate = input.BurialDate ?? ParseDate(c.BurialDateText);
        var burialPlace = FirstNonEmpty(input.BurialPlace, c.BurialPlace);
        var otherText = FirstNonEmpty(input.RemainsArrangement, c.RemainsArrangement) ?? string.Empty;

        var branch = input.DispositionBranch?.Trim() ?? string.Empty;
        var hasCremation = cremationDate.HasValue || !string.IsNullOrWhiteSpace(cremationPlace);
        var hasBurial = burialDate.HasValue || !string.IsNullOrWhiteSpace(burialPlace);

        var useCremation = string.Equals(branch, DeathDeclarationConstants.DispositionCremation, StringComparison.Ordinal)
            || (branch.Length == 0 && hasCremation);
        var useBurial = string.Equals(branch, DeathDeclarationConstants.DispositionBurial, StringComparison.Ordinal)
            || (branch.Length == 0 && !useCremation && hasBurial);

        if (useCremation)
        {
            var head = cremationDate.HasValue ? $"已于{cremationDate.Value:yyyy年M月d日}" : "已";
            var where = string.IsNullOrWhiteSpace(cremationPlace) ? string.Empty : $"在{cremationPlace}";
            var noPart = string.IsNullOrWhiteSpace(cremationCertNo) ? string.Empty : $"（火化证明编号：{cremationCertNo}）";
            return $"{head}{where}火化{noPart}";
        }

        if (useBurial)
        {
            if (burialDate is null && string.IsNullOrWhiteSpace(burialPlace))
                return otherText;
            var verb = burialDate.HasValue && burialDate.Value.Date >= DateTime.Today ? "拟于" : "已于";
            var when = burialDate.HasValue ? $"{verb}{burialDate.Value:yyyy年M月d日}安葬于" : "安葬于";
            return $"{when}{(string.IsNullOrWhiteSpace(burialPlace) ? string.Empty : burialPlace)}";
        }

        // 「其他」分支 / 无结构化数据（含存量只有安置文本的行）
        return otherText;
    }

    /// <summary>
    /// 条目三 惠民政策享受情况：由身份八源聚合按 低保/特困/高龄/残疾/优抚 关键词命中自动勾选，
    /// 零手填；命中 → 「☑是 □否 为{命中类别}对象」，未命中 → 「□是 ☑否 为{五类全列}对象」。
    /// </summary>
    private static string BuildBenefitFlagText(List<DeathIdentityItem> identities)
    {
        var matched = new List<string>();

        void Hit(string label, params string[] keys)
        {
            if (matched.Contains(label, StringComparer.Ordinal)) return;
            if (identities.Any(i => !string.IsNullOrEmpty(i.TypeLabel)
                                    && keys.Any(k => i.TypeLabel.Contains(k, StringComparison.Ordinal))))
                matched.Add(label);
        }

        Hit("低保", "低保", "最低生活保障");
        Hit("特困", "特困");
        Hit("高龄", "高龄");
        Hit("残疾", "残疾");
        Hit("优抚", "优抚");

        return matched.Count > 0
            ? $"该死者☑是 □否 为{string.Join("、", matched)}对象，相关补贴已/正在办理停发手续"
            : "该死者□是 ☑否 为低保/特困/高龄/残疾/优抚对象，相关补贴已/正在办理停发手续";
    }

    /// <summary>条目五 矛盾纠纷及信访隐患：勾选态打 ☑，勾「有」附说明；未选为空（存量行）</summary>
    private static string BuildDisputeText(string status, string desc)
    {
        if (string.IsNullOrWhiteSpace(status)) return string.Empty;
        if (string.Equals(status, DeathDeclarationConstants.DisputeNone, StringComparison.Ordinal))
            return "☑无 □有";
        if (string.Equals(status, DeathDeclarationConstants.DisputeHas, StringComparison.Ordinal))
            return string.IsNullOrWhiteSpace(desc) ? "□无 ☑有" : $"□无 ☑有（{desc}）";
        return status;
    }

    /// <summary>首段年龄（岁）：身份证出生日期按死亡日期计算（非当前年龄）；取不到出生日期为空</summary>
    private static string BuildAgeText(string idCard, string deathDateText)
    {
        var birth = IdCardValidator.ExtractBirthDate(idCard);
        if (birth is null) return string.Empty;
        var death = DateTime.TryParse(deathDateText, out var d) ? d.Date : DateTime.Today;
        if (death < birth.Value.Date) return string.Empty;
        var age = death.Year - birth.Value.Year;
        if (death.Month < birth.Value.Month || (death.Month == birth.Value.Month && death.Day < birth.Value.Day))
            age--;
        return age < 0 ? string.Empty : age.ToString();
    }

    /// <summary>日期 → 中文整段（yyyy年M月d日）；value 优先，回退 context 文本解析</summary>
    private static string FormatDateText(DateTime? value, string fallbackText)
    {
        if (value.HasValue) return value.Value.ToString("yyyy年M月d日");
        return DateTime.TryParse(fallbackText, out var d) ? d.ToString("yyyy年M月d日") : string.Empty;
    }

    private static DateTime? ParseDate(string? text) =>
        DateTime.TryParse(text, out var d) ? d.Date : null;

    private static string? FirstNonEmpty(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) ? a.Trim() : (!string.IsNullOrWhiteSpace(b) ? b.Trim() : null);

    #endregion

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
            // 规范化格式五条目（表单值直接进手工上下文，组句仍由装配器按表单优先取值）
            CertType = input.CertType ?? string.Empty,
            CertIssueOrg = input.CertIssueOrg ?? string.Empty,
            CremationDateText = input.CremationDate?.ToString("yyyy-MM-dd") ?? string.Empty,
            CremationPlace = input.CremationPlace ?? string.Empty,
            CremationCertNo = input.CremationCertNo ?? string.Empty,
            BurialDateText = input.BurialDate?.ToString("yyyy-MM-dd") ?? string.Empty,
            BurialPlace = input.BurialPlace ?? string.Empty,
            FamilySituation = input.FamilySituation ?? string.Empty,
            DisputeStatus = input.DisputeStatus ?? string.Empty,
            DisputeDesc = input.DisputeDesc ?? string.Empty,
            VillageName = scope?.VillageName ?? string.Empty,
            TownName = scope?.TownName ?? string.Empty
        };
    }
}
