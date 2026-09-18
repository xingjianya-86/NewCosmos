using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using System.Text.RegularExpressions;

namespace NewCosmos.Services.Import;

public abstract class BaseImportService : BaseService, IImportService
{
    protected readonly IDatabaseService DatabaseService;
    protected int BatchSize { get; set; } = 500;
    protected int ProgressReportInterval { get; set; } = 100;

    protected override string ServiceName => ImportTypeName;

    public abstract string ImportTypeName { get; }
    public virtual string[] FileNamePatterns => Array.Empty<string>();

    protected BaseImportService(IDatabaseService databaseService, ILoggerService logger) : base(logger)
    {
        DatabaseService = databaseService;
    }

    public virtual Task<ImportPreviewResult> PreviewAsync(string filePath, int previewRows = 10, CancellationToken ct = default)
    {
        return Task.FromResult(new ImportPreviewResult { FilePath = filePath, ErrorMessage = "预览功能未实现" });
    }

    public virtual Task<Result> ClearTableAsync(CancellationToken ct = default)
    {
        return Task.FromResult(Result.Failure("NOT_IMPLEMENTED", "清空功能未实现"));
    }

    public virtual async Task<ImportResult> ImportAsync(IEnumerable<string> filePaths,  bool clearBeforeImport, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (clearBeforeImport)
        {
            var clearResult = await ClearTableAsync(ct);
            if (clearResult.IsFailure)
            {
                return ImportResult.Failed("执行清空操作失败");
            }
        }
        return await ImportAsync(filePaths, progress, ct);
    }

    public virtual async Task<ImportResult> ImportAsync(IEnumerable<string> filePaths, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var result = new ImportResult { ImportType = ImportTypeName };
        var startTime = DateTime.UtcNow;
        var fileList = filePaths.ToList();

        try
        {
            LogInfo($"开始批量导入{fileList.Count}个文件");

            for (var i = 0; i < fileList.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var filePath = fileList[i];
                progress?.Report($"正在处理文件({i + 1}/{fileList.Count})");

                var fileResult = await ImportSingleFileAsync(filePath, progress!, ct);
                
                result.ImportedCount += fileResult.ImportedCount;
                result.FamilyImportedCount += fileResult.FamilyImportedCount;
                result.PersonImportedCount += fileResult.PersonImportedCount;
                result.ErrorCount += fileResult.ErrorCount;
                result.Errors.AddRange(fileResult.Errors);
                result.Warnings.AddRange(fileResult.Warnings);

                if (!fileResult.Success)
                {
                    LogWarn($"文件导入失败: {Path.GetFileName(filePath)}");
                }
            }

            result.Success = result.ErrorCount == 0;
            result.Duration = DateTime.UtcNow - startTime;

            LogInfo($"批量导入完成: 成功{result.ImportedCount}条, 失败{result.ErrorCount}条, 耗时{result.Duration.TotalSeconds:F2}秒");
            progress?.Report($"导入完成! 成功{result.ImportedCount}条, 失败{result.ErrorCount}条");
        }
        catch (OperationCanceledException)
        {
            LogWarn("导入被用户取消");
            result.Errors.Add("导入已取消");
        }
        catch (Exception ex)
        {
            LogError($"操作失败: {ex.Message}");
            result.Errors.Add($"执行失败: {ex.Message}");
        }

        return result;
    }

