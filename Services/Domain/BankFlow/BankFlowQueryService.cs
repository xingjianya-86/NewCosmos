using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using OfficeOpenXml;

namespace NewCosmos.Services.Domain.BankFlow;

/// <summary>
/// 银行流水核对查询服务：查看页分页筛选导出 + 经济状况界面按身份证带出最新批次数据。
/// SQL 全部位置参数 + 明确列名 + LIMIT；身份证批量匹配用 = ANY($1)。
/// </summary>
public class BankFlowQueryService : BaseService, IBankFlowQueryService
{
    protected override string ServiceName => "BankFlowQueryService";

    private readonly IDatabaseService _db;

    public BankFlowQueryService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    private const string RecordColumns = @"
        r.id, r.import_id, r.row_no, r.person_name, r.id_card, r.id_card_valid,
        r.relationship, r.family_address, r.total_balance, r.max_flow_amount,
        r.property_1, r.property_2, r.property_3, r.machinery_power, r.is_exceed, r.created_at";

    public async Task<Result<List<BankFlowImport>>> GetImportsAsync(CancellationToken ct = default)
    {
        try
        {
            const string sql = @"
                SELECT id, batch_no, source_file_name, total_count, exceed_count,
                       invalid_id_card_count, exceed_threshold, created_by, created_at
                FROM nc_biz_bank_flow_imports
                WHERE deleted_at IS NULL
                ORDER BY created_at DESC
                LIMIT 200";
            var result = await _db.QueryAsync<BankFlowImport>(sql, ct);
            if (result.IsFailure)
                return Result.Failure<List<BankFlowImport>>(result.ErrorCode, result.Message);
            return Result.Success(result.Value ?? new List<BankFlowImport>());
        }
        catch (Exception ex)
        {
            LogException(ex, "查询核对批次列表失败");
            return Result.Failure<List<BankFlowImport>>(ErrorCodes.UNKNOWN_ERROR, $"查询批次列表失败: {ex.Message}");
        }
    }

    public async Task<Result<PagedResult<BankFlowRecord>>> SearchRecordsAsync(
        string? keyword, long? importId, bool exceedOnly, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        try
        {
            if (pageIndex < 1) pageIndex = 1;
            if (pageSize < 1) pageSize = 20;

            var kw = (keyword ?? "").Trim();
            var offset = (pageIndex - 1) * pageSize;

            const string countSql = @"
                SELECT COUNT(*)
                FROM nc_biz_bank_flow_records r
                JOIN nc_biz_bank_flow_imports i ON i.id = r.import_id AND i.deleted_at IS NULL
                WHERE r.deleted_at IS NULL
                  AND ($1 = '' OR r.person_name LIKE '%' || $1 || '%' OR r.id_card LIKE '%' || $1 || '%' OR r.family_address LIKE '%' || $1 || '%')
                  AND ($2::BIGINT IS NULL OR r.import_id = $2)
                  AND ($3::BOOLEAN IS FALSE OR r.is_exceed)";

            var countResult = await _db.ExecuteScalarAsync<long>(countSql, ct,
                kw, (object?)importId ?? DBNull.Value, exceedOnly);
            if (countResult.IsFailure)
                return Result.Failure<PagedResult<BankFlowRecord>>(countResult.ErrorCode, countResult.Message);

            const string pageSql = @"
                SELECT " + RecordColumns + @",
                       (SELECT string_agg(i.bank_name || ' ' || i.balance || '/' || i.max_flow_amount, '；' ORDER BY i.id)
                        FROM nc_biz_bank_flow_items i WHERE i.record_id = r.id) AS bank_summary
                FROM nc_biz_bank_flow_records r
                JOIN nc_biz_bank_flow_imports i ON i.id = r.import_id AND i.deleted_at IS NULL
                WHERE r.deleted_at IS NULL
                  AND ($1 = '' OR r.person_name LIKE '%' || $1 || '%' OR r.id_card LIKE '%' || $1 || '%' OR r.family_address LIKE '%' || $1 || '%')
                  AND ($2::BIGINT IS NULL OR r.import_id = $2)
                  AND ($3::BOOLEAN IS FALSE OR r.is_exceed)
                ORDER BY r.row_no
                LIMIT $4 OFFSET $5";

            var pageResult = await _db.QueryAsync<BankFlowRecord>(pageSql, ct,
                kw, (object?)importId ?? DBNull.Value, exceedOnly, pageSize, offset);
            if (pageResult.IsFailure)
                return Result.Failure<PagedResult<BankFlowRecord>>(pageResult.ErrorCode, pageResult.Message);

            return Result.Success(new PagedResult<BankFlowRecord>
            {
                Items = pageResult.Value ?? new List<BankFlowRecord>(),
                TotalCount = (int)countResult.Value,
                PageIndex = pageIndex,
                PageSize = pageSize
            });
        }
        catch (Exception ex)
        {
            LogException(ex, "分页查询核对记录失败");
            return Result.Failure<PagedResult<BankFlowRecord>>(ErrorCodes.UNKNOWN_ERROR, $"查询失败: {ex.Message}");
        }
    }

