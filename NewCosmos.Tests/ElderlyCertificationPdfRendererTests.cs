using System.Text;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Domain.ElderlyBenefits;
using PdfSharp.Pdf.IO;
using Xunit;

namespace NewCosmos.Tests;

public class ElderlyCertificationPdfRendererTests
{
    [Fact]
    public void Render_ProducesReadablePdfWithChinese()
    {
        if (!ChinesePdfFontResolver.EnsureRegistered(out _)) return;

        var report = new ElderlyCertificationReport(
            ElderlyCertificationReportFormKeys.Roster,
            "2026年度 高龄认证 年度认证花名册",
            ElderlyCertificationReportService.RosterColumns,
            new[]
            {
                ElderlyCertificationReportRow.From(
                    ("Seq", 1),
                    ("Name", "张三"),
                    ("IdCard", "110101195001011234"),
                    ("Gender", "男"),
                    ("BirthDate", new DateTime(1950, 1, 1)),
                    ("Age", 76),
                    ("Category", "80-89周岁老人"),
                    ("IssueAmount", 100m),
                    ("HukouAddress", "XX县XX镇XX村"),
                    ("FamilyAddress", "XX县XX镇XX村1号"),
                    ("Phone", "13800000000"),
                    ("VerifyMethod", "上门"),
                    ("VerifiedAt", new DateTime(2026, 11, 20)),
                    ("CertStatus", "已通过"),
                    ("EvidenceStatus", "齐全"),
                    ("StopDecision", "无"),
                    ("ResultNote", "测试行"))
            });

        var result = ElderlyCertificationPdfRenderer.Render(report);

        Assert.True(result.IsSuccess, result.Message);
        var bytes = result.Value!;
        Assert.True(bytes.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));

        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.True(document.PageCount >= 1);
    }

    [Fact]
    public void RenderMany_AddsPagesAndKeepsReportSections()
    {
        if (!ChinesePdfFontResolver.EnsureRegistered(out _)) return;

        var rows = Enumerable.Range(1, 80)
            .Select(i => ElderlyCertificationReportRow.From(
                ("Seq", i),
                ("Name", $"老人{i:D3}"),
                ("IdCard", $"11010119500101{i:D4}"),
                ("Gender", i % 2 == 0 ? "男" : "女"),
                ("BirthDate", new DateTime(1940, 1, 1)),
                ("Age", 86),
                ("Category", "80-89周岁老人"),
                ("IssueAmount", 100m),
                ("HukouAddress", "XX县XX镇XX村"),
                ("FamilyAddress", "XX县XX镇XX村1号"),
                ("Phone", "13800000000"),
                ("VerifyMethod", "上门"),
                ("VerifiedAt", new DateTime(2026, 11, 20)),
                ("CertStatus", "已通过"),
                ("EvidenceStatus", "齐全"),
                ("StopDecision", "无"),
                ("ResultNote", "批量分页测试")))
            .ToList();

        var roster = new ElderlyCertificationReport(
            ElderlyCertificationReportFormKeys.Roster,
            "2026年度 年度认证花名册",
            ElderlyCertificationReportService.RosterColumns,
            rows);
        var summary = new ElderlyCertificationReport(
            ElderlyCertificationReportFormKeys.Summary,
            "2026年度 年度认证情况汇总表",
            ElderlyCertificationReportService.SummaryColumns,
            Array.Empty<ElderlyCertificationReportRow>());

        var result = ElderlyCertificationPdfRenderer.RenderMany(new[] { roster, summary });

        Assert.True(result.IsSuccess, result.Message);
        using var document = PdfReader.Open(
            new MemoryStream(result.Value!), PdfDocumentOpenMode.Import);
        Assert.True(document.PageCount >= 2);
    }
}
