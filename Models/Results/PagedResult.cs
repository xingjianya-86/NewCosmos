namespace NewCosmos.Models.Results;

/// <summary>
/// ҳѯ
/// </summary>
/// <typeparam name="T"></typeparam>
public class PagedResult<T>
{
    /// <summary>
    /// б
    /// </summary>
    public List<T> Items { get; set; } = new();

    /// <summary>
    /// ǰҳ
    /// </summary>
    public int PageIndex { get; set; } = 1;

    /// <summary>
    /// ÿҳ
    /// </summary>
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// ܼ¼
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// ҳ
    /// </summary>
    public int TotalPages => PageSize > 0 ? Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize)) : 1;

    /// <summary>
    /// Ƿһҳ
    /// </summary>
    public bool HasPreviousPage => PageIndex > 1;

    /// <summary>
    /// Ƿһҳ
    /// </summary>
    public bool HasNextPage => PageIndex < TotalPages;

    /// <summary>
    /// Ƿ
    /// </summary>
    public bool HasData => TotalCount > 0;

    /// <summary>
    /// ҳ״̬ı
    /// </summary>
    public string StatusText => TotalCount > 0
        ? $"第 {PageIndex}/{TotalPages} 页bool 共 {TotalCount} 条"
        : "无数据";

    /// <summary>
    /// ս
    /// </summary>
    public static PagedResult<T> Empty() => new();

    /// <summary>
    /// бҳ
    /// </summary>
    public static PagedResult<T> FromList(List<T> items, int pageIndex, int pageSize, int totalCount) => new()
    {
        Items = items,
        PageIndex = pageIndex,
        PageSize = pageSize,
        TotalCount = totalCount
    };
}
