namespace NewCosmos.Services.Templates;

/// <summary>
/// 模板引擎接口
/// 负责加载模板、替换占位符、填充表格、保存/导出/打印
/// </summary>
public interface ITemplateEngine : IDisposable
{
    /// <summary>
    /// 从字节数组加载模板
    /// </summary>
    void Load(byte[] templateData);

    /// <summary>
    /// 从文件路径加载模板
    /// </summary>
    void LoadFromFile(string filePath);

    /// <summary>
    /// 替换单个字段占位符
    /// </summary>
    void ReplaceField(string placeholder, string value);

    /// <summary>
    /// 批量替换字段占位符
    /// key=占位符（{户主姓名}），value=替换值
    /// </summary>
    void ReplaceFields(Dictionary<string, string> fields);

    /// <summary>
    /// 删除内容仅为单个待删占位符的整段（docx 段落去空行，如会议记录多余成员行）。
    /// 须在 ReplaceFields 之前调用（此时段内仍是占位符原文）。
    /// 非 Word 引擎为空实现。
    /// </summary>
    void RemovePlaceholderParagraphs(IReadOnlyCollection<string> placeholders) { }

    /// <summary>
    /// 删除指定行区间（含端行，startRow &lt;= endRow）。
    /// Excel 引擎用于"固定行数模板"分页时删除尾部空行（如公示名单每页 22 行，末页不足 22 户时删除空行）。
    /// 非 Excel 引擎为空实现。
    /// </summary>
    void DeleteRows(int startRow, int endRow) { }

    /// <summary>
    /// 替换表格行数据（如家庭成员列表）
    /// tableStartMarker=表格起始标记，tableEndMarker=表格结束标记
    /// 每行是一个字典，key=列占位符，value=替换值
    /// </summary>
    void ReplaceTableByPlaceholder(string tableStartMarker, string tableEndMarker, List<Dictionary<string, string>> rows);

    /// <summary>
    /// 印章模式渲染：将表格数据分块，每块用原模板副本渲染后合并
    /// </summary>
    /// <param name="templateData">原始模板字节数组</param>
    /// <param name="fields">单值字段替换</param>
    /// <param name="tablePlaceholders">表格占位符列（如 {家庭成员姓名}）</param>
    /// <param name="rows">表格数据</param>
    /// <param name="pageSize">每页行数（0=不分页）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>合并后的文档字节数组</returns>
    Task<byte[]> RenderWithPagingAsync(
        byte[] templateData,
        Dictionary<string, string> fields,
        List<string> tablePlaceholders,
        List<Dictionary<string, string>> rows,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// 打印机名称（null=使用默认打印机）
    /// </summary>
    string PrinterName { get; set; }

    /// <summary>
    /// 是否双面打印
    /// </summary>
    bool IsDuplex { get; set; }

    /// <summary>
    /// 保存到字节数组
    /// </summary>
    Task<byte[]> SaveAsync(CancellationToken ct = default);

    /// <summary>
    /// 保存到文件
    /// </summary>
    Task SaveToFileAsync(string outputPath, CancellationToken ct = default);

    /// <summary>
    /// 导出PDF到字节数组
    /// </summary>
    Task<byte[]> ExportPdfAsync(CancellationToken ct = default);

    /// <summary>
    /// 导出PDF到文件
    /// </summary>
    Task<string> ExportPdfToFileAsync(string outputDir, CancellationToken ct = default);

    /// <summary>
    /// 打印文档（从指定文件路径，不创建临时文件）
    /// </summary>
    Task PrintFromFileAsync(string filePath, int copies = 1, CancellationToken ct = default);

    /// <summary>
    /// 导出PDF（从指定文件路径，不创建临时文件）
    /// </summary>
    Task<byte[]> ExportPdfFromFileAsync(string sourceFilePath, string pdfOutputPath = null, CancellationToken ct = default);

    /// <summary>
    /// 获取模板中所有未替换的占位符（用于调试/校验）
    /// </summary>
    List<string> GetUnresolvedPlaceholders();
}

/// <summary>
/// 模板字段映射项（ConfigJson 中的结构    /// </summary>
public class TemplateFieldMapping
{
    /// <summary>
    /// 中文占位符（{户主姓名}）
    /// </summary>
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>
    /// 标准字段KEY（如 HEAD_name）
    /// </summary>
    public string FieldKey { get; set; } = string.Empty;

