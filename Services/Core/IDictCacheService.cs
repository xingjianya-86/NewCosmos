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

    Task RefreshAsync(CancellationToken ct = default);

    /// <summary>按 TTL 惰性刷新：距上次全量刷新未超过间隔时直接返回，避免热路径反复重载</summary>
    Task EnsureFreshAsync(CancellationToken ct = default);
}
