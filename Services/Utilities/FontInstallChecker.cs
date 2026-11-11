#if WINDOWS
using Microsoft.Win32;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using SkiaSharp;

namespace NewCosmos.Services.Utilities;

/// <summary>公文字体检查结果项（族名 + 随包源文件路径）</summary>
public sealed record OfficialFontItem(string FamilyNames, string SourceFilePath, string FileName);

/// <summary>
/// 公文字体安装检查器（公文文书打印/导出依赖系统已装字体，AGENTS §2 公文字体目录）。
/// 检测：扫描安装目录下 公文字体\ 全部 ttf/ttc → 解析族名 → 与系统已装族名比对 → 返回缺失项。
/// 安装：一键装到当前用户字体目录（免管理员）+ 写 HKCU 注册表 + 广播 WM_FONTCHANGE。
/// 仿 OfficeProviderDetector 静态工具类先例；SkiaSharp 枚举异常回退注册表比对。
/// </summary>
public static class FontInstallChecker
{
    /// <summary>检查结果进程内缓存一次（装完字体需重启应用才复查，与 Office 检测同口径）</summary>
    private static readonly Lazy<IReadOnlyList<OfficialFontItem>> _missingCache =
        new(() => CheckCore(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>缺失字体清单（进程内缓存；目录不存在返回空清单）</summary>
    public static IReadOnlyList<OfficialFontItem> GetMissingFonts() => _missingCache.Value;

    /// <summary>强制重新检查（安装后复检用，绕过缓存）</summary>
    public static IReadOnlyList<OfficialFontItem> Recheck() => CheckCore();

    private static IReadOnlyList<OfficialFontItem> CheckCore()
    {
        var missing = new List<OfficialFontItem>();
        try
        {
            var fontDir = Path.Combine(AppContext.BaseDirectory, OfficialFontConstants.FontDirectoryName);
            if (!Directory.Exists(fontDir))
                return missing; // 开发机未攜/Android 侧：静默跳过

            var files = Directory.EnumerateFiles(fontDir, "*" + OfficialFontConstants.TtfExtension, SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(fontDir, "*" + OfficialFontConstants.TtcExtension, SearchOption.AllDirectories));

            HashSet<string>? installed = null;
            foreach (var file in files)
            {
                installed ??= LoadInstalledFamilies();
                var candidates = FontFamilyReader.ReadFamilyNames(file);
                var hit = candidates.Any(c => installed.Contains(c));
                if (!hit)
                    missing.Add(new OfficialFontItem(string.Join(" / ", candidates), file, Path.GetFileName(file)));
            }
        }
        catch
        {
            // 检查本身失败不阻断启动：调用方按"无缺失"处理并由上层记日志
        }
        return missing;
    }

    /// <summary>
    /// 系统已装字体族名集合（OrdinalIgnoreCase）。
    /// 两路【合并】（任一命中即算已装）：
    /// ① SkiaSharp 枚举（覆盖系统字体）；
    /// ② HKLM/HKCU 注册表 Fonts 值名（权威安装记录）——刚装的用户字体在 GDI/系统字体缓存
    ///    刷新前 Skia 枚举可能看不到（实测 2026-11-10：装完重启仍漏报），注册表值必然已写入，
    ///    合并后复检立即通过，且与 Word 的 GDI 字体链判定一致。
    /// Skia 枚举整体异常时仍返回注册表集合；两路都空才返回空集合。
    /// </summary>
    private static HashSet<string> LoadInstalledFamilies()
    {
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Default 是共享单例，不可 Dispose（Dispose 会破坏后续枚举）
            var skiaFamilies = SKFontManager.Default.GetFontFamilies();
            if (skiaFamilies != null)
            {
                foreach (var f in skiaFamilies)
                    if (!string.IsNullOrWhiteSpace(f)) families.Add(f);
            }
        }
        catch
        {
            // Skia 枚举失败：至少还有注册表一路
        }

        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var key = root.OpenSubKey(OfficialFontConstants.UserFontRegistryKey);
                if (key == null) continue;
                foreach (var valueName in key.GetValueNames())
                {
                    var family = valueName;
                    if (family.EndsWith(OfficialFontConstants.RegistryValueSuffix, StringComparison.OrdinalIgnoreCase))
                        family = family[..^OfficialFontConstants.RegistryValueSuffix.Length];
                    if (!string.IsNullOrWhiteSpace(family))
                        families.Add(family);
                }
            }
            catch
            {
                // 单个注册表根读取失败跳过
            }
        }

        return families;
    }

    /// <summary>
    /// 一键安装缺失字体（当前用户级，免管理员）：
    /// 复制到 %LOCALAPPDATA%\Microsoft\Windows\Fonts → 写 HKCU Fonts 注册表 → 广播 WM_FONTCHANGE。
    /// </summary>
    /// <returns>Success=全部装成；Failure=带首个失败原因（ErrorCode=FILE_WRITE_ERROR）</returns>
    public static Result Install(IReadOnlyList<OfficialFontItem> missing)
    {
        if (missing == null || missing.Count == 0)
            return Result.Success();

        var userFontDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            OfficialFontConstants.UserFontInstallDirectoryName);

        var installedCount = 0;
        var failures = new List<string>();

        foreach (var item in missing)
        {
            try
            {
                Directory.CreateDirectory(userFontDir);
                var target = Path.Combine(userFontDir, item.FileName);

                // 文件已存在（上次装到一半/同名他源）：视为已就位，只补注册表
                if (!File.Exists(target))
                    File.Copy(item.SourceFilePath, target, overwrite: false);

                using var key = Registry.CurrentUser.CreateSubKey(OfficialFontConstants.UserFontRegistryKey, writable: true);
                if (key == null)
                {
                    failures.Add($"{item.FileName}: 无法打开用户字体注册表");
                    continue;
                }
                key.SetValue(
                    StripExtension(item.FileName) + OfficialFontConstants.RegistryValueSuffix,
                    item.FileName,
                    RegistryValueKind.String);

                installedCount++;
            }
            catch (Exception ex)
            {
                failures.Add($"{item.FileName}: {ex.Message}");
            }
        }

        if (installedCount > 0)
            BroadcastFontChange();

        return failures.Count == 0
            ? Result.Success()
            : Result.Failure(
                ErrorCodes.FILE_WRITE_ERROR,
                $"已安装 {installedCount}/{missing.Count} 个字体，{failures.Count} 个失败：{string.Join("；", failures)}");
    }

    /// <summary>注册表值名基名：去掉扩展名（注册表值名按族名+后缀，不带文件扩展名）</summary>
    private static string StripExtension(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName);

    /// <summary>
    /// 广播 WM_FONTCHANGE 让运行中的应用（含 Word）刷新字体表；
    /// SendMessageTimeout 防止有窗口卡死时广播挂起本进程。
    /// </summary>
    private static void BroadcastFontChange()
    {
        try
        {
            const int WM_FONTCHANGE = 0x001D;
            const uint HWND_BROADCAST = 0xFFFF;
            const uint SMTO_ABORTIFHUNG = 0x0002;
            _ = SendMessageTimeout(HWND_BROADCAST, WM_FONTCHANGE, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 3000, out _);
        }
        catch
        {
            // 广播失败不影响安装结果（新起进程本来就能读到新字体）
        }
    }

    [global::System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        uint hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
#endif
