using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using System.Text.RegularExpressions;

namespace NewCosmos.Services.Import;

public partial class PlantingSubsidyImportService : BaseImportService
{
    protected override string ServiceName => ImportTypeName;
    public override string ImportTypeName => ImportTypeCodes.PLANTING_SUBSIDY;

    public PlantingSubsidyImportService(IDatabaseService databaseService, ILoggerService logger) : base(databaseService, logger)
    {
    }

    public override async Task<Result> ClearTableAsync(CancellationToken ct = default)
    {
        try
        {
            await DatabaseService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_biz_planting_subsidy RESTART IDENTITY CASCADE", ct);
            LogInfo("表已清空: nc_biz_planting_subsidy");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogError($"操作失败: {ex.Message}");
            return Result.Failure("CLEAR_TABLE_FAILED", $"执行清空失败: {ex.Message}");
        }
    }

    protected override async Task ProcessWorksheetAsync(IExcelSheetReader reader, ImportResult result, IProgress<string>? progress, CancellationToken ct)
    {
        // 每文件上下文由文件名派生（原 Singleton 实例字段存在并发导入交错写错风险）
        var originalFileName = Path.GetFileName(result.FilePath);
        var dataYear = ExtractYearFromFileName(Path.GetFileNameWithoutExtension(result.FilePath));
        var addressFromFile = ExtractAddressFromFileName(Path.GetFileNameWithoutExtension(result.FilePath));
        var rowCount = reader.RowCount;
        var headerRow = FindHeaderRow(reader);

        if (headerRow == -1)
        {
            result.Errors.Add("未找到有效的表头行（需包含姓名和身份证号）");
            return;
        }

        var mapping = MapColumnsByHeader(reader, headerRow);
        if (mapping == null || mapping.Count == 0)
        {
            result.Errors.Add("无法识别表头列，请检查 Excel 格式");
            return;
        }

        var missingFields = ValidateRequiredColumns(mapping);
        if (missingFields.Count > 0)
        {
            result.Errors.Add($"缺少必需列: {string.Join(", ", missingFields)}");
            return;
        }

        var dataStartRow = FindDataStartRow(reader, mapping, headerRow);

        LogInfo($"[种植补贴] Excel表头诊断 | 行数={rowCount}, 表头行={headerRow}, 数据起始行={dataStartRow}");
        LogColumnMapping(reader, headerRow, mapping);

        var personDataMap = new Dictionary<string, PlantingSubsidyRecord>();
        var fileName = originalFileName;

        for (var row = dataStartRow; row <= rowCount; row++)
        {
            ct.ThrowIfCancellationRequested();

            var name = ReadCellString(reader, row, mapping, "name");
            var idCard = ReadCellString(reader, row, mapping, "id_card");

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(idCard))
                continue;

            if (idCard.Length < 15 || idCard.Contains("身份证"))
                continue;

            var phone = ReadCellString(reader, row, mapping, "phone");
            var address = addressFromFile;

            var totalAcreage = ReadDecimal(reader, row, mapping, "total_acreage");
            var cornAcreage = ReadDecimal(reader, row, mapping, "corn_acreage");
            var soybeanAcreage = ReadDecimal(reader, row, mapping, "soybean_acreage");
            var riceTotalAcreage = ReadDecimal(reader, row, mapping, "rice_total_acreage");
            var riceSurfaceWaterAcreage = ReadDecimal(reader, row, mapping, "rice_surface_water_acreage");
            var riceGroundwaterAcreage = ReadDecimal(reader, row, mapping, "rice_groundwater_acreage");

            if (personDataMap.TryGetValue(idCard, out var existing))
            {
                existing.TotalAcreage += totalAcreage;
                existing.CornAcreage += cornAcreage;
                existing.SoybeanAcreage += soybeanAcreage;
                existing.RiceTotalAcreage += riceTotalAcreage;
                existing.RiceSurfaceWaterAcreage += riceSurfaceWaterAcreage;
                existing.RiceGroundwaterAcreage += riceGroundwaterAcreage;

                if (!string.IsNullOrWhiteSpace(phone))
                    existing.Phone = NormalizePhone(phone);

                if (!existing.OriginalFile.Contains(fileName))
                    existing.OriginalFile += ", " + fileName;
            }
            else
            {
                personDataMap[idCard] = new PlantingSubsidyRecord
                {
                    Name = name.Trim(),
                    IdCard = idCard.Trim(),
                    Phone = NormalizePhone(phone),
                    Address = address,
                    TotalAcreage = totalAcreage,
                    CornAcreage = cornAcreage,
                    SoybeanAcreage = soybeanAcreage,
                    RiceTotalAcreage = riceTotalAcreage,
                    RiceSurfaceWaterAcreage = riceSurfaceWaterAcreage,
                    RiceGroundwaterAcreage = riceGroundwaterAcreage,
                    DataYear = dataYear,
                    OriginalFile = fileName
                };
            }
        }

