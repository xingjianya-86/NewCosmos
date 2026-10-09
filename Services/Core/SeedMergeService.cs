using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Models.Schema;
using NewCosmos.Services.Database;
using System.Text.RegularExpressions;

namespace NewCosmos.Services.Core;

public class SeedMergeService : BaseService, ISeedMergeService
{
    protected override string ServiceName => "SeedMergeService";
    private readonly IDatabaseService _db;

    public SeedMergeService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    private static readonly Regex ColumnNamePattern = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    private static void ValidateColumnName(string columnName)
    {
        if (!ColumnNamePattern.IsMatch(columnName))
            throw new InvalidOperationException($"无效的列名: {columnName}");
    }

    public async Task<Result<MergeResult>> MergeSeedDataAsync(
        SeedMergeDefinition definition,
        List<Dictionary<string, object>> seedRows,
        CancellationToken ct = default)
    {
        LogInfo($"执行操作");
        TableNameValidator.ValidateOrThrow(definition.TableName);

        foreach (var col in definition.Columns)
            ValidateColumnName(col);
        ValidateColumnName(definition.BusinessKeyColumn);

        var result = new MergeResult();

        await using var tx = await _db.BeginTransactionScopeAsync(ct);
        try
        {
            var existingKeysResult = await LoadExistingKeysAsync(definition, ct);
            if (existingKeysResult.IsFailure)
            {
                // §6：查询失败显式中止，绝不按"无现存行"继续（否则种子合并只插不删、UPDATE 全部静默失败）
                await tx.RollbackAsync(ct);
                result.IsSuccess = false;
                result.ErrorMessage = existingKeysResult.Message;
                return Result.Failure<MergeResult>(
                    existingKeysResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    existingKeysResult.Message ?? "读取现存种子键失败");
            }
            var existingKeys = existingKeysResult.Value!;
            var seedKeys = new HashSet<string>(
                seedRows.Select(r => GetBusinessKey(r, definition.BusinessKeyColumn)),
                StringComparer.OrdinalIgnoreCase);

            var colList = string.Join(", ", definition.Columns);
            var updateSets = string.Join(", ", definition.Columns.Select((c, i) => $"{c} = ${i + 1}"));

            // 新行多行 VALUES 批插（分批 100 行，防参数数量超限）
            var insertBatches = new List<List<Dictionary<string, object>>>();
            foreach (var row in seedRows)
            {
                var key = GetBusinessKey(row, definition.BusinessKeyColumn);
                if (existingKeys.Contains(key))
                {
                    // [批写豁免] UPDATE 保留逐行：字典种子为低频管理路径且体量小（几十行级），
                    // 合成 UPDATE...FROM VALUES 需拼接动态列名，复杂度与回归风险高于收益
                    var allParams = GetColumnValues(definition, row).Append(key).Cast<object>().ToArray();
                    var sql = $"UPDATE {definition.TableName} SET {updateSets} WHERE \"{definition.BusinessKeyColumn}\" = ${definition.Columns.Count + 1}";
                    var updateResult = await _db.ExecuteNonQueryAsync(sql, ct, allParams);
                    // 写失败显式中止回滚（原实现静默跳过，种子会与定义漂移）；事务已开启，回滚安全
                    if (updateResult.IsFailure)
                        return Result.Failure<MergeResult>(updateResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                            $"种子 UPDATE 失败: {updateResult.Message}");
                    result.Updated++;
                }
                else
                {
                    if (insertBatches.Count == 0 || insertBatches[^1].Count >= 100)
                        insertBatches.Add(new List<Dictionary<string, object>>());
                    insertBatches[^1].Add(row);
                }
            }

            foreach (var batch in insertBatches)
            {
                if (batch.Count == 0) continue;
                var (valuesClause, args) = MultiRowValuesBuilder.Build(
                    batch.Count,
                    definition.Columns.Count,
                    r => GetColumnValues(definition, batch[r]));
                var batchSql = $"INSERT INTO {definition.TableName} ({colList}) VALUES {valuesClause}";
                var insertResult = await _db.ExecuteNonQueryAsync(batchSql, ct, args);
                if (insertResult.IsFailure)
                    return Result.Failure<MergeResult>(insertResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        $"种子 INSERT 失败: {insertResult.Message}");
                result.Inserted += batch.Count;
            }

            // 残留键一次批删（原逐 key DELETE）
            var staleKeys = existingKeys.Where(k => !seedKeys.Contains(k)).ToList();
            if (staleKeys.Count > 0)
            {
                var deleteSql = $"DELETE FROM {definition.TableName} WHERE \"{definition.BusinessKeyColumn}\" = ANY($1)";
                var deleteResult = await _db.ExecuteNonQueryAsync(deleteSql, ct, staleKeys.ToArray());
                if (deleteResult.IsFailure)
                    return Result.Failure<MergeResult>(deleteResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        $"种子 DELETE 失败: {deleteResult.Message}");
                result.Deleted = staleKeys.Count;
            }

            await tx.CommitAsync(ct);
            LogInfo($"种子数据合并完成: {definition.TableName}");
            result.IsSuccess = true;
            return Result.Success(result);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogError($"操作失败");
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            return Result.FromException<MergeResult>(ex);
        }
    }

    private async Task<Result<HashSet<string>>> LoadExistingKeysAsync(SeedMergeDefinition def, CancellationToken ct)
    {
        var sql = $"SELECT \"{def.BusinessKeyColumn}\" AS key_value FROM {def.TableName}";
        var rawResult = await _db.QueryAsync<SeedKeyRow>(sql, ct);
        if (rawResult.IsFailure)
            return Result.Failure<HashSet<string>>(
                rawResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                rawResult.Message ?? "查询现存种子键失败");
        var rows = rawResult.Value ?? new List<SeedKeyRow>();
        return Result.Success(new HashSet<string>(
            rows.Select(r => r.KeyValue ?? string.Empty),
            StringComparer.OrdinalIgnoreCase));
    }

    private static string GetBusinessKey(Dictionary<string, object> row, string businessKeyColumn)
    {
        if (row.TryGetValue(businessKeyColumn, out var keyValue) && keyValue != null)
            return keyValue?.ToString() ?? string.Empty;
        return string.Empty;
    }

    private static List<object> GetColumnValues(SeedMergeDefinition def, Dictionary<string, object> row)
    {
        return def.Columns.Select(c => row.TryGetValue(c, out var v) ? v : null).Cast<object>().ToList();
    }

    private class SeedKeyRow
    {
        public string? KeyValue { get; set; }
    }
}
