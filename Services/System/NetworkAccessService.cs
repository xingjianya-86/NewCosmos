using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Services.System;

/// <summary>
/// ZeroTier 网络接入服务（Windows）。
/// 通过 zerotier-cli 检测/加入网络；安装走随包 MSI（管理员提权）。
/// 管理令牌 authtoken.secret 默认仅管理员可读：接入/退出时按需提权一次授予当前用户读权限。
/// </summary>
public class NetworkAccessService : BaseService, INetworkAccessService
{
    protected override string ServiceName => "NetworkAccessService";

    private static readonly string[] CliCandidates =
    {
        @"C:\Program Files (x86)\ZeroTier\One\zerotier-cli.bat",
        @"C:\Program Files\ZeroTier\One\zerotier-cli.bat",
    };

    /// <summary>ZeroTier 本地管理令牌（默认 ACL 仅 Administrators/SYSTEM 可读）</summary>
    private const string TokenPath = @"C:\ProgramData\ZeroTier\One\authtoken.secret";

    public NetworkAccessService(ILoggerService logger) : base(logger) { }

    public async Task<Result<ZeroTierStatus>> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var status = new ZeroTierStatus();
            var cli = FindCli();
            if (cli == null)
                return Result.Success(status);

            status.Installed = true;
            status.CliPath = cli;

