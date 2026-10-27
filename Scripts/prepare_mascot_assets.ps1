# 吉祥物素材预处理（可重复执行）
#   源图：仓库根 newcosmos_icon_mascot_v2.png / newcosmos_character_v2.png（均为 24bpp 白底、无 Alpha）
#   产物：
#     Resources\AppIcon\appicon.png                 应用图标（MauiIcon 通道，四角白底转透明）
#     installer\appicon.ico                         安装包图标（多尺寸：<=48 BMP 帧 / >=64 PNG 帧）
#     Resources\Images\mascot_icon.png              应用内 Logo 引用副本（MauiImage）
#     Resources\Images\newcosmos_character.png      立绘（去白底 + 裁边 + 去白边）
#
# 算法：只清除「与画布边缘连通的近中性白」区域（四边洪泛填充），被线稿包围的
#       白发 / 白裙 / 白手套不会被误删；随后对边界近白像素做 Alpha 渐隐消除
#       抠图白边，最后按需裁剪到内容包围盒。
#
# 用法：powershell -ExecutionPolicy Bypass -File Scripts\prepare_mascot_assets.ps1
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [int]$MinChannel = 230,   # 判定为背景的单通道下限
    [int]$MaxSpread = 20,     # 判定为背景的通道间最大差（近中性色）
    [int]$FeatherFloor = 200  # 边界羽化起点：中性白边界像素按此区间比例透明
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$src = @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class MascotAssetTool
{
    static int Min3(byte r, byte g, byte b) { int m = r < g ? r : g; return m < b ? m : b; }
    static int Max3(byte r, byte g, byte b) { int m = r > g ? r : g; return m > b ? m : b; }

    static bool IsBg(byte r, byte g, byte b, int minCh, int maxSpread)
    {
        return Min3(r, g, b) >= minCh && (Max3(r, g, b) - Min3(r, g, b)) <= maxSpread;
    }

    static void TrySeed(int x, int y, byte[] px, byte[] bg, Stack<int> stack,
                        int w, int h, int minCh, int maxSpread)
    {
        int i = y * w + x;
        if (bg[i] == 1) return;
        int o = i * 4;
        if (IsBg(px[o + 2], px[o + 1], px[o], minCh, maxSpread)) stack.Push(i);
    }

    /// <summary>去白底 + 可选裁边，返回统计串。</summary>
    public static string Cutout(string srcPath, string dstPath, int minCh, int maxSpread,
                                int featherFloor, bool crop, int pad)
    {
        Bitmap bmp;
        using (Bitmap tmp = new Bitmap(srcPath))
        {
            bmp = new Bitmap(tmp.Width, tmp.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(tmp, 0, 0, tmp.Width, tmp.Height);
            }
        }

        int w = bmp.Width, h = bmp.Height;
        BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h),
                                       ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        byte[] px = new byte[w * h * 4];
        Marshal.Copy(data.Scan0, px, 0, px.Length);

        // ---- 1) 四边洪泛填充，清除与边缘连通的背景 ----
        byte[] bg = new byte[w * h];
        Stack<int> stack = new Stack<int>();
        for (int x = 0; x < w; x++)
        {
            TrySeed(x, 0, px, bg, stack, w, h, minCh, maxSpread);
            TrySeed(x, h - 1, px, bg, stack, w, h, minCh, maxSpread);
        }
        for (int y = 0; y < h; y++)
        {
            TrySeed(0, y, px, bg, stack, w, h, minCh, maxSpread);
            TrySeed(w - 1, y, px, bg, stack, w, h, minCh, maxSpread);
        }

        while (stack.Count > 0)
        {
            int i = stack.Pop();
            if (bg[i] == 1) continue;
            int o = i * 4;
            if (!IsBg(px[o + 2], px[o + 1], px[o], minCh, maxSpread)) continue;
            bg[i] = 1;
            int x = i % w, y = i / w;
            if (x > 0) stack.Push(i - 1);
            if (x < w - 1) stack.Push(i + 1);
            if (y > 0) stack.Push(i - w);
            if (y < h - 1) stack.Push(i + w);
        }

        // ---- 2) 边界近白像素 Alpha 渐隐（去抠图白边）----
        int feathered = 0;
        for (int i = 0; i < w * h; i++)
        {
            if (bg[i] == 1) { px[i * 4 + 3] = 0; continue; }

            int x = i % w, y = i / w;
            bool boundary = (x > 0 && bg[i - 1] == 1) || (x < w - 1 && bg[i + 1] == 1) ||
                            (y > 0 && bg[i - w] == 1) || (y < h - 1 && bg[i + w] == 1);
            if (!boundary) continue;

            int o = i * 4;
            byte r = px[o + 2], g = px[o + 1], b = px[o];
            int mn = Min3(r, g, b), mx = Max3(r, g, b);
            if (mn < featherFloor || (mx - mn) > 40) continue;

            int a = 255 - (mn - featherFloor) * 255 / (255 - featherFloor);
            if (a < 0) a = 0;
            if (a < px[o + 3]) { px[o + 3] = (byte)a; feathered++; }
        }

        // ---- 3) 可选裁边 ----
        int x0 = 0, y0 = 0, x1 = w - 1, y1 = h - 1;
        if (crop)
        {
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int i = 0; i < w * h; i++)
            {
                if (bg[i] == 1 || px[i * 4 + 3] < 8) continue;
                int x = i % w, y = i / w;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            if (maxX < 0) throw new Exception("抠图结果为空：阈值过严（" + srcPath + "）");
            x0 = Math.Max(0, minX - pad); y0 = Math.Max(0, minY - pad);
            x1 = Math.Min(w - 1, maxX + pad); y1 = Math.Min(h - 1, maxY + pad);
        }

        int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
        Bitmap outBmp = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
        BitmapData od = outBmp.LockBits(new Rectangle(0, 0, cw, ch),
                                        ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        byte[] opx = new byte[cw * ch * 4];
        for (int y = 0; y < ch; y++)
        {
            for (int x = 0; x < cw; x++)
            {
                int s = ((y + y0) * w + (x + x0)) * 4;
                int d = (y * cw + x) * 4;
                opx[d] = px[s]; opx[d + 1] = px[s + 1];
                opx[d + 2] = px[s + 2]; opx[d + 3] = px[s + 3];
            }
        }
        Marshal.Copy(opx, 0, od.Scan0, opx.Length);
        outBmp.UnlockBits(od);

        string dir = Path.GetDirectoryName(dstPath);
        if (!String.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        outBmp.Save(dstPath, ImageFormat.Png);
        bmp.UnlockBits(data);
        bmp.Dispose(); outBmp.Dispose();

        long cleared = 0;
        for (int i = 0; i < bg.Length; i++) if (bg[i] == 1) cleared++;
        return String.Format("{0}: {1}x{2} -> {3}x{4}, 清除背景 {5}px ({6:P1}), 羽化边界 {7}px",
                             Path.GetFileName(srcPath), w, h, cw, ch,
                             cleared, cleared / (double)(w * h), feathered);
    }

    /// <summary>等比缩放到最长边 maxEdge（源为正方形即等比缩小），写回原路径。返回统计串。</summary>
    public static string Resize(string srcPath, int maxEdge)
    {
        int w, h;
        Bitmap dst;
        using (Bitmap tmp = new Bitmap(srcPath))
        {
            w = tmp.Width; h = tmp.Height;
            if (w <= maxEdge && h <= maxEdge)
                return String.Format("{0}: 无需缩放 ({1}x{2})", Path.GetFileName(srcPath), w, h);

            double k = Math.Min((double)maxEdge / w, (double)maxEdge / h);
            int nw = Math.Max(1, (int)Math.Round(w * k)), nh = Math.Max(1, (int)Math.Round(h * k));
            dst = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.Clear(Color.Transparent);
                g.DrawImage(tmp, 0, 0, nw, nh);
            }
        }   // 源位图在此释放，文件句柄已解除，才能写回原路径

        int fw = dst.Width, fh = dst.Height;
        dst.Save(srcPath, ImageFormat.Png);
        dst.Dispose();
        return String.Format("{0}: {1}x{2} -> {3}x{4}", Path.GetFileName(srcPath), w, h, fw, fh);
    }

    /// <summary>由源 PNG 生成多尺寸 ICO。</summary>
    public static string MakeIco(string srcPath, string dstPath, int[] sizes)
    {
        List<byte[]> frames = new List<byte[]>();
        using (Bitmap src = new Bitmap(srcPath))
        {
            foreach (int s in sizes)
            {
                using (Bitmap f = new Bitmap(s, s, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(f))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.Clear(Color.Transparent);
                        g.DrawImage(src, new Rectangle(0, 0, s, s));
                    }
                    frames.Add(s <= 48 ? EncodeBmp(f) : EncodePng(f));
                }
            }
        }

        using (FileStream fs = new FileStream(dstPath, FileMode.Create))
        using (BinaryWriter bw = new BinaryWriter(fs))
        {
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)sizes.Length);
            long offset = 6 + 16L * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write((uint)frames[i].Length);
                bw.Write((uint)offset);
                offset += frames[i].Length;
            }
            foreach (byte[] f in frames) bw.Write(f);
        }
        return String.Format("{0}: {1} 帧 [{2}]",
                             Path.GetFileName(dstPath), sizes.Length,
                             String.Join(",", Array.ConvertAll(sizes, s => s.ToString())));
    }

    static byte[] EncodePng(Bitmap b)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            b.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }

    static byte[] EncodeBmp(Bitmap b)
    {
        int w = b.Width, h = b.Height;
        BitmapData d = b.LockBits(new Rectangle(0, 0, w, h),
                                  ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] px = new byte[w * h * 4];
        Marshal.Copy(d.Scan0, px, 0, px.Length);
        b.UnlockBits(d);

        int maskStride = ((w + 31) / 32) * 4;
        int imageSize = w * h * 4 + maskStride * h;
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter bw = new BinaryWriter(ms))
        {
            bw.Write(40);
            bw.Write(w);
            bw.Write(h * 2);
            bw.Write((ushort)1);
            bw.Write((ushort)32);
            bw.Write(0);
            bw.Write(imageSize);
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

            for (int y = h - 1; y >= 0; y--)
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 4;
                    bw.Write(px[o]);
                    bw.Write(px[o + 1]);
                    bw.Write(px[o + 2]);
                    bw.Write(px[o + 3]);
                }
            for (int i = 0; i < maskStride * h; i++) bw.Write((byte)0);
            bw.Flush();
            return ms.ToArray();
        }
    }
}
"@

