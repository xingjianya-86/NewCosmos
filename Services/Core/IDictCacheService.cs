namespace NewCosmos.Services.Core;

using NewCosmos.Models.Results;

public interface IDictCacheService
{
    Task WarmupAsync(CancellationToken ct = default);

    string GetValue(string category, string key);
    decimal GetDecimal(string category, string key);
    int GetInt(string category, string key);
    bool HasItem(string category, string key);
    Dictionary<string, string> GetAllByCategory(string category);
    List<string> GetKeys(string category);

    string? GetKeyByValue(string category, string value);
    List<DictItemOption> GetOptions(string category);

    /// <summary>全量刷新；返回是否成功（失败保留旧快照）</summary>
    Task<bool> RefreshAsync(CancellationToken ct = default);

    /// <summary>
    /// 按 TTL 惰性刷新：距上次【成功】刷新未超过间隔时直接返回，避免热路径反复重载。
    /// 刷新失败不推进时间戳（2026-11 修复：原实现在刷新前置位，失败后 10 分钟降级窗口内
    /// 不再重试，字典陈旧被当成正常状态）——下个调用点立即重试。
    /// </summary>
    Task EnsureFreshAsync(CancellationToken ct = default);
}
