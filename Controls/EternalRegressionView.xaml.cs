using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Graphics;
using NewCosmos.Constants;

namespace NewCosmos.Controls;

/// <summary>
/// 「翁法罗斯 · 永劫回归 · 数据删除特效」全屏半透明背景层组件。
/// 固定速度删除：每人 5 分钟；13 人全部删除完毕为一轮，每轮计数器 -1（自 33,550,336 递减）。
/// 每人独立删除进度条；内容布局在右侧，避开左侧登录窗体。
/// 进度持久化到用户数据目录，重启后续跑；页面卸载自动停表并写盘。
/// </summary>
public partial class EternalRegressionView : ContentView
{
    private static readonly Color LogInfo = Color.FromArgb("#A8E8F0");
    private static readonly Color LogGold = Color.FromArgb("#FFD970");
    private static readonly Color LogWarn = Color.FromArgb("#FFA070");
    private static readonly Color LogError = Color.FromArgb("#FF9090");

    private readonly EternalRegressionDrawable _drawable = new();
    private readonly ObservableCollection<EternalHeirRow> _heirs = new();
    private readonly ObservableCollection<EternalTitanRow> _titans = new();
    private readonly ObservableCollection<EternalLogLine> _logs = new();

    private readonly Random _random = new();
    private IDispatcherTimer? _timer;
    private CancellationTokenSource? _startCts;

    /// <summary>总已删除人数（跨轮回累积，持久化键值）</summary>
    private long _totalDeleted;

    /// <summary>当前正删除角色开始时刻</summary>
    private DateTime _currentStartUtc;

    /// <summary>已回收泰坦数（随已删人数累计，不回退；全黑后复活会递减）</summary>
    private int _lastRecalled;

    /// <summary>泰坦复活阶段：复活间隔帧计数 / 当前间隔 / 保留的黑色火种数下限</summary>
    private int _reviveTick;
    private int _reviveInterval = 25;
    private int _reviveFloor;

    /// <summary>聚焦层左侧快速清除：文件名更换帧计数 / 当前间隔 / 序号</summary>
    private int _quickTick;
    private int _quickInterval = 4;
    private int _quickFileSeq;

    /// <summary>聚焦层左侧快速清除：文件名模板池（{0}=电信号，{1}=序号）</summary>
    private static readonly string[] QuickFileTemplates =
    {
        "删除_{0}_{1:D2}.sig",
        "purge_{0}_{1:D2}.bin",
        "archive_{0}_{1:D2}.dat",
        "cleansing_{0}_{1:D2}.tmp",
        "scrap_{0}_{1:D2}.sector"
    };

    private bool _ended;
    private int _tickCount;
    private int _titanFlashTick;
    private DateTime _lastPersistUtc;

    /// <summary>每删除一个人员时触发（LoginPage 订阅后切换背景图）</summary>
    public event EventHandler? PersonDeleted;