Add-Type -TypeDefinition $src -Language CSharp -ReferencedAssemblies System.Drawing | Out-Null

$iconSrc = Join-Path $RepoRoot 'newcosmos_icon_mascot_v2.png'
$charSrc = Join-Path $RepoRoot 'newcosmos_character_v2.png'
if (!(Test-Path $iconSrc)) { throw "缺少源图 $iconSrc" }
if (!(Test-Path $charSrc)) { throw "缺少源图 $charSrc" }

function Invoke-Cutout {
    param([string]$Source, [string]$Target, [bool]$Crop)
    [MascotAssetTool]::Cutout($Source, $Target, $MinChannel, $MaxSpread, $FeatherFloor, $Crop, 6)
}

Write-Host '[1/4] 应用图标 Resources\AppIcon\appicon.png'
Invoke-Cutout $iconSrc (Join-Path $RepoRoot 'Resources\AppIcon\appicon.png') $false | Write-Host

Write-Host '[2/4] 应用内 Logo 副本 Resources\Images\mascot_icon.png'
$logoPath = Join-Path $RepoRoot 'Resources\Images\mascot_icon.png'
Invoke-Cutout $iconSrc $logoPath $false | Write-Host
# 内层 Logo 只在 28~100px 显示（3x DPI 也就 300px），缩到 512 减少打包体积
[MascotAssetTool]::Resize($logoPath, 512) | Write-Host

Write-Host '[3/4] 立绘 Resources\Images\newcosmos_character.png'
Invoke-Cutout $charSrc (Join-Path $RepoRoot 'Resources\Images\newcosmos_character.png') $true | Write-Host

Write-Host '[4/4] 安装包图标 installer\appicon.ico'
[MascotAssetTool]::MakeIco((Join-Path $RepoRoot 'Resources\AppIcon\appicon.png'),
                           (Join-Path $RepoRoot 'installer\appicon.ico'),
                           [int[]]@(16, 24, 32, 48, 64, 128, 256)) | Write-Host

Write-Host '完成。'
