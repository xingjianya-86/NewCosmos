using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NewCosmos.Services.Import;

public abstract class BaseCombinedImportService : BaseService, ICombinedImportService
{
    /// <summary>表名白名单统一走 Helpers.TableNameValidator（原本地白名单为其子集，已删除）。</summary>
    protected static void ValidateTableName(string tableName)
    {
        Helpers.TableNameValidator.ValidateOrThrow(tableName);
    }

    protected readonly IDatabaseService DatabaseService;
    protected readonly IDictCacheService DictCache;
    protected int BatchSize { get; set; } = 500;
    protected int ProgressReportInterval { get; set; } = 100;

    protected override string ServiceName => ImportTypeName;
    public abstract string ImportTypeName { get; }
    public abstract string FamilyFilePattern { get; }
    public abstract string PersonFilePattern { get; }
    protected abstract string FamilyTableName { get; }
    protected abstract string PersonTableName { get; }
    protected abstract Dictionary<string, string[]> FamilyColumnAliases { get; }
    protected abstract Dictionary<string, string[]> PersonColumnAliases { get; }

    /// <summary>列值读取方式。String/Int/Decimal/Bool 直接按列读取；其余为保留各服务原有行为的派生列。</summary>
    protected enum ImportValueKind
    {
        String,
        Int,
        Decimal,
        Bool,
        /// <summary>人员表 head_id_card：为空时回退为本人身份证号。</summary>
        HeadIdCard,
        /// <summary>gender：为空且身份证为 18 位时由身份证推导。</summary>
        GenderFromIdCard,
        /// <summary>birth_date：文本可解析则用文本，否则由 18 位身份证推导（可能为 null）。</summary>
        BirthDateFromIdCard,
    }

    /// <summary>
    /// 一列的导入规格。DbColumn 同时是数据库列名与列映射（别名字典）的键。
    /// Kind=String 且 DictCategory 非空时经 MapFieldToKey 归一化为字典 key。
    /// </summary>
    protected sealed record ImportColumnSpec(string DbColumn, ImportValueKind Kind, int DefaultInt = 0, string? DictCategory = null);

    /// <summary>家庭文件的有序列规格（不含末尾 imported_at，由基类以 CURRENT_TIMESTAMP 追加）。</summary>
    protected abstract IReadOnlyList<ImportColumnSpec> FamilyColumns { get; }

    /// <summary>人员文件的有序列规格（不含末尾 imported_at）。</summary>
    protected abstract IReadOnlyList<ImportColumnSpec> PersonColumns { get; }

    /// <summary>家庭文件的键列（DELETE ... = ANY 去重键）。</summary>
    protected virtual string FamilyKeyColumn => "applicant_id_card";

    /// <summary>家庭文件用于空行跳过检查的姓名列。</summary>
    protected virtual string FamilyNameColumn => "applicant_name";

    /// <summary>人员文件的键列。</summary>
    protected virtual string PersonKeyColumn => "id_card";

    /// <summary>人员文件用于空行跳过检查的姓名列。</summary>
    protected virtual string PersonNameColumn => "name";

    protected BaseCombinedImportService(IDatabaseService databaseService, IDictCacheService dictCache, ILoggerService logger) : base(logger)
    {
        DatabaseService = databaseService;
        DictCache = dictCache;
    }

    /// <summary>
    /// 本次导入的行级错误。ImportCombinedAsync 开始时清空，结束前并入 result.Errors——
    /// 有任何行级错误即整体回滚（与 BaseImportService 的"全有或全无"语义一致）。
    /// 注意：导入由 UI 串行触发（IsBusy 互斥），此字段不做并发防护。
    /// </summary>
    protected readonly List<string> RowErrors = new();

    /// <summary>
    /// 本次导入的非致命警告（如文件内重复键），结束时并入 result.Warnings，不触发回滚。
    /// </summary>
    protected readonly List<string> ImportWarnings = new();

    // ── 当前库查重（排除已存在/已死亡）──
    // 服务注册为 Singleton；并发导入由 _importGate 串行化，实例字段不再被交叉污染。
    private HashSet<string> _existingIdCards = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _headToMemberCards = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _rejectedHeadCards = new(StringComparer.OrdinalIgnoreCase);
    private int _excludeFamilyCount;
    private int _excludePersonCount;
    private readonly List<string> _excludeSamples = new();

    /// <summary>整合导入互斥锁：串行化并发导入，避免实例状态（查重集/排除计数）互相污染。</summary>
    private readonly SemaphoreSlim _importGate = new(1, 1);

    /// <summary>
    /// 记录一条行级错误（写入 RowErrors 并记日志）。子类的行循环 catch 中必须调用，
    /// 禁止只记日志——那会导致"全部行失败仍提交并报成功"。
    /// </summary>
    protected void RecordRowError(int rowNumber, string message)
    {
        RowErrors.Add($"第 {rowNumber} 行: {message}");
        LogError($"第 {rowNumber} 行导入失败: {message}");
    }

