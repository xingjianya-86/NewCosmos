using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Core;

/// <summary>
/// 在线更新服务实现（清单 RSA-SHA256 验签 + 安装包 SHA-256 校验 + Inno 静默安装）。
/// </summary>
public class UpdateService : BaseService, IUpdateService
{
    protected override string ServiceName => "UpdateService";

    private static readonly HttpClient _http = CreateHttpClient();
    private static readonly string StateRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewCosmos");
    private static readonly string UpdatesRoot = Path.Combine(StateRoot, "Updates");
    private static readonly string SkipFile = Path.Combine(StateRoot, "update_skip.json");

    private readonly IConfigService _configService;

    public UpdateOptions Options { get; }
    public string CurrentVersion { get; }

    public UpdateService(IConfigService configService, ILoggerService logger) : base(logger)
    {
        _configService = configService;
        Options = configService.GetUpdateOptions();
        CurrentVersion = configService.GetAppOptions().Version;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(15)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan // 逐请求用 CTS 控制超时
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NewCosmos-Updater/1.0");
        return client;
    }

    #region 检查

    public async Task<Result<UpdateCheckResult>> CheckAsync(bool manual, CancellationToken ct = default)
    {
        if (!Options.Enabled || Options.ManifestUrls.Count == 0)
        {
            return Result.Success(new UpdateCheckResult(false, false, null));
        }

        string? lastError = null;
        foreach (var url in Options.ManifestUrls)
        {
            try
            {
                var infoResult = await TryFetchManifestAsync(url, ct);
                if (infoResult.IsFailure)
                {
                    lastError = infoResult.Message;
                    LogWarn($"更新清单不可用: {url} - {infoResult.Message}");
                    continue;
                }

                var info = infoResult.Value;
                var updateAvailable = CompareVersions(info.LatestVersion, CurrentVersion) > 0;
                var forceUpdate = updateAvailable
                    && (info.ForceByManifest || CompareVersions(info.MinSupportedVersion, CurrentVersion) > 0);

                LogInfo($"更新检查完成: 当前={CurrentVersion}, 最新={info.LatestVersion}, 可用={updateAvailable}, 强制={forceUpdate}");
                return Result.Success(new UpdateCheckResult(updateAvailable, forceUpdate, info));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Result.Failure<UpdateCheckResult>(ErrorCodes.CANCELLED, "已取消");
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                LogWarn($"更新检查异常: {url} - {ex.Message}");
            }
        }

        return Result.Failure<UpdateCheckResult>(ErrorCodes.NETWORK_ERROR, lastError ?? "无法连接更新服务器");
    }

