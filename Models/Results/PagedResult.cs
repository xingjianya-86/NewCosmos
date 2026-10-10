namespace NewCosmos.Models.Results;

/// <summary>
/// 分页查询结果
/// </summary>
/// <typeparam name="T">元素类型</typeparam>
public class PagedResult<T>
{
    /// <summary>
    /// 数据列表
    /// </summary>
    public List<T> Items { get; set; } = new();

    /// <summary>
    /// 当前页码（从 1 开始）
    /// </summary>
    public int PageIndex { get; set; } = 1;

    /// <summary>
    /// 每页条数
    /// </summary>
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// 总记录数
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 总页数
    /// </summary>
    public int TotalPages => PageSize > 0 ? Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize)) : 1;

    /// <summary>
    /// 是否有上一页
    /// </summary>
    public bool HasPreviousPage => PageIndex > 1;

    /// <summary>
    /// 是否有下一页
    /// </summary>
    public bool HasNextPage => PageIndex < TotalPages;

    /// <summary>
    /// 是否有数据
    /// </summary>
    public bool HasData => TotalCount > 0;

    /// <summary>
    /// 分页状态文本
    /// </summary>
    public string StatusText => TotalCount > 0
        ? $"第 {PageIndex}/{TotalPages} 页，共 {TotalCount} 条"
        : "无数据";

    /// <summary>
    /// 空结果
    /// </summary>
    public static PagedResult<T> Empty() => new();

    /// <summary>
    /// 从列表创建分页结果
    /// </summary>
    public static PagedResult<T> FromList(List<T> items, int pageIndex, int pageSize, int totalCount) => new()
    {
        Items = items,
        PageIndex = pageIndex,
        PageSize = pageSize,
        TotalCount = totalCount
    };
}