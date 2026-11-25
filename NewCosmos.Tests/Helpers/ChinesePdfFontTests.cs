using NewCosmos.Helpers;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>告知书 PDF 的中文字体可用性（PDFsharp 系统字体解析）。</summary>
public class ChinesePdfFontTests
{
    [Fact]
    public void 能注册中文字体并输出含中文的PDF()
    {
        Assert.True(ChinesePdfFontResolver.EnsureRegistered(out var error), error);

        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont(ChinesePdfFontResolver.FaceName, 12, XFontStyleEx.Regular);
        gfx.DrawString("社会救助一事一议告知书", font, XBrushes.Black, 20, 40);

        using var ms = new MemoryStream();
        doc.Save(ms, false);
        Assert.True(ms.Length > 0);
    }
}
