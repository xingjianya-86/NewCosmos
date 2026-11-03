using NewCosmos.Helpers;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>身份证校验器（AGENTS §14 指定权威）——结构精简期间的口径锚点。</summary>
public class IdCardValidatorTests
{
    [Theory]
    [InlineData("11010519491231002X")] // 经典示例（49年生，校验位 X）
    [InlineData("440524188001010014")] // 15 位升 18 位标准样例派生
    public void IsValid_合法身份证_返回true(string idCard)
        => Assert.True(IdCardValidator.IsValid(idCard));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123456")]                     // 长度不足
    [InlineData("110105194912310021")]        // 校验位错误
    [InlineData("11010519491231002A")]        // 非法字符
    public void IsValid_非法身份证_返回false(string? idCard)
        => Assert.False(IdCardValidator.IsValid(idCard!));
}
