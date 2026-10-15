using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NewCosmos.Services.Import;

/// <summary>
/// 银行流水核对导入：解析上级返回的《申请社会救助家庭查询金融资产核对情况表》(.xls/.xlsx)，
/// 按「批次 → 每人汇总 → 每银行明细」三表入库，供经济状况界面按身份证带出。
///
/// 表头识别规则（不写死列号）：
///   - 表头行 = 首个含「身份证」的行；其后紧跟的第二行表头（同含"身份证/姓名"）按行内文本识别并跳过；
///   - 余额列/流水列按出现顺序成对配对（样例文件"工行余额"与"工商银行流水"银行名不一致，故不能按银行名配对）；
///   - 银行名取余额列表头去掉"余额"后缀，做成明细数据行，增减银行不改表结构。
///
/// 超标判定（仅提示，不自动决策）：家庭合计余额 &gt; 月低保标准 × 30 × 家庭人数。
/// 标准读 IStandardConfigService（城乡取较低者从严提示），阈值留痕在批次表 exceed_threshold；
/// 核对表无户籍类型，判定为初筛口径，经济状况界面带入时按申请户实际户籍标准重算（权威）。
/// </summary>
public partial class BankFlowImportService : BaseImportService
{
    public override string ImportTypeName => ImportTypeCodes.BANK_FLOW_VERIFY;

    public override string[] FileNamePatterns => BankFlowConstants.FileNamePatterns;

    private readonly IStandardConfigService _standardConfigService;

    public BankFlowImportService(
        IDatabaseService databaseService,
        IStandardConfigService standardConfigService,
        ILoggerService logger) : base(databaseService, logger)
    {
        _standardConfigService = standardConfigService;
    }

    #region 清空（软删批次，保留历史）

    /// <summary>
    /// 覆写基类清空：软删除批次（记录随批次失效），不 TRUNCATE——核对数据按批次留痕，历史可追溯。
    /// 文件哈希唯一索引带 WHERE deleted_at IS NULL，软删后同一文件可重新导入。
    /// </summary>
    public override async Task<Result> ClearTableAsync(CancellationToken ct = default)
    {
        try
        {
            var affected = await DatabaseService.ExecuteNonQueryAsync(
                "UPDATE nc_biz_bank_flow_imports SET deleted_at = NOW() WHERE deleted_at IS NULL", ct);
            LogInfo($"软删核对批次: {affected} 个");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogError(ex, "清空核对批次失败");
            return Result.Failure("CLEAR_TABLE_FAILED", $"执行失败: {ex.Message}");
        }
    }

    #endregion

    #region 数据模型

    private sealed class ParsedRow
    {
        public int RowNo { get; set; }
        public string Relationship { get; set; } = "";
        public string Name { get; set; } = "";
        public string IdCard { get; set; } = "";
        public bool IdCardValid { get; set; }
        public string Address { get; set; } = "";
        public string Property1 { get; set; } = "";
        public string Property2 { get; set; } = "";
        public string Property3 { get; set; } = "";
        public decimal? MachineryPower { get; set; }
        public decimal TotalBalance { get; set; }
        public decimal MaxFlow { get; set; }
        /// <summary>各行银行余额/流水（与表头银行顺序一致，用于明细表）</summary>
        public List<(string Bank, decimal Balance, decimal Flow)> Banks { get; set; } = new();
        /// <summary>是否超阈值（按家庭分组判定后回填）</summary>
        public bool IsExceed { get; set; }
    }

    private sealed class ColumnMap
    {
        public int RelationCol { get; set; } = -1;
        public int NameCol { get; set; } = -1;
        public int IdCardCol { get; set; } = -1;
        public int AddressCol { get; set; } = -1;
        public int MachineryCol { get; set; } = -1;
        public int SeqCol { get; set; } = -1;
        public List<int> PropertyCols { get; } = new();
        public List<int> BalanceCols { get; } = new();
        public List<int> FlowCols { get; } = new();
        public List<string> BankNames { get; } = new();
    }

    #endregion

    #region 主流程

