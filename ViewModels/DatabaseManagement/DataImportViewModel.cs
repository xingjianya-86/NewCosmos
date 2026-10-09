using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Import;
using NewCosmos.Services.Platform;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace NewCosmos.ViewModels.DatabaseManagement;

public partial class DataImportViewModel : ViewModelBase
{
    private static readonly string[] ExcelExtensionsWinUI = { ".xlsx", ".xls" };

    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IImportServiceManager _importServiceManager = null!;
    private readonly ILoggerService _logger = null!;

    private readonly IDialogService _dialogService = null!;

    #region 导入类型

    private Dictionary<string, string> _importTypeMapping = new();

    [ObservableProperty]
    private ObservableCollection<string> _importTypeNames = new();

    [ObservableProperty]
    private string _selectedImportTypeName = string.Empty;

    public string SelectedImportTypeKey => _importTypeMapping.FirstOrDefault(x => x.Value == SelectedImportTypeName).Key ?? string.Empty;

    public bool IsCombinedImportMode => ImportTypeCodes.IsCombinedType(SelectedImportTypeKey);

    public bool IsSingleFileImportMode => ImportTypeCodes.IsSingleFileType(SelectedImportTypeKey);

    public bool ShowMatchedFiles => IsCombinedImportMode && HasMatchedFiles;

    public bool ShowMatchError => IsCombinedImportMode && HasSelectedFiles && !HasMatchedFiles;

    public bool CanStartImport => IsCombinedImportMode ? HasMatchedFiles : HasSelectedFiles;

    #endregion

    #region 文件选择

    [ObservableProperty]
    private ObservableCollection<SelectedFileInfo> _selectedFiles = new();

    public bool HasSelectedFiles => SelectedFiles.Count > 0;

    [ObservableProperty]
    private string _selectedFolderPath = string.Empty;

    public bool HasSelectedFolder => !string.IsNullOrEmpty(SelectedFolderPath);

    [ObservableProperty]
    private string _familyFileName = string.Empty;

    [ObservableProperty]
    private string _personFileName = string.Empty;

    public bool HasMatchedFiles => !string.IsNullOrEmpty(FamilyFileName) && !string.IsNullOrEmpty(PersonFileName);

    #endregion

    #region 导入选项

    [ObservableProperty]
    private bool _clearBeforeImport;

    [ObservableProperty]
    private string _importModeHint = string.Empty;

    #endregion

    #region 预览

    [ObservableProperty]
    private bool _isPreviewLoading;

    [ObservableProperty]
    private bool _hasPreview;

    [ObservableProperty]
    private int _familyPreviewRows;

    [ObservableProperty]
    private int _personPreviewRows;

    [ObservableProperty]
    private ObservableCollection<ColumnMappingInfo> _familyColumnMappings = new();

    [ObservableProperty]
    private ObservableCollection<ColumnMappingInfo> _personColumnMappings = new();

    #endregion

    #region 导入进度

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private double _importProgress;

    [ObservableProperty]
    private string _importStatusMessage = string.Empty;

    [ObservableProperty]
    private string _importResultMessage = string.Empty;

    #endregion

    #region 导入结果

    [ObservableProperty]
    private int _familyImportedCount;

    [ObservableProperty]
    private int _personImportedCount;

    [ObservableProperty]
    private int _linkedCount;

    [ObservableProperty]
    private int _unlinkedCount;

    [ObservableProperty]
    private ObservableCollection<string> _importLogLines = new();

    [ObservableProperty]
    private ObservableCollection<UnlinkedPersonInfo> _unlinkedPersons = new();

    public bool HasImportResult => FamilyImportedCount > 0 || PersonImportedCount > 0;

    public bool HasUnlinkedPersons => UnlinkedPersons.Count > 0;

    #endregion

    public DataImportViewModel(
        IServiceProvider serviceProvider,
        IImportServiceManager importServiceManager,
        ILoggerService logger,
        IDialogService dialogService)
    {
        _serviceProvider = serviceProvider;
        _importServiceManager = importServiceManager;
        _logger = logger;
        _dialogService = dialogService;

        Title = "数据导入";

        LoadImportTypes();
    }

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    /// <summary>
    /// [PERF-PROBE] 阶段0归因埋点：本页无进入期数据加载，采样应恒为 0ms 左右——
    /// 若"进入缓慢"仍复现，则延迟必然在 push 前后（见 日志中的 导航-页面构造 / 导航-PushAsync），而非 OnAppearing。
    /// </summary>
    public override async Task OnAppearingAsync()
    {
        var probe = Stopwatch.StartNew();
        await base.OnAppearingAsync();
        probe.Stop();
        _logger.LogPerf("数据导入-OnAppearing", probe.Elapsed.TotalMilliseconds);
    }

