namespace NewCosmos.Constants;

/// <summary>
/// 临时救助业务常量
/// </summary>
public static class TempReliefConstants
{
    /// <summary>救助类型：大额救助（金额不填写）</summary>
    public const string ReliefTypeLarge = "Large";

    /// <summary>救助类型：小额快速救助（定额）</summary>
    public const string ReliefTypeSmall = "Small";

    /// <summary>状态：草稿</summary>
    public const string StatusDraft = "Draft";

    /// <summary>状态：已确认</summary>
    public const string StatusConfirmed = "Confirmed";

    /// <summary>状态：已终止</summary>
    public const string StatusStopped = "Stopped";

    /// <summary>申请人来源：导入台账库</summary>
    public const string SourceImportTab = "ImportTab";

    /// <summary>申请人来源：低保申请库（nc_biz_applications）</summary>
    public const string SourceApplication = "Application";

    /// <summary>申请编号前缀</summary>
    public const string ApplicationNoPrefix = "LZ";

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

    /// <summary>小额定额档位配置类型（nc_config_standards.standard_type）</summary>
    public const string StandardType = "TEMP_RELIEF_SMALL";

    /// <summary>家庭人口上限（审批表成员行数）</summary>
    public const int MaxMemberRows = 5;

    /// <summary>获取救助类型中文名</summary>
    public static string GetReliefTypeName(string? reliefType) => reliefType switch
    {
        ReliefTypeLarge => "大额救助",
        ReliefTypeSmall => "小额快速救助",
        _ => reliefType ?? string.Empty
    };

    /// <summary>获取申请人来源中文名</summary>
    public static string GetSourceName(string? sourceType) => sourceType switch
    {
        SourceImportTab => "导入台账",
        SourceApplication => "最低生活保障申请",
        _ => sourceType ?? string.Empty
    };

    /// <summary>获取来源表所属来源中文名（候选人展示用）</summary>
    public static string GetSourceNameByTable(string? tableName) => tableName switch
    {
        "nc_biz_rural_subsistence_families" => "最低生活保障台账",
        "nc_biz_urban_subsistence_families" => "最低生活保障台账",
        "nc_biz_low_income_edge_families" => "最低生活保障边缘台账",
        "nc_biz_rigid_expenditure_families" => "刚性支出台账",
        "nc_biz_destitute_families" => "特困人员台账",
        "nc_biz_applications" => "最低生活保障申请库",
        _ => tableName ?? string.Empty
    };

    /// <summary>获取状态中文名</summary>
    public static string GetStatusName(string? status) => status switch
    {
        StatusDraft => "草稿",
        StatusConfirmed => "已确认",
        StatusStopped => "已终止",
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

    /// <summary>白名单：来源表（SQL 表名白名单校验用，禁止动态拼接）</summary>
    public static readonly string[] AllowedSourceTables =
    {
        "nc_biz_rural_subsistence_families",
        "nc_biz_urban_subsistence_families",
        "nc_biz_low_income_edge_families",
        "nc_biz_rigid_expenditure_families",
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
        "nc_biz_destitute_families" => "nc_biz_destitute_persons",
        _ => string.Empty
    };

    /// <summary>persons 成员表关系英文枚举 → 中文（未知值保留原文）</summary>
    public static string GetRelationshipDisplayName(string? relationship) => relationship switch
    {
        "Head" => "户主",
        "Spouse" => "配偶",
        "Son" => "儿子",
        "Daughter" => "女儿",
        "Parent" => "父母",
        "Grandchild" => "孙子女",
        _ => relationship ?? string.Empty
    };

    /// <summary>
    /// 是否适用临时救助简化程序（入户调查核实5个工作日，普通为10个工作日）：
    /// 依据 黑龙江省/牡丹江市 临时救助规范，已认定的低保/特困/低保边缘/建档立卡/刚性支出/重点优抚对象
    /// 只核实必需支出、流程压缩。刚性支出经归一化并入最低生活保障边缘家庭。
    /// </summary>
    public static bool IsSimplifiedProcedure(string? familyCategory, string? sourceTable)
    {
        var cat = NormalizeFamilyCategory(familyCategory);

        if (cat is "最低生活保障家庭" or "特困人员" or "最低生活保障边缘家庭"
            or "建档立卡贫困户" or "重点优抚对象")
            return true;

        // 来源台账兜底（family_category 可能未归一化/为空）
        if (sourceTable is not null
            && (IsSource(sourceTable, "destitute")
                || IsSource(sourceTable, "low_income_edge")
                || IsSource(sourceTable, "rigid_expenditure")
                || IsSource(sourceTable, "rural_subsistence")
                || IsSource(sourceTable, "urban_subsistence")
                || string.Equals(sourceTable, "nc_biz_applications", StringComparison.OrdinalIgnoreCase)))
            return true;

        return cat.Contains("低保") || cat.Contains("特困") || cat.Contains("边缘")
            || cat.Contains("建档立卡") || cat.Contains("刚性") || cat.Contains("优抚");
    }

    /// <summary>业务类型（打印导航/模板分类用）</summary>
    public const string BusinessType = "TempRelief";

    /// <summary>打印模板分类：大额</summary>
    public const string CategoryLarge = "临时救助/大额";

    /// <summary>打印模板分类：小额</summary>
    public const string CategorySmall = "临时救助/小额";
}
