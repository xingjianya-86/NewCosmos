namespace NewCosmos.Models.Results;

/// <summary>
/// 身份证 OCR 识别结果。
/// P4a 阶段仅抽取「姓名 + 身份证号」；性别/出生日期/年龄由身份证号推导，不依赖 OCR。
/// </summary>
public sealed class IdCardInfo
{
    /// <summary>姓名（识别失败为空）</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>身份证号（识别失败为空）</summary>
    public string IdCard { get; init; } = string.Empty;

    /// <summary>身份证号校验码是否通过（OCR 可能个别位误识，未通过需人工核对）</summary>
    public bool IdCardChecksumValid { get; init; }

    /// <summary>OCR 原始文本行（按行自上而下；用于调试展示与人工核对）</summary>
    public List<string> RawLines { get; init; } = new();

    /// <summary>是否至少识别出一个可用字段</summary>
    public bool HasAny => !string.IsNullOrWhiteSpace(Name) || !string.IsNullOrWhiteSpace(IdCard);
}