        if (personDataMap.Count == 0)
        {
            result.Errors.Add("未找到有效数据");
            return;
        }

        progress?.Report($"正在导入 {personDataMap.Count} 条数据...");

        var successCount = 0;
        var errorCount = 0;
        var records = personDataMap.Values.ToList();

        // 批量清理：本次导入年度内同证件旧记录（data_year 统一，一次 DELETE + = ANY）
        ct.ThrowIfCancellationRequested();
        var deleteResult = await DatabaseService.ExecuteNonQueryAsync(
            "DELETE FROM nc_biz_planting_subsidy WHERE data_year = $1 AND id_card = ANY($2::text[])",
            ct, records[0].DataYear, records.Select(r => r.IdCard).Distinct().ToArray());
        if (deleteResult.IsFailure)
        {
            result.Errors.Add($"清理旧数据失败: {deleteResult.Message}");
            result.ImportedCount = 0;
            result.ErrorCount = records.Count;
            progress?.Report("导入失败：清理旧数据失败");
            LogError($"种植补贴导入失败: 清理旧数据失败 {deleteResult.Message}");
            return;
        }

        // 多行 VALUES 分块插入（12 参数/行，400 行/块 ≈ 4800 参数，低于 PG 单语句 65535 参数上限）
        const int chunkSize = 400;
        for (var start = 0; start < records.Count; start += chunkSize)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = records.Skip(start).Take(chunkSize).ToList();
            var (valuesClause, args) = MultiRowValuesBuilder.Build(chunk.Count, 12, i =>
            {
                var record = chunk[i];
                return new object?[]
                {
                    record.Name, record.IdCard, record.Phone, record.Address,
                    record.TotalAcreage, record.CornAcreage, record.SoybeanAcreage,
                    record.RiceTotalAcreage, record.RiceSurfaceWaterAcreage, record.RiceGroundwaterAcreage,
                    record.DataYear, record.OriginalFile
                };
            });

            var execResult = await DatabaseService.ExecuteNonQueryAsync(@"
                INSERT INTO nc_biz_planting_subsidy 
                (name, id_card, phone, address, total_acreage, corn_acreage, soybean_acreage, 
                 rice_total_acreage, rice_surface_water_acreage, rice_groundwater_acreage, 
                 data_year, original_file, imported_at)
                VALUES " + valuesClause + ", CURRENT_TIMESTAMP", ct, args);

            if (execResult.IsSuccess)
            {
                successCount += chunk.Count;
                progress?.Report($"已导入 {successCount} 条...");
            }
            else
            {
                errorCount = records.Count - successCount;
                result.Errors.Add($"保存失败: {execResult.Message}");
                LogError($"种植补贴分块插入失败: {execResult.Message}");
                break;
            }
        }

        result.ImportedCount = successCount;
        result.ErrorCount = errorCount;
        progress?.Report($"导入完成！成功 {successCount} 条，失败 {errorCount} 条");

        LogInfo($"种植补贴导入完成: 成功 {successCount} 条，失败 {errorCount} 条");
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

    private static int FindDataStartRow(IExcelSheetReader reader, Dictionary<string, int> mapping, int headerRow)
    {
        if (!mapping.TryGetValue("id_card", out var idCardCol))
            return headerRow + 3;

        for (var r = headerRow + 1; r <= headerRow + 10; r++)
        {
            var val = reader.GetCellText(r, idCardCol).Trim();
            if (val.Length >= 15 && !val.Contains("身份证") && val.All(ch => char.IsDigit(ch)))
                return r;
        }

        return headerRow + 3;
    }

