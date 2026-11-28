using System.Globalization;

namespace NewCosmos.Constants;

/// <summary>
/// 临时救助业务常量
/// </summary>
public static class TempReliefConstants
{
    /// <summary>救助类型：常规救助（金额由上级核定；备用金渠道手工填）</summary>
    public const string ReliefTypeLarge = "Large";

    /// <summary>救助类型：小额快救（定额）</summary>
    public const string ReliefTypeSmall = "Small";

    // ── 两大类 × 三小类（2026-11-25） ──
    /// <summary>人群状态：在保障人群内（四类在册）</summary>
    public const string PopulationInsured = "Insured";

    /// <summary>人群状态：不在保障人群内（手工登记）</summary>
    public const string PopulationUninsured = "Uninsured";

    /// <summary>发放渠道：上级部门发放</summary>
    public const string PaymentCounty = "County";

    /// <summary>发放渠道：备用金发放（小额固定此渠道）</summary>
    public const string PaymentReserve = "Reserve";

    /// <summary>非在册手工登记来源（source_type；source_table 为空）</summary>
    public const string SourceManual = "Manual";

    /// <summary>
    /// 四类在册来源表（临时救助“在保障人群内”白名单）：
    /// 农村低保 / 城市低保 / 特困 / 低保边缘 / 刚性支出。
    /// </summary>
    public static readonly string[] InsuredSourceTables =
    {
        "nc_biz_rural_subsistence_families",
        "nc_biz_urban_subsistence_families",
        "nc_biz_destitute_families",
        "nc_biz_low_income_edge_families",
        "nc_biz_rigid_expenditure_families"
    };

    /// <summary>来源是否属于四类在册（人群状态判定单点）。</summary>
    public static bool IsInsuredSourceTable(string? sourceTable)
        => !string.IsNullOrEmpty(sourceTable) && Array.IndexOf(InsuredSourceTables, sourceTable) >= 0;

    /// <summary>
    /// 人群状态判定（兜底口径）：手工登记一律非在册；其余按来源表是否四类在册。
    /// 正式流程已改为按身份证反查四类在册台账（<c>ITempReliefService.ResolveInsuredIdentityAsync</c>），
    /// 本方法仅用于存量/异常路径兜底，不再作为落库唯一依据。
    /// </summary>
    public static string ResolvePopulationStatus(string? sourceType, string? sourceTable)
        => string.Equals(sourceType, SourceManual, StringComparison.Ordinal)
           || !IsInsuredSourceTable(sourceTable)
            ? PopulationUninsured
            : PopulationInsured;

    /// <summary>四类在册台账展示名（自动判定依据展示用）。</summary>
    public static string GetInsuredLedgerDisplayName(string? table) => table switch
    {
        "nc_biz_rural_subsistence_families" => "农村低保台账",
        "nc_biz_urban_subsistence_families" => "城市低保台账",
        "nc_biz_destitute_families" => "特困人员台账",
        "nc_biz_low_income_edge_families" => "最低生活保障边缘台账",
        "nc_biz_rigid_expenditure_families" => "刚性支出困难家庭台账",
        _ => table ?? string.Empty
    };

    /// <summary>
    /// 四类在册台账主口径优先级（同一人命中多张台账时取首个展示）：
    /// 特困 &gt; 低保（农村/城市）&gt; 边缘 &gt; 刚性支出。
    /// </summary>
    public static readonly string[] InsuredLedgerPriority =
    {
        "nc_biz_destitute_families",
        "nc_biz_rural_subsistence_families",
        "nc_biz_urban_subsistence_families",
        "nc_biz_low_income_edge_families",
        "nc_biz_rigid_expenditure_families"
    };