            var info = await RunCaptureAsync(cli, "info", ct);
            if (info.ExitCode == 0)
            {
                // 200 info <nodeid> <version> <status>
                var parts = info.StdOut.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4)
                {
                    status.NodeId = parts[2];
                    status.Version = parts[3];
                    status.Online = string.Equals(parts[^1], "ONLINE", StringComparison.OrdinalIgnoreCase);
                }
            }
            else if (IsTokenPermissionError(info))
            {
                status.TokenAccessDenied = true;
                LogWarn("ZeroTier CLI 无权读取管理令牌（需在“接入”时提权授权一次）");
            }

            var list = await RunCaptureAsync(cli, "listnetworks", ct);
            if (list.ExitCode == 0)
                status.Networks = ParseNetworks(list.StdOut);
            else if (IsTokenPermissionError(list))
                status.TokenAccessDenied = true;

            return Result.Success(status);
        }
        catch (Exception ex)
        {
            LogException(ex, "ZeroTier 状态检测失败");
            return Result.FromException<ZeroTierStatus>(ex);
        }
    }

    public async Task<Result> InstallZeroTierAsync(CancellationToken ct = default)
    {
        try
        {
            if (FindCli() != null)
                return Result.Success();

            var msi = FindBundledMsi();
            if (msi == null)
                return Result.Failure(ErrorCodes.FILE_NOT_FOUND,
                    "未找到随包的 ZeroTier 安装包（Resources\\ZeroTier\\*.msi）");

            var psi = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/i \"{msi}\" /qn /norestart",
                UseShellExecute = true,
                Verb = "runas"
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return Result.Failure(ErrorCodes.UNKNOWN_ERROR, "无法启动 ZeroTier 安装程序");

            await proc.WaitForExitAsync(ct);

            if (FindCli() == null)
                return Result.Failure(ErrorCodes.UNKNOWN_ERROR, "ZeroTier 安装未完成，请重试或手动安装");

            // 安装后顺带授予当前用户管理令牌读权限（若令牌尚未生成，接入时会再次按需授权）
            var access = await EnsureCliAccessAsync(ct);
            if (access.IsFailure)
                LogWarn($"ZeroTier 安装后授权未完成（接入时将重试）: {access.Message}");

            LogInfo("ZeroTier 安装完成");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "ZeroTier 安装失败");
            return Result.Failure(ErrorCodes.UNKNOWN_ERROR, "ZeroTier 安装失败：可能是未授予管理员权限");
        }
    }

    public async Task<Result> JoinNetworkAsync(string networkId, string? moonId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(networkId))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "网络ID不能为空");

        var cli = FindCli();
        if (cli == null)
            return Result.Failure(ErrorCodes.NOT_FOUND, "未检测到 ZeroTier，请先安装");

        var join = await RunCaptureAsync(cli, $"join {networkId}", ct);
        if (IsTokenPermissionError(join))
        {
            // 管理令牌仅管理员可读：提权一次授予当前用户读权限后重试（用户动作触发，可接受 UAC）
            var access = await EnsureCliAccessAsync(ct);
            if (access.IsFailure)
            {
                return access.ErrorCode == ErrorCodes.CANCELLED
                    ? Result.Failure(ErrorCodes.CANCELLED, access.Message)
                    : Result.Failure(ErrorCodes.ZEROTIER_TOKEN_DENIED,
                        $"{access.Message}；也可手动以管理员身份执行授权后重试");
            }
            join = await RunCaptureAsync(cli, $"join {networkId}", ct);
        }

        if (join.ExitCode != 0 || !join.StdOut.Contains("200", StringComparison.Ordinal))
        {
            LogWarn($"ZeroTier join 失败: {join.StdOut} {join.StdErr}");
            var hint = IsTokenPermissionError(join)
                ? "（管理权限未授权，请重试接入并同意管理员授权）"
                : string.Empty;
            return Result.Failure(ErrorCodes.UNKNOWN_ERROR, $"加入网络失败：{FirstLine(join.StdErr, join.StdOut)}{hint}");
        }

        if (!string.IsNullOrWhiteSpace(moonId))
        {
            var orbit = await RunCaptureAsync(cli, $"orbit {moonId} {moonId}", ct);
            if (orbit.ExitCode != 0)
                LogWarn($"ZeroTier orbit 失败（不影响接入）: {orbit.StdErr}");
        }

        LogInfo($"ZeroTier 加入网络: {networkId}");
        return Result.Success();
    }

    public async Task<Result> LeaveNetworkAsync(string networkId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(networkId))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "网络ID不能为空");

        var cli = FindCli();
        if (cli == null)
            return Result.Failure(ErrorCodes.NOT_FOUND, "未检测到 ZeroTier");

        var leave = await RunCaptureAsync(cli, $"leave {networkId}", ct);
        if (IsTokenPermissionError(leave))
        {
            var access = await EnsureCliAccessAsync(ct);
            if (access.IsFailure)
            {
                return access.ErrorCode == ErrorCodes.CANCELLED
                    ? Result.Failure(ErrorCodes.CANCELLED, access.Message)
                    : Result.Failure(ErrorCodes.ZEROTIER_TOKEN_DENIED, access.Message);
            }
            leave = await RunCaptureAsync(cli, $"leave {networkId}", ct);
        }

        if (leave.ExitCode != 0)
            return Result.Failure(ErrorCodes.UNKNOWN_ERROR, $"退出网络失败：{FirstLine(leave.StdErr, leave.StdOut)}");

        LogInfo($"ZeroTier 退出网络: {networkId}");
        return Result.Success();
    }

    public async Task<Result> EnsureCliAccessAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(TokenPath))
                return Result.Failure(ErrorCodes.NOT_FOUND, "未找到 ZeroTier 管理令牌文件，请先安装并启动 ZeroTier");

            if (IsTokenReadable())
                return Result.Success();

            // 提权（UAC）为当前用户授予令牌读权限；输出重定向到临时文件（ShellExecute 无法直接捕获流）
            var user = WindowsIdentity.GetCurrent().Name;
            var tempOut = Path.Combine(Path.GetTempPath(), $"newcosmos_zt_acl_{Guid.NewGuid():N}.txt");
            var args = $"/c \"\"icacls\" \"{TokenPath}\" /grant \"{user}:R\" > \"{tempOut}\" 2>&1\"";

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return Result.Failure(ErrorCodes.UNKNOWN_ERROR, "无法启动授权程序");

            await proc.WaitForExitAsync(ct);

            var output = string.Empty;
            try { output = File.Exists(tempOut) ? await File.ReadAllTextAsync(tempOut, ct) : string.Empty; } catch { /* ignore */ }
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { /* ignore */ }

            if (!IsTokenReadable())
            {
                LogWarn($"ZeroTier 令牌授权未生效: exit={proc.ExitCode}, out={output}");
                return Result.Failure(ErrorCodes.ZEROTIER_TOKEN_DENIED,
                    "ZeroTier 接入权限授权失败（可手动以管理员身份执行 icacls 授权后重试）");
            }

            Logger.LogSecurity("已授予 ZeroTier 管理令牌读权限", ("User", user));
            return Result.Success();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            LogWarn("用户取消了 ZeroTier 权限授权（UAC）");
            return Result.Failure(ErrorCodes.CANCELLED, "已取消管理员授权，接入未完成");
        }
        catch (Exception ex)
        {
            LogException(ex, "ZeroTier 权限授权");
            return Result.FromException(ex);
        }
    }

    public async Task<Result> OpenAuthorizationPageAsync(string url, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url))
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "授权地址未配置");
            await Launcher.OpenAsync(url);
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "打开授权页面失败");
            return Result.Failure(ErrorCodes.UNKNOWN_ERROR, "打开授权页面失败");
        }
    }

    // ── 内部工具 ──

    private static string? FindCli()
    {
        foreach (var path in CliCandidates)
            if (File.Exists(path)) return path;
        return null;
    }

    private static string? FindBundledMsi()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Resources", "ZeroTier");
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, "*.msi").FirstOrDefault();
    }

    /// <summary>当前用户是否可读取管理令牌</summary>
    private static bool IsTokenReadable()
    {
        try
        {
            using var stream = File.Open(TokenPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return stream.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>CLI 输出是否命中"管理令牌不可读"错误</summary>
    private static bool IsTokenPermissionError((int ExitCode, string StdOut, string StdErr) result)
    {
        var text = result.StdErr + " " + result.StdOut;
        return text.Contains("authtoken", StringComparison.OrdinalIgnoreCase)
               || text.Contains("try again as root", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunCaptureAsync(string cli, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{cli}\" {args}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) return (-1, string.Empty, "无法启动 zerotier-cli");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(true); } catch { /* ignore */ }
            return (-1, string.Empty, "zerotier-cli 执行超时");
        }

        return (proc.ExitCode, await stdoutTask, await stderrTask);
    }

    private static List<ZeroTierNetworkState> ParseNetworks(string output)
    {
        var list = new List<ZeroTierNetworkState>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // 200 listnetworks <nwid> <name> <mac> <status> <type> <dev> [ips...]
            if (p.Length < 8 || p[0] != "200" || p[1] != "listnetworks") continue;

            var state = new ZeroTierNetworkState
            {
                Nwid = p[2],
                Name = p[3],
                Status = p[5],
                Type = p[6],
                Device = p[7]
            };
            if (p.Length > 8)
                state.AssignedAddresses = p[8].Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            list.Add(state);
        }
        return list;
    }

    private static string FirstLine(string a, string b)
    {
        var text = string.IsNullOrWhiteSpace(a) ? b : a;
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "未知错误";
        return line.Trim();
    }
}
