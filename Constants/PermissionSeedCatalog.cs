using NewCosmos.Models.Entities.UserManagement;

namespace NewCosmos.Constants;

/// <summary>
/// 权限系统唯一权威目录（单一事实来源）：
/// 默认角色、权限定义、角色-权限映射矩阵、角色数据范围均以此为准。
/// InitializationService（系统初始化建库）与 NewPermissionService（启动幂等补种/矩阵重建）共用本目录。
/// 矩阵或数据范围发生变更时，递增 <see cref="MatrixVersion"/>，
/// 启动检测到版本不一致即对 6 个默认角色执行一次性重建（旧映射清空后按矩阵重写）。
/// </summary>
public static class PermissionSeedCatalog
{
    /// <summary>权限矩阵版本：内容变更时递增；与库内版本不一致触发一次重建。</summary>
    public const int MatrixVersion = 4;

    /// <summary>缓存版本表 key：矩阵版本标记（nc_perm_cache_version）</summary>
    public const string MatrixVersionKey = "permission_matrix";

    /// <summary>
    /// 默认角色（固定 id；level 1-6，数值越小权限越高）。
    /// DataScope：功能权限与数据范围是两个维度——乡镇管理员功能权限与区县级等同，但数据范围按组织树限定。
    /// </summary>
    public static readonly (int Id, string Code, string Name, int Level, string Desc, string DataScope)[] Roles =
    {
        (1, "SUPER_ADMIN",   "超级管理员",   1, "系统超级管理员，拥有所有权限", DataScopeConstants.ALL),
        (2, "COUNTY_ADMIN",  "区县级管理员", 2, "区县级业务管理员", DataScopeConstants.ALL),
        (3, "TOWN_ADMIN",    "乡镇级管理员", 3, "乡镇街道级业务管理员（功能权限与区县级等同，数据范围限本乡镇及下级）", DataScopeConstants.ORG_AND_CHILDREN),
        (4, "TOWN_CLERK",    "乡镇级经办人", 4, "乡镇街道级业务经办人", DataScopeConstants.ORG_AND_CHILDREN),
        (5, "VILLAGE_CLERK", "村级经办人",   5, "村级业务经办人", DataScopeConstants.ORG),
        (6, "NORMAL_USER",   "普通用户",     6, "普通查询用户", DataScopeConstants.SELF),
    };