    /// <summary>
    /// 将命中台账按优先级转展示名（纯逻辑，单测覆盖）：优先输出 <see cref="InsuredLedgerPriority"/>，
    /// 未知表名按原样追加在末尾。
    /// </summary>
    public static List<string> ResolveInsuredLedgerNames(IEnumerable<string?> matchedTables)
    {
        var remaining = new HashSet<string>(
            (matchedTables ?? Enumerable.Empty<string?>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t!),
            StringComparer.Ordinal);

        var names = new List<string>();
        foreach (var table in InsuredLedgerPriority)
        {
            if (remaining.Remove(table)) names.Add(GetInsuredLedgerDisplayName(table));
        }

        foreach (var table in remaining)
        {
            var name = GetInsuredLedgerDisplayName(table);
            if (!string.IsNullOrEmpty(name) && !names.Contains(name)) names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// 发放渠道归一：小额固定备用金；常规默认上级部门；非法值回退默认。
    /// </summary>
    public static string ResolvePaymentChannel(string? reliefType, string? channel)
    {
        if (string.Equals(reliefType, ReliefTypeSmall, StringComparison.Ordinal))
            return PaymentReserve;
        return string.Equals(channel, PaymentReserve, StringComparison.Ordinal)
            ? PaymentReserve
            : PaymentCounty;
    }

    /// <summary>人群状态中文名。</summary>
    public static string GetPopulationStatusName(string? status) => status switch
    {
        PopulationInsured => "在保障人群内",
        PopulationUninsured => "不在保障人群内",
        _ => status ?? string.Empty
    };

    /// <summary>发放渠道中文名。</summary>
    public static string GetPaymentChannelName(string? channel) => channel switch
    {
        PaymentCounty => "上级部门发放",
        PaymentReserve => "备用金发放",
        _ => channel ?? string.Empty
    };

    /// <summary>6 类业务场景展示名（列表/打印用）。</summary>
    public static string GetCategoryDisplayName(string? reliefType, string? populationStatus, string? channel)
    {
        var population = GetPopulationStatusName(populationStatus);
        var sub = string.Equals(reliefType, ReliefTypeSmall, StringComparison.Ordinal)
            ? "小额快救"
            : $"常规救助（{GetPaymentChannelName(ResolvePaymentChannel(reliefType, channel))}）";
        return $"{population} · {sub}";
    }

    /// <summary>
    /// 办理时限（自然日，2026-11-25 口径）：小额 2 日；在册常规 5 日；非在册常规 15 日。
    /// </summary>
    public static int GetDeadlineDays(string? reliefType, string? populationStatus)
    {
        if (string.Equals(reliefType, ReliefTypeSmall, StringComparison.Ordinal)) return 2;
        return string.Equals(populationStatus, PopulationInsured, StringComparison.Ordinal) ? 5 : 15;
    }

    /// <summary>办理截止日 = 受理日 + 时限天数。</summary>
    public static DateTime GetDeadlineDate(DateTime acceptedAt, string? reliefType, string? populationStatus)
        => acceptedAt.Date.AddDays(GetDeadlineDays(reliefType, populationStatus));

    /// <summary>剩余天数（负数=已逾期）。</summary>
    public static int GetRemainingDays(DateTime dueDate, DateTime today)
        => (dueDate.Date - today.Date).Days;

    /// <summary>备用金单次上限系数（政策 4–6 倍档上界）。</summary>
    public const decimal ReserveCapMultiplier = 6m;

    /// <summary>备用金单次上限 = 基数 × 遇困人数 × 6（配置化前默认）。</summary>
    public static decimal GetReserveCap(decimal baseStandard, int personCount)
        => baseStandard <= 0 || personCount <= 0
            ? 0m
            : CeilToYuan(baseStandard * personCount * ReserveCapMultiplier);

    /// <summary>状态：草稿</summary>
    public const string StatusDraft = "Draft";

    /// <summary>状态：已确认</summary>
    public const string StatusConfirmed = "Confirmed";

    /// <summary>状态：已提交（乡镇初审通过，等待登记县确认件）</summary>
    public const string StatusSubmitted = "Submitted";

    /// <summary>状态：不予救助（终态，须有理由与告知时间）</summary>
    public const string StatusRejected = "Rejected";

    /// <summary>状态：退回补正（乡镇可修改后重新提交）</summary>
    public const string StatusReturned = "Returned";

    /// <summary>备案状态：待备案（上级部门发放确认后）</summary>
    public const string FilingPending = "Pending";

    /// <summary>备案状态：已备案</summary>
    public const string FilingFiled = "Filed";

    /// <summary>状态：已终止</summary>
    public const string StatusStopped = "Stopped";

    /// <summary>申请人来源：导入台账库</summary>
    public const string SourceImportTab = "ImportTab";

    /// <summary>申请人来源：低保申请库（nc_biz_applications）</summary>
    public const string SourceApplication = "Application";

    /// <summary>申请编号前缀</summary>
    public const string ApplicationNoPrefix = "LZ";

    /// <summary>
    /// 申请日期最多可提前的月数（未来日期上限，2026-11-25 口径）：
    /// 新建/改期均以「今天 + 本值」为上限，超过即校验失败。
    /// </summary>
    public const int ApplyDateFutureMonths = 1;

    /// <summary>申请日期未来上限 = 今天 + <see cref="ApplyDateFutureMonths"/>（含当日）。</summary>
    public static DateTime GetApplyDateUpperBound(DateTime today) => today.Date.AddMonths(ApplyDateFutureMonths);

    /// <summary>
    /// 申请日期是否在允许范围内：只校验未来上限（过去日期不设下限，保持历史行为）。
    /// </summary>
    public static bool IsApplyDateWithinRange(DateTime date, DateTime today)
        => date.Date <= GetApplyDateUpperBound(today);

    /// <summary>
    /// 拼申请编号：LZ + yyyyMMdd + 4 位序号（取号与改期重号共用，禁止调用点各自拼字符串）。
    /// </summary>
    public static string BuildApplicationNo(DateTime date, long sequence)
        => $"{ApplicationNoPrefix}{date:yyyyMMdd}{sequence:D4}";

    /// <summary>困难类型：疾病</summary>
    public const string DifficultyTypeDisease = "疾病";

    /// <summary>困难类型：意外灾害</summary>
    public const string DifficultyTypeAccident = "意外灾害";

    /// <summary>困难类型：教育支出</summary>
    public const string DifficultyTypeEducation = "教育支出";

    /// <summary>困难类型：其他困难（默认）</summary>
    public const string DifficultyTypeOther = "其他困难";

    /// <summary>与户主关系：户主</summary>
    public const string RelationHead = "户主";

    /// <summary>村字段默认值（候选人无村信息时兜底）</summary>
    public const string DefaultVillage = "村委会";

    /// <summary>困难类型选项（表单下拉，落库存中文值）</summary>
    public static readonly string[] DifficultyTypeOptions =
    {
        DifficultyTypeDisease, DifficultyTypeAccident, DifficultyTypeEducation, DifficultyTypeOther
    };

    /// <summary>困难明细行数上限</summary>
    public const int MaxDetailRows = 10;

    /// <summary>意外/灾害类型选项（落库存中文值）</summary>
    public static readonly string[] AccidentTypeOptions =
    {
        "火灾", "交通事故", "溺水", "人身伤害", "其他意外"
    };

    /// <summary>就学阶段选项（落库存中文值）</summary>
    public static readonly string[] EducationStageOptions =
    {
        "学前教育", "小学", "初中", "高中", "中职", "高职", "大学"
    };

    /// <summary>学年制选项（落库存中文值，与大学生管理学制一致）</summary>
    public static readonly string[] SchoolDurationOptions =
    {
        "三年制", "四年制", "五年制"
    };

    /// <summary>
    /// 小额定额档位配置类型（nc_config_standards.standard_type）。
    /// 2026-11-13 起定额救助改为按户籍动态取低保月标准（农村 632 / 城镇 827），
    /// 本类型三档（500/1000/2000）已停用（is_active=false，留行可回溯），代码不再读取。
    /// </summary>
    public const string StandardType = "TEMP_RELIEF_SMALL";

    /// <summary>低保月标准类型：农村（定额救助农村档取此标准）</summary>
    public const string SubsistenceStandardRural = "RuralSubsistenceStandard";

    /// <summary>低保月标准类型：城镇（定额救助城镇档取此标准）</summary>
    public const string SubsistenceStandardUrban = "UrbanSubsistenceStandard";

    /// <summary>
    /// 户籍类型快照 → 低保月标准类型（定额救助动态取值用，唯一解析点）：
    /// 农村/Rural → 农村标准；城镇|城市/Urban → 城镇标准；空/未知 → null（调用方兜底列出两档手选）。
    /// 来源兼容：台账按表名固定 rural/urban、申请库 hukou_type 列 Rural/Urban、中文快照 农村/城镇。
    /// </summary>
    public static string? ResolveSubsistenceStandardType(string? hukouType)
    {
        var v = (hukouType ?? string.Empty).Trim();
        if (v.Length == 0) return null;
        if (v.Equals("Rural", StringComparison.OrdinalIgnoreCase)) return SubsistenceStandardRural;
        if (v.Equals("Urban", StringComparison.OrdinalIgnoreCase)) return SubsistenceStandardUrban;
        if (v.Contains("农村")) return SubsistenceStandardRural;
        if (v.Contains("城镇") || v.Contains("城市")) return SubsistenceStandardUrban;
        return null;
    }

    /// <summary>
    /// 解析「确定金额」输入（定额救助可下调，上限=户籍对应低保月标准，如农村 632、城镇 827；例如可填 332）。
    /// 允许携带「元」后缀；仅保留两位小数（四舍五入到分）。
    /// </summary>
    public static bool TryParseConfirmAmount(string? input, out decimal amount)
    {
        amount = 0m;
        var text = (input ?? string.Empty).Trim();
        if (text.EndsWith('元')) text = text[..^1].Trim();
        if (text.Length == 0) return false;

        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed)
            && !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
        {
            return false;
        }

        if (parsed <= 0) return false;
        amount = Math.Round(parsed, 2, MidpointRounding.AwayFromZero);
        return amount > 0;
    }

    /// <summary>
    /// 校验「确定金额」：须为大于 0 的数字，且不得超过户籍对应低保月标准（maxAmount&lt;=0 表示未取到标准，跳过上限校验）。
    /// 校验消息单点维护，ViewModel 直接展示 error。
    /// </summary>
    public static bool TryValidateConfirmAmount(string? input, decimal maxAmount, out decimal amount, out string? error)
    {
        error = null;
        if (!TryParseConfirmAmount(input, out amount))
        {
            error = "确定金额须为大于 0 的数字";
            return false;
        }

        if (maxAmount > 0 && amount > maxAmount)
        {
            error = $"确定金额不能超过户籍对应低保月标准 {maxAmount:F2} 元";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 家庭遇困人数统一兜底（单点）：显式值 > 0 时用之；否则按家庭人口，仍为空取 1。
    /// 遇困人数 ≠ 家庭人口，批量录入/旧客户端未提供时才回退。
    /// </summary>
    public static int ResolveReliefPersonCount(int? personCount, int? familySize)
        => personCount is > 0 ? personCount.Value : Math.Max(familySize ?? 1, 1);

    #region 两项政策档位（黑民规〔2026〕12 号，2026-11-25）

    /// <summary>小额快救 50% 档</summary>
    public const string TierSmall50 = "S50";

    /// <summary>小额快救 75% 档</summary>
    public const string TierSmall75 = "S75";

    /// <summary>小额快救 100% 档</summary>
    public const string TierSmall100 = "S100";

    /// <summary>小额快救全部档位（顺序即表单顺序）</summary>
    public static readonly string[] SmallTierCodes = { TierSmall50, TierSmall75, TierSmall100 };

    /// <summary>档位系数（0.5/0.75/1）；未知档位返回 0。</summary>
    public static decimal TierMultiplier(string? code) => code switch
    {
        TierSmall50 => 0.5m,
        TierSmall75 => 0.75m,
        TierSmall100 => 1m,
        _ => 0m
    };

    /// <summary>档位展示名。</summary>
    public static string GetTierLabel(string? code) => code switch
    {
        TierSmall50 => "50%",
        TierSmall75 => "75%",
        TierSmall100 => "100%",
        _ => code ?? string.Empty
    };

    /// <summary>金额进一法到元（政策口径，禁止四舍五入）。</summary>
    public static decimal CeilToYuan(decimal value) => Math.Ceiling(value);

    /// <summary>
    /// 小额快救单次总额 = 家庭遇困人数 × 城市低保月标准 × 档位系数，最后一次性进一法到元。
    /// </summary>
    public static decimal ComputeSmallTotal(int personCount, decimal baseStandard, string? tierCode)
    {
        if (personCount <= 0) return 0m;
        var multiplier = TierMultiplier(tierCode);
        if (baseStandard <= 0 || multiplier <= 0) return 0m;
        return CeilToYuan(personCount * baseStandard * multiplier);
    }

    /// <summary>档位上限（人数 × 基数 × 系数，进一法到元）。</summary>
    public static decimal GetSmallTierCeiling(decimal baseStandard, int personCount, string? tierCode)
        => ComputeSmallTotal(personCount, baseStandard, tierCode);

    /// <summary>
    /// 校验小额确定金额：大于 0 且不超过档位上限；允许下调。
    /// 标准/档位缺失或人数非法时显式失败（禁止静默放行）。
    /// </summary>
    public static bool TryValidateSmallAmount(string? input, decimal baseStandard, int personCount,
        string? tierCode, out decimal amount, out string? error)
    {
        error = null;
        if (!TryParseConfirmAmount(input, out amount))
        {
            error = "确定金额须为大于 0 的数字";
            return false;
        }

        if (personCount <= 0)
        {
            error = "家庭遇困人数（含户主）须大于 0";
            return false;
        }

        if (baseStandard <= 0)
        {
            error = "未配置当前生效的城市低保月标准，无法核定救助金额";
            return false;
        }

        if (TierMultiplier(tierCode) <= 0)
        {
            error = "请选择小额快救档位（50%/75%/100%）";
            return false;
        }

        var ceiling = GetSmallTierCeiling(baseStandard, personCount, tierCode);
        if (amount > ceiling)
        {
            error = $"确定金额不能超过{GetTierLabel(tierCode)}档上限 {ceiling:F2} 元（{personCount} 人 × 城市低保标准 {baseStandard:F2} × {TierMultiplier(tierCode):0.##}）";
            return false;
        }

        return true;
    }

    #endregion

    /// <summary>家庭人口上限（审批表成员行数）</summary>
    public const int MaxMemberRows = 5;

    /// <summary>获取救助类型中文名</summary>
    public static string GetReliefTypeName(string? reliefType) => reliefType switch
    {
        ReliefTypeLarge => "常规救助",
        ReliefTypeSmall => "小额快救",
        _ => reliefType ?? string.Empty
    };

    /// <summary>
    /// 月报「临时救助新增汇总表」分段标签（打印标题 {申报月份} 后缀 / 导出文件名 / 预览标题共用，单一来源）：
    /// 无额度 = 大额救助（金额不填写）→ 报县局审批；有额度 = 小额快速救助（定额）→ 备用金发放。
    /// 输出一律用业务措辞，「无额度/有额度」仅作代码内判定用语，不落到打印件上。
    /// </summary>
    public const string SegmentTagCountyApproval = "报县局审批";

    /// <summary>月报「临时救助新增汇总表」有额度段标签（小额快速救助定额 → 备用金发放），见 <see cref="SegmentTagCountyApproval"/></summary>
    public const string SegmentTagReservePayout = "备用金发放";

    // ── 两大类 × 三小类（2026-11-25）：月报按发放渠道 × 人群状态分四段 ──
    /// <summary>上级发放 · 在保障人群内</summary>
    public const string SegmentTagCountyInsured = "上级发放·在册";

    /// <summary>上级发放 · 不在保障人群内</summary>
    public const string SegmentTagCountyUninsured = "上级发放·非在册";

    /// <summary>备用金发放 · 在保障人群内（含常规 + 小额）</summary>
    public const string SegmentTagReserveInsured = "备用金·在册";

    /// <summary>备用金发放 · 不在保障人群内（含常规 + 小额）</summary>
    public const string SegmentTagReserveUninsured = "备用金·非在册";

    /// <summary>
    /// 是否「有额度」分段——月报临时救助新增汇总表分段的唯一判定点：
    /// 小额快速救助（定额、有确定金额）= 有额度；大额救助与历史空值 = 无额度
    /// （主表 relief_type 默认值即 Large，空值兜底与默认一致）。
    /// </summary>
    public static bool IsFixedQuotaType(string? reliefType)
        => string.Equals(reliefType, ReliefTypeSmall, StringComparison.Ordinal);

    /// <summary>获取申请人来源中文名</summary>
    public static string GetSourceName(string? sourceType) => sourceType switch
    {
        SourceImportTab => "导入台账",
        SourceApplication => "最低生活保障申请",
        SourceManual => "非在册登记",
        _ => sourceType ?? string.Empty
    };

    /// <summary>获取来源表所属来源中文名（候选人展示用）</summary>
    public static string GetSourceNameByTable(string? tableName) => tableName switch
    {
        "nc_biz_rural_subsistence_families" => "最低生活保障台账",
        "nc_biz_urban_subsistence_families" => "最低生活保障台账",
        "nc_biz_low_income_edge_families" => "最低生活保障边缘台账",
        "nc_biz_rigid_expenditure_families" => "刚性支出台账",
        "nc_biz_other_difficulty_families" => "其他困难人员台账",
        "nc_biz_destitute_families" => "特困人员台账",
        "nc_biz_applications" => "最低生活保障申请库",
        _ => tableName ?? string.Empty
    };

    /// <summary>获取状态中文名</summary>
    public static string GetStatusName(string? status) => status switch
    {
        StatusDraft => "草稿",
        StatusSubmitted => "已提交",
        StatusConfirmed => "已确认",
        StatusStopped => "已终止",
        StatusRejected => "不予救助",
        StatusReturned => "退回补正",
        _ => status ?? string.Empty
    };

    /// <summary>家庭类别选项（7 类，表单下拉；官方名称）</summary>
    public static readonly string[] FamilyCategoryOptions =
    {
        "最低生活保障家庭", "最低生活保障边缘家庭", "特困人员", "重点优抚对象",
        "遭受突发事件家庭", "建档立卡贫困户", "其他困难类型家庭"
    };

    /// <summary>来源表名 → 家庭类别中文名（用于界面展示与模板 {户主家庭类别}）</summary>
    public static string GetFamilyCategoryByTable(string? tableName) => tableName switch
    {
        "nc_biz_rural_subsistence_families" => "最低生活保障家庭",
        "nc_biz_urban_subsistence_families" => "最低生活保障家庭",
        "nc_biz_low_income_edge_families" => "最低生活保障边缘家庭",
        "nc_biz_rigid_expenditure_families" => "其他困难类型家庭",
        "nc_biz_other_difficulty_families" => "其他困难类型家庭",
        "nc_biz_destitute_families" => "特困人员",
        "nc_biz_applications" => "最低生活保障家庭",
        _ => tableName ?? string.Empty
    };

    /// <summary>
    /// 家庭类别归一化到 7 类选项：把历史/导入台账措辞映射为选项内值（农村低保→最低生活保障家庭、特困→特困人员等）；
    /// 已是 7 类之一或无法识别时原样返回。
    /// </summary>
    public static string NormalizeFamilyCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return category ?? string.Empty;
        if (Array.IndexOf(FamilyCategoryOptions, category) >= 0) return category;

        return category switch
        {
            "农村低保" or "城市低保" or "低保" or "农村低保单人" or "城市低保单人"
                or "低保家庭" or "农村低保家庭" or "城市低保家庭"
                or "最低生活保障" or "最低生活保障家庭" or "低保对象" or "最低生活保障对象"
                or "农村最低生活保障对象" or "城市最低生活保障对象"
                or "农村最低生活保障对象（单人）" or "城市最低生活保障对象（单人）" => "最低生活保障家庭",
            "低保边缘" or "低收入家庭" or "低收入" or "低保边缘家庭" or "最低生活保障边缘家庭"
                or "刚性支出" or "刚性支出困难家庭" or "刚性支出困难家庭成员"
                or "农村最低生活保障边缘家庭成员" or "城市最低生活保障边缘家庭成员"
                or "农村刚性支出困难家庭成员" or "城市刚性支出困难家庭成员"
                or "支出型困难家庭" => "最低生活保障边缘家庭",
            "其他困难人员" or "其他困难人员家庭" or "其他困难人员家庭成员" or "其他困难"
                => "其他困难类型家庭",
            "特困" or "特困供养家庭" or "特困分散供养" or "特困集中供养" or "特困人员"
                or "特困供养对象" or "农村特困人员（分散供养）" or "农村特困人员（集中供养）"
                or "城市特困人员（分散供养）" or "城市特困人员（集中供养）"
                or "农村特困分散供养" or "农村特困集中供养" or "城市特困分散供养" or "城市特困集中供养" => "特困人员",
            _ => category
        };
    }

    /// <summary>
    /// 家庭类别 → 缩写（月报_临时救助新增汇总表"个人类别"列）；
    /// 未识别的自定义类别原样返回。
    /// </summary>
    public static string GetFamilyCategoryShortName(string? category) => category switch
    {
        "最低生活保障家庭" => "低保",
        "最低生活保障边缘家庭" => "低保边缘",
        "特困人员" => "特困",
        "重点优抚对象" => "优抚",
        "遭受突发事件家庭" => "突发事件",
        "建档立卡贫困户" => "建档立卡",
        "其他困难类型家庭" => "其他",
        _ => category ?? string.Empty
    };

    /// <summary>
    /// 审核审批表{户主家庭类别}勾选清单（9 项固定顺序，5+4 两行排版；选中项 □→☑）。
    /// </summary>
    public static readonly string[] AuditChecklistOptions =
    {
        "城镇低保家庭", "农村低保家庭", "城保边缘户家庭", "农保边缘户家庭", "城乡特困",
        "重点优抚对象家庭", "遭受突发事件家庭", "刚性支出困难家庭", "其他困难类型家庭"
    };

    /// <summary>勾选清单选项索引（AuditChecklistOptions 内下标，固定映射用）</summary>
    private const int OptUrbanSubsistence = 0;
    private const int OptRuralSubsistence = 1;
    private const int OptUrbanEdge = 2;
    private const int OptRuralEdge = 3;
    private const int OptDestitute = 4;
    private const int OptOther = 8;

    /// <summary>
    /// 归一化到审核审批表勾选清单 9 项——全部为指定字段固定映射，不做开放文本猜测：
    /// 类别由来源表（或低保申请库认定分类）决定；城乡后缀由 hukou_type 快照（农村/Rural→农、城镇|城市/Urban→城）决定。
    /// </summary>
    /// <param name="familyCategory">主表 family_category（申请库来源存认定分类中文）</param>
    /// <param name="sourceTable">来源表名（白名单）</param>
    /// <param name="hukouType">户籍类型快照（农村/城镇；兼容 Rural/Urban/城市）</param>
    public static string ResolveAuditChecklistCategory(string? familyCategory, string? sourceTable, string? hukouType)
    {
        var cat = familyCategory ?? string.Empty;

        // 特困/刚性支出为单一选项，与城乡无关
        if (IsSource(sourceTable, "destitute")) return AuditChecklistOptions[OptDestitute];
        if (cat.Contains("特困")) return AuditChecklistOptions[OptDestitute];
        if (IsSource(sourceTable, "rigid_expenditure")) return "刚性支出困难家庭";

        // 城乡判定：hukou_type 指定字段固定映射；空值时按来源表名兜底（rural/urban 台账），再兜底城镇
        var rural = IsRuralHukou(hukouType) || (string.IsNullOrWhiteSpace(hukouType) && IsSource(sourceTable, "rural"));

        // 边缘：边缘台账 + 申请库含"边缘"的认定分类
        if (IsSource(sourceTable, "low_income_edge") || cat.Contains("边缘"))
            return AuditChecklistOptions[rural ? OptRuralEdge : OptUrbanEdge];

        // 低保：城乡低保台账 + 申请库低保类认定分类
        if (IsSource(sourceTable, "rural_subsistence") || IsSource(sourceTable, "urban_subsistence"))
            return AuditChecklistOptions[rural ? OptRuralSubsistence : OptUrbanSubsistence];
        if (cat.Contains("低保"))
            return AuditChecklistOptions[rural ? OptRuralSubsistence : OptUrbanSubsistence];

        return AuditChecklistOptions[OptOther];
    }

    /// <summary>来源表白名单段匹配（OrdinalIgnoreCase）</summary>
    private static bool IsSource(string? sourceTable, string segment)
        => (sourceTable ?? string.Empty).Contains(segment, StringComparison.OrdinalIgnoreCase);

    /// <summary>户籍类型固定映射：农村/Rural→true；城镇、城市/Urban→false</summary>
    private static bool IsRuralHukou(string? hukouType)
    {
        var v = (hukouType ?? string.Empty).Trim();
        if (v.Length == 0) return false;
        if (v.Equals("Rural", StringComparison.OrdinalIgnoreCase)) return true;
        if (v.Equals("Urban", StringComparison.OrdinalIgnoreCase)) return false;
        if (v.Contains("农村")) return true;
        if (v.Contains("城镇") || v.Contains("城市")) return false;
        return false;
    }

    /// <summary>
    /// 当前库（低保申请库）来源表名：候选人检索与政策清单的排序锚点——**当前库优先、历史导入台账在后**。
    /// 与 <see cref="AllowedSourceTables"/> 末位表一致。
    /// </summary>
    public const string CurrentSourceTable = "nc_biz_applications";

    /// <summary>白名单：来源表（SQL 表名白名单校验用，禁止动态拼接）</summary>
    public static readonly string[] AllowedSourceTables =
    {
        "nc_biz_rural_subsistence_families",
        "nc_biz_urban_subsistence_families",
        "nc_biz_low_income_edge_families",
        "nc_biz_rigid_expenditure_families",
        "nc_biz_other_difficulty_families",
        "nc_biz_destitute_families",
        "nc_biz_applications"
    };

    /// <summary>校验表名是否在白名单内</summary>
    public static bool IsAllowedSourceTable(string? tableName) =>
        tableName != null && Array.IndexOf(AllowedSourceTables, tableName) >= 0;

    /// <summary>台账家庭表 → 成员明细表映射（persons 成员表均有 family_id 关联家庭表 id）</summary>
    public static string GetPersonsTable(string? familyTable) => familyTable switch
    {
        "nc_biz_rural_subsistence_families" => "nc_biz_rural_subsistence_persons",
        "nc_biz_urban_subsistence_families" => "nc_biz_urban_subsistence_persons",
        "nc_biz_low_income_edge_families" => "nc_biz_low_income_edge_persons",
        "nc_biz_rigid_expenditure_families" => "nc_biz_rigid_expenditure_persons",
        "nc_biz_other_difficulty_families" => "nc_biz_other_difficulty_persons",
        "nc_biz_destitute_families" => "nc_biz_destitute_persons",
        _ => string.Empty
    };

    /// <summary>
    /// 成员关系字典键（Head/Spouse/...）→ 中文显示（未知值/已是中文时原样返回）。
    /// 临时救助成员关系在申请库快照、台账人员表、历史落库中均可能以字典键存在，
    /// 表单/打印/审批表统一经本方法转换，避免输出英文（如 Spouse）。
    /// </summary>
    public static string GetRelationshipDisplayName(string? relationship) => relationship?.Trim() switch
    {
        null or "" => string.Empty,
        "Head" => "户主",
        "Spouse" => "配偶",
        "Son" => "儿子",
        "Daughter" => "女儿",
        "Parent" => "父母",
        "Grandchild" => "孙子女",
        "Grandparent" => "祖父母/外祖父母",
        "Sibling" => "兄弟姐妹",
        "Other" => "其他",
        _ => relationship!.Trim()
    };

    /// <summary>
    /// 家庭人口统一口径（唯一实现，禁止各处重抄）：
    /// 户主（1 人）+ 有效家庭成员行数；赡养/抚养/扶养义务人不计入（既非户主也非家庭成员行）。
    /// 无成员明细（台账来源仅保障人数、历史导入数据）时退回来源快照，避免凭空归 1。
    /// 与本系统既有口径一致（ApplicationFormViewModel.DetailCommands 的 SharedLivingMembers.Count + 1）。
    /// </summary>
    /// <param name="storedFamilySize">来源快照（临时救助主表 family_size / 候选人 family_size）</param>
    /// <param name="effectiveMemberCount">有效家庭成员行数（已剔除户主与赡养抚养扶养义务人）</param>
    public static int? ResolveFamilySize(int? storedFamilySize, int effectiveMemberCount)
        => effectiveMemberCount > 0 ? effectiveMemberCount + 1 : storedFamilySize;

    /// <summary>
    /// 统计有效家庭成员行数：成员姓名非空的行（表单预置的空占位行、仅展示槽位不计）。
    /// 户主行由调用方按身份证/关系剔除后再传入。
    /// </summary>
    public static int CountEffectiveMembers(IEnumerable<string?>? memberNames)
        => memberNames?.Count(n => !string.IsNullOrWhiteSpace(n)) ?? 0;

    /// <summary>业务类型（打印导航/模板分类用）</summary>
    public const string BusinessType = "TempRelief";

    /// <summary>打印模板分类：大额</summary>
    public const string CategoryLarge = "临时救助/大额";

    /// <summary>打印模板分类：小额</summary>
    public const string CategorySmall = "临时救助/小额";

    // 原 TemplateAuditApprovalNameToken（"审核审批表" 名称令牌）已删：手机推送打印选模板改按
    // TemplateDocTypes.TempReliefAuditApproval 文档类型精准匹配（方案B，2026-11-18）。
}
