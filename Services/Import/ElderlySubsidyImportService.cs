using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using System.Globalization;

namespace NewCosmos.Services.Import;

public partial class ElderlySubsidyImportService : BaseImportService
{
    private readonly IPinyinConverter _pinyinConverter;

    public override string ImportTypeName => ImportTypeCodes.ELDERLY_SUBSIDY;

    private static readonly Dictionary<string, string[]> ColumnAliases = new()
    {
        ["name"] = new[] { "老人姓名" },
        ["id_card"] = new[] { "身份证号码" },
        ["phone"] = new[] { "老人联系方式" },
        ["address"] = new[] { "户籍地址" },
        ["subsidy_amount"] = new[] { "发放金额" },
        ["bank_account"] = new[] { "银行卡号" },
        ["person_type"] = new[] { "人员类型" },
        ["original_type"] = new[] { "原始类型" }
    };

    public ElderlySubsidyImportService(
        IDatabaseService databaseService,
        IPinyinConverter pinyinConverter,
        ILoggerService logger) : base(databaseService, logger)
    {
        _pinyinConverter = pinyinConverter;
    }

    public override async Task<Result> ClearTableAsync(CancellationToken ct = default)
    {
        try
        {
            await DatabaseService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_biz_elderly_subsidy_history RESTART IDENTITY CASCADE", ct);
            LogInfo("高龄补贴数据表已清空");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogError($"清空高龄补贴数据表失败: {ex.Message}");
            return Result.Failure("CLEAR_TABLE_FAILED", $"清空高龄补贴数据表失败: {ex.Message}");
        }
    }

    protected override async Task<ImportResult> ImportSingleFileAsync(string filePath, IProgress<string> progress = null, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(filePath);

        if (!string.Equals(fileName, "津贴对象.xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return ImportResult.Failed($"文件名必须为'津贴对象.xlsx'，当前文件名: {fileName}");
        }

        return await base.ImportSingleFileAsync(filePath, progress, ct);
    }

    protected override async Task ProcessWorksheetAsync(IExcelSheetReader reader, ImportResult result, IProgress<string>? progress, CancellationToken ct)
    {
        // 数据年度为导入当年（原 Singleton 实例字段存在并发导入交错写错风险）
        var dataYear = DateTime.Now.Year;
        var rowCount = reader.RowCount;
        var colCount = reader.ColumnCount;
        LogInfo($"诊断: RowCount={rowCount}, ColumnCount={colCount}");

        var headerParts = new List<string>();
        for (var c = 1; c <= colCount; c++)
        {
            var cellText = reader.GetCellText(2, c);
            headerParts.Add($"列{c}=[{cellText}]");
        }
        LogInfo($"诊断: 第2行表头: {string.Join(", ", headerParts)}");

        var mapping = BuildColumnMapping(reader, ColumnAliases, 2);
        var mappingParts = new List<string>();
        foreach (var kvp in mapping)
            mappingParts.Add($"{kvp.Key}->列{kvp.Value}");
        LogInfo($"诊断: 列映射结果: {string.Join(", ", mappingParts)}");

        if (!mapping.TryGetValue("name", out _) || !mapping.TryGetValue("id_card", out _))
        {
            result.Errors.Add("未找到必需的列：姓名或身份证号");
            return;
        }

        var sampleName = ReadCellString(reader, 3, mapping, "name");
        var sampleIdCard = ReadCellString(reader, 3, mapping, "id_card");
        LogInfo($"诊断: 第3行数据: name=[{DataMasker.MaskName(sampleName)}], id_card=[{DataMasker.MaskIdCard(sampleIdCard)}]");

        var successCount = 0;
        var errorCount = 0;
        var skipCount = 0;
        var excludeCount = 0;
        var excludeSamples = new List<string>();

        // 加载查重集：当前库已登记（未删除）身份证 + 死亡记录身份证
        // 已登记/已死亡的人员拒绝再次导入（含原户主死亡等场景）
        // §6：查重集加载失败中止导入（空查重集会导致全部数据重复导入）
        var existingIdsResult = await LoadExistingIdCardSetAsync(ct);
        if (existingIdsResult.IsFailure)
        {
            result.Errors.Add(existingIdsResult.Message ?? "加载查重集失败，已中止导入");
            return;
        }
        var existingIds = existingIdsResult.Value!;

        for (var row = 3; row <= rowCount; row++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var name = ReadCellString(reader, row, mapping, "name");
                var idCard = ReadCellString(reader, row, mapping, "id_card");

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(idCard))
                {
                    skipCount++;
                    continue;
                }

                // 排除当前库已有/已死亡人员
                if (existingIds.Contains(idCard))
                {
                    excludeCount++;
                    if (excludeSamples.Count < 5)
                        excludeSamples.Add(DataMasker.MaskIdCard(idCard));
                    continue;
                }

                var phone = ReadCellString(reader, row, mapping, "phone");
                var address = ReadCellString(reader, row, mapping, "address");
                var subsidyAmount = ReadDecimal(reader, row, mapping, "subsidy_amount");
                var bankAccount = ReadCellString(reader, row, mapping, "bank_account");
                var personType = ReadCellString(reader, row, mapping, "person_type");
                var originalType = ReadCellString(reader, row, mapping, "original_type");

                var gender = GetGenderFromIdCard(idCard);
                var birthDate = GetBirthDateFromIdCard(idCard);
                int? age = birthDate.HasValue ? (int)CalculateAge(birthDate.Value) : null;
                var pinyinName = _pinyinConverter.ToPinyin(name);

                var sql = @"
                    INSERT INTO nc_biz_elderly_subsidy_history 
                    (name, pinyin_name, id_card, gender, birth_date, age, phone, address, 
                     subsidy_amount, bank_account, person_type, original_type, data_year, imported_at)
                    VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, CURRENT_TIMESTAMP);";

                await DatabaseService.ExecuteNonQueryAsync("DELETE FROM nc_biz_elderly_subsidy_history WHERE id_card = $1 AND data_year = $2", ct, idCard, dataYear);
                var execResult = await DatabaseService.ExecuteNonQueryAsync(sql, ct,
                    name!, pinyinName!, idCard!, gender!, (object)birthDate!, (object)age!, phone, address,
                    subsidyAmount, bankAccount, personType, originalType, dataYear);

                if (execResult.IsSuccess)
                {
                    successCount++;
                }
                else
                {
                    errorCount++;
                    result.Errors.Add($"第{row}行导入失败: {execResult.Message}");
                }

                if (successCount % ProgressReportInterval == 0)
                {
                    progress?.Report($"已处理 {successCount} 条数据...");
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                LogError($"第{row}行导入失败: {ex.Message}");
            }
        }

