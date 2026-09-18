using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.System;

public interface IDictionaryService
{
    #region 基础查询（使用视图）
    
    Task<Result<List<DictCategoryView>>> GetCategoriesAsync(CancellationToken ct = default);

    Task<Result<List<DictItemView>>> GetItemsByCategoryAsync(string category, CancellationToken ct = default);

    /// <summary>一次查询返回所有分类的全部字典项（含 category 列），供缓存整体刷新使用，避免按分类 N+1 查询</summary>
    Task<Result<List<DictItemView>>> GetAllItemsAsync(CancellationToken ct = default);

    Task<Result<DictItemView>> GetItemByKeyAsync(string category, string itemKey, CancellationToken ct = default);

    #endregion

    #region Schema 管理

    Task<Result> EnsureTablesExistAsync(CancellationToken ct = default);

    Task<Result> SyncSchemaAsync(IProgress<ProgressContext> progress = null, CancellationToken ct = default);

    #endregion

    #region 数据初始化与管理

    Task<Result> InitializeFromSeedAsync(IProgress<ProgressContext> progress = null, CancellationToken ct = default);

    Task<Result> ClearSeedTablesAsync(CancellationToken ct = default);

    #endregion

    #region 写入操作（操作更新表）
    Task<Result> CreateCategoryAsync(DictCategoryUpdate category, CancellationToken ct = default);

    Task<Result> CreateItemAsync(DictItemUpdate item, CancellationToken ct = default);

    Task<Result> UpdateCategoryAsync(DictCategoryUpdate category, CancellationToken ct = default);

    Task<Result> UpdateItemAsync(DictItemUpdate item, CancellationToken ct = default);

    Task<Result> DeleteCategoryAsync(int categoryId, CancellationToken ct = default);

    Task<Result> DeleteItemAsync(int itemId, CancellationToken ct = default);

    #endregion
}
