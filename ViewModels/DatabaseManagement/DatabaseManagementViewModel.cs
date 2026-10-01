using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;
using NewCosmos.Pages.DatabaseManagement;
using NewCosmos.Services.Core;
using NewCosmos.Navigation;
using NewCosmos.Services.Database;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.DatabaseManagement;

public partial class DatabaseManagementViewModel : ViewModelBase
{
    private readonly IDatabaseManagementService _dbManagementService = null!;
    private readonly IConfigService _configService = null!;
    private readonly ISchemaService _schemaService = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly INewPermissionService _permissionService = null!;
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IClientVersionService _clientVersionService = null!;
    private int _currentUserId;

    #region 数据库状态

    [ObservableProperty]
    private DatabaseStatus _databaseStatus = null!;

    [ObservableProperty]
    private DatabaseStatistics _databaseStatistics = null!;

    [ObservableProperty]
    private ObservableCollection<TableSizeInfo> _tableSizes = new();

    [ObservableProperty]
    private bool _isDatabaseHealthy;

    #endregion

    #region 备份管理

    [ObservableProperty]
    private ObservableCollection<BackupFileInfo> _backupFiles = new();

    [ObservableProperty]
    private string _backupPath = string.Empty;

    #endregion

    #region Schema状态

    [ObservableProperty]
    private SchemaStatus _schemaStatus = new();

    [ObservableProperty]
    private bool _canInitializeSchema;

    public string SchemaStatusText => SchemaStatus.StatusText ?? "未检测";
    public string SchemaStatusColor => SchemaStatus.StatusColor ?? "#6C757D";
    public string SchemaStatusIcon => SchemaStatus.StatusIcon ?? "○";
    public string SchemaStatusSummary => SchemaStatus.StatusSummary ?? string.Empty;

    public bool HasMissingColumns => SchemaStatus.HasMissingColumns == true;
    public bool HasMissingTables => SchemaStatus.HasMissingTables == true;

    public string SchemaMissingColumnsSummary
    {
        get
        {
            if (SchemaStatus == null || !SchemaStatus.HasMissingColumns)
                return string.Empty;
            return string.Join("\n", SchemaStatus.MissingColumns
                .Select(kv => $"• {kv.Key}: 缺少 {string.Join(", ", kv.Value)}"));
        }
    }

    public string SchemaMissingTablesSummary
    {
        get
        {
            if (SchemaStatus == null || !SchemaStatus.HasMissingTables)
                return string.Empty;
            return "缺少: " + string.Join(", ", SchemaStatus.MissingTables);
        }
    }

    [ObservableProperty]
    private bool _canFixSchema;

    #endregion

