namespace NewCosmos.Models.NavigationData;

/// <summary>
/// 打印/档案输出流程的跨页面传参。
/// ⚠️ FieldData/TableData 含公民 PII（姓名、身份证、收入）——
/// 离开输出流程时必须调用 Clear()（ArchiveOutputPage.OnDisappearing 在页面弹出导航栈时负责），
/// 严禁让这些数据驻留到进程退出。
/// </summary>
public static class PrintNavigationData
{
    public static string BusinessType { get; set; } = string.Empty;
    public static long? BusinessId { get; set; }
    public static Dictionary<string, string> FieldData { get; set; } = new();
    public static List<Dictionary<string, string>> TableData { get; set; } = new();

    /// <summary>
    /// 赡养人表格数据（用于赡养费承诺书按人迭代生成PDF）
    /// </summary>
    public static List<Dictionary<string, string>>? SupporterTableData { get; set; }

    /// <summary>
    /// 近亲属备案对数据（用于档案近亲属两表按对迭代生成PDF，每对一页）
    /// 每行 = 一名工作人员 + 一名关联救助对象的字段集
    /// </summary>
    public static List<Dictionary<string, string>>? NearRelativePairs { get; set; }

    /// <summary>
    /// 家庭分类代码（如 "RuralSubsistence", "UrbanSubsistence" 等）
    /// 用于按分类查询模板配置的打印份数
    /// </summary>
    public static string Classification { get; set; } = string.Empty;

    /// <summary>
    /// 申请状态（如 "Completed", "Stopped" 等）
    /// 用于判断是否为停保档案，加载变更告知书模板
    /// </summary>
    public static string Status { get; set; } = string.Empty;

    /// <summary>
    /// 仅出文书模式：允许展示的模板名白名单（null/空=不按名单收窄）。
    /// 由 ApplicationFormViewModel.OpenDocumentOutputAsync 强白名单直出时写入；Output 分类模式一般不设。
    /// </summary>
    public static string[]? TemplateFilter { get; set; }

    /// <summary>
    /// 输出文书模式：模板分类集（null=按 BusinessType 默认分类）。
    /// 文档直出/变动场景传 ArchiveCategoryResolver.DocumentOperationCategories。
    /// </summary>
    public static string[]? OutputCategories { get; set; }

    /// <summary>
    /// 输出文书模式：操作位覆盖（传入 GetRecordCategories 的 operationOverride）。
    /// </summary>
    public static string? OperationOverride { get; set; }

    /// <summary>
    /// 输出文书模式：预勾选模板名（列表仍展示分类内全部，仅控制 IsSelected）。
    /// 告知书必选由调用方始终写入；人员变动/渐退/减发按业务事实追加。
    /// </summary>
    public static string[]? PrefilterTemplateNames { get; set; }

    /// <summary>
    /// 仅清「文书模式」四字段，保留记录型上下文（BusinessType/Classification/FieldData…）。
    /// 由整档型入口（高龄停发/复核、临救、追缴、资产核查、月报等）在写入记录上下文时调用，
    /// 防止上一次「仅出文书」会话的 OutputCategories/OperationOverride/Prefilter/TemplateFilter
    /// 静态残留被继承（曾致高龄停发档案输出误出「档案_渐退期审批表」等变动文书）。
    /// </summary>
    public static void ClearDocumentMode()
    {
        OutputCategories = null;
        OperationOverride = null;
        TemplateFilter = null;
        PrefilterTemplateNames = null;
    }

    public static void Clear()
    {
        BusinessType = string.Empty;
        BusinessId = null;
        // 置空引用释放 PII；读方均有 null 检查（历史行为即置 null，保持一致）
        FieldData = null;
        TableData = null;
        SupporterTableData = null;
        NearRelativePairs = null;
        Classification = string.Empty;
        Status = string.Empty;
        TemplateFilter = null;
        OutputCategories = null;
        OperationOverride = null;
        PrefilterTemplateNames = null;
    }
}
