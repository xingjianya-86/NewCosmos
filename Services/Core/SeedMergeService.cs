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

        await _db.BeginTransactionAsync();
        try
        {
            var existingKeys = await LoadExistingKeysAsync(definition, ct);
            var seedKeys = new HashSet<string>(
                seedRows.Select(r => GetBusinessKey(r, definition.BusinessKeyColumn)),
                StringComparer.OrdinalIgnoreCase);

            var colList = string.Join(", ", definition.Columns);
            var paramNames = string.Join(", ", definition.Columns.Select((_, i) => $"${i + 1}"));
            var insertSql = $"INSERT INTO {definition.TableName} ({colList}) VALUES ({paramNames})";
            var updateSets = string.Join(", ", definition.Columns.Select((c, i) => $"{c} = ${i + 1}"));

            foreach (var row in seedRows)
            {
                var key = GetBusinessKey(row, definition.BusinessKeyColumn);
                if (existingKeys.Contains(key))
                {
                    var allParams = GetColumnValues(definition, row).Append(key).Cast<object>().ToArray();
                    var sql = $"UPDATE {definition.TableName} SET {updateSets} WHERE \"{definition.BusinessKeyColumn}\" = ${definition.Columns.Count + 1}";
                    var updateResult = await _db.ExecuteNonQueryAsync(sql, ct, allParams);
                    if (updateResult.IsSuccess) result.Updated++;
                }
                else
                {
                    var allParams = GetColumnValues(definition, row).ToArray();
                    var insertResult = await _db.ExecuteNonQueryAsync(insertSql, ct, allParams);
                    if (insertResult.IsSuccess) result.Inserted++;
                }
            }

            foreach (var key in existingKeys)
            {
                if (!seedKeys.Contains(key))
                {
                    var deleteSql = $"DELETE FROM {definition.TableName} WHERE \"{definition.BusinessKeyColumn}\" = $1";
                    var deleteResult = await _db.ExecuteNonQueryAsync(deleteSql, ct, (object)key);
                    if (deleteResult.IsSuccess) result.Deleted++;
                }
            }

            await _db.CommitTransactionAsync();
            LogInfo($"种子数据合并完成: {definition.TableName}");
            result.IsSuccess = true;
            return Result.Success(result);
        }
        catch (Exception ex)
        {
            await _db.RollbackTransactionAsync();
            LogError($"操作失败");
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            return Result.FromException<MergeResult>(ex);
        }
    }

    private async Task<HashSet<string>> LoadExistingKeysAsync(SeedMergeDefinition def, CancellationToken ct)
    {
        var sql = $"SELECT \"{def.BusinessKeyColumn}\" AS key_value FROM {def.TableName}";
        var rawResult = await _db.QueryAsync<SeedKeyRow>(sql, ct);
        var rows = rawResult.Value ?? new List<SeedKeyRow>();
        return new HashSet<string>(
            rows.Select(r => r.KeyValue ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);
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