    #region 权限控制

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackupDatabaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanupLogsCommand))]
    private bool _canBackupDatabase;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackupDatabaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanupLogsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DetectSoftDeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanupSoftDeletedCommand))]
    private bool _canCleanupLogs;

    public double BackupOpacity => CanBackupDatabase ? 1.0 : 0.5;
    public double CleanupOpacity => CanCleanupLogs ? 1.0 : 0.5;

    #endregion

    #region 客户端版本台账

    [ObservableProperty]
    private ObservableCollection<ClientVersionRow> _clientVersions = new();

    [ObservableProperty]
    private ObservableCollection<ClientVersionRow> _filteredClientVersions = new();

    [ObservableProperty]
    private bool _showOnlyOutdatedClients;

    [ObservableProperty]
    private string _clientVersionSummary = "未加载";

    /// <summary>参考版本=当前运行版本（县局一般运行最新版）</summary>
    public string LatestKnownVersion => _configService.GetAppOptions().Version;

    partial void OnShowOnlyOutdatedClientsChanged(bool value) => ApplyClientVersionFilter();

    /// <summary>加载客户端版本台账（登录后自动上报的记录）</summary>
    [RelayCommand]
    private async Task LoadClientVersionsAsync()
    {
        await ExecuteAsync(async ct =>
        {
            var result = await _clientVersionService.GetListAsync(ct);
            if (result.IsFailure)
            {
                ClientVersionSummary = $"加载失败：{result.Message}";
                return;
            }

            ClientVersions.Clear();
            foreach (var row in result.Value)
                ClientVersions.Add(row);
            ApplyClientVersionFilter();
        }, "加载客户端版本...");
    }

    private void ApplyClientVersionFilter()
    {
        var latest = LatestKnownVersion;
        var source = ShowOnlyOutdatedClients
            ? ClientVersions.Where(v => !string.Equals(v.AppVersion, latest, StringComparison.OrdinalIgnoreCase))
            : ClientVersions;

        FilteredClientVersions.Clear();
        foreach (var row in source)
            FilteredClientVersions.Add(row);

        var outdated = ClientVersions.Count(v => !string.Equals(v.AppVersion, latest, StringComparison.OrdinalIgnoreCase));
        ClientVersionSummary = $"共 {ClientVersions.Count} 台；非最新 {outdated} 台（最新版本 {latest}）";
    }

    #endregion

    public DatabaseManagementViewModel(
        IDatabaseManagementService dbManagementService,
        IConfigService configService,
        ISchemaService schemaService,
        IDialogService dialogService,
        ILoggerService logger,
        INewPermissionService permissionService,
        IServiceProvider serviceProvider,
        IClientVersionService clientVersionService)
    {
        _dbManagementService = dbManagementService;
        _configService = configService;
        _schemaService = schemaService;
        _dialogService = dialogService;
        _logger = logger;
        _permissionService = permissionService;
        _serviceProvider = serviceProvider;
        _clientVersionService = clientVersionService;

        var storageOptions = _configService.GetStorageOptions();
        BackupPath = storageOptions.GetBackupPath();
    }

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    public override async Task OnAppearingAsync()
    {
        await LoadDatabaseStatusAsync();
        await LoadBackupFilesAsync();
        await LoadSchemaStatusAsync();
        await LoadClientVersionsAsync();
    }

    public async Task InitializePermissionsAsync(int userId)
    {
        _currentUserId = userId;
        if (_currentUserId <= 0) return;

        try
        {
            CanBackupDatabase = await _permissionService.HasPermissionAsync(_currentUserId, PermissionCodes.SYSTEM_BACKUP);
            CanCleanupLogs = await _permissionService.HasPermissionAsync(_currentUserId, PermissionCodes.SYSTEM_BACKUP);
            OnPropertyChanged(nameof(BackupOpacity));
            OnPropertyChanged(nameof(CleanupOpacity));
        }
        catch (Exception ex)
        {
            _logger.Error($"权限检查失败: {ex.Message}");
        }
    }

    #region 数据库状态

    [RelayCommand]
    private async Task LoadDatabaseStatusAsync()
    {
        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("加载数据库状态");

            var statusResult = await _dbManagementService.GetDatabaseStatusAsync(ct);
            if (statusResult.IsSuccess)
            {
                DatabaseStatus = statusResult.Value;
                IsDatabaseHealthy = statusResult.Value.IsConnected;
            }

            var statsResult = await _dbManagementService.GetDatabaseStatisticsAsync(ct);
            if (statsResult.IsSuccess)
            {
                DatabaseStatistics = statsResult.Value;
            }

            var tablesResult = await _dbManagementService.GetTableSizesAsync(ct);
            if (tablesResult.IsSuccess && tablesResult.Value is not null)
            {
                TableSizes.Clear();
                foreach (var table in tablesResult.Value)
                {
                    TableSizes.Add(table);
                }
            }

            _logger.LogBusiness("数据库状态加载完成");
        }, "加载数据库状态...");
    }

    #endregion

    #region 备份管理

    [RelayCommand]
    private async Task LoadBackupFilesAsync()
    {
        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("加载备份文件列表");

            var result = await _dbManagementService.GetBackupFilesAsync(ct);
            if (result.IsSuccess && result.Value is not null)
            {
                BackupFiles.Clear();
                foreach (var file in result.Value)
                {
                    BackupFiles.Add(file);
                }
            }

            _logger.LogBusiness($"找到 {BackupFiles.Count} 个备份文件");
        }, "加载备份文件...");
    }

    [RelayCommand(CanExecute = nameof(CanExecuteBackup))]
    private async Task BackupDatabaseAsync()
    {
        if (!CanBackupDatabase)
            return;

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认备份",
            "将执行数据库完整备份，备份文件将保存到:\n" + BackupPath + "\n\n是否继续？",
            "确定",
            "取消");

        if (!confirm)
            return;

        await ExecuteAsync(async ct =>
        {
            try
            {
                _logger.LogBusiness("执行数据库备份");

                var result = await _dbManagementService.BackupDatabaseAsync(BackupPath, ct);
                if (result.IsSuccess)
                {
                    await _dialogService.DisplayAlertAsync("成功", $"数据库备份完成\n文件: {result.Value}", "确定");
                    await LoadBackupFilesAsync();
                    _logger.LogBusiness("数据库备份成功", ("BackupPath", result.Value));
                }
                else
                {
                    await _dialogService.DisplayAlertAsync("错误", $"备份失败: {result.Message}", "确定");
                    _logger.Error($"数据库备份失败: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                await _dialogService.DisplayAlertAsync("错误", $"备份过程发生异常: {ex.Message}", "确定");
                _logger.Error($"数据库备份异常: {ex.Message}");
            }
        }, "执行数据库备份...");
    }

    private bool CanExecuteBackup() => CanBackupDatabase && IsNotBusy;

    #endregion

    #region 日志清理

    [RelayCommand(CanExecute = nameof(CanExecuteCleanup))]
    private async Task CleanupLogsAsync()
    {
        if (!CanCleanupLogs)
            return;

        var confirm = await _dialogService.DisplayAlertAsync(
            "清理本地日志",
            "将清理过期的本地日志文件：\n" +
            "• app/ 保留 30 天\n" +
            "• biz/ 保留 180 天\n" +
            "• sec/ 保留 365 天\n" +
            "• err/ 保留 90 天\n" +
            "• perf/ 保留 30 天\n\n" +
            "此操作不可恢复，是否继续？",
            "确定",
            "取消");

        if (!confirm)
            return;

        await ExecuteAsync(async ct =>
        {
            try
            {
                _logger.LogBusiness("清理本地日志文件");

                var result = await _dbManagementService.CleanupOldLogsAsync(90, ct);
                if (result.IsSuccess)
                {
                    var storageOptions = _configService.GetStorageOptions();
                    var logPath = storageOptions.GetLogPath();
                    await _dialogService.DisplayAlertAsync("完成", $"已清理 {result.Value} 个过期日志文件\n日志目录: {logPath}", "确定");
                    _logger.LogBusiness("本地日志清理完成", ("DeletedCount", result.Value));
                }
                else
                {
                    await _dialogService.DisplayAlertAsync("错误", $"清理失败: {result.Message}", "确定");
                    _logger.Error($"本地日志清理失败: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                await _dialogService.DisplayAlertAsync("错误", $"清理过程发生异常: {ex.Message}", "确定");
                _logger.Error($"本地日志清理异常: {ex.Message}");
            }
        }, "清理本地日志...");
    }

    private bool CanExecuteCleanup() => CanCleanupLogs && IsNotBusy;

    #endregion

    #region 软删除清理

    [ObservableProperty]
    private SoftDeleteCensus _softDeleteCensus = new();

    [ObservableProperty]
    private string _softDeleteResultText = string.Empty;

    /// <summary>检测全库软删除行（只读）</summary>
    [RelayCommand(CanExecute = nameof(CanExecuteCleanup))]
    private async Task DetectSoftDeleteAsync()
    {
        if (!CanCleanupLogs) return;

        await ExecuteAsync(async ct =>
        {
            var result = await _dbManagementService.DetectSoftDeleteAsync(ct);
            if (result.IsFailure)
            {
                await ShowFailureAsync(result, "检测软删除");
                return;
            }

            SoftDeleteCensus = result.Value;
            var detail = string.Join("\n", result.Value.Tables
                .Select(t => $"• {t.TableName}: 软删 {t.SoftDeleted}，待删 {t.ToDelete}，保留 {t.Kept}"));
            var softText = result.Value.TotalSoftDeleted == 0
                ? "未发现软删除行"
                : $"共 {result.Value.TotalSoftDeleted} 行软删：待删 {result.Value.TotalToDelete}，保留 {result.Value.TotalKept}\n{detail}";

            var chainText = result.Value.ChainIssues.Count == 0
                ? "档案链一致性：正常（无应停未停）"
                : $"档案链一致性：应停未停 {result.Value.ChainIssues.Count} 条\n" +
                  string.Join("\n", result.Value.ChainIssues.Select(c =>
                      $"• 旧档案 {c.OldApplicationId} {c.OldName}（{c.OldStatus}）→ 新档案 {c.NewApplicationId} [{c.ChainType}]"));

            SoftDeleteResultText = $"{softText}\n\n{chainText}";
            await _dialogService.DisplayAlertAsync("检测结果", SoftDeleteResultText, "确定");
        }, "检测软删除...");
    }

    /// <summary>清理软删除行（先本地备份，再物理删除）</summary>
    [RelayCommand(CanExecute = nameof(CanExecuteCleanup))]
    private async Task CleanupSoftDeletedAsync()
    {
        if (!CanCleanupLogs) return;

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认清理软删除档案",
            "将先执行本地数据库备份，再物理删除全库软删除行：\n" +
            "• 保留：停保(Stopped)档案、被变更记录/存活档案链接的档案\n" +
            "• 恢复：停保旧档案下被误软删的成员\n" +
            "• 删除：其余软删行（救助/高龄/临时救助/资产核查/变更记录等）\n\n" +
            "此操作不可恢复，是否继续？",
            "继续",
            "取消");
        if (!confirm) return;

        var cleaned = false;
        await ExecuteAsync(async ct =>
        {
            var result = await _dbManagementService.CleanupSoftDeletedAsync(ct);
            if (result.IsFailure)
            {
                await ShowFailureAsync(result, "清理软删除");
                return;
            }

            var r = result.Value;
            SoftDeleteResultText = $"清理完成：删除 {r.DeletedRows} 行，恢复 {r.RestoredRows} 行，删除档案 {r.DeletedArchives} 个\n备份: {r.BackupFile}";
            await _dialogService.DisplayAlertAsync("清理完成", SoftDeleteResultText, "确定");
            cleaned = true;
        }, "正在清理软删除档案...");

        if (cleaned)
            await LoadDatabaseStatusAsync();
    }

    #endregion

    #region Schema 初始化

    [RelayCommand]
    private async Task LoadSchemaStatusAsync()
    {
        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("检查Schema状态");

            var result = await _schemaService.GetSchemaStatusAsync(ct);
            if (result.IsSuccess && result.Value != null)
            {
                SchemaStatus = result.Value;
                CanInitializeSchema = result.Value.HasMissingTables;
                CanFixSchema = result.Value.HasMissingColumns;
                OnPropertyChanged(nameof(SchemaStatusText));
                OnPropertyChanged(nameof(SchemaStatusColor));
                OnPropertyChanged(nameof(SchemaStatusIcon));
                OnPropertyChanged(nameof(SchemaStatusSummary));
                OnPropertyChanged(nameof(HasMissingColumns));
                OnPropertyChanged(nameof(HasMissingTables));
                OnPropertyChanged(nameof(SchemaMissingColumnsSummary));
                OnPropertyChanged(nameof(SchemaMissingTablesSummary));

                if (result.Value.HasMissingColumns)
                {
                    _logger.Warn($"Schema列级验证: 发现 {result.Value.TotalMissingColumns} 个缺失列");
                }
                if (result.Value.HasMissingTables)
                {
                    _logger.Warn($"Schema表级验证: 发现 {result.Value.TotalMissingTables} 个缺失表 [{string.Join(", ", result.Value.MissingTables)}]");
                }

                _logger.LogBusiness($"Schema状态: {result.Value.StatusText} | {result.Value.StatusSummary}");
            }
        }, "检查Schema状态...");
    }

    [RelayCommand]
    private async Task InitializeSchemaAsync()
    {
        if (!CanInitializeSchema) return;

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认初始化",
            SchemaStatus.MissingTables.Count > 0
                ? $"将创建 {SchemaStatus.MissingTables.Count} 个缺失的表\n\n是否继续？"
                : "将初始化数据库结构\n\n是否继续？",
            "确定",
            "取消");

        if (!confirm) return;

        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("开始初始化数据库结构");

            var userName = App.CurrentUserName ?? "Unknown";
            var result = await _schemaService.InitializeAllTablesAsync(userName, ct);

            if (result.IsSuccess)
            {
                await _dialogService.DisplayAlertAsync("成功", "数据库结构初始化完成", "确定");
                await LoadSchemaStatusAsync();
                _logger.LogBusiness("数据库结构初始化成功");
            }
            else
            {
                await _dialogService.DisplayAlertAsync("失败", result.Message ?? "初始化失败", "确定");
                _logger.Error($"数据库结构初始化失败: {result.Message}");
            }
        }, "初始化数据库结构...");
    }

    [RelayCommand]
    private async Task FixSchemaAsync()
    {
        if (!CanFixSchema || SchemaStatus == null || !SchemaStatus.HasMissingColumns) return;

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认修复",
            $"将自动添加 {SchemaStatus.TotalMissingColumns} 个缺失列到数据库\n\n" + SchemaMissingColumnsSummary + "\n\n是否继续？",
            "确定",
            "取消");

        if (!confirm) return;

        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("开始修复缺失列");

            var validationResult = await _schemaService.ValidateAllSchemasAsync(ct);
            if (!validationResult.IsSuccess || validationResult.Value == null)
            {
                await _dialogService.DisplayAlertAsync("错误", "验证Schema失败", "确定");
                return;
            }

            var missingColumnDiffs = validationResult.Value.Differences
                .Where(d => d.Type == DifferenceType.MissingColumn)
                .ToList();

            if (missingColumnDiffs.Count == 0)
            {
                await _dialogService.DisplayAlertAsync("提示", "没有需要修复的缺失列", "确定");
                return;
            }

            var fixResult = await _schemaService.FixSchemaDifferencesAsync(missingColumnDiffs, ct);

            if (fixResult.IsSuccess && fixResult.Value != null)
            {
                var msg = fixResult.Value.Summary;
                await _dialogService.DisplayAlertAsync("修复完成", msg, "确定");
                await LoadSchemaStatusAsync();
                _logger.LogBusiness($"缺失列修复完成: {fixResult.Value.Summary}");
            }
            else
            {
                await _dialogService.DisplayAlertAsync("失败", fixResult.Message ?? "修复失败", "确定");
                _logger.Error($"缺失列修复失败: {fixResult.Message}");
            }
        }, "修复缺失列...");
    }

    #endregion

    #region 页面导航

    private async Task PushPageAsync<T>() where T : Page
    {
        try
        {
            var page = _serviceProvider.GetRequiredService<T>();
            _serviceProvider.GetRequiredService<IWindowTitleService>()?.Register(page);
            await Helpers.WindowNavigator.CurrentPage!.Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            _logger.Error($"Navigation to {typeof(T).Name} failed: {ex.Message}");
            await _dialogService.DisplayAlertAsync("错误", $"打开页面失败: {ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private async Task NavigateToDictionaryManagementAsync()
    {
        await PushPageAsync<DictionaryManagementPage>();
    }

    [RelayCommand]
    private async Task NavigateToStandardConfigManagementAsync()
    {
        await PushPageAsync<StandardConfigManagementPage>();
    }

    [RelayCommand]
    private async Task NavigateToDataImportAsync()
    {
        await PushPageAsync<DataImportPage>();
    }

    [RelayCommand]
    private async Task NavigateToRegionManagementAsync()
    {
        await PushPageAsync<RegionManagementPage>();
    }

    #endregion
}