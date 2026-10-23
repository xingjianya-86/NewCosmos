namespace NewCosmos.Models.Entities;

/// <summary>
/// 在线会话记录（nc_sys_user_sessions）。
/// 只承载数据库行映射，不放业务判断；状态/端类型常量见 SessionConstants。
/// </summary>
public sealed class UserSessionRecord
{
    public long Id { get; set; }
    public Guid SessionId { get; set; }
    public int UserId { get; set; }

    // 列表查询时联表带出，非表列
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? OrganizationName { get; set; }

    /// <summary>安装级设备标识（重装应用会变化，非硬件指纹）</summary>
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceKind { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;

    public DateTime LoginAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? EndReason { get; set; }
    public int? EndedBy { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? ClientIp { get; set; }

    /// <summary>是否在在线窗口内（最后活跃时间距今不超过阈值）</summary>
    public bool IsOnline { get; set; }
}
