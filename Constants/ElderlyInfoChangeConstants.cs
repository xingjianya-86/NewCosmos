namespace NewCosmos.Constants;

/// <summary>
/// 普惠高龄年度认证·信息变更单常量（字段白名单、风险级别、状态、来源）。
/// 字段 key 与 <c>nc_biz_elderly_applications</c> 列名一一对应；身份/户籍/类别字段禁止走信息变更。
/// </summary>
public static class ElderlyInfoChangeConstants
{
    #region 字段 key

    public const string FieldPhone = "phone";
    public const string FieldFamilyAddress = "family_address";
    public const string FieldDetailAddress = "detail_address";
    public const string FieldBankName = "bank_name";
    public const string FieldBankAccount = "bank_account";
    public const string FieldAgentName = "agent_name";
    public const string FieldAgentRelation = "agent_relation";
    public const string FieldAgentReceiveName = "agent_receive_name";
    public const string FieldAgentReceiveRelation = "agent_receive_relation";
    public const string FieldAgentReceiveBankName = "agent_receive_bank_name";
    public const string FieldAgentReceiveBankAccount = "agent_receive_bank_account";

    /// <summary>
    /// 允许变更的字段（key / 中文名 / 风险 / 最大长度）。
    /// 风险 HIGH 的字段审批时需复核凭证；LOW 可直接审批。
    /// </summary>
    private static readonly (string Key, string Label, string Risk, int MaxLength)[] FieldDefinitions =
    {
        (FieldPhone, "联系电话", RiskLow, 20),
        (FieldFamilyAddress, "家庭住址", RiskLow, 200),
        (FieldDetailAddress, "家庭详细地址", RiskLow, 200),
        (FieldBankName, "社保卡开户行", RiskHigh, 100),
        (FieldBankAccount, "社保卡账号", RiskHigh, 50),
        (FieldAgentName, "代办人姓名", RiskHigh, 50),
        (FieldAgentRelation, "代办人关系", RiskHigh, 50),
        (FieldAgentReceiveName, "代领人姓名", RiskHigh, 50),
        (FieldAgentReceiveRelation, "代领人关系", RiskHigh, 50),
        (FieldAgentReceiveBankName, "代领人开户行", RiskHigh, 100),
        (FieldAgentReceiveBankAccount, "代领人账号", RiskHigh, 50)
    };

    private static readonly Dictionary<string, (string Key, string Label, string Risk, int MaxLength)> FieldMap =
        FieldDefinitions.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>允许变更的字段 key（顺序与合同一致）。</summary>
    public static readonly string[] AllowedFieldKeys =
        FieldDefinitions.Select(f => f.Key).ToArray();

    /// <summary>银行卡/代领卡字段：审批时展示“生效月”。</summary>
    public static readonly string[] BankFieldKeys =
    {
        FieldBankName, FieldBankAccount, FieldAgentReceiveBankName, FieldAgentReceiveBankAccount
    };

    #endregion

    #region 禁止字段（身份 / 户籍 / 类别）

    /// <summary>禁止直接变更的身份与类别字段。</summary>
    public static readonly string[] IdentityFieldKeys =
    {
        "name", "id_card", "category", "identity_flag"
    };

    /// <summary>户籍字段前缀：户籍变化可能影响资格，必须走迁出/停旧建新或复核流程。</summary>
    public const string HukouFieldPrefix = "hukou_";

    #endregion

    #region 风险级别

    public const string RiskLow = "LOW";
    public const string RiskHigh = "HIGH";

    #endregion

    #region 状态

    public const string StatusPending = "Pending";
    public const string StatusApproved = "Approved";
    public const string StatusRejected = "Rejected";
    public const string StatusCancelled = "Cancelled";

    public static readonly (string Key, string Name)[] StatusOptions =
    {
        (StatusPending, "待审批"),
        (StatusApproved, "已通过"),
        (StatusRejected, "已驳回"),
        (StatusCancelled, "已作废")
    };

    /// <summary>审批列表默认展示的状态（不含被新提交覆盖的 Cancelled）。</summary>
    public static readonly string[] ReviewListStatuses =
    {
        StatusPending, StatusApproved, StatusRejected
    };

