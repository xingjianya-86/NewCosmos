namespace NewCosmos.Constants;

/// <summary>
/// 高龄年度认证报表 formKey（桌面与 Web 共用，口径见
/// NewCosmosWeb/docs/API_CONTRACT_ELDERLY_CERTIFICATION_REPORT.md）。
/// 本批只做 Excel：ROSTER / SUMMARY / UNVERIFIED，PDF/打印在下一批。
/// </summary>
public static class ElderlyCertificationReportFormKeys
{
    /// <summary>年度认证花名册（批次全部任务）</summary>
    public const string Roster = "ROSTER";

    /// <summary>年度认证情况汇总表（按乡镇/村聚合）</summary>
    public const string Summary = "SUMMARY";

    /// <summary>未认证人员名单（Pending/Refused/Failed/NotLocated 且未停发）</summary>
    public const string Unverified = "UNVERIFIED";

    /// <summary>报表 key + 中文名（界面勾选顺序即此顺序）。</summary>
    public static readonly (string Key, string Name)[] Options =
    {
        (Roster, "年度认证花名册"),
        (Summary, "年度认证情况汇总表"),
        (Unverified, "未认证人员名单")
    };

    public static bool IsValid(string? formKey) =>
        !string.IsNullOrWhiteSpace(formKey)
        && Options.Any(o => string.Equals(o.Key, formKey, StringComparison.Ordinal));

    public static string GetName(string? formKey) => formKey switch
    {
        Roster => "年度认证花名册",
        Summary => "年度认证情况汇总表",
        Unverified => "未认证人员名单",
        _ => formKey ?? string.Empty
    };

    public static IReadOnlyList<string> AllKeys { get; } = Options.Select(o => o.Key).ToArray();
}