    private static Dictionary<string, int>? MapColumnsByHeader(IExcelSheetReader reader, int headerRow)
    {
        var mapping = new Dictionary<string, int>();
        var rowCount = reader.RowCount;
        var colCount = reader.ColumnCount;

        // 扫描 headerRow 到 headerRow+3 行
        var scanEnd = Math.Min(headerRow + 3, rowCount);

        // 第一轮：识别玉米和大豆的主列位置
        var cornMainCol = 0;
        var soybeanMainCol = 0;

        for (var r = headerRow; r <= scanEnd; r++)
        {
            for (var c = 1; c <= colCount; c++)
            {
                var text = reader.GetCellText(r, c).Trim();
                if (text == "玉米" && cornMainCol == 0)
                    cornMainCol = c;
                if (text == "大豆" && soybeanMainCol == 0)
                    soybeanMainCol = c;
            }
        }

        // 第二轮：扫描所有行，按关键词匹配
        for (var r = headerRow; r <= scanEnd; r++)
        {
            for (var c = 1; c <= colCount; c++)
            {
                var text = reader.GetCellText(r, c).Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;

                // 基础字段匹配
                if (text.Contains("姓名") && !mapping.ContainsKey("name"))
                    mapping["name"] = c;
                if (text.Contains("身份证") && !mapping.ContainsKey("id_card"))
                    mapping["id_card"] = c;
                if (text.Contains("联系方式") || text == "电话" || text == "手机")
                {
                    if (!mapping.ContainsKey("phone"))
                        mapping["phone"] = c;
                }
                if (text.Contains("总种植面积") || text == "总面积")
                {
                    if (!mapping.ContainsKey("total_acreage"))
                        mapping["total_acreage"] = c;
                }

                // 玉米面积：找到"玉米"主列后，在下一行找到同一列的"面积"
                if (cornMainCol > 0 && c == cornMainCol && text == "面积")
                {
                    if (!mapping.ContainsKey("corn_acreage"))
                        mapping["corn_acreage"] = c;
                }

                // 大豆面积
                if (soybeanMainCol > 0 && c == soybeanMainCol && text == "面积")
                {
                    if (!mapping.ContainsKey("soybean_acreage"))
                        mapping["soybean_acreage"] = c;
                }

                // 稻谷子列
                if (text == "合计" && !mapping.ContainsKey("rice_total_acreage"))
                    mapping["rice_total_acreage"] = c;
                if (text == "地表水" && !mapping.ContainsKey("rice_surface_water_acreage"))
                    mapping["rice_surface_water_acreage"] = c;
                if (text == "地下水" && !mapping.ContainsKey("rice_groundwater_acreage"))
                    mapping["rice_groundwater_acreage"] = c;
            }
        }

        // 玉米/大豆：如果子行没有"面积"，主列本身就是面积列
        if (cornMainCol > 0 && !mapping.ContainsKey("corn_acreage"))
            mapping["corn_acreage"] = cornMainCol;
        if (soybeanMainCol > 0 && !mapping.ContainsKey("soybean_acreage"))
            mapping["soybean_acreage"] = soybeanMainCol;

        return mapping.Count > 0 ? mapping : null;
    }

    private static List<string> ValidateRequiredColumns(Dictionary<string, int> mapping)
    {
        var requiredFields = new[] { "name", "id_card", "total_acreage" };
        var fieldLabels = new Dictionary<string, string>
        {
            ["name"] = "姓名",
            ["id_card"] = "身份证号",
            ["phone"] = "联系方式",
            ["total_acreage"] = "总种植面积",
            ["corn_acreage"] = "玉米面积",
            ["soybean_acreage"] = "大豆面积",
            ["rice_total_acreage"] = "稻谷合计",
            ["rice_surface_water_acreage"] = "地表水面积",
            ["rice_groundwater_acreage"] = "地下水面积"
        };

        var missing = new List<string>();
        foreach (var field in requiredFields)
        {
            if (!mapping.ContainsKey(field))
                missing.Add(fieldLabels.GetValueOrDefault(field, field));
        }
        return missing;
    }

    private static int ExtractYearFromFileName(string fileName)
    {
        var match = YearRegex().Match(fileName);
        return match.Success && int.TryParse(match.Value, out var year) ? year : DateTime.Now.Year;
    }

    private static string ExtractAddressFromFileName(string fileName)
    {
        var cleanName = YearPrefixRegex().Replace(fileName, "");
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

    [GeneratedRegex(@"20\d{2}")]
    private static partial Regex YearPrefixRegex();

    [GeneratedRegex(@"[\u4e00-\u9fa5]+(村|屯)")]
    private static partial Regex VillageRegex();

    private void LogColumnMapping(IExcelSheetReader reader, int headerRow,
        Dictionary<string, int> mapping)
    {
        var actualHeaders = new List<string>();
        for (var c = 1; c <= Math.Min(20, reader.ColumnCount); c++)
        {
            var text = reader.GetCellText(headerRow, c);
            if (!string.IsNullOrEmpty(text))
                actualHeaders.Add($"[{c}]{text}");
        }
        LogInfo($"  Excel实际表头: {string.Join(" | ", actualHeaders)}");

        foreach (var kvp in mapping)
        {
            var actualText = reader.GetCellText(headerRow, kvp.Value);
            LogInfo($"  动态映射: {kvp.Key} -> 第{kvp.Value}列, 实际内容=\"{actualText}\"");
        }

        LogInfo($"  列映射: {mapping.Count} 个字段已识别");
    }

    private sealed class PlantingSubsidyRecord
    {
        public string Name { get; set; } = "";
        public string IdCard { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Address { get; set; } = "";
        public decimal TotalAcreage { get; set; }
        public decimal CornAcreage { get; set; }
        public decimal SoybeanAcreage { get; set; }
        public decimal RiceTotalAcreage { get; set; }
        public decimal RiceSurfaceWaterAcreage { get; set; }
        public decimal RiceGroundwaterAcreage { get; set; }
        public int DataYear { get; set; }
        public string OriginalFile { get; set; } = "";
    }
}
