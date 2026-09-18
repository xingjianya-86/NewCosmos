namespace NewCosmos.Models.Enums;

/// <summary>
/// 变更类型
/// </summary>
public enum ChangeType
{
    EconomicChange,
    MemberChange,
    HeadOnlyChange,
    HeadWithMemberChange
}

/// <summary>
/// 变更分类
/// </summary>
public enum ChangeCategory
{
    FundChange,
    MemberAttribute,
    MemberChange
}

/// <summary>
/// 打印状    /// </summary>
public enum PrintStatus
{
    Pending,
    Printing,
    Completed,
    Failed
}

/// <summary>
/// 归档类型
/// </summary>
public enum ArchiveType
{
    NewApplication,
    Change,
    Stop
}

/// <summary>
/// 数据来源类型
/// </summary>
public enum SourceType
{
    NewApplication,
    HistoricalImport,
    HistoricalLocal
}
