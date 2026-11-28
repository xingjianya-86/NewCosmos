namespace NewCosmos.Constants;

/// <summary>
/// 普惠高龄年度身份认证常量（批次状态、认证状态、认证方式、凭证状态、导入匹配状态）。
/// 状态值统一用英文 key 落库，中文显示名只在界面/报表层映射，避免中文字面量散落。
/// </summary>
public static class ElderlyCertificationConstants
{
    #region 批次状态

    public const string CampaignStatusDraft = "Draft";
    public const string CampaignStatusCollecting = "Collecting";
    public const string CampaignStatusReviewing = "Reviewing";
    public const string CampaignStatusClosed = "Closed";

    public static readonly (string Key, string Name)[] CampaignStatusOptions =
    {
        (CampaignStatusDraft, "草稿"),
        (CampaignStatusCollecting, "收集中"),
        (CampaignStatusReviewing, "复核中"),
        (CampaignStatusClosed, "已关闭")
    };

    public static string GetCampaignStatusName(string? status) => status switch
    {
        CampaignStatusDraft => "草稿",
        CampaignStatusCollecting => "收集中",
        CampaignStatusReviewing => "复核中",
        CampaignStatusClosed => "已关闭",
        _ => status ?? string.Empty
    };

    #endregion

    #region 认证状态

    public const string CertStatusPending = "Pending";
    public const string CertStatusVerified = "Verified";
    public const string CertStatusRefused = "Refused";
    public const string CertStatusFailed = "Failed";
    public const string CertStatusDeceased = "Deceased";
    public const string CertStatusMovedOut = "MovedOut";
    public const string CertStatusNotLocated = "NotLocated";
    public const string CertStatusExempt = "Exempt";
    public const string CertStatusStopped = "Stopped";
    public const string CertStatusInvalid = "Invalid";

    public static readonly (string Key, string Name)[] CertStatusOptions =
    {
        (CertStatusPending, "待核查"),
        (CertStatusVerified, "已通过"),
        (CertStatusRefused, "拒绝核查"),
        (CertStatusFailed, "核查不通过"),
        (CertStatusDeceased, "已去世"),
        (CertStatusMovedOut, "已迁出"),
        (CertStatusNotLocated, "无法联系"),
        (CertStatusExempt, "免核查"),
        (CertStatusStopped, "已停发"),
        (CertStatusInvalid, "数据无效")
    };

    public static string GetCertStatusName(string? status) => status switch
    {
        CertStatusPending => "待核查",
        CertStatusVerified => "已通过",
        CertStatusRefused => "拒绝核查",
        CertStatusFailed => "核查不通过",
        CertStatusDeceased => "已去世",
        CertStatusMovedOut => "已迁出",
        CertStatusNotLocated => "无法联系",
        CertStatusExempt => "免核查",
        CertStatusStopped => "已停发",
        CertStatusInvalid => "数据无效",
        _ => status ?? string.Empty
    };

    /// <summary>是否已形成明确结论（不含待核查/无效）。</summary>
    public static bool IsConclusive(string? status) => status is
        CertStatusVerified or CertStatusRefused or CertStatusFailed or CertStatusDeceased
        or CertStatusMovedOut or CertStatusNotLocated or CertStatusExempt or CertStatusStopped;

    /// <summary>是否需要进入停发预览（不含已通过/免核查/已停发/无效）。</summary>
    public static bool IsStopCandidate(string? status) => status is
        CertStatusPending or CertStatusRefused or CertStatusFailed
        or CertStatusDeceased or CertStatusMovedOut or CertStatusNotLocated;

