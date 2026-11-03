using NewCosmos.Constants;
using Xunit;

namespace NewCosmos.Tests.Constants;

/// <summary>
/// 文书模板名判定单点（批次4 魔串收口）：实机模板带分类前缀（如"档案_封面"），
/// 判定用子串匹配保持兼容；改名只改 DocumentTemplateNames。
/// </summary>
public class DocumentTemplateNamesTests
{
    [Theory]
    [InlineData("档案_封面", true)]
    [InlineData("停保_档案_封面", true)]     // 前缀变体
    [InlineData("档案_目录", false)]
    [InlineData("档案_变更告知书", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsCover_封面判定(string? name, bool expected)
        => Assert.Equal(expected, DocumentTemplateNames.IsCover(name));

    [Theory]
    [InlineData("档案_赡养费承诺书", true)]
    [InlineData("赡养费承诺书", true)]
    [InlineData("档案_授权承诺书_P1", false)]
    [InlineData("通用_申请人承诺书", false)]
    [InlineData(null, false)]
    public void IsSupporterPledge_赡养费承诺书判定(string? name, bool expected)
        => Assert.Equal(expected, DocumentTemplateNames.IsSupporterPledge(name));
}
