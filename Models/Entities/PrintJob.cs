namespace NewCosmos.Models.Entities;

/// <summary>
/// 推送打印任务队列表实体（nc_biz_print_jobs）。
/// 手机端入队（Pending），PC 打印代理认领（Processing）并回写结果（Completed/Failed）。
/// </summary>
public class PrintJob
{
    public long Id { get; set; }

    public string JobNo { get; set; } = string.Empty;

    public string BusinessType { get; set; } = string.Empty;

    public long? BusinessId { get; set; }

    public string? Classification { get; set; }

    public long? TemplateId { get; set; }

    public string? TemplateName { get; set; }

    public string? ApplicantName { get; set; }

    public string? ApplicantIdCard { get; set; }

    public string? PrinterName { get; set; }

    public int Copies { get; set; } = 1;

    public bool IsDuplex { get; set; }

    /// <summary>模板字段字典 JSON（Dictionary&lt;string,string&gt;）</summary>
    public string? FieldsJson { get; set; }

    /// <summary>表格行数据 JSON（List&lt;Dictionary&lt;string,string&gt;&gt;）</summary>
    public string? TableRowsJson { get; set; }

    /// <summary>赡养人表格数据 JSON</summary>
    public string? SupporterTableDataJson { get; set; }

    /// <summary>Pending / Processing / Completed / Failed</summary>
    public string Status { get; set; } = PrintJobConstants.StatusPending;

    public string? ErrorMessage { get; set; }

    public int? RequestedBy { get; set; }

    public string? RequestedByName { get; set; }

    public string? AgentMachine { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? ClaimedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    /// <summary>业务类型 + 分类展示（供界面绑定）</summary>
    public string BusinessDisplay =>
        string.IsNullOrEmpty(Classification) ? BusinessType : $"{BusinessType} / {Classification}";

    /// <summary>状态中文名（供界面绑定）</summary>
    public string StatusName => PrintJobConstants.GetStatusName(Status);

    /// <summary>创建时间显示</summary>
    public string CreatedAtDisplay => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
}

/// <summary>推送打印任务状态常量与显示映射</summary>
public static class PrintJobConstants
{
    public const string StatusPending = "Pending";
    public const string StatusProcessing = "Processing";
    public const string StatusCompleted = "Completed";
    public const string StatusFailed = "Failed";

    public static string GetStatusName(string? status) => status switch
    {
        StatusPending => "待打印",
        StatusProcessing => "打印中",
        StatusCompleted => "已完成",
        StatusFailed => "失败",
        _ => status ?? string.Empty
    };
}

/// <summary>推送打印入队请求（字段/表格已由业务侧构建完成，PC 代理直接重放打印）。</summary>
public class PrintJobRequest
{
    public string BusinessType { get; set; } = string.Empty;

    public long? BusinessId { get; set; }

    public string? Classification { get; set; }

    public long? TemplateId { get; set; }

    public string? TemplateName { get; set; }

    public string? ApplicantName { get; set; }

    public string? ApplicantIdCard { get; set; }

    public string? PrinterName { get; set; }

    public int Copies { get; set; } = 1;

    public bool IsDuplex { get; set; }

    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.Ordinal);

    public List<Dictionary<string, string>> TableRows { get; set; } = new();

    public List<Dictionary<string, string>>? SupporterTableData { get; set; }

    public int? RequestedBy { get; set; }

    public string? RequestedByName { get; set; }
}
