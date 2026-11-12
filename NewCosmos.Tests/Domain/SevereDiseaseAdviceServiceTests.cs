using NewCosmos.Models.Results;
using NewCosmos.Services.Domain.SevereDisease;
using Xunit;

namespace NewCosmos.Tests.Domain;

/// <summary>
/// 重病判定建议服务——自动预勾选闸门（ShouldAutoCheck）口径锚点。
/// 安全策略（2026-11-11 补测定稿）：只勾不取消、人工优先——
/// AI 永不把勾选改为取消，用户取消过（true→false 只能来自用户）即为否决，本会话不再自动勾。
/// </summary>
public class SevereDiseaseAdviceServiceTests
{
    private static SevereDiseaseJudgment Severe(string matchedItem) => new()
    {
        Verdict = SevereDiseaseJudgment.VerdictSevere,
        MatchedItem = matchedItem,
        Confidence = 0.95,
        Reason = "命中清单",
        Source = SevereDiseaseJudgment.SourceLlm,
    };

    [Xunit.Fact]
    public void 闸门_severe且matched_item在清单_允许自动勾选()
        => Assert.True(ISevereDiseaseAdviceService.ShouldAutoCheck(Severe("尿毒症"), userRejected: false));

    [Xunit.Fact]
    public void 闸门_用户否决过_禁止自动勾选()
        => Assert.False(ISevereDiseaseAdviceService.ShouldAutoCheck(Severe("尿毒症"), userRejected: true));

    [Xunit.Fact]
    public void 闸门_matched_item为空_禁止自动勾选()
        // LLM 判 severe 但未归入清单（胡扯项被服务端置空的场景）→ 只出建议，不自动勾
        => Assert.False(ISevereDiseaseAdviceService.ShouldAutoCheck(Severe(""), userRejected: false));

    [Theory]
    [InlineData(SevereDiseaseJudgment.VerdictNotSevere)]
    [InlineData(SevereDiseaseJudgment.VerdictUnknown)]
    public void 闸门_非sever_禁止自动勾选(string verdict)
    {
        var j = Severe("尿毒症") with { Verdict = verdict };
        Assert.False(ISevereDiseaseAdviceService.ShouldAutoCheck(j, userRejected: false));
    }

    [Xunit.Fact]
    public void 闸门_无判定结果_禁止自动勾选()
        => Assert.False(ISevereDiseaseAdviceService.ShouldAutoCheck(null, userRejected: false));
}
