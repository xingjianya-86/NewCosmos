using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using System.Text.RegularExpressions;

namespace NewCosmos.Services.Import;

public partial class AgriculturalSubsidyImportService : BaseImportService
{
    protected override string ServiceName => ImportTypeName;
    public override string ImportTypeName => ImportTypeCodes.AGRICULTURAL_SUBSIDY;

    private string _subsidyType = string.Empty;
    private int _dataYear;

    private static readonly Dictionary<string, string[]> ColumnAliases = new()
    {
        ["name"] = new[] { "收款人全称" },
        ["id_card"] = new[] { "收款人身份证号" },
        ["phone"] = new[] { "联系电话", "收款人手机号" },
        ["subsidy_amount"] = new[] { "最终确定发放该人补贴总金额" },
        ["address"] = new[] { "收款人家庭地址" },
        ["account_number"] = new[] { "银行账号" },
        ["bank_name"] = new[] { "银行名称" },
        ["bank_branch"] = new[] { "开户网点", "开户银行网点名称" },
    };

    public AgriculturalSubsidyImportService(IDatabaseService databaseService, ILoggerService logger) : base(databaseService, logger)
    {
    }

    public override async Task<Result> ClearTableAsync(CancellationToken ct = default)
    {
        try
        {
            await DatabaseService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_biz_soil_subsidy RESTART IDENTITY CASCADE", ct);
            LogInfo("执行清空操作成功");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogError($"操作失败: {ex.Message}");
            return Result.Failure("CLEAR_TABLE_FAILED", $"执行清空失败: {ex.Message}");
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
        var mapping = BuildColumnMapping(reader, ColumnAliases);

        LogInfo($"[地力补贴] Excel表头诊断 | 行数={rowCount}, 表头行=1");
        LogColumnInfo(reader, 1, ColumnAliases, mapping);

        if (!mapping.TryGetValue("name", out _) ||
            !mapping.TryGetValue("id_card", out _) ||
            !mapping.TryGetValue("subsidy_amount", out _))
        {
            result.Errors.Add("缺少必要列：姓名、身份证或补贴金额");
            return;
        }

        var successCount = 0;
        var errorCount = 0;

        for (var row = 2; row <= rowCount; row++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var name = ReadCellString(reader, row, mapping, "name");
                var idCard = ReadCellString(reader, row, mapping, "id_card");
                var amountStr = ReadCellString(reader, row, mapping, "subsidy_amount");

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(idCard))
                    continue;

                if (name.Contains("全称") && idCard.Contains("身份证"))
                    continue;

                var subsidyAmount = ParseAmount(amountStr);

                var phone = ReadCellString(reader, row, mapping, "phone");
                var address = NormalizeAddress(ReadCellString(reader, row, mapping, "address"));
                var account = ReadCellString(reader, row, mapping, "account_number");
                var bank = ReadCellString(reader, row, mapping, "bank_name");
                var branch = ReadCellString(reader, row, mapping, "bank_branch");

                var sql = @"
                    INSERT INTO nc_biz_soil_subsidy 
                    (name, id_card, subsidy_amount, phone, address, account_number, bank_name, bank_branch, subsidy_type, data_year, imported_at)
                    VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, CURRENT_TIMESTAMP);
";

                await DatabaseService.ExecuteNonQueryAsync("DELETE FROM nc_biz_soil_subsidy WHERE id_card = $1 AND data_year = $2 AND subsidy_type = $3", ct, idCard, _dataYear, _subsidyType);
                var execResult = await DatabaseService.ExecuteNonQueryAsync(sql, ct,
                    name, idCard, subsidyAmount, phone, address, account, bank, branch, _subsidyType, _dataYear);

                if (execResult.IsSuccess)
                {
                    successCount++;
                }
                else
                {
                    errorCount++;
                    result.Errors.Add($"第{row}行导入失败");
                }

                if (successCount % ProgressReportInterval == 0)
                {
                    progress?.Report($"已处理 {successCount + errorCount} 条");
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                LogError($"第{row}行处理失败: {ex.Message}");
            }
        }

        result.ImportedCount = successCount;
        result.ErrorCount = errorCount;
        progress?.Report($"处理完成，成功{successCount}条，失败{errorCount}条");

        LogInfo($"地力补贴导入完成: 成功{successCount}条，失败{errorCount}条");
    }

    private static decimal ParseAmount(string amountStr)
    {
        if (string.IsNullOrWhiteSpace(amountStr))
            return 0;

        var cleanAmount = AmountRegex().Replace(amountStr, "");
        return decimal.TryParse(cleanAmount, out var result) ? result : 0;
    }

    private static string NormalizeAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return string.Empty;

        return address.Replace("\r", "").Replace("\n", "").Trim();
    }

    [GeneratedRegex(@"20\d{2}")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"[^\d.]")]
    private static partial Regex AmountRegex();

    private void LogColumnInfo(IExcelSheetReader reader, int headerRow,
        Dictionary<string, string[]> aliases, Dictionary<string, int> mapping)
    {
        var actualHeaders = new List<string>();
        for (var c = 1; c <= reader.ColumnCount; c++)
        {
            var text = reader.GetCellText(headerRow, c);
            if (!string.IsNullOrEmpty(text))
                actualHeaders.Add($"[{c}]{text}");
        }
        LogInfo($"  Excel实际表头: {string.Join(" | ", actualHeaders)}");

        foreach (var kvp in aliases)
        {
            var matched = mapping.ContainsKey(kvp.Key);
            var status = matched ? "匹配" : "未匹配";
            LogInfo($"  系统期望: {kvp.Key} -> [{string.Join("/", kvp.Value)}] => {status}");
        }

        LogInfo($"  映射结果: {mapping.Count}/{aliases.Count} 列匹配成功");
    }
}
