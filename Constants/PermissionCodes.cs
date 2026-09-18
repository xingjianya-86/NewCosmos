namespace NewCosmos.Constants;

/// <summary>
/// 权限代码常量 —— 代码中引用权限码的唯一入口。
/// 权限定义（名称/模块/排序）与角色-权限映射的权威来源是
/// <see cref="PermissionSeedCatalog"/>，本类仅声明代码中使用的常量。
/// 约定：{模块前缀}_{动作}；打印类为 PRINT_{业务类型大写}
/// （与 PrintExecuteService.PrintPermissionMap 的 businessType 中心映射一一对应）。
/// </summary>
public static class PermissionCodes
{
    #region 基础权限
    public const string APP_ACCESS = "APP_ACCESS";
    public const string APP_FILE_UPLOAD = "APP_FILE_UPLOAD";
    public const string APP_FILE_DOWNLOAD = "APP_FILE_DOWNLOAD";
    public const string APP_SEARCH = "APP_SEARCH";
    #endregion

    #region 用户管理
    public const string USER_VIEW = "USER_VIEW";
    public const string USER_CREATE = "USER_CREATE";
    public const string USER_EDIT = "USER_EDIT";
    public const string USER_DELETE = "USER_DELETE";
    public const string USER_RESET_PASSWORD = "USER_RESET_PASSWORD";
    public const string USER_MANAGE_STATUS = "USER_MANAGE_STATUS";
    #endregion

    #region 组织管理
    public const string ORG_VIEW = "ORG_VIEW";
    public const string ORG_CREATE = "ORG_CREATE";
    public const string ORG_EDIT = "ORG_EDIT";
    public const string ORG_DELETE = "ORG_DELETE";
    #endregion

    #region 角色管理
    public const string ROLE_VIEW = "ROLE_VIEW";
    public const string ROLE_MANAGE = "ROLE_MANAGE";
    public const string ROLE_DELETE = "ROLE_DELETE";
    #endregion

    #region 系统运维
    public const string SYSTEM_BACKUP = "SYSTEM_BACKUP";
    public const string SYSTEM_RESTORE = "SYSTEM_RESTORE";
    public const string SYSTEM_IMPORT = "SYSTEM_IMPORT";
    public const string SYSTEM_EXPORT = "SYSTEM_EXPORT";
    #endregion

    #region 数据中心
    public const string DB_CENTER_ACCESS = "DB_CENTER_ACCESS";
    public const string DICTIONARY_VIEW = "DICTIONARY_VIEW";
    public const string DICTIONARY_MANAGE = "DICTIONARY_MANAGE";
    public const string REGION_VIEW = "REGION_VIEW";
    public const string REGION_MANAGE = "REGION_MANAGE";
    public const string STANDARD_VIEW = "STANDARD_VIEW";
    public const string STANDARD_MANAGE = "STANDARD_MANAGE";
    #endregion

    #region 低收入救助
    public const string INCOME_VIEW = "INCOME_VIEW";
    public const string INCOME_CREATE = "INCOME_CREATE";
    public const string INCOME_EDIT = "INCOME_EDIT";
    public const string INCOME_DELETE = "INCOME_DELETE";
    public const string REPORT_VIEW = "REPORT_VIEW";
    #endregion

    #region 临时救助
    public const string TEMP_VIEW = "TEMP_VIEW";
    public const string TEMP_CREATE = "TEMP_CREATE";
    public const string TEMP_EDIT = "TEMP_EDIT";
    public const string TEMP_DELETE = "TEMP_DELETE";
    #endregion

    #region 信息变更管理
    public const string CHANGE_VIEW = "CHANGE_VIEW";
    public const string CHANGE_MANAGE = "CHANGE_MANAGE";
    #endregion

    #region 一事一议
    public const string SPECIAL_APPROVAL_CREATE = "SPECIAL_APPROVAL_CREATE";
    public const string SPECIAL_APPROVAL_REVIEW = "SPECIAL_APPROVAL_REVIEW";
    public const string SPECIAL_APPROVAL_PRINT = "SPECIAL_APPROVAL_PRINT";
    #endregion

    #region 近亲属备案
    public const string NEAR_RELATIVE_MANAGE = "NEAR_RELATIVE_MANAGE";
    #endregion

    #region 普惠高龄
    public const string ELDERLY_VIEW = "ELDERLY_VIEW";
    public const string ELDERLY_CREATE = "ELDERLY_CREATE";
    public const string ELDERLY_EDIT = "ELDERLY_EDIT";
    public const string ELDERLY_DELETE = "ELDERLY_DELETE";
    #endregion

    #region 资产核查
    public const string ASSET_VIEW = "ASSET_VIEW";
    public const string ASSET_CREATE = "ASSET_CREATE";
    public const string ASSET_EDIT = "ASSET_EDIT";
    public const string ASSET_DELETE = "ASSET_DELETE";
    public const string ASSET_MAUDIT = "ASSET_MAUDIT";
    public const string ASSET_REPORT = "ASSET_REPORT";
    #endregion

    #region 档案管理
    public const string ARCHIVE_VIEW = "ARCHIVE_VIEW";
    public const string ARCHIVE_MANAGE = "ARCHIVE_MANAGE";
    #endregion

    #region 文书模板
    public const string TEMPLATE_MANAGE = "TEMPLATE_MANAGE";
    #endregion

    #region 后补追缴
    public const string RECOVERY_VIEW = "RECOVERY_VIEW";
    public const string RECOVERY_MANAGE = "RECOVERY_MANAGE";
    #endregion

    #region 值班管理
    public const string DUTY_VIEW = "DUTY_VIEW";
    public const string DUTY_MANAGE = "DUTY_MANAGE";
    #endregion

    #region 彩票助手
    public const string LOTTERY_ACCESS = "LOTTERY_ACCESS";
    #endregion

    #region 打印（与 businessType 中心映射对应，勿手工拼串）
    public const string PRINT_ASSETVERIFICATION = "PRINT_ASSETVERIFICATION";
    public const string PRINT_FAMILYAPPLICATION = "PRINT_FAMILYAPPLICATION";
    public const string PRINT_ASSETVERIFICATIONMONTHLYREPORT = "PRINT_ASSETVERIFICATIONMONTHLYREPORT";
    public const string PRINT_ECONOMICREVIEW = "PRINT_ECONOMICREVIEW";
    public const string PRINT_ELDERLYBENEFITS = "PRINT_ELDERLYBENEFITS";
    public const string PRINT_LOWINCOMEPROOF = "PRINT_LOWINCOMEPROOF";
    public const string PRINT_TEMPRELIEF = "PRINT_TEMPRELIEF";
    public const string PRINT_RECOVERY = "PRINT_RECOVERY";
    #endregion
}
