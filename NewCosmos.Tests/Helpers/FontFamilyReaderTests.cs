using System.Text;
using NewCosmos.Helpers;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 公文字体族名解析器（FontFamilyReader）——合成 sfnt 字节的纯逻辑锚点。
/// 覆盖：TTF name 表解析、TTC 首字体定位、多语言记录收集、坏数据回退。
/// </summary>
public class FontFamilyReaderTests
{
    [Fact]
    public void ReadFamilyNames_Ttf_取Windows平台族名()
    {
        var bytes = BuildTtf((platform: 3, nameId: 1, "仿宋_GB2312"));

        var names = FontFamilyReader.ReadFamilyNames(bytes);

        Assert.Contains("仿宋_GB2312", names);
    }

    [Fact]
    public void ReadFamilyNames_多语言记录_全部收集()
    {
        // 中文字体惯例：Windows 中文记录（本地化名）+ Windows 英文记录（英文名）并存
        var bytes = BuildTtf(
            (platform: 3, nameId: 1, "宋体"),
            (platform: 3, nameId: 1, "SimSun"));

        var names = FontFamilyReader.ReadFamilyNames(bytes);

        Assert.Contains("宋体", names);
        Assert.Contains("SimSun", names);
    }

    [Fact]
    public void ReadFamilyNames_TypographicFamily一并收集()
    {
        var bytes = BuildTtf(
            (platform: 3, nameId: 1, "方正小标宋简体"),
            (platform: 3, nameId: 16, "方正小标宋"));

        var names = FontFamilyReader.ReadFamilyNames(bytes);

        Assert.Contains("方正小标宋简体", names);
        Assert.Contains("方正小标宋", names);
    }

    [Fact]
    public void ReadFamilyNames_Mac平台Ascii名_可解析()
    {
        var bytes = BuildTtf((platform: 1, nameId: 1, "Times New Roman"));

        var names = FontFamilyReader.ReadFamilyNames(bytes);

        Assert.Contains("Times New Roman", names);
    }

