using NewCosmos.Constants;
using Xunit;

namespace NewCosmos.Tests.Constants;

/// <summary>
/// 货币财产核对月数分档（2026-11-18 功能缺口修复，拍板：确认为缺口并开发）。
/// 锁点：① 法定分档——低保/单人保 30（细则第二十条(二)）、边缘/刚性支出 36（第二十五/二十六条(二)）；
/// ② 未知/未判定/null 一律从严 30（禁止回落 36 宽松方向）；③ 提示文案参数化月数。
/// </summary>
public class BankFlowConstantsTests
{
    // ── 36 个月档（边缘 + 刚性支出参照边缘）──

    [Fact]
    public void 边缘分类_取36个月档()
    {
        Assert.Equal(36, BankFlowConstants.MonthsFor(ClassificationConstants.RuralLowIncome));
        Assert.Equal(36, BankFlowConstants.MonthsFor(ClassificationConstants.UrbanLowIncome));
    }

    [Fact]
    public void 刚性支出分类_参照边缘取36个月档()
    {
        // 细则第二十六条(二)：家庭财产认定条件参照最低生活保障边缘家庭
        Assert.Equal(36, BankFlowConstants.MonthsFor(ClassificationConstants.RuralRigidExpenditure));
        Assert.Equal(36, BankFlowConstants.MonthsFor(ClassificationConstants.UrbanRigidExpenditure));
    }

    // ── 30 个月档（低保 + 单人保 + 特困 + 从严兜底）──

    [Fact]
    public void 低保分类_取30个月档()
    {
        Assert.Equal(30, BankFlowConstants.MonthsFor(ClassificationConstants.RuralSubsistence));
        Assert.Equal(30, BankFlowConstants.MonthsFor(ClassificationConstants.UrbanSubsistence));
    }

    [Fact]
    public void 单人保_已入低保取30个月档()
    {
        // 细则第二十五条末段：参照「单人户」纳入低保范围 → 适用低保 30 个月
        Assert.Equal(30, BankFlowConstants.MonthsFor(ClassificationConstants.RuralLowIncomeSingle));
        Assert.Equal(30, BankFlowConstants.MonthsFor(ClassificationConstants.UrbanLowIncomeSingle));
    }

    [Fact]
    public void 特困分类_无法定货币财产条件从严取30()
    {
        Assert.Equal(30, BankFlowConstants.MonthsFor(ClassificationConstants.RuralDestituteScattered));
        Assert.Equal(30, BankFlowConstants.MonthsFor(ClassificationConstants.UrbanDestituteCentralized));
    }

    [Theory]
    [InlineData(ClassificationConstants.RuralIncomeExceeded)]
    [InlineData(ClassificationConstants.UrbanIncomeExceeded)]
    [InlineData(ClassificationConstants.IneligibleWithLabor)]
    [InlineData("")]
    [InlineData("NoSuchClassificationCode")]
    public void 超标拒保与未知码_从严取30不回落36(string code)
    {
        Assert.Equal(30, BankFlowConstants.MonthsFor(code));
    }

    [Fact]
    public void 未判定null_从严取30()
    {
        Assert.Equal(30, BankFlowConstants.MonthsFor(null));
    }

    // ── 提示文案参数化月数 ──

    [Fact]
    public void 提示文案_月数为占位符非写死()
    {
        // 原文案写死「30 个月」，分档后必须参数化（{3}=月数）
        Assert.Contains("{3}", BankFlowConstants.BalanceExceedFormat);
        Assert.DoesNotContain("超过 30 个月", BankFlowConstants.BalanceExceedFormat);
    }

    [Fact]
    public void 提示文案_渲染36个月档()
    {
        var text = string.Format(BankFlowConstants.BalanceExceedFormat,
            "50,000.00", 2, "45,504.00", 36);
        Assert.Contains("超过 36 个月低保标准", text);
        Assert.Contains("2 人 × 36 × 月标准", text);
        Assert.Contains("仅提示，请人工核定", text);
    }

    [Fact]
    public void 提示文案_渲染30个月档()
    {
        var text = string.Format(BankFlowConstants.BalanceExceedFormat,
            "50,000.00", 2, "37,920.00", 30);
        Assert.Contains("超过 30 个月低保标准", text);
        Assert.Contains("2 人 × 30 × 月标准", text);
    }

    /// <summary>档位常量锚点：30/36 与细则条文一致，误改即失败</summary>
    [Fact]
    public void 档位常量锚点()
    {
        Assert.Equal(30, BankFlowConstants.BalanceLimitMonths);
        Assert.Equal(36, BankFlowConstants.BalanceLimitMonthsEdge);
    }
}
