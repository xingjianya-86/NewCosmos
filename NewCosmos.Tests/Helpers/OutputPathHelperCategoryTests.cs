using NewCosmos.Helpers;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 输出分类名映射（2026-11-15 输出目录合理化阶段A）：
/// 该映射同时决定输出目录名与 UI 业务类型显示名，未登记类型原样返回英文
/// 曾实际产出 AssetVerificationMonthlyReport / Recovery 两个英文目录，故兜底改为「未分类」。
/// </summary>
public class OutputPathHelperCategoryTests
{
    /// <summary>PrintExecuteService.PrintPermissionMap + PrintBusinessTypes 的全集（新增业务类型须同步登记）</summary>
    public static TheoryData<string, string> 已登记业务类型 => new()
    {
        { "AssetVerification", "资产核查" },
        { "FamilyApplication", "社会救助申请" },
        { "MonthlyReport", "月报表" },
        { "AssetVerificationMonthlyReport", "资产核查月报表" },
        { "EconomicReview", "经济复核" },
        { "ArchiveQuery", "档案查询" },
        { "TemporaryAssistance", "临时救助" },
        { "TempRelief", "临时救助" },
        { "ElderlyBenefits", "普惠高龄" },
        { "LowIncomeProof", "低保证明" },
        { "DeathStatement", "死亡情况说明" },
        { "NearRelative", "近亲属备案" },
        { "Recovery", "追缴" },
        { "ArchivePhoto", "档案照片" },
        { "DynamicRecord", "动态管理档案" },
        { "DynamicManagementRecord", "动态管理档案" },
    };

    [Theory]
    [MemberData(nameof(已登记业务类型))]
    public void 已登记类型_返回中文分类名(string businessType, string expected)
    {
        Assert.Equal(expected, OutputPathHelper.GetCategoryDisplayName(businessType));
    }

    /// <summary>历史英文目录的两个来源必须已登记（防回潮）</summary>
    [Theory]
    [InlineData("AssetVerificationMonthlyReport")]
    [InlineData("Recovery")]
    public void 曾产出英文目录的类型_必须返回中文(string businessType)
    {
        var display = OutputPathHelper.GetCategoryDisplayName(businessType);

        Assert.False(IsAscii(display),
            $"'{businessType}' 仍会产出英文目录：映射为 '{display}'");
    }

    /// <summary>未登记类型一律回退「未分类」，绝不把英文原样当目录名</summary>
    [Theory]
    [InlineData("SomeNewBusinessType")]
    [InlineData("some_new_business")]
    public void 未登记类型_回退未分类而非英文(string businessType)
    {
        Assert.Equal("未分类", OutputPathHelper.GetCategoryDisplayName(businessType));
    }

    [Fact]
    public void 空业务类型_返回未分类()
    {
        Assert.Equal("未分类", OutputPathHelper.GetCategoryDisplayName(""));
        Assert.Equal("未分类", OutputPathHelper.GetCategoryDisplayName("   "));
    }

    /// <summary>目录名不得含路径分隔符（防业务类型注入出预期外的层级）</summary>
    [Fact]
    public void 所有已登记分类名_不含路径分隔符()
    {
        foreach (var row in 已登记业务类型)
        {
            var display = (string)row[1];
            Assert.DoesNotContain(display, c => c is '\\' or '/' or ':');
        }
    }

    private static bool IsAscii(string value) =>
        value.Length > 0 && value.All(c => c <= 127);
}
