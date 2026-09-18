namespace NewCosmos.Models.Results;

public record SchemaDifference
{
    public required string Database { get; init; }
    public required string TableName { get; init; }
    public required DifferenceType Type { get; init; }
    public required SeverityLevel Severity { get; init; }
    public string ColumnName { get; init; } = string.Empty;
    public required string Expected { get; init; }
    public required string Actual { get; init; }
    public required string Description { get; init; }
    public required string Suggestion { get; init; }

    public string SeverityIcon => Severity switch
    {
        SeverityLevel.Error => "🔴",
        SeverityLevel.Warning => "🟡",
        SeverityLevel.Info => "🔵",
        _ => ""
    };

    public string SeverityColor => Severity switch
    {
        SeverityLevel.Error => "#EF4444",
        SeverityLevel.Warning => "#F59E0B",
        SeverityLevel.Info => "#3B82F6",
        _ => "#6B7280"
    };

    public string TypeText => Type switch
    {
        DifferenceType.MissingTable => "缺失",
        DifferenceType.MissingColumn => "缺失",
        DifferenceType.TypeMismatch => "类型不匹",
        DifferenceType.NullableMismatch => "可空性不匹配",
        DifferenceType.MissingIndex => "缺失索引",
        DifferenceType.MissingConstraint => "缺失约束",
        DifferenceType.ExtraTable => "多余",
        DifferenceType.ExtraColumn => "多余",
        DifferenceType.MissingTableComment => "缺失表注",
        DifferenceType.MissingColumnComment => "缺失列注",
        _ => "未知"
    };
}

public enum DifferenceType
{
    MissingTable,
    MissingColumn,
    TypeMismatch,
    NullableMismatch,
    MissingIndex,
    MissingConstraint,
    ExtraTable,
    ExtraColumn,
    MissingTableComment,
    MissingColumnComment
}

public enum SeverityLevel
{
    Error,
    Warning,
    Info
}
