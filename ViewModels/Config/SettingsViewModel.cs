using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Helpers;
using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.ViewModels.Config;

/// <summary>
/// 系统设置 ViewModel：文档输出位置（输出根）查看、更改与恢复默认。
/// 输出根全局生效（档案制作/补打/核查/月报表/高龄月报/资产核查六处四件套统一落此根，
/// 见 docs\20261007_文档动作与输出路径统一规范.md）；
/// 选择结果存 AppData 用户级覆盖文件（document_output.user.json），旧根由 OutputRootMigrationService 后台整理。
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly IOutputRootMigrationService _outputMigrator;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerService _logger;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    /// <summary>
    /// 当前平台是否允许改输出根：Android 无共享「文档」目录写权限，
    /// 且 AndroidFolderPickerService 返回的是缓存副本目录，不可作为输出根，故只读展示。
    /// </summary>
    public bool CanChangeOutputRoot { get; }

    /// <summary>当前输出根（绝对路径）</summary>
    [ObservableProperty] private string _outputRoot = string.Empty;

    /// <summary>输出根来源：默认配置 / 自定义（系统设置）</summary>
    [ObservableProperty] private string _outputRootSourceText = string.Empty;

    /// <summary>预览临时目录（{输出根}/{temp}，启动时按 cleanup 规则回收）</summary>
    [ObservableProperty] private string _previewTempDirectory = string.Empty;

    /// <summary>平台提示（Android 只读说明 / 桌面说明）</summary>
    [ObservableProperty] private string _platformHintText = string.Empty;

    public SettingsViewModel(
        IConfigService configService,
        IOutputRootMigrationService outputMigrator,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider)
    {
        _configService = configService;
        _outputMigrator = outputMigrator;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        Title = "系统设置";

        CanChangeOutputRoot = Microsoft.Maui.Devices.DeviceInfo.Platform != Microsoft.Maui.Devices.DevicePlatform.Android;
        PlatformHintText = CanChangeOutputRoot
            ? "所有文书/报表的保存、打印留痕都会写入该目录；预览临时文件也在此目录下。"
            : "当前平台输出目录固定为应用私有存储，不可更改。";

        LoadOutputRoot();
    }

    public override Task OnAppearingAsync()
    {
        LoadOutputRoot();
        return base.OnAppearingAsync();
    }

    private void LoadOutputRoot()
    {
        var options = _configService.GetDocumentOutputOptions();
        OutputRoot = options.BaseDirectory;
        OutputRootSourceText = options.IsUserOverride ? "自定义（系统设置）" : "默认（document_output.yaml）";
        PreviewTempDirectory = Path.Combine(options.BaseDirectory, options.TempSubdirectory);
    }

    /// <summary>打开当前输出根</summary>
    [RelayCommand]
    private void OpenOutputRoot()
    {
        try
        {
            Directory.CreateDirectory(OutputRoot);
        }
        catch
        {
            // 目录创建失败交由 TryOpenFolder 静默处理
        }
        TryOpenFolder(OutputRoot);
    }

    /// <summary>选择新的输出根（全局生效）</summary>
    [RelayCommand]
    private async Task ChangeOutputRootAsync()
    {
        if (!CanChangeOutputRoot)
        {
            await _dialogService.DisplayAlertAsync("提示", PlatformHintText, "确定");
            return;
        }

        var picked = await PickExportFolderAsync("选择文书与报表输出目录", OutputRoot,
            "已取消更改输出目录", promptReuse: false);
        if (string.IsNullOrWhiteSpace(picked))
            return; // 取消/失败已各自给出可见反馈

        await ApplyOutputRootAsync(picked);
    }

    /// <summary>恢复默认输出根（删除用户覆盖，回退 config/document_output.yaml）</summary>
    [RelayCommand]
    private async Task ResetOutputRootAsync()
    {
        if (!CanChangeOutputRoot)
        {
            await _dialogService.DisplayAlertAsync("提示", PlatformHintText, "确定");
            return;
        }

        if (!_configService.GetDocumentOutputOptions().IsUserOverride)
        {
            await _dialogService.ShowSnackBarAsync("当前已是默认输出目录", NewCosmos.Components.SnackBarType.Info);
            return;
        }

        var proceed = await _dialogService.DisplayAlertAsync("恢复默认输出目录",
            "将删除自定义设置，恢复为配置文件中的默认目录。\n旧目录中的文件会在后台整理到默认目录。",
            "恢复默认", "取消");
        if (!proceed)
            return;

        try
        {
            _configService.ResetDocumentOutputBaseDirectory();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[系统设置] 恢复默认输出根失败");
            await _dialogService.DisplayAlertAsync("恢复失败", ex.Message, "确定");
            return;
        }

        await AfterOutputRootChangedAsync("已恢复默认输出目录");
    }

    /// <summary>保存/恢复后的共通动作：刷新展示 + 触发后台迁移</summary>
    private async Task AfterOutputRootChangedAsync(string successMessage)
    {
        var options = _configService.GetDocumentOutputOptions();
        OutputPathHelper.Configure(options.BaseDirectory, options.TempSubdirectory);
        LoadOutputRoot();

        _logger.LogBusiness("更改文档输出目录", ("输出根", options.BaseDirectory), ("来源", OutputRootSourceText));

        await _dialogService.ShowSnackBarAsync($"{successMessage}：{options.BaseDirectory}",
            NewCosmos.Components.SnackBarType.Success);
        await _dialogService.ShowSnackBarAsync("旧文件正在后台整理到新目录（含打印留痕路径同步）…",
            NewCosmos.Components.SnackBarType.Info, 5000);

        // 后台迁移：失败只记日志，状态不更新，下次启动重试（服务内部已兜底 try/catch）
        _ = Task.Run(async () =>
        {
            try
            {
                await _outputMigrator.MigrateToAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[系统设置] 切换输出根后迁移异常");
            }
        });
    }

    private async Task ApplyOutputRootAsync(string newRoot)
    {
        var oldRoot = OutputPathHelper.OutputRoot;

        if (OutputPathHelper.ArePathsNested(oldRoot, newRoot))
        {
            // ArePathsNested 对同一路径也返回 true：同根单独给"无变化"提示
            var same = string.Equals(
                TrimPath(oldRoot), TrimPath(newRoot), StringComparison.OrdinalIgnoreCase);
            await _dialogService.DisplayAlertAsync(same ? "提示" : "无法选择该目录",
                same ? "当前输出目录已是所选目录。" : "所选目录与当前输出目录互为包含，请选择两者之外的目录。",
                "确定");
            return;
        }

        if (!IsDirectoryWritable(newRoot, out var writeError))
        {
            await _dialogService.DisplayAlertAsync("目录不可用",
                $"所选目录无法写入，请更换目录。\n{writeError}", "确定");
            return;
        }

        try
        {
            _configService.SetDocumentOutputBaseDirectory(newRoot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[系统设置] 保存输出根失败");
            await _dialogService.DisplayAlertAsync("保存失败", ex.Message, "确定");
            return;
        }

        await AfterOutputRootChangedAsync("输出目录已更改");
    }

    private static string TrimPath(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>可写探针：建目录 + 写删测试文件（删除失败不影响判定）</summary>
    private static bool IsDirectoryWritable(string directory, out string? error)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".nc_write_probe_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "probe");
            try { File.Delete(probe); } catch { /* 被占用也不影响可写判定 */ }
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