    protected override async Task ProcessWorksheetAsync(IExcelSheetReader reader, ImportResult result, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("识别表头...");

        var headerRow = FindHeaderRow(reader);
        if (headerRow < 0)
        {
            // 单文件导入会把所选文件夹内全部 Excel 传入：非核对表跳过（告警可见），不报错导致整批回滚
            result.Warnings.Add($"文件缺少「身份证」表头，非银行流水核对表，已跳过: {Path.GetFileName(result.FilePath)}");
            LogWarn($"非核对表文件跳过: {Path.GetFileName(result.FilePath)}");
            return;
        }

        var map = MapColumns(reader, headerRow);
        if (map.IdCardCol < 0 || map.NameCol < 0)
        {
            result.Warnings.Add($"表头缺少「身份证/姓名」列，已跳过: {Path.GetFileName(result.FilePath)}");
            return;
        }
        if (map.BalanceCols.Count == 0)
        {
            result.Warnings.Add($"表头未识别到银行余额列，已跳过: {Path.GetFileName(result.FilePath)}");
            return;
        }
        if (map.BalanceCols.Count != map.FlowCols.Count)
        {
            LogWarn($"余额列({map.BalanceCols.Count})与流水列({map.FlowCols.Count})数量不一致，按最小数量配对");
        }

        progress?.Report("解析数据行...");
        var rows = ParseRows(reader, headerRow, map, result, ct);
        if (rows.Count == 0)
        {
            if (result.Errors.Count == 0) result.Errors.Add("未解析到有效数据行");
            return;
        }

        // 阈值：城乡月低保标准取较低者（从严初筛），30 倍数在 Constants 单点
        var threshold = await ResolvePerCapitaThresholdAsync(result, ct);

        // 按连续行 + 关系列分组为家庭，判定超标（仅提示）
        MarkExceed(rows, threshold);

        var fileHash = ComputeFileHash(result.FilePath);
        var (batchNo, _) = DeriveBatchNo(result.FilePath, reader);

        // 文件去重：同一文件未清空重复导入则跳过（幂等，不报错不落数据）
        if (!string.IsNullOrEmpty(fileHash))
        {
            var dup = await DatabaseService.QuerySingleAsync<ExistingImport>(
                "SELECT id, batch_no FROM nc_biz_bank_flow_imports WHERE file_hash = $1 AND deleted_at IS NULL LIMIT 1",
                ct, fileHash);
            if (dup.IsSuccess && dup.Value != null)
            {
                result.Warnings.Add($"该文件已导入过（批次 {dup.Value.BatchNo}），已跳过；如需重新导入请勾选「导入前清空」");
                progress?.Report("文件已导入过，跳过");
                return;
            }
        }

        progress?.Report($"入库 {rows.Count} 人...");
        var batchId = await InsertBatchAsync(batchNo, result.FilePath, fileHash, rows, threshold, ct);
        if (batchId <= 0)
        {
            result.Errors.Add("批次写入失败");
            return;
        }

        await InsertRecordsAsync(batchId, rows, ct);

        // 明细行：拿回 record_id 映射（row_no 批内唯一，一次查询替代逐行 RETURNING）
        var idMap = await DatabaseService.QueryAsync<RecordIdMap>(
            "SELECT id, row_no FROM nc_biz_bank_flow_records WHERE import_id = $1", ct, batchId);
        if (idMap.IsSuccess && idMap.Value != null)
        {
            var mapById = idMap.Value.ToDictionary(r => r.RowNo, r => r.Id);
            var items = rows
                .Where(r => mapById.ContainsKey(r.RowNo))
                .SelectMany(r => r.Banks.Select(b => new BankItem
                {
                    RecordId = mapById[r.RowNo],
                    BankName = b.Bank,
                    Balance = b.Balance,
                    MaxFlow = b.Flow
                }))
                .ToList();
            await InsertItemsAsync(items, ct);
        }
        else
        {
            LogWarn($"记录ID回查失败，跳过银行明细入库: {idMap.Message}");
            result.Warnings.Add("银行明细未入库（记录ID回查失败），仅保留汇总数据");
        }

        var exceedCount = rows.Count(r => r.IsExceed);
        var invalidCount = rows.Count(r => !r.IdCardValid);

        result.ImportedCount = rows.Count;
        result.FamilyImportedCount = rows.Count;
        result.PersonImportedCount = rows.Count;

        if (invalidCount > 0)
            result.Warnings.Add($"{invalidCount} 个身份证校验位存疑，已导入并标记异常（不丢数据）");
        if (exceedCount > 0)
            result.Warnings.Add($"{exceedCount} 人所在家庭合计余额超 30 个月低保标准（初筛提示，请人工核定）");

        progress?.Report($"导入完成: {rows.Count} 人, 超阈值 {exceedCount} 人, 身份证存疑 {invalidCount} 人");
        LogInfo($"核对表导入完成: 批次 {batchNo}, {rows.Count} 人, 超阈值 {exceedCount}, 身份证存疑 {invalidCount}");
    }

