using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace NewCosmos.Services.Platform;

/// <summary>
/// 北斗终端序列页（T1–T7）启动器。
/// 页面随包内嵌（<c>Resources\Terminal\index.html</c>，csproj 显式 LogicalName），
/// 点页头右上角按钮时复制到本机缓存，再用系统默认浏览器打开；
/// 不依赖服务端、不引入 WebView 宿主，离线可用。
/// </summary>
public static class TerminalPageLauncher
{
    /// <summary>csproj 中 &lt;EmbeddedResource LogicalName&gt; 的唯一名字。</summary>
    private const string ResourceName = "NewCosmos.Terminal.index.html";

    private const string CachedFileName = "newcosmos-terminal.html";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _cachedPath;

    /// <summary>用系统浏览器打开终端序列页；失败只记日志，不打断当前业务。</summary>
    public static async Task OpenAsync()
    {
        try
        {
            var path = await EnsureLocalCopyAsync();
            await Launcher.Default.OpenAsync(new OpenFileRequest
            {
                File = new ReadOnlyFile(path),
                Title = "北斗终端序列"
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[Terminal] 打开北斗终端序列页失败");
        }
    }

    /// <summary>把内嵌页面落到本机缓存（进程内只写一次；缓存文件丢失时自动重建）。</summary>
    private static async Task<string> EnsureLocalCopyAsync()
    {
        if (_cachedPath is not null && File.Exists(_cachedPath))
            return _cachedPath;

        await Gate.WaitAsync();
        try
        {
            if (_cachedPath is not null && File.Exists(_cachedPath))
                return _cachedPath;

            await using var source = typeof(TerminalPageLauncher).Assembly
                .GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"未找到内嵌资源 {ResourceName}");

            var target = Path.Combine(FileSystem.CacheDirectory, CachedFileName);
            await using (var dest = File.Create(target))
            {
                await source.CopyToAsync(dest);
            }

            _cachedPath = target;
            return target;
        }
        finally
        {
            Gate.Release();
        }
    }
}
