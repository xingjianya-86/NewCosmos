namespace NewCosmos.Constants;

/// <summary>
/// 死亡申报台账常量（nc_biz_death_declarations）。
/// 状态与申报人类型用常量而非字典：沿用 ElderlyBenefitConstants.StatusConfirmed 的既有先例，
/// 避免为单一页面引入字典 seed 分类项；如后续需要运营可配置再迁 nc_dict_。
/// </summary>
public static class DeathDeclarationConstants
{
    #region 状态（status）

    /// <summary>待核实（默认；提交即为该态）</summary>
    public const string StatusPending = "Pending";

    /// <summary>已核实</summary>
    public const string StatusVerified = "Verified";

    /// <summary>已作废（软状态：不再进列表、不参与文书输出）</summary>
    public const string StatusRevoked = "Revoked";

    /// <summary>已归档（完结归档：申报流程全部办结结案，退出首页联动提醒；不可再作废）</summary>
    public const string StatusArchived = "Archived";

    /// <summary>状态 → 中文（未知原样返回，禁止洗成空串）</summary>
    public static string GetStatusName(string? status) => status switch
    {
        StatusPending => "待核实",
        StatusVerified => "已核实",
        StatusRevoked => "已作废",
        StatusArchived => "已归档",
        _ => string.IsNullOrEmpty(status) ? string.Empty : status
    };

    #endregion

    #region 申报人类型（declared_by_type）

    public const string DeclaredByFamily = "家属";
    public const string DeclaredByVillage = "村民委员会";
    public const string DeclaredByHospital = "医疗机构";
    public const string DeclaredByPolice = "公安机关";
    public const string DeclaredByOther = "其他";

    /// <summary>全部申报人类型（表单 Picker 数据源）</summary>
    public static readonly string[] DeclaredByTypes =
    {
        DeclaredByFamily, DeclaredByVillage, DeclaredByHospital, DeclaredByPolice, DeclaredByOther
    };

    #endregion

    #region 死亡原因（death_reason，表单 Picker 选项）

    /// <summary>「其他」哨兵值：选中时表单浮现手填框，实际值由手填内容落库</summary>
    public const string DeathReasonOther = "其他";

    /// <summary>死亡原因 Picker 选项（固定小集合；「其他」触发手填）</summary>
    public static readonly string[] DeathReasonOptions =
    {
        "自然死亡", "病故", "意外死亡", DeathReasonOther
    };

    /// <summary>选中项是否为「其他」（→ 显示手填输入框）</summary>
    public static bool IsOtherOption(string? value) =>
        string.Equals(value?.Trim(), DeathReasonOther, StringComparison.Ordinal);

    #endregion

    #region 相关业务事项（business_matter，表单多选 CheckBox）

    /// <summary>多选拼接分隔符（落库仍单列 varchar(200)，勾选多项按此连接；模板 {相关业务事项} 供给拼接串）。
    /// 选项集本身不进代码——字典分类 <c>BusinessMatters</c>（DictionaryTypeCodes），种子见
    /// Resources\Seed\sys_dictionary\items\business_matter.yaml，运行时经 IDictionaryService 加载。</summary>
    public const string BusinessMatterSeparator = "、";

    #endregion

    #region 申报渠道（declare_channel）

    public const string ChannelPc = "PC";
    public const string ChannelAndroid = "Android";
    public const string ChannelWindow = "Window";

    /// <summary>按当前平台解析申报渠道（手机端写 Android，其余写 PC）</summary>
    public static string ResolveChannel() =>
        DeviceInfo.Platform == DevicePlatform.Android ? ChannelAndroid : ChannelPc;

    #endregion

    #region 重复申报检测

    /// <summary>同身份证该窗口内已有有效申报时提交前弹确认（天）</summary>
    public const int DuplicateWindowDays = 3;

    #endregion

    #region 导入台账候选库（搜索候选臂 + 身份八源第⑧源共用；2026-11 从 DeathStatementService 提升为常量）

    /// <summary>
    /// 5 个历史导入台账（人员表, 家庭表, 显示名）。命中优先级即数组顺序
    ///（低保 &gt; 低保边缘 &gt; 特困；刚性支出殿后）——跨库同人保留先命中者。
    /// 表名与 ImportTypeCodes.GetTableNames、ImportedArchiveService.Libraries 同口径（编译期常量，非动态表名）。
    /// </summary>
    public static readonly (string Persons, string Families, string Display)[] ImportCandidateLibraries =
    {
        ("nc_biz_rural_subsistence_persons", "nc_biz_rural_subsistence_families", "农村低保档案"),
        ("nc_biz_urban_subsistence_persons", "nc_biz_urban_subsistence_families", "城市低保档案"),
        ("nc_biz_low_income_edge_persons", "nc_biz_low_income_edge_families", "低保边缘档案"),
        ("nc_biz_destitute_persons", "nc_biz_destitute_families", "特困供养档案"),
        ("nc_biz_rigid_expenditure_persons", "nc_biz_rigid_expenditure_families", "刚性支出档案"),
    };

    /// <summary>
    /// 关系代码→显示名 SQL（字典 FamilyRelationships，未命中回退原值；导入库 relationship 存代码如 Head）。
    /// 口径同 ImportedArchiveService.RelationshipDisplaySql，别名固定 p（人员表）。
    /// </summary>
    public const string ImportRelationshipDisplaySql =
        "COALESCE((SELECT item_value FROM nc_dict_items WHERE category = 'FamilyRelationships' AND item_key = p.relationship AND is_active), NULLIF(p.relationship, ''))";

    #endregion
}