    private sealed class ExistingImport
    {
        public long Id { get; set; }
        public string BatchNo { get; set; } = "";
    }

    private sealed class RecordIdMap
    {
        public long Id { get; set; }
        public int RowNo { get; set; }
    }

    private sealed class BankItem
    {
        public long RecordId { get; set; }
        public string BankName { get; set; } = "";
        public decimal Balance { get; set; }
        public decimal MaxFlow { get; set; }
    }

    #endregion

    #region 表头与列映射

    /// <summary>首个同时含「身份证」的行即明细表头行（样例 R2）；返回 -1 表示未找到。</summary>
    private static int FindHeaderRow(IExcelSheetReader reader)
    {
        var limit = Math.Min(10, reader.RowCount);
        for (var r = 1; r <= limit; r++)
        {
            for (var c = 1; c <= Math.Min(40, reader.ColumnCount); c++)
            {
                if (reader.GetCellText(r, c).Contains("身份证"))
                    return r;
            }
        }
        return -1;
    }

    private static ColumnMap MapColumns(IExcelSheetReader reader, int headerRow)
    {
        var map = new ColumnMap();
        var bankNames = new List<string>();

        for (var c = 1; c <= reader.ColumnCount; c++)
        {
            var h = reader.GetCellText(headerRow, c).Trim();
            if (h.Length == 0) continue;

            if (h.Contains("序号")) map.SeqCol = c;
            else if (h.Contains("关系")) map.RelationCol = c;
            else if (h.Contains("姓名")) map.NameCol = c;
            else if (h.Contains("身份证")) map.IdCardCol = c;
            else if (h.Contains("地址")) map.AddressCol = c;
            else if (h.StartsWith("房产"))
            {
                if (map.PropertyCols.Count < 3) map.PropertyCols.Add(c);
            }
            else if (h.Contains("农机") && h.Contains("功率")) map.MachineryCol = c;
            else if (h.Contains("流水")) map.FlowCols.Add(c);
            else if (h.Contains("余额"))
            {
                map.BalanceCols.Add(c);
                bankNames.Add(StripBalanceSuffix(h));
            }
        }

        // 银行名取余额列（与顺序配对的流水列同属一家银行）
        foreach (var n in bankNames) map.BankNames.Add(n);
        return map;
    }

    /// <summary>"农信社（余额）"→"农信社"；兼容全/半角括号混排（样例"农行(余额）"）。</summary>
    private static string StripBalanceSuffix(string header)
    {
        var name = BalanceSuffixRegex().Replace(header, "").Trim();
        return name.Length == 0 ? header : name;
    }

    [GeneratedRegex(@"[\（(]?\s*余额\s*[\）)]?\s*$")]
    private static partial Regex BalanceSuffixRegex();

    #endregion

    #region 数据行解析

