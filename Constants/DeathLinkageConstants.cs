namespace NewCosmos.Constants;

/// <summary>
/// 死亡联动待办（nc_biz_death_linkage_tasks）常量（2026-10-31 方案2：申报自动生成各域待办，有权限者承接）。
///
/// 定位铁律：本表是**派活表**，不是办结回写表——办没办成一律以目标域实时状态判定
/// （DeathStatementService.GetLinkageTasksAsync 的 live 条件），任务行 status=Completed
/// 仅为读取时自愈标记；申报作废/归档靠 JOIN 台账状态自动退出列表（不物理删行）。
///
/// 生成时机两道闸：① DeclareAsync 成功后按申报ID补齐；② 任何列表/计数读取前按范围幂等补齐
/// （覆盖申报后身份才变化的漏任务）。两道闸共用 EnsureLinkageTasksAsync 的域级 INSERT...SELECT。
/// </summary>
public static class DeathLinkageConstants
{
    #region 任务域（task_domain）

    /// <summary>普惠高龄在享/草稿 → 需办理停发（承接面：高龄待办列表「死亡待停发」Tab）</summary>
    public const string DomainElderlyStop = "ElderlyStop";

    /// <summary>低收入档案户主 → 需办理户主死亡变更（停旧建新/整户停止）</summary>
    public const string DomainArchiveHead = "ArchiveHead";

    /// <summary>低收入档案成员 → 需办理减员（原因：人员死亡）</summary>
    public const string DomainArchiveMember = "ArchiveMember";

    /// <summary>在校大学生 → 需办理退出登记</summary>
    public const string DomainCollege = "College";

    /// <summary>追缴在办（Draft）→ 需确认结案</summary>
    public const string DomainRecovery = "Recovery";

    /// <summary>全部任务域（生成/补齐按此顺序；顺序即列表展示顺序）</summary>
    public static readonly string[] AllDomains =
    {
        DomainElderlyStop, DomainArchiveHead, DomainArchiveMember, DomainCollege, DomainRecovery
    };

    /// <summary>任务域 → 中文名（列表/统计/审计用）</summary>
    public static string GetDomainName(string? domain) => domain switch
    {
        DomainElderlyStop => "高龄停发",
        DomainArchiveHead => "户主死亡变更",
        DomainArchiveMember => "档案成员减员",
        DomainCollege => "大学生退出",
        DomainRecovery => "追缴结案",
        _ => string.IsNullOrEmpty(domain) ? string.Empty : domain
    };

    #endregion

    #region 任务域 → 承接所需权限码（同域承接面 UI 过滤与联动卡禁用判定共用同一映射）

    /// <summary>
    /// 任务域 → 承接所需权限（PermissionCodes）。
    /// 村级经办（VILLAGE_CLERK）矩阵不含以下任何一项：只申报不办理，
    /// 各域承接面按此过滤后对村级隐藏/禁用，联动卡显示「需镇级办理」。
    /// </summary>
    public static string GetRequiredPermission(string domain) => domain switch
    {
        DomainElderlyStop => PermissionCodes.ELDERLY_EDIT,
        DomainArchiveHead => PermissionCodes.CHANGE_MANAGE,
        DomainArchiveMember => PermissionCodes.CHANGE_MANAGE,
        DomainCollege => PermissionCodes.COLLEGE_MANAGE,
        DomainRecovery => PermissionCodes.RECOVERY_MANAGE,
        _ => string.Empty
    };

    /// <summary>联动卡 NavKey → 承接所需权限（DeathStatementViewModel 联动卡禁用判定用）。</summary>
    public static string GetNavKeyPermission(string? navKey) => navKey switch
    {
        "ElderlyStop" => PermissionCodes.ELDERLY_EDIT,
        "HouseholdDeath" => PermissionCodes.CHANGE_MANAGE,
        "MemberChange" => PermissionCodes.CHANGE_MANAGE,
        "College" => PermissionCodes.COLLEGE_MANAGE,
        "Recovery" => PermissionCodes.RECOVERY_MANAGE,
        "NearRelative" => PermissionCodes.NEAR_RELATIVE_MANAGE,
        _ => string.Empty // None / Declare（手工录入）等无权限动作
    };

    /// <summary>联动卡涉及的全部权限码（VM 一次批量查询后缓存，避免每张卡单独查库）</summary>
    public static readonly string[] AllNavPermissions =
    {
        PermissionCodes.ELDERLY_EDIT, PermissionCodes.CHANGE_MANAGE,
        PermissionCodes.COLLEGE_MANAGE, PermissionCodes.RECOVERY_MANAGE,
        PermissionCodes.NEAR_RELATIVE_MANAGE
    };

    #endregion

    #region 任务状态（status）

    public const string StatusPending = "Pending";
    public const string StatusCompleted = "Completed";

    #endregion
}
