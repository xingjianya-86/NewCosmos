using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BsDiff;
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
    private readonly Services.Platform.IAppPackageInstaller _packageInstaller;

    public UpdateOptions Options { get; }
    public string CurrentVersion { get; }

    public UpdateService(
        IConfigService configService,
        ILoggerService logger,
        Services.Platform.IAppPackageInstaller packageInstaller) : base(logger)
    {
        _configService = configService;
        _packageInstaller = packageInstaller;
        Options = configService.GetUpdateOptions();
        CurrentVersion = configService.GetAppOptions().Version;
    }

    /// <summary>当前平台生效的清单地址（Android 优先 AndroidManifestUrls，否则按约定从 ManifestUrls 推导）</summary>
    private IReadOnlyList<string> EffectiveManifestUrls
    {
        get
        {
            if (!OperatingSystem.IsAndroid())
                return Options.ManifestUrls;

            if (Options.AndroidManifestUrls.Count > 0)
                return Options.AndroidManifestUrls;

            return Options.ManifestUrls.Select(DeriveAndroidManifestUrl).ToList();
        }
    }

    /// <summary>把 Windows 清单地址的 /stable/ 段替换为 /android/（Android 清单约定目录）</summary>
    private static string DeriveAndroidManifestUrl(string url)
        => url.Contains("/stable/", StringComparison.OrdinalIgnoreCase)
            ? url.Replace("/stable/", "/android/", StringComparison.OrdinalIgnoreCase)
            : url;

    /// <summary>安装包扩展名（Windows=.exe，Android=.apk）</summary>
    private static string InstallerExtension => OperatingSystem.IsAndroid() ? ".apk" : ".exe";

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
        if (!Options.Enabled || EffectiveManifestUrls.Count == 0)
        {
            return Result.Success(new UpdateCheckResult(false, false, null));
        }

        string? lastError = null;
        foreach (var url in EffectiveManifestUrls)
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
        if (schema < 1 || schema > 2)
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

        // ── schema=2: 解析 patch 字段 ──
        string? patchUrl = null;
        long patchSize = 0;
        string? patchSha = null;
        string? patchBase = null;

        if (schema >= 2 && root.TryGetProperty("patch", out var patchEl) && patchEl.ValueKind == JsonValueKind.Object)
        {
            patchUrl = GetString(patchEl, "url");
            patchSha = GetString(patchEl, "sha256");
            patchBase = GetString(patchEl, "baseVersion");
            patchSize = patchEl.TryGetProperty("size", out var pSizeEl) && pSizeEl.TryGetInt64(out var pSize) ? pSize : 0;
        }

        // 签名字段串（与 Scripts/publish_release.ps1 / UpdateSigningTool 保持一致）
        var canonical = BuildCanonicalString(schema, channel, latest, minSupported, force, publishedAt, pkgUrl, pkgSize, pkgSha, patchSha, patchBase);
        if (!VerifyManifestSignature(canonical, signature!))
        {
            Logger.LogSecurity("更新清单签名校验失败", ("Url", url), ("LatestVersion", latest));
            return Result.Failure<UpdateInfo>(ErrorCodes.NETWORK_ERROR, "更新清单签名校验失败");
        }

        // 相对包地址 → 绝对地址
        var absoluteUrl = Uri.TryCreate(pkgUrl, UriKind.Absolute, out var abs)
            ? abs.ToString()
            : new Uri(new Uri(url), pkgUrl).ToString();

        // 相对 patch 地址 → 绝对地址
        string? absolutePatchUrl = null;
        if (!string.IsNullOrEmpty(patchUrl))
        {
            absolutePatchUrl = Uri.TryCreate(patchUrl, UriKind.Absolute, out var patchAbs)
                ? patchAbs.ToString()
                : new Uri(new Uri(url), patchUrl).ToString();
        }

        DateTime? published = DateTime.TryParse(publishedAt, out var p) ? p : null;

        return Result.Success(new UpdateInfo(latest!, minSupported, force, notes, absoluteUrl, pkgSize, pkgSha!.ToLowerInvariant(), published,
            absolutePatchUrl, patchSize, patchSha?.ToLowerInvariant(), patchBase));
    }

    /// <summary>
    /// 签名字段串（与 Scripts/publish_release.ps1 / UpdateSigningTool 保持一致）：
    /// schema=1: schema|channel|latest|minSupported|force|publishedAt|pkgUrl|pkgSize|pkgSha
    /// schema=2: ...+patchSha|patchBase
    /// </summary>
    private static string BuildCanonicalString(int schema, string? channel, string? latest, string? minSupported,
        bool force, string? publishedAt, string pkgUrl, long pkgSize, string pkgSha,
        string? patchSha = null, string? patchBase = null)
    {
        var sb = new StringBuilder();
        sb.Append(schema); sb.Append('|');
        sb.Append(channel ?? string.Empty); sb.Append('|');
        sb.Append(latest ?? string.Empty); sb.Append('|');
        sb.Append(minSupported ?? string.Empty); sb.Append('|');
        sb.Append(force ? "true" : "false"); sb.Append('|');
        sb.Append(publishedAt ?? string.Empty); sb.Append('|');
        sb.Append(pkgUrl); sb.Append('|');
        sb.Append(pkgSize.ToString()); sb.Append('|');
        sb.Append(pkgSha);
        if (schema >= 2)
        {
            sb.Append('|');
            sb.Append(patchSha ?? string.Empty); sb.Append('|');
            sb.Append(patchBase ?? string.Empty);
        }
        return sb.ToString();
    }

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
            // 1. 尝试增量 patch 路径
            if (!string.IsNullOrEmpty(info.PatchUrl)
                && string.Equals(CurrentVersion, info.PatchBaseVersion, StringComparison.OrdinalIgnoreCase))
            {
                var patchDir = Path.Combine(UpdatesRoot, info.LatestVersion);
                Directory.CreateDirectory(patchDir);
                var patchTarget = Path.Combine(patchDir, $"NewCosmosPatch_{info.PatchBaseVersion}_{info.LatestVersion}.bin");

                var patchResult = await DownloadAndVerifyFileAsync(info.PatchUrl, patchTarget, info.PatchSha256!, info.PatchSize, onProgress, ct);
                if (patchResult.IsSuccess)
                {
                    LogInfo("patch 文件下载校验完成，开始应用");
                    var applyResult = await ApplyPatchToInstallerAsync(patchResult.Value, info, patchDir, onProgress, ct);
                    if (applyResult.IsSuccess)
                    {
                        LogInfo($"patch 应用成功: {applyResult.Value}");
                        return applyResult;
                    }
                    LogWarn($"patch 应用失败: {applyResult.Message}，回退全量安装包");
                }
                else
                {
                    LogWarn($"patch 下载失败: {patchResult.Message}，回退全量安装包");
                }
            }

            // 2. 回退全量安装包
            return await DownloadFullInstallerAsync(info, onProgress, ct);
        }
        catch (Exception ex)
        {
            LogException(ex, "下载更新包");
            return Result.FromException<string>(ex);
        }
    }

    /// <summary>下载全量安装包（含 3 次重试）</summary>
    private async Task<Result<string>> DownloadFullInstallerAsync(UpdateInfo info, Action<double>? onProgress, CancellationToken ct)
    {
        var dir = Path.Combine(UpdatesRoot, info.LatestVersion);
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, $"NewCosmosSetup_{info.LatestVersion}{InstallerExtension}");

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

    /// <summary>下载并校验单个文件（通用）</summary>
    private async Task<Result<string>> DownloadAndVerifyFileAsync(
        string url, string targetPath, string expectedSha256, long fileSize,
        Action<double>? onProgress, CancellationToken ct)
    {
        if (File.Exists(targetPath) && VerifyFileHash(targetPath, expectedSha256))
        {
            onProgress?.Invoke(100);
            return Result.Success(targetPath);
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var tmp = targetPath + ".download";
                if (File.Exists(tmp)) File.Delete(tmp);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(Options.DownloadTimeoutSeconds));

                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                response.EnsureSuccessStatusCode();

                var total = response.Content.Headers.ContentLength ?? fileSize;
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
                if (!string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(tmp); } catch { }
                    return Result.Failure<string>(ErrorCodes.NETWORK_ERROR, "文件校验失败（SHA-256 不匹配）");
                }

                if (File.Exists(targetPath)) File.Delete(targetPath);
                File.Move(tmp, targetPath);
                onProgress?.Invoke(100);
                LogInfo($"文件下载并校验完成: {targetPath} ({read} 字节)");
                return Result.Success(targetPath);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Result.Failure<string>(ErrorCodes.CANCELLED, "已取消下载");
            }
            catch (Exception ex)
            {
                LogWarn($"文件下载异常（第 {attempt} 次）: {ex.Message}");
            }

            await Task.Delay(1000 * attempt, ct);
        }

        return Result.Failure<string>(ErrorCodes.NETWORK_ERROR, "文件下载失败");
    }

    /// <summary>
    /// 获取上一版本安装包路径（用于 patch 的 base）；本地无缓存则下载。
    /// </summary>
    private async Task<Result<string>> GetOrDownloadBaseInstallerAsync(string baseVersion, Action<double>? onProgress, CancellationToken ct)
    {
        var dir = Path.Combine(UpdatesRoot, baseVersion);
        var path = Path.Combine(dir, $"NewCosmosSetup_{baseVersion}.exe");
        if (File.Exists(path))
            return Result.Success(path);

        // 需要下载 base 安装包：构造一个最小的 UpdateInfo
        var manifestUrl = EffectiveManifestUrls.FirstOrDefault() ?? "";
        var baseManifest = await FetchVersionManifestAsync(baseVersion, ct);
        if (baseManifest == null)
            return Result.Failure<string>(ErrorCodes.NETWORK_ERROR, $"无法获取 base 版本 {baseVersion} 的清单");

        Directory.CreateDirectory(dir);
        var downloadResult = await DownloadFullInstallerAsync(baseManifest, onProgress, ct);
        return downloadResult;
    }

    /// <summary>
    /// 获取指定版本的 manifest（从 releases/{version}/update.json）
    /// </summary>
    private async Task<UpdateInfo?> FetchVersionManifestAsync(string version, CancellationToken ct)
    {
        var baseUrl = EffectiveManifestUrls.FirstOrDefault();
        if (string.IsNullOrEmpty(baseUrl)) return null;

        // releases/<version>/update.json
        var manifestUrl = baseUrl.Replace("stable/update.json", $"releases/{version}/update.json");
        var result = await TryFetchManifestAsync(manifestUrl, ct);
        return result.IsSuccess ? result.Value : null;
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

    /// <summary>应用 patch 到 base 安装包，生成新的安装包</summary>
    private async Task<Result<string>> ApplyPatchToInstallerAsync(
        string patchPath, UpdateInfo info, string outputDir, Action<double>? onProgress, CancellationToken ct)
    {
        var targetPath = Path.Combine(outputDir, $"NewCosmosSetup_{info.LatestVersion}.exe");
        if (File.Exists(targetPath) && VerifyFileHash(targetPath, info.PackageSha256))
        {
            onProgress?.Invoke(100);
            return Result.Success(targetPath);
        }

        // 获取或下载 base 安装包
        var basePath = await GetOrDownloadBaseInstallerAsync(info.PatchBaseVersion!, onProgress, ct);
        if (basePath.IsFailure)
            return Result.Failure<string>(ErrorCodes.NETWORK_ERROR, $"获取 base 安装包失败: {basePath.Message}");

        try
        {
            LogInfo($"应用 patch: {patchPath} -> {basePath.Value} => {targetPath}");
            onProgress?.Invoke(50); // base 就绪，开始 patch

            var tmpPath = targetPath + ".tmp";
            if (File.Exists(tmpPath)) File.Delete(tmpPath);

            // 调用 BsDiff BinaryPatch.Apply（baseStream, Func<Stream> patchFactory, newStream）
            var patchBytes = await File.ReadAllBytesAsync(patchPath, ct);
            using var baseStream = File.OpenRead(basePath.Value);
            using var newStream = File.Create(tmpPath);
            BinaryPatch.Apply(baseStream, () => new MemoryStream(patchBytes), newStream);

            onProgress?.Invoke(90);

            // 校验新安装包 SHA-256
            if (!VerifyFileHash(tmpPath, info.PackageSha256))
            {
                try { File.Delete(tmpPath); } catch { }
                return Result.Failure<string>(ErrorCodes.NETWORK_ERROR, "patch 生成的安装包校验失败（SHA-256 不匹配）");
            }

            if (File.Exists(targetPath)) File.Delete(targetPath);
            File.Move(tmpPath, targetPath);
            onProgress?.Invoke(100);
            LogInfo($"patch 应用成功，新安装包: {targetPath}");
            return Result.Success(targetPath);
        }
        catch (Exception ex)
        {
            try { File.Delete(targetPath + ".tmp"); } catch { }
            LogException(ex, "应用 patch");
            return Result.Failure<string>(ErrorCodes.NETWORK_ERROR, $"patch 应用异常: {ex.Message}");
        }
    }

    #endregion

    #region 安装与跳过

    public Result LaunchInstaller(string installerPath, string version)
    {
        return _packageInstaller.InstallPackage(installerPath, version);
    }

    public string? GetSkippedVersion()
    {
        try
        {
            if (!File.Exists(SkipFile)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(SkipFile));
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch (Exception ex)
        {
            LogWarn($"读取跳过更新版本失败(按未跳过处理): {ex.Message}");
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