    public static string GetStatusName(string? status) => status switch
    {
        StatusPending => "待审批",
        StatusApproved => "已通过",
        StatusRejected => "已驳回",
        StatusCancelled => "已作废",
        _ => status ?? string.Empty
    };

    #endregion

    #region 来源类型

    public const string SourceManual = "Manual";
    public const string SourceCertificationImport = "CertificationImport";
    public const string SourceCertification = "Certification";

    #endregion

    #region 查询方法

    /// <summary>规范化字段 key（大小写不敏感）；不在白名单返回 null。</summary>
    public static string? NormalizeFieldKey(string? fieldKey)
    {
        if (string.IsNullOrWhiteSpace(fieldKey)) return null;
        return FieldMap.TryGetValue(fieldKey.Trim(), out var definition) ? definition.Key : null;
    }

    /// <summary>字段是否在允许变更白名单内。</summary>
    public static bool IsAllowed(string? fieldKey)
        => !IsIdentityField(fieldKey) && NormalizeFieldKey(fieldKey) != null;

    /// <summary>是否属于禁止直接变更的身份/户籍/类别字段。</summary>
    public static bool IsIdentityField(string? fieldKey)
    {
        if (string.IsNullOrWhiteSpace(fieldKey)) return false;
        var key = fieldKey.Trim();
        if (key.StartsWith(HukouFieldPrefix, StringComparison.OrdinalIgnoreCase)) return true;
        return IdentityFieldKeys.Contains(key, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>字段中文名；未知 key 原样返回，避免界面空白。</summary>
    public static string GetFieldLabel(string? fieldKey)
    {
        if (string.IsNullOrWhiteSpace(fieldKey)) return string.Empty;
        return FieldMap.TryGetValue(fieldKey.Trim(), out var definition) ? definition.Label : fieldKey;
    }

    /// <summary>字段风险级别（LOW/HIGH）；未知 key 返回空串。</summary>
    public static string GetRisk(string? fieldKey)
    {
        if (string.IsNullOrWhiteSpace(fieldKey)) return string.Empty;
        return FieldMap.TryGetValue(fieldKey.Trim(), out var definition) ? definition.Risk : string.Empty;
    }

    /// <summary>字段最大长度；未知 key 返回 0（不限制）。</summary>
    public static int GetMaxLength(string? fieldKey)
    {
        if (string.IsNullOrWhiteSpace(fieldKey)) return 0;
        return FieldMap.TryGetValue(fieldKey.Trim(), out var definition) ? definition.MaxLength : 0;
    }

    public static bool IsHighRisk(string? fieldKey)
        => string.Equals(GetRisk(fieldKey), RiskHigh, StringComparison.Ordinal);

    /// <summary>本次变更是否包含银行卡字段（决定审批时是否必填/展示生效月）。</summary>
    public static bool RequiresEffectiveMonth(IEnumerable<string>? fieldKeys)
        => fieldKeys != null && fieldKeys.Any(k => BankFieldKeys.Contains(k, StringComparer.OrdinalIgnoreCase));

    /// <summary>变更单号：BG + yyyyMMddHHmmssfff（共 19 位）。</summary>
    public static string BuildChangeNo(DateTime time)
        => $"BG{time:yyyyMMddHHmmssfff}";

    /// <summary>默认生效月（下个月，YYYY-MM）。</summary>
    public static string GetDefaultEffectiveMonth(DateTime today)
        => today.AddMonths(1).ToString("yyyy-MM");

    /// <summary>校验 YYYY-MM 格式且月份合法。</summary>
    public static bool IsValidEffectiveMonth(string? month)
    {
        if (string.IsNullOrWhiteSpace(month) || month.Length != 7) return false;
        if (month[4] != '-') return false;
        if (!int.TryParse(month.AsSpan(0, 4), out var year) || year is < 2000 or > 2100) return false;
        if (!int.TryParse(month.AsSpan(5, 2), out var m) || m is < 1 or > 12) return false;
        return true;
    }

    #endregion
}