    private async Task<Result<UpdateInfo>> TryFetchManifestAsync(string url, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Options.HttpTimeoutSeconds));

        var json = await _http.GetStringAsync(url, timeoutCts.Token);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var schema = root.TryGetProperty("schema", out var schemaEl) && schemaEl.TryGetInt32(out var s) ? s : 1;
        if (schema != 1)
            return Result.Failure<UpdateInfo>(ErrorCodes.VALIDATION_FAILED, $"不支持的清单版本: {schema}");

        var channel = GetString(root, "channel");
        if (!string.Equals(channel, UpdateSignatureConstants.Channel, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<UpdateInfo>(ErrorCodes.VALIDATION_FAILED, $"更新渠道不匹配: {channel}");

        var latest = GetString(root, "latestVersion");
        var minSupported = GetString(root, "minSupportedVersion");
        var notes = GetString(root, "notes");
        var publishedAt = GetString(root, "publishedAt");
        var force = root.TryGetProperty("force", out var forceEl) && forceEl.ValueKind == JsonValueKind.True;

        if (string.IsNullOrWhiteSpace(latest))
            return Result.Failure<UpdateInfo>(ErrorCodes.VALIDATION_FAILED, "清单缺少 latestVersion");

        if (!root.TryGetProperty("package", out var pkg))
            return Result.Failure<UpdateInfo>(ErrorCodes.VALIDATION_FAILED, "清单缺少 package");

        var pkgUrl = GetString(pkg, "url");
        var pkgSha = GetString(pkg, "sha256");
        var pkgSize = pkg.TryGetProperty("size", out var sizeEl) && sizeEl.TryGetInt64(out var size) ? size : 0;
        var signature = GetString(root, "signature");

        if (string.IsNullOrWhiteSpace(pkgUrl) || string.IsNullOrWhiteSpace(pkgSha))
            return Result.Failure<UpdateInfo>(ErrorCodes.VALIDATION_FAILED, "清单 package 字段不完整");
        if (string.IsNullOrWhiteSpace(signature))
            return Result.Failure<UpdateInfo>(ErrorCodes.VALIDATION_FAILED, "清单缺少 signature");

        // 签名字段串（与 Scripts/publish_release.ps1 / UpdateSigningTool 保持一致）
        var canonical = BuildCanonicalString(schema, channel, latest, minSupported, force, publishedAt, pkgUrl, pkgSize, pkgSha);
        if (!VerifyManifestSignature(canonical, signature!))
        {
            Logger.LogSecurity("更新清单签名校验失败", ("Url", url), ("LatestVersion", latest));
            return Result.Failure<UpdateInfo>(ErrorCodes.NETWORK_ERROR, "更新清单签名校验失败");
        }

        // 相对包地址 → 绝对地址
        var absoluteUrl = Uri.TryCreate(pkgUrl, UriKind.Absolute, out var abs)
            ? abs.ToString()
            : new Uri(new Uri(url), pkgUrl).ToString();

        DateTime? published = DateTime.TryParse(publishedAt, out var p) ? p : null;

        return Result.Success(new UpdateInfo(latest!, minSupported, force, notes, absoluteUrl, pkgSize, pkgSha!.ToLowerInvariant(), published));
    }

    /// <summary>签名字段串（顺序与发布脚本一致）：schema|channel|latest|minSupported|force|publishedAt|pkgUrl|pkgSize|pkgSha</summary>
    private static string BuildCanonicalString(int schema, string? channel, string? latest, string? minSupported,
        bool force, string? publishedAt, string pkgUrl, long pkgSize, string pkgSha)
        => string.Join("|", schema.ToString(),
            channel ?? string.Empty,
            latest ?? string.Empty,
            minSupported ?? string.Empty,
            force ? "true" : "false",
            publishedAt ?? string.Empty,
            pkgUrl,
            pkgSize.ToString(),
            pkgSha);

    private bool VerifyManifestSignature(string canonical, string signatureBase64)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(UpdateSignatureConstants.PublicKeyPem);
            var signature = Convert.FromBase64String(signatureBase64);
            return rsa.VerifyData(Encoding.UTF8.GetBytes(canonical), signature,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex)
        {
            LogException(ex, "更新清单验签");
            return false;
        }
    }

    /// <summary>版本比较：支持 1.1.yyyyMMdd 三段数字；解析失败回退字符串序比较</summary>
    private static int CompareVersions(string? left, string? right)
    {
        if (Version.TryParse(NormalizeVersion(left), out var lv) && Version.TryParse(NormalizeVersion(right), out var rv))
            return lv.CompareTo(rv);

        return string.CompareOrdinal(left ?? string.Empty, right ?? string.Empty);
    }

    private static string NormalizeVersion(string? version)
    {
        var v = (version ?? string.Empty).Trim().TrimStart('v', 'V');
        return string.IsNullOrEmpty(v) ? "0.0" : v;
    }

    private static string GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? string.Empty
            : string.Empty;

    #endregion

    #region 下载

    public async Task<Result<string>> DownloadAsync(UpdateInfo info, Action<double>? onProgress, CancellationToken ct = default)
    {
        try
        {
            var dir = Path.Combine(UpdatesRoot, info.LatestVersion);
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, $"NewCosmosSetup_{info.LatestVersion}.exe");

            if (File.Exists(target) && VerifyFileHash(target, info.PackageSha256))
            {
                LogInfo($"安装包已存在且校验通过: {target}");
                onProgress?.Invoke(100);
                return Result.Success(target);
            }

            Exception? lastError = null;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    LogInfo($"开始下载安装包: {info.PackageUrl} (第 {attempt} 次)");
                    var downloaded = await DownloadOnceAsync(info, target, onProgress, ct);
                    if (downloaded.IsSuccess) return downloaded;
                    lastError = new Exception(downloaded.Message);
                    LogWarn($"安装包下载失败（第 {attempt} 次）: {downloaded.Message}");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return Result.Failure<string>(ErrorCodes.CANCELLED, "已取消下载");
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    LogWarn($"安装包下载异常（第 {attempt} 次）: {ex.Message}");
                }

                await Task.Delay(1000 * attempt, ct);
            }

            return Result.Failure<string>(ErrorCodes.NETWORK_ERROR, $"安装包下载失败: {lastError?.Message}");
        }
        catch (Exception ex)
        {
            LogException(ex, "下载更新包");
            return Result.FromException<string>(ex);
        }
    }

    private async Task<Result<string>> DownloadOnceAsync(UpdateInfo info, string target, Action<double>? onProgress, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Options.DownloadTimeoutSeconds));

        var tmp = target + ".download";
        if (File.Exists(tmp)) File.Delete(tmp);

        using var response = await _http.GetAsync(info.PackageUrl, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? info.PackageSize;
        await using var src = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
        await using var dst = File.Create(tmp);
        using var sha = SHA256.Create();

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, timeoutCts.Token)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), timeoutCts.Token);
            sha.TransformBlock(buffer, 0, n, null, 0);
            read += n;
            if (total > 0) onProgress?.Invoke(Math.Clamp((double)read / total * 100, 0, 100));
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        await dst.FlushAsync(timeoutCts.Token);
        await dst.DisposeAsync();

        var hash = Convert.ToHexString(sha.Hash ?? Array.Empty<byte>()).ToLowerInvariant();
        if (!string.Equals(hash, info.PackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(tmp); } catch { }
            return Result.Failure<string>(ErrorCodes.NETWORK_ERROR, "安装包校验失败（SHA-256 不匹配）");
        }

        if (File.Exists(target)) File.Delete(target);
        File.Move(tmp, target);
        onProgress?.Invoke(100);
        LogInfo($"安装包下载并校验完成: {target} ({read} 字节)");
        return Result.Success(target);
    }

    private static bool VerifyFileHash(string path, string expectedSha256)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            return string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region 安装与跳过

    public Result LaunchInstaller(string installerPath, string version)
    {
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), $"newcosmos_update_{version}.log");
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1 /LOG=\"{logPath}\"",
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Path.GetTempPath()
            };

            var process = Process.Start(psi);
            if (process == null)
                return Result.Failure(ErrorCodes.NETWORK_ERROR, "无法启动更新安装程序");

            Logger.LogBusiness("已启动更新安装程序，应用即将退出", ("Installer", installerPath), ("Version", version), ("Pid", process.Id));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "启动更新安装程序");
            return Result.FromException(ex);
        }
    }

    public string? GetSkippedVersion()
    {
        try
        {
            if (!File.Exists(SkipFile)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(SkipFile));
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public void SetSkippedVersion(string version)
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            File.WriteAllText(SkipFile, $"{{\"version\":\"{version.Trim()}\"}}");
            Logger.LogBusiness("已跳过更新版本", ("Version", version));
        }
        catch (Exception ex)
        {
            LogWarn($"记录跳过版本失败: {ex.Message}");
        }
    }

    #endregion
}
