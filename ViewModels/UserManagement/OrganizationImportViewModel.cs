using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Components;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.UserManagement;

public partial class OrganizationImportViewModel : ViewModelBase
{
    private readonly IOrganizationService _organizationService;
    private readonly ILoggerService _logger;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    private static readonly string[] ExcelFileExtensions = { ".xlsx", ".xls", ".csv" };

    [ObservableProperty]
    private string _selectedFilePath = string.Empty;

    [ObservableProperty]
    private string _selectedFileName = string.Empty;

    [ObservableProperty]
    private bool _hasSelectedFile;

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private double _importProgress;

    [ObservableProperty]
    private string _importProgressText = string.Empty;

    [ObservableProperty]
    private int _successCount;

    [ObservableProperty]
    private int _failureCount;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private bool _hasImportResult;

    [ObservableProperty]
    private ObservableCollection<OrganizationPreviewRow> _previewRows = new();

    #region SnackBar

    [ObservableProperty]
    private string _snackBarMessage = string.Empty;

    [ObservableProperty]
    private SnackBarType _snackBarType;

    [ObservableProperty]
    private bool _isSnackBarVisible;

    private int _snackBarVersion;

    private Task ShowSnackBarAsync(string message, SnackBarType type = SnackBarType.Success)
    {
        SnackBarMessage = message;
        SnackBarType = type;
        IsSnackBarVisible = true;

        // 非阻塞隐藏：不延长调用命令的执行时间；版本号防止旧的隐藏任务关闭新消息
        var version = ++_snackBarVersion;
        _ = HideSnackBarAfterDelayAsync(version);
        return Task.CompletedTask;
    }

    private async Task HideSnackBarAfterDelayAsync(int version)
    {
        try
        {
            await Task.Delay(3000);
            if (version == _snackBarVersion)
            {
                IsSnackBarVisible = false;
            }
        }
        catch
        {
            // fire-and-forget：忽略异常，避免未观察的任务异常
        }
    }

    #endregion

    public OrganizationImportViewModel(
        IOrganizationService organizationService,
        ILoggerService logger,
        IDialogService dialogService,
        IServiceProvider serviceProvider)
    {
        _organizationService = organizationService;
        _logger = logger;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;
    }

    [RelayCommand]
    private async Task SelectFileAsync()
    {
        var path = await PickFileWithFeedbackAsync("选择组织数据文件", ExcelFileExtensions);
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            SelectedFilePath = path;
            SelectedFileName = Path.GetFileName(path);
            HasSelectedFile = true;

            await LoadPreviewAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "文件选择失败");
            await _dialogService.DisplayAlertAsync("错误", $"选择文件失败: {ex.Message}", "确定");
        }
    }

    private async Task LoadPreviewAsync()
    {
        try
        {
            PreviewRows.Clear();
            HasImportResult = false;
            SuccessCount = 0;
            FailureCount = 0;
            TotalCount = 0;

            var preview = await Task.Run(() => _organizationService.PreviewCsvImport(SelectedFilePath, 5));

            if (preview.TotalRows == 0)
            {
                await ShowSnackBarAsync("文件无有效数据或缺少「名称」列", SnackBarType.Warning);
                return;
            }

            TotalCount = preview.TotalRows;
            foreach (var row in preview.PreviewRows)
            {
                PreviewRows.Add(row);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "文件读取失败");
            await ShowSnackBarAsync($"文件读取失败: {ex.Message}", SnackBarType.Error);
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (!HasSelectedFile || TotalCount == 0)
        {
            await ShowSnackBarAsync("没有可导入的数据", SnackBarType.Warning);
            return;
        }

        IsImporting = true;
        ImportProgress = 0;
        ImportProgressText = "正在导入...";
        SuccessCount = 0;
        FailureCount = 0;

        try
        {
            var progress = new Progress<string>(msg =>
            {
                ImportProgressText = msg;
            });

            var result = await _organizationService.ImportFromCsvAsync(SelectedFilePath, progress, CancellationToken);

            SuccessCount = result.ImportedCount;
            FailureCount = result.ErrorCount;
            ImportProgress = 1.0;
            HasImportResult = true;
            ImportProgressText = result.Message ?? $"导入完成：成功{SuccessCount}，失败{FailureCount}";
            _logger.LogBusiness("组织导入完成", ("Success", SuccessCount), ("Failure", FailureCount));

            await ShowSnackBarAsync($"导入完成：成功{SuccessCount}条",
                FailureCount > 0 ? SnackBarType.Warning : SnackBarType.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导入失败");
            await ShowSnackBarAsync($"导入失败: {ex.Message}", SnackBarType.Error);
        }
        finally
        {
            IsImporting = false;
        }
    }
}