    /// <summary>权限定义全集（69 码）。module 用于权限配置页分组渲染；sort_order 决定渲染顺序。</summary>
    public static readonly (string Code, string Name, string Module, string Desc, int SortOrder)[] Permissions =
    {
        // 用户管理（10-15）
        ("USER_VIEW",           "查看用户",         "用户管理",   "查看用户列表和详情", 10),
        ("USER_CREATE",         "创建用户",         "用户管理",   "创建新用户", 11),
        ("USER_EDIT",           "编辑用户",         "用户管理",   "编辑用户信息", 12),
        ("USER_DELETE",         "删除用户",         "用户管理",   "删除用户", 13),
        ("USER_RESET_PASSWORD", "重置密码",         "用户管理",   "重置用户密码", 14),
        ("USER_MANAGE_STATUS",  "管理状态",         "用户管理",   "启用/禁用用户", 15),

        // 组织管理（20-23）
        ("ORG_VIEW",   "查看组织", "组织管理", "查看组织机构列表和详情", 20),
        ("ORG_CREATE", "创建组织", "组织管理", "创建新组织机构", 21),
        ("ORG_EDIT",   "编辑组织", "组织管理", "编辑组织机构信息", 22),
        ("ORG_DELETE", "删除组织", "组织管理", "删除组织机构", 23),

        // 角色管理（30-32）
        ("ROLE_VIEW",   "查看角色", "角色管理", "查看角色列表和详情", 30),
        ("ROLE_MANAGE", "管理角色", "角色管理", "创建和编辑角色", 31),
        ("ROLE_DELETE", "删除角色", "角色管理", "删除角色", 32),

        // 系统运维（40-43）
        ("SYSTEM_BACKUP",  "数据备份", "系统运维", "执行数据库备份与日志清理", 40),
        ("SYSTEM_RESTORE", "数据恢复", "系统运维", "执行数据库恢复", 41),
        ("SYSTEM_IMPORT",  "数据导入", "系统运维", "导入外部数据", 42),
        ("SYSTEM_EXPORT",  "数据导出", "系统运维", "导出系统数据", 43),

        // 数据中心（45-51）
        ("DB_CENTER_ACCESS",  "数据中心入口", "数据中心", "访问数据中心模块", 45),
        ("DICTIONARY_VIEW",   "查看字典",     "数据中心", "查看系统数据字典", 46),
        ("DICTIONARY_MANAGE", "管理字典",     "数据中心", "编辑系统数据字典", 47),
        ("REGION_VIEW",       "查看地区",     "数据中心", "查看地区数据", 48),
        ("REGION_MANAGE",     "管理地区",     "数据中心", "维护地区数据", 49),
        ("STANDARD_VIEW",     "查看标准",     "数据中心", "查看政策标准配置", 50),
        ("STANDARD_MANAGE",   "管理标准",     "数据中心", "编辑政策标准配置", 51),

        // 低收入救助（60-64）
        ("INCOME_VIEW",   "查看低收入", "低收入救助", "查看低收入家庭档案", 60),
        ("INCOME_CREATE", "创建低收入", "低收入救助", "创建低收入家庭申请", 61),
        ("INCOME_EDIT",   "编辑低收入", "低收入救助", "编辑低收入家庭信息", 62),
        ("INCOME_DELETE", "删除低收入", "低收入救助", "删除低收入家庭申请", 63),
        ("REPORT_VIEW",   "统计报表",   "低收入救助", "查看救助统计报表", 64),

        // 临时救助（70-73）
        ("TEMP_VIEW",   "查看临时救助", "临时救助", "查看临时救助申请", 70),
        ("TEMP_CREATE", "创建临时救助", "临时救助", "创建临时救助申请", 71),
        ("TEMP_EDIT",   "编辑临时救助", "临时救助", "编辑临时救助信息", 72),
        ("TEMP_DELETE", "删除临时救助", "临时救助", "删除临时救助申请", 73),

        // 信息变更管理（75-76）
        ("CHANGE_VIEW",   "查看信息变更", "信息变更管理", "查看信息变更与经济复核", 75),
        ("CHANGE_MANAGE", "办理信息变更", "信息变更管理", "办理成员/户主变更、死亡注销等变更业务", 76),

        // 一事一议（80-82）
        ("SPECIAL_APPROVAL_CREATE", "发起一事一议", "一事一议", "发起一事一议特殊救助申请", 80),
        ("SPECIAL_APPROVAL_REVIEW", "审核一事一议", "一事一议", "审核一事一议申请", 81),
        ("SPECIAL_APPROVAL_PRINT",  "打印一事一议", "一事一议", "打印一事一议表册", 82),

        // 近亲属备案（84）
        ("NEAR_RELATIVE_MANAGE", "近亲属备案", "近亲属备案", "维护近亲属备案信息", 84),

        // 普惠高龄（90-93）
        ("ELDERLY_VIEW",   "查看普惠高龄", "普惠高龄", "查看高龄补贴档案", 90),
        ("ELDERLY_CREATE", "创建普惠高龄", "普惠高龄", "创建高龄补贴申请", 91),
        ("ELDERLY_EDIT",   "编辑普惠高龄", "普惠高龄", "编辑高龄补贴信息", 92),
        ("ELDERLY_DELETE", "删除普惠高龄", "普惠高龄", "删除高龄补贴申请", 93),

        // 资产核查（100-105）
        ("ASSET_VIEW",   "查看资产核查", "资产核查", "查看资产核查记录", 100),
        ("ASSET_CREATE", "创建资产核查", "资产核查", "创建资产核查任务", 101),
        ("ASSET_EDIT",   "编辑资产核查", "资产核查", "编辑资产核查信息", 102),
        ("ASSET_DELETE", "删除资产核查", "资产核查", "删除资产核查记录", 103),
        ("ASSET_MAUDIT", "月度审核",     "资产核查", "资产核查月度审核", 104),
        ("ASSET_REPORT", "月度报表",     "资产核查", "生成资产核查月度报表", 105),

        // 档案管理（110-111）
        ("ARCHIVE_VIEW",   "查看救助档案", "档案管理", "查看救助档案与出具证明", 110),
        ("ARCHIVE_MANAGE", "档案产出管理", "档案管理", "档案产出、输出与月度公示", 111),

        // 文书模板（112）
        ("TEMPLATE_MANAGE", "文书模板管理", "文书模板", "管理文书模板与证明单位模板", 112),

        // 后补追缴（115-116）
        ("RECOVERY_VIEW",   "查看后补追缴", "后补追缴", "查看后补追缴记录", 115),
        ("RECOVERY_MANAGE", "办理后补追缴", "后补追缴", "新建/办理后补追缴业务", 116),

        // 值班管理（118-119）
        ("DUTY_VIEW",   "查看值班管理", "值班管理", "查看值班表、成员与节假日", 118),
        ("DUTY_MANAGE", "管理值班",     "值班管理", "生成/调整值班表、维护成员与节假日", 119),

        // 打印（125-132）：与 PrintExecuteService 的 businessType 中心映射一一对应
        ("PRINT_ASSETVERIFICATION",              "打印资产核查授权书", "打印", "打印资产核查授权书", 125),
        ("PRINT_FAMILYAPPLICATION",              "打印家庭申请档案",   "打印", "打印家庭申请档案", 126),
        ("PRINT_ASSETVERIFICATIONMONTHLYREPORT", "打印资产核查月报",   "打印", "打印资产核查月度报表", 127),
        ("PRINT_ECONOMICREVIEW",                 "打印经济复核档案",   "打印", "打印经济复核档案", 128),
        ("PRINT_ELDERLYBENEFITS",                "打印高龄津贴档案",   "打印", "打印高龄津贴档案", 129),
        ("PRINT_LOWINCOMEPROOF",                 "打印低保证明",       "打印", "打印低收入家庭低保证明文件", 130),
        ("PRINT_TEMPRELIEF",                     "打印临时救助档案",   "打印", "打印临时救助档案", 131),
        ("PRINT_RECOVERY",                       "打印追缴资金办理单", "打印", "打印后补追缴资金办理单", 132),

        // 彩票助手（135）
        ("LOTTERY_ACCESS", "彩票助手", "彩票助手", "使用彩票助手工具", 135),

        // 基础权限（140-143）
        ("APP_ACCESS",        "访问系统", "基础权限", "访问系统基本功能", 140),
        ("APP_FILE_UPLOAD",   "上传文件", "基础权限", "上传文件", 141),
        ("APP_FILE_DOWNLOAD", "下载文件", "基础权限", "下载文件", 142),
        ("APP_SEARCH",        "搜索功能", "基础权限", "使用搜索功能", 143),
    };

