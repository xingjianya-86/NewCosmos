using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using System.Text.RegularExpressions;

namespace NewCosmos.Services.Import;

/// <summary>
/// 年度种植补贴导入（四合一长表 nc_biz_annual_planting_subsidy）。
/// 合并原 农业补贴(地力)/种植补贴/大豆补贴(品种)/轮作补贴 四个入口。
/// 模板为长表：数据年份(可选) | 姓名 | 身份证号 | 分类 | 金额（元）——一行 = 一年 × 一人 × 一个分类。
/// 年段规则：行内「数据年份」优先（空则沿用上一个有效值），其次文件名 20xx，最后当前年；
/// 分类严格限定 7 个规范名（AnnualPlantingColumns.NormalizeCategory），认不出即整份报错、不写库。
/// 幂等：按 (data_year, 证件) 先删后插（覆盖存量转换行，避免新旧同人同年双算），同文件重复导入不翻倍。
/// </summary>
public partial class AnnualPlantingSubsidyImportService : BaseImportService
{
    protected override string ServiceName => ImportTypeName;
    public override string ImportTypeName => ImportTypeCodes.ANNUAL_PLANTING_SUBSIDY;

    /// <summary>年份管理型导入：支持年份强制覆盖 / 区间校验 / 年段感知清空（方案A，不整表 TRUNCATE）</summary>
    public override bool SupportsScope => true;

    private const string TableName = "nc_biz_annual_planting_subsidy";

    public AnnualPlantingSubsidyImportService(IDatabaseService databaseService, ILoggerService logger)
        : base(databaseService, logger)
    {
    }

