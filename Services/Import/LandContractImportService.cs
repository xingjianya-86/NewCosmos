using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Helpers;
using System.Text;
using System.Text.RegularExpressions;

namespace NewCosmos.Services.Import;

public partial class LandContractImportService : BaseImportService
{
    protected override string ServiceName => ImportTypeName;
    public override string ImportTypeName => ImportTypeCodes.LAND_CONTRACT;

    private int _dataYear;

    private static readonly char[] ColonSeparators = { '：', ':' };

    private readonly IPinyinConverter _pinyinConverter;

    #region 数据模型

    private sealed class ContractorData
    {
        public string Name { get; set; } = "";
        public string Employer { get; set; } = "";
        public decimal TotalContractArea { get; set; }
        public decimal TotalMeasuredArea { get; set; }
        public string Pinyin { get; set; } = "";
        public int SourceRow { get; set; }
        public int Id { get; set; }
        public List<PlotData> Plots { get; set; } = new();
    }

    private sealed class PlotData
    {
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";
        public string East { get; set; } = "";
        public string South { get; set; } = "";
        public string West { get; set; } = "";
        public string North { get; set; } = "";
        public decimal ContractArea { get; set; }
        public decimal MeasuredArea { get; set; }
        public int SourceRow { get; set; }
        public int ContractorId { get; set; }
    }

    #endregion

    public LandContractImportService(
        IDatabaseService databaseService,
        IPinyinConverter pinyinConverter,
        ILoggerService logger) : base(databaseService, logger)
    {
        _pinyinConverter = pinyinConverter;
    }

    #region 列映射（固定位置）
    private static readonly Dictionary<string, int> ColumnMapping = new()
    {
        ["contractor_name"] = 2,
        ["total_contract_area"] = 3,
        ["total_measured_area"] = 4,
        ["plot_name"] = 5,
        ["plot_code"] = 6,
        ["location_east"] = 8,
        ["location_south"] = 9,
        ["location_west"] = 10,
        ["location_north"] = 11,
        ["plot_contract_area"] = 12,
        ["plot_measured_area"] = 13
    };

    #endregion

    #region 正则

    [GeneratedRegex(@"^[\u4e00-\u9fa5]{2,6}$")]
    private static partial Regex ChineseNameRegex();

    #endregion

    #region 主流程
    public override async Task<Result> ClearTableAsync(CancellationToken ct = default)
    {
        try
        {
            await DatabaseService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_biz_land_contract_plot RESTART IDENTITY CASCADE", ct);
            await DatabaseService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_biz_land_contract_contractor RESTART IDENTITY CASCADE", ct);
            LogInfo("清空表: nc_biz_land_contract_contractor");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogError($"清空失败: {ex.Message}");
            return Result.Failure("CLEAR_TABLE_FAILED", $"执行失败: {ex.Message}");
        }
    }

    protected override async Task<ImportResult> ImportSingleFileAsync(string filePath, IProgress<string> progress = null, CancellationToken ct = default)
    {
        _dataYear = DateTime.Now.Year;

        var yearMatch = YearRegex().Match(Path.GetFileName(filePath));
        if (yearMatch.Success && int.TryParse(yearMatch.Value, out var year))
        {
            _dataYear = year;
        }

        return await base.ImportSingleFileAsync(filePath, progress, ct);
    }

    [GeneratedRegex(@"20\d{2}")]
    private static partial Regex YearRegex();

