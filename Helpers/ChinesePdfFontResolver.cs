using PdfSharp.Fonts;

namespace NewCosmos.Helpers;

/// <summary>
/// PDFsharp 中文字体解析：用于直接生成告知书/认证报表 PDF。
/// 查找顺序：环境变量 NC_PDF_FONT_PATH → Windows 系统字体 → 应用目录 Resources/Fonts → Linux 常见字体。
/// 仅在首次调用时注册；找不到字体时显式失败，禁止静默输出乱码。
/// </summary>
public sealed class ChinesePdfFontResolver : IFontResolver
{
    /// <summary>注册到 PDFsharp 的字体族名（与系统字体解耦）。</summary>
    public const string FaceName = "NewCosmosChinese";

    private static readonly object Gate = new();
    private static bool _initialized;
    private static string? _initError;

    private readonly byte[] _fontData;

    private ChinesePdfFontResolver(string fontPath)
    {
        _fontData = File.ReadAllBytes(fontPath);
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        => new(FaceName);

    public byte[] GetFont(string faceName) => _fontData;

    /// <summary>注册系统中文 TTF；成功返回 true，找不到字体返回 false 并给出 error。</summary>
    public static bool EnsureRegistered(out string? error)
    {
        lock (Gate)
        {
            if (_initialized)
            {
                error = _initError;
                return _initError == null;
            }

            var fontPath = FindChineseFont();
            if (fontPath == null)
            {
                _initError = "未找到中文字体（可设置 NC_PDF_FONT_PATH，或安装 simhei/Noto Sans CJK），无法生成 PDF";
                error = _initError;
                return false;
            }

            if (GlobalFontSettings.FontResolver == null)
                GlobalFontSettings.FontResolver = new ChinesePdfFontResolver(fontPath);

            _initialized = true;
            error = null;
            return true;
        }
    }

    private static string? FindChineseFont()
    {
        foreach (var path in EnumerateCandidates())
        {
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        // 1) 部署环境显式指定（Linux 容器最稳）。
        var configured = Environment.GetEnvironmentVariable("NC_PDF_FONT_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
            yield return configured;

        // 2) Windows 系统黑体/仿宋/楷体。
        var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (!string.IsNullOrWhiteSpace(fontsDir))
        {
            foreach (var name in new[] { "simhei.ttf", "simfang.ttf", "simkai.ttf", "simsunb.ttf" })
                yield return Path.Combine(fontsDir, name);
        }

        // 3) 随应用发布的字体（Web 服务端内置，避免依赖系统字体包）。
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "Resources", "Fonts", "notosansregular.ttf");
        yield return Path.Combine(baseDir, "Fonts", "notosansregular.ttf");
        yield return Path.Combine(baseDir, "Resources", "Fonts", "NotoSansSC-Regular.otf");

        // 4) Linux 常见单文件字体（.ttc 集合 PDFsharp 不支持，跳过）。
        foreach (var path in new[]
                 {
                     "/usr/share/fonts/opentype/noto/NotoSansCJKsc-Regular.otf",
                     "/usr/share/fonts/truetype/noto/NotoSansCJKsc-Regular.otf",
                     "/usr/share/fonts/truetype/wqy/wqy-microhei.ttf",
                     "/usr/share/fonts/truetype/wqy/wqy-zenhei.ttf",
                     "/usr/share/fonts/truetype/arphic/uming.ttf"
                 })
        {
            yield return path;
        }
    }
}
