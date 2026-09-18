using System.Collections.Concurrent;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.System;

namespace NewCosmos.Services.Core;

public class DictCacheService : BaseService, IDictCacheService
{
    protected override string ServiceName => "DictCacheService";

    private readonly IDictionaryService _dictionaryService;

    /// <summary>
    /// 缓存快照：三份索引打包为一个不可变对象，整体原子替换。
    /// 原实现是三个独立字段分三次赋值，并发读者可能同时读到新 _cache 和旧 _valueToKey，
    /// 导致 GetValue/GetKeyByValue 返回互相矛盾的结果。
    /// </summary>
    private sealed record CacheSnapshot(
        ConcurrentDictionary<string, Dictionary<string, string>> Cache,
        ConcurrentDictionary<string, List<string>> OrderedKeys,
        ConcurrentDictionary<string, Dictionary<string, string>> ValueToKey);

    private volatile CacheSnapshot _snapshot = new(new(), new(), new());

    /// <summary>字典全量刷新的最小间隔（多台部署机字典变更的同步节拍）</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);
    private DateTime _lastRefreshUtc = DateTime.MinValue;

    private ConcurrentDictionary<string, Dictionary<string, string>> _cache => _snapshot.Cache;
    private ConcurrentDictionary<string, List<string>> _orderedKeys => _snapshot.OrderedKeys;
    private ConcurrentDictionary<string, Dictionary<string, string>> _valueToKey => _snapshot.ValueToKey;

    public DictCacheService(IDictionaryService dictionaryService, ILoggerService logger)
        : base(logger)
    {
        _dictionaryService = dictionaryService;
    }

    public async Task WarmupAsync(CancellationToken ct = default)
    {
        Logger.Info("开始预热字典缓");
        await RefreshAsync(ct);
        Logger.Info($"字典缓存预热完成，共 {_cache.Sum(kv => kv.Value.Count)} 条");
    }

    /// <summary>
    /// 按 TTL 惰性刷新：距上次全量刷新未超过 RefreshInterval 时直接返回，
    /// 避免主页每次 OnAppearing 都全量重载字典（2 次 DB 查询 + 内存重建 3 份索引）。
    /// 多台部署机字典变更最多 RefreshInterval 后自动同步。
    /// </summary>
    public async Task EnsureFreshAsync(CancellationToken ct = default)
    {
        if (DateTime.UtcNow - _lastRefreshUtc < RefreshInterval)
            return;
        _lastRefreshUtc = DateTime.UtcNow;
        await RefreshAsync(ct);
    }

    public string GetValue(string category, string key)
    {
        if (_cache.TryGetValue(category, out var items) && items.TryGetValue(key, out var value))
            return value;
        return key;
    }

    public decimal GetDecimal(string category, string key)
    {
        var val = GetValue(category, key);
        return decimal.TryParse(val, out var result) ? result : 0m;
    }

    public int GetInt(string category, string key)
    {
        var val = GetValue(category, key);
        return int.TryParse(val, out var result) ? result : 0;
    }

    public bool HasItem(string category, string key)
    {
        return _cache.TryGetValue(category, out var items) && items.ContainsKey(key);
    }

    public Dictionary<string, string> GetAllByCategory(string category)
    {
        return _cache.TryGetValue(category, out var items)
            ? new Dictionary<string, string>(items)
            : new Dictionary<string, string>();
    }

    public List<string> GetKeys(string category)
    {
        return _orderedKeys.TryGetValue(category, out var keys)
            ? new List<string>(keys)
            : new List<string>();
    }

    public string? GetKeyByValue(string category, string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (_valueToKey.TryGetValue(category, out var map) && map.TryGetValue(value, out var key))
            return key;
        return null;
    }

    public List<DictItemOption> GetOptions(string category)
    {
        if (!_cache.TryGetValue(category, out var items))
            return new List<DictItemOption>();

        return items.Select(kv => new DictItemOption
        {
            Key = kv.Key,
            Display = kv.Value
        }).ToList();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var categories = await _dictionaryService.GetCategoriesAsync(ct);
            if (!categories.IsSuccess || categories.Value == null)
            {
                Logger.Warn("刷新字典缓存失败：无法获取分类列");
                return;
            }

            // 一次查询取回全部字典项，内存中按分类分组，
            // 替代原先"每个分类一次查询"的 N+1 模式（分类数十个 = 数十次往返）。
            var allItems = await _dictionaryService.GetAllItemsAsync(ct);
            if (!allItems.IsSuccess || allItems.Value == null)
            {
                Logger.Warn("刷新字典缓存失败：无法获取字典项列表");
                return;
            }

            var itemsByCategory = allItems.Value
                .Where(i => !string.IsNullOrEmpty(i.Category))
                .GroupBy(i => i.Category)
                .ToDictionary(g => g.Key, g => g.ToList());

            var newCache = new ConcurrentDictionary<string, Dictionary<string, string>>();
            var newOrderedKeys = new ConcurrentDictionary<string, List<string>>();
            var newValueToKey = new ConcurrentDictionary<string, Dictionary<string, string>>();

            foreach (var category in categories.Value)
            {
                var categoryItems = itemsByCategory.TryGetValue(category.Category, out var list)
                    ? list
                    : new List<DictItemView>();

                var validItems = categoryItems.Where(i => !string.IsNullOrEmpty(i.ItemKey)).ToList();
                newCache[category.Category] = validItems
                    .ToDictionary(i => i.ItemKey, i => i.ItemValue ?? i.ItemKey);
                newOrderedKeys[category.Category] = validItems
                    .Select(i => i.ItemKey)
                    .ToList();
                newValueToKey[category.Category] = validItems
                    .Where(i => !string.IsNullOrEmpty(i.ItemValue))
                    .GroupBy(i => i.ItemValue!)
                    .ToDictionary(g => g.Key, g => g.First().ItemKey);
            }

            _snapshot = new CacheSnapshot(newCache, newOrderedKeys, newValueToKey); // 原子替换
            DictDisplayHelper.UpdateFromCache(this);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "失败");
        }
    }
}
