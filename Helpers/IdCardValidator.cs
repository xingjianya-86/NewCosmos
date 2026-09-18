using System.IO;

namespace NewCosmos.Helpers;

/// <summary>
/// 身份证号验证    /// 支持 18 位身份证校验
/// </summary>
public static class IdCardValidator
{
    private static readonly int[] Weights = { 7, 9,10, 5, 8, 4, 2, 1, 6, 3, 7, 9,10, 5, 8, 4, 2 };
    private static readonly char[] CheckCodes = { '1', '0', 'X', '9', '8', '7', '6', '5', '4', '3', '2' };

    /// <summary>
    /// 验证身份证号是否有效
    /// </summary>
    public static bool IsValid(string idCard)
    {
        if (string.IsNullOrWhiteSpace(idCard)) return false;
        if (idCard.Length != 18) return false;

        var span = idCard.AsSpan();
        
        for (var i = 0; i < 17; i++)
        {
            if (!char.IsDigit(span[i])) return false;
        }

        var lastChar = char.ToUpperInvariant(span[17]);
        if (!char.IsDigit(lastChar) && lastChar != 'X') return false;

        var sum = 0;
        for (var i = 0; i < 17; i++)
        {
            sum += (span[i] - '0') * Weights[i];
        }

        return lastChar == CheckCodes[sum % 11];
    }

    /// <summary>
    /// 从身份证号提取出生日期
    /// </summary>
    public static DateTime? ExtractBirthDate(string idCard)
    {
        if (!IsValid(idCard)) return null;

        try
        {
            var year = int.Parse(idCard!.Substring(6, 4));
            var month = int.Parse(idCard.Substring(10, 2));
            var day = int.Parse(idCard.Substring(12, 2));
            return new DateTime(year, month, day);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从身份证号提取性别
    /// </summary>
    public static string? ExtractGender(string idCard)
    {
        if (!IsValid(idCard)) return null;
        var genderCode = int.Parse(idCard![16..17]);
        return genderCode % 2 == 1 ? "男" : "女";
    }

    /// <summary>
    /// 从身份证号提取年龄（带校验码验证）
    /// </summary>
    public static int? ExtractAge(string idCard)
    {
        var birthDate = ExtractBirthDate(idCard);
        if (birthDate == null) return null;

        var today = DateTime.Today;
        var age = today.Year - birthDate.Value.Year;
        if (birthDate.Value.Date > today.AddYears(-age)) age--;

        return age;
    }

    /// <summary>
    /// 从身份证号提取年龄（不带校验码验证，仅检查格式）
    /// </summary>
    public static int? ExtractAgeBasic(string idCard)
    {
        var birthDate = ExtractBirthDateBasic(idCard);
        if (birthDate == null) return null;

        var today = DateTime.Today;
        var age = today.Year - birthDate.Value.Year;
        if (birthDate.Value.Date > today.AddYears(-age)) age--;

        return age;
    }

    /// <summary>
    /// 从身份证号提取出生日期（不带校验码验证，仅检查格式）
    /// </summary>
    public static DateTime? ExtractBirthDateBasic(string idCard)
    {
        if (string.IsNullOrWhiteSpace(idCard) || idCard.Length != 18) return null;

        try
        {
            var year = int.Parse(idCard.Substring(6, 4));
            var month = int.Parse(idCard.Substring(10, 2));
            var day = int.Parse(idCard.Substring(12, 2));
            return new DateTime(year, month, day);
        }
        catch
        {
            return null;
        }
    }

}