    /// <summary>
    /// 字段描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 默认值（占位符未匹配时使用）
    /// </summary>
    public string DefaultValue { get; set; } = string.Empty;

    /// <summary>
    /// 输出格式字符串（null=原值输出）
    /// 日期格式：yyyy年M月d日, yyyy-MM-dd, M月d日
    /// 数字格式：N2, C, P0
    /// </summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// 将原始值按 Format 格式化输出
    /// </summary>
    public static string ApplyFormat(string rawValue, string format)
    {
        if (string.IsNullOrWhiteSpace(rawValue) || string.IsNullOrWhiteSpace(format))
            return rawValue ?? string.Empty;

        try
        {
            if (format.StartsWith('y') || format.StartsWith('M'))
            {
                if (DateTime.TryParse(rawValue, out var dt))
                    return dt.ToString(format);
            }

            if (format.StartsWith('N') || format.StartsWith('C') || format.StartsWith('P') || format.StartsWith('D'))
            {
                if (decimal.TryParse(rawValue, out var num))
                    return num.ToString(format);
            }

            return rawValue;
        }
        catch
        {
            return rawValue;
        }
    }
}

/// <summary>
/// 模板表格列映射项（ConfigJson 中的结构    /// </summary>
public class TemplateTableMapping
{
    /// <summary>
    /// 表格起始标记占位符
    /// </summary>
    public string StartMarker { get; set; } = string.Empty;

    /// <summary>
    /// 表格结束标记占位符
    /// </summary>
    public string EndMarker { get; set; } = string.Empty;

    /// <summary>
    /// 所属字段分组代码（如 FamilyMember）
    /// </summary>
    public string GroupCode { get; set; } = string.Empty;

    /// <summary>
    /// 每页行数（印章模式：每页盖章填充的最大行数）
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// 列映射列表
    /// </summary>
    public List<TemplateFieldMapping> Columns { get; set; } = new();
}

/// <summary>
/// 模板ConfigJson完整结构
/// </summary>
public class TemplateConfig
{
    /// <summary>
    /// 字段映射列表（单值替换）
    /// </summary>
    public List<TemplateFieldMapping> Fields { get; set; } = new();

    /// <summary>
    /// 表格映射列表（多行替换）
    /// </summary>
    public List<TemplateTableMapping> Tables { get; set; } = new();

    /// <summary>
    /// 复合字段列表（占位符展开为含多个子占位符的格式块）
    /// </summary>
    public List<CompositeFieldConfig> CompositeFields { get; set; } = new();

    /// <summary>
    /// 按分类的打印份数映射（如 {"RuralSubsistence": 2, "UrbanSubsistence": 1}）
    /// 未配置的分类默认 1 份
    /// </summary>
    public Dictionary<string, int> CopiesByClassification { get; set; } = new();

    /// <summary>
    /// 是否按赡养人/照料人每人生成一份文档
    /// </summary>
    public bool CopiesBySupporter { get; set; }

    /// <summary>
    /// 是否为目录模板（动态生成目录内容）
    /// </summary>
    public bool IsDirectoryTemplate { get; set; }

    /// <summary>
    /// 索引字段投影规则（indexShifts）：
    /// 渲染前将索引字段槽位（如 FAMILY_MEMBER_NAME_1..N）移除前 Skip 个（通常是户主），
    /// 其余槽位前移，使同一份数据字典对不同模板呈现独立视图（如模板74 不含户主、模板15 含户主）。
    /// 无数据槽位由映射层 defaultValue 兜底。
    /// </summary>
    public List<IndexShiftRule> IndexShifts { get; set; } = new();