    /// <summary>全部权限码（去重集合，供断言/校验使用）</summary>
    public static readonly IReadOnlyCollection<string> AllPermissionCodes =
        Permissions.Select(p => p.Code).ToArray();

    /// <summary>
    /// 角色-权限矩阵（默认授予）。
    /// 打印权限跟随对应业务编辑权：PRINT_* ↔ 对应模块 EDIT（证明类 PRINT_LOWINCOMEPROOF 跟随 ARCHIVE_VIEW）。
    /// </summary>
    public static readonly Dictionary<string, string[]> RolePermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        // 超级管理员：全量
        ["SUPER_ADMIN"] = AllPermissionCodes.ToArray(),

        // 区县级管理员（功能权限与乡镇级管理员等同）
        ["COUNTY_ADMIN"] = new[]
        {
            "APP_ACCESS", "APP_FILE_UPLOAD", "APP_FILE_DOWNLOAD", "APP_SEARCH",
            "USER_VIEW", "USER_CREATE", "USER_EDIT", "USER_RESET_PASSWORD", "USER_MANAGE_STATUS",
            "ORG_VIEW", "ORG_CREATE", "ORG_EDIT",
            "ROLE_VIEW",
            "SYSTEM_EXPORT",
            "DB_CENTER_ACCESS", "DICTIONARY_VIEW", "REGION_VIEW", "STANDARD_VIEW",
            "INCOME_VIEW", "INCOME_CREATE", "INCOME_EDIT", "INCOME_DELETE", "REPORT_VIEW",
            "TEMP_VIEW", "TEMP_CREATE", "TEMP_EDIT", "TEMP_DELETE",
            "CHANGE_VIEW", "CHANGE_MANAGE",
            "SPECIAL_APPROVAL_CREATE", "SPECIAL_APPROVAL_REVIEW", "SPECIAL_APPROVAL_PRINT",
            "NEAR_RELATIVE_MANAGE",
            "ELDERLY_VIEW", "ELDERLY_CREATE", "ELDERLY_EDIT", "ELDERLY_DELETE",
            "ASSET_VIEW", "ASSET_CREATE", "ASSET_EDIT", "ASSET_DELETE", "ASSET_MAUDIT", "ASSET_REPORT",
            "ARCHIVE_VIEW", "ARCHIVE_MANAGE",
            "TEMPLATE_MANAGE",
            "RECOVERY_VIEW", "RECOVERY_MANAGE",
            "DUTY_VIEW", "DUTY_MANAGE",
            "LOTTERY_ACCESS",
            "PRINT_ASSETVERIFICATION", "PRINT_FAMILYAPPLICATION", "PRINT_ASSETVERIFICATIONMONTHLYREPORT",
            "PRINT_ECONOMICREVIEW", "PRINT_ELDERLYBENEFITS", "PRINT_LOWINCOMEPROOF",
            "PRINT_TEMPRELIEF", "PRINT_RECOVERY",
        },