    protected override async Task ProcessWorksheetAsync(IExcelSheetReader reader, ImportResult result, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("开始解析...");

        var (contractors, parseErrors) = ParseWorksheetData(reader, progress!, ct);

        if (parseErrors.Count > 0)
        {
            var errorReport = new StringBuilder();
            errorReport.AppendLine($"解析完成，验证结果:");
            errorReport.AppendLine($"  有效承包方: {contractors.Count} 个");
            errorReport.AppendLine($"  有效地块: {contractors.Sum(c => c.Plots.Count)} 个");
            errorReport.AppendLine($"  验证失败: {parseErrors.Count} 个");

            foreach (var error in parseErrors)
                errorReport.AppendLine(error);

            progress?.Report(errorReport.ToString());

            if (contractors.Count == 0)
            {
                result.Errors.AddRange(parseErrors);
                return;
            }
        }

        if (contractors.Count == 0)
        {
            result.Errors.Add("未找到有效数据");
            return;
        }

        LogInfo($"解析完成: {contractors.Count} 个承包方, {contractors.Sum(c => c.Plots.Count)} 个地块");

        var contractorBatchSize = Math.Min(BatchSize, 50);
        var totalBatches = (int)Math.Ceiling(contractors.Count / (double)contractorBatchSize);
        var successCount = 0;
        var errorCount = 0;

        for (var batch = 0; batch < totalBatches; batch++)
        {
            ct.ThrowIfCancellationRequested();

            progress?.Report($"正在处理第 {batch + 1}/{totalBatches} 批...");

            var batchContractors = contractors.Skip(batch * contractorBatchSize).Take(contractorBatchSize).ToList();

            try
            {
                var (contrCount, plotCount) = await ProcessBatchAsync(batchContractors, ct);
                successCount += contrCount;
            }
            catch (Exception ex)
            {
                LogError($"处理失败: {ex.Message}");
                result.Errors.Add($"执行数据库操作失败: {ex.Message}");
                errorCount++;
                throw;
            }
        }

        result.FamilyImportedCount = successCount;
        result.ImportedCount = successCount;
        result.ErrorCount = errorCount;

        var finalReport = new StringBuilder();
        finalReport.AppendLine($"导入结果:");
        finalReport.AppendLine($"  成功承包方: {successCount} 个");
        if (parseErrors.Count > 0)
            finalReport.AppendLine($"  验证失败数: {parseErrors.Count} 个");

        progress?.Report(finalReport.ToString());
        LogInfo($"导入承包方完成: 成功 {successCount}");
    }

    #endregion

    #region 数据解析

