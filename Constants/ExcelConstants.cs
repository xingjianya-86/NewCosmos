namespace NewCosmos.Constants;

/// <summary>
/// Excel 相关常量类
/// </summary>
public static class ExcelConstants
{
    /// <summary>
    /// 单个 Excel 文件最大数据行数（小于 Excel 硬性行上限 1,048,576，留出表头/版式行余量）。
    /// 超过该值（如全量核查月报导出）自动拆分为多个文件（文件名加 _1/_2 后缀）。
    /// </summary>
    public const int MaxRowsPerFile = 1_000_000;
}