using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NewCosmos.Constants;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Services.Templates;

/// <summary>
/// Office COM 应用类别
/// </summary>
internal enum OfficeAppKind
{
    Excel = 0,
    Word = 1
}

/// <summary>
/// Office COM 专用工作线程（R6 办公自动化层重构核心）。
///
/// 设计要点：
/// 1. 单一 STA 线程 + 工作队列：所有 Office COM 调用（启动应用、打开文档、打印、导出 PDF、退出）
///    都封送到这一条线程上顺序执行，实现全应用范围的 COM 串行化——Office 自动化在并行下极不稳定，
///    原实现每次操作从线程池（MTA）直接创建 COM 实例且互不排队。
/// 2. 应用实例池：每类应用（Excel/Word）复用同一个 COM 进程，空闲超过 <see cref="AppIdleTimeout"/>
///    后自动 Quit；原实现每次打印/导出都冷启动再退出一个 Office 进程。
/// 3. 进程跟踪：创建应用时捕获其 PID（Excel 经 Application.Hwnd + GetWindowThreadProcessId，
///    否则用创建前后进程快照差集；串行化后差集法不再有并发误判）。超时/退出时只终止被跟踪的 PID，
///    绝不无差别扫杀。KillStaleOfficeProcesses 仅在工作线程启动时执行一次作为最后兜底。
/// 4. 操作超时：默认 120 秒（PerformanceOptions 中无合适字段，见 DefaultOperationTimeout 注释）。
///    超时后终止被跟踪进程使卡死的 COM 调用立即失败，队列继续处理后续操作，向调用方抛出
///    BusinessException(COM_TIMEOUT)。
/// </summary>
internal sealed class OfficeComWorker
{
    // TODO(配置化): PerformanceOptions 现有字段（PrintTaskTimeoutMinutes 等）语义是"整个打印任务"
    // 而非"单次 COM 调用"，且该类 Validate() 强制所有字段必须在配置文件中出现，新增字段会要求
    // 同步更新部署机配置。故此处按设计约定采用 120 秒常量；后续若在 PerformanceOptions 增加
    // OfficeComOperationTimeoutSeconds 字段，可在 AttachLogger 处一并注入。
    internal static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(120);

    /// <summary>应用空闲多久后自动退出（释放 Office 进程）</summary>
    private static readonly TimeSpan AppIdleTimeout = TimeSpan.FromMinutes(5);

    /// <summary>队列空轮询间隔（兼作空闲清扫节拍）</summary>
    private static readonly TimeSpan QueuePollInterval = TimeSpan.FromSeconds(30);

