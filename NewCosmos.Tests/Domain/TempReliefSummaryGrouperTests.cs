using NewCosmos.Constants;
using NewCosmos.Services.Domain.Reporting;
using Xunit;

namespace NewCosmos.Tests.Domain;

/// <summary>
/// 月报「临时救助新增汇总表」分段（TempReliefSummaryGrouper 唯一实现）。
/// 锁点：小额快速救助（Small，定额）= 有额度 → 备用金发放；大额救助（Large）与历史空值 = 无额度 → 报县局审批；
/// 无额度段在前、有额度段在后，段内保持入参顺序（数据源按 confirmed_at, id 升序）。
/// </summary>
public class TempReliefSummaryGrouperTests
{
    private static MonthlyTempReliefRow Row(string name, string? reliefType,
        string? populationStatus = null, string? paymentChannel = null)
        => new()
        {
            Name = name,
            ReliefType = reliefType ?? string.Empty,
            PopulationStatus = populationStatus ?? TempReliefConstants.PopulationInsured,
            PaymentChannel = paymentChannel ?? TempReliefConstants.PaymentCounty
        };

    [Theory]
    [InlineData(TempReliefConstants.ReliefTypeSmall, true)]
    [InlineData(TempReliefConstants.ReliefTypeLarge, false)]
    [InlineData("", false)]   // 历史空值 → 无额度（主表默认值即 Large）
    [InlineData(null, false)]
    [InlineData("Other", false)]
    public void IsFixedQuotaType_仅小额定额为有额度(string? reliefType, bool expected)
    {
        Assert.Equal(expected, TempReliefConstants.IsFixedQuotaType(reliefType));
    }

    [Fact]
    public void SplitByQuota_大额进无额度段_小额进有额度段()
    {
        var rows = new List<MonthlyTempReliefRow>
        {
            Row("甲", TempReliefConstants.ReliefTypeLarge),
            Row("乙", TempReliefConstants.ReliefTypeSmall),
            Row("丙", null)
        };

        var (noQuota, withQuota) = TempReliefSummaryGrouper.SplitByQuota(rows);

        Assert.Equal(new[] { "甲", "丙" }, noQuota.Select(r => r.Name));
        Assert.Equal(new[] { "乙" }, withQuota.Select(r => r.Name));
    }

    [Fact]
    public void SplitByQuota_段内保持入参顺序()
    {
        var rows = new List<MonthlyTempReliefRow>
        {
            Row("小额1", TempReliefConstants.ReliefTypeSmall),
            Row("大额1", TempReliefConstants.ReliefTypeLarge),
            Row("小额2", TempReliefConstants.ReliefTypeSmall),
            Row("大额2", TempReliefConstants.ReliefTypeLarge)
        };

        var (noQuota, withQuota) = TempReliefSummaryGrouper.SplitByQuota(rows);

        Assert.Equal(new[] { "大额1", "大额2" }, noQuota.Select(r => r.Name));
        Assert.Equal(new[] { "小额1", "小额2" }, withQuota.Select(r => r.Name));
    }

    [Fact]
    public void SplitByQuota_空输入_两段皆空()
    {
        var (noQuota, withQuota) = TempReliefSummaryGrouper.SplitByQuota(new List<MonthlyTempReliefRow>());

        Assert.Empty(noQuota);
        Assert.Empty(withQuota);
    }

    [Fact]
    public void SplitByQuota_不修改入参()
    {
        var rows = new List<MonthlyTempReliefRow> { Row("甲", TempReliefConstants.ReliefTypeSmall) };

        TempReliefSummaryGrouper.SplitByQuota(rows);

        Assert.Single(rows);
    }

    [Fact]
    public void 分段标签_业务措辞单一来源()
    {
        // 输出（打印标题/文件名/预览标题）只用业务措辞，"无额度/有额度"仅作代码内用语
        Assert.Equal("报县局审批", TempReliefConstants.SegmentTagCountyApproval);
        Assert.Equal("备用金发放", TempReliefConstants.SegmentTagReservePayout);
    }

    [Fact]
    public void SplitByCategory_按渠道与人群状态拆四段()
    {
        var rows = new List<MonthlyTempReliefRow>
        {
            Row("上级在册", TempReliefConstants.ReliefTypeLarge,
                TempReliefConstants.PopulationInsured, TempReliefConstants.PaymentCounty),
            Row("上级非在册", TempReliefConstants.ReliefTypeLarge,
                TempReliefConstants.PopulationUninsured, TempReliefConstants.PaymentCounty),
            Row("备用金在册", TempReliefConstants.ReliefTypeLarge,
                TempReliefConstants.PopulationInsured, TempReliefConstants.PaymentReserve),
            // 小额快救即使误传 County，也归入备用金段
            Row("小额在册", TempReliefConstants.ReliefTypeSmall,
                TempReliefConstants.PopulationInsured, TempReliefConstants.PaymentCounty),
            Row("小额非在册", TempReliefConstants.ReliefTypeSmall,
                TempReliefConstants.PopulationUninsured, TempReliefConstants.PaymentReserve)
        };

        var (countyInsured, countyUninsured, reserveInsured, reserveUninsured) =
            TempReliefSummaryGrouper.SplitByCategory(rows);

        Assert.Equal(new[] { "上级在册" }, countyInsured.Select(r => r.Name));
        Assert.Equal(new[] { "上级非在册" }, countyUninsured.Select(r => r.Name));
        Assert.Equal(new[] { "备用金在册", "小额在册" }, reserveInsured.Select(r => r.Name));
        Assert.Equal(new[] { "小额非在册" }, reserveUninsured.Select(r => r.Name));
    }
}
