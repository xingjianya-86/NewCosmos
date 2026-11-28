using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Domain.ElderlyBenefits;
using Xunit;

namespace NewCosmos.Tests;

/// <summary>
/// 高龄年度认证报表纯逻辑：formKey、列定义顺序、汇总率与合计行。
/// 不测 EPPlus 文件 IO（导出器由编译 + 冒烟覆盖）。
/// </summary>
public class ElderlyCertificationReportTests
{
    [Fact]
    public void FormKeys_MatchContract()
    {
        Assert.True(ElderlyCertificationReportFormKeys.IsValid(ElderlyCertificationReportFormKeys.Roster));
        Assert.True(ElderlyCertificationReportFormKeys.IsValid(ElderlyCertificationReportFormKeys.Summary));
        Assert.True(ElderlyCertificationReportFormKeys.IsValid(ElderlyCertificationReportFormKeys.Unverified));
        Assert.False(ElderlyCertificationReportFormKeys.IsValid("PDF"));
        Assert.False(ElderlyCertificationReportFormKeys.IsValid(null));
        Assert.False(ElderlyCertificationReportFormKeys.IsValid(""));

        Assert.Equal(new[] { "ROSTER", "SUMMARY", "UNVERIFIED" }, ElderlyCertificationReportFormKeys.AllKeys);
        Assert.Equal("年度认证花名册", ElderlyCertificationReportFormKeys.GetName(ElderlyCertificationReportFormKeys.Roster));
        Assert.Equal("年度认证情况汇总表", ElderlyCertificationReportFormKeys.GetName(ElderlyCertificationReportFormKeys.Summary));
        Assert.Equal("未认证人员名单", ElderlyCertificationReportFormKeys.GetName(ElderlyCertificationReportFormKeys.Unverified));
    }

    [Fact]
    public void RosterColumns_MatchContractOrder()
    {
        Assert.Equal(new[]
        {
            "序号", "姓名", "身份证号", "性别", "出生日期", "年龄",
            "享受类别", "月标准（元）", "户籍地址", "家庭住址", "联系电话",
            "认证方式", "认证日期", "认证结果", "凭证状态", "停发决定", "备注"
        }, ElderlyCertificationReportService.RosterColumns.Select(c => c.Name));
    }

    [Fact]
    public void SummaryColumns_MatchContractOrder()
    {
        Assert.Equal(new[]
        {
            "乡镇", "村社区", "应认证人数", "已通过", "免核查", "待核查",
            "拒绝核查", "核查不通过", "已去世", "已迁出", "无法联系",
            "已停发", "凭证异常人数", "认证率"
        }, ElderlyCertificationReportService.SummaryColumns.Select(c => c.Name));

        var rate = ElderlyCertificationReportService.SummaryColumns.Single(c => c.Name == "认证率");
        Assert.Equal(ElderlyCertificationReportColumnFormat.Percent1, rate.Format);
    }

    [Fact]
    public void UnverifiedColumns_MatchContractOrder()
    {
        Assert.Equal(new[]
        {
            "序号", "姓名", "身份证号", "性别", "年龄", "乡镇", "村社区",
            "联系电话", "认证状态", "凭证状态", "建议停发原因",
            "应停日期", "停发决定", "跟进日期", "备注"
        }, ElderlyCertificationReportService.UnverifiedColumns.Select(c => c.Name));
    }

    [Theory]
    [InlineData(10, 6, 2, 80.0)]
    [InlineData(14, 7, 2, 64.3)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(3, 1, 0, 33.3)]
    public void CalculateCertificationRate_RoundsToOneDecimal(
        int total, int verified, int exempt, decimal expected)
    {
        Assert.Equal(expected, ElderlyCertificationReportService.CalculateCertificationRate(total, verified, exempt));
    }

    [Fact]
    public void BuildSummaryRows_AppendsTotalsRowWithRecomputedRate()
    {
        var source = new List<ElderlyCertificationSummarySourceRow>
        {
            new()
            {
                TownName = "甲镇", VillageName = "东村", Total = 10,
                Verified = 6, Exempt = 2, Pending = 1, Refused = 1, Failed = 0,
                Deceased = 0, MovedOut = 0, NotLocated = 0, Stopped = 0, EvidenceIssue = 3
            },
            new()
            {
                TownName = "甲镇", VillageName = "西村", Total = 4,
                Verified = 1, Exempt = 0, Pending = 0, Refused = 0, Failed = 1,
                Deceased = 1, MovedOut = 0, NotLocated = 1, Stopped = 0, EvidenceIssue = 2
            }
        };

        var rows = ElderlyCertificationReportService.BuildSummaryRows(source);

        Assert.Equal(3, rows.Count);

        var totalRow = rows[^1];
        Assert.Equal("合计", totalRow.Get("TownName"));
        Assert.Equal(string.Empty, totalRow.Get("VillageName"));
        Assert.Equal(14, totalRow.Get("Total"));
        Assert.Equal(7, totalRow.Get("Verified"));
        Assert.Equal(2, totalRow.Get("Exempt"));
        Assert.Equal(1, totalRow.Get("Pending"));
        Assert.Equal(1, totalRow.Get("Refused"));
        Assert.Equal(1, totalRow.Get("Failed"));
        Assert.Equal(1, totalRow.Get("Deceased"));
        Assert.Equal(1, totalRow.Get("NotLocated"));
        Assert.Equal(5, totalRow.Get("EvidenceIssue"));
        Assert.Equal(64.3m, totalRow.Get("CertificationRate"));
    }

    [Fact]
    public void BuildSummaryRows_EmptySource_StillHasTotalsRow()
    {
        var rows = ElderlyCertificationReportService.BuildSummaryRows(
            Array.Empty<ElderlyCertificationSummarySourceRow>());

        var row = Assert.Single(rows);
        Assert.Equal("合计", row.Get("TownName"));
        Assert.Equal(0, row.Get("Total"));
        Assert.Equal(0m, row.Get("CertificationRate"));
    }
}