    public async Task<Result<Dictionary<string, BankFlowPersonData>>> GetByIdCardsAsync(
        IReadOnlyCollection<string> idCards, CancellationToken ct = default)
    {
        try
        {
            var normalized = idCards
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim().ToUpperInvariant())
                .Distinct()
                .ToArray();

            if (normalized.Length == 0)
                return Result.Success(new Dictionary<string, BankFlowPersonData>());

            // 仅取最新批次（同文件重导后以最新为准）
            const string sql = @"
                SELECT " + RecordColumns + @",
                       i.batch_no,
                       (SELECT string_agg(i2.bank_name || ' ' || i2.balance || '/' || i2.max_flow_amount, '；' ORDER BY i2.id)
                        FROM nc_biz_bank_flow_items i2 WHERE i2.record_id = r.id) AS bank_summary
                FROM nc_biz_bank_flow_records r
                JOIN nc_biz_bank_flow_imports i ON i.id = r.import_id AND i.deleted_at IS NULL
                WHERE r.deleted_at IS NULL
                  AND UPPER(r.id_card) = ANY($1)
                  AND r.import_id = (SELECT MAX(id) FROM nc_biz_bank_flow_imports WHERE deleted_at IS NULL)";

            var result = await _db.QueryAsync<BankFlowRecordBatchRow>(sql, ct, normalized);
            if (result.IsFailure)
                return Result.Failure<Dictionary<string, BankFlowPersonData>>(result.ErrorCode, result.Message);

            var records = result.Value ?? new List<BankFlowRecordBatchRow>();

            var data = new Dictionary<string, BankFlowPersonData>();
            if (records.Count > 0)
            {
                var items = await GetItemsByRecordIdsAsync(records.Select(r => r.Id).ToList(), ct);
                if (items.IsFailure)
                    return Result.Failure<Dictionary<string, BankFlowPersonData>>(items.ErrorCode, items.Message);

                foreach (var rec in records)
                {
                    var key = (rec.IdCard ?? "").ToUpperInvariant();
                    data[key] = new BankFlowPersonData
                    {
                        Record = rec,
                        BatchNo = rec.BatchNo,
                        Items = items.Value.TryGetValue(rec.Id, out var list)
                            ? list
                            : new List<BankFlowItem>()
                    };
                }
            }

            return Result.Success(data);
        }
        catch (Exception ex)
        {
            LogException(ex, "按身份证查询核对数据失败");
            return Result.Failure<Dictionary<string, BankFlowPersonData>>(ErrorCodes.UNKNOWN_ERROR, $"查询核对数据失败: {ex.Message}");
        }
    }

    private async Task<Result<Dictionary<long, List<BankFlowItem>>>> GetItemsByRecordIdsAsync(
        List<long> recordIds, CancellationToken ct)
    {
        if (recordIds.Count == 0)
            return Result.Success(new Dictionary<long, List<BankFlowItem>>());

        const string sql = @"
            SELECT id, record_id, bank_name, balance, max_flow_amount, created_at
            FROM nc_biz_bank_flow_items
            WHERE record_id = ANY($1)
            ORDER BY id";
        var result = await _db.QueryAsync<BankFlowItem>(sql, ct, recordIds.ToArray());
        if (result.IsFailure)
            return Result.Failure<Dictionary<long, List<BankFlowItem>>>(result.ErrorCode, result.Message);

        var map = (result.Value ?? new List<BankFlowItem>())
            .GroupBy(i => i.RecordId)
            .ToDictionary(g => g.Key, g => g.ToList());
        return Result.Success(map);
    }

    public async Task<Result<byte[]>> ExportAsync(string? keyword, long? importId, bool exceedOnly, CancellationToken ct = default)
    {
        try
        {
            // 单次拉全量导出（上限 20000 行，避免撑爆内存）
            var page = await SearchRecordsAsync(keyword, importId, exceedOnly, 1, 20000, ct);
            if (page.IsFailure)
                return Result.Failure<byte[]>(page.ErrorCode, page.Message);

            var rows = page.Value.Items;

            ExcelPackage.License.SetNonCommercialOrganization("民政社会救助管理系统");
            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("银行流水核对");

            string[] headers =
            {
                "序号", "姓名", "身份证号", "身份证校验", "与户主关系", "家庭地址",
                "合计余额", "12月最高流水", "银行明细(余额/最高流水)",
                "房产1", "房产2", "房产3", "农机60功率", "超阈值标记"
            };
            for (var c = 0; c < headers.Length; c++)
                sheet.Cells[1, c + 1].Value = headers[c];

            for (var i = 0; i < rows.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var r = rows[i];
                var row = i + 2;
                sheet.Cells[row, 1].Value = r.RowNo;
                sheet.Cells[row, 2].Value = r.PersonName;
                sheet.Cells[row, 3].Value = r.IdCard;
                sheet.Cells[row, 4].Value = r.IdCardValidDisplay;
                sheet.Cells[row, 5].Value = r.Relationship;
                sheet.Cells[row, 6].Value = r.FamilyAddress;
                sheet.Cells[row, 7].Value = r.TotalBalance;
                sheet.Cells[row, 8].Value = r.MaxFlowAmount;
                sheet.Cells[row, 9].Value = r.BankSummary;
                sheet.Cells[row, 10].Value = r.Property1;
                sheet.Cells[row, 11].Value = r.Property2;
                sheet.Cells[row, 12].Value = r.Property3;
                sheet.Cells[row, 13].Value = r.MachineryPower?.ToString("0.##") ?? "";
                sheet.Cells[row, 14].Value = r.IsExceed ? "超30月标准" : "正常";
            }

            sheet.Cells[sheet.Dimension.Address].AutoFitColumns();

            return Result.Success(package.GetAsByteArray());
        }
        catch (OperationCanceledException)
        {
            return Result.Failure<byte[]>("CANCELLED", "导出已取消");
        }
        catch (Exception ex)
        {
            LogException(ex, "导出核对数据失败");
            return Result.Failure<byte[]>(ErrorCodes.UNKNOWN_ERROR, $"导出失败: {ex.Message}");
        }
    }

    /// <summary>查询行模型：在汇总列之上带批次号（仅供带出查询映射）</summary>
    private sealed class BankFlowRecordBatchRow : BankFlowRecord
    {
        public string BatchNo { get; set; } = string.Empty;
    }
}