    public EternalRegressionView()
    {
        InitializeComponent();
        MatrixView.Drawable = _drawable;
        HeirList.ItemsSource = _heirs;
        TitanList.ItemsSource = _titans;
        LogList.ItemsSource = _logs;
        BuildRows();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void BuildRows()
    {
        foreach (var h in EternalRegressionData.Heirs)
            _heirs.Add(new EternalHeirRow(h));
        foreach (var t in EternalRegressionData.Titans)
            _titans.Add(new EternalTitanRow(t));
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _startCts?.Cancel();
        _startCts = new CancellationTokenSource();
        _ = StartAfterDelayAsync(_startCts.Token);
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _startCts?.Cancel();
        _timer?.Stop();
        Persist();
    }

    private async Task StartAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(EternalRegressionData.StartDelayMs, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (ct.IsCancellationRequested) return;
        Dispatcher.Dispatch(BeginSimulation);
    }

    /// <summary>剩余轮回计数 = 初始 − 已完成轮数</summary>
    private long CycleRemaining => EternalRegressionData.InitialCycle - (_totalDeleted / EternalRegressionData.HeirsPerCycle);

    /// <summary>当前轮内已删除人数（0..12）</summary>
    private int CurrentIndex => (int)(_totalDeleted % EternalRegressionData.HeirsPerCycle);

    /// <summary>当前正删除角色的进度（0-1）</summary>
    private double CurrentProgress
    {
        get
        {
            var elapsed = (DateTime.UtcNow - _currentStartUtc).TotalSeconds;
            return Math.Clamp(elapsed / EternalRegressionData.PersonWindowSeconds, 0d, 1d);
        }
    }

    private void BeginSimulation()
    {
        LoadProgress();
        if (_currentStartUtc == default)
            _currentStartUtc = DateTime.UtcNow;
        _lastPersistUtc = DateTime.UtcNow;
        _tickCount = 0;

        ResetAllRows();

        if (_ended || CycleRemaining <= 0)
        {
            _ended = true;
            CompleteDeletions();
            ShowEnding();
            return;
        }

        // 恢复：已删除/已回收视觉状态（不重放历史日志）
        for (var i = 0; i < CurrentIndex; i++)
            MarkDeleted(_heirs[i]);
        RestoreTitans();

        _logs.Clear();
        AppendLog("▸ 翁法罗斯永劫回归协议启动", LogInfo);
        AppendLog($"▸ 循环基线 {EternalRegressionData.InitialCycle:N0} · 每轮 {EternalRegressionData.HeirsPerCycle} 人 · 每人 {EternalRegressionData.PersonWindowSeconds}s", LogInfo);
        AppendLog("▸ 数据源：Chrysos Heirs / Titan Core 已连接", LogInfo);

        UpdateFrames(CurrentProgress);

        _timer?.Stop();
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(EternalRegressionData.FrameMs);
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _tickCount++;
        _drawable.Advance();
        MatrixView.Invalidate();

        if (_ended)
            return;

        // 当前人删除完成 → 推进：标记 DELETED、跨人/跨轮、重置开始时刻
        if (CurrentProgress >= 1d)
        {
            var done = CurrentIndex;
            var row = _heirs[done];
            MarkDeleted(row);
            AppendLog($"[{row.Signal}] {row.Name} · {row.FullName} 档案已删除", LogWarn);
            AppendLog($"  >> {row.RandomLine(_random)}", LogGold);

            _totalDeleted++;
            _currentStartUtc = DateTime.UtcNow;

            // 每删除一人 → 通知宿主切换背景图
            PersonDeleted?.Invoke(this, EventArgs.Empty);

            // 一轮 13 人删除完毕 → 计数器 -1
            if (_totalDeleted % EternalRegressionData.HeirsPerCycle == 0)
            {
                var cycleLog = string.Format(
                    EternalRegressionData.CycleCompleteLogs[_random.Next(EternalRegressionData.CycleCompleteLogs.Length)],
                    CycleRemaining);
                AppendLog(cycleLog, LogInfo);
            }

            if (CycleRemaining <= 0)
            {
                _ended = true;
                CompleteDeletions();
                ShowEnding();
                return;
            }
        }

        UpdateFrames(CurrentProgress);

        // 系统日志高频刷新
        if (_tickCount % EternalRegressionData.LogEveryTicks == 0)
            AppendWorldLog();

        // 定期持久化
        if ((DateTime.UtcNow - _lastPersistUtc).TotalSeconds >= EternalRegressionData.PersistEverySeconds)
            Persist();
    }

    /// <summary>刷新计数器 / 总进度 / 每人独立进度条 / 泰坦回收 / 聚焦层 / 故障强度</summary>
    private void UpdateFrames(double local)
    {
        CounterLabel.Text = CycleRemaining.ToString("N0", CultureInfo.InvariantCulture);

        var cur = CurrentIndex;
        var cycleProgress = (cur + local) / EternalRegressionData.HeirsPerCycle;
        TotalProgress.Progress = cycleProgress;
        TotalProgressLabel.Text = $"{(int)(cycleProgress * 100):D2}%";
        HeirPanelSub.Text = $"{cur}/{EternalRegressionData.HeirsPerCycle}";
        _drawable.NoiseLevel = (float)cycleProgress;

        // 每人独立进度条：已删=满，当前=局部进度，未到=0
        for (var i = 0; i < _heirs.Count; i++)
        {
            if (i < cur)
                _heirs[i].Progress = 1d;
            else if (i == cur)
                _heirs[i].Progress = local;
            else
                _heirs[i].Progress = 0d;
        }

        // 泰坦随已删人数累计回收（不回退）
        var targetRecalled = Math.Min(EternalRegressionData.Titans.Length, (int)Math.Min(int.MaxValue, _totalDeleted));
        if (_lastRecalled < targetRecalled)
        {
            // 未全黑：正常逐项回收
            while (_lastRecalled < targetRecalled)
            {
                MarkRecalled(_titans[_lastRecalled], log: true);
                _lastRecalled++;
            }
        }
        else if (_lastRecalled >= _titans.Count && !_ended)
        {
            // 全黑 → 持续循环复活阶段：随机点亮部分火种，黑↔亮在保留下限附近流动
            if (_reviveFloor <= 0)
            {
                _reviveFloor = _random.Next(4, 9);       // 保留 4~8 个黑色（回收态）
                _reviveInterval = _random.Next(15, 36);  // 复活间隔 15~35 帧
                _reviveTick = 0;
            }

            _reviveTick++;
            if (_reviveTick >= _reviveInterval)
            {
                _reviveTick = 0;
                _reviveInterval = _random.Next(15, 36);

                if (_lastRecalled > _reviveFloor)
                {
                    // 黑色过多 → 随机复活 1 个已回收火种
                    var recall = _titans.Where(t => t.IsRecalled).ToList();
                    if (recall.Count > 0)
                    {
                        var row = recall[_random.Next(recall.Count)];
                        ReviveTitan(row, log: true);
                        _lastRecalled--;
                    }
                }
                else
                {
                    // 达到下限 → 随机回收 1 个已复活火种（继续循环，黑↔亮流动）
                    var revived = _titans.Where(t => !t.IsRecalled).ToList();
                    if (revived.Count > 0)
                    {
                        var row = revived[_random.Next(revived.Count)];
                        MarkRecalled(row, log: true);
                        _lastRecalled++;
                    }
                }
            }
        }

        // 泰坦逐项闪烁：未回收项轮流高亮
        _titanFlashTick++;
        var flash = (_titanFlashTick / 6) % _titans.Count;
        for (var i = 0; i < _titans.Count; i++)
        {
            if (_titans[i].IsRecalled) continue;
            _titans[i].StatusColor = i == flash ? EternalTitanRow.BrightColor : EternalTitanRow.NormalColor;
        }
        TitanPanelSub.Text = $"{_lastRecalled}/{_titans.Count}";

        // 聚焦层：当前删除角色特写（中间偏下；左右各一进度条）
        if (cur < _heirs.Count)
        {
            var row = _heirs[cur];
            FocusSignal.Text = row.Signal;
            FocusName.Text = row.Name;
            FocusFullName.Text = row.FullName;
            FocusLine.Text = "“" + row.Lines[0] + "”";
            FocusSub.Text = $"DELETING · {row.Signal} · {(int)(local * 100):D2}%";

            // 左栏：快速清除（随机文件名，0.3~0.6 秒/文件，含当前人电信号）
            _quickTick++;
            if (_quickTick >= _quickInterval)
            {
                _quickTick = 0;
                _quickInterval = _random.Next(3, 7);          // 3~6 帧（FrameMs=100 → 0.3~0.6s）
                _quickFileSeq = _random.Next(1, 100);
                var template = QuickFileTemplates[_random.Next(QuickFileTemplates.Length)];
                QuickFileName.Text = string.Format(template, row.Signal, _quickFileSeq);
            }
            // 快速进度：文件周期内 0→1（随 tick 快速推进）
            QuickProgress.Progress = _quickTick / (double)Math.Max(1, _quickInterval);
            QuickSub.Text = $"PURGING · {row.Signal} · 文件队列";

            // 右栏：目标进度（当前人单独进度，符合时间轴；100% 由 OnTick 自动跳转下一人）
            TargetPath.Text = $"C:\\永劫回归\\轮回#{CycleRemaining:N0}\\第{cur + 1}/{EternalRegressionData.HeirsPerCycle}人\\{row.Signal}\\{row.Name}档案_删除中";
            TargetProgress.Progress = local;
            TargetSub.Text = $"{row.Signal} · {(int)(local * 100):D2}% / {EternalRegressionData.PersonWindowSeconds}s 时间轴";

            FocusLayer.IsVisible = true;
        }
    }

    private void MarkDeleted(EternalHeirRow row)
    {
        row.IsDeleted = true;
        row.SignalColor = Color.FromArgb("#FF7070");
        row.NameColor = Color.FromArgb("#FF9090");
        row.ProgressColor = Color.FromArgb("#8A2F2F");
        row.Progress = 1d;
        row.StatusText = "DELETED";
    }

    private void MarkRecalled(EternalTitanRow row, bool log = false)
    {
        row.IsRecalled = true;
        row.StatusColor = Color.FromArgb("#6A7070");
        row.StatusText = "RECALLED";
        if (log)
            AppendLog(string.Format(
                EternalRegressionData.TitanRecalledLogs[_random.Next(EternalRegressionData.TitanRecalledLogs.Length)],
                row.Name), LogInfo);
    }

    /// <summary>复活一个已回收的泰坦火种（点亮 + 日志；全黑后随机复活用）</summary>
    private void ReviveTitan(EternalTitanRow row, bool log = false)
    {
        row.IsRecalled = false;
        row.StatusColor = EternalTitanRow.BrightColor;
        row.StatusText = "REVIVED";
        if (log)
            AppendLog(string.Format(
                EternalRegressionData.TitanRevivedLogs[_random.Next(EternalRegressionData.TitanRevivedLogs.Length)],
                row.Name), LogGold);
    }

    private void RestoreTitans()
    {
        var targetRecalled = Math.Min(EternalRegressionData.Titans.Length, (int)Math.Min(int.MaxValue, _totalDeleted));
        for (var i = 0; i < targetRecalled && i < _titans.Count; i++)
            MarkRecalled(_titans[i]);
        _lastRecalled = targetRecalled;
    }

    private void ResetAllRows()
    {
        foreach (var row in _heirs)
        {
            row.IsDeleted = false;
            row.SignalColor = EternalHeirRow.NormalSignal;
            row.NameColor = EternalHeirRow.NormalName;
            row.ProgressColor = EternalHeirRow.NormalProgressColor;
            row.Progress = 0d;
            row.StatusText = "";
        }
        foreach (var row in _titans)
        {
            row.IsRecalled = false;
            row.StatusColor = EternalTitanRow.NormalColor;
            row.StatusText = "";
        }
        _lastRecalled = 0;
        _reviveTick = 0;
        _reviveInterval = 25;
        _reviveFloor = 0;
        _quickTick = 0;
        _quickInterval = 4;
        _quickFileSeq = 0;
    }

    private void CompleteDeletions()
    {
        for (var i = 0; i < _heirs.Count; i++)
            MarkDeleted(_heirs[i]);
        for (var i = 0; i < _titans.Count; i++)
            MarkRecalled(_titans[i]);
        _lastRecalled = _titans.Count;
        CounterLabel.Text = "0";
        CounterSubLabel.Text = "全部轮回完成";
        TotalProgress.Progress = 1d;
        TotalProgressLabel.Text = "100%";
        HeirPanelSub.Text = $"{_heirs.Count}/{_heirs.Count}";
        FocusLayer.IsVisible = false;
        _drawable.NoiseLevel = 1f;
    }

    private void ShowEnding()
    {
        EndingTextLabel.Text = EternalRegressionData.EndingText;
        EndingLayer.IsVisible = true;
        AppendLog("▸ 永劫归零：世界线收束完成，等待自动再创世", LogError);
        Persist();
        _ = ScheduleAutoRecreateAsync();
    }

    private async Task ScheduleAutoRecreateAsync()
    {
        try
        {
            await Task.Delay(EternalRegressionData.AutoRecreateSeconds * 1000, _startCts?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (_startCts?.IsCancellationRequested == true)
            return;
        Dispatcher.Dispatch(Recreate);
    }

    private void Recreate()
    {
        EndingLayer.IsVisible = false;
        _ended = false;
        _totalDeleted = 0;
        _currentStartUtc = DateTime.UtcNow;
        ResetAllRows();
        Persist();
        BeginSimulation();
    }

    private void AppendWorldLog()
    {
        if (_random.NextDouble() < 0.28)
        {
            var signal = EternalRegressionData.OutWorldSignals[_random.Next(EternalRegressionData.OutWorldSignals.Length)];
            AppendLog($"◈ {signal}", LogGold);
            return;
        }

        var template = EternalRegressionData.WorldLogTemplates[_random.Next(EternalRegressionData.WorldLogTemplates.Length)];
        var line = string.Format(template, _random.Next(0, 101), _random.Next(0, 100));
        AppendLog($"[{CycleRemaining:N0}] · {line}", LogInfo);
    }

    private void AppendLog(string text, Color color)
    {
        _logs.Add(new EternalLogLine(text, color));
        if (_logs.Count > EternalRegressionData.MaxLogLines)
            _logs.RemoveAt(0);
        // 自动滚动到底：先直接滚，再调度一次确保新项渲染后滚到底
        TryScrollToEnd();
        Dispatcher.Dispatch(TryScrollToEnd);
    }

    private void TryScrollToEnd()
    {
        if (_logs.Count == 0) return;
        try
        {
            LogList.ScrollTo(_logs[^1], position: ScrollToPosition.End, animate: false);
        }
        catch
        {
            // 布局未就绪时静默，下一帧调度重试
        }
    }

    // ───────── 进度持久化 ─────────

    private static string ProgressPath => Path.Combine(EternalRegressionData.ProgressDirectory, EternalRegressionData.ProgressFileName);

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(EternalRegressionData.ProgressDirectory);
            var payload = new EternalProgressPayload
            {
                TotalDeleted = _totalDeleted,
                CurrentStartUtc = _currentStartUtc,
                Ended = _ended
            };
            File.WriteAllText(ProgressPath, JsonSerializer.Serialize(payload));
            _lastPersistUtc = DateTime.UtcNow;
        }
        catch
        {
            // 装饰数据，持久化失败静默
        }
    }

    private void LoadProgress()
    {
        try
        {
            if (!File.Exists(ProgressPath))
                return;
            var payload = JsonSerializer.Deserialize<EternalProgressPayload>(File.ReadAllText(ProgressPath));
            if (payload == null)
                return;
            _totalDeleted = Math.Max(0, payload.TotalDeleted);
            if (payload.CurrentStartUtc != default)
                _currentStartUtc = payload.CurrentStartUtc.ToUniversalTime();
            _ended = payload.Ended;
        }
        catch
        {
            // 读取失败则新起一轮
        }
    }
}

/// <summary>进度持久化载荷</summary>
public sealed class EternalProgressPayload
{
    public long TotalDeleted { get; set; }
    public DateTime CurrentStartUtc { get; set; }
    public bool Ended { get; set; }
}

/// <summary>黄金裔删除列表行模型</summary>
public sealed partial class EternalHeirRow : ObservableObject
{
    public static readonly Color NormalSignal = Color.FromArgb("#A8E4FF");
    public static readonly Color NormalName = Color.FromArgb("#E0F8F8");
    public static readonly Color NormalProgressColor = Color.FromArgb("#40E0E0");

