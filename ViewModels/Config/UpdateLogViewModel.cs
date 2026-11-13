using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.Config;

/// <summary>
/// 更新日志页：当前版本 + 检查更新 + 升级历史（本机升级轨迹 ∪ 服务端全量 changelog.json）。
/// 素材来源：发版时由仓库根 CHANGELOG.md 抽取、写入 update.json.notes 与服务端 changelog.json。
/// 只做展示与检查更新，不参与安装决策（安装走签名清单 update.json）。
/// </summary>
public partial class UpdateLogViewModel : ViewModelBase
{
    private readonly IUpdateService _updateService;
    private readonly IUpdateHistoryService _historyService;
    private readonly IAppUpdateCoordinator _updateCoordinator;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    /// <summary>当前运行版本。</summary>
    [ObservableProperty]
    private string _currentVersion = string.Empty;

    /// <summary>升级历史条目（本机优先合并、按版本新 → 旧）。</summary>
    [ObservableProperty]
    private ObservableCollection<UpdateHistoryItem> _items = new();

    [ObservableProperty]
    private bool _hasItems;

    /// <summary>列表空态提示。</summary>
    [ObservableProperty]
    private string _emptyHint = "暂无升级记录，可点「在线日志」拉取服务端全量";

    /// <summary>是否已拉过服务端全量（影响空态文案与刷新按钮态）。</summary>
    [ObservableProperty]
    private bool _remoteLoaded;

    public UpdateLogViewModel(
        IUpdateService updateService,
        IUpdateHistoryService historyService,
        IAppUpdateCoordinator updateCoordinator,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider)
    {
        _updateService = updateService;
        _historyService = historyService;
        _updateCoordinator = updateCoordinator;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        Title = "更新日志";
    }

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        CurrentVersion = _updateService.CurrentVersion;
        await LoadLocalAsync();
    }

    /// <summary>载入本机升级历史；读取失败显式提示（不静默当成"无记录"）。</summary>
    private async Task LoadLocalAsync()
    {
        var result = await _historyService.GetHistoryAsync(CancellationToken);
        if (result.IsFailure)
        {
            Apply(Array.Empty<UpdateHistoryItem>());
            await _dialogService.DisplayAlertAsync("更新日志", result.Message ?? "本机升级历史读取失败", "确定");
            return;
        }

        Apply(result.Value);
    }

    /// <summary>拉取服务端全量升级日志并与本机历史合并。</summary>
    [RelayCommand]
    private async Task LoadRemoteAsync()
    {
        var result = await ExecuteAsync(async () =>
        {
            var merged = await _historyService.FetchMergedAsync(CancellationToken);
            if (merged.IsFailure)
                return Result.Failure<IReadOnlyList<UpdateHistoryItem>>(merged.ErrorCode!, merged.Message!);

            return Result.Success(merged.Value);
        }, "拉取在线更新日志...");

        if (result.IsFailure)
        {
            // 显式失败：网络/服务端不可用不伪装成"没有记录"
            await _dialogService.DisplayAlertAsync("在线日志", result.Message ?? "拉取失败", "确定");
            return;
        }

        RemoteLoaded = true;
        Apply(result.Value);
    }

    /// <summary>检查更新（复用统一升级流程：弹窗说明 → 下载 → 安装；说明落 pending 待新版写入历史）。</summary>
    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        await ExecuteAsync(async () =>
        {
            await _updateCoordinator.CheckAndPromptAsync(manual: true, CancellationToken);
            return Result.Success();
        }, "检查更新...");
    }

    private void Apply(IReadOnlyList<UpdateHistoryItem> items)
    {
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);

        HasItems = Items.Count > 0;
        EmptyHint = RemoteLoaded
            ? "暂无升级记录（服务端与本机都没有）"
            : "暂无升级记录，可点「在线日志」拉取服务端全量";
    }
}