    /// <summary>
    /// 目录日期来源字段（如 SURVEY_DATE/APPLICATION_DATE/AUDIT_DATE）
    /// 档案目录中该模板行的"日期"列取此字段的值；未配置时回退申请日期
    /// </summary>
    public string DirectoryDateField { get; set; } = string.Empty;

    /// <summary>
    /// 目录模板配置
    /// </summary>
    public DirectoryConfig? DirectoryConfig { get; set; }

    private static readonly global::System.Text.Json.JsonSerializerOptions _fromJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly global::System.Text.Json.JsonSerializerOptions _toJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = global::System.Text.Json.JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// 从JSON字符串反序列化，兼容 {"elem": {...}} 和 {...} 两种格式
    /// </summary>
    public static TemplateConfig FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new TemplateConfig();

        // 快速检测是否需要展开 elem 格式
        if (json.Contains("\"elem\""))
        {
            try
            {
                var normalized = FlattenElemFormat(json);
                return global::System.Text.Json.JsonSerializer.Deserialize<TemplateConfig>(normalized, _fromJsonOptions)
                    ?? new TemplateConfig();
            }
            catch { }
        }

        return global::System.Text.Json.JsonSerializer.Deserialize<TemplateConfig>(json, _fromJsonOptions) ?? new TemplateConfig();
    }

    private static string FlattenElemFormat(string json)
    {
        using var doc = global::System.Text.Json.JsonDocument.Parse(json);
        using var stream = new global::System.IO.MemoryStream();
        using (var writer = new global::System.Text.Json.Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.Equals("fields", StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == global::System.Text.Json.JsonValueKind.Array)
                {
                    writer.WritePropertyName("fields");
                    writer.WriteStartArray();
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        if (item.TryGetProperty("elem", out var elem))
                            elem.WriteTo(writer);
                        else
                            item.WriteTo(writer);
                    }
                    writer.WriteEndArray();
                }
                else
                {
                    prop.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }
        return global::System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// 序列化为JSON字符串
    /// </summary>
    public string ToJson()
    {
        return global::System.Text.Json.JsonSerializer.Serialize(this, _toJsonOptions);
    }
}

/// <summary>
/// 目录模板配置
/// </summary>
public class DirectoryConfig
{
    /// <summary>
    /// 是否包含页眉
    /// </summary>
    public bool IncludeHeader { get; set; } = true;

    /// <summary>
    /// 是否包含页码
    /// </summary>
    public bool IncludePageNumbers { get; set; } = true;

    /// <summary>
    /// 打印顺序："first"=第一个, "last"=最后一个
    /// </summary>
    public string SortOrder { get; set; } = "first";
}

/// <summary>
/// 索引投影规则（IndexShiftRule）：
/// BaseKey=索引字段前缀（如 FAMILY_MEMBER_NAME），Skip=移除的前槽位数（通常 1 = 户主）。
/// 投影时移除 {BaseKey}_{Skip}，将 {BaseKey}_{Skip+1..N} 依次前移为 {BaseKey}_{1..N-Skip}。
/// 规则通过模板 config_json 的 indexShifts 数组声明，禁止硬编码在代码中。
/// </summary>
public class IndexShiftRule
{
    /// <summary>
    /// 索引字段前缀（如 FAMILY_MEMBER_NAME）
    /// </summary>
    public string BaseKey { get; set; } = string.Empty;

    /// <summary>
    /// 移除的前 N 个槽位（如 1 = 户主）
    /// </summary>
    public int Skip { get; set; } = 1;
}

/// <summary>
/// 复合字段配置（一个占位符展开为含多个子占位符的格式块）
/// </summary>
public class CompositeFieldConfig
{
    /// <summary>
    /// 复合占位符（{会议纪要}）
    /// </summary>
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>
    /// 展开模板（含子占位符，如 "会议时间：{会议时间}\n会议地点：{会议地点}"）
    /// </summary>
    public string Template { get; set; } = string.Empty;

    /// <summary>
    /// 子占位符列表（如 ["{会议时间}", "{会议地点}"]）
    /// </summary>
    public List<string> SubFields { get; set; } = new();
}