    private (List<ContractorData> contractors, List<string> errors) ParseWorksheetData(
        IExcelSheetReader reader, IProgress<string> progress, CancellationToken ct)
    {
        var contractors = new List<ContractorData>();
        var errors = new List<string>();
        var rowCount = reader.RowCount;

        if (rowCount == 0)
        {
            errors.Add("文件为空");
            return (contractors, errors);
        }

        var employerName = ExtractEmployerName(reader);
        LogInfo($"开始解析数据，单位: {employerName}");

        var headerRow = FindHeaderRow(reader);
        var startRow = headerRow + 1;

        ContractorData? currentContractor = null;
        var plotCodeSet = new HashSet<string>();

        for (var row = startRow; row <= rowCount; row++)
        {
            if (ct.IsCancellationRequested)
                break;

            if (IsRowEmpty(reader, row))
                continue;

            if (row % ProgressReportInterval == 0)
                progress?.Report($"正在处理第 {row}/{rowCount} 行...");

            var contractorName = GetMergedCellValue(reader, row, ColumnMapping.GetValueOrDefault("contractor_name", 2));
            var plotCode = GetMergedCellValue(reader, row, ColumnMapping.GetValueOrDefault("plot_code", 6));

            contractorName = contractorName.Trim();
            plotCode = plotCode.Trim();

            var hasContractorName = !string.IsNullOrWhiteSpace(contractorName) &&
                                    !contractorName.Contains("承包方") &&
                                    !contractorName.Contains("合计") &&
                                    !contractorName.Contains("总计") &&
                                    !contractorName.Contains("序号");

            var hasPlotCode = !string.IsNullOrWhiteSpace(plotCode);

            if (hasContractorName)
            {
                if (!ChineseNameRegex().IsMatch(contractorName))
                {
                    errors.Add(FormatValidationError(row, contractorName, employerName,
                        "承包方姓名格式错", "承包方姓名应为2-6个汉字"));
                    currentContractor = null;
                    continue;
                }

                if (!ValidateEmployerName(employerName))
                {
                    errors.Add(FormatValidationError(row, contractorName, employerName,
                        "发包方格式错误", $"发包方名称必须有镇村信息，当前: {employerName}"));
                    currentContractor = null;
                    continue;
                }

                var contractArea = GetDecimalValue(reader, row, "total_contract_area");
                if (!ValidateArea(contractArea))
                {
                    errors.Add(FormatValidationError(row, contractorName, employerName,
                        "合同面积错误", $"合同面积不能为负数: {contractArea}"));
                    currentContractor = null;
                    continue;
                }

                var measuredArea = GetDecimalValue(reader, row, "total_measured_area");
                if (!ValidateArea(measuredArea))
                {
                    errors.Add(FormatValidationError(row, contractorName, employerName,
                        "实测面积错误", $"实测面积不能为负数: {measuredArea}"));
                    currentContractor = null;
                    continue;
                }

                currentContractor = new ContractorData
                {
                    Name = contractorName,
                    Employer = employerName,
                    Pinyin = _pinyinConverter.ToPinyin(contractorName),
                    TotalContractArea = contractArea,
                    TotalMeasuredArea = measuredArea,
                    SourceRow = row
                };
                contractors.Add(currentContractor);
                plotCodeSet.Clear();
            }

            if (hasPlotCode && currentContractor != null)
            {
                if (plotCodeSet.Contains(plotCode))
                    continue;

                plotCodeSet.Add(plotCode);

                var plotName = GetMergedCellValue(reader, row, ColumnMapping.GetValueOrDefault("plot_name", 5));
                if (string.IsNullOrWhiteSpace(plotName))
                {
                    errors.Add(FormatPlotValidationError(row, plotCode, "未知",
                        "地块名为空", "地块名和地块号都为空"));
                    continue;
                }

                var plotContractArea = GetDecimalValue(reader, row, "plot_contract_area");
                if (!ValidateArea(plotContractArea))
                {
                    errors.Add(FormatPlotValidationError(row, plotCode, plotName,
                        "合同面积错", $"地块合同面积不能为零: {plotContractArea}"));
                    continue;
                }

                var plotMeasuredArea = GetDecimalValue(reader, row, "plot_measured_area");
                if (!ValidateArea(plotMeasuredArea))
                {
                    errors.Add(FormatPlotValidationError(row, plotCode, plotName,
                        "实测面积错", $"地块实测面积不能为零: {plotMeasuredArea}"));
                    continue;
                }

                currentContractor.Plots.Add(new PlotData
                {
                    Name = plotName,
                    Code = plotCode,
                    East = GetMergedCellValue(reader, row, ColumnMapping.GetValueOrDefault("location_east", 8)),
                    South = GetMergedCellValue(reader, row, ColumnMapping.GetValueOrDefault("location_south", 9)),
                    West = GetMergedCellValue(reader, row, ColumnMapping.GetValueOrDefault("location_west", 10)),
                    North = GetMergedCellValue(reader, row, ColumnMapping.GetValueOrDefault("location_north", 11)),
                    ContractArea = plotContractArea,
                    MeasuredArea = plotMeasuredArea,
                    SourceRow = row
                });
            }
        }

        if (errors.Count > 0)
        {
            LogWarn($"解析完成，共 {errors.Count} 个验证失败");
        }

        return (contractors, errors);
    }

    #endregion

    #region 批量入库

    private async Task<(int contractorCount, int plotCount)> ProcessBatchAsync(List<ContractorData> batchContractors, CancellationToken ct)
    {
        var contractorCount = 0;
        var plotsToInsert = new List<PlotData>();

        foreach (var contractor in batchContractors)
        {
            var id = await InsertContractorAsync(contractor, ct);

            if (id <= 0)
            {
                throw new InvalidOperationException(
                    FormatContractorError(contractor, "承包方保存失败，未获得有效ID"));
            }

            contractor.Id = id;
            contractorCount++;

            foreach (var plot in contractor.Plots)
            {
                plot.ContractorId = id;
                plotsToInsert.Add(plot);
            }
        }

        var plotCount = 0;
        if (plotsToInsert.Count > 0)
        {
            await BulkInsertPlotsAsync(plotsToInsert, ct);
            plotCount = plotsToInsert.Count;
        }

        return (contractorCount, plotCount);
    }