    public string Name { get; }
    public string Signal { get; }
    public string FullName { get; }
    public string[] Lines { get; }

    [ObservableProperty]
    private bool _isDeleted;

    [ObservableProperty]
    private Color _signalColor = NormalSignal;

    [ObservableProperty]
    private Color _nameColor = NormalName;

    [ObservableProperty]
    private Color _progressColor = NormalProgressColor;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public EternalHeirRow(EternalHeir heir)
    {
        Name = heir.Name;
        Signal = heir.Signal;
        FullName = heir.FullName;
        Lines = heir.Lines;
    }

    /// <summary>从台词池随机取一句（删除日志用）</summary>
    public string RandomLine(Random random)
        => Lines.Length > 0 ? Lines[random.Next(Lines.Length)] : string.Empty;
}

/// <summary>泰坦火种回收列表行模型</summary>
public sealed partial class EternalTitanRow : ObservableObject
{
    public static readonly Color NormalColor = Color.FromArgb("#A8D8D8");
    public static readonly Color BrightColor = Color.FromArgb("#E8FFFF");

    public string Name { get; }
    public string Code { get; }

    [ObservableProperty]
    private bool _isRecalled;

    [ObservableProperty]
    private Color _statusColor = NormalColor;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public EternalTitanRow(EternalTitan titan)
    {
        Name = titan.Name;
        Code = titan.Code;
    }
}

/// <summary>系统日志行模型（只增不改，无需属性通知）</summary>
public sealed class EternalLogLine
{
    public string Text { get; }
    public Color Color { get; }

    public EternalLogLine(string text, Color color)
    {
        Text = text;
        Color = color;
    }
}