    private static readonly Lazy<OfficeComWorker> LazyInstance =
        new(() => new OfficeComWorker(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>全局单例（static Lazy，避免 DI 注册变更）</summary>
    public static OfficeComWorker Instance => LazyInstance.Value;

    private static ILoggerService? _log;

    /// <summary>由引擎构造函数附加日志服务（幂等；worker 是无 DI 的静态单例）</summary>
    internal static void AttachLogger(ILoggerService logger) => _log ??= logger;

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>工作项状态：0=排队 1=执行中 2=完成 3=已放弃（排队期超时）</summary>
    private sealed class WorkItem
    {
        public WorkItem(string name, OfficeAppKind kind, Func<OperationContext, object?> body)
        {
            Name = name;
            Kind = kind;
            Body = body;
            Tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public string Name { get; }
        public OfficeAppKind Kind { get; }
        public Func<OperationContext, object?> Body { get; }
        public TaskCompletionSource<object?> Tcs { get; }
        public int State;
    }

    /// <summary>池中托管的 Office 应用（所有字段仅在工作线程上访问）</summary>
    private sealed class HostedApp
    {
        public dynamic App = null!;
        public string ProgId = string.Empty;
        public int? ProcessId;
        public int OpenDocuments;
        public DateTime LastUsedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// 操作上下文：传给操作体（在工作线程上执行）。
    /// App 按需获取（Dispose 清理等场景不应为关闭文档而凭空启动应用）；
    /// DocumentOpened/DocumentClosed 维护打开文档计数，空闲清扫不会退出仍持有文档的应用。
    /// </summary>
    internal sealed class OperationContext
    {
        private readonly OfficeComWorker _worker;
        private readonly OfficeAppKind _kind;

        internal OperationContext(OfficeComWorker worker, OfficeAppKind kind)
        {
            _worker = worker;
            _kind = kind;
        }

        /// <summary>获取（必要时创建）当前类别的 Office 应用。仅可在工作线程上访问。</summary>
        public dynamic App => _worker.AcquireApp(_kind);

        public void DocumentOpened() => _worker.AdjustLease(_kind, +1);

        public void DocumentClosed() => _worker.AdjustLease(_kind, -1);
    }

    private readonly BlockingCollection<WorkItem> _queue = new();
    private readonly Thread _thread;
    private readonly HostedApp?[] _hosted = new HostedApp?[2];
    private readonly object _pidLock = new();
    private readonly int?[] _trackedPids = new int?[2];
    private int _shutdown;

    private OfficeComWorker()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "OfficeComWorker"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
    }

    #region 对外调用入口

    /// <summary>
    /// 异步执行一个 COM 操作（封送到 STA 工作线程，全局串行）。
    /// 超时（默认 120s）后：若操作正在执行则终止被跟踪的 Office 进程使其解卡；
    /// 若仍在排队则标记放弃。两种情况均抛 BusinessException(COM_TIMEOUT)。
    /// 取消令牌仅在入队前检查（与原实现一致：COM 调用一旦开始不可中途取消）。
    /// </summary>
    public Task<T> InvokeAsync<T>(
        string operationName,
        OfficeAppKind kind,
        Func<OperationContext, T> body,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // 已在工作线程上（操作体内部再次调用）→ 直接内联执行，避免自死锁
        if (Thread.CurrentThread.ManagedThreadId == _thread.ManagedThreadId)
        {
            var inlineCtx = new OperationContext(this, kind);
            try
            {
                return Task.FromResult(body(inlineCtx));
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        var item = new WorkItem(operationName, kind, ctx => body(ctx));
        try
        {
            _queue.Add(item);
        }
        catch (InvalidOperationException)
        {
            throw new BusinessException(ErrorCodes.COM_INSTANCE_FAILED,
                $"应用正在退出，无法执行 Office 操作：{operationName}");
        }

        return WaitForResultAsync<T>(item, timeout ?? DefaultOperationTimeout);
    }

    /// <summary>同步执行（供 ITemplateEngine 的同步方法使用，阻塞调用线程直至完成/超时）</summary>
    public T Invoke<T>(
        string operationName,
        OfficeAppKind kind,
        Func<OperationContext, T> body,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
        => InvokeAsync(operationName, kind, body, timeout, ct).GetAwaiter().GetResult();

    private async Task<T> WaitForResultAsync<T>(WorkItem item, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(item.Tcs.Task, Task.Delay(timeout)).ConfigureAwait(false);
        if (completed == item.Tcs.Task)
        {
            return (T)(await item.Tcs.Task.ConfigureAwait(false))!;
        }

        // 超时。尝试把"排队中"原子地改为"已放弃"；失败说明已开始执行或恰好完成。
        var observed = Interlocked.CompareExchange(ref item.State, 3, 0);
        if (observed == 2 || item.Tcs.Task.IsCompleted)
        {
            // 恰在超时瞬间完成 → 直接取结果
            return (T)(await item.Tcs.Task.ConfigureAwait(false))!;
        }

        if (observed == 1)
        {
            // 正在执行中卡死 → 终止被跟踪进程，使工作线程上的 COM 调用立即失败并继续处理队列
            KillTrackedProcess(item.Kind, $"操作超时（{timeout.TotalSeconds:F0} 秒）: {item.Name}");
            throw new BusinessException(ErrorCodes.COM_TIMEOUT,
                $"Office COM 操作超时（{timeout.TotalSeconds:F0} 秒）：{item.Name}。已强制终止对应的 Office 进程，请重试；若频繁出现请检查 Office 安装与文档大小。");
        }

        // 仍在排队（前序操作占用队列过久）
        throw new BusinessException(ErrorCodes.COM_TIMEOUT,
            $"Office COM 排队等待超时（{timeout.TotalSeconds:F0} 秒）：{item.Name}。前序 Office 操作耗时过长，请稍后重试。");
    }

    /// <summary>
    /// 终止指定类别当前被跟踪的 Office 进程（任意线程可调用；只杀本 worker 启动的 PID）。
    /// </summary>
    public void KillTrackedProcess(OfficeAppKind kind, string reason)
    {
        int? pid;
        lock (_pidLock)
        {
            pid = _trackedPids[(int)kind];
        }

        if (pid.HasValue)
        {
            _log?.Warn($"[OfficeComWorker] 强制终止 {kind} 进程 PID={pid.Value}：{reason}");
            OfficeProviderDetector.KillProcessById(pid.Value);
        }
        else
        {
            _log?.Warn($"[OfficeComWorker] 需要终止 {kind} 进程但无被跟踪 PID（可能由外部程序托管）：{reason}");
        }
    }

    /// <summary>应用退出时调用：停止接收新操作，等待队列排空并退出所有托管应用；卡死则终止被跟踪进程。</summary>
    public void Shutdown()
    {
        if (Interlocked.Exchange(ref _shutdown, 1) == 1) return;

        try { _queue.CompleteAdding(); }
        catch (Exception ex) { Debug.WriteLine($"[OfficeComWorker] CompleteAdding 失败: {ex.Message}"); }

        if (!_thread.Join(TimeSpan.FromSeconds(5)))
        {
            // 工作线程卡死在某个 COM 调用上：终止被跟踪进程（仅限本 worker 启动的）
            KillTrackedProcess(OfficeAppKind.Excel, "应用退出，工作线程未能及时结束");
            KillTrackedProcess(OfficeAppKind.Word, "应用退出，工作线程未能及时结束");
        }
    }

    #endregion

    #region 工作线程主循环

    private void Run()
    {
        // 兜底清扫仅在启动时执行一次（原实现每次打印/导出前都全表扫描一遍进程）。
        // 仅清理"无主窗口且存活超 30 分钟"的残留，绝不误杀用户自己打开的文档。
        try
        {
            var killed = OfficeProviderDetector.KillStaleOfficeProcesses(TimeSpan.FromMinutes(30));
            if (killed > 0)
                _log?.Info($"[OfficeComWorker] 启动兜底清扫：终止 {killed} 个残留 Office 进程");
        }
        catch (Exception ex)
        {
            _log?.Warn($"[OfficeComWorker] 启动兜底清扫失败: {ex.Message}");
        }

        while (true)
        {
            WorkItem? item = null;
            try
            {
                if (!_queue.TryTake(out item, QueuePollInterval))
                {
                    if (_queue.IsCompleted) break;
                    SweepIdleApps();
                    continue;
                }
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (item == null) continue;

            // 排队期已被调用方放弃（排队超时）→ 跳过
            if (Interlocked.CompareExchange(ref item.State, 1, 0) != 0)
            {
                item.Tcs.TrySetException(new BusinessException(ErrorCodes.COM_TIMEOUT,
                    $"操作在排队期已超时放弃：{item.Name}"));
                continue;
            }

            var ctx = new OperationContext(this, item.Kind);
            try
            {
                var result = item.Body(ctx);
                item.Tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                HandleOperationFailure(item.Kind);
                item.Tcs.TrySetException(ex);
            }
            finally
            {
                Interlocked.Exchange(ref item.State, 2);
                TouchApp(item.Kind);
            }
        }

        ShutdownAppsOnWorker();
    }

    /// <summary>操作失败后探测应用是否已死（进程被杀/崩溃），死则清理，下次操作自动重建。</summary>
    private void HandleOperationFailure(OfficeAppKind kind)
    {
        var hosted = _hosted[(int)kind];
        if (hosted == null) return;

        if (!IsAppAlive(hosted))
        {
            _log?.Warn($"[OfficeComWorker] {kind} 进程在操作中失效（超时被终止或自行崩溃），清理并将在下次操作时重建");
            CleanupHostedApp(kind, hosted, graceful: false);
        }
    }

    private void SweepIdleApps()
    {
        var now = DateTime.UtcNow;
        foreach (OfficeAppKind kind in new[] { OfficeAppKind.Excel, OfficeAppKind.Word })
        {
            var hosted = _hosted[(int)kind];
            if (hosted == null) continue;
            if (hosted.OpenDocuments > 0) continue;
            if (now - hosted.LastUsedUtc < AppIdleTimeout) continue;

            _log?.Info($"[OfficeComWorker] {kind} 应用空闲超过 {AppIdleTimeout.TotalMinutes:F0} 分钟，退出（PID={hosted.ProcessId?.ToString() ?? "未知"}）");
            CleanupHostedApp(kind, hosted, graceful: true);
        }
    }

    private void ShutdownAppsOnWorker()
    {
        foreach (OfficeAppKind kind in new[] { OfficeAppKind.Excel, OfficeAppKind.Word })
        {
            var hosted = _hosted[(int)kind];
            if (hosted == null) continue;
            if (hosted.OpenDocuments > 0)
                _log?.Warn($"[OfficeComWorker] 退出时 {kind} 仍有 {hosted.OpenDocuments} 个未关闭文档，随应用一并关闭");
            CleanupHostedApp(kind, hosted, graceful: true);
        }
    }

    #endregion

    #region 应用池（仅工作线程访问）

    private dynamic AcquireApp(OfficeAppKind kind)
    {
        var idx = (int)kind;
        var hosted = _hosted[idx];

        if (hosted != null && !IsAppAlive(hosted))
        {
            _log?.Warn($"[OfficeComWorker] 检测到 {kind} 进程已失效，清理并重建");
            CleanupHostedApp(kind, hosted, graceful: false);
            hosted = null;
        }

        if (hosted == null)
        {
            hosted = CreateHostedApp(kind);
            _hosted[idx] = hosted;
        }

        hosted.LastUsedUtc = DateTime.UtcNow;
        return hosted.App;
    }

    private void AdjustLease(OfficeAppKind kind, int delta)
    {
        var hosted = _hosted[(int)kind];
        if (hosted == null) return;
        hosted.OpenDocuments = Math.Max(0, hosted.OpenDocuments + delta);
        hosted.LastUsedUtc = DateTime.UtcNow;
    }

    private void TouchApp(OfficeAppKind kind)
    {
        var hosted = _hosted[(int)kind];
        if (hosted != null) hosted.LastUsedUtc = DateTime.UtcNow;
    }

    private static bool IsAppAlive(HostedApp hosted)
    {
        try
        {
            // 廉价的跨进程 COM 属性访问：进程死亡/RPC 断开会抛异常
            object _ = hosted.App.Version;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private HostedApp CreateHostedApp(OfficeAppKind kind)
    {
        string progId;
        try
        {
            progId = kind == OfficeAppKind.Excel
                ? OfficeProviderDetector.GetExcelProgId()
                : OfficeProviderDetector.GetWordProgId();
        }
        catch (InvalidOperationException ex)
        {
            throw new BusinessException(ErrorCodes.OFFICE_NOT_INSTALLED, ex.Message, ex);
        }

        var type = Type.GetTypeFromProgID(progId)
            ?? throw new BusinessException(ErrorCodes.COM_INSTANCE_FAILED, $"无法解析 Office COM 类型：{progId}");

        var before = SnapshotProcessIds(kind);

        dynamic app;
        try
        {
            app = Activator.CreateInstance(type)
                ?? throw new BusinessException(ErrorCodes.COM_INSTANCE_FAILED, $"启动 Office 失败（{progId}）：CreateInstance 返回空");
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BusinessException(ErrorCodes.COM_INSTANCE_FAILED, $"启动 Office 失败（{progId}）：{ex.Message}", ex);
        }

        try
        {
            app.Visible = false;
            app.DisplayAlerts = 0;
        }
        catch (Exception ex)
        {
            _log?.Debug($"[OfficeComWorker] 设置 {kind} 静默模式失败（可忽略）: {ex.Message}");
        }

        // PID 捕获：Excel 优先经 Application.Hwnd（最精确）；否则用创建前后快照差集。
        // 串行化保证同一时刻只有本操作在创建 Office 进程，差集法不再有并发误判；
        // 若差集不唯一（外部恰好也启动了 Office）则放弃跟踪——宁可不杀，绝不误杀。
        var pid = TryGetProcessIdViaHwnd(app);
        if (pid == null)
        {
            var after = SnapshotProcessIds(kind);
            after.ExceptWith(before);
            if (after.Count == 1)
                pid = after.First();
            else if (after.Count > 1)
                _log?.Warn($"[OfficeComWorker] 检测到多个新 {kind} 进程（{after.Count} 个），放弃 PID 跟踪以避免误杀");
        }

        lock (_pidLock)
        {
            _trackedPids[(int)kind] = pid;
        }

        _log?.Info($"[OfficeComWorker] 已启动 {kind} 应用：ProgID={progId}, PID={pid?.ToString() ?? "未知"}");

        return new HostedApp
        {
            App = app,
            ProgId = progId,
            ProcessId = pid,
            OpenDocuments = 0,
            LastUsedUtc = DateTime.UtcNow
        };
    }

    /// <summary>退出并释放托管应用；graceful=false 表示进程已死/需强杀，跳过 Quit 等待。</summary>
    private void CleanupHostedApp(OfficeAppKind kind, HostedApp hosted, bool graceful)
    {
        var idx = (int)kind;

        if (graceful)
        {
            try { hosted.App.Quit(); }
            catch (Exception ex) { _log?.Debug($"[OfficeComWorker] {kind} Quit 失败（可能已退出）: {ex.Message}"); }
        }

        try { Marshal.ReleaseComObject(hosted.App); }
        catch (Exception ex) { _log?.Debug($"[OfficeComWorker] 释放 {kind} RCW 失败: {ex.Message}"); }

        _hosted[idx] = null;

        // 确定性等待退出 + 只杀被跟踪 PID（取代原先无条件延迟 2 秒的 fire-and-forget Kill）
        if (hosted.ProcessId.HasValue)
        {
            var pid = hosted.ProcessId.Value;
            try
            {
                using var proc = Process.GetProcessById(pid);
                var waitMs = graceful ? 3000 : 0;
                if (!proc.WaitForExit(waitMs))
                {
                    _log?.Warn($"[OfficeComWorker] {kind} 进程 PID={pid} 未随 Quit 退出，强制终止");
                    proc.Kill();
                }
            }
            catch (ArgumentException)
            {
                // 进程已退出——正常
            }
            catch (Exception ex)
            {
                _log?.Warn($"[OfficeComWorker] 清理 {kind} 进程 PID={pid} 失败: {ex.Message}");
            }
        }

        lock (_pidLock)
        {
            _trackedPids[idx] = null;
        }
    }

    private static HashSet<int> SnapshotProcessIds(OfficeAppKind kind)
    {
        var names = kind == OfficeAppKind.Excel
            ? new[] { "EXCEL", "ET", "KET" }
            : new[] { "WINWORD", "wps", "kwps", "wpsoffice" };

        var ids = new HashSet<int>();
        foreach (var name in names)
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName(name))
                {
                    ids.Add(proc.Id);
                    proc.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OfficeComWorker] 枚举进程失败（预期）: {name} - {ex.Message}");
            }
        }
        return ids;
    }

    private static int? TryGetProcessIdViaHwnd(dynamic app)
    {
        try
        {
            // Excel/WPS 表格的 Application.Hwnd；Word 无此属性 → 异常返回 null 走快照差集
            long hwnd = Convert.ToInt64(app.Hwnd);
            if (hwnd != 0)
            {
                GetWindowThreadProcessId(new IntPtr(hwnd), out var pid);
                if (pid != 0) return (int)pid;
            }
        }
        catch
        {
            // Word 或不支持 Hwnd 的提供方——正常回退
        }
        return null;
    }

    #endregion
}

/// <summary>
/// Excel/Word 引擎共用的 COM 文档操作（打开→操作→关闭，全部在 OfficeComWorker 线程上执行）。
/// 原先两个引擎各自复制了一份打印与 PDF 导出逻辑（约 250 行），此处合并为唯一实现。
/// </summary>
internal static class OfficeComDocuments
{
    /// <summary>
    /// 打印文件（Excel 工作簿或 Word 文档）。
    /// 诚实错误语义：打印命令两次失败 → 抛 BusinessException(PRINT_FAILED)（原 Excel 实现静默吞掉，
    /// 调用方误记"打印成功"）；设置指定打印机失败 → 降级到系统默认打印机并以 Warn 级别显式记录。
    /// </summary>
    public static async Task PrintFileAsync(
        OfficeAppKind kind,
        string filePath,
        string printerName,
        bool isDuplex,
        int copies,
        ILoggerService logger,
        CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(filePath);
        var fileName = Path.GetFileName(fullPath);

        await OfficeComWorker.Instance.InvokeAsync<object?>($"打印 {fileName}", kind, ctx =>
        {
            var app = ctx.App;
            dynamic? doc = null;
            try
            {
                doc = OpenDocument(ctx, kind, app, fullPath, "打印");

                if (!string.IsNullOrEmpty(printerName))
                {
                    try
                    {
                        app.ActivePrinter = printerName;
                    }
                    catch (Exception ex)
                    {
                        logger.Warn($"设置打印机 [{printerName}] 失败（已降级：使用系统默认打印机）: {ex.Message}");
                    }

                    try
                    {
                        PrinterDuplexHelper.SetPrinterDuplex(printerName, isDuplex);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn($"设置双面打印失败（已降级：使用打印机当前双面设置）: {ex.Message}");
                    }
                }

                PrintOutWithRetry(kind, doc!, copies, logger);
                return null;
            }
            finally
            {
                CloseDocumentQuietly(ctx, ref doc, logger);
            }
        }, ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 将源文件（xlsx/docx）导出为 PDF 并读回字节。输出文件保留在 pdfPath（删除与否由调用方决定）。
    /// </summary>
    public static async Task<byte[]> ExportToPdfAsync(
        OfficeAppKind kind,
        string sourceFilePath,
        string pdfPath,
        ILoggerService logger,
        CancellationToken ct = default)
    {
        var fullSource = Path.GetFullPath(sourceFilePath);
        var fullPdf = Path.GetFullPath(pdfPath);
        var fileName = Path.GetFileName(fullSource);

        await OfficeComWorker.Instance.InvokeAsync<object?>($"导出 PDF {fileName}", kind, ctx =>
        {
            var app = ctx.App;
            dynamic? doc = null;
            try
            {
                doc = OpenDocument(ctx, kind, app, fullSource, "PDF 导出");

                try
                {
                    if (kind == OfficeAppKind.Excel)
                        doc!.ExportAsFixedFormat(0, fullPdf);      // 0 = xlTypePDF
                    else
                        doc!.ExportAsFixedFormat(fullPdf, 17);     // 17 = wdExportFormatPDF
                }
                catch (Exception ex) when (ex is not BusinessException)
                {
                    throw new BusinessException(ErrorCodes.DOCUMENT_PDF_CONVERSION_FAILED,
                        $"PDF 导出失败：Office 转换出错（{ex.Message}）。文件：{fileName}", ex);
                }
                return null;
            }
            finally
            {
                CloseDocumentQuietly(ctx, ref doc, logger);
            }
        }, ct: ct).ConfigureAwait(false);

        if (!File.Exists(fullPdf))
            throw new BusinessException(ErrorCodes.DOCUMENT_PDF_CONVERSION_FAILED,
                $"PDF 导出失败：Office 已执行但未生成输出文件。文件：{fileName}");

        return await File.ReadAllBytesAsync(fullPdf, ct).ConfigureAwait(false);
    }

    private static dynamic OpenDocument(
        OfficeComWorker.OperationContext ctx,
        OfficeAppKind kind,
        dynamic app,
        string fullPath,
        string purpose)
    {
        dynamic doc;
        try
        {
            doc = kind == OfficeAppKind.Excel
                ? app.Workbooks.Open(fullPath)
                : app.Documents.Open(fullPath);
        }
        catch (Exception ex) when (ex is not BusinessException)
        {
            throw new BusinessException(ErrorCodes.PRINT_FAILED,
                $"{purpose}失败：无法打开文档（{ex.Message}）。文件：{Path.GetFileName(fullPath)}", ex);
        }
        ctx.DocumentOpened();
        return doc;
    }

    private static void PrintOutWithRetry(OfficeAppKind kind, dynamic doc, int copies, ILoggerService logger)
    {
        try
        {
            if (kind == OfficeAppKind.Word)
            {
                // Background:=false 同步打印：应用实例池化后 Quit 时机不再紧随打印，
                // 但超时强杀仍可能命中后台假脱机中的实例——同步打印保证任务已提交给系统打印队列
                if (copies > 1)
                    doc.PrintOut(Background: false, Copies: copies);
                else
                    doc.PrintOut(Background: false);
            }
            else
            {
                if (copies > 1)
                    doc.PrintOut(Copies: copies);
                else
                    doc.PrintOut();
            }
        }
        catch (Exception ex1)
        {
            logger.Warn($"首次打印调用失败，降级为无参重试一次: {ex1.Message}");
            try
            {
                doc.PrintOut();
            }
            catch (Exception ex2)
            {
                // 原 Excel 实现此处仅 Debug 日志后返回成功——打印被静默跳过。现改为诚实报错。
                throw new BusinessException(ErrorCodes.PRINT_FAILED,
                    $"打印失败：Office 打印命令两次均未成功（{ex2.Message}）。请检查打印机连接与驱动后重试。", ex2);
            }
        }
    }

    private static void CloseDocumentQuietly(
        OfficeComWorker.OperationContext ctx,
        ref dynamic? doc,
        ILoggerService logger)
    {
        if (doc == null) return;

        try { doc.Close(false); }
        catch (Exception ex) { logger.Debug($"关闭文档失败（预期）: {ex.Message}"); }

        try { Marshal.ReleaseComObject(doc); }
        catch (Exception ex) { logger.Debug($"释放文档 COM 对象失败（预期）: {ex.Message}"); }

        doc = null;
        ctx.DocumentClosed();
    }
}

/// <summary>
/// 模板引擎共用的文件/分块小工具（原先在 ExcelEngine 与 WordEngine 中各有一份拷贝）。
/// </summary>
internal static class TemplateEngineHelpers
{
    /// <summary>在应用临时目录下生成唯一文件路径（自动创建目录）。subDirectory 为 null 时用临时根目录。</summary>
    public static string NewTempFilePath(StorageOptions storageOptions, string extension, string? subDirectory = "PrintTemp", string prefix = "temp_")
    {
        var dir = storageOptions.GetTempPath(subDirectory);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{prefix}{Guid.NewGuid()}{extension}");
    }

    public static void TryDeleteFile(string path, ILoggerService? logger = null, string tag = "TemplateEngine")
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            logger?.Warn($"[{tag}] 删除临时文件失败: {path}, 错误: {ex.Message}");
        }
    }

    public static List<List<T>> ChunkList<T>(List<T> source, int pageSize)
    {
        var chunks = new List<List<T>>();
        for (int i = 0; i < source.Count; i += pageSize)
        {
            chunks.Add(source.Skip(i).Take(pageSize).ToList());
        }
        return chunks;
    }
}