    [Fact]
    public void ReadFamilyNames_Ttc_取首个字体()
    {
        var ttf = BuildTtf((platform: 3, nameId: 1, "楷体_GB2312"));
        var ttc = WrapAsTtcFirstFont(ttf);

        var names = FontFamilyReader.ReadFamilyNames(ttc);

        Assert.Contains("楷体_GB2312", names);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 1, 2, 3 })]                       // 过短
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })] // 非法 sfnt
    public void ReadFamilyNames_坏数据_返回空不抛异常(byte[] data)
        => Assert.Empty(FontFamilyReader.ReadFamilyNames(data));

    [Fact]
    public void ReadFamilyNames_坏文件_回退文件名不抛异常()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nc_font_test_{Guid.NewGuid():N}.ttf");
        try
        {
            File.WriteAllBytes(path, new byte[] { 0x00, 0x01, 0x02 });

            var names = FontFamilyReader.ReadFamilyNames(path);

            var expected = Path.GetFileNameWithoutExtension(path);
            Assert.Single(names);
            Assert.Equal(expected, names[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadFamilyNames_真实字体文件_能解析族名()
    {
        // 用系统自带字体（Windows 必有）做真实文件解析冒烟；非 Windows CI 跳过
        var simsun = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "simsun.ttc");
        if (!File.Exists(simsun))
            return;

        var names = FontFamilyReader.ReadFamilyNames(simsun);

        Assert.NotEmpty(names);
        // 中文系统记录或英文名任一存在即证明 name 表解析生效
        Assert.Contains(names, n => n is "宋体" or "SimSun" or "NSimSun" or "新宋体");
    }

    #region 合成 sfnt 构造器

    // 注意：BinaryWriter 默认小端，sfnt/TTC/name 表全部大端——必须手动字节交换写入。

    private static void W16(Stream s, ushort v)
    {
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    private static void W32(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24));
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    /// <summary>
    /// 构造最小合法 TTF：Offset Table + 单个 name 表（多条 NameRecord + 字符串存储）。
    /// NameRecord: platformID encodingID languageID nameID length stringOffset（各 uint16 BE）。
    /// </summary>
    private static byte[] BuildTtf(params (ushort platform, ushort nameId, string value)[] records)
    {
        using var ms = new MemoryStream();

        // 字符串存储 + 记录
        var stringStorage = new MemoryStream();
        var recordBytes = new List<byte[]>();
        foreach (var (platform, nameId, value) in records)
        {
            var str = platform == 1
                ? Encoding.Latin1.GetBytes(value)   // MacRoman：族名记录基本是 ASCII
                : Encoding.BigEndianUnicode.GetBytes(value); // Windows/Unicode：UTF-16BE

            var offset = (ushort)stringStorage.Length;
            stringStorage.Write(str, 0, str.Length);

            using var rw = new MemoryStream();
            W16(rw, platform);                // platformID
            W16(rw, 3);                       // encodingID（Windows=UCS-2；Mac=Roman 近似）
            W16(rw, 0x409);                   // languageID（英语——值不参与解析）
            W16(rw, nameId);                  // nameID
            W16(rw, (ushort)str.Length);      // length
            W16(rw, offset);                  // stringOffset
            recordBytes.Add(rw.ToArray());
        }

        var nameTableHeaderLen = 6;
        var recordsLen = recordBytes.Count * 12;
        var stringOffset = (ushort)(nameTableHeaderLen + recordsLen);

        // name 表体
        var nameBody = new MemoryStream();
        W16(nameBody, 0);                     // format
        W16(nameBody, (ushort)recordBytes.Count); // count
        W16(nameBody, stringOffset);          // stringOffset（相对 name 表起点）
        foreach (var r in recordBytes) nameBody.Write(r, 0, r.Length);
        nameBody.Write(stringStorage.ToArray(), 0, (int)stringStorage.Length);

        var nameBytes = nameBody.ToArray();

        // Offset Table（numTables=1）
        W32(ms, 0x00010000u);  // sfntVersion
        W16(ms, 1);            // numTables
        W16(ms, 16);           // searchRange
        W16(ms, 0);            // entrySelector
        W16(ms, 0);            // rangeShift

        // Table Record: name @ offset 28（12 头 + 16 记录）
        ms.Write(Encoding.ASCII.GetBytes("name"), 0, 4);
        W32(ms, 0u);           // checksum（校验不参与解析）
        W32(ms, 28u);          // offset
        W32(ms, (uint)nameBytes.Length);

        ms.Write(nameBytes, 0, nameBytes.Length);
        return ms.ToArray();
    }

    /// <summary>
    /// TTC 包装：ttcf 头 + numFonts=1 + 首字体 Offset Table 指针。
    /// OpenType 规范：TTC 内 Table Record 的 offset 相对整个 TTC 文件起点——
    /// 嵌入 TTF 后必须给 name 表记录的 offset 字段加头部偏移（与真实 simsun.ttc 同构）。
    /// </summary>
    private static byte[] WrapAsTtcFirstFont(byte[] ttf)
    {
        const uint headerSize = 16; // ttcf(4)+version(4)+numFonts(4)+offset[0](4)
        using var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes("ttcf"), 0, 4);
        W32(ms, 0x00010000u);  // version
        W32(ms, 1u);           // numFonts
        W32(ms, headerSize);   // 首字体 Offset Table 偏移
        ms.Write(ttf, 0, ttf.Length);

        var arr = ms.ToArray();

        // 补丁首个 Table Record（TTF 内 +12 处）的 offset 字段（记录内 +8）：原值相对 TTF 起点 → 相对 TTC 起点
        var fieldPos = (int)headerSize + 12 + 8;
        var original = (uint)((arr[fieldPos] << 24) | (arr[fieldPos + 1] << 16) |
                              (arr[fieldPos + 2] << 8) | arr[fieldPos + 3]);
        var patched = original + headerSize;
        arr[fieldPos] = (byte)(patched >> 24);
        arr[fieldPos + 1] = (byte)(patched >> 16);
        arr[fieldPos + 2] = (byte)(patched >> 8);
        arr[fieldPos + 3] = (byte)patched;

        return arr;
    }

    #endregion
}