    /// <summary>页面可见后异步加载（C 组：与其余子页一致，不阻塞 PushAsync）</summary>
    public void StartLoadingInBackground() => SafeFireAndForget(async () =>
    {
        await OnAppearingAsync();
    }, nameof(StartLoadingInBackground));

    private void LoadImportTypes()
    {
        _importTypeMapping = _importServiceManager.GetImportTypes();
        ImportTypeNames.Clear();
        foreach (var name in _importTypeMapping.Values)
        {
            ImportTypeNames.Add(name);
        }
        if (ImportTypeNames.Count > 0)
        {
            SelectedImportTypeName = ImportTypeNames[0];
        }
    }

    partial void OnSelectedImportTypeNameChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedImportTypeKey));
        OnPropertyChanged(nameof(IsCombinedImportMode));
        OnPropertyChanged(nameof(IsSingleFileImportMode));
        OnPropertyChanged(nameof(ShowMatchedFiles));
        OnPropertyChanged(nameof(ShowMatchError));
        OnPropertyChanged(nameof(CanStartImport));

        ImportModeHint = IsCombinedImportMode
            ? "整合导入：自动识别家庭人员文件配对"
            : "单文件导入：批量导入文件夹中的所有Excel文件";

        ClearPreview();
        ClearMatchedFiles();
    }

    partial void OnSelectedFilesChanged(ObservableCollection<SelectedFileInfo> value)
    {
        OnPropertyChanged(nameof(HasSelectedFiles));
        OnPropertyChanged(nameof(CanStartImport));
    }

    [RelayCommand]
    private async Task SelectFolderAsync()
    {
        try
        {
            var folderPath = await PickExportFolderAsync("选择导入文件夹", cancelMessage: "已取消选择");

            if (!string.IsNullOrEmpty(folderPath))
            {
                SelectedFolderPath = folderPath;
                SelectedFiles.Clear();

                var files = await _importServiceManager.GetExcelFilesAsync(folderPath);
                foreach (var file in files)
                {
                    SelectedFiles.Add(file);
                }

                OnPropertyChanged(nameof(HasSelectedFiles));

                AutoMatchFiles();

                _logger.Info("已选择文件夹");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("选择文件夹失败");
            ErrorMessage = $"选择文件夹失败: {ex.Message}";
        }
    }

    private void AutoMatchFiles()
    {
        if (string.IsNullOrEmpty(SelectedImportTypeKey) || string.IsNullOrEmpty(SelectedFolderPath))
        {
            ClearMatchedFiles();
            return;
        }

        var (familyPath, personPath) = _importServiceManager.FindMatchingFiles(SelectedImportTypeKey, SelectedFolderPath);

        FamilyFileName = string.IsNullOrEmpty(familyPath) ? "" : Path.GetFileName(familyPath);
        PersonFileName = string.IsNullOrEmpty(personPath) ? "" : Path.GetFileName(personPath);

        OnPropertyChanged(nameof(HasMatchedFiles));
        OnPropertyChanged(nameof(ShowMatchedFiles));
        OnPropertyChanged(nameof(ShowMatchError));
        OnPropertyChanged(nameof(CanStartImport));

        if (!HasMatchedFiles)
        {
            var patterns = ImportTypeCodes.GetFilePatterns(SelectedImportTypeKey);
            _logger.Warn("文件匹配警告: 未找到匹配文件");
        }
    }

    private void ClearMatchedFiles()
    {
        FamilyFileName = string.Empty;
        PersonFileName = string.Empty;
        OnPropertyChanged(nameof(HasMatchedFiles));
        OnPropertyChanged(nameof(ShowMatchedFiles));
        OnPropertyChanged(nameof(ShowMatchError));
        OnPropertyChanged(nameof(CanStartImport));
    }

    private void ClearPreview()
    {
        HasPreview = false;
        FamilyPreviewRows = 0;
        PersonPreviewRows = 0;
        FamilyColumnMappings.Clear();
        PersonColumnMappings.Clear();
    }

    [RelayCommand]
    private void ClearFiles()
    {
        SelectedFiles.Clear();
        SelectedFolderPath = string.Empty;
        OnPropertyChanged(nameof(HasSelectedFiles));
        ClearMatchedFiles();
        ClearPreview();
        ClearResults();
        _logger.Info("已清空文件列表");
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (string.IsNullOrEmpty(SelectedImportTypeKey))
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择导入类型", "确定");
            return;
        }

        if (!HasMatchedFiles)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择包含家庭和人员文件的文件夹", "确定");
            return;
        }

        IsPreviewLoading = true;
        ClearPreview();

        try
        {
            var (familyPath, personPath) = _importServiceManager.FindMatchingFiles(SelectedImportTypeKey, SelectedFolderPath);

            var result = await _importServiceManager.PreviewCombinedAsync(SelectedImportTypeKey,
                familyPath,
                personPath,
                10);

            if (result.Success)
            {
                FamilyPreviewRows = result.FamilyTotalRows;
                PersonPreviewRows = result.PersonTotalRows;

                FamilyColumnMappings.Clear();
                foreach (var mapping in result.FamilyColumnMappings)
                {
                    FamilyColumnMappings.Add(mapping);
                }

                PersonColumnMappings.Clear();
                foreach (var mapping in result.PersonColumnMappings)
                {
                    PersonColumnMappings.Add(mapping);
                }

                HasPreview = true;
                _logger.Info($"预览完成: 家庭 {FamilyPreviewRows} 行 人员 {PersonPreviewRows} 行");
            }
            else
            {
                await _dialogService.DisplayAlertAsync("预览失败", result.ErrorMessage ?? "未知错误", "确定");
                _logger.Error("预览失败");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("预览异常");
            await _dialogService.DisplayAlertAsync("预览异常", ex.Message, "确定");
        }
        finally
        {
            IsPreviewLoading = false;
        }
    }

    [RelayCommand]
    private async Task StartImportAsync()
    {
        if (string.IsNullOrEmpty(SelectedImportTypeKey))
        {
            await _dialogService.DisplayAlertAsync("提示", "请选择导入类型", "确定");
            return;
        }

        if (!HasSelectedFiles)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择包含Excel文件的文件夹", "确定");
            return;
        }

        if (IsCombinedImportMode && !HasMatchedFiles)
        {
            await _dialogService.DisplayAlertAsync("提示", "未找到匹配的家庭和人员文件，请检查文件命名", "确定");
            return;
        }

        if (ClearBeforeImport)
        {
            var confirm = await _dialogService.DisplayAlertAsync("确认",
                "导入前将清空数据表，确定继续吗？", "确定", "取消");
            if (!confirm) return;
        }

        IsImporting = true;
        ImportProgress = 0;
        ImportLogLines.Clear();
        FamilyImportedCount = 0;
        PersonImportedCount = 0;
        LinkedCount = 0;
        UnlinkedCount = 0;
        UnlinkedPersons.Clear();
        ErrorMessage = null;

        var progress = new Progress<string>(msg =>
        {
            ImportStatusMessage = msg;
            ImportLogLines.Add(msg);
        });
        var progressReporter = (IProgress<string>)progress;

        try
        {
            ImportStatusMessage = "开始导入...";
            _logger.Info($"开始导入 {SelectedImportTypeName}");

            if (IsCombinedImportMode)
            {
                await ExecuteCombinedImportAsync(progressReporter);
            }
            else if (IsSingleFileImportMode)
            {
                await ExecuteSingleFileImportAsync(progressReporter);
            }
        }
        catch (OperationCanceledException)
        {
            ImportStatusMessage = "导入已取消";
            _logger.Warn("导入已取消");
        }
        catch (Exception ex)
        {
            ImportStatusMessage = "导入失败";
            ImportResultMessage = $"导入失败: {ex.Message}";
            ImportLogLines.Add($"[错误] {ex.Message}");
            _logger.Error($"导入失败: {ex.Message}");
        }
        finally
        {
            IsImporting = false;
            OnPropertyChanged(nameof(HasImportResult));
            OnPropertyChanged(nameof(HasUnlinkedPersons));

            if (FamilyImportedCount > 0 || PersonImportedCount > 0)
            {
                SelectedFiles.Clear();
                SelectedFolderPath = string.Empty;
                OnPropertyChanged(nameof(HasSelectedFiles));
                ClearMatchedFiles();
                ClearPreview();
            }
        }

        _logger.Info($"导入完成: 类型={SelectedImportTypeName} "
            + $"模式={(IsCombinedImportMode ? "整合" : "单文件")} "
            + $"家庭={FamilyImportedCount} 人员={PersonImportedCount} "
            + $"关联={LinkedCount} 未关联={UnlinkedCount}");
    }

    private async Task ExecuteCombinedImportAsync(IProgress<string> progress)
    {
        var result = await _importServiceManager.ImportFromFolderAsync(SelectedImportTypeKey,
            SelectedFolderPath,
            ClearBeforeImport,
            progress,
            CancellationToken);

        FamilyImportedCount = result.FamilyImportedCount;
        PersonImportedCount = result.PersonImportedCount;
        LinkedCount = result.LinkedCount;
        UnlinkedCount = result.UnlinkedCount;
        ImportProgress = 1.0;

        foreach (var person in result.UnlinkedPersons)
        {
            UnlinkedPersons.Add(person);
        }

        if (result.Success)
        {
            ImportStatusMessage = $"导入完成: 家庭 {FamilyImportedCount} 户 人员 {PersonImportedCount} 人";
            ImportResultMessage = $"导入成功！家庭 {FamilyImportedCount} 条，人员 {PersonImportedCount} 条，关联 {LinkedCount} 条";

            if (UnlinkedCount > 0)
            {
                ImportResultMessage += $"，未关联 {UnlinkedCount} 条";
                ImportLogLines.Add($"[警告] 有 {UnlinkedCount} 条人员未关联到家庭，请检查户主身份证号");
            }

            _logger.Info($"整合导入成功: 家庭 {FamilyImportedCount}");
        }
        else
        {
            ImportStatusMessage = "导入失败";
            ImportResultMessage = $"导入失败: {result.ErrorMessage}";

            foreach (var error in result.Errors)
            {
                ImportLogLines.Add($"[错误] {error}");
            }

            _logger.Error($"导入失败: {result.ErrorMessage}");
        }
    }

    private async Task ExecuteSingleFileImportAsync(IProgress<string> progress)
    {
        var filePaths = SelectedFiles.Select(f => f.FilePath).ToList();

        var result = await _importServiceManager.ImportSingleFilesAsync(SelectedImportTypeKey,
            filePaths,
            ClearBeforeImport,
            progress,
            CancellationToken);

        PersonImportedCount = result.ImportedCount;
        ImportProgress = 1.0;

        if (result.Success)
        {
            ImportStatusMessage = $"导入完成: 共 {result.ImportedCount} 条";
            ImportResultMessage = $"导入成功！共 {result.ImportedCount} 条";

            if (result.Warnings.Count > 0)
            {
                foreach (var warning in result.Warnings.Take(5))
                {
                    ImportLogLines.Add($"[警告] {warning}");
                }
            }

            _logger.Info($"单文件导入成功: {result.ImportedCount} 条");
        }
        else
        {
            ImportStatusMessage = "导入失败";
            ImportResultMessage = $"导入失败: {result.Message}";

            foreach (var error in result.Errors)
            {
                ImportLogLines.Add($"[错误] {error}");
            }

            _logger.Error($"导入失败: {result.Message}");
        }
    }

    [RelayCommand]
    private async Task ExportErrorLogAsync()
    {
        if (ImportLogLines.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "没有可导出的日志", "确定");
            return;
        }

        try
        {
            var logLines = ImportLogLines.ToList();
            var tempPath = await _importServiceManager.ExportImportLogAsync(SelectedImportTypeName, logLines);

            await _dialogService.DisplayAlertAsync("导出成功", $"日志已导出到:\n{tempPath}", "确定");
            _logger.Info($"日志导出成功: {tempPath}");
        }
        catch (Exception ex)
        {
            _logger.Error($"导出日志失败: {ex.Message}");
            await _dialogService.DisplayAlertAsync("导出失败", ex.Message, "确定");
        }
    }

    [RelayCommand]
    private async Task ShowUnlinkedDetailAsync()
    {
        if (UnlinkedPersons.Count == 0) return;

        var details = UnlinkedPersons.Take(10)
            .Select((p, i) => $"{i + 1}. {p.Name} ({p.IdCard})");

        var message = $"未关联人员列表（共 {UnlinkedPersons.Count} 条，显示前10条）:\n\n" +
                      string.Join("\n", details);

        if (UnlinkedPersons.Count > 10)
        {
            message += $"\n\n... 还有 {UnlinkedPersons.Count - 10} 条";
        }

        await _dialogService.DisplayAlertAsync("未关联人员详情", message, "确定");
    }

    [RelayCommand]
    private void ClearResults()
    {
        FamilyImportedCount = 0;
        PersonImportedCount = 0;
        LinkedCount = 0;
        UnlinkedCount = 0;
        UnlinkedPersons.Clear();
        ImportLogLines.Clear();
        ImportResultMessage = null;
        ImportProgress = 0;
        ImportStatusMessage = "等待开始导入...";
        OnPropertyChanged(nameof(HasImportResult));
        OnPropertyChanged(nameof(HasUnlinkedPersons));
    }
}