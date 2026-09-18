using NewCosmos.Constants;

namespace NewCosmos.Helpers;

/// <summary>
/// 档案封面版权信息生成：根据户主（申请人）身份证出生月份取对应电信号，
/// 1-12 月对应第 1-12 号，无效/越界取第 13 号（昔涟）兜底。
/// </summary>
public static class CopyrightHelper
{
    /// <summary>
    /// 构建版权信息字符串
    /// </summary>
    /// <param name="idCard">申请人身份证号（18 位）</param>
    /// <returns>格式：&amp;-me13 {电信号} Powered by tangxiaolei design sua17.cn</returns>
    public static string BuildCopyrightInfo(string? idCard)
    {
        var signal = EternalRegressionData.Heirs[EternalRegressionData.Heirs.Length - 1].Signal;
        if (!string.IsNullOrWhiteSpace(idCard)
            && IdCardValidator.ExtractBirthDateBasic(idCard) is { } birth)
        {
            var month = birth.Month;
            if (month >= 1 && month <= 12)
                signal = EternalRegressionData.Heirs[month - 1].Signal;
        }
        return $"&-me13 {signal} Powered by tangxiaolei design sua17.cn";
    }
}