    /// <summary>
    /// 中文认证结果文本 → 状态 key。兼容腾讯收集表常见选项与同义写法；无法识别返回 Invalid。
    /// 注意：空文本返回 Pending（尚未填写结果），由调用方决定是否允许应用。
    /// </summary>
    public static string ParseCertStatus(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return CertStatusPending;

        var value = NormalizeText(text);
        if (value.Length == 0) return CertStatusPending;

        if (ContainsAny(value, "拒绝", "不配合", "拒绝核查", "拒不")) return CertStatusRefused;
        if (ContainsAny(value, "去世", "死亡", "已故", "亡故")) return CertStatusDeceased;
        if (ContainsAny(value, "迁出", "户籍迁出", "迁往")) return CertStatusMovedOut;
        if (ContainsAny(value, "无法联系", "联系不上", "失联", "查找不到", "找不到")) return CertStatusNotLocated;
        if (ContainsAny(value, "免认证", "免核查", "无需认证", "上门已核实", "视同认证")) return CertStatusExempt;
        if (ContainsAny(value, "未认证", "未核查", "待认证", "待核查", "未完成")) return CertStatusPending;
        if (ContainsAny(value, "不通过", "未通过", "核查不通过", "认证不通过", "不合格")) return CertStatusFailed;
        if (ContainsAny(value, "通过", "已认证", "已核查", "合格", "正常")) return CertStatusVerified;
        return CertStatusInvalid;
    }

    #endregion

    #region 认证方式

    public const string VerifyMethodFace = "Face";
    public const string VerifyMethodOnsite = "Onsite";
    public const string VerifyMethodVideo = "Video";
    public const string VerifyMethodPhoto = "Photo";
    public const string VerifyMethodOther = "Other";

    public static readonly (string Key, string Name)[] VerifyMethodOptions =
    {
        (VerifyMethodFace, "人脸识别"),
        (VerifyMethodOnsite, "上门核查"),
        (VerifyMethodVideo, "视频核查"),
        (VerifyMethodPhoto, "照片核查"),
        (VerifyMethodOther, "其他")
    };

    public static string GetVerifyMethodName(string? method) => method switch
    {
        VerifyMethodFace => "人脸识别",
        VerifyMethodOnsite => "上门核查",
        VerifyMethodVideo => "视频核查",
        VerifyMethodPhoto => "照片核查",
        VerifyMethodOther => "其他",
        _ => method ?? string.Empty
    };

    public static string ParseVerifyMethod(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return VerifyMethodOther;
        var value = NormalizeText(text);
        if (ContainsAny(value, "人脸", "刷脸", "APP", "app", "龙江", "识别")) return VerifyMethodFace;
        if (ContainsAny(value, "上门", "入户", "到家")) return VerifyMethodOnsite;
        if (ContainsAny(value, "视频", "微信")) return VerifyMethodVideo;
        if (ContainsAny(value, "照片", "拍照", "图片", "截图")) return VerifyMethodPhoto;
        return VerifyMethodOther;
    }

    #endregion

    #region 凭证状态

    public const string EvidenceStatusNone = "None";
    public const string EvidenceStatusComplete = "Complete";
    public const string EvidenceStatusPartial = "Partial";
    public const string EvidenceStatusMissing = "Missing";
    public const string EvidenceStatusAmbiguous = "Ambiguous";

    public static string GetEvidenceStatusName(string? status) => status switch
    {
        EvidenceStatusNone => "无凭证",
        EvidenceStatusComplete => "齐全",
        EvidenceStatusPartial => "部分缺失",
        EvidenceStatusMissing => "全部缺失",
        EvidenceStatusAmbiguous => "文件名歧义",
        _ => status ?? string.Empty
    };

    public const string EvidenceMatchMatched = "Matched";
    public const string EvidenceMatchUnmatched = "Unmatched";
    public const string EvidenceMatchAmbiguous = "Ambiguous";
    public const string EvidenceMatchManual = "Manual";

    public const string EvidenceMatchMethodExcelName = "ExcelName";
    public const string EvidenceMatchMethodExcelUrl = "ExcelUrl";
    public const string EvidenceMatchMethodManual = "Manual";

    public const string EvidenceTypeFace = "FACE";
    public const string EvidenceTypeOnsite = "ONSITE";
    public const string EvidenceTypeIdCard = "ID_CARD";
    public const string EvidenceTypeOther = "OTHER";

