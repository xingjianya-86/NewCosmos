namespace NewCosmos.Models.Entities;

using NewCosmos.Constants;

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

    /// <summary>任务类型：Template=模板文书（默认）, Photo=照片（手机采集照片远程打印）</summary>
    public string JobKind { get; set; } = PrintJobConstants.JobKindTemplate;

    /// <summary>照片二进制（JPEG，JobKind=Photo 时有值；PC 代理直接打印，无需回查照片表）</summary>
    public byte[]? PhotoData { get; set; }

    /// <summary>照片打印版式（PhotoLayoutConstants：整页/半页/证件原寸）</summary>
    public string? PhotoLayout { get; set; }

    /// <summary>照片类别（PhotoCategoryConstants，展示/留痕用）</summary>
    public string? PhotoCategory { get; set; }

    /// <summary>业务类型 + 分类展示（供界面绑定）</summary>
    public string BusinessDisplay =>
        string.IsNullOrEmpty(Classification) ? BusinessType : $"{BusinessType} / {Classification}";

    /// <summary>任务类型中文名（供界面绑定）</summary>
    public string JobKindName => PrintJobConstants.GetJobKindName(JobKind);

    /// <summary>照片类别中文名（供界面绑定，非照片任务为空）</summary>
    public string PhotoCategoryName =>
        string.IsNullOrEmpty(PhotoCategory) ? string.Empty : PhotoCategoryConstants.GetDisplayName(PhotoCategory);

    /// <summary>列表展示标题：照片任务显示「照片打印·类别」，模板任务显示模板名。</summary>
    public string DisplayTitle =>
        JobKind == PrintJobConstants.JobKindPhoto
            ? $"照片打印·{PhotoCategoryName}"
            : (TemplateName ?? "模板文书");

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

    /// <summary>任务类型：模板文书（走 PrintExecuteService 模板打印路径）</summary>
    public const string JobKindTemplate = "Template";

    /// <summary>任务类型：照片（手机采集照片，PC 代理直接打印图片）</summary>
    public const string JobKindPhoto = "Photo";

    public static string GetStatusName(string? status) => status switch
    {
        StatusPending => "待打印",
        StatusProcessing => "打印中",
        StatusCompleted => "已完成",
        StatusFailed => "失败",
        _ => status ?? string.Empty
    };

    public static string GetJobKindName(string? jobKind) => jobKind switch
    {
        JobKindPhoto => "照片",
        _ => "模板文书",
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

    /// <summary>任务类型：Template=模板文书（默认）, Photo=照片（手机采集照片远程打印）</summary>
    public string JobKind { get; set; } = PrintJobConstants.JobKindTemplate;

    /// <summary>照片二进制（JPEG，JobKind=Photo 时必填）</summary>
    public byte[]? PhotoData { get; set; }

    /// <summary>照片打印版式（PhotoLayoutConstants）</summary>
    public string? PhotoLayout { get; set; }

    /// <summary>照片类别（PhotoCategoryConstants，展示/留痕用）</summary>
    public string? PhotoCategory { get; set; }
}
