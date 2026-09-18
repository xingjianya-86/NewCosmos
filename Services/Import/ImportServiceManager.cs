using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Services.Import;

public interface IImportServiceManager
{
    Dictionary<string, string> GetImportTypes();
    Task<CombinedPreviewResult> PreviewCombinedAsync(string importType, string familyFilePath, string personFilePath, int previewRows = 10, CancellationToken ct = default);
    Task<CombinedImportResult> ImportCombinedAsync(string importType, string familyFilePath, string personFilePath,  bool clearBeforeImport, IProgress<string> progress = null, CancellationToken ct = default);
    Task<CombinedImportResult> ImportFromFolderAsync(string importType, string folderPath,  bool clearBeforeImport, IProgress<string> progress = null, CancellationToken ct = default);
    Task<ImportResult> ImportSingleFilesAsync(string importType, IEnumerable<string> filePaths,  bool clearBeforeImport, IProgress<string> progress = null, CancellationToken ct = default);
    ICombinedImportService? GetCombinedService(string importType);
    (string FamilyPath, string PersonPath) FindMatchingFiles(string importType, string folderPath);
    string? DetectImportType(string filePath);
    Task<List<SelectedFileInfo>> GetExcelFilesAsync(string folderPath);
    Task<string> ExportImportLogAsync(string importTypeName, List<string> logLines);
}

public class SelectedFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }

    public string FileSizeText
    {
        get
        {
            if (FileSize < 1024) return $"{FileSize} B";
            if (FileSize < 1024 * 1024) return $"{FileSize / 1024.0:F1} KB";
            return $"{FileSize / 1024.0 / 1024.0:F1} MB";
        }
    }
}