    private async Task<int> InsertContractorAsync(ContractorData contractor, CancellationToken ct)
    {
        var sql = @"
            INSERT INTO nc_biz_land_contract_contractor 
            (contractor_name, contractor_pinyin, employer_name, total_contract_area, total_measured_area, data_year, imported_at)
            VALUES ($1, $2, $3, $4, $5, $6, CURRENT_TIMESTAMP)
            RETURNING id";

        await DatabaseService.ExecuteNonQueryAsync(
            "DELETE FROM nc_biz_land_contract_contractor WHERE contractor_name = $1 AND employer_name = $2 AND data_year = $3",
            ct, contractor.Name, contractor.Employer, _dataYear);
        var result = await DatabaseService.ExecuteScalarAsync(sql, ct,
            contractor.Name, contractor.Pinyin, contractor.Employer,
            contractor.TotalContractArea, contractor.TotalMeasuredArea, _dataYear);

        return result.IsSuccess && result.Value > 0 ? (int)result.Value : -1;
    }

    private async Task BulkInsertPlotsAsync(List<PlotData> plots, CancellationToken ct)
    {
        if (plots.Count == 0) return;

        var sqlBuilder = new StringBuilder();
        sqlBuilder.Append("INSERT INTO nc_biz_land_contract_plot ");
        sqlBuilder.Append("(contractor_id, plot_name, plot_code, location_east, location_south, location_west, location_north, ");
        sqlBuilder.AppendLine("contract_area, measured_area) VALUES ");

        var totalBatches = (int)Math.Ceiling(plots.Count / (double)BatchSize);

        for (var batch = 0; batch < totalBatches; batch++)
        {
            var batchPlots = plots.Skip(batch * BatchSize).Take(BatchSize).ToList();
            var paramValues = new List<object>();
            var valueClauses = new List<string>();

            for (var i = 0; i < batchPlots.Count; i++)
            {
                var p = batchPlots[i];
                var offset = i * 9;
                valueClauses.Add($"(${offset + 1}, ${offset + 2}, ${offset + 3}, ${offset + 4}, ${offset + 5}, ${offset + 6}, ${offset + 7}, ${offset + 8}, ${offset + 9})");
                paramValues.Add(p.ContractorId);
                paramValues.Add(p.Name ?? "");
                paramValues.Add(p.Code ?? "");
                paramValues.Add(p.East ?? "");
                paramValues.Add(p.South ?? "");
                paramValues.Add(p.West ?? "");
                paramValues.Add(p.North ?? "");
                paramValues.Add(p.ContractArea);
                paramValues.Add(p.MeasuredArea);
            }

            var sql = sqlBuilder.ToString() + string.Join(", ", valueClauses);
            await DatabaseService.ExecuteNonQueryAsync(sql, ct, paramValues.ToArray());
        }
    }

    #endregion

    #region 辅助方法

    private static int FindHeaderRow(IExcelSheetReader reader)
    {
        var rowCount = reader.RowCount;
        var colCount = reader.ColumnCount;

        for (var r = 1; r <= Math.Min(10, rowCount); r++)
        {
            for (var c = 1; c <= Math.Min(30, colCount); c++)
            {
                var val = reader.GetCellText(r, c);
                if (string.IsNullOrEmpty(val)) continue;
                if (val.Contains("承包方") || val.Contains("地块名称") || val.Contains("地块代码"))
                    return r;
            }
        }

        return 6;
    }

    private static bool IsRowEmpty(IExcelSheetReader reader, int row)
    {
        for (var c = 1; c <= Math.Min(15, reader.ColumnCount); c++)
        {
            if (!string.IsNullOrWhiteSpace(reader.GetCellText(row, c)))
                return false;
        }
        return true;
    }

