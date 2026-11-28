namespace NewCosmos.Models.Results;

/// <summary>报表单元格格式（Excel 导出器据此设置数字/日期格式）。</summary>
public enum ElderlyCertificationReportColumnFormat
{
    Text,
    Integer,
    Money,
    Date,
    /// <summary>百分数数值（如 95.5 表示 95.5%），Excel 显示为 95.5%。</summary>
    Percent1
}

/// <summary>报表列定义：key 用于取值，name 是表头中文，width 是最小列宽，format 决定单元格格式。</summary>
public sealed class ElderlyCertificationReportColumn
{
    public ElderlyCertificationReportColumn(
        string key,
        string name,
        double width,
        ElderlyCertificationReportColumnFormat format = ElderlyCertificationReportColumnFormat.Text)
    {
        Key = key;
        Name = name;
        Width = width;
        Format = format;
    }

    public string Key { get; }
    public string Name { get; }
    public double Width { get; }
    public ElderlyCertificationReportColumnFormat Format { get; }
}

/// <summary>报表数据行：按列 key 取值的只读字典，空值表示导出为空单元格。</summary>
public sealed class ElderlyCertificationReportRow
{
    private readonly Dictionary<string, object?> _values;

    public ElderlyCertificationReportRow(IEnumerable<KeyValuePair<string, object?>> values)
    {
        _values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
            _values[key] = value;
    }

    public static ElderlyCertificationReportRow From(params (string Key, object? Value)[] cells) =>
        new(cells.Select(c => new KeyValuePair<string, object?>(c.Key, c.Value)));

    public IReadOnlyDictionary<string, object?> Values => _values;

    public object? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;
}

/// <summary>
/// 一份高龄年度认证报表：formKey/title/列定义/数据行。
/// 数据行由 Core 服务统一拼装，桌面与 Web 共用同一套列定义。
/// </summary>
public sealed class ElderlyCertificationReport
{
    public ElderlyCertificationReport(
        string formKey,
        string title,
        IReadOnlyList<ElderlyCertificationReportColumn> columns,
        IReadOnlyList<ElderlyCertificationReportRow> rows)
    {
        FormKey = formKey;
        Title = title;
        Columns = columns;
        Rows = rows;
    }

    public string FormKey { get; }
    public string Title { get; }
    public IReadOnlyList<ElderlyCertificationReportColumn> Columns { get; }
    public IReadOnlyList<ElderlyCertificationReportRow> Rows { get; }

    /// <summary>总行数（SUMMARY 含最后一行合计）。</summary>
    public int TotalRows => Rows.Count;
}

#region 查询源行（服务内部映射用，公开以便纯逻辑单元测试）

/// <summary>花名册查询行（nc_biz_elderly_cert_tasks + 乡镇/村名）。</summary>
public sealed class ElderlyCertificationRosterSourceRow
{
    public string? TownName { get; set; }
    public string? VillageName { get; set; }
    public string IdCard { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; }
    public int? AgeSnapshot { get; set; }
    public string? Category { get; set; }
    public decimal? IssueAmount { get; set; }
    public string? HukouAddress { get; set; }
    public string? FamilyAddress { get; set; }
    public string? Phone { get; set; }
    public string? VerifyMethod { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string CertStatus { get; set; } = string.Empty;
    public string EvidenceStatus { get; set; } = string.Empty;
    public string StopDecision { get; set; } = string.Empty;
    public string? ResultNote { get; set; }
}

/// <summary>汇总查询行（按乡镇/村聚合的计数）。</summary>
public sealed class ElderlyCertificationSummarySourceRow
{
    public string? TownName { get; set; }
    public string? VillageName { get; set; }
    public int Total { get; set; }
    public int Verified { get; set; }
    public int Exempt { get; set; }
    public int Pending { get; set; }
    public int Refused { get; set; }
    public int Failed { get; set; }
    public int Deceased { get; set; }
    public int MovedOut { get; set; }
    public int NotLocated { get; set; }
    public int Stopped { get; set; }
    public int EvidenceIssue { get; set; }
}

/// <summary>未认证名单查询行（认证任务 + 应停/跟进/停发信息）。</summary>
public sealed class ElderlyCertificationUnverifiedSourceRow
{
    public string IdCard { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; }
    public int? AgeSnapshot { get; set; }
    public string? TownName { get; set; }
    public string? VillageName { get; set; }
    public string? Phone { get; set; }
    public string CertStatus { get; set; } = string.Empty;
    public string EvidenceStatus { get; set; } = string.Empty;
    public DateTime? DueStopDate { get; set; }
    public DateTime? DeathDate { get; set; }
    public string StopDecision { get; set; } = string.Empty;
    public DateTime? FollowUpDate { get; set; }
    public string? StopNote { get; set; }
    public string? ResultNote { get; set; }
}

#endregion
