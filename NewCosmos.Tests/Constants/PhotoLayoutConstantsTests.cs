using NewCosmos.Constants;
using Xunit;

namespace NewCosmos.Tests.Constants;

/// <summary>
/// 打印版式共享助手（2026-11-15 收敛三端重复实现）：
/// ActionSheet 选项组、「按默认」解析、按类别默认版式。
/// </summary>
public class PhotoLayoutConstantsTests
{
    [Theory]
    [InlineData("整页", PhotoLayoutConstants.FULL_PAGE)]
    [InlineData("半页", PhotoLayoutConstants.HALF_PAGE)]
    [InlineData("证件原寸", PhotoLayoutConstants.ID_CARD)]
    public void FromDisplayName_KnownChoice_ReturnsCode(string choice, string expected)
    {
        Assert.Equal(expected, PhotoLayoutConstants.FromDisplayName(choice));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("按默认（证件原寸/整页）")]
    [InlineData("取消")]
    [InlineData("未知选项")]
    public void FromDisplayName_DefaultOrUnknown_ReturnsNull(string? choice)
    {
        Assert.Null(PhotoLayoutConstants.FromDisplayName(choice));
    }

    [Theory]
    [InlineData(PhotoCategoryConstants.ID_CARD, PhotoLayoutConstants.ID_CARD)]
    [InlineData(PhotoCategoryConstants.HOUSEHOLD_INSPECTION, PhotoLayoutConstants.FULL_PAGE)]
    [InlineData(PhotoCategoryConstants.PUBLICITY_CHECK, PhotoLayoutConstants.FULL_PAGE)]
    [InlineData(PhotoCategoryConstants.OTHER_CERT, PhotoLayoutConstants.FULL_PAGE)]
    [InlineData(null, PhotoLayoutConstants.FULL_PAGE)]
    public void DefaultForCategory_CardOriginalSize_OthersFullPage(string? category, string expected)
    {
        Assert.Equal(expected, PhotoLayoutConstants.DefaultForCategory(category));
    }

    [Fact]
    public void BuildChoiceButtons_DefaultFirst_ThenAllLayouts()
    {
        var buttons = PhotoLayoutConstants.BuildChoiceButtons();

        Assert.Equal(PhotoLayoutConstants.DefaultChoiceText, buttons[0]);
        Assert.Equal(1 + PhotoLayoutConstants.All.Length, buttons.Length);
        Assert.Contains("整页", buttons);
        Assert.Contains("半页", buttons);
        Assert.Contains("证件原寸", buttons);
    }
}
