using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using System.Text.RegularExpressions;

namespace NewCosmos.Services.Import;

public partial class SoybeanSubsidyImportService : BaseImportService
{
    private static readonly HashSet<string> AllowedTableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "nc_biz_high_oil_soybean_person",
        "nc_biz_high_oil_soybean_detail",
        "nc_biz_high_protein_soybean_person",
        "nc_biz_high_protein_soybean_detail",
    };

    private static void ValidateTableName(string tableName)
    {
        if (!AllowedTableNames.Contains(tableName))
            throw new BusinessException(
                ErrorCodes.VALIDATION_FAILED,
                $"不允许的表名: {tableName}");
    }

    protected override string ServiceName => ImportTypeName;
    public override string ImportTypeName => ImportTypeCodes.SOYBEAN_SUBSIDY;

    private string _dbType = "";
    private string _fileName = "";
    private int _dataYear;

    private static readonly Dictionary<string, string[]> ColumnAliases = new()
    {
        ["name"] = new[] { "姓名" },
        ["id_card"] = new[] { "身份证号" },
        ["phone"] = new[] { "联系电话" },
        ["total_area"] = new[] { "总面积" },
        ["self_area"] = new[] { "自有耕地" },
        ["rented_area"] = new[] { "承租他人" },
        ["variety"] = new[] { "品种" },
        ["seed_quantity"] = new[] { "种子数量" },
        ["subsidy_area"] = new[] { "补贴面积" },
        ["source"] = new[] { "来源" }
    };

    public SoybeanSubsidyImportService(IDatabaseService databaseService, ILoggerService logger) : base(databaseService, logger)
    {
    }

    public override async Task<Result> ClearTableAsync(CancellationToken ct = default)
    {
        try
        {
            await DatabaseService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_biz_high_oil_soybean_detail RESTART IDENTITY CASCADE", ct);
            await DatabaseService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_biz_high_oil_soybean_person RESTART IDENTITY CASCADE", ct);
            await DatabaseService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_biz_high_protein_soybean_detail RESTART IDENTITY CASCADE", ct);
            await DatabaseService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_biz_high_protein_soybean_person RESTART IDENTITY CASCADE", ct);
            LogInfo("表已清空: nc_biz_high_oil_soybean_person/detail");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogError($"操作失败: {ex.Message}");
            return Result.Failure("CLEAR_TABLE_FAILED", $"执行清空失败: {ex.Message}");
        }
    }

    public async Task<SoybeanSubsidyImportResult> ImportAsync(string filePath, string dbType, IProgress<string> progress = null, CancellationToken ct = default)
    {
        _dbType = dbType;
        _fileName = Path.GetFileName(filePath);
        _dataYear = DateTime.Now.Year;

        var yearMatch = YearRegex().Match(_fileName);
        if (yearMatch.Success && int.TryParse(yearMatch.Value, out var year))
        {
            _dataYear = year;
        }

        var baseResult = await base.ImportSingleFileAsync(filePath, progress, ct);

        return new SoybeanSubsidyImportResult
        {
            Success = baseResult.Success,
            ImportedCount = baseResult.ImportedCount,
            Errors = baseResult.Errors
        };
    }

    protected override async Task ProcessWorksheetAsync(IExcelSheetReader reader, ImportResult result, IProgress<string>? progress, CancellationToken ct)
    {
        var (personTable, detailTable) = GetTableNames();

        if (string.IsNullOrEmpty(personTable) || string.IsNullOrEmpty(detailTable))
        {
            result.Errors.Add("未知的大豆类型");
            return;
        }

        ValidateTableName(personTable);
        ValidateTableName(detailTable);

        var address = ExtractAddressFromFileName(_fileName);

        var rowCount = reader.RowCount;
        var headerRow = FindHeaderRow(reader);

        if (headerRow == -1)
        {
            result.Errors.Add("未找到有效的表头行");
            return;
        }

        var mapping = BuildColumnMapping(reader, ColumnAliases, headerRow);

        LogInfo($"[大豆补贴] Excel表头诊断 | 行数={rowCount}, 表头行={headerRow}, 类型={_dbType}");
        LogColumnInfo(reader, headerRow, ColumnAliases, mapping);

        var personRecords = new Dictionary<string, SoybeanPersonRecord>();
        var detailRecords = new List<SoybeanDetailRecord>();
        var personIdMap = new Dictionary<string, long>();

        for (var row = headerRow + 1; row <= rowCount; row++)
        {
            ct.ThrowIfCancellationRequested();

            var name = ReadCellString(reader, row, mapping, "name");
            var idCard = ReadCellString(reader, row, mapping, "id_card");

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(idCard))
                continue;

            if (idCard.Length < 15)
                continue;

            var phone = ReadCellString(reader, row, mapping, "phone");
            var totalArea = ReadDecimal(reader, row, mapping, "total_area");
            var selfArea = ReadDecimal(reader, row, mapping, "self_area");
            var rentedArea = ReadDecimal(reader, row, mapping, "rented_area");

            if (!personRecords.TryGetValue(idCard, out var personRecord))
            {
                personRecord = new SoybeanPersonRecord
                {
                    Name = name.Trim(),
                    IdCard = idCard.Trim(),
                    Phone = NormalizePhone(phone),
                    Address = address,
                    TotalArea = totalArea,
                    SelfArea = selfArea,
                    RentedArea = rentedArea,
                    DataYear = _dataYear
                };
                personRecords[idCard] = personRecord;
            }
            else
            {
                personRecord.TotalArea += totalArea;
                personRecord.SelfArea += selfArea;
                personRecord.RentedArea += rentedArea;
                if (!string.IsNullOrWhiteSpace(phone))
                    personRecord.Phone = NormalizePhone(phone);
            }

            var variety = ReadCellString(reader, row, mapping, "variety");
            var seedQuantity = ReadDecimal(reader, row, mapping, "seed_quantity");
            var subsidyArea = ReadDecimal(reader, row, mapping, "subsidy_area");
            var source = ReadCellString(reader, row, mapping, "source");

            if (!string.IsNullOrWhiteSpace(variety) || seedQuantity > 0 || subsidyArea > 0 || !string.IsNullOrWhiteSpace(source))
            {
                detailRecords.Add(new SoybeanDetailRecord
                {
                    IdCard = idCard.Trim(),
                    Variety = variety,
                    SeedQuantity = seedQuantity,
                    SubsidyArea = subsidyArea,
                    SeedSource = source
                });
            }
        }

        if (personRecords.Count == 0)
        {
            result.Errors.Add("未找到有效数据");
            return;
        }

        progress?.Report($"正在导入 {personRecords.Count} 条人员数据...");

        var successCount = 0;
        var errorCount = 0;

        foreach (var personRecord in personRecords.Values)
        {
            ct.ThrowIfCancellationRequested();

            var personSql = $@"
                INSERT INTO {personTable} 
                (name, id_card, phone, address, total_area, self_area, rented_area, data_year, imported_at, created_at, updated_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                RETURNING id;";

            await DatabaseService.ExecuteNonQueryAsync($"DELETE FROM {personTable} WHERE id_card = $1", ct, personRecord.IdCard);
            var personIdResult = await DatabaseService.ExecuteScalarAsync(personSql, ct,
                personRecord.Name, personRecord.IdCard, personRecord.Phone, personRecord.Address,
                personRecord.TotalArea, personRecord.SelfArea, personRecord.RentedArea, personRecord.DataYear);

            if (personIdResult.IsSuccess && personIdResult.Value > 0)
            {
                personIdMap[personRecord.IdCard] = personIdResult.Value;
                successCount++;
            }
            else
            {
                errorCount++;
                result.Errors.Add($"保存人员失败: {personRecord.Name}");
            }
        }

        var detailSuccessCount = 0;
        foreach (var detail in detailRecords)
        {
            ct.ThrowIfCancellationRequested();

            if (!personIdMap.TryGetValue(detail.IdCard, out var personId))
                continue;

            var detailSql = $@"
                INSERT INTO {detailTable} 
                (person_id, variety, seed_quantity, subsidy_area, seed_source, imported_at, created_at)
                VALUES ($1, $2, $3, $4, $5, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);";

            var detailResult = await DatabaseService.ExecuteNonQueryAsync(detailSql, ct, personId, detail.Variety, detail.SeedQuantity, detail.SubsidyArea, detail.SeedSource ?? "");

            if (detailResult.IsSuccess)
                detailSuccessCount++;
        }

        result.ImportedCount = successCount;
        result.ErrorCount = errorCount;
        progress?.Report($"导入完成！人员: {successCount} 条, 品种: {detailSuccessCount} 条");

        LogInfo($"大豆补贴导入完成: 类型 {_dbType}");
    }

    private (string personTable, string detailTable) GetTableNames()
    {
        return _dbType switch
        {
            "HighOil" => ("nc_biz_high_oil_soybean_person", "nc_biz_high_oil_soybean_detail"),
            "HighProtein" => ("nc_biz_high_protein_soybean_person", "nc_biz_high_protein_soybean_detail"),
            _ => ("", "")
        };
    }

    private static int FindHeaderRow(IExcelSheetReader reader)
    {
        var rowCount = reader.RowCount;
        var colCount = reader.ColumnCount;

        for (var r = 1; r <= Math.Min(10, rowCount); r++)
        {
            for (var c = 1; c <= colCount; c++)
            {
                var val = reader.GetCellText(r, c);
                if (val.Contains("姓名") || val.Contains("身份证"))
                    return r;
            }
        }
        return -1;
    }

    private static string ExtractAddressFromFileName(string fileName)
    {
        var cleanName = fileName.Replace("2025", "").Replace("2024", "");
        var match = VillageRegex().Match(cleanName);
        return match.Success ? match.Value : "";
    }

    private static string NormalizePhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return "";

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

    [GeneratedRegex(@"[\u4e00-\u9fa5]+(村|屯)")]
    private static partial Regex VillageRegex();

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

    private sealed class SoybeanPersonRecord
    {
        public string Name { get; set; } = "";
        public string IdCard { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Address { get; set; } = "";
        public decimal TotalArea { get; set; }
        public decimal SelfArea { get; set; }
        public decimal RentedArea { get; set; }
        public int DataYear { get; set; }
    }

    private sealed class SoybeanDetailRecord
    {
        public string IdCard { get; set; } = "";
        public string Variety { get; set; } = "";
        public decimal SeedQuantity { get; set; }
        public decimal SubsidyArea { get; set; }
        public string SeedSource { get; set; } = "";
    }
}

public class SoybeanSubsidyImportResult : ImportResult
{
}
