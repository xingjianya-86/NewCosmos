namespace NewCosmos.Constants;

public static class AssetCheckStatusConstants
{
    public const string SUBMITTED = "0";
    public const string VERIFIED = "1";
    public const string INCLUDED = "2";
    public const string REFUSED = "3";

    public static string GetDescription(string status) => status switch
    {
        SUBMITTED => "已提交",
        VERIFIED => "已核查",
        INCLUDED => "已纳入低收入人口",
        REFUSED => "不予认定",
        _ => "未知"
    };

    public static bool IsSubmitted(string status) => status == SUBMITTED;
    public static bool IsVerified(string status) => status == VERIFIED;
    public static bool IsIncluded(string status) => status == INCLUDED;
    public static bool IsRefused(string status) => status == REFUSED;
}