    /// <summary>
    /// 执行行级 SQL 并检查结果；失败时记入 RowErrors 并返回 false。
    /// 子类行循环内的 DELETE/INSERT 必须经此方法（直接调 ExecuteNonQueryAsync 会静默丢弃失败）。
    /// </summary>
    protected async Task<bool> ExecuteRowAsync(string sql, int rowNumber, CancellationToken ct, params object[] args)
    {
        var result = await DatabaseService.ExecuteNonQueryAsync(sql, ct, args);
        if (result.IsFailure)
        {
            RecordRowError(rowNumber, result.Message ?? "SQL 执行失败");
            return false;
        }
        return true;
    }

    protected string MapFieldToKey(string category, string? rawValue)
        => ImportedDataMapper.MapToKey(rawValue, category, DictCache);

    public async Task<CombinedPreviewResult> PreviewCombinedAsync(
        string familyFilePath,
        string personFilePath,
        int previewRows = 10,
        CancellationToken ct = default)
    {
        var result = new CombinedPreviewResult
        {
            FamilyFilePath = familyFilePath,
            PersonFilePath = personFilePath
        };

        try
        {
            var familyPreview = await PreviewFileAsync(familyFilePath, FamilyColumnAliases, previewRows, ct);
            var personPreview = await PreviewFileAsync(personFilePath, PersonColumnAliases, previewRows, ct);

            if (!string.IsNullOrEmpty(familyPreview.ErrorMessage) || !string.IsNullOrEmpty(personPreview.ErrorMessage))
            {
                var errors = new List<string>();
                if (!string.IsNullOrEmpty(familyPreview.ErrorMessage))
                    errors.Add($"家庭文件: {familyPreview.ErrorMessage}");
                if (!string.IsNullOrEmpty(personPreview.ErrorMessage))
                    errors.Add($"人员文件: {personPreview.ErrorMessage}");
                result.Success = false;
                result.ErrorMessage = string.Join("; ", errors);
                LogError($"预览失败: {result.ErrorMessage}");
                return result;
            }

            result.FamilyTotalRows = familyPreview.TotalRows;
            result.FamilyColumnMappings = familyPreview.ColumnMappings;
            result.FamilyPreviewRows = familyPreview.PreviewRows;

            result.PersonTotalRows = personPreview.TotalRows;
            result.PersonColumnMappings = personPreview.ColumnMappings;
            result.PersonPreviewRows = personPreview.PreviewRows;

            result.Success = true;
            LogInfo($"预览完成: 家庭 {result.FamilyTotalRows} 行, 人员 {result.PersonTotalRows} 行");
        }
        catch (Exception ex)
        {
            LogError($"操作失败: {ex.Message}");
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private Task<ImportPreviewResult> PreviewFileAsync(
        string filePath,
        Dictionary<string, string[]> columnAliases,
        int previewRows,
        CancellationToken ct)
    {
        var result = new ImportPreviewResult { FilePath = filePath };

        if (!File.Exists(filePath))
        {
            result.ErrorMessage = $"文件不存在: {filePath}";
            return Task.FromResult(result);
        }

        using var reader = SheetReaderFactory.Create(filePath);
        if (reader.IsEmpty)
        {
            result.ErrorMessage = "Excel文件为空";
            return Task.FromResult(result);
        }

        var mapping = BuildColumnMapping(reader, columnAliases);
        if (mapping == null || mapping.Count == 0)
        {
            result.ErrorMessage = "无法识别列映射：Excel 表头与导入模板不匹配，请检查表头名称";
            return Task.FromResult(result);
        }

        result.ColumnMappings = mapping.Select(m => new ColumnMappingInfo
        {
            SourceColumn = m.Value.ToString(),
            TargetField = m.Key,
            IsMapped = true
        }).ToList();

        var rowCount = reader.RowCount;
        var previewRowCount = Math.Min(previewRows, rowCount - 1);

        for (var row = 2; row <= previewRowCount + 1; row++)
        {
            var rowData = new Dictionary<string, object?>();
            foreach (var kvp in mapping)
            {
                rowData[kvp.Key] = reader.GetCellText(row, kvp.Value);
            }
            result.PreviewRows.Add(rowData);
        }

        result.TotalRows = rowCount - 1;
        result.Success = true;
        return Task.FromResult(result);
    }

    public async Task<Result> ClearTablesAsync(CancellationToken ct = default)
    {
        LogInfo("开始清空家庭和人员表");
        try
        {
            ValidateTableName(PersonTableName);
            ValidateTableName(FamilyTableName);

            var personResult = await DatabaseService.ExecuteNonQueryAsync($"TRUNCATE TABLE {PersonTableName} RESTART IDENTITY CASCADE", ct);
            if (personResult.IsFailure)
            {
                return Result.Failure(personResult.ErrorCode!, "清空人员表失败");
            }

            var familyResult = await DatabaseService.ExecuteNonQueryAsync($"TRUNCATE TABLE {FamilyTableName} RESTART IDENTITY CASCADE", ct);
            if (familyResult.IsFailure)
            {
                return Result.Failure(familyResult.ErrorCode!, "清空家庭表失败");
            }

            LogInfo($"表已清空: {FamilyTableName}");
            Logger.LogBusiness("导入表已清空", ("FamilyTable", FamilyTableName), ("PersonTable", PersonTableName));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogError($"操作失败: {ex.Message}");
            return Result.FromException(ex);
        }
    }

    public async Task<CombinedImportResult> ImportCombinedAsync(
        string familyFilePath,
        string personFilePath,
        bool clearBeforeImport,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        await _importGate.WaitAsync(ct);
        try
        {
            return await ImportCombinedCoreAsync(familyFilePath, personFilePath, clearBeforeImport, progress, ct);
        }
        finally
        {
            _importGate.Release();
        }
    }

    private async Task<CombinedImportResult> ImportCombinedCoreAsync(
        string familyFilePath,
        string personFilePath,
        bool clearBeforeImport,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var result = new CombinedImportResult();
        var startTime = DateTime.UtcNow;
        RowErrors.Clear();
        ImportWarnings.Clear();
        _existingIdCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _headToMemberCards = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        _rejectedHeadCards.Clear();
        _excludeFamilyCount = 0;
        _excludePersonCount = 0;
        _excludeSamples.Clear();

        try
        {
            await using var tx = await DatabaseService.BeginTransactionScopeAsync(ct);
            try
            {
                // 排除当前库已存在/已死亡：加载查重集 + 预读人员文件构建"户主→成员身份证"映射
                // （户主可能已死亡、家庭已由他人建档，故须扫描全家任一成员命中即拒整户）
                await LoadExistingIdCardSetAsync(ct);
                BuildHeadToMemberCards(personFilePath);

                if (clearBeforeImport)
                {
                    // ⚠️ 清空必须发生在事务内！历史实现把 TRUNCATE 放在事务之外先行提交——
                    // 之后导入任何一步失败虽然回滚，但被清空的存量数据已永久丢失。
                    // 事务内用 DELETE（不用 TRUNCATE RESTART IDENTITY CASCADE：CASCADE 波及面不可控）。
                    progress?.Report("开始清空表...");
                    ValidateTableName(PersonTableName);
                    ValidateTableName(FamilyTableName);
                    var clearPersons = await DatabaseService.ExecuteNonQueryAsync($"DELETE FROM {PersonTableName}", ct);
                    if (clearPersons.IsFailure)
                        throw new BusinessException(clearPersons.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, $"清空人员表失败: {clearPersons.Message}");
                    var clearFamilies = await DatabaseService.ExecuteNonQueryAsync($"DELETE FROM {FamilyTableName}", ct);
                    if (clearFamilies.IsFailure)
                        throw new BusinessException(clearFamilies.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, $"清空家庭表失败: {clearFamilies.Message}");
                }

                // ⚠️ 解除人员表对家庭表的外键引用（family_id → families.id）：
                // 组合导入是"先删后插"，删除家庭行时若人员表仍引用这些家庭会触发 23503 外键违反。
                // 清空引用后再删家庭/插家庭，最后 LinkPersonsToFamiliesAsync 会按 head_id_card 全量重建关联。
                ValidateTableName(PersonTableName);
                var clearRefs = await DatabaseService.ExecuteNonQueryAsync(
                    $"UPDATE {PersonTableName} SET family_id = NULL WHERE family_id IS NOT NULL", ct);
                if (clearRefs.IsFailure)
                    throw new BusinessException(clearRefs.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, $"解除人员外键引用失败: {clearRefs.Message}");

                progress?.Report("开始导入家庭数据...");
                var familyCount = await ImportFamilyFileAsync(familyFilePath, progress, ct);
                result.FamilyImportedCount = familyCount;
                progress?.Report("开始导入人员数据...");
                var personCount = await ImportPersonFileAsync(personFilePath, progress, ct);
                result.PersonImportedCount = personCount;

                progress?.Report("开始关联家庭与人员...");
                var linkResult = await LinkPersonsToFamiliesAsync(ct);
                result.LinkedCount = linkResult.linkedCount;
                result.UnlinkedCount = linkResult.unlinkedCount;
                result.UnlinkedPersons = linkResult.unlinkedPersons;

                // 行级错误并入结果：任何一行失败 → 整体回滚（全有或全无）
                result.Errors.AddRange(RowErrors);
                // 非致命警告（如文件内重复键）并入结果，不影响提交
                result.Warnings.AddRange(ImportWarnings);

                // 排除当前库已存在/已死亡提示（家庭整户拒绝 + 人员跳过）
                if (_excludeFamilyCount > 0 || _excludePersonCount > 0)
                {
                    var sampleText = _excludeSamples.Count > 0 ? $"，示例: {string.Join("、", _excludeSamples)}" : "";
                    var warn = $"已排除当前库已存在/已死亡：家庭 {_excludeFamilyCount} 户、人员 {_excludePersonCount} 条{sampleText}";
                    result.Warnings.Add(warn);
                    LogWarn(warn);
                }

                if (result.Errors.Count == 0)
                {
                    await tx.CommitAsync(ct);
                    result.Success = true;
                }
                else
                {
                    await tx.RollbackAsync(ct);
                    result.ErrorMessage = $"导入因错误回滚: {string.Join("; ", result.Errors.Take(5))}";
                }
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            result.Warnings.Add("导入已取消");
            LogWarn("导入被取消");
        }
        catch (Exception ex)
        {
            LogError($"操作失败: {ex.Message}");
            result.ErrorMessage = ex.Message;
            result.Errors.Add(ex.Message);
        }

        result.Duration = DateTime.UtcNow - startTime;
        LogInfo($"整合导入完成: 家庭 {result.FamilyImportedCount} 条, 人员 {result.PersonImportedCount} 条, 关联 {result.LinkedCount} 条");
        Logger.LogBusiness("整合导入完成",
            ("Type", ImportTypeName),
            ("Families", result.FamilyImportedCount),
            ("Persons", result.PersonImportedCount),
            ("Linked", result.LinkedCount),
            ("Unlinked", result.UnlinkedCount));

        return result;
    }

    protected virtual Task<int> ImportFamilyFileAsync(string filePath, IProgress<string>? progress, CancellationToken ct)
        => ImportFileAsync(filePath, "家庭", FamilyTableName, FamilyKeyColumn, FamilyNameColumn,
            FamilyColumns, FamilyColumnAliases, warnOnSkippedRows: true, progress, ct);

    protected virtual Task<int> ImportPersonFileAsync(string filePath, IProgress<string>? progress, CancellationToken ct)
        => ImportFileAsync(filePath, "人员", PersonTableName, PersonKeyColumn, PersonNameColumn,
            PersonColumns, PersonColumnAliases, warnOnSkippedRows: false, progress, ct);

    /// <summary>
    /// PostgreSQL 扩展查询协议单条语句参数上限为 65535，这里取 60000 留余量；
    /// 实际批大小 = min(BatchSize, (60000-1)/列数)，保证 行数×列数 &lt; 60000。
    /// </summary>
    private const int MaxParametersPerStatement = 60000;

    private readonly record struct BufferedRow(int RowNumber, string Key, object?[] Values);

    /// <summary>
    /// 通用导入管线：读取 Excel → 按列规格取值 → 按批 DELETE(= ANY) + 单条多行 INSERT。
    /// 文件内重复键保留最后一行（批内去重 + 跨批先删后插），重复数量计入 ImportWarnings。
    /// 返回本次成功写入的（按键去重后）行数。
    /// </summary>
    protected async Task<int> ImportFileAsync(
        string filePath,
        string fileLabel,
        string tableName,
        string keyColumn,
        string nameColumn,
        IReadOnlyList<ImportColumnSpec> columns,
        Dictionary<string, string[]> columnAliases,
        bool warnOnSkippedRows,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        if (!File.Exists(filePath))
        {
            LogError("操作失败");
            return 0;
        }

        using var reader = SheetReaderFactory.Create(filePath);
        if (reader.IsEmpty)
        {
            LogError("操作失败");
            return 0;
        }

        ValidateTableName(tableName);

        var mapping = BuildColumnMapping(reader, columnAliases);
        var rowCount = reader.RowCount;

        LogInfo($"{fileLabel}文件导入: 行数={rowCount}");

        // 防御性钳制：单条 INSERT 的参数数 = 行数 × 列数，必须 < MaxParametersPerStatement
        var effectiveBatchSize = Math.Max(1, Math.Min(BatchSize, (MaxParametersPerStatement - 1) / Math.Max(1, columns.Count)));

        var buffer = new List<BufferedRow>(effectiveBatchSize);
        var insertedKeys = new HashSet<string>(StringComparer.Ordinal);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var duplicateKeys = new HashSet<string>(StringComparer.Ordinal);
        var duplicateRowCount = 0;
        var duplicateSamples = new List<string>();

        for (var row = 2; row <= rowCount; row++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var name = ReadString(reader, row, mapping, nameColumn);
                var key = ReadString(reader, row, mapping, keyColumn);

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(key))
                {
                    if (warnOnSkippedRows && row <= 5)
                    {
                        LogWarn($"{fileLabel}第{row}行跳过: 姓名=[{DataMasker.MaskName(name)}]");
                    }
                    continue;
                }

                // 文件内重复键检测：保留最后一行（批内去重 + 先删后插），此处仅计数用于结果警告
                if (!seenKeys.Add(key))
                {
                    duplicateRowCount++;
                    if (duplicateKeys.Add(key) && duplicateSamples.Count < 5)
                    {
                        duplicateSamples.Add(DataMasker.MaskIdCard(key));
                    }
                }

                // 排除当前库已存在/已死亡（需求：户主应扫描全家成员再做关联拒绝，
                // 因可能原户主死亡、家庭已由他人建档）：
                //  - 家庭行：户主 + 该户全部成员身份证任一命中查重集 → 拒绝整户
                //  - 人员行：户主所属家庭已被拒 → 同户人员一并跳过；本人命中查重集 → 跳过
                if (string.Equals(fileLabel, "家庭", StringComparison.Ordinal))
                {
                    var familyHit = _existingIdCards.Contains(key)
                        || (_headToMemberCards.TryGetValue(key, out var memberCards)
                            && memberCards.Any(m => _existingIdCards.Contains(m)));
                    if (familyHit)
                    {
                        _rejectedHeadCards.Add(key);
                        _excludeFamilyCount++;
                        if (_excludeSamples.Count < 5)
                            _excludeSamples.Add(DataMasker.MaskIdCard(key));
                        continue;
                    }
                }
                else
                {
                    var headId = ReadString(reader, row, mapping, "head_id_card");
                    if (!string.IsNullOrEmpty(headId) && _rejectedHeadCards.Contains(headId))
                    {
                        _excludePersonCount++;
                        continue;
                    }
                    if (_existingIdCards.Contains(key))
                    {
                        _excludePersonCount++;
                        if (_excludeSamples.Count < 5)
                            _excludeSamples.Add(DataMasker.MaskIdCard(key));
                        continue;
                    }
                }

                buffer.Add(new BufferedRow(row, key, ReadRowValues(reader, row, mapping, columns, nameColumn, name, keyColumn, key)));
            }
            catch (Exception ex)
            {
                RecordRowError(row, ex.Message);
            }

            if (buffer.Count < effectiveBatchSize) continue;

            if (!await FlushBatchAsync(tableName, keyColumn, columns, buffer, insertedKeys, ct))
            {
                // 批失败已记入 RowErrors；事务处于中止状态，继续执行只会产生重复错误 → 提前结束（整体回滚，全有或全无）
                return insertedKeys.Count;
            }

            progress?.Report($"正在导入第 {insertedKeys.Count} 行");
        }

        if (!await FlushBatchAsync(tableName, keyColumn, columns, buffer, insertedKeys, ct))
        {
            return insertedKeys.Count;
        }

        if (duplicateRowCount > 0)
        {
            var warning = $"{fileLabel}文件内重复身份证 {duplicateRowCount} 个（保留最后一行），示例: {string.Join("、", duplicateSamples)}";
            ImportWarnings.Add(warning);
            LogWarn(warning);
        }

        progress?.Report($"{fileLabel}数据导入完成: {insertedKeys.Count} 条");
        return insertedKeys.Count;
    }

    /// <summary>
    /// 加载当前库查重集：nc_biz_applications 户主 + nc_biz_family_members 成员 + nc_biz_death_records 死亡记录。
    /// 用于排除当前库已存在/已死亡的人员。
    /// </summary>
    private async Task LoadExistingIdCardSetAsync(CancellationToken ct)
    {
        try
        {
            var apps = await DatabaseService.QueryAsync<string>(
                "SELECT applicant_id_card FROM nc_biz_applications WHERE deleted_at IS NULL AND applicant_id_card IS NOT NULL", ct);
            if (apps.IsSuccess && apps.Value != null)
            {
                foreach (var id in apps.Value)
                {
                    if (!string.IsNullOrWhiteSpace(id)) _existingIdCards.Add(id.Trim());
                }
            }

            var members = await DatabaseService.QueryAsync<string>(
                "SELECT id_card FROM nc_biz_family_members WHERE deleted_at IS NULL AND id_card IS NOT NULL", ct);
            if (members.IsSuccess && members.Value != null)
            {
                foreach (var id in members.Value)
                {
                    if (!string.IsNullOrWhiteSpace(id)) _existingIdCards.Add(id.Trim());
                }
            }

            var deaths = await DatabaseService.QueryAsync<string>(
                "SELECT member_id_card FROM nc_biz_death_records WHERE member_id_card IS NOT NULL", ct);
            if (deaths.IsSuccess && deaths.Value != null)
            {
                foreach (var id in deaths.Value)
                {
                    if (!string.IsNullOrWhiteSpace(id)) _existingIdCards.Add(id.Trim());
                }
            }

            LogInfo($"当前库查重集加载完成: {_existingIdCards.Count} 个身份证");
        }
        catch (Exception ex)
        {
            LogError($"加载当前库查重集失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 预读人员文件，构建"户主身份证 → 同户成员身份证列表"映射（不落库）。
    /// 家庭查重须扫描全家成员（原户主死亡时家庭已由他人建档），故需预读人员文件。
    /// </summary>
    private void BuildHeadToMemberCards(string personFilePath)
    {
        if (!File.Exists(personFilePath))
        {
            LogError("操作失败");
            return;
        }

        try
        {
            using var reader = SheetReaderFactory.Create(personFilePath);
            if (reader.IsEmpty) return;

            var mapping = BuildColumnMapping(reader, PersonColumnAliases);
            if (mapping.Count == 0) return;

            for (var row = 2; row <= reader.RowCount; row++)
            {
                var headId = ReadString(reader, row, mapping, "head_id_card");
                var idCard = ReadString(reader, row, mapping, "id_card");
                if (string.IsNullOrWhiteSpace(headId) || string.IsNullOrWhiteSpace(idCard)) continue;

                if (!_headToMemberCards.TryGetValue(headId, out var list))
                {
                    list = new List<string>();
                    _headToMemberCards[headId] = list;
                }
                list.Add(idCard);
            }

            LogInfo($"人员文件预读完成: {_headToMemberCards.Count} 个户主");
        }
        catch (Exception ex)
        {
            LogError($"预读人员文件失败: {ex.Message}");
        }
    }

    /// <summary>按列规格读取一行的全部参数值（顺序与 INSERT 列一致）。</summary>
    private object?[] ReadRowValues(
        IExcelSheetReader reader,
        int row,
        Dictionary<string, int> mapping,
        IReadOnlyList<ImportColumnSpec> columns,
        string nameColumn,
        string name,
        string keyColumn,
        string key)
    {
        var values = new object?[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            var spec = columns[i];
            values[i] = spec.Kind switch
            {
                ImportValueKind.String when spec.DbColumn == keyColumn => key,
                ImportValueKind.String when spec.DbColumn == nameColumn => name,
                ImportValueKind.String when spec.DictCategory != null =>
                    MapFieldToKey(spec.DictCategory, ReadString(reader, row, mapping, spec.DbColumn)),
                ImportValueKind.String => ReadString(reader, row, mapping, spec.DbColumn),
                ImportValueKind.Int => ReadInt(reader, row, mapping, spec.DbColumn, spec.DefaultInt),
                ImportValueKind.Decimal => ReadDecimal(reader, row, mapping, spec.DbColumn),
                ImportValueKind.Bool => ReadBool(reader, row, mapping, spec.DbColumn),
                ImportValueKind.HeadIdCard => ReadHeadIdCardOrFallback(reader, row, mapping, spec.DbColumn, key),
                ImportValueKind.GenderFromIdCard => ReadGenderOrDerive(reader, row, mapping, spec.DbColumn, key),
                ImportValueKind.BirthDateFromIdCard => ReadBirthDateOrDerive(reader, row, mapping, spec.DbColumn, key),
                _ => throw new BusinessException(ErrorCodes.VALIDATION_FAILED, $"未知的列规格类型: {spec.Kind}"),
            };
        }
        return values;
    }

    private static string ReadHeadIdCardOrFallback(IExcelSheetReader reader, int row, Dictionary<string, int> mapping, string dbColumn, string idCard)
    {
        var headIdCard = ReadString(reader, row, mapping, dbColumn);
        return string.IsNullOrWhiteSpace(headIdCard) ? idCard : headIdCard;
    }

    private static string ReadGenderOrDerive(IExcelSheetReader reader, int row, Dictionary<string, int> mapping, string dbColumn, string idCard)
    {
        var gender = ReadString(reader, row, mapping, dbColumn);
        if (string.IsNullOrEmpty(gender) && idCard.Length == 18)
        {
            gender = GetGenderFromIdCard(idCard);
        }
        return gender;
    }

    private static object? ReadBirthDateOrDerive(IExcelSheetReader reader, int row, Dictionary<string, int> mapping, string dbColumn, string idCard)
    {
        var birthDateStr = ReadString(reader, row, mapping, dbColumn);
        if (!string.IsNullOrEmpty(birthDateStr) && DateTime.TryParse(birthDateStr, out var bd))
        {
            return bd;
        }
        return idCard.Length == 18 ? GetBirthDateFromIdCard(idCard) : null;
    }

    /// <summary>
    /// 刷写一批：批内按键去重（保留最后一行）→ DELETE ... = ANY($1) → 单条多行 INSERT。
    /// 失败时按行号区间记入 RowErrors 并返回 false（整体事务随后回滚，全有或全无）。
    /// </summary>
    private async Task<bool> FlushBatchAsync(
        string tableName,
        string keyColumn,
        IReadOnlyList<ImportColumnSpec> columns,
        List<BufferedRow> buffer,
        HashSet<string> insertedKeys,
        CancellationToken ct)
    {
        if (buffer.Count == 0) return true;

        var firstRow = buffer[0].RowNumber;
        var lastRow = buffer[^1].RowNumber;

        // 批内按键去重，保留最后一行——同一条 INSERT 不能包含相同键两次
        var deduped = new List<BufferedRow>(buffer.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = buffer.Count - 1; i >= 0; i--)
        {
            if (seen.Add(buffer[i].Key)) deduped.Add(buffer[i]);
        }
        deduped.Reverse();
        buffer.Clear();

        // 先删后插：覆盖库中既有同键行与本次导入早前批次的同键行（跨批重复同样"保留最后一行"）
        var keys = new string[deduped.Count];
        for (var i = 0; i < deduped.Count; i++)
        {
            keys[i] = deduped[i].Key;
        }

        // 注意：string[] 必须包一层 object[]，否则会被 params object[] 展开成多个参数
        var deleteResult = await DatabaseService.ExecuteNonQueryAsync(
            $"DELETE FROM {tableName} WHERE {keyColumn} = ANY($1::text[])", ct, new object[] { keys });
        if (deleteResult.IsFailure)
        {
            RecordBatchError(firstRow, lastRow, deleteResult.Message ?? "批量 DELETE 失败");
            return false;
        }

        var colCount = columns.Count;
        var sb = new StringBuilder(colCount * 16 + deduped.Count * (colCount * 5 + 24));
        sb.Append("INSERT INTO ").Append(tableName).Append(" (");
        for (var c = 0; c < colCount; c++)
        {
            if (c > 0) sb.Append(", ");
            sb.Append(columns[c].DbColumn);
        }
        sb.Append(", imported_at) VALUES ");

        var args = new object[deduped.Count * colCount];
        var p = 0;
        for (var r = 0; r < deduped.Count; r++)
        {
            if (r > 0) sb.Append(", ");
            sb.Append('(');
            var values = deduped[r].Values;
            for (var c = 0; c < colCount; c++)
            {
                if (c > 0) sb.Append(", ");
                args[p] = values[c]!;
                p++;
                sb.Append('$').Append(p);
            }
            sb.Append(", CURRENT_TIMESTAMP)");
        }

        var insertResult = await DatabaseService.ExecuteNonQueryAsync(sb.ToString(), ct, args);
        if (insertResult.IsFailure)
        {
            RecordBatchError(firstRow, lastRow, insertResult.Message ?? "批量 INSERT 失败");
            return false;
        }

        foreach (var bufferedRow in deduped)
        {
            insertedKeys.Add(bufferedRow.Key);
        }
        return true;
    }

    /// <summary>记录一段行号区间的批量失败（写入 RowErrors 并记日志），语义同 RecordRowError。</summary>
    protected void RecordBatchError(int firstRowNumber, int lastRowNumber, string message)
    {
        RowErrors.Add($"第 {firstRowNumber}-{lastRowNumber} 行: {message}");
        LogError($"第 {firstRowNumber}-{lastRowNumber} 行批量导入失败: {message}");
    }

    protected virtual async Task<(int linkedCount, int unlinkedCount, List<UnlinkedPersonInfo> unlinkedPersons)> LinkPersonsToFamiliesAsync(CancellationToken ct)
    {
        ValidateTableName(PersonTableName);
        ValidateTableName(FamilyTableName);

        var linkedCount = 0;
        var unlinkedCount = 0;
        var unlinkedPersons = new List<UnlinkedPersonInfo>();

        // 增量导入时家庭行被删重建、id 变化，旧人员的 family_id 会悬挂指向已删行。
        // 先清掉悬挂指向，再全量重连（不带 family_id IS NULL 条件），保证每次导入后关联自洽。
        var clearDanglingSql = $@"
            UPDATE {PersonTableName} p
            SET family_id = NULL
            WHERE p.family_id IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM {FamilyTableName} f WHERE f.id = p.family_id)";
        var clearDangling = await DatabaseService.ExecuteNonQueryAsync(clearDanglingSql, ct);
        if (clearDangling.IsFailure)
        {
            LogWarn($"清理悬挂家庭关联失败: {clearDangling.Message}");
        }

        var linkSql = $@"
            UPDATE {PersonTableName} p
            SET family_id = f.id FROM {FamilyTableName} f
            WHERE p.head_id_card = f.applicant_id_card";

        var linkResult = await DatabaseService.ExecuteNonQueryAsync(linkSql, ct);
        if (linkResult.IsSuccess)
        {
            linkedCount = linkResult.Value; // 语义：当前已关联总数（含既有关联的重申）
        }

        var unlinkedSql = $@"
            SELECT name, id_card, head_id_card FROM {PersonTableName}
            WHERE family_id IS NULL
            ORDER BY imported_at";

        // 注意不能用 QueryAsync<dynamic>：dynamic 在泛型实参位置就是 object，
        // 映射层会返回空 object，随后任何成员访问必抛 RuntimeBinderException
        var unlinkedResult = await DatabaseService.QueryAsync<UnlinkedPersonRow>(unlinkedSql, ct);
        if (unlinkedResult.IsSuccess && unlinkedResult.Value != null)
        {
            foreach (var row in unlinkedResult.Value)
            {
                unlinkedCount++;
                unlinkedPersons.Add(new UnlinkedPersonInfo
                {
                    Name = row.Name ?? "",
                    IdCard = row.IdCard ?? "",
                    HeadIdCard = row.HeadIdCard ?? "",
                    RowNumber = unlinkedCount
                });
            }
        }

        return (linkedCount, unlinkedCount, unlinkedPersons);
    }

    private sealed class UnlinkedPersonRow
    {
        public string? Name { get; set; }
        public string? IdCard { get; set; }
        public string? HeadIdCard { get; set; }
    }

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

            var header = Regex.Replace(headerRaw, @"[
\u0000-\u001F]", "");
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

    protected static string ReadString(IExcelSheetReader reader, int row, Dictionary<string, int> mapping, string mappingKey, string defaultValue = "")
    {
        if (!mapping.TryGetValue(mappingKey, out var col)) return defaultValue;
        var text = reader.GetCellText(row, col);
        if (string.IsNullOrEmpty(text)) return defaultValue;
        return ImportDataReader.ReadString(new Dictionary<string, int> { [mappingKey] = 0 }, new[] { text }, mappingKey);
    }

    protected static int ReadInt(IExcelSheetReader reader, int row, Dictionary<string, int> mapping, string mappingKey, int defaultValue = 0)
    {
        if (!mapping.TryGetValue(mappingKey, out var col)) return defaultValue;
        var text = reader.GetCellText(row, col);
        if (string.IsNullOrEmpty(text)) return defaultValue;
        return ImportDataReader.ReadInt(new Dictionary<string, int> { [mappingKey] = 0 }, new[] { text }, mappingKey);
    }

    protected static decimal ReadDecimal(IExcelSheetReader reader, int row, Dictionary<string, int> mapping, string mappingKey, decimal defaultValue = 0)
    {
        if (!mapping.TryGetValue(mappingKey, out var col)) return defaultValue;
        var text = reader.GetCellText(row, col);
        if (string.IsNullOrEmpty(text)) return defaultValue;
        text = text.Replace("元", "").Replace(",", "").Trim();
        return ImportDataReader.ReadDecimal(new Dictionary<string, int> { [mappingKey] = 0 }, new[] { text }, mappingKey);
    }

    protected static DateTime? ReadDate(IExcelSheetReader reader, int row, Dictionary<string, int> mapping, string mappingKey)
    {
        if (!mapping.TryGetValue(mappingKey, out var col)) return null;
        var text = reader.GetCellText(row, col);
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length == 6 && int.TryParse(text, out var yyyymm))
        {
            var year = yyyymm / 100;
            var month = yyyymm % 100;
            if (month >= 1 && month <= 12) return new DateTime(year, month, 1);
        }
        return ImportDataReader.ReadDate(new Dictionary<string, int> { [mappingKey] = 0 }, new[] { text }, mappingKey);
    }

    protected static bool ReadBool(IExcelSheetReader reader, int row, Dictionary<string, int> mapping, string mappingKey)
    {
        if (!mapping.TryGetValue(mappingKey, out var col)) return false;
        var text = reader.GetCellText(row, col);
        if (string.IsNullOrEmpty(text)) return false;
        return ImportDataReader.ReadBool(new Dictionary<string, int> { [mappingKey] = 0 }, new[] { text }, mappingKey);
    }

    protected static DateTime? GetBirthDateFromIdCard(string idCard)
    {
        // 结构化解析（TryParseExact）：非数字/非法日期返回 null，不走异常路径
        if (idCard.Length != 18) return null;
        return DateTime.TryParseExact(
            idCard.Substring(6, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
    }

    protected static string GetGenderFromIdCard(string idCard)
    {
        if (idCard.Length != 18) return "";
        var ch = idCard[16];
        if (!char.IsDigit(ch)) return "";
        return (ch - '0') % 2 == 1 ? "男" : "女";
    }
}