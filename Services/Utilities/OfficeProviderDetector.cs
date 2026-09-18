using Microsoft.Win32;
using System.Diagnostics;

namespace NewCosmos.Services.Utilities;

/// <summary>
/// 办公软件检测器
    /// 检测系统安装的 WPS / Microsoft Office，返回可用 ProgID
/// </summary>
public static class OfficeProviderDetector
{
    private static readonly string[] WpsWordProgIds = { "KWPS.Application", "KWPS.Application.12", "WPS.Application" };
    private static readonly string[] WpsExcelProgIds = { "KET.Application", "KET.Application.12", "ET.Application" };
    private static readonly string[] OfficeWordProgIds = { "Word.Application", "Word.Application.16", "Word.Application.15", "Word.Application.14" };
    private static readonly string[] OfficeExcelProgIds = { "Excel.Application", "Excel.Application.16", "Excel.Application.15", "Excel.Application.14" };

    private static readonly Lazy<bool> _officeInstalled = new(() =>
        TryGetProgId(OfficeWordProgIds) != null || TryGetProgId(OfficeExcelProgIds) != null);

    /// <summary>
    /// 检测系统安装的办公软件
    /// </summary>
    public static OfficeProviderInfo Detect()
    {
        var info = new OfficeProviderInfo();

        string? wpsWord = TryGetProgId(WpsWordProgIds);
        string? wpsExcel = TryGetProgId(WpsExcelProgIds);
        string? officeWord = TryGetProgId(OfficeWordProgIds);
        string? officeExcel = TryGetProgId(OfficeExcelProgIds);

        bool hasWps = wpsWord != null || wpsExcel != null;
        bool hasOffice = officeWord != null || officeExcel != null;

        info.Provider = (hasWps, hasOffice) switch
        {
            (true, true) => OfficeProvider.Both,
            (true, false) => OfficeProvider.WPS,
            (false, true) => OfficeProvider.Office,
            _ => OfficeProvider.None
        };

        // 优先 MS Office（渲染质量更好）
        info.WordProgId = officeWord ?? wpsWord;
        info.ExcelProgId = officeExcel ?? wpsExcel;

        if (info.WordProgId != null)
        {
            try
            {
                var type = Type.GetTypeFromProgID(info.WordProgId);
                if (type != null)
                {
                    dynamic? app = Activator.CreateInstance(type);
                    if (app is not null)
                    {
                        info.InstallPath = app.Path as string;
                        info.Version = app.Version as string;
                        try { app.Quit(); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"退出Word应用失败（预期）: {ex.Message}");
                }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"检测Word应用失败（预期）: {ex.Message}");
            }
        }

        return info;
    }

    public static bool IsWpsInstalled() => TryGetProgId(WpsWordProgIds) != null || TryGetProgId(WpsExcelProgIds) != null;

    /// <summary>
    /// 检测 MS Office 是否安装（ProgID 注册表探测）。结果进程内缓存一次——安装状态运行时不变。
    /// </summary>
    public static bool IsOfficeInstalled() => _officeInstalled.Value;

    /// <summary>
    /// 强制使用 MS Office，不回退到 WPS
    /// </summary>
    public static string GetWordProgId()
    {
        var progId = TryGetProgId(OfficeWordProgIds);
        if (progId == null)
            throw new InvalidOperationException("Microsoft Word 未安装，请安装 Microsoft Office 365");
        return progId;
    }

    public static string GetExcelProgId()
    {
        var progId = TryGetProgId(OfficeExcelProgIds);
        if (progId == null)
            throw new InvalidOperationException("Microsoft Excel 未安装，请安装 Microsoft Office 365");
        return progId;
    }

