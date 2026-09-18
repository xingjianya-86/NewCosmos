namespace NewCosmos.Models.Requests;

/// <summary>
/// 分页请求参数（带安全校验）    /// </summary>
public class PaginationRequest
{
    private int _page = 1;
    private int _pageSize = 20;

    /// <summary>
    /// 最大每页条数    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// 最大页码    /// </summary>
    public const int MaxPage = 10000;

    /// <summary>
    /// 默认每页条数
    /// </summary>
    public const int DefaultPageSize = 20;

    /// <summary>
    /// 当前页码（自动校验范围）
    /// </summary>
    public int Page
    {
        get => _page;
        set => _page = Math.Clamp(value, 1, MaxPage);
    }

    /// <summary>
    /// 每页条数（自动校验范围）
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, MaxPageSize);
    }

    /// <summary>
    /// 计算偏移量    /// </summary>
    public int CalculateOffset() => (Page - 1) * PageSize;

    /// <summary>
    /// 重置为默认值    /// </summary>
    public void Reset()
    {
        _page = 1;
        _pageSize = DefaultPageSize;
    }
}