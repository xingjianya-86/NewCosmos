using NewCosmos.Helpers;
using NewCosmos.Services.Domain.SocialAssistance;
using Xunit;

namespace NewCosmos.Tests.Domain;

/// <summary>
/// 家庭情况说明（原因详情模板）健康状况显示测试：
/// 2026-11-12 实测 BUG——库里存字典键 SevereIllness，生成器原样拼接输出"彭文华SevereIllness"。
/// 修复口径：字典键必须转中文；已中文化的存量值原样通过（不重复映射）。
/// </summary>
public class FamilySituationTextBuilderTests
{
    private static FamilySituationContext Ctx(params (string Name, string Health)[] sickMembers) => new()
    {
        ApplicantName = "王兴久",
        HukouType = "农村户口",
        FamilySize = 2,
        SickMembers = sickMembers.ToList(),
        Reason = "收入低",
        TotalFamilyIncome = 1029m,
        PerCapitaIncome = 514m,
        TotalAnnualIncome = 12345m,
        PerCapitaAnnualIncome = 6173m
    };

    [Xunit.Fact]
    public void Build_健康状况为字典键_输出中文_不含英文原始值()
    {
        var text = FamilySituationTextBuilder.Build(Ctx(("彭文华", "SevereIllness")));

        Assert.Contains("家庭成员中彭文华重病，", text);
        Assert.DoesNotContain("SevereIllness", text);
    }

    [Xunit.Fact]
    public void Build_全部健康字典键_均输出中文()
    {
        var text = FamilySituationTextBuilder.Build(Ctx(
            ("甲", "Healthy"), ("乙", "Weak"),
            ("丙", "SevereDisability"), ("丁", "SevereIllnessAndDisability")));

        Assert.Contains("甲健康或良好", text);
        Assert.Contains("乙一般或较弱", text);
        Assert.Contains("丙重残", text);
        Assert.Contains("丁重病且重残", text);
        // 逐键确认不含英文原始值
        foreach (var key in new[] { "Healthy", "Weak", "SevereDisability", "SevereIllnessAndDisability" })
            Assert.DoesNotContain(key, text);
    }

    [Xunit.Fact]
    public void Build_已中文化的存量值_原样通过_不重复映射()
    {
        var text = FamilySituationTextBuilder.Build(Ctx(("张三", "重病")));

        Assert.Contains("家庭成员中张三重病，", text);
        Assert.DoesNotContain("重病重病", text);
    }

    [Xunit.Fact]
    public void Build_无重病重残成员_不输出该句()
    {
        var text = FamilySituationTextBuilder.Build(Ctx());

        Assert.DoesNotContain("家庭成员中", text);
    }

    // ── DictDisplayHelper.GetHealthStatusDisplay（字典冷启动时的内置回退） ──

    [Xunit.Fact]
    public void GetHealthStatusDisplay_字典键_回退内置中文映射()
    {
        Assert.Equal("重病", DictDisplayHelper.GetHealthStatusDisplay("SevereIllness"));
        Assert.Equal("重残", DictDisplayHelper.GetHealthStatusDisplay("SevereDisability"));
        Assert.Equal("重病且重残", DictDisplayHelper.GetHealthStatusDisplay("SevereIllnessAndDisability"));
        Assert.Equal("健康或良好", DictDisplayHelper.GetHealthStatusDisplay("Healthy"));
        Assert.Equal("一般或较弱", DictDisplayHelper.GetHealthStatusDisplay("Weak"));
        Assert.Equal("其他", DictDisplayHelper.GetHealthStatusDisplay("Other"));
    }

    [Xunit.Fact]
    public void GetHealthStatusDisplay_已中文与空值_安全返回()
    {
        Assert.Equal("重病", DictDisplayHelper.GetHealthStatusDisplay("重病"));
        Assert.Equal("", DictDisplayHelper.GetHealthStatusDisplay(null));
        Assert.Equal("", DictDisplayHelper.GetHealthStatusDisplay("  "));
        Assert.Equal("未知值", DictDisplayHelper.GetHealthStatusDisplay("未知值"));
    }
}
