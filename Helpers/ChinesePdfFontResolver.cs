using PdfSharp.Fonts;

namespace NewCosmos.Helpers;

/// <summary>
/// PDFsharp 中文字体解析（Windows 系统字体）：用于直接生成告知书 PDF。
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

            var fontPath = FindSystemChineseFont();
            if (fontPath == null)
            {
                _initError = "未找到中文字体（simhei.ttf/simfang.ttf/simkai.ttf），无法生成告知书 PDF";
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

    private static string? FindSystemChineseFont()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (string.IsNullOrWhiteSpace(dir)) return null;
        foreach (var name in new[] { "simhei.ttf", "simfang.ttf", "simkai.ttf", "simsunb.ttf" })
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