        result.ImportedCount = successCount;
        result.ErrorCount = errorCount;
        if (excludeCount > 0)
        {
            var sampleText = excludeSamples.Count > 0 ? $"，示例: {string.Join("、", excludeSamples)}" : "";
            result.Warnings.Add($"已排除当前库已存在/已死亡 {excludeCount} 条{sampleText}");
        }
        progress?.Report($"处理完成，成功 {successCount} 条，失败 {errorCount} 条" + (excludeCount > 0 ? $"，排除已存在 {excludeCount} 条" : ""));

        LogInfo($"高龄补贴导入完成: 成功 {successCount} 条，失败 {errorCount} 条，跳过 {skipCount} 条，排除已存在 {excludeCount} 条");
    }

    /// <summary>
    /// 加载查重集：当前库已登记（未删除）身份证 + 死亡记录身份证（nc_biz_death_records.member_id_card）。
    /// §6：加载失败显式失败——查重集为空会导致全部数据按"新数据"重复导入。
    /// </summary>
    private async Task<Result<HashSet<string>>> LoadExistingIdCardSetAsync(CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var apps = await DatabaseService.QueryAsync<string>(
                "SELECT id_card FROM nc_biz_elderly_applications WHERE deleted_at IS NULL AND id_card IS NOT NULL", ct);
            if (apps.IsFailure)
                return Result.Failure<HashSet<string>>(
                    apps.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    $"加载高龄导入查重集失败: {apps.Message}");
            if (apps.Value != null)
            {
                foreach (var id in apps.Value)
                {
                    if (!string.IsNullOrWhiteSpace(id)) set.Add(id.Trim());
                }
            }

            var deaths = await DatabaseService.QueryAsync<string>(
                "SELECT member_id_card FROM nc_biz_death_records WHERE member_id_card IS NOT NULL", ct);
            if (deaths.IsFailure)
                return Result.Failure<HashSet<string>>(
                    deaths.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    $"加载高龄导入查重集失败: {deaths.Message}");
            if (deaths.Value != null)
            {
                foreach (var id in deaths.Value)
                {
                    if (!string.IsNullOrWhiteSpace(id)) set.Add(id.Trim());
                }
            }

            LogInfo($"高龄导入查重集加载完成: {set.Count} 个身份证");
        }
        catch (Exception ex)
        {
            LogError($"加载高龄导入查重集失败: {ex.Message}");
            return Result.FromException<HashSet<string>>(ex);
        }
        return Result.Success(set);
    }

    private static string? GetGenderFromIdCard(string idCard)
    {
        if (idCard.Length != 18)
            return null;

        var ch = idCard[16];
        if (!char.IsDigit(ch))
            return null;
        return (ch - '0') % 2 == 1 ? "男" : "女";
    }

    private static DateTime? GetBirthDateFromIdCard(string idCard)
    {
        // 结构化解析（TryParseExact）：非数字/非法日期返回 null，不走异常路径
        if (idCard.Length != 18)
            return null;

        return DateTime.TryParseExact(
            idCard.Substring(6, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
    }

    private static int CalculateAge(DateTime birthDate)
    {
        var today = DateTime.Today;
        var age = today.Year - birthDate.Year;
        if (birthDate.Date > today.AddYears(-age))
            age--;

        return age;
    }
}
