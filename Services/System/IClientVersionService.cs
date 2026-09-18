using NewCosmos.Models.Results;

namespace NewCosmos.Services.System;

/// <summary>客户端版本台账行</summary>
public sealed class ClientVersionRow
{
    public long Id { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public DateTime? FirstSeenAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
}

/// <summary>
/// 客户端版本台账服务：登录成功后上报本机版本（fire-and-forget），供县局查看升级情况。
/// </summary>
public interface IClientVersionService
{
    /// <summary>上报/更新本机客户端版本（按 client_id 幂等 upsert）</summary>
    Task<Result> ReportAsync(string appVersion, CancellationToken ct = default);

    /// <summary>读取最近上报的客户端台账（按最后在线倒序，最多 500 条）</summary>
    Task<Result<List<ClientVersionRow>>> GetListAsync(CancellationToken ct = default);
}
