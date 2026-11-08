using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 档案照片命名规则（姓名_身份证后4位_类别_编号）与流水号推进逻辑测试。
/// 规则定稿 2026-11-04：编号 = 该人该类别内流水，往下递增不覆盖。
/// </summary>
public class PhotoNameBuilderTests
{
    [Xunit.Fact]
    public void Build_FullRule_JoinsAllSegments()
    {
        var name = PhotoNameBuilder.Build("张三", "231025198001014929", "ID_CARD", 1);
        Assert.Equal("张三_4929_身份证_01", name);
    }

    [Xunit.Fact]
    public void Build_SlashInCategory_ReplacedWithFullWidthSlash()
    {
        // 病历/诊断证明、银行卡/一卡通 含 '/'，会破坏文件名
        var name = PhotoNameBuilder.Build("李四", "123456789012345678", "BANK_CARD", 3);
        Assert.Equal("李四_5678_银行卡／一卡通_03", name);
    }

    [Xunit.Fact]
    public void Build_MissingName_OmitsNameSegment()
    {
        Assert.Equal("4929_身份证_02",
            PhotoNameBuilder.Build(null, "231025198001014929", "ID_CARD", 2));
        Assert.Equal("4929_身份证_02",
            PhotoNameBuilder.Build("  ", "2310251980010144929", "ID_CARD", 2));
    }

    [Xunit.Fact]
    public void Build_ShortIdCard_UsesWholeId()
    {
        Assert.Equal("张三_1234_身份证_01",
            PhotoNameBuilder.Build("张三", "1234", "ID_CARD", 1));
    }

    [Xunit.Fact]
    public void Build_SequencePadsToTwoDigits()
    {
        Assert.EndsWith("_07", PhotoNameBuilder.Build("张三", "1234", "ID_CARD", 7));
        Assert.EndsWith("_12", PhotoNameBuilder.Build("张三", "1234", "ID_CARD", 12));
        Assert.EndsWith("_100", PhotoNameBuilder.Build("张三", "1234", "ID_CARD", 100));
    }

    [Xunit.Fact]
    public void NextSequence_EmptyList_ReturnsOne()
    {
        Assert.Equal(1, PhotoNameBuilder.NextSequence(new List<ArchivePhoto>()));
    }

    [Xunit.Fact]
    public void NextSequence_ContinuesAfterExistingNames()
    {
        var existing = new List<ArchivePhoto>
        {
            new() { PhotoName = "张三_4929_身份证_01" },
            new() { PhotoName = "张三_4929_身份证_02" },
        };
        Assert.Equal(3, PhotoNameBuilder.NextSequence(existing));
    }

    [Xunit.Fact]
    public void NextSequence_DeletedMiddlePhoto_DoesNotReuseNumber()
    {
        // 01、03 存在（02 已删）：取可解析最大值 3 → 下一个 4，不复用 02
        var existing = new List<ArchivePhoto>
        {
            new() { PhotoName = "张三_4929_身份证_01" },
            new() { PhotoName = "张三_4929_身份证_03" },
        };
        Assert.Equal(4, PhotoNameBuilder.NextSequence(existing));
    }

    [Xunit.Fact]
    public void NextSequence_OldNullNamePhotos_FallsBackToCount()
    {
        // 旧数据 PhotoName 为 null（3 张）：按数量兜底 → 下一个 4，不与旧数据混淆
        var existing = new List<ArchivePhoto>
        {
            new() { PhotoName = null },
            new() { PhotoName = null },
            new() { PhotoName = null },
        };
        Assert.Equal(4, PhotoNameBuilder.NextSequence(existing));
    }

    [Xunit.Fact]
    public void NextSequence_MixedNullAndNamed_TakesMax()
    {
        // 2 张旧无名 + 1 张已回填 _03：max(可解析3, 总数3) + 1 = 4
        var existing = new List<ArchivePhoto>
        {
            new() { PhotoName = null },
            new() { PhotoName = null },
            new() { PhotoName = "张三_4929_身份证_03" },
        };
        Assert.Equal(4, PhotoNameBuilder.NextSequence(existing));
    }

    [Xunit.Fact]
    public void NextSequence_FreeFormRemark_NotParsedAsSequence()
    {
        // 旧自由备注（"户口本第2页"，非 _数字 结尾）不参与编号解析，
        // 数量兜底保证新编号不与既有行数撞号
        var existing = new List<ArchivePhoto>
        {
            new() { PhotoName = "户口本第2页" },
            new() { PhotoName = "张三_4929_户口本_01" },
        };
        Assert.Equal(3, PhotoNameBuilder.NextSequence(existing));
    }

    [Xunit.Fact]
    public void SanitizeFileName_ReplacesInvalidChars()
    {
        var clean = PhotoNameBuilder.SanitizeFileName("张三/4929:身份证?01");
        foreach (var c in Path.GetInvalidFileNameChars())
            Assert.False(clean.Contains(c), $"不应包含非法字符 '{c}'");
    }

    [Xunit.Fact]
    public void SanitizeFileName_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, PhotoNameBuilder.SanitizeFileName(null));
        Assert.Equal(string.Empty, PhotoNameBuilder.SanitizeFileName("   "));
    }
}
