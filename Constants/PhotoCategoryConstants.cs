namespace NewCosmos.Constants;

/// <summary>
/// 档案照片类别常量（手机端采集身份证/户口本等照片存档的分类标定）。
/// 类别相对固定，集中在此维护；后续扩展直接加常量即可。
/// </summary>
public static class PhotoCategoryConstants
{
    public const string ID_CARD = "ID_CARD";                          // 身份证
    public const string HOUSEHOLD_REGISTER = "HOUSEHOLD_REGISTER";    // 户口本
    public const string PERSON_PHOTO = "PERSON_PHOTO";                // 本人照片/证件照（文书嵌入用）
    public const string MEDICAL_RECORD = "MEDICAL_RECORD";            // 病历/诊断证明
    public const string DISABILITY_CERT = "DISABILITY_CERT";          // 残疾证
    public const string BANK_CARD = "BANK_CARD";                      // 银行卡/一卡通
    public const string INCOME_PROOF = "INCOME_PROOF";                // 收入/务工证明
    public const string PUBLICITY_CHECK = "PUBLICITY_CHECK";          // 公示/核查照片
    public const string OTHER_CERT = "OTHER_CERT";                    // 其他证件照

    /// <summary>全部有效类别（供 Picker 绑定）。</summary>
    public static readonly (string Code, string Name)[] All =
    {
        (ID_CARD, "身份证"),
        (HOUSEHOLD_REGISTER, "户口本"),
        (PERSON_PHOTO, "本人照片/证件照"),
        (MEDICAL_RECORD, "病历/诊断证明"),
        (DISABILITY_CERT, "残疾证"),
        (BANK_CARD, "银行卡/一卡通"),
        (INCOME_PROOF, "收入/务工证明"),
        (PUBLICITY_CHECK, "公示/核查照片"),
        (OTHER_CERT, "其他证件照"),
    };

    /// <summary>类别代码 → 中文显示名。</summary>
    public static string GetDisplayName(string? category) => category switch
    {
        ID_CARD => "身份证",
        HOUSEHOLD_REGISTER => "户口本",
        PERSON_PHOTO => "本人照片/证件照",
        MEDICAL_RECORD => "病历/诊断证明",
        DISABILITY_CERT => "残疾证",
        BANK_CARD => "银行卡/一卡通",
        INCOME_PROOF => "收入/务工证明",
        PUBLICITY_CHECK => "公示/核查照片",
        OTHER_CERT => "其他证件照",
        _ => "其他"
    };
}
