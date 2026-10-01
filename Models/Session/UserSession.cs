namespace NewCosmos.Models.Session;

/// <summary>
/// 登录会话快照（不含密码）。进程重启后用于恢复当前用户静态上下文。
/// </summary>
public sealed class UserSession
{
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
    public DateTime LoginAtUtc { get; set; }
}
