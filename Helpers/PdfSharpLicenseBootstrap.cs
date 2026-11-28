using PdfSharp;

namespace NewCosmos.Helpers;

/// <summary>
/// PDFsharp 6.5.0 起改为 AGPL / 商业双许可，首次使用前必须显式选择许可，
/// 否则抛 <c>PdfSharpLicenseException: The loaded license is not valid</c>。
/// 本项目为 GPL-3.0 开源（GPLv3 与 AGPLv3 兼容），统一加载免费 AGPL 许可。
/// </summary>
public static class PdfSharpLicenseBootstrap
{
    private static readonly object Gate = new();
    private static bool _loaded;

    /// <summary>幂等加载 AGPL 许可；所有 PDFsharp 使用入口（渲染 / 合并 / 测试）先调用它。</summary>
    public static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (Gate)
        {
            if (_loaded) return;
            PdfSharpLicense.SetAgplLicense();
            _loaded = true;
        }
    }
}
