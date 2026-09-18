using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using System.Text.RegularExpressions;

namespace NewCosmos.Services.Import;

public partial class RotationSubsidyImportService : BaseImportService
{
    protected override string ServiceName => ImportTypeName;
    public override string ImportTypeName => ImportTypeCodes.ROTATION_SUBSIDY;

    private string _subsidyType = string.Empty;
    private int _dataYear;

    private static readonly Dictionary<string, int> FixedColumnMapping = new()
    {
        ["name"] = 2,
        ["id_card"] = 3,
        ["phone"] = 4,
        ["subsidy_amount"] = 5,
        ["rotation_area"] = 6,
        ["address"] = 7,
        ["account_number"] = 8,
        ["bank_name"] = 9,
        ["bank_branch"] = 10,
        ["remarks"] = 11
    };

    public RotationSubsidyImportService(IDatabaseService databaseService, ILoggerService logger) : base(databaseService, logger)
    {
    }

    public override async Task<Result> ClearTableAsync(CancellationToken ct = default)
    {
        try
        {
            await DatabaseService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_biz_rotation_subsidy RESTART IDENTITY CASCADE", ct);
            LogInfo("执行清空操作成功");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogError($"操作失败: {ex.Message}");
            return Result.Failure("CLEAR_TABLE_FAILED", $"执行清空操作失败: {ex.Message}");
        }
    }

    protected override async Task<ImportResult> ImportSingleFileAsync(string filePath, IProgress<string> progress = null, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(filePath);
        _subsidyType = fileName.Contains("非社保卡") ? "非社保卡" : "社保卡";

        _dataYear = DateTime.Now.Year;
        var yearMatch = YearRegex().Match(fileName);
        if (yearMatch.Success && int.TryParse(yearMatch.Value, out var year))
        {
            _dataYear = year;
        }

        return await base.ImportSingleFileAsync(filePath, progress, ct);
    }

    protected override async Task ProcessWorksheetAsync(IExcelSheetReader reader, ImportResult result, IProgress<string>? progress, CancellationToken ct)
    {
        var rowCount = reader.RowCount;
        var headerRow = FindHeaderRow(reader);

        if (headerRow == -1)
        {
            result.Errors.Add("未找到有效的表头行（需包含姓名和身份证号）");
            return;
        }

        var mapping = FixedColumnMapping;
        var dataStartRow = headerRow + 1;

        LogInfo($"[轮作补贴] Excel表头诊断 | 行数={rowCount}, 表头行={headerRow}, 数据起始行={dataStartRow}");
        LogFixedColumnMapping(reader, headerRow, FixedColumnMapping);

        var successCount = 0;
        var errorCount = 0;

        for (var row = dataStartRow; row <= rowCount; row++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var name = ReadCellString(reader, row, mapping, "name");
                var idCard = ReadCellString(reader, row, mapping, "id_card");

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(idCard))
                    continue;

                if (idCard.Length < 15 || idCard.Contains("身份证"))
                    continue;

                if (name.Contains("全称") || name.Contains("合计"))
                    continue;

                var amountStr = ReadCellString(reader, row, mapping, "subsidy_amount");
                var subsidyAmount = ParseAmount(amountStr);
                var rotationArea = ReadDecimal(reader, row, mapping, "rotation_area");

                var phone = NormalizePhone(ReadCellString(reader, row, mapping, "phone"));
                var address = ReadCellString(reader, row, mapping, "address").Replace("\r", "").Replace("\n", "").Trim();
                var account = ReadCellString(reader, row, mapping, "account_number");
                var bank = ReadCellString(reader, row, mapping, "bank_name");
                var branch = ReadCellString(reader, row, mapping, "bank_branch");
                var remarks = ReadCellString(reader, row, mapping, "remarks");

                var sql = @"
                    INSERT INTO nc_biz_rotation_subsidy 
                    (name, id_card, rotation_area, subsidy_amount, phone, address, account_number, bank_name, bank_branch, subsidy_type, data_year, remarks, imported_at)
                    VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, CURRENT_TIMESTAMP);
                ";

                await DatabaseService.ExecuteNonQueryAsync("DELETE FROM nc_biz_rotation_subsidy WHERE id_card = $1 AND data_year = $2 AND subsidy_type = $3", ct, idCard, _dataYear, _subsidyType);
                var execResult = await DatabaseService.ExecuteNonQueryAsync(sql, ct,
                    name, idCard, rotationArea, subsidyAmount,
                    phone, address, account, bank, branch,
                    _subsidyType, _dataYear, remarks);

                if (execResult.IsSuccess)
                {
                    successCount++;
                }
                else
                {
                    errorCount++;
                    result.Errors.Add("执行插入操作失败");
                }

                if (successCount % ProgressReportInterval == 0)
                {
                    progress?.Report($"已处理 {successCount} 条记录");
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                LogError($"操作失败: {ex.Message}");
            }
        }

        result.ImportedCount = successCount;
        result.ErrorCount = errorCount;
        progress?.Report($"处理完成，成功 {successCount} 条，失败 {errorCount} 条");

        LogInfo($"轮作补贴导入完成: 成功 {successCount} 条，失败 {errorCount} 条");
    }

    private static int FindHeaderRow(IExcelSheetReader reader)
    {
        var rowCount = reader.RowCount;
        var colCount = reader.ColumnCount;

        for (var r = 1; r <= Math.Min(10, rowCount); r++)
        {
            for (var c = 1; c <= colCount; c++)
            {
                var txt = reader.GetCellText(r, c).Replace("\r", "").Replace(" ", "").Trim();
                if ((txt.Contains("姓名") || txt.Contains("全称")) && !txt.Contains("签字"))
                    return r;
            }
        }
        return -1;
    }

    private static decimal ParseAmount(string amountStr)
    {
        if (string.IsNullOrWhiteSpace(amountStr))
            return 0;

        var cleanAmount = AmountRegex().Replace(amountStr, "");
        return decimal.TryParse(cleanAmount, out var result) ? result : 0;
    }

    private static string NormalizePhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        phone = phone.Trim();

        if (phone.Contains('E') || phone.Contains('e'))
        {
            if (double.TryParse(phone, out var d))
                return d.ToString("N2");
        }

        return phone;
    }

    [GeneratedRegex(@"20\d{2}")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"[^\d.]")]
    private static partial Regex AmountRegex();

    private void LogFixedColumnMapping(IExcelSheetReader reader, int headerRow,
        Dictionary<string, int> fixedMapping)
    {
        var actualHeaders = new List<string>();
        for (var c = 1; c <= Math.Min(15, reader.ColumnCount); c++)
        {
            var text = reader.GetCellText(headerRow, c);
            if (!string.IsNullOrEmpty(text))
                actualHeaders.Add($"[{c}]{text}");
        }
        LogInfo($"  Excel实际表头: {string.Join(" | ", actualHeaders)}");

        foreach (var kvp in fixedMapping)
        {
            var actualText = reader.GetCellText(headerRow, kvp.Value);
            LogInfo($"  固定列: {kvp.Key} -> 第{kvp.Value}列, 实际内容=\"{actualText}\"");
        }

        LogInfo($"  固定列映射: {fixedMapping.Count} 个字段");
    }
}
