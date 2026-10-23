namespace NewCosmos.Models.Session;

/// <summary>
/// 登录会话快照（不含密码）。数据库 nc_sys_user_sessions 是权威来源，
/// 本对象仅作 SecureStorage 离线缓存：进程重启后先本地读出，再用 SessionId 向数据库校验。
/// </summary>
public sealed class UserSession
{
    /// <summary>数据库会话令牌；为空表示升级前的历史缓存，需重新登录</summary>
    public Guid SessionId { get; set; }

    /// <summary>安装级设备标识（与登录时的 device_id 比对，防会话被复制到其他设备）</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>端类型：desktop / android</summary>
    public string DeviceKind { get; set; } = string.Empty;

    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int? OrganizationId { get; set; }
    public string? OrgName { get; set; }
    public string? CityName { get; set; }
    public string? CountyName { get; set; }
    public string? TownName { get; set; }
    public string? VillageName { get; set; }
    public string? OrgAddress { get; set; }

    /// <summary>登录时间（仅本地展示；有效期以数据库 expires_at 为准）</summary>
    public DateTime LoginAtUtc { get; set; }

    /// <summary>数据库签发的绝对过期时间（UTC）</summary>
    public DateTime ExpiresAtUtc { get; set; }
}
