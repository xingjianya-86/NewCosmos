using NewCosmos.Constants;

namespace NewCosmos.Services.Domain.Reporting;

/// <summary>
/// 月报「临时救助新增汇总表」分段（纯函数，无 DB/DI 依赖，供 PrintService 与单元测试共用）：
/// 主口径 = 发放渠道 × 人群状态（上级·在册 / 上级·非在册 / 备用金·在册 / 备用金·非在册）；
/// 小额快救归入备用金段。段内保持入参顺序（数据源 SQL 已按 confirmed_at, id 升序）。
/// 旧的两段口径（SplitByQuota）保留仅为兼容存量测试。
/// </summary>
public static class TempReliefSummaryGrouper
{
    /// <summary>
    /// 按额度拆两段：返回 (无额度段, 有额度段)。历史空值/未知类型归无额度段
    /// （主表 relief_type 默认值即 Large，与默认一致）。
    /// </summary>
    public static (List<MonthlyTempReliefRow> NoQuota, List<MonthlyTempReliefRow> WithQuota) SplitByQuota(
        IReadOnlyCollection<MonthlyTempReliefRow> rows)
    {
        var noQuota = new List<MonthlyTempReliefRow>();
        var withQuota = new List<MonthlyTempReliefRow>();
        foreach (var row in rows)
        {
            if (TempReliefConstants.IsFixedQuotaType(row.ReliefType)) withQuota.Add(row);
            else noQuota.Add(row);
        }
        return (noQuota, withQuota);
    }

    /// <summary>
    /// 按「发放渠道 × 人群状态」拆四段：上级·在册 / 上级·非在册 / 备用金·在册 / 备用金·非在册。
    /// 小额快救的渠道由 <see cref="TempReliefConstants.ResolvePaymentChannel"/> 归一为备用金。
    /// </summary>
    public static (
        List<MonthlyTempReliefRow> CountyInsured,
        List<MonthlyTempReliefRow> CountyUninsured,
        List<MonthlyTempReliefRow> ReserveInsured,
        List<MonthlyTempReliefRow> ReserveUninsured) SplitByCategory(
        IReadOnlyCollection<MonthlyTempReliefRow> rows)
    {
        var countyInsured = new List<MonthlyTempReliefRow>();
        var countyUninsured = new List<MonthlyTempReliefRow>();
        var reserveInsured = new List<MonthlyTempReliefRow>();
        var reserveUninsured = new List<MonthlyTempReliefRow>();

        foreach (var row in rows)
        {
            var channel = TempReliefConstants.ResolvePaymentChannel(row.ReliefType, row.PaymentChannel);
            var insured = string.Equals(row.PopulationStatus, TempReliefConstants.PopulationInsured,
                StringComparison.Ordinal);

            if (channel == TempReliefConstants.PaymentCounty)
                (insured ? countyInsured : countyUninsured).Add(row);
            else
                (insured ? reserveInsured : reserveUninsured).Add(row);
        }

        return (countyInsured, countyUninsured, reserveInsured, reserveUninsured);
    }
}
