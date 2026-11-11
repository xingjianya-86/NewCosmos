using NewCosmos.Constants;

namespace NewCosmos.Helpers;

/// <summary>
/// TTF/TTC 字体文件族名解析器（公文字体安装检查用，纯逻辑无 IO 之外依赖）。
/// 读 sfnt "name" 表的 nameID 1（Font Family）与 16（Typographic Family），
/// <b>收集全部语言记录</b>——中文字体常同时带本地化名与英文名
/// （如 宋体/SimSun），两侧各自与系统已装族名比对、任一命中即算已安装，
/// 避免"系统装的是 simsun.ttc、目录里是 宋体.ttc"这类文件名不同导致的误报。
/// 解析失败回退文件名去扩展名，保证检查永不因坏文件中断。
/// </summary>
public static class FontFamilyReader
{
    /// <summary>name 表中 Font Family 的 nameID</summary>
    private const ushort NameIdFontFamily = 1;

    /// <summary>name 表中 Typographic Family 的 nameID（可选，存在时一并收集）</summary>
    private const ushort NameIdTypographicFamily = 16;

    /// <summary>platformID：Unicode</summary>
    private const ushort PlatformUnicode = 0;

    /// <summary>platformID：Macintosh</summary>
    private const ushort PlatformMac = 1;

    /// <summary>platformID：Windows</summary>
    private const ushort PlatformWindows = 3;

    /// <summary>
    /// 读取字体文件的族名候选集（去重、去空白、保留原大小写供比对用）。
    /// </summary>
    /// <param name="filePath">字体文件路径</param>
    /// <returns>族名列表；解析失败返回基于文件名的单项回退列表，空文件返回空列表</returns>
    public static IReadOnlyList<string> ReadFamilyNames(string filePath)
    {
        var fallback = Path.GetFileNameWithoutExtension(filePath);
        try
        {
            var data = File.ReadAllBytes(filePath);
            var names = ReadFamilyNames(data);
            return names.Count > 0 ? names : Fallback(fallback);
        }
        catch
        {
            // 坏文件/IO 失败：回退文件名，不中断检查
            return Fallback(fallback);
        }
    }

    /// <summary>
    /// 从字体文件字节解析族名候选集（测试入口；TTC 取首个字体的 Offset Table）。
    /// </summary>
    public static IReadOnlyList<string> ReadFamilyNames(byte[] data)
    {
        if (data == null || data.Length < 12)
            return Array.Empty<string>();

        try
        {
            var offsetTableOffset = 0;
            if (IsTtc(data))
            {
                // TTC 头：tag(4) version(4) numFonts(4) offsets[](4×N)——取首个字体
                if (data.Length < 16) return Array.Empty<string>();
                var numFonts = ReadUInt32Be(data, 8);
                if (numFonts == 0) return Array.Empty<string>();
                offsetTableOffset = (int)ReadUInt32Be(data, 12);
            }

            return ParseNameTable(data, offsetTableOffset);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsTtc(byte[] data) =>
        data.Length >= 4 &&
        data[0] == (byte)'t' && data[1] == (byte)'t' &&
        data[2] == (byte)'c' && data[3] == (byte)'f';

    private static List<string> Fallback(string name)
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(name)) list.Add(name);
        return list;
    }

    /// <summary>
    /// 从 Offset Table 定位 name 表并提取族名。
    /// Offset Table: sfntVersion(4) numTables(2) searchRange(2) entrySelector(2) rangeShift(2)；
    /// Table Record: tag(4) checksum(4) offset(4) length(4)。
    /// </summary>
    private static List<string> ParseNameTable(byte[] data, int offsetTableOffset)
    {
        var names = new List<string>();
        if (offsetTableOffset < 0 || offsetTableOffset + 12 > data.Length)
            return names;

        var numTables = ReadUInt16Be(data, offsetTableOffset + 4);
        var tableStart = offsetTableOffset + 12;
        var nameOffset = -1;
        var nameLength = 0;

        for (var i = 0; i < numTables; i++)
        {
            var record = tableStart + i * 16;
            if (record + 16 > data.Length) break;
            // tag = "name"
            if (data[record] == (byte)'n' && data[record + 1] == (byte)'a' &&
                data[record + 2] == (byte)'m' && data[record + 3] == (byte)'e')
            {
                nameOffset = (int)ReadUInt32Be(data, record + 8);
                nameLength = (int)ReadUInt32Be(data, record + 12);
                break;
            }
        }

        if (nameOffset < 0 || nameOffset + 6 > data.Length)
            return names;

        var nameEnd = Math.Min(data.Length, nameOffset + nameLength);
        var count = ReadUInt16Be(data, nameOffset + 2);
        var stringOffset = nameOffset + ReadUInt16Be(data, nameOffset + 4);

        for (var i = 0; i < count; i++)
        {
            var rec = nameOffset + 6 + i * 12;
            if (rec + 12 > nameEnd) break;

            // NameRecord: platformID(+0) encodingID(+2) languageID(+4) nameID(+6) length(+8) stringOffset(+10)
            var platformId = ReadUInt16Be(data, rec);
            var nameId = ReadUInt16Be(data, rec + 6);
            var length = ReadUInt16Be(data, rec + 8);
            var strOffset = ReadUInt16Be(data, rec + 10);

            if (nameId != NameIdFontFamily && nameId != NameIdTypographicFamily)
                continue;

            var absStart = stringOffset + strOffset;
            if (absStart < 0 || absStart + length > data.Length)
                continue;

            var value = DecodeName(data, absStart, length, platformId);
            if (!string.IsNullOrWhiteSpace(value) && !names.Contains(value, StringComparer.OrdinalIgnoreCase))
                names.Add(value.Trim('\0').Trim());
        }

        return names;
    }

    private static string DecodeName(byte[] data, int start, int length, ushort platformId)
    {
        if (length <= 0) return string.Empty;
        var span = data.AsSpan(start, length);

        // Windows/Unicode 平台为 UTF-16BE；Mac 平台为 MacRoman（此处按 Latin-1 兜底解码，
        // 族名记录里 Mac 侧基本是 ASCII 英文名，Latin-1 对 ASCII 完全一致）
        if (platformId == PlatformWindows || platformId == PlatformUnicode)
        {
            // UTF-16BE → 字符串（奇数字节截断补 0）
            var chars = new char[length / 2];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = (char)((span[i * 2] << 8) | span[i * 2 + 1]);
            return new string(chars);
        }

        if (platformId == PlatformMac)
        {
            var chars = new char[length];
            for (var i = 0; i < length; i++)
                chars[i] = (char)span[i]; // Latin-1 兜底
            return new string(chars);
        }

        // 其他平台（如 0/10000 组合）按 UTF-16BE 尝试，长度为偶数时
        return length % 2 == 0 ? DecodeName(data, start, length, PlatformWindows) : string.Empty;
    }

    private static ushort ReadUInt16Be(byte[] data, int offset)
    {
        if (offset < 0 || offset + 2 > data.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static uint ReadUInt32Be(byte[] data, int offset)
    {
        if (offset < 0 || offset + 4 > data.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) | data[offset + 3];
    }
}
