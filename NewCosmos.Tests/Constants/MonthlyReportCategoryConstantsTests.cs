using NewCosmos.Constants;
using Xunit;

namespace NewCosmos.Tests.Constants;

/// <summary>
/// 月报表按社会救助分类拆分：分类码集、匹配口径、表单键拆分。
/// </summary>
public class MonthlyReportCategoryConstantsTests
{
    [Fact]
    public void SplitCategories_固定为四类且不含其他困难人员()
    {
        Assert.Equal(
            new[]
            {
                MonthlyReportCategoryConstants.Subsistence,
                MonthlyReportCategoryConstants.LowIncomeEdge,
                MonthlyReportCategoryConstants.Destitute,
                MonthlyReportCategoryConstants.RigidExpenditure
            },
            MonthlyReportCategoryConstants.SplitCategories);
        Assert.DoesNotContain(MonthlyReportCategoryConstants.OtherDifficulty,
            MonthlyReportCategoryConstants.SplitCategories);
    }

    [Theory]
    [InlineData("最低生活保障", ClassificationConstants.RuralSubsistence, true)]
    [InlineData("最低生活保障", ClassificationConstants.UrbanSubsistence, true)]
    [InlineData("最低生活保障", ClassificationConstants.RuralLowIncomeSingle, true)]
    [InlineData("最低生活保障", ClassificationConstants.RuralLowIncome, false)]
    [InlineData("最低生活保障边缘家庭", ClassificationConstants.RuralLowIncome, true)]
    [InlineData("最低生活保障边缘家庭", ClassificationConstants.UrbanLowIncome, true)]
    [InlineData("最低生活保障边缘家庭", ClassificationConstants.RuralSubsistence, false)]
    [InlineData("特困人员", ClassificationConstants.RuralDestituteCentralized, true)]
    [InlineData("特困人员", ClassificationConstants.UrbanDestituteScattered, true)]
    [InlineData("特困人员", ClassificationConstants.RuralRigidExpenditure, false)]
    [InlineData("刚性支出困难家庭", ClassificationConstants.RuralRigidExpenditure, true)]
    [InlineData("刚性支出困难家庭", ClassificationConstants.UrbanRigidExpenditure, true)]
    [InlineData("刚性支出困难家庭", ClassificationConstants.RuralLowIncome, false)]
    [InlineData("最低生活保障", " RuralSubsistence ", true)]
    [InlineData("最低生活保障", null, false)]
    [InlineData("最低生活保障", "", false)]
    public void Matches_按分类码判定(string category, string? classificationCode, bool expected)
        => Assert.Equal(expected, MonthlyReportCategoryConstants.Matches(category, classificationCode));

    [Fact]
    public void Matches_全部汇总不过滤()
    {
        Assert.True(MonthlyReportCategoryConstants.Matches(null, null));
        Assert.True(MonthlyReportCategoryConstants.Matches("", "任意分类码"));
        Assert.True(MonthlyReportCategoryConstants.Matches("  ", null));
    }

    [Fact]
    public void Matches_未识别分类维持不过滤语义()
        => Assert.True(MonthlyReportCategoryConstants.Matches(MonthlyReportCategoryConstants.OtherDifficulty, "任意分类码"));

    [Theory]
    [InlineData("自然减员表", "自然减员表", null)]
    [InlineData("自然减员表_最低生活保障", "自然减员表", "最低生活保障")]
    [InlineData("自然减员表_最低生活保障边缘家庭", "自然减员表", "最低生活保障边缘家庭")]
    [InlineData("自然减员表_特困人员", "自然减员表", "特困人员")]
    [InlineData("自然减员表_刚性支出困难家庭", "自然减员表", "刚性支出困难家庭")]
    [InlineData("档案_退出对象兜底情况纠治表_特困人员", "档案_退出对象兜底情况纠治表", "特困人员")]
    [InlineData("停保汇总表_其他困难人员", "停保汇总表", "其他困难人员")]
    [InlineData("会议记录_一事一议", "会议记录_一事一议", null)]
    [InlineData("档案_退出对象兜底情况纠治表", "档案_退出对象兜底情况纠治表", null)]
    public void SplitFormKey_只按最后一个下划线识别分类后缀(string formKey, string baseForm, string? category)
    {
        var (actualBase, actualCategory) = MonthlyReportCategoryConstants.SplitFormKey(formKey);
        Assert.Equal(baseForm, actualBase);
        Assert.Equal(category, actualCategory);
    }

    [Fact]
    public void ResolveCodes_四类与未识别()
    {
        Assert.Equal(4, MonthlyReportCategoryConstants.ResolveCodes(MonthlyReportCategoryConstants.Subsistence)!.Length);
        Assert.Equal(2, MonthlyReportCategoryConstants.ResolveCodes(MonthlyReportCategoryConstants.LowIncomeEdge)!.Length);
        Assert.Equal(4, MonthlyReportCategoryConstants.ResolveCodes(MonthlyReportCategoryConstants.Destitute)!.Length);
        Assert.Equal(2, MonthlyReportCategoryConstants.ResolveCodes(MonthlyReportCategoryConstants.RigidExpenditure)!.Length);
        Assert.Null(MonthlyReportCategoryConstants.ResolveCodes(MonthlyReportCategoryConstants.OtherDifficulty));
        Assert.Null(MonthlyReportCategoryConstants.ResolveCodes(null));
    }
}