        // 乡镇级管理员：功能权限与区县级等同（数据范围由 data_scope 限定为本组织及下级）
        ["TOWN_ADMIN"] = new[]
        {
            "APP_ACCESS", "APP_FILE_UPLOAD", "APP_FILE_DOWNLOAD", "APP_SEARCH",
            "USER_VIEW", "USER_CREATE", "USER_EDIT", "USER_RESET_PASSWORD", "USER_MANAGE_STATUS",
            "ORG_VIEW", "ORG_CREATE", "ORG_EDIT",
            "ROLE_VIEW",
            "SYSTEM_EXPORT",
            "DB_CENTER_ACCESS", "DICTIONARY_VIEW", "REGION_VIEW", "STANDARD_VIEW",
            "INCOME_VIEW", "INCOME_CREATE", "INCOME_EDIT", "INCOME_DELETE", "REPORT_VIEW",
            "TEMP_VIEW", "TEMP_CREATE", "TEMP_EDIT", "TEMP_DELETE",
            "CHANGE_VIEW", "CHANGE_MANAGE",
            "SPECIAL_APPROVAL_CREATE", "SPECIAL_APPROVAL_REVIEW", "SPECIAL_APPROVAL_PRINT",
            "NEAR_RELATIVE_MANAGE",
            "ELDERLY_VIEW", "ELDERLY_CREATE", "ELDERLY_EDIT", "ELDERLY_DELETE",
            "ASSET_VIEW", "ASSET_CREATE", "ASSET_EDIT", "ASSET_DELETE", "ASSET_MAUDIT", "ASSET_REPORT",
            "ARCHIVE_VIEW", "ARCHIVE_MANAGE",
            "TEMPLATE_MANAGE",
            "RECOVERY_VIEW", "RECOVERY_MANAGE",
            "DUTY_VIEW", "DUTY_MANAGE",
            "LOTTERY_ACCESS",
            "PRINT_ASSETVERIFICATION", "PRINT_FAMILYAPPLICATION", "PRINT_ASSETVERIFICATIONMONTHLYREPORT",
            "PRINT_ECONOMICREVIEW", "PRINT_ELDERLYBENEFITS", "PRINT_LOWINCOMEPROOF",
            "PRINT_TEMPRELIEF", "PRINT_RECOVERY",
        },

        // 乡镇级经办人
        ["TOWN_CLERK"] = new[]
        {
            "APP_ACCESS", "APP_FILE_UPLOAD", "APP_FILE_DOWNLOAD", "APP_SEARCH",
            "USER_VIEW",
            "INCOME_VIEW", "INCOME_CREATE", "INCOME_EDIT", "INCOME_DELETE",
            "TEMP_VIEW", "TEMP_CREATE", "TEMP_EDIT", "TEMP_DELETE",
            "ELDERLY_VIEW", "ELDERLY_CREATE", "ELDERLY_EDIT", "ELDERLY_DELETE",
            "ASSET_VIEW", "ASSET_CREATE", "ASSET_EDIT", "ASSET_DELETE",
            "CHANGE_VIEW", "CHANGE_MANAGE",
            "SPECIAL_APPROVAL_CREATE", "SPECIAL_APPROVAL_REVIEW", "SPECIAL_APPROVAL_PRINT",
            "NEAR_RELATIVE_MANAGE",
            "ARCHIVE_VIEW",
            "DUTY_VIEW", "DUTY_MANAGE",
            "LOTTERY_ACCESS",
            "PRINT_ASSETVERIFICATION", "PRINT_FAMILYAPPLICATION", "PRINT_ECONOMICREVIEW",
            "PRINT_ELDERLYBENEFITS", "PRINT_LOWINCOMEPROOF", "PRINT_TEMPRELIEF",
        },

        // 村级经办人
        ["VILLAGE_CLERK"] = new[]
        {
            "APP_ACCESS", "APP_FILE_UPLOAD", "APP_FILE_DOWNLOAD", "APP_SEARCH",
            "INCOME_VIEW", "INCOME_CREATE",
            "TEMP_VIEW", "TEMP_CREATE",
            "ELDERLY_VIEW", "ELDERLY_CREATE",
            "ASSET_VIEW", "ASSET_CREATE",
            "DUTY_VIEW", "DUTY_MANAGE",
            "LOTTERY_ACCESS",
        },

        // 普通用户（值班/彩票按决策对全部角色开放）
        ["NORMAL_USER"] = new[]
        {
            "APP_ACCESS", "APP_FILE_UPLOAD", "APP_FILE_DOWNLOAD", "APP_SEARCH",
            "DUTY_VIEW", "DUTY_MANAGE",
            "LOTTERY_ACCESS",
        },
    };

    /// <summary>按角色代码取数据范围（未知角色回退 SELF）</summary>
    public static string GetDataScope(string roleCode) =>
        Roles.FirstOrDefault(r => string.Equals(r.Code, roleCode, StringComparison.OrdinalIgnoreCase)).DataScope
        ?? DataScopeConstants.SELF;
}