    public override async Task<Result> ClearTableAsync(CancellationToken ct = default)
    {
        try
        {
            TableNameValidator.ValidateOrThrow(TableName);
            await DatabaseService.ExecuteNonQueryAsync($"TRUNCATE TABLE {TableName} RESTART IDENTITY CASCADE", ct);
            LogInfo($"表已清空: {TableName}");
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
        // 每文件上下文由文件名派生（年份兜底 + 来源审计标记 + 地址兜底）；范围（年份覆盖/区间/清空）来自页面
        var scope = ActiveScope;
        var fileName = Path.GetFileName(result.FilePath);
        var fileYear = ExtractYearFromFileName(Path.GetFileNameWithoutExtension(result.FilePath));
        var sourceType = ExtractSourceTag(Path.GetFileNameWithoutExtension(result.FilePath));
        var addressFromFile = ExtractAddressFromFileName(Path.GetFileNameWithoutExtension(result.FilePath));

        var headerRow = FindHeaderRow(reader);
        if (headerRow == -1)
        {
            result.Errors.Add("未找到有效的表头行（需包含姓名和身份证号）");
            return;
        }

        var mapping = BuildMapping(reader, headerRow);
        var missing = AnnualPlantingColumns.RequiredKeys
            .Where(k => !mapping.ContainsKey(k))
            .Select(AnnualPlantingColumns.GetHeader)
            .ToList();
        if (missing.Count > 0)
        {
            result.Errors.Add($"缺少必需列: {string.Join("、", missing)}（模板列：{string.Join(" | ", AnnualPlantingColumns.GetTemplateHeaders())}）");
            return;
        }

        var dataStartRow = FindDataStartRow(reader, headerRow, mapping);
        LogInfo($"[年度种植补贴] 表头诊断 | 文件={fileName}, 表头行={headerRow}, 数据起始行={dataStartRow}, " +
                $"文件名年份={fileYear}, 来源标记={sourceType}, 范围={(scope?.Describe() ?? "无")}" +
                (scope?.OverrideYear != null ? $", 年份强制为 {scope.OverrideYear}" : ""));

        // 旧宽表/面积列提示：本模板按金额入账，面积列不会被读取（避免用户以为面积生效）
        var areaHeader = FindHeaderContains(reader, headerRow, "面积");
        if (!string.IsNullOrEmpty(areaHeader))
        {
            var warn = $"检测到「{areaHeader}」列：本导入按【金额（元）】入账，面积列不会被读取";
            result.Warnings.Add(warn);
            LogWarn($"[年度种植补贴] {warn}");
        }

        // ---------------- 扫描阶段：只读，全部校验通过才写库 ----------------
        var invalidCategories = new List<string>();
        var persons = new Dictionary<string, PersonAccum>(StringComparer.Ordinal);
        var amounts = new Dictionary<(int Year, string IdCard, string Category), decimal>();
        var skippedNoAmount = 0;
        var skippedBadIdCard = 0;
        var outOfRangeYears = new List<string>();
        int? lastRowYear = null;

        for (var row = dataStartRow; row <= reader.RowCount; row++)
        {
            ct.ThrowIfCancellationRequested();

            var name = ReadCell(reader, row, mapping, AnnualPlantingColumns.NAME);
            var idCard = ReadCell(reader, row, mapping, AnnualPlantingColumns.ID_CARD);

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(idCard)) continue;
            idCard = idCard.Trim().ToUpperInvariant();
            if (!IsValidIdCard(idCard)) { skippedBadIdCard++; continue; }
            if (name.Contains("全称") || name.Contains("合计") || name.Contains("总计")) continue;

            // 年段解析：页面「导入年份」强制覆盖 → 行内年份 → 沿用上一个有效值 → 文件名年份 → 当前年
            var rowYear = ParseYear(ReadCell(reader, row, mapping, AnnualPlantingColumns.YEAR));
            if (rowYear.HasValue) lastRowYear = rowYear;
            var year = scope?.OverrideYear ?? lastRowYear ?? fileYear;

            // 区间校验：填了区间则最终年份必须落在区间内；越界行先记账，扫描结束后整份报错、零写库
            if (scope is { HasYearRange: true } && (year < scope.RangeStart!.Value || year > scope.RangeEnd!.Value))
            {
                outOfRangeYears.Add($"第{row}行（年份 {year}，允许 {scope.RangeStart}-{scope.RangeEnd}）");
                continue;
            }

            // 分类：严格 7 规范名，认不出记行号（最后整份报错、零写库）
            var rawCategory = ReadCell(reader, row, mapping, AnnualPlantingColumns.CATEGORY);
            var category = AnnualPlantingColumns.NormalizeCategory(rawCategory);
            if (category == null)
            {
                var shown = string.IsNullOrWhiteSpace(rawCategory) ? "(空)" : rawCategory.Trim();
                invalidCategories.Add($"第{row}行「{shown}」");
                continue;
            }

            var amount = ReadAmount(reader, row, mapping[AnnualPlantingColumns.AMOUNT]);
            if (amount <= 0) { skippedNoAmount++; continue; }

            if (!persons.TryGetValue(idCard, out var acc))
            {
                acc = new PersonAccum
                {
                    Name = name.Trim(),
                    IdCard = idCard,
                    Phone = NormalizePhone(ReadCell(reader, row, mapping, "phone")),
                    Address = ReadCell(reader, row, mapping, "address").Replace("\r", "").Replace("\n", "").Trim()
                };
                if (string.IsNullOrWhiteSpace(acc.Address)) acc.Address = addressFromFile;
                persons[idCard] = acc;
            }

            var key = (year, idCard, category);
            amounts[key] = amounts.TryGetValue(key, out var prev) ? prev + amount : amount;
        }

        // ---------------- 分类非法 → 整份失败、不碰数据库 ----------------
        if (invalidCategories.Count > 0)
        {
            const int maxShown = 20;
            foreach (var line in invalidCategories.Take(maxShown))
                result.Errors.Add($"分类无法识别（仅允许 7 个规范名：{string.Join("/", AnnualPlantingColumns.Categories)}）：{line}");
            if (invalidCategories.Count > maxShown)
                result.Errors.Add($"……另有 {invalidCategories.Count - maxShown} 行同类错误");
            result.Errors.Add("本文件未写入任何数据；修正「分类」列后重新导入即可（幂等，不会产生重复数据）");
            result.ErrorCount = invalidCategories.Count;
            LogError($"年度种植补贴导入校验失败: {invalidCategories.Count} 行分类非法，文件={fileName}");
            return;
        }

        // ---------------- 年份越界 → 整份失败、不碰数据库（与分类非法同口径） ----------------
        if (outOfRangeYears.Count > 0)
        {
            var rangeStart = scope!.RangeStart!.Value;
            var rangeEnd = scope.RangeEnd!.Value;
            const int maxShown = 20;
            foreach (var line in outOfRangeYears.Take(maxShown))
                result.Errors.Add($"年份超出导入区间 {rangeStart}-{rangeEnd}：{line}");
            if (outOfRangeYears.Count > maxShown)
                result.Errors.Add($"……另有 {outOfRangeYears.Count - maxShown} 行超出区间");
            result.Errors.Add("本文件未写入任何数据；修正「数据年份」列或调整页面导入区间后重新导入即可（幂等，不会产生重复数据）");
            result.ErrorCount = outOfRangeYears.Count;
            LogError($"年度种植补贴导入区间校验失败: {outOfRangeYears.Count} 行越界，区间={rangeStart}-{rangeEnd}，文件={fileName}");
            return;
        }

        if (amounts.Count == 0)
        {
            result.Errors.Add("未找到有效数据（姓名/身份证号/分类/金额 均无有效行）");
            return;
        }

        // ---------------- 写库阶段：按年段分组，各自幂等 ----------------
        var totalPersons = persons.Count;
        progress?.Report($"正在导入 {totalPersons} 人 / {amounts.Count} 条补贴记录...");

        // ---------------- 年段感知清空（方案A）：只清本次涉及的年段，同批次多文件只清一次 ----------------
        // 不再整表 TRUNCATE —— 其余年段（如 2025 历史台账）一律保留；清空前已在 Manager 侧完成 pg_dump 备份。
        if (scope?.ClearBeforeImport == true)
        {
            var clearYears = amounts.Keys.Select(k => k.Year).Distinct().OrderBy(y => y).ToList();
            foreach (var y in clearYears)
            {
                if (!scope.ClearedYears.Add(y))
                {
                    LogInfo($"[年度种植补贴] {y} 年段本批次已清空过，跳过（避免覆盖同批先前文件）");
                    continue;
                }

                var clearResult = await DatabaseService.ExecuteNonQueryAsync(
                    $"DELETE FROM {TableName} WHERE data_year = $1", ct, y);
                if (clearResult.IsFailure)
                {
                    result.Errors.Add($"清空 {y} 年段失败: {clearResult.Message}");
                    result.ImportedCount = 0;
                    result.ErrorCount = amounts.Count(kv => kv.Key.Year == y);
                    progress?.Report("导入失败：清空年段失败");
                    LogError($"年度种植补贴年段清空失败: {y}年 {clearResult.Message}");
                    return;
                }

                LogInfo($"[年度种植补贴] 已按勾选清空 {y} 年段全部记录（其余年段保留）");
            }
        }

        var successCount = 0;
        var insertErrorCount = 0;
        const int chunkSize = 400; // 9 参数/行 × 400 = 3600 参数，远低于 PG 65535 上限

        foreach (var yearGroup in amounts.GroupBy(kv => kv.Key.Year).OrderBy(g => g.Key))
        {
            ct.ThrowIfCancellationRequested();
            var year = yearGroup.Key;
            var cards = yearGroup.Select(kv => kv.Key.IdCard).Distinct().ToArray();

            // 同年+同证件旧记录整批清理：不按 source_type 过滤，必须覆盖存量转换行（标记各异），
            // 否则新旧同人同年在读取侧求和双算。代价：同证件同年多文件时后导入者覆盖先前（与原种植补贴口径一致）。
            var deleteResult = await DatabaseService.ExecuteNonQueryAsync(
                $"DELETE FROM {TableName} WHERE data_year = $1 AND id_card = ANY($2::text[])",
                ct, year, cards);
            if (deleteResult.IsFailure)
            {
                result.Errors.Add($"清理旧数据失败({year}年): {deleteResult.Message}");
                result.ImportedCount = 0;
                result.ErrorCount = yearGroup.Count();
                progress?.Report("导入失败：清理旧数据失败");
                LogError($"年度种植补贴导入失败: {year}年 清理旧数据失败 {deleteResult.Message}");
                return;
            }

            var rows = yearGroup
                .Select(kv => (Name: persons[kv.Key.IdCard].Name,
                               IdCard: kv.Key.IdCard,
                               Category: kv.Key.Category,
                               Amount: kv.Value))
                .ToList();

            for (var start = 0; start < rows.Count; start += chunkSize)
            {
                ct.ThrowIfCancellationRequested();
                var chunk = rows.Skip(start).Take(chunkSize).ToList();
                var (valuesClause, args) = MultiRowValuesBuilder.Build(
                    chunk.Count,
                    9,
                    i =>
                    {
                        var r = chunk[i];
                        var p = persons[r.IdCard];
                        return new object?[] { year, r.Name, r.IdCard, p.Phone, p.Address, r.Category, r.Amount, sourceType, fileName };
                    },
                    startIndex => $"(${startIndex},${startIndex + 1},${startIndex + 2},${startIndex + 3},${startIndex + 4}," +
                                  $"${startIndex + 5},${startIndex + 6},${startIndex + 7},${startIndex + 8}, CURRENT_TIMESTAMP)");

                var execResult = await DatabaseService.ExecuteNonQueryAsync($@"
                    INSERT INTO {TableName}
                    (data_year, name, id_card, phone, address, category, amount, source_type, source_file, imported_at)
                    VALUES " + valuesClause, ct, args);

                if (execResult.IsSuccess)
                {
                    successCount += chunk.Count;
                    progress?.Report($"已导入 {successCount} 条...");
                }
                else
                {
                    insertErrorCount += chunk.Count;
                    result.Errors.Add($"保存失败({year}年): {execResult.Message}");
                    LogError($"年度种植补贴分块插入失败({year}年): {execResult.Message}");
                    break;
                }
            }
        }

        result.ImportedCount = successCount;
        result.ErrorCount = insertErrorCount;

        // 分类 × 年段 小计进日志（可经「导出日志」核对）
        foreach (var g in amounts.GroupBy(kv => kv.Key.Year).OrderBy(g => g.Key))
        {
            foreach (var cat in g.GroupBy(kv => kv.Key.Category).OrderBy(x => x.Key))
            {
                LogInfo($"[年度种植补贴] {g.Key}年 {cat.Key}: {cat.Count()} 人, 合计 {cat.Sum(x => x.Value):F2} 元");
            }
        }
        if (skippedNoAmount > 0)
        {
            result.Warnings.Add($"跳过 {skippedNoAmount} 行（金额为空或为 0）");
            LogWarn($"[年度种植补贴] 跳过 {skippedNoAmount} 行：金额为空或为 0");
        }
        if (skippedBadIdCard > 0)
        {
            result.Warnings.Add($"跳过 {skippedBadIdCard} 行（身份证非 15/18 位，或前 17 位含非数字——常见于被 Excel 存成数字/科学计数法）");
            LogWarn($"[年度种植补贴] ⚠ 跳过 {skippedBadIdCard} 行：身份证不合规（请检查该列是否被 Excel 转成数字），文件={fileName}");
        }

        progress?.Report($"导入完成！{totalPersons} 人 / 成功 {successCount} 条, 失败 {insertErrorCount} 条");
        LogInfo($"年度种植补贴导入完成: 人={totalPersons}, 行={successCount}, 失败={insertErrorCount}, " +
                $"年段={string.Join(",", amounts.GroupBy(kv => kv.Key.Year).Select(g => g.Key).OrderBy(x => x))}, 来源标记={sourceType}");
    }

    #region 表头与单元格

    /// <summary>表头行：前 10 行内出现「姓名」或「身份证」。</summary>
    private static int FindHeaderRow(IExcelSheetReader reader)
    {
        var end = Math.Min(10, reader.RowCount);
        for (var r = 1; r <= end; r++)
        {
            for (var c = 1; c <= reader.ColumnCount; c++)
            {
                var val = GetHeaderText(reader, r, c);
                if (val.Contains("姓名") || val.Contains("身份证")) return r;
            }
        }
        return -1;
    }

    /// <summary>
    /// 列映射：扫描表头行至表头行+1（容忍两行式表头）。
    /// 关键词按 AnnualPlantingColumns.Columns 顺序匹配，首个命中生效；**不剥离括号**（Base 的实现会剥掉括号造成同名歧义）。
    /// </summary>
    private static Dictionary<string, int> BuildMapping(IExcelSheetReader reader, int headerRow)
    {
        var mapping = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var scanEnd = Math.Min(headerRow + 1, reader.RowCount);

        for (var r = headerRow; r <= scanEnd; r++)
        {
            for (var c = 1; c <= reader.ColumnCount; c++)
            {
                var text = NormalizeHeader(GetHeaderText(reader, r, c));
                if (text.Length == 0) continue;

                foreach (var (key, _, keywords, _) in AnnualPlantingColumns.Columns)
                {
                    if (!keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase))) continue;
                    if (!mapping.ContainsKey(key)) mapping[key] = c;
                    break;
                }
            }
        }
        return mapping;
    }

    private static int FindDataStartRow(IExcelSheetReader reader, int headerRow, Dictionary<string, int> mapping)
    {
        if (!mapping.TryGetValue(AnnualPlantingColumns.ID_CARD, out var idCardCol)) return headerRow + 1;

        for (var r = headerRow + 1; r <= Math.Min(headerRow + 10, reader.RowCount); r++)
        {
            if (IsValidIdCard(reader.GetCellText(r, idCardCol))) return r;
        }
        return headerRow + 1;
    }

    /// <summary>
    /// 身份证是否合规：15 位或 18 位；除末位外全为数字；**末位允许 X/x**——
    /// 校验码 10 的罗马写法，18 位身份证大量存在（曾因误判为"非数字"静默跳过 1228 行，务必保持此规则）。
    /// </summary>
    private static bool IsValidIdCard(string? idCard)
    {
        if (string.IsNullOrWhiteSpace(idCard)) return false;
        var s = idCard.Trim();
        if (s.Length != 15 && s.Length != 18) return false;

        for (var i = 0; i < s.Length - 1; i++)
        {
            if (!char.IsDigit(s[i])) return false;
        }
        var last = s[^1];
        return char.IsDigit(last) || last is 'X' or 'x';
    }

    /// <summary>找表头里包含指定关键词的列名（面积列提示用），没有返回空串。</summary>
    private static string FindHeaderContains(IExcelSheetReader reader, int headerRow, string keyword)
    {
        var end = Math.Min(headerRow + 1, reader.RowCount);
        for (var r = headerRow; r <= end; r++)
        {
            for (var c = 1; c <= reader.ColumnCount; c++)
            {
                var text = NormalizeHeader(GetHeaderText(reader, r, c));
                if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return text;
            }
        }
        return string.Empty;
    }

    /// <summary>取表头文本：优先普通文本，空则取合并单元格文本。</summary>
    private static string GetHeaderText(IExcelSheetReader reader, int row, int col)
    {
        var text = reader.GetCellText(row, col);
        return string.IsNullOrEmpty(text) ? reader.GetMergedCellText(row, col) : text;
    }

    /// <summary>表头规范化：去控制字符与换行、压缩空白。</summary>
    private static string NormalizeHeader(string header)
    {
        if (string.IsNullOrEmpty(header)) return string.Empty;
        var s = Regex.Replace(header, "[\\n\\r\\u0000-\\u001F]", " ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    /// <summary>读金额：容错 ¥/￥/元/全角逗号/空格。</summary>
    private static decimal ReadAmount(IExcelSheetReader reader, int row, int col)
    {
        var text = reader.GetCellText(row, col);
        if (string.IsNullOrWhiteSpace(text)) return 0;

        text = text.Replace("￥", "").Replace("¥", "").Replace("元", "")
                   .Replace("，", ",").Replace(" ", "").Trim();
        return decimal.TryParse(text, out var v) ? v : 0;
    }

    /// <summary>解析年份：文本里取 20xx（容忍「2026年」「2026-01」等写法），非法返回 null。</summary>
    private static int? ParseYear(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Regex.Match(text, @"20\d{2}");
        return m.Success && int.TryParse(m.Value, out var y) ? y : null;
    }

    private static string ReadCell(IExcelSheetReader reader, int row, Dictionary<string, int> mapping, string key)
        => mapping.TryGetValue(key, out var col) ? reader.GetCellText(row, col) : string.Empty;

    #endregion

    #region 文件名派生（年份兜底/来源标记/村屯地址）

    /// <summary>文件名中的 20xx 年份；取不到用当前年（仅作年份列缺失时的兜底）。</summary>
    private static int ExtractYearFromFileName(string fileName)
    {
        var match = YearRegex().Match(fileName);
        return match.Success && int.TryParse(match.Value, out var year) ? year : DateTime.Now.Year;
    }

    /// <summary>文件名来源标记：非社保卡 / 社保卡 / 空串（仅作审计，不参与幂等键）。</summary>
    private static string ExtractSourceTag(string fileName)
    {
        if (fileName.Contains("非社保卡")) return "非社保卡";
        if (fileName.Contains("社保卡")) return "社保卡";
        return string.Empty;
    }

    /// <summary>从文件名解析「xx村/xx屯」作地址兜底。</summary>
    private static string ExtractAddressFromFileName(string fileName)
    {
        var cleanName = YearPrefixRegex().Replace(fileName, "");
        var match = VillageRegex().Match(cleanName);
        return match.Success ? match.Value : "";
    }

    private static string NormalizePhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "";
        phone = phone.Trim();
        if (phone.Contains('E') || phone.Contains('e'))
        {
            if (double.TryParse(phone, out var d)) return d.ToString("N2");
        }
        return phone;
    }

    [GeneratedRegex(@"20\d{2}")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"20\d{2}")]
    private static partial Regex YearPrefixRegex();

    [GeneratedRegex(@"[\u4e00-\u9fa5]+(村|屯)")]
    private static partial Regex VillageRegex();

    #endregion

    private sealed class PersonAccum
    {
        public string Name { get; set; } = "";
        public string IdCard { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Address { get; set; } = "";
    }
}