    private List<ParsedRow> ParseRows(IExcelSheetReader reader, int headerRow, ColumnMap map,
        ImportResult result, CancellationToken ct)
    {
        var rows = new List<ParsedRow>();
        var pairCount = Math.Min(map.BalanceCols.Count, map.FlowCols.Count);
        var bankCount = Math.Min(pairCount, map.BankNames.Count);
        var skipCount = 0;

        for (var r = headerRow + 1; r <= reader.RowCount; r++)
        {
            if (ct.IsCancellationRequested) break;

            var nameCell = map.NameCol > 0 ? reader.GetCellText(r, map.NameCol).Trim() : "";
            var idCardCell = map.IdCardCol > 0 ? reader.GetCellText(r, map.IdCardCol).Trim() : "";

            // 第二行表头（R3）按单元格文本识别跳过；空行跳过
            if (nameCell.Contains("姓名") || idCardCell.Contains("身份证"))
            {
                skipCount++;
                continue;
            }
            if (nameCell.Length == 0 && idCardCell.Length == 0) continue;

            var idCard = NormalizeIdCard(idCardCell);
            var row = new ParsedRow
            {
                RowNo = rows.Count + 1,
                Relationship = map.RelationCol > 0 ? reader.GetCellText(r, map.RelationCol).Trim() : "",
                Name = nameCell,
                IdCard = idCard,
                // 校验位存疑仍导入，标记异常，不丢数据
                IdCardValid = idCard.Length == 0 || IdCardValidator.IsValid(idCard),
                Address = map.AddressCol > 0 ? reader.GetCellText(r, map.AddressCol).Trim() : "",
                MachineryPower = ParsePower(map.MachineryCol > 0 ? reader.GetCellText(r, map.MachineryCol) : "")
            };

            for (var i = 0; i < 3; i++)
            {
                var text = i < map.PropertyCols.Count ? reader.GetCellText(r, map.PropertyCols[i]).Trim() : "";
                if (text.Length > 500) text = text[..500];
                if (i == 0) row.Property1 = text;
                else if (i == 1) row.Property2 = text;
                else row.Property3 = text;
            }

            decimal balanceSum = 0, flowMax = 0;
            for (var i = 0; i < bankCount; i++)
            {
                var balance = ParseAmount(reader.GetCellText(r, map.BalanceCols[i]));
                var flow = ParseAmount(reader.GetCellText(r, map.FlowCols[i]));
                balanceSum += balance;
                if (flow > flowMax) flowMax = flow;
                row.Banks.Add((map.BankNames[i], balance, flow));
            }
            row.TotalBalance = balanceSum;
            row.MaxFlow = flowMax;

            rows.Add(row);
        }

        if (skipCount > 0) LogDebug($"跳过表头/异常行 {skipCount} 行");
        return rows;
    }

    /// <summary>金额清洗：￥/¥、千分位、"元"、全角空格；空值→0。</summary>
    private static decimal ParseAmount(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var t = text.Replace("¥", "").Replace("￥", "")
                    .Replace(",", "").Replace("元", "")
                    .Replace("\u3000", "").Trim();
        if (t.Length == 0) return 0;
        return decimal.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>农机功率："60"/"60马力"→60；非数值→null（源值无损可回溯由校验告警提示）。</summary>
    private static decimal? ParsePower(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Replace("马力", "").Replace(" ", "").Trim();
        return decimal.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static string NormalizeIdCard(string text)
    {
        var t = text.Replace(" ", "").Replace("\u3000", "").ToUpperInvariant();
        return t;
    }

    #endregion

    #region 超标判定（家庭分组，仅提示）

    /// <summary>
    /// 家庭分组：连续行中关系含「户主」或等于「本人」开新户，配偶/子女挂上一户；
    /// 判定 家庭合计余额 &gt; 阈值(月标准×30) × 家庭人数 → 全员标记（仅红字提示，不自动决策）。
    /// </summary>
    private static void MarkExceed(List<ParsedRow> rows, decimal perCapitaThreshold)
    {
        if (perCapitaThreshold <= 0 || rows.Count == 0) return;

        var familyIdx = -1;
        var familyStart = new List<int>();      // 每户起始下标
        var familyOf = new int[rows.Count];

        for (var i = 0; i < rows.Count; i++)
        {
            var rel = rows[i].Relationship;
            var startNew = rel.Contains("户主") || rel == "本人";
            if (familyIdx < 0 || startNew)
            {
                familyIdx++;
                familyStart.Add(i);
            }
            familyOf[i] = familyIdx;
        }

        var totals = new decimal[familyStart.Count];
        var sizes = new int[familyStart.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            totals[familyOf[i]] += rows[i].TotalBalance;
            sizes[familyOf[i]]++;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].IsExceed = totals[familyOf[i]] > perCapitaThreshold * sizes[familyOf[i]];
        }
    }

    /// <summary>阈值 = 30 × 月低保标准（城乡取较低者从严提示）。两标准均读取失败 → 0（不判定、告警，禁止硬编码回退）。</summary>
    private async Task<decimal> ResolvePerCapitaThresholdAsync(ImportResult result, CancellationToken ct)
    {
        try
        {
            var rural = await _standardConfigService.GetStandardValueAsync(
                "RuralSubsistenceStandard", "Rural", null, ct);
            var urban = await _standardConfigService.GetStandardValueAsync(
                "UrbanSubsistenceStandard", "Urban", null, ct);

            var standards = new List<decimal>();
            if (rural.IsSuccess && rural.Value > 0) standards.Add(rural.Value);
            if (urban.IsSuccess && urban.Value > 0) standards.Add(urban.Value);

            if (standards.Count == 0)
            {
                result.Warnings.Add("低保标准读取失败，本批未执行超阈值初筛（请在「标准配置管理」维护后重新导入）");
                LogWarn("低保标准读取失败，超阈值判定跳过");
                return 0;
            }

            return standards.Min() * BankFlowConstants.BalanceLimitMonths;
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"低保标准读取异常，本批未执行超阈值初筛：{ex.Message}");
            LogWarn($"低保标准读取异常: {ex.Message}");
            return 0;
        }
    }

