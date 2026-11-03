namespace NewCosmos.Constants;

/// <summary>
/// 职位名（nc_sys_users.position 的业务约定值，数据字典 DictionaryTypeCodes.Positions 同步维护）。
/// 原为 3 处 VM 各自硬编码字面量（月报主持人默认值/临救乡镇验收人/核对经办人），改名即静默失配。
/// </summary>
public static class PositionTitles
{
    /// <summary>民政助理（月报会议主持人默认值、临时救助乡镇验收人默认值、核对经办人默认值的职位匹配键）</summary>
    public const string CivilAssistant = "民政助理";
}
