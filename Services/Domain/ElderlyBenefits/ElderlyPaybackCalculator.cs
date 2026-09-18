using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using Globalization = global::System.Globalization;

namespace NewCosmos.Services.Domain.ElderlyBenefits;

/// <summary>
/// 普惠高龄补贴补发计算器（分段计算）。
/// 规则：起算 = max(政策开始 2024-10, 满80周岁次月)；止算 = 受理当月（含）；
/// 逐月按档位取月标准，按档位变化切段，累加得补发金额。
/// 年龄档位自生日次月生效（生日当月不计）：80 起算、90/100 切档均取生日次月。
/// 纯静态工具：金额标准由调用方通过 getMonthlyAmount 委托注入（读标准配置表）。
/// </summary>
public static class ElderlyPaybackCalculator
{
    /// <summary>
    /// 计算补发分段。months = 满80周岁次月（与政策开始取大）到止算月（含）。
    /// </summary>
    /// <param name="birthDate">出生日期</param>
    /// <param name="identityFlag">身份比对结果（80-89档判断高/低标准）</param>
    /// <param name="endMonth">止算月（受理当月，yyyy-MM）</param>
    /// <param name="getMonthlyAmount">按类别代码取月标准的委托</param>
    public static ElderlyPaybackResult Calculate(
        DateTime birthDate,
        string? identityFlag,
        string endMonth,
        Func<string, decimal> getMonthlyAmount)
    {
        var result = new ElderlyPaybackResult();

        var start = MaxMonth(FirstDayOfMonth(ElderlyBenefitConstants.PolicyStartDate), MonthOf80(birthDate).AddMonths(1));
        if (!TryParseMonth(endMonth, out var end))
        {
            result.StartMonth = ToMonthString(start);
            result.EndMonth = endMonth;
            return result;
        }

        if (start > end)
        {
            // 受理月早于满80次月（理论不应发生）：无补发
            result.StartMonth = ToMonthString(start);
            result.EndMonth = endMonth;
            return result;
        }

        result.StartMonth = ToMonthString(start);
        result.EndMonth = endMonth;

        var segments = new List<ElderlyPaybackSegment>();
        ElderlyPaybackSegment? current = null;
        var cursor = start;

        while (cursor <= end)
        {
            var ageAtMonth = GetAgeAtMonth(birthDate, cursor);
            var category = GetCategoryForAge(ageAtMonth, identityFlag);
            var monthlyAmount = getMonthlyAmount(category);

            if (current == null || current.CategoryCode != category || current.MonthlyAmount != monthlyAmount)
            {
                current = new ElderlyPaybackSegment
                {
                    CategoryCode = category,
                    MonthlyAmount = monthlyAmount,
                    SegmentStartMonth = ToMonthString(cursor)
                };
                segments.Add(current);
            }

            current.SegmentEndMonth = ToMonthString(cursor);
            current.Months++;
            current.SegmentAmount = current.MonthlyAmount * current.Months;

            cursor = cursor.AddMonths(1);
        }

        result.Segments = segments;
        result.TotalMonths = segments.Sum(s => s.Months);
        result.TotalAmount = segments.Sum(s => s.SegmentAmount);

        return result;
    }

    /// <summary>
    /// 满80周岁当月（yyyy-MM-01）；补发起算需再取次月（生日当月不计）。
    /// </summary>
    private static DateTime MonthOf80(DateTime birthDate)
    {
        var date = birthDate.AddYears(ElderlyBenefitConstants.Threshold80);
        return new DateTime(date.Year, date.Month, 1);
    }

    /// <summary>
    /// 计算某月适用的周岁年龄档（按上月最后一天判断：生日在本月及以后仍按上一档，
    /// 即年龄档自生日次月生效——生日当月不计，与补发起算口径一致）
    /// </summary>
    public static int GetAgeAtMonth(DateTime birthDate, DateTime monthStart)
    {
        var refDate = monthStart.AddDays(-1);
        var age = refDate.Year - birthDate.Year;
        if (birthDate.Date > refDate.AddYears(-age)) age--;
        return age;
    }

    /// <summary>
    /// 按年龄与身份判断享受类别（类别复核重评复用，判档口径唯一）
    /// </summary>
    public static string GetCategoryForAge(int age, string? identityFlag)
    {
        if (age >= ElderlyBenefitConstants.Threshold100) return ElderlyBenefitConstants.Cat100Plus;
        if (age >= ElderlyBenefitConstants.Threshold90) return ElderlyBenefitConstants.Cat90To99;
        if (age >= ElderlyBenefitConstants.Threshold80)
            return ElderlyBenefitConstants.IsHighSubsidyIdentity(identityFlag)
                ? ElderlyBenefitConstants.CatLowSubsidy
                : ElderlyBenefitConstants.CatOtherElderly;
        return ElderlyBenefitConstants.CatOtherElderly;
    }

    private static DateTime MaxMonth(DateTime a, DateTime b) => a >= b ? a : b;

    private static DateTime FirstDayOfMonth(DateTime d) => new(d.Year, d.Month, 1);

    /// <summary>
    /// 解析 yyyy-MM 为当月1号；失败返回 false
    /// </summary>
    private static bool TryParseMonth(string month, out DateTime value)
    {
        if (DateTime.TryParseExact(month, "yyyy-MM", Globalization.CultureInfo.InvariantCulture,
                Globalization.DateTimeStyles.None, out var parsed))
        {
            value = new DateTime(parsed.Year, parsed.Month, 1);
            return true;
        }
        value = default;
        return false;
    }

    private static string ToMonthString(DateTime d) => d.ToString("yyyy-MM");
}
