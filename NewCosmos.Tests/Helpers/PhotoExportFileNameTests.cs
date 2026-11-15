using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 导出文件名共用规则（2026-11-15 收敛桌面查询页/手机采集页两份重复实现）：
/// 优先 PhotoName，旧数据回退 {类别}_{时间}_{Id}；forceIdSuffix 防同名覆盖。
/// </summary>
public class PhotoExportFileNameTests
{
    [Fact]
    public void 有PhotoName_原样作为文件名()
    {
        var photo = new ArchivePhoto
        {
            Id = 11,
            PhotoName = "张三_4929_身份证_01",
            PhotoCategory = "ID_CARD",
            CreatedAt = new DateTime(2026, 11, 15, 10, 30, 0),
        };

        Assert.Equal("张三_4929_身份证_01.webp",
            PhotoNameBuilder.BuildExportFileName(photo, "webp"));
    }

    [Fact]
    public void 有PhotoName_强制Id后缀_追加Id防覆盖()
    {
        var photo = new ArchivePhoto
        {
            Id = 11,
            PhotoName = "张三_4929_身份证_01",
            PhotoCategory = "ID_CARD",
            CreatedAt = new DateTime(2026, 11, 15, 10, 30, 0),
        };

        Assert.Equal("张三_4929_身份证_01_11.webp",
            PhotoNameBuilder.BuildExportFileName(photo, "webp", forceIdSuffix: true));
    }

    [Fact]
    public void 无PhotoName_回退类别时间Id_不再重复追加Id()
    {
        var photo = new ArchivePhoto
        {
            Id = 11,
            PhotoName = null,
            PhotoCategory = "PUBLICITY_CHECK",
            CreatedAt = new DateTime(2026, 11, 15, 10, 30, 0),
        };

        var expected = "PUBLICITY_CHECK_20261115_103000_11.webp";
        Assert.Equal(expected, PhotoNameBuilder.BuildExportFileName(photo, "webp"));
        // 回退格式已含 Id，强追加时不再叠第二个
        Assert.Equal(expected, PhotoNameBuilder.BuildExportFileName(photo, "webp", forceIdSuffix: true));
    }

    [Fact]
    public void 扩展名跟随格式()
    {
        var photo = new ArchivePhoto { Id = 1, PhotoName = "李四_5678_银行卡／一卡通_03", PhotoCategory = "BANK_CARD" };

        Assert.EndsWith(".jpg", PhotoNameBuilder.BuildExportFileName(photo, "jpeg"));
        Assert.EndsWith(".webp", PhotoNameBuilder.BuildExportFileName(photo, "webp"));
    }
}
