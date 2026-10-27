namespace NewCosmos.Constants;

public static class AssetCheckStatusConstants
{
    public const string SUBMITTED = "0";
    public const string VERIFIED = "1";
    public const string INCLUDED = "2";
    public const string REFUSED = "3";

    /// <summary>
    /// 单个核查报告 PDF 大小上限（写死）：业务实测一般 ≤300KB，留 5MB 安全上限，
    /// 防止 report_data(bytea) 无节制膨胀。超限文件在批次扫描阶段直接跳过并提示。
    /// </summary>
    public const long MaxReportFileSizeBytes = 5 * 1024 * 1024;

    public static string GetDescription(string status) => status switch
    {
        SUBMITTED => "已提交",
        VERIFIED => "有报告未建档",
        INCLUDED => "已建档（已纳入）",
        REFUSED => "不予认定",
        _ => "未知"
    };

    public static bool IsSubmitted(string status) => status == SUBMITTED;
    public static bool IsVerified(string status) => status == VERIFIED;
    public static bool IsIncluded(string status) => status == INCLUDED;
    public static bool IsRefused(string status) => status == REFUSED;
}