    #endregion

    #region 入库

    private async Task<long> InsertBatchAsync(string batchNo, string filePath, string fileHash,
        List<ParsedRow> rows, decimal threshold, CancellationToken ct)
    {
        var exceedCount = rows.Count(r => r.IsExceed);
        var invalidCount = rows.Count(r => !r.IdCardValid);
        var fileName = Path.GetFileName(filePath);
        if (fileName.Length > 500) fileName = fileName[..500];

        var sql = @"
            INSERT INTO nc_biz_bank_flow_imports
            (batch_no, source_file_name, file_hash, total_count, exceed_count,
             invalid_id_card_count, exceed_threshold, created_by, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, NOW())
            RETURNING id";

        var result = await DatabaseService.ExecuteScalarAsync<long>(sql, ct,
            batchNo, fileName, string.IsNullOrEmpty(fileHash) ? DBNull.Value : fileHash,
            rows.Count, exceedCount, invalidCount,
            threshold, (object?)App.CurrentUserId ?? DBNull.Value);

        return result.IsSuccess && result.Value > 0 ? result.Value : -1;
    }

    private async Task InsertRecordsAsync(long batchId, List<ParsedRow> rows, CancellationToken ct)
    {
        var totalBatches = (int)Math.Ceiling(rows.Count / (double)BatchSize);

        for (var b = 0; b < totalBatches; b++)
        {
            ct.ThrowIfCancellationRequested();

            var chunk = rows.Skip(b * BatchSize).Take(BatchSize).ToList();
            var sb = new StringBuilder();
            sb.Append("INSERT INTO nc_biz_bank_flow_records ");
            sb.Append("(import_id, row_no, person_name, id_card, id_card_valid, relationship, family_address, ");
            sb.AppendLine("total_balance, max_flow_amount, property_1, property_2, property_3, machinery_power, is_exceed) VALUES ");

            var values = new List<string>();
            var parameters = new List<object>();

            for (var i = 0; i < chunk.Count; i++)
            {
                var p = chunk[i];
                var o = i * 14;
                values.Add($"(${o + 1}, ${o + 2}, ${o + 3}, ${o + 4}, ${o + 5}, ${o + 6}, ${o + 7}, " +
                           $"${o + 8}, ${o + 9}, ${o + 10}, ${o + 11}, ${o + 12}, ${o + 13}, ${o + 14})");
                parameters.Add(batchId);
                parameters.Add(p.RowNo);
                parameters.Add(p.Name);
                parameters.Add(p.IdCard);
                parameters.Add(p.IdCardValid);
                parameters.Add(p.Relationship);
                parameters.Add(p.Address);
                parameters.Add(p.TotalBalance);
                parameters.Add(p.MaxFlow);
                parameters.Add(p.Property1);
                parameters.Add(p.Property2);
                parameters.Add(p.Property3);
                parameters.Add((object?)p.MachineryPower ?? DBNull.Value);
                parameters.Add(p.IsExceed);
            }

            var sql = sb.ToString() + string.Join(", ", values);
            var result = await DatabaseService.ExecuteNonQueryAsync(sql, ct, parameters.ToArray());
            if (result.IsFailure)
                throw new InvalidOperationException($"人员汇总写入失败: {result.Message}");
        }
    }

