using NewCosmos.Helpers;
using System.Runtime.CompilerServices;

namespace NewCosmos.Tests;

/// <summary>
/// 测试宿主启动时加载 PDFsharp 6.5.0 的 AGPL 许可，
/// 否则任何 PDF 生成 / 合并用例都会抛 PdfSharpLicenseException。
/// </summary>
internal static class TestBootstrap
{
    [ModuleInitializer]
    public static void Init() => PdfSharpLicenseBootstrap.EnsureLoaded();
}
