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

    /// <summary>状态 → 中文（未知原样返回，禁止洗成空串）</summary>
    public static string GetStatusName(string? status) => status switch
    {
        StatusPending => "待核实",
        StatusVerified => "已核实",
        StatusRevoked => "已作废",
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
}