    protected virtual async Task<ImportResult> ImportSingleFileAsync(string filePath, IProgress<string> progress = null, CancellationToken ct = default)
    {
        var result = new ImportResult { FilePath = filePath, ImportType = ImportTypeName };
        var startTime = DateTime.UtcNow;

        try
        {
            LogInfo($"开始导入文件: {Path.GetFileName(filePath)}");
            progress?.Report($"正在处理文件: {Path.GetFileName(filePath)}");

            if (!File.Exists(filePath))
            {
                return ImportResult.Failed($"文件不存在: {filePath}");
            }

            using var reader = SheetReaderFactory.Create(filePath);
            if (reader.IsEmpty)
            {
                return ImportResult.Failed("工作表为空");
            }

            await DatabaseService.BeginTransactionAsync();
            try
            {
                await ProcessWorksheetAsync(reader, result, progress, ct);

                if (result.ErrorCount == 0)
                {
                    await DatabaseService.CommitTransactionAsync();
                    result.Success = true;
                }
                else
                {
                    await DatabaseService.RollbackTransactionAsync();
                    LogWarn($"导入发现{result.ErrorCount}个错误，已回滚");
                }
            }
            catch
            {
                await DatabaseService.RollbackTransactionAsync();
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            LogWarn("文件导入被取消");
            result.Errors.Add("导入已取消");
        }
        catch (Exception ex)
        {
            LogError($"操作失败: {ex.Message}");
            result.Errors.Add($"执行失败: {ex.Message}");
        }
        finally
        {
            result.Duration = DateTime.UtcNow - startTime;
        }

        return result;
    }

    protected abstract Task ProcessWorksheetAsync(IExcelSheetReader reader, ImportResult result, IProgress<string>? progress, CancellationToken ct);

    protected static Dictionary<string, int> BuildColumnMapping(IExcelSheetReader reader, Dictionary<string, string[]> columnAliases)
    {
        return BuildColumnMapping(reader, columnAliases, 1);
    }

    protected static Dictionary<string, int> BuildColumnMapping(IExcelSheetReader reader, Dictionary<string, string[]> columnAliases, int headerRow)
    {
        var mapping = new Dictionary<string, int>();
        var colCount = reader.ColumnCount;

        for (var col = 1; col <= colCount; col++)
        {
            var headerRaw = reader.GetCellText(headerRow, col);
            if (string.IsNullOrEmpty(headerRaw)) continue;

            var header = Regex.Replace(headerRaw, @"[\n\r\u0000-\u001F]", "");
            header = Regex.Replace(header, @"[（(][^）)]*[）)]", "");
            header = Regex.Replace(header, @"\s+", " ").Trim();

            foreach (var kvp in columnAliases)
            {
                foreach (var alias in kvp.Value)
                {
                    if (string.Equals(header, alias, StringComparison.OrdinalIgnoreCase))
                    {
                        mapping[kvp.Key] = col;
                        break;
                    }
                }
            }
        }

        return mapping;
    }

    protected static string ReadCellString(IExcelSheetReader reader, int row, Dictionary<string, int> mapping, string fieldName, string defaultValue = "")
    {
        if (!mapping.TryGetValue(fieldName, out var col)) return defaultValue;
        var text = reader.GetCellText(row, col);
        if (string.IsNullOrEmpty(text)) return defaultValue;
        return ImportDataReader.ReadString(new Dictionary<string, int> { [fieldName] = 0 }, new[] { text }, fieldName);
    }

    protected static int ReadInt(IExcelSheetReader reader, int row, Dictionary<string, int> mapping, string fieldName, int defaultValue = 0)
    {
        if (!mapping.TryGetValue(fieldName, out var col)) return defaultValue;
        var text = reader.GetCellText(row, col);
        if (string.IsNullOrEmpty(text)) return defaultValue;
        return ImportDataReader.ReadInt(new Dictionary<string, int> { [fieldName] = 0 }, new[] { text }, fieldName);
    }

    protected static decimal ReadDecimal(IExcelSheetReader reader, int row, Dictionary<string, int> mapping, string fieldName, decimal defaultValue = 0)
    {
        if (!mapping.TryGetValue(fieldName, out var col)) return defaultValue;
        var text = reader.GetCellText(row, col);
        if (string.IsNullOrEmpty(text)) return defaultValue;
        text = text.Replace("，", ",").Replace(" ", "").Trim();
        return ImportDataReader.ReadDecimal(new Dictionary<string, int> { [fieldName] = 0 }, new[] { text }, fieldName);
    }

    protected static DateTime? ReadDate(IExcelSheetReader reader, int row, Dictionary<string, int> mapping, string fieldName)
    {
        if (!mapping.TryGetValue(fieldName, out var col)) return null;
        var text = reader.GetCellText(row, col);
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length == 6 && int.TryParse(text, out var yyyymm))
        {
            var year = yyyymm / 100;
            var month = yyyymm % 100;
            if (month >= 1 && month <= 12) return new DateTime(year, month, 1);
        }
        return ImportDataReader.ReadDate(new Dictionary<string, int> { [fieldName] = 0 }, new[] { text }, fieldName);
    }
}
