namespace NewCosmos.Constants;

/// <summary>
/// 登录会话常量（nc_sys_user_sessions 的状态/端类型/结束原因唯一维护点）。
/// 会话权威记录在数据库，本机 SecureStorage 仅为离线缓存。
/// </summary>
public static class SessionConstants
{
    /// <summary>数据库配置类别（nc_config_system_standards.category）</summary>
    public const string ConfigCategory = "session";

    #region 会话状态

    /// <summary>活动会话（唯一可继续使用的状态）</summary>
    public const string StatusActive = "active";

    /// <summary>被同端新登录替换（顶号）</summary>
    public const string StatusReplaced = "replaced";

    /// <summary>被管理员强制下线 / 改密 / 禁用等安全操作结束</summary>
    public const string StatusKicked = "kicked";

    /// <summary>用户主动登出</summary>
    public const string StatusLogout = "logout";

    /// <summary>到期或被判定为离线陈旧会话</summary>
    public const string StatusExpired = "expired";

    #endregion

    #region 设备端类型

    public const string DeviceKindDesktop = "desktop";
    public const string DeviceKindAndroid = "android";

    #endregion

    #region 结束原因（end_reason）

    public const string EndReasonOtherDeviceLogin = "other_device_login";
    public const string EndReasonAdminKick = "admin_kick";
    public const string EndReasonUserLogout = "user_logout";
    public const string EndReasonPasswordChanged = "password_changed";
    public const string EndReasonAccountDisabled = "account_disabled";
    public const string EndReasonPermissionsChanged = "permissions_changed";
    public const string EndReasonExpired = "expired";
    public const string EndReasonDeviceMismatch = "device_mismatch";

    #endregion

    #region 会话策略

    /// <summary>策略：按端配额（桌面 N + 移动 M，同端互踢）</summary>
    public const int PolicyPerDevice = 1;

    /// <summary>策略：严格单会话（任何新登录踢掉所有旧会话）</summary>
    public const int PolicySingle = 2;

    /// <summary>策略：多端并存（只记录不踢）</summary>
    public const int PolicyMulti = 3;

    #endregion

    #region 默认值（DB 未配置时兜底）

    public const int DefaultPolicy = PolicyPerDevice;
    public const int DefaultMaxDesktop = 1;
    public const int DefaultMaxMobile = 1;
    public const int DefaultHeartbeatDesktopSeconds = 45;
    public const int DefaultHeartbeatMobileSeconds = 90;
    public const int DefaultOnlineWindowSeconds = 180;
    public const int DefaultAbsoluteDays = 7;
    public const int DefaultGraceSeconds = 30;

    /// <summary>会话记录保留天数（超过后清理，保留审计追溯窗口）</summary>
    public const int RetentionDays = 90;

    #endregion

    #region 配置键（nc_config_system_standards.config_key）

    public const string ConfigKeyPolicy = "session.policy";
    public const string ConfigKeyMaxDesktop = "session.max_desktop";
    public const string ConfigKeyMaxMobile = "session.max_mobile";
    public const string ConfigKeyHeartbeatDesktop = "session.heartbeat_desktop";
    public const string ConfigKeyHeartbeatMobile = "session.heartbeat_mobile";
    public const string ConfigKeyOnlineWindow = "session.online_window";
    public const string ConfigKeyAbsoluteDays = "session.absolute_days";
    public const string ConfigKeyGraceSeconds = "session.grace_seconds";

    #endregion

    /// <summary>结束原因 → 用户可读提示</summary>
    public static string GetEndedMessage(string? reason) => reason switch
    {
        EndReasonOtherDeviceLogin => "账号已在其他设备登录，本端已退出",
        EndReasonAdminKick => "账号已被管理员强制下线",
        EndReasonPasswordChanged => "密码已修改，请使用新密码重新登录",
        EndReasonAccountDisabled => "账号已被停用，请联系管理员",
        EndReasonPermissionsChanged => "账号权限已调整，请重新登录",
        EndReasonExpired => "登录已过期，请重新登录",
        EndReasonDeviceMismatch => "登录设备标识已变化，请重新登录",
        _ => "登录会话已失效，请重新登录"
    };
}
