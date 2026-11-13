using NewCosmos.Helpers;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 档案照片插入归属人解析：PC 端插入必须锁定已存在被查询的人（关键词身份证 / 结果同属一人）。
/// </summary>
public class PhotoOwnerResolverTests
{
    private const string ValidId = "11010519491231002X";
    private const string AnotherValidId = "440524188001010014";

    [Fact]
    public void Resolve_关键词是合法身份证_直接采用()
    {
        var result = PhotoOwnerResolver.Resolve(ValidId, new[] { "别的身份证", "123456" });
        Assert.Equal(ValidId, result);
    }

    [Fact]
    public void Resolve_关键词带空格_先修剪()
    {
        var result = PhotoOwnerResolver.Resolve($"  {ValidId} ", null);
        Assert.Equal(ValidId, result);
    }

    [Fact]
    public void Resolve_关键词非身份证但结果同属一人_用该人()
    {
        var result = PhotoOwnerResolver.Resolve("张三_4929_身份证_01",
            new string?[] { ValidId, ValidId, ValidId });
        Assert.Equal(ValidId, result);
    }

    [Fact]
    public void Resolve_查询结果跨多人_返回null()
    {
        var result = PhotoOwnerResolver.Resolve(string.Empty,
            new string?[] { ValidId, AnotherValidId });
        Assert.Null(result);
    }

    [Fact]
    public void Resolve_结果同属一人但身份证非法_返回null()
    {
        // 18 位但校验位错
        var result = PhotoOwnerResolver.Resolve(null, new string?[] { "110105194912310021" });
        Assert.Null(result);
    }

    [Fact]
    public void Resolve_无关键词且无结果_返回null()
    {
        Assert.Null(PhotoOwnerResolver.Resolve(string.Empty, null));
        Assert.Null(PhotoOwnerResolver.Resolve(null, Array.Empty<string?>()));
        Assert.Null(PhotoOwnerResolver.Resolve("   ", new string?[] { null, "  " }));
    }

    [Fact]
    public void Resolve_关键词是照片名_回退到结果同属一人()
    {
        // 关键词是模糊词（非身份证）→ 走 ② 分支
        var result = PhotoOwnerResolver.Resolve("户口本第2页", new string?[] { ValidId });
        Assert.Equal(ValidId, result);
    }
}
