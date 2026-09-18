namespace NewCosmos.Services.Core;

/// <summary>
/// 数据脱敏工具    /// </summary>
public static class DataMasker
{
    /// <summary>
    /// 脱敏身份证号（前6    /// </summary>
    /// <param name="idCard">身份证号</param>
    /// <returns>脱敏后的身份证号</returns>
    public static string MaskIdCard(string idCard)
    {
        if (string.IsNullOrEmpty(idCard)) return string.Empty;
        if (idCard.Length < 10) return idCard;
        return idCard[..6] + "****" + idCard[^4..];
    }

    /// <summary>
    /// 脱敏手机号（    /// </summary>
    /// <param name="phone">手机</param>
    /// <returns>脱敏后的手机</returns>
    public static string MaskPhone(string phone)
    {
        if (string.IsNullOrEmpty(phone)) return string.Empty;
        if (phone.Length < 7) return phone;
        return phone[..3] + "****" + phone[^4..];
    }

    /// <summary>
    /// 脱敏姓名（首字保留）
    /// </summary>
    /// <param name="name">姓名</param>
    /// <returns>脱敏后的姓名</returns>
    public static string MaskName(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        if (name.Length == 1) return name;
        return name[0] + new string('*', Math.Min(name.Length - 1, 2));
    }

    /// <summary>
    /// 脱敏银行账号（前4    /// </summary>
    /// <param name="account">银行账号</param>
    /// <returns>脱敏后的银行账号</returns>
    public static string MaskBankAccount(string account)
    {
        if (string.IsNullOrEmpty(account)) return string.Empty;
        if (account.Length < 8) return account;
        return account[..4] + "****" + account[^4..];
    }

    /// <summary>
    /// 通用脱敏方法（自动识别类型）
    /// </summary>
    /// <param name="value">要脱敏的</param>
    /// <returns>脱敏后的</returns>
    public static object Sanitize(object value)
    {
        if (value is null) return string.Empty;

        return value switch
        {
            string s when IsIdCard(s) => MaskIdCard(s),
            string s when IsPhone(s) => MaskPhone(s),
            string s when IsBankAccount(s) => MaskBankAccount(s),
            _ => value
        };
    }

    /// <summary>
    /// 脱敏字典中的敏感字段
    /// </summary>
    public static Dictionary<string, object> SanitizeDictionary(Dictionary<string, object> dict)
    {
        if (dict is null) return new Dictionary<string, object>();

        var sensitiveKeys = new[] { "id_card", "IdCard", "idCard", "phone", "Phone", "mobile", "bank_account", "BankAccount", "password", "Password" };

        var result = new Dictionary<string, object>();
        foreach (var kvp in dict)
        {
            var key = kvp.Key;
            var value = kvp.Value;

            if (Array.Exists(sensitiveKeys, sk => key.Contains(sk, StringComparison.OrdinalIgnoreCase)))
            {
                result[key] = Sanitize(value);
            }
            else
            {
                result[key] = value ?? string.Empty;
            }
        }

        return result;
    }

    /// <summary>
    /// 格式化日志上下文（自动脱敏）
    /// </summary>
    public static string FormatLogContext(params (string Key, object Value)[] context)
    {
        if (context is null || context.Length == 0)
            return string.Empty;

        var sanitized = context.Select(c => $"{c.Key}={Sanitize(c.Value)}");
        return string.Join(" | ", sanitized);
    }

    private static bool IsIdCard(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (s.Length != 18) return false;
        return s[..17].All(char.IsDigit) && (char.IsDigit(s[17]) || s[17] == 'X' || s[17] == 'x');
    }

    private static bool IsPhone(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (s.Length != 11) return false;
        return s.All(char.IsDigit) && s[0] == '1';
    }

    private static bool IsBankAccount(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (s.Length < 16 || s.Length > 19) return false;
        return s.All(char.IsDigit);
    }
}
