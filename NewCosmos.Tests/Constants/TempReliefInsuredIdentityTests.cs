using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using Xunit;

namespace NewCosmos.Tests.Constants;

/// <summary>
/// 临时救助「保障人群」自动判定：四类在册台账命中后的展示名与优先级。
/// 判定规则唯一实现见 <c>ITempReliefService.ResolveInsuredIdentityAsync</c>，本测试覆盖纯逻辑部分。
/// </summary>
public class TempReliefInsuredIdentityTests
{
    [Theory]
    [InlineData("nc_biz_rural_subsistence_families", "农村低保台账")]
    [InlineData("nc_biz_urban_subsistence_families", "城市低保台账")]
    [InlineData("nc_biz_destitute_families", "特困人员台账")]
    [InlineData("nc_biz_low_income_edge_families", "最低生活保障边缘台账")]
    [InlineData("nc_biz_rigid_expenditure_families", "刚性支出困难家庭台账")]
    [InlineData("nc_biz_applications", "nc_biz_applications")]
    public void GetInsuredLedgerDisplayName_四类台账与未知表(string table, string expected)
        => Assert.Equal(expected, TempReliefConstants.GetInsuredLedgerDisplayName(table));

    [Fact]
    public void ResolveInsuredLedgerNames_按政策优先级排序()
    {
        var names = TempReliefConstants.ResolveInsuredLedgerNames(new[]
        {
            "nc_biz_rigid_expenditure_families",
            "nc_biz_low_income_edge_families",
            "nc_biz_urban_subsistence_families",
            "nc_biz_destitute_families",
            "nc_biz_rural_subsistence_families"
        });

        Assert.Equal(new[]
        {
            "特困人员台账",
            "农村低保台账",
            "城市低保台账",
            "最低生活保障边缘台账",
            "刚性支出困难家庭台账"
        }, names);
    }

    [Fact]
    public void ResolveInsuredLedgerNames_去重与空值()
    {
        var names = TempReliefConstants.ResolveInsuredLedgerNames(new[]
        {
            "nc_biz_rural_subsistence_families",
            "nc_biz_rural_subsistence_families",
            null,
            ""
        });

        Assert.Equal(new[] { "农村低保台账" }, names);
        Assert.Empty(TempReliefConstants.ResolveInsuredLedgerNames(Array.Empty<string?>()));
    }

    [Fact]
    public void InsuredIdentity_命中时为在册且带依据()
    {
        var identity = new TempReliefInsuredIdentity
        {
            IsInsured = true,
            LedgerNames = new List<string> { "特困人员台账", "农村低保台账" }
        };

        Assert.Equal(TempReliefConstants.PopulationInsured, identity.PopulationStatus);
        Assert.Equal("特困人员台账", identity.PrimaryLedgerName);
        Assert.Equal("身份证命中《特困人员台账》《农村低保台账》", identity.BasisText);
    }

    [Fact]
    public void InsuredIdentity_未命中时为非在册()
    {
        var identity = new TempReliefInsuredIdentity { IsInsured = false };

        Assert.Equal(TempReliefConstants.PopulationUninsured, identity.PopulationStatus);
        Assert.Equal(string.Empty, identity.PrimaryLedgerName);
        Assert.Equal("身份证未命中四类在册台账", identity.BasisText);
    }

    [Theory]
    [InlineData("nc_biz_rural_subsistence_families", TempReliefConstants.PopulationInsured)]
    [InlineData("nc_biz_destitute_families", TempReliefConstants.PopulationInsured)]
    [InlineData("nc_biz_other_difficulty_families", TempReliefConstants.PopulationUninsured)]
    [InlineData("nc_biz_applications", TempReliefConstants.PopulationUninsured)]
    public void ResolvePopulationStatus_兜底口径仍与四类台账一致(string sourceTable, string expected)
        => Assert.Equal(expected,
            TempReliefConstants.ResolvePopulationStatus(TempReliefConstants.SourceImportTab, sourceTable));

    [Fact]
    public void ResolvePopulationStatus_手工登记一律非在册()
        => Assert.Equal(TempReliefConstants.PopulationUninsured,
            TempReliefConstants.ResolvePopulationStatus(
                TempReliefConstants.SourceManual, "nc_biz_rural_subsistence_families"));
}
