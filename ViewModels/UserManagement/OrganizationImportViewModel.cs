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
    private ObservableCollection<string> _importErrors = new();

    [ObservableProperty]
    private bool _hasImportErrors;

    [ObservableProperty]
    private ObservableCollection<OrganizationPreviewRow> _previewRows = new();
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
            ImportErrors.Clear();
            HasImportErrors = false;
            SuccessCount = 0;
            FailureCount = 0;
            TotalCount = 0;

            var preview = await Task.Run(() => _organizationService.PreviewImport(SelectedFilePath, 5));

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
        ImportErrors.Clear();
        HasImportErrors = false;

        try
        {
            var progress = new Progress<string>(msg =>
            {
                ImportProgressText = msg;
            });

            var result = await _organizationService.ImportFromFileAsync(SelectedFilePath, progress, CancellationToken);

            // 失败结果要显式失败：原实现无视 result.Success，一律显示"导入完成"
            if (!result.Success)
            {
                _logger.Error($"组织导入失败: {result.Message}");
                HasImportResult = true;
                ImportProgressText = result.Message ?? "导入失败";
                await ShowSnackBarAsync(ImportProgressText, SnackBarType.Error);
                return;
            }

            SuccessCount = result.ImportedCount;
            FailureCount = result.ErrorCount;
            ImportProgress = 1.0;
            HasImportResult = true;
            ImportProgressText = result.Message ?? $"导入完成：成功{SuccessCount}，失败{FailureCount}";
            _logger.LogBusiness("组织导入完成", ("Success", SuccessCount), ("Failure", FailureCount));

            if (FailureCount > 0 && result.Errors.Count > 0)
            {
                foreach (var err in result.Errors.Take(50))
                    ImportErrors.Add(err);
                if (result.Errors.Count > 50)
                    ImportErrors.Add($"……另有 {result.Errors.Count - 50} 条，完整明细见导入日志");
                HasImportErrors = true;
                _logger.Warn($"组织导入失败明细({result.Errors.Count}条): {string.Join("；", result.Errors)}");

                await ShowSnackBarAsync($"成功 {SuccessCount} 条，失败 {FailureCount} 条，详见下方明细",
                    SnackBarType.Warning);
            }
            else
            {
                await ShowSnackBarAsync($"导入完成：成功{SuccessCount}条", SnackBarType.Success);
            }
        }
        catch (OperationCanceledException)
        {
            await ShowSnackBarAsync("导入已取消", SnackBarType.Warning);
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