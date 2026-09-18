using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using Globalization = global::System.Globalization;

namespace NewCosmos.Services.Domain.ElderlyBenefits;

/// <summary>
/// 普惠高龄打印字段构建器：把登记/停止记录及明细批次转换为模板 FieldData 字典。
/// </summary>
public static class ElderlyPrintDataBuilder
{
    public static Dictionary<string, string> BuildSingleFields(ElderlyApplication app)
    {
        var f = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FieldKeys.ELDERLY_NAME] = app.Name,
            [FieldKeys.ELDERLY_AGE] = app.Age?.ToString() ?? string.Empty,
            [FieldKeys.ELDERLY_PHONE] = app.Phone,
            [FieldKeys.ELDERLY_ID_CARD] = app.IdCard,
            [FieldKeys.ELDERLY_HUKOU_ADDRESS] = app.HukouAddress,
            [FieldKeys.ELDERLY_FAMILY_ADDRESS] = app.FamilyAddress,
            [FieldKeys.ELDERLY_BANK_NAME] = app.BankName,
            [FieldKeys.ELDERLY_BANK_ACCOUNT] = app.BankAccount,
            [FieldKeys.ELDERLY_AGENT_NAME] = app.AgentName,
            [FieldKeys.ELDERLY_AGENT_RELATION] = app.AgentRelation,
            [FieldKeys.ELDERLY_AGENT_RECEIVE_NAME] = app.AgentReceiveName,
            [FieldKeys.ELDERLY_AGENT_RECEIVE_RELATION] = app.AgentReceiveRelation,
            [FieldKeys.ELDERLY_AGENT_RECEIVE_BANK_NAME] = app.AgentReceiveBankName,
            [FieldKeys.ELDERLY_AGENT_RECEIVE_BANK_ACCOUNT] = app.AgentReceiveBankAccount,
            [FieldKeys.ELDERLY_AGENT_RECEIVE_REASON] = app.AgentReceiveReason,
            [FieldKeys.ELDERLY_CATEGORY] = ElderlyBenefitConstants.BuildCategoryCheckText(app.Category),
            [FieldKeys.ELDERLY_APPLY_DATE] = FormatDate(app.ApplyDate),
            [FieldKeys.ELDERLY_ACCEPT_DATE] = FormatDate(app.ApplyDate),
            [FieldKeys.ELDERLY_ISSUE_START_MONTH] = FormatMonth(app.IssueStartMonth),
            [FieldKeys.ELDERLY_ISSUE_AMOUNT] = FormatAmount(app.IssueAmount),
            [FieldKeys.ELDERLY_PAYBACK_RANGE] = BuildPaybackRange(app),
            [FieldKeys.ELDERLY_PAYBACK_AMOUNT] = FormatAmount(app.PaybackAmount),
            [FieldKeys.ELDERLY_PAYBACK_REASON] = ElderlyBenefitConstants.BuildPaybackReasonCheckText(app.PaybackReason),
            [FieldKeys.ELDERLY_STANDARD] = app.PaybackMonths > 0 ? FormatAmount(Math.Round(app.PaybackAmount / app.PaybackMonths, 2)) : string.Empty,
            [FieldKeys.ELDERLY_CONFIRM_DATE] = FormatDate(app.ConfirmedAt),
            [FieldKeys.ELDERLY_ACTUAL_AMOUNT] = FormatAmount(app.PaybackAmount),
            [FieldKeys.ELDERLY_CONTACT_NAME] = app.AgentName,
            [FieldKeys.ELDERLY_CONTACT_RELATION] = app.AgentRelation
        };

        if (app.Status == ElderlyBenefitConstants.StatusStopped)
        {
            // 停发场景：受理日期 = 停发操作时间（取消备案表使用）
            f[FieldKeys.ELDERLY_ACCEPT_DATE] = FormatDate(app.ActualStopDate ?? app.StoppedAt);
            f[FieldKeys.ELDERLY_STOP_REASON] = ElderlyBenefitConstants.BuildStopReasonCheckText(app.StopReason);
            f[FieldKeys.ELDERLY_DUE_STOP_DATE] = FormatDate(app.DueStopDate);
            f[FieldKeys.ELDERLY_ACTUAL_STOP_DATE] = FormatDate(app.ActualStopDate ?? app.StoppedAt);
            f[FieldKeys.ELDERLY_IS_RECOVER] = app.IsRecover ? "是" : "否";
            if (app.IsRecover)
            {
                f[FieldKeys.ELDERLY_RECOVER_RANGE] = BuildRecoverRange(app);
                f[FieldKeys.ELDERLY_RECOVER_AMOUNT] = FormatAmount(app.RecoverAmount);
            }
            else
            {
                f[FieldKeys.ELDERLY_RECOVER_RANGE] = string.Empty;
                f[FieldKeys.ELDERLY_RECOVER_AMOUNT] = FormatAmount(0m);
            }
            f[FieldKeys.ELDERLY_REMARK] = app.Remark;
            f[FieldKeys.ELDERLY_STOP_TIME] = FormatDate(app.ActualStopDate ?? app.StoppedAt);
        }

        return f;
    }

    /// <summary>
    /// 构建新增/停止明细表编号字段（ELDERLY_XXX_1..N）。rows=null 时用默认行数 13。
    /// historyAmounts: 历史档案"实际发放"金额映射（id_card → 发放金额），供停止明细表读取；
    /// 为空或未命中时回退计发金额（月标准）。
    /// </summary>
    public static Dictionary<string, string> BuildDetailFields(List<ElderlyApplication> records, bool isStop, int maxRows = 13, IReadOnlyDictionary<string, decimal>? historyAmounts = null)
    {
        var f = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < records.Count && i < maxRows; i++)
        {
            var idx = i + 1;
            var app = records[i];
            f[$"{FieldKeys.ELDERLY_INDEX}_{idx}"] = idx.ToString();
            f[$"{FieldKeys.ELDERLY_NAME}_{idx}"] = app.Name;
            f[$"{FieldKeys.ELDERLY_ID_CARD}_{idx}"] = app.IdCard;
            f[$"{FieldKeys.ELDERLY_AGE}_{idx}"] = app.Age?.ToString() ?? string.Empty;
            f[$"{FieldKeys.ELDERLY_GENDER}_{idx}"] = app.Gender;
            f[$"{FieldKeys.ELDERLY_STANDARD}_{idx}"] = FormatAmount(app.PaybackMonths > 0 ? Math.Round(app.PaybackAmount / app.PaybackMonths, 2) : 0m);
            f[$"{FieldKeys.ELDERLY_PAYBACK_TIME}_{idx}"] = BuildPaybackRange(app);
            f[$"{FieldKeys.ELDERLY_PAYBACK_MONTHS}_{idx}"] = app.PaybackMonths.ToString();
            f[$"{FieldKeys.ELDERLY_PAYBACK_AMOUNT}_{idx}"] = FormatAmount(app.PaybackAmount);
            // 新增明细表：实际发放 = 补发金额 + 一个月身份类别标准（计发月首月津贴）；
            // 停止明细表：实际发放读取历史档案发放金额（缺失回退计发金额/月标准，避免停发人员补发金额为 0 时误显 0）
            var actualAmount = isStop
                ? (historyAmounts != null && historyAmounts.TryGetValue(app.IdCard, out var historyAmount) ? historyAmount : app.IssueAmount)
                : app.PaybackAmount + MonthlyStandardAmount(app);
            f[$"{FieldKeys.ELDERLY_ACTUAL_AMOUNT}_{idx}"] = FormatAmount(actualAmount);
            f[$"{FieldKeys.ELDERLY_CONFIRM_DATE}_{idx}"] = FormatDate(app.ConfirmedAt);
            f[$"{FieldKeys.ELDERLY_CATEGORY}_{idx}"] = ElderlyBenefitConstants.GetCategoryName(app.Category);
            f[$"{FieldKeys.ELDERLY_ADDRESS}_{idx}"] = app.FamilyAddress;
            f[$"{FieldKeys.ELDERLY_CONTACT_NAME}_{idx}"] = app.AgentName;
            f[$"{FieldKeys.ELDERLY_CONTACT_RELATION}_{idx}"] = app.AgentRelation;
            f[$"{FieldKeys.ELDERLY_PHONE}_{idx}"] = app.Phone;
            f[$"{FieldKeys.ELDERLY_PAYBACK_REASON}_{idx}"] = ElderlyBenefitConstants.GetPaybackReasonName(app.PaybackReason);
            if (isStop)
            {
                f[$"{FieldKeys.ELDERLY_STOP_TIME}_{idx}"] = FormatDate(app.ActualStopDate ?? app.StoppedAt);
                // 停止明细表：停止原因只写选中的一项（直接填原因名称），不做多项勾选清单
                f[$"{FieldKeys.ELDERLY_STOP_REASON}_{idx}"] = ElderlyBenefitConstants.GetStopReasonName(app.StopReason);
                f[$"{FieldKeys.ELDERLY_FAMILY_ADDRESS}_{idx}"] = app.FamilyAddress;
            }
        }

        return f;
    }

    /// <summary>
    /// 构建《普惠高龄津贴调整备案表》字段（满90周岁月报，逐人一页；调整原因=年龄，不补差）。
    /// </summary>
    /// <param name="row">满90周岁名单行</param>
    /// <param name="acceptDate">受理日期（填报日）</param>
    /// <param name="monthText">调整时间（如 2026年9月）</param>
    public static Dictionary<string, string> BuildAge90AdjustFields(ElderlyAge90Row row, string acceptDate, string monthText)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FieldKeys.ELDERLY_ADJUST_NAME] = row.Name,
            [FieldKeys.ELDERLY_ADJUST_ID_CARD] = row.IdCard,
            [FieldKeys.ELDERLY_ADJUST_HUKOU_ADDRESS] = row.HukouAddress,
            [FieldKeys.ELDERLY_ADJUST_FAMILY_ADDRESS] = row.FamilyAddress,
            [FieldKeys.ELDERLY_ADJUST_REASON] = ElderlyBenefitConstants.BuildAdjustReasonCheckText(
                ElderlyBenefitConstants.AdjustReasonAge, false),
            [FieldKeys.ELDERLY_ADJUST_TIME] = monthText,
            [FieldKeys.ELDERLY_ADJUST_AMOUNT] = $"由{row.OldMonthlyAmount:F2}元/月调整至{row.NewMonthlyAmount:F2}元/月",
            [FieldKeys.ELDERLY_ADJUST_PAYBACK_RANGE] = "—",
            [FieldKeys.ELDERLY_ADJUST_PAYBACK_AMOUNT] = "—",
            [FieldKeys.ELDERLY_ADJUST_ACCEPT_DATE] = acceptDate
        };
    }

    /// <summary>
    /// 一个月身份类别月标准：优先用计发金额（受理时类别月标准）；为空时回退补发月均（防旧数据/除零）
    /// </summary>
    private static decimal MonthlyStandardAmount(ElderlyApplication app)
    {
        if (app.IssueAmount > 0)
            return app.IssueAmount;
        return app.PaybackMonths > 0 ? Math.Round(app.PaybackAmount / app.PaybackMonths, 2) : 0m;
    }

    private static string BuildPaybackRange(ElderlyApplication app)
    {
        if (string.IsNullOrEmpty(app.PaybackStartMonth) || string.IsNullOrEmpty(app.PaybackEndMonth))
            return string.Empty;
        var start = FormatMonth(app.PaybackStartMonth);
        var end = FormatMonth(app.PaybackEndMonth);
        return app.PaybackMonths > 0
            ? $"{start}至{end}，共{app.PaybackMonths}个月"
            : $"{start}至{end}";
    }

    private static string BuildRecoverRange(ElderlyApplication app)
    {
        if (string.IsNullOrEmpty(app.RecoverStartMonth) || string.IsNullOrEmpty(app.RecoverEndMonth))
            return string.Empty;
        var months = CountMonths(app.RecoverStartMonth, app.RecoverEndMonth);
        var start = FormatMonth(app.RecoverStartMonth);
        var end = FormatMonth(app.RecoverEndMonth);
        return months > 0 ? $"{start}至{end}，共{months}个月" : $"{start}至{end}";
    }

    private static int CountMonths(string start, string end)
    {
        if (!DateTime.TryParseExact(start, "yyyy-MM", Globalization.CultureInfo.InvariantCulture,
                Globalization.DateTimeStyles.None, out var s)) return 0;
        if (!DateTime.TryParseExact(end, "yyyy-MM", Globalization.CultureInfo.InvariantCulture,
                Globalization.DateTimeStyles.None, out var e)) return 0;
        return Math.Max(0, ((e.Year - s.Year) * 12 + e.Month - s.Month) + 1);
    }

    private static string FormatDate(DateTime? dt) => dt?.ToString("yyyy年M月d日") ?? string.Empty;

    private static string FormatMonth(string month)
    {
        if (string.IsNullOrWhiteSpace(month)) return string.Empty;
        if (month.Length == 6 && int.TryParse(month, out _))
        {
            var y = int.Parse(month[..4]);
            var m = int.Parse(month[4..]);
            return $"{y}年{m}月";
        }
        if (DateTime.TryParseExact(month, "yyyy-MM", Globalization.CultureInfo.InvariantCulture,
                Globalization.DateTimeStyles.None, out var dt))
        {
            return $"{dt.Year}年{dt.Month}月";
        }
        return month;
    }

    private static string FormatAmount(decimal amount) => $"{amount:F2}元";
}