    private static string GetMergedCellValue(IExcelSheetReader reader, int row, int col)
    {
        return reader.GetMergedCellText(row, col);
    }

    private static decimal GetDecimalValue(IExcelSheetReader reader, int row, string key)
    {
        if (ColumnMapping.TryGetValue(key, out var colIndex))
        {
            var val = GetMergedCellValue(reader, row, colIndex);
            if (decimal.TryParse(val, out var result))
                return result;
        }
        return 0;
    }

    private string ExtractEmployerName(IExcelSheetReader reader)
    {
        for (var r = 1; r <= Math.Min(5, reader.RowCount); r++)
        {
            for (var c = 1; c <= Math.Min(10, reader.ColumnCount); c++)
            {
                var val = reader.GetMergedCellText(r, c);
                if (string.IsNullOrEmpty(val)) continue;
                if (!val.StartsWith("发包")) continue;

                if (val.Contains('：') || val.Contains(':'))
                {
                    var parts = val.Split(ColonSeparators, 2);
                    if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                        return NormalizeEmployerName(parts[1].Trim());
                }

                var nextVal = reader.GetMergedCellText(r, c + 1);
                if (!string.IsNullOrWhiteSpace(nextVal) && !nextVal.Contains("发包") && !nextVal.Contains("承包方"))
                    return NormalizeEmployerName(nextVal);
            }
        }

        return "";
    }

    private string NormalizeEmployerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        var result = name;

        var townIdx = -1;
        for (var i = 0; i < result.Length; i++)
        {
            if (result[i] == '镇' || result[i] == '乡')
            {
                townIdx = i;
                break;
            }
        }

        if (townIdx > 0)
        {
            var townStartIdx = townIdx;
            for (var i = townIdx - 1; i >= 0; i--)
            {
                var c = result[i];
                if (c == '市' || c == '县' || c == '区')
                {
                    townStartIdx = i + 1;
                    break;
                }
            }
            result = result.Substring(townStartIdx);
        }

        var villageIdx = result.IndexOf('村');
        if (villageIdx >= 0)
        {
            result = result.Substring(0, villageIdx + 1);
        }
        else
        {
            var communityIdx = result.IndexOf("社区");
            if (communityIdx >= 0)
            {
                result = result.Substring(0, communityIdx + 2);
            }
        }

        return result;
    }

    private static bool ValidateEmployerName(string employerName)
    {
        if (string.IsNullOrWhiteSpace(employerName))
            return false;

        return (employerName.Contains("镇") || employerName.Contains("乡")) && employerName.Contains("村");
    }

    private static bool ValidateArea(decimal area)
    {
        return area >= 0;
    }

    private static string FormatValidationError(int row, string contractorName, string employerName, string errorType, string detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"行号: {row}");
        sb.AppendLine($"承包方: {contractorName}");
        sb.AppendLine($"单位: {employerName}");
        sb.AppendLine($"错误: {errorType}");
        sb.AppendLine($"详细信息: {detail}");
        return sb.ToString();
    }

    private static string FormatPlotValidationError(int row, string plotCode, string plotName, string errorType, string detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"行号: {row}");
        sb.AppendLine($"地块号: {plotCode}");
        sb.AppendLine($"地块名: {plotName}");
        sb.AppendLine($"错误: {errorType}");
        sb.AppendLine($"详细信息: {detail}");
        return sb.ToString();
    }

    private static string FormatContractorError(ContractorData contractor, string errorMessage)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"承包方: {contractor.Name}");
        sb.AppendLine($"单位: {contractor.Employer}");
        sb.AppendLine($"拼音: {contractor.Pinyin}");
        sb.AppendLine($"  合同面积: {contractor.TotalContractArea} 亩");
        sb.AppendLine($"  实测面积: {contractor.TotalMeasuredArea} 亩");
        sb.AppendLine($"错误信息: {errorMessage}");
        return sb.ToString();
    }

    #endregion
}
