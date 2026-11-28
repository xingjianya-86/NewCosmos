using System.Text.RegularExpressions;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Helpers;

/// <summary>导入联动信息变更的构建结果。</summary>
public sealed class ElderlyInfoChangeImportBuildResult
{
    /// <summary>与申请主表当前值不同、可提交的字段明细。</summary>
    public List<ElderlyInfoChangeDetailInput> Details { get; } = new();

    /// <summary>Excel 是否带了任一信息变更候选列。</summary>
    public bool HasAnyValue { get; set; }

    /// <summary>银行卡号无法识别（科学计数法/非 12–30 位数字），该字段已跳过。</summary>
    public bool HasInvalidBankAccount { get; set; }
}

/// <summary>
/// 高龄年度认证导入 → 信息变更单的纯逻辑：
/// 只转换白名单字段，且只保留与申请主表当前值不同的项；身份/户籍/类别字段永不进入变更。
/// </summary>
public static class ElderlyInfoChangeImportHelper
{
    private static readonly Regex BankAccountPattern = new(@"^\d{12,30}$", RegexOptions.Compiled);
    private static readonly Regex ScientificNotationPattern = new(@"^\d(\.\d+)?[Ee][+]?\d+$", RegexOptions.Compiled);

    /// <summary>Excel 行是否带了任一信息变更候选值。</summary>
    public static bool HasValues(ElderlyCertImportRowPreview row)
        => !string.IsNullOrWhiteSpace(row.Phone)
           || !string.IsNullOrWhiteSpace(row.Address)
           || !string.IsNullOrWhiteSpace(row.BankName)
           || !string.IsNullOrWhiteSpace(row.BankAccount)
           || !string.IsNullOrWhiteSpace(row.AgentName)
           || !string.IsNullOrWhiteSpace(row.AgentRelation)
           || !string.IsNullOrWhiteSpace(row.AgentReceiveName)
           || !string.IsNullOrWhiteSpace(row.AgentReceiveRelation)
           || !string.IsNullOrWhiteSpace(row.AgentReceiveBankName)
           || !string.IsNullOrWhiteSpace(row.AgentReceiveBankAccount);

    /// <summary>
    /// 构建待提交字段：值去空白；与当前值完全相同则跳过；
    /// 银行卡字段必须是 12–30 位数字，科学计数法（Excel 已截断）直接标记为无法识别。
    /// </summary>
    public static ElderlyInfoChangeImportBuildResult Build(
        ElderlyCertImportRowPreview row,
        ElderlyInfoChangeApplicationValues current)
    {
        var result = new ElderlyInfoChangeImportBuildResult
        {
            HasAnyValue = HasValues(row)
        };

        void Add(string fieldKey, string? rawValue)
        {
            var newValue = rawValue?.Trim() ?? string.Empty;
            if (newValue.Length == 0) return;

            var oldValue = current.GetValue(fieldKey);
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) return;

            result.Details.Add(new ElderlyInfoChangeDetailInput
            {
                FieldKey = fieldKey,
                OldValue = oldValue,
                NewValue = newValue
            });
        }

        void AddBankAccount(string fieldKey, string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue)) return;

            var newValue = NormalizeBankAccount(rawValue);
            if (!BankAccountPattern.IsMatch(newValue))
            {
                result.HasInvalidBankAccount = true;
                return;
            }
            Add(fieldKey, newValue);
        }

        Add(ElderlyInfoChangeConstants.FieldPhone, row.Phone);
        Add(ElderlyInfoChangeConstants.FieldFamilyAddress, row.Address);
        Add(ElderlyInfoChangeConstants.FieldBankName, row.BankName);
        AddBankAccount(ElderlyInfoChangeConstants.FieldBankAccount, row.BankAccount);
        Add(ElderlyInfoChangeConstants.FieldAgentName, row.AgentName);
        Add(ElderlyInfoChangeConstants.FieldAgentRelation, row.AgentRelation);
        Add(ElderlyInfoChangeConstants.FieldAgentReceiveName, row.AgentReceiveName);
        Add(ElderlyInfoChangeConstants.FieldAgentReceiveRelation, row.AgentReceiveRelation);
        Add(ElderlyInfoChangeConstants.FieldAgentReceiveBankName, row.AgentReceiveBankName);
        AddBankAccount(ElderlyInfoChangeConstants.FieldAgentReceiveBankAccount, row.AgentReceiveBankAccount);

        return result;
    }

    /// <summary>
    /// 银行卡号只保留数字：去空格/连字符/前置单引号，全角转半角。
    /// 科学计数法（Excel 数值截断）无法还原，返回空串，由调用方按无法识别处理。
    /// </summary>
    public static string NormalizeBankAccount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var chars = value.Trim().TrimStart('\'')
            .Select(c => c is >= '０' and <= '９' ? (char)(c - 0xFEE0) : c)
            .Where(c => !char.IsWhiteSpace(c) && c != '-')
            .ToArray();
        var text = new string(chars);
        return ScientificNotationPattern.IsMatch(text) ? string.Empty : text;
    }
}
