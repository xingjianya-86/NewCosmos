using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using System.Text.RegularExpressions;

namespace NewCosmos.Services.Import;

public partial class SoybeanSubsidyImportService : BaseImportService
{
    /// <summary>表名白名单统一走 Helpers.TableNameValidator（原本地白名单为其子集，已删除）。</summary>
    private static void ValidateTableName(string tableName)
    {
        TableNameValidator.ValidateOrThrow(tableName);
    }

    protected override string ServiceName => ImportTypeName;
    public override string ImportTypeName => ImportTypeCodes.SOYBEAN_SUBSIDY;

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

    /// <summary>按文件名派生大豆类型：高蛋白→HighProtein，高油→HighOil，否则为空（显式失败）。</summary>
    private static string DeriveDbTypeFromFileName(string fileName)
    {
        if (fileName.Contains("高蛋白")) return "HighProtein";
        if (fileName.Contains("高油")) return "HighOil";
        return "";
    }

    private static int DeriveDataYear(string fileName)
    {
        var dataYear = DateTime.Now.Year;
        var yearMatch = YearRegex().Match(fileName);
        if (yearMatch.Success && int.TryParse(yearMatch.Value, out var year))
        {
            dataYear = year;
        }
        return dataYear;
    }

    protected override async Task ProcessWorksheetAsync(IExcelSheetReader reader, ImportResult result, IProgress<string>? progress, CancellationToken ct)
    {
        // 每文件上下文由文件名派生（原 Singleton 实例字段存在并发导入交错写错风险；
        // 原自定义 ImportAsync(filePath, dbType) 入口为 0 调用方僵尸 API 且绕过导入互斥，已删除）
        var fileName = Path.GetFileName(result.FilePath);
        var dbType = DeriveDbTypeFromFileName(fileName);
        var dataYear = DeriveDataYear(fileName);
        var (personTable, detailTable) = GetTableNames(dbType);

        if (string.IsNullOrEmpty(personTable) || string.IsNullOrEmpty(detailTable))
        {
            result.Errors.Add("无法从文件名识别大豆类型：文件名需包含“高油”或“高蛋白”");
            return;
        }

        ValidateTableName(personTable);
        ValidateTableName(detailTable);

        var address = ExtractAddressFromFileName(fileName);

        var rowCount = reader.RowCount;
        var headerRow = FindHeaderRow(reader);

        if (headerRow == -1)
        {
            result.Errors.Add("未找到有效的表头行");
            return;
        }

        var mapping = BuildColumnMapping(reader, ColumnAliases, headerRow);

        LogInfo($"[大豆补贴] Excel表头诊断 | 行数={rowCount}, 表头行={headerRow}, 类型={dbType}");
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
                    DataYear = dataYear
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

        // 批量写入（原逐人 DELETE+INSERT+RETURNING 与逐明细 INSERT 为 N+1，大批量导入会超时）：
        // 1) 整批删旧人员 → 2) 多行 VALUES 批插人员 → 3) 一次查回 id 映射 → 4) 多行 VALUES 批插明细
        var allIdCards = personRecords.Values.Select(p => p.IdCard).ToList();

        var deleteResult = await DatabaseService.ExecuteNonQueryAsync(
            $"DELETE FROM {personTable} WHERE id_card = ANY($1)", ct, allIdCards);
        if (deleteResult.IsFailure)
        {
            result.Errors.Add($"清除旧人员数据失败: {deleteResult.Message}");
            result.ErrorCount = allIdCards.Count;
            return;
        }

        var personList = personRecords.Values.ToList();
        for (var chunkStart = 0; chunkStart < personList.Count; chunkStart += BatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = personList.Skip(chunkStart).Take(BatchSize).ToList();
            var (valuesClause, insertArgs) = MultiRowValuesBuilder.Build(chunk.Count, 8, i =>
                new object?[] { chunk[i].Name, chunk[i].IdCard, chunk[i].Phone, chunk[i].Address,
                    chunk[i].TotalArea, chunk[i].SelfArea, chunk[i].RentedArea, chunk[i].DataYear },
                s => $"(${s},${s + 1},${s + 2},${s + 3},${s + 4},${s + 5},${s + 6},${s + 7},CURRENT_TIMESTAMP,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)");

            var insertSql = $@"INSERT INTO {personTable}
                (name, id_card, phone, address, total_area, self_area, rented_area, data_year, imported_at, created_at, updated_at)
                VALUES {valuesClause}";
            var insertResult = await DatabaseService.ExecuteNonQueryAsync(insertSql, ct, insertArgs);
            if (insertResult.IsSuccess)
            {
                successCount += chunk.Count;
            }
            else
            {
                errorCount += chunk.Count;
                result.Errors.Add($"批量保存人员失败（{chunk.Count} 条）: {insertResult.Message}");
            }
        }

        // 批插后一次查回 id 映射（替代逐人 RETURNING id）
        var idResult = await DatabaseService.QueryAsync<SoybeanPersonIdRow>(
            $"SELECT id, id_card FROM {personTable} WHERE id_card = ANY($1)", ct, allIdCards);
        if (idResult.IsFailure)
        {
            result.Errors.Add($"回查人员ID失败: {idResult.Message}");
            result.ImportedCount = successCount;
            result.ErrorCount = errorCount + 1;
            return;
        }
        foreach (var row in idResult.Value ?? new List<SoybeanPersonIdRow>())
        {
            personIdMap[row.IdCard] = row.Id;
        }

        var detailRows = detailRecords.Where(d => personIdMap.ContainsKey(d.IdCard)).ToList();
        var detailSuccessCount = 0;
        for (var chunkStart = 0; chunkStart < detailRows.Count; chunkStart += BatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = detailRows.Skip(chunkStart).Take(BatchSize).ToList();
            var (valuesClause, insertArgs) = MultiRowValuesBuilder.Build(chunk.Count, 5, i =>
                new object?[] { personIdMap[chunk[i].IdCard], chunk[i].Variety, chunk[i].SeedQuantity,
                    chunk[i].SubsidyArea, chunk[i].SeedSource ?? "" },
                s => $"(${s},${s + 1},${s + 2},${s + 3},${s + 4},CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)");

            var detailSql = $@"INSERT INTO {detailTable}
                (person_id, variety, seed_quantity, subsidy_area, seed_source, imported_at, created_at)
                VALUES {valuesClause}";
            var detailResult = await DatabaseService.ExecuteNonQueryAsync(detailSql, ct, insertArgs);
            if (detailResult.IsSuccess)
                detailSuccessCount += chunk.Count;
        }

        result.ImportedCount = successCount;
        result.ErrorCount = errorCount;
        progress?.Report($"导入完成！人员: {successCount} 条, 品种: {detailSuccessCount} 条");

        LogInfo($"大豆补贴导入完成: 类型 {dbType}");
    }

    private static (string personTable, string detailTable) GetTableNames(string dbType)
    {
        return dbType switch
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

    private sealed class SoybeanPersonIdRow
    {
        public long Id { get; set; }
        public string IdCard { get; set; } = "";
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
