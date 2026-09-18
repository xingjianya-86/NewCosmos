namespace NewCosmos.Constants;

/// <summary>
/// 模板文件类型统一口径：nc_biz_templates.file_type 规范值固定为小写 "xlsx" / "docx"。
/// 所有模板类型的判定、扩展名推导、入库写入一律经由本类，禁止散落手写判断。
/// </summary>
public static class TemplateFileTypes
{
    /// <summary>Excel 模板规范值</summary>
    public const string Excel = "xlsx";

    /// <summary>Word 模板规范值</summary>
    public const string Word = "docx";

    public static readonly string[] ValidExtensions = { ".docx", ".xlsx" };

    /// <summary>
    /// 归一化为规范值（写入口唯一闸口）。
    /// 兼容历史遗留的 "Excel"/"Word"/大小写变体，统一输出小写规范值。
    /// </summary>
    public static string Normalize(string? fileType) => fileType?.Trim().ToLowerInvariant() switch
    {
        "xlsx" or "excel" => Excel,
        "docx" or "word" => Word,
        _ => throw new NotSupportedException($"不支持的模板类型: {fileType}")
    };

    /// <summary>按模板类型推导输出文件扩展名（含前导点，小写）。</summary>
    public static string ToExtension(string fileType) =>
        Normalize(fileType) == Excel ? ".xlsx" : ".docx";

    /// <summary>判断是否为 Excel 类模板。</summary>
    public static bool IsExcel(string fileType) => Normalize(fileType) == Excel;
}
