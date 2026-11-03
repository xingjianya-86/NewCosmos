namespace NewCosmos.Constants;

/// <summary>
/// OCR 识别字段键契约（唯一维护点）。
/// 与服务端 deploy\ocr\ocr_api.py 的 CATEGORY_FIELDS 严格一致——改一边必须同步另一边。
/// 服务端按类别构建 KIE schema 返回 fields[key]，客户端按此键取值回填。
/// </summary>
public static class OcrFieldKeys
{
    #region 身份证 (ID_CARD)

    /// <summary>姓名</summary>
    public const string Name = "name";

    /// <summary>身份证号</summary>
    public const string IdCard = "id_card";

    #endregion

    #region 户口本 (HOUSEHOLD_REGISTER)

    /// <summary>户主姓名</summary>
    public const string HeadName = "head_name";

    /// <summary>户籍地址</summary>
    public const string HukouAddress = "hukou_address";

    #endregion

    #region 银行卡 (BANK_CARD)

    /// <summary>开户行</summary>
    public const string BankName = "bank_name";

    /// <summary>银行卡号</summary>
    public const string CardNumber = "card_number";

    #endregion

    #region 病历/诊断证明 (MEDICAL_RECORD)

    /// <summary>诊断结论</summary>
    public const string Diagnosis = "diagnosis";

    /// <summary>医疗机构</summary>
    public const string Hospital = "hospital";

    /// <summary>诊断日期</summary>
    public const string Date = "date";

    #endregion

    #region 残疾证 (DISABILITY_CERT)

    /// <summary>残疾证号</summary>
    public const string CertNo = "cert_no";

    /// <summary>残疾类型</summary>
    public const string DisabilityType = "disability_type";

    /// <summary>残疾等级</summary>
    public const string DisabilityLevel = "disability_level";

    #endregion

    #region 收入/务工证明 (INCOME_PROOF)

    /// <summary>工作单位</summary>
    public const string Employer = "employer";

    /// <summary>收入金额（⚠️ §9 金额铁律：仅展示供手抄，禁止自动落金额字段）</summary>
    public const string Amount = "amount";

    /// <summary>收入期间</summary>
    public const string Period = "period";

    #endregion

    /// <summary>字段键 → 中文标签（分配页卡片渲染）</summary>
    public static string GetFieldLabel(string key) => key switch
    {
        Name => "姓名",
        IdCard => "身份证号",
        HeadName => "户主姓名",
        HukouAddress => "户籍地址",
        BankName => "开户行",
        CardNumber => "银行卡号",
        Diagnosis => "诊断结论",
        Hospital => "医疗机构",
        Date => "诊断日期",
        CertNo => "残疾证号",
        DisabilityType => "残疾类型",
        DisabilityLevel => "残疾等级",
        Employer => "工作单位",
        Amount => "收入金额",
        Period => "收入期间",
        _ => key
    };

    /// <summary>按类别取该类别的 KIE 字段键（与服务端 CATEGORY_FIELDS 同构）。
    /// 空数组 = 全文文本类（PUBLICITY_CHECK / OTHER_CERT）；null = 不参与识别（PERSON_PHOTO）。</summary>
    public static IReadOnlyList<string>? GetFields(string categoryCode) => categoryCode switch
    {
        PhotoCategoryConstants.ID_CARD => new[] { Name, IdCard },
        PhotoCategoryConstants.HOUSEHOLD_REGISTER => new[] { HeadName, HukouAddress },
        PhotoCategoryConstants.BANK_CARD => new[] { BankName, CardNumber },
        PhotoCategoryConstants.MEDICAL_RECORD => new[] { Diagnosis, Hospital, Date },
        PhotoCategoryConstants.DISABILITY_CERT => new[] { CertNo, DisabilityType, DisabilityLevel },
        PhotoCategoryConstants.INCOME_PROOF => new[] { Employer, Amount, Period },
        PhotoCategoryConstants.PUBLICITY_CHECK => Array.Empty<string>(),
        PhotoCategoryConstants.OTHER_CERT => Array.Empty<string>(),
        _ => null, // PERSON_PHOTO 等：无需识别
    };
}