    public static string InferEvidenceType(string? fileName, string? verifyMethod)
    {
        var text = NormalizeText(fileName);
        if (ContainsAny(text, "身份证", "证件")) return EvidenceTypeIdCard;
        if (ContainsAny(text, "上门", "入户")) return EvidenceTypeOnsite;
        if (ContainsAny(text, "人脸", "认证", "截图", "识别")) return EvidenceTypeFace;
        return verifyMethod switch
        {
            VerifyMethodFace => EvidenceTypeFace,
            VerifyMethodOnsite => EvidenceTypeOnsite,
            _ => EvidenceTypeOther
        };
    }

    #endregion

    #region 停发决定

    public const string StopDecisionNone = "None";
    public const string StopDecisionPending = "Pending";
    public const string StopDecisionApproved = "Approved";
    public const string StopDecisionDeferred = "Deferred";
    public const string StopDecisionExcluded = "Excluded";

    public static string GetStopDecisionName(string? decision) => decision switch
    {
        StopDecisionNone => "未处理",
        StopDecisionPending => "待停发",
        StopDecisionApproved => "已停发",
        StopDecisionDeferred => "暂缓",
        StopDecisionExcluded => "已排除",
        _ => decision ?? string.Empty
    };

    #endregion

    #region 导入匹配状态

    public const string ImportRowMatched = "Matched";
    public const string ImportRowUnknown = "Unknown";
    public const string ImportRowNameMismatch = "NameMismatch";
    public const string ImportRowDuplicate = "Duplicate";
    public const string ImportRowInvalid = "Invalid";
    public const string ImportRowConflict = "Conflict";

    public static string GetImportRowMatchStatusName(string? status) => status switch
    {
        ImportRowMatched => "已匹配",
        ImportRowUnknown => "名单外",
        ImportRowNameMismatch => "姓名不一致",
        ImportRowDuplicate => "重复提交",
        ImportRowInvalid => "无效行",
        ImportRowConflict => "结果冲突",
        _ => status ?? string.Empty
    };

    public const string ImportBatchStatusPreviewed = "Previewed";
    public const string ImportBatchStatusApplied = "Applied";
    public const string ImportBatchStatusPartiallyApplied = "PartiallyApplied";
    public const string ImportBatchStatusRevoked = "Revoked";
    public const string ImportBatchStatusFailed = "Failed";

    #endregion

    #region 限制与默认值

    /// <summary>一人最多接收的凭证数，防止异常 ZIP 把单人名下塞爆。</summary>
    public const int MaxEvidencePerTask = 20;

    /// <summary>单张凭证最大字节数（10 MB）。</summary>
    public const long MaxEvidenceFileSizeBytes = 10 * 1024 * 1024;

    /// <summary>单次 ZIP 解压总量上限（2 GB），防 ZIP 炸弹。</summary>
    public const long MaxZipUncompressedBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>单次 ZIP 条目数量上限。</summary>
    public const int MaxZipEntries = 20000;

    /// <summary>允许的图片扩展名。</summary>
    public static readonly string[] AllowedImageExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };

    #endregion

    #region 文本规范化

    /// <summary>
    /// 认证结果/方式文本规范化：去空白、全角转半角、去标点干扰。
    /// 仅用于“包含”判断，不用于身份证号等精确值。
    /// </summary>
    public static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var chars = text.Trim().Select(NormalizeChar).ToArray();
        return new string(chars).Replace(" ", string.Empty).Replace("\t", string.Empty);
    }

    private static char NormalizeChar(char c)
    {
        if (c == '\u3000') return ' ';
        if (c >= '\uFF01' && c <= '\uFF5E') return (char)(c - 0xFEE0);
        return c;
    }

    private static bool ContainsAny(string value, params string[] keywords) =>
        keywords.Any(k => value.Contains(k, StringComparison.OrdinalIgnoreCase));

    #endregion
}