    private async Task InsertItemsAsync(List<BankItem> items, CancellationToken ct)
    {
        if (items.Count == 0) return;

        var totalBatches = (int)Math.Ceiling(items.Count / (double)BatchSize);

        for (var b = 0; b < totalBatches; b++)
        {
            ct.ThrowIfCancellationRequested();

            var chunk = items.Skip(b * BatchSize).Take(BatchSize).ToList();
            var sb = new StringBuilder();
            sb.Append("INSERT INTO nc_biz_bank_flow_items (record_id, bank_name, balance, max_flow_amount) VALUES ");

            var values = new List<string>();
            var parameters = new List<object>();

            for (var i = 0; i < chunk.Count; i++)
            {
                var p = chunk[i];
                var o = i * 4;
                values.Add($"(${o + 1}, ${o + 2}, ${o + 3}, ${o + 4})");
                parameters.Add(p.RecordId);
                parameters.Add(p.BankName);
                parameters.Add(p.Balance);
                parameters.Add(p.MaxFlow);
            }

            var sql = sb.ToString() + string.Join(", ", values);
            var result = await DatabaseService.ExecuteNonQueryAsync(sql, ct, parameters.ToArray());
            if (result.IsFailure)
                throw new InvalidOperationException($"银行明细写入失败: {result.Message}");
        }
    }

    #endregion

    #region 批次号与文件哈希

    /// <summary>批次号：优先文件名/标题中的"yyyy年M月"，否则"年(文件修改年)+月"，再否则当前年月。</summary>
    private static (string BatchNo, string Raw) DeriveBatchNo(string filePath, IExcelSheetReader reader)
    {
        var title = reader.GetCellText(1, 1);
        var source = Path.GetFileNameWithoutExtension(filePath) + " " + title;

        int? year = null, month = null;

        var ym = YearMonthRegex().Match(source);
        if (ym.Success)
        {
            year = int.Parse(ym.Groups["y"].Value);
            month = int.Parse(ym.Groups["m"].Value);
        }
        else
        {
            var m = MonthRegex().Match(source);
            if (m.Success) month = int.Parse(m.Groups["m"].Value);

            var y = YearRegex().Match(source);
            if (y.Success) year = int.Parse(y.Value);
        }

        year ??= SafeFileYear(filePath);
        month ??= DateTime.Now.Month;

        return ($"{year:0000}-{month:00}", source.Trim());
    }

    private static int SafeFileYear(string filePath)
    {
        try { return File.GetLastWriteTime(filePath).Year; }
        catch { return DateTime.Now.Year; }
    }

    [GeneratedRegex(@"(?<y>20\d{2})\s*年\s*(?<m>\d{1,2})\s*月")]
    private static partial Regex YearMonthRegex();

    [GeneratedRegex(@"(?<m>\d{1,2})\s*月")]
    private static partial Regex MonthRegex();

    [GeneratedRegex(@"20\d{2}")]
    private static partial Regex YearRegex();

    private static string ComputeFileHash(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            // 哈希失败不阻断导入（去重降级为按文件名提示）
            return "";
        }
        try
        {
            using var stream = File.OpenRead(filePath);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            // 哈希失败不阻断导入（去重降级为按文件名提示）
            return "";
        }
    }

    #endregion
}