public class ImportServiceManager : BaseService, IImportServiceManager
{
    private static readonly EnumerationOptions _recursiveOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true
    };

    protected override string ServiceName => "ImportServiceManager";

    private readonly Dictionary<string, ICombinedImportService> _combinedServices;
    private readonly IEnumerable<IImportService> _singleFileServices;

    public ImportServiceManager(
        IEnumerable<ICombinedImportService> combinedServices,
        IEnumerable<IImportService> singleFileServices,
        ILoggerService logger) : base(logger)
    {
        _combinedServices = combinedServices.ToDictionary(s => s.ImportTypeName, s => s);
        _singleFileServices = singleFileServices;
        LogInfo($"已注册{_combinedServices.Count}个整合导入服务, {_singleFileServices.Count()}个单文件服务");
    }

    public Dictionary<string, string> GetImportTypes()
    {
        var types = new Dictionary<string, string>();
        foreach (var code in ImportTypeCodes.Implemented)
        {
            types[code] = ImportTypeCodes.GetDisplayName(code);
        }
        return types;
    }

    public async Task<CombinedPreviewResult> PreviewCombinedAsync(string importType, string familyFilePath, string personFilePath, int previewRows = 10, CancellationToken ct = default)
    {
        if (!_combinedServices.TryGetValue(importType, out var service))
        {
            LogError($"操作失败");
            return new CombinedPreviewResult { ErrorMessage = $"不支持的导入类型: {importType}" };
        }

        LogInfo($"预览: {importType}");
        return await service.PreviewCombinedAsync(familyFilePath, personFilePath, previewRows, ct);
    }

    public async Task<CombinedImportResult> ImportCombinedAsync(string importType, string familyFilePath, string personFilePath,  bool clearBeforeImport, IProgress<string> progress = null, CancellationToken ct = default)
    {
        if (!_combinedServices.TryGetValue(importType, out var service))
        {
            LogError($"操作失败");
            return new CombinedImportResult { ErrorMessage = $"不支持的导入类型: {importType}" };
        }

        LogInfo($"执行整合导入: {importType}");
        return await service.ImportCombinedAsync(familyFilePath, personFilePath, clearBeforeImport, progress, ct);
    }

    public async Task<CombinedImportResult> ImportFromFolderAsync(string importType, string folderPath,  bool clearBeforeImport, IProgress<string> progress = null, CancellationToken ct = default)
    {
        if (!_combinedServices.TryGetValue(importType, out var service))
        {
            return new CombinedImportResult { ErrorMessage = $"不支持的导入类型: {importType}" };
        }

        var (familyPath, personPath) = FindMatchingFiles(importType, folderPath);

        if (string.IsNullOrEmpty(familyPath))
        {
            return new CombinedImportResult { ErrorMessage = $"未找到家庭文件，请确保文件夹包含匹配的文件: {service.FamilyFilePattern}" };
        }

        if (string.IsNullOrEmpty(personPath))
        {
            return new CombinedImportResult { ErrorMessage = $"未找到人员文件，请确保文件夹包含匹配的文件: {service.PersonFilePattern}" };
        }

        progress?.Report($"找到文件: 家庭={Path.GetFileName(familyPath)}");
        LogInfo($"从文件夹导入: 家庭={familyPath}");

        return await service.ImportCombinedAsync(familyPath, personPath, clearBeforeImport, progress, ct);
    }

    public ICombinedImportService? GetCombinedService(string importType)
    {
        return _combinedServices.TryGetValue(importType, out var service) ? service : null;
    }

    public (string FamilyPath, string PersonPath) FindMatchingFiles(string importType, string folderPath)
    {
        if (!ImportTypeCodes.IsCombinedType(importType))
        {
            return (string.Empty, string.Empty);
        }

        var patterns = ImportTypeCodes.GetFilePatterns(importType);
        var familyPattern = patterns.FamilyPattern;
        var personPattern = patterns.PersonPattern;

        var familyPath = FindFileByPattern(folderPath, familyPattern);
        var personPath = FindFileByPattern(folderPath, personPattern);

        return (familyPath, personPath);
    }

    private string FindFileByPattern(string folderPath, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return string.Empty;

        var files = Directory.GetFiles(folderPath, pattern, _recursiveOptions);
        return files.FirstOrDefault() ?? string.Empty;
    }

    public string? DetectImportType(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        foreach (var kvp in _combinedServices)
        {
            var service = kvp.Value;
            if (MatchesPattern(fileName, service.FamilyFilePattern) ||
                MatchesPattern(fileName, service.PersonFilePattern))
            {
                return kvp.Key;
            }
        }

        return null;
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;

        pattern = Path.GetFileNameWithoutExtension(pattern);
        if (pattern.Contains('*'))
        {
            var parts = pattern.Split('*');
            if (parts.Length == 2)
            {
                var prefix = parts[0];
                var suffix = parts[1];
                return fileName.StartsWith(prefix) && fileName.EndsWith(suffix);
            }
        }
        return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    public Task<List<SelectedFileInfo>> GetExcelFilesAsync(string folderPath)
    {
        // 文件系统遍历为同步阻塞操作，移出调用线程执行；结果返回 Excel 文件清单
        return Task.Run(() =>
        {
            var files = Directory.GetFiles(folderPath, "*.xlsx", _recursiveOptions)
                .Concat(Directory.GetFiles(folderPath, "*.xls", _recursiveOptions))
                .ToList();

            var result = files.Select(file =>
            {
                var fileInfo = new FileInfo(file);
                var relativePath = file.Substring(folderPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return new SelectedFileInfo
                {
                    FileName = relativePath,
                    FilePath = file,
                    FileSize = fileInfo.Length
                };
            }).ToList();

            return result;
        });
    }

    public async Task<string> ExportImportLogAsync(string importTypeName, List<string> logLines)
    {
        var fileName = $"导入日志_{importTypeName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);

        var lines = new List<string> { "时间,类型,消息" };
        foreach (var line in logLines)
        {
            var parts = line.Split(']', 3);
            if (parts.Length >= 3)
            {
                var time = parts[0].TrimStart('[');
                var type = parts[1].Trim();
                var msg = parts[2].Trim().Replace("\"", "");
                lines.Add($"\"{time}\",\"{type}\",\"{msg}\"");
            }
            else
            {
                lines.Add($"\"\",\"\",\"{line.Replace("\"", "")}\"");
            }
        }

        await File.WriteAllLinesAsync(tempPath, lines);
        LogInfo($"执行操作");
        return tempPath;
    }

    public async Task<ImportResult> ImportSingleFilesAsync(
        string importType,
        IEnumerable<string> filePaths,
        bool clearBeforeImport,
        IProgress<string> progress = null,
        CancellationToken ct = default)
    {
        if (!ImportTypeCodes.IsSingleFileType(importType))
        {
            LogError($"操作失败");
            return ImportResult.Failed($"不支持的导入类型: {importType}");
        }

        var service = _singleFileServices.FirstOrDefault(s => s.ImportTypeName == importType);
        if (service == null)
        {
            LogError($"操作失败");
            return ImportResult.Failed($"未找到导入服务: {importType}");
        }

        var fileList = filePaths.ToList();
        if (fileList.Count == 0)
        {
            return ImportResult.Failed("未选择任何文件");
        }

        LogInfo($"开始单文件批量导入: {importType}");

        var result = new ImportResult();
        var totalImported = 0;
        var totalErrors = 0;

        if (clearBeforeImport)
        {
            progress?.Report("正在清空现有数据...");
            var clearResult = await service.ClearTableAsync(ct);
            if (clearResult.IsFailure)
            {
                LogError($"操作失败");
                return ImportResult.Failed("清空数据失败");
            }
            LogInfo($"已清空现有数据");
        }

        for (var i = 0; i < fileList.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var filePath = fileList[i];
            var fileName = Path.GetFileName(filePath);
            progress?.Report($"正在处理 ({i + 1}/{fileList.Count})");

            try
            {
                var fileResult = await service.ImportAsync(new[] { filePath }, false, progress, ct);

                totalImported += fileResult.ImportedCount;
                totalErrors += fileResult.ErrorCount;

                if (fileResult.ErrorCount > 0)
                {
                    foreach (var error in fileResult.Errors.Take(3))
                    {
                        result.Errors.Add($"文件: {fileName}, 错误: {error}");
                    }
                }

                LogInfo($"文件导入完成: {fileName}");
            }
            catch (Exception ex)
            {
                totalErrors++;
                result.Errors.Add($"文件: {fileName}, 异常: {ex.Message}");
                LogError($"文件导入异常: {fileName}");
            }
        }

        result.ImportedCount = totalImported;
        result.ErrorCount = totalErrors;
        result.Success = totalErrors == 0;

        progress?.Report($"导入完成: 成功 {totalImported} 条, 失败 {totalErrors} 条");
        LogInfo($"单文件批量导入完成: 成功 {totalImported}");

        return result;
    }
}