    public static string? GetWpsPdfPath()
    {
        string? wpsPath = FindWpsInstallPath();
        if (string.IsNullOrEmpty(wpsPath)) return null;

        var candidates = new[]
        {
            Path.Combine(wpsPath, "office6", "wpspdf.exe"),
            Path.Combine(wpsPath, "office", "wpspdf.exe"),
            Path.Combine(wpsPath, "wpspdf.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindWpsInstallPath()
    {
        var registryPaths = new[]
        {
            @"SOFTWARE\Kingsoft\WPS",
            @"SOFTWARE\Kingsoft Office",
            @"SOFTWARE\WOW6432Node\Kingsoft\WPS",
            @"SOFTWARE\WOW6432Node\Kingsoft Office",
            @"SOFTWARE\Kingsoft\WPS Office",
            @"SOFTWARE\WOW6432Node\Kingsoft\WPS Office"
        };

        foreach (var path in registryPaths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key == null) continue;

                var installPath = key.GetValue("InstallPath") as string;
                if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                    return installPath;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    installPath = subKey?.GetValue("InstallPath") as string;
                    if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                        return installPath;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"读取注册表路径失败（预期）: {path} - {ex.Message}");
            }
        }

        try
        {
            var wpsExePaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Kingsoft", "WPS Office"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Kingsoft", "WPS Office"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kingsoft", "WPS Office")
            };

            foreach (var basePath in wpsExePaths)
            {
                if (Directory.Exists(basePath))
                {
                    var wpsExe = Directory.GetFiles(basePath, "wps.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (!string.IsNullOrEmpty(wpsExe))
                        return Path.GetDirectoryName(wpsExe);
                }
            }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"检测Word应用失败（预期）: {ex.Message}");
            }

        return null;
    }

    private static string? TryGetProgId(string[] progIds)
    {
        return progIds.FirstOrDefault(pid =>
        {
            try { return Type.GetTypeFromProgID(pid) != null; }
            catch (Exception ex)
            {
                Debug.WriteLine($"检测ProgID失败（预期）: {pid} - {ex.Message}");
                return false;
            }
        });
    }

    /// <summary>
    /// 清理超时 WPS/Office 残留进程。
    /// 只终止【无主窗口】的进程——COM 自动化残留是无窗口的后台进程；
    /// 用户自己打开的 Excel/WPS 文档必定有可见主窗口，绝不能误杀
    /// （原实现按进程名+存活时间无差别 Kill，用户编辑超过 30 分钟的文档会被直接杀掉）。
    /// R6 起仅作为最后兜底在 OfficeComWorker 启动时调用一次（不再每次打印/导出前全表扫描）；
    /// 正常生命周期由 OfficeComWorker 按被跟踪 PID 精确管理。
    /// </summary>
    /// <param name="maxAge">进程最大存活时间，超过此时间且无主窗口的将被终止</param>
    /// <returns>被终止的进程数量</returns>
    public static int KillStaleOfficeProcesses(TimeSpan? maxAge = null)
    {
        var threshold = maxAge ?? TimeSpan.FromMinutes(30);
        var killed = 0;
        // 修复：MS Word 的进程名是 WINWORD（原列表写成 "word" 永远匹配不到，Word 残留从未被清理过）
        var processNames = new[] { "wps", "wpsoffice", "et", "wpp", "winword", "excel", "powerpnt" };

        foreach (var name in processNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                foreach (var proc in processes)
                {
                    try
                    {
                        if (proc.MainWindowHandle == IntPtr.Zero &&
                            proc.StartTime < DateTime.Now - threshold)
                        {
                            proc.Kill();
                            killed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"终止进程失败（预期）: {name} - {ex.Message}");
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取进程列表失败（预期）: {name} - {ex.Message}");
            }
        }

        return killed;
    }

    /// <summary>
    /// 终止指定进程 ID 的 COM 服务器进程
    /// </summary>
    public static void KillProcessById(int processId)
    {
        try
        {
            var proc = Process.GetProcessById(processId);
            if (!proc.HasExited)
            {
                proc.Kill();
            }
            proc.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"终止进程失败（预期）: {processId} - {ex.Message}");
        }
    }
}
