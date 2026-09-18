using System.Linq;
using System.Text.Json;
using NewCosmos.Helpers;
using NewCosmos.Models.Lottery;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Lottery;

/// <summary>
/// 用户购彩记录服务实现
/// </summary>
public class UserPurchaseService : IUserPurchaseService
{
    private readonly IDatabaseService _db;
    private readonly ILoggerService _logger;

    public UserPurchaseService(
        IDatabaseService db,
        ILoggerService logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 批量保存预测结果为购彩记录（单条多行 VALUES 批量插入）
    /// </summary>
    public async Task<Result<int>> SaveFromPredictionsAsync(LotteryType lotteryType, List<PredictionResult> predictions, string algorithm, CancellationToken ct = default)
    {
        if (predictions == null || predictions.Count == 0)
            return Result<int>.Success(0);

        try
        {
            var valueRows = new List<string>(predictions.Count);
            var parameters = new List<object>(predictions.Count * 4);
            var paramIndex = 1;

            foreach (var pred in predictions)
            {
                var redJson = JsonSerializer.Serialize(pred.GetPredictedReds());
                var blueJson = JsonSerializer.Serialize(pred.GetPredictedBlues());

                valueRows.Add($"(${paramIndex++}::varchar, ${paramIndex++}::text, ${paramIndex++}::text, ${paramIndex++}::varchar, CURRENT_DATE)");
                parameters.Add(lotteryType.ToString());
                parameters.Add(redJson);
                parameters.Add(blueJson);
                parameters.Add(algorithm ?? string.Empty);
            }

            var sql = $@"INSERT INTO nc_lottery_user_purchases
                (lottery_type, red_numbers, blue_numbers, algorithm, purchase_date)
                VALUES {string.Join(", ", valueRows)}";

            var result = await _db.ExecuteNonQueryAsync(sql, ct, parameters.ToArray());
            if (!result.IsSuccess)
                return Result<int>.Failure(result.ErrorCode!, result.Message!);

            _logger.Info($"购彩记录入库: {result.Value}条, 彩种={lotteryType}, 算法={algorithm}");
            return Result<int>.Success(result.Value);
        }
        catch (Exception ex)
        {
            _logger.Error($"保存购彩记录失败: {ex.Message}");
            return Result<int>.FromException<int>(ex);
        }
    }

    /// <summary>
    /// 验证所有待验证的购彩记录（对照开奖结果，事务内批量处理）
    /// </summary>
    public async Task<Result<VerificationSummary>> VerifyAndRewardAsync(LotteryType lotteryType, CancellationToken ct = default)
    {
        try
        {
            // 1. 一次读取全部待验证记录
            var pendingResult = await _db.QueryAsync<PurchaseRow>(@"
                SELECT id, lottery_type, draw_number, red_numbers, blue_numbers, algorithm,
                       purchase_date, is_verified, is_hit, hit_red_count, hit_blue_count,
                       prize_level, prize_amount, created_at
                FROM nc_lottery_user_purchases
                WHERE lottery_type = $1 AND is_verified = FALSE
                ORDER BY purchase_date ASC", ct, lotteryType.ToString());

            if (!pendingResult.IsSuccess)
                return Result<VerificationSummary>.Failure(pendingResult.ErrorCode!, pendingResult.Message!);

            var pendingRows = pendingResult.Value;
            var summary = new VerificationSummary();

            if (pendingRows.Count == 0)
            {
                summary.EncouragementMessage = "暂无待验证记录，生成预测后开奖即可自动验证";
                return Result<VerificationSummary>.Success(summary);
            }

            // 2. 一次性加载候选开奖记录（购彩日期当天及之后），在内存中按期匹配，避免逐注查库
            var candidateResult = await _db.QueryAsync<CandidateDraw>(@"
                SELECT draw_number, draw_date, red_numbers_json, blue_numbers_json, prize_grades_json
                FROM nc_lottery_draws
                WHERE lottery_type = $1 AND draw_date >= $2
                ORDER BY draw_date ASC", ct, lotteryType.ToString(), pendingRows.Min(r => r.PurchaseDate));

            if (!candidateResult.IsSuccess)
                return Result<VerificationSummary>.Failure(candidateResult.ErrorCode!, candidateResult.Message!);

            var candidates = candidateResult.Value;

            // 3. 计算命中并准备批量更新参数
            var updates = new List<VerificationUpdate>();

            foreach (var row in pendingRows)
            {
                var draw = ResolveDraw(row, candidates);
                if (draw == null)
                {
                    // 尚未开奖：保持未验证，计入待开奖
                    summary.PendingCount++;
                    continue;
                }

                var predictedReds = DeserializeNumbers(row.RedNumbers);
                var predictedBlues = DeserializeNumbers(row.BlueNumbers);
                var actualReds = DeserializeNumbers(draw.RedNumbersJson);
                var actualBlues = DeserializeNumbers(draw.BlueNumbersJson);

                var redHits = predictedReds.Intersect(actualReds).Count();
                var blueHits = predictedBlues.Intersect(actualBlues).Count();
                var prizeLevel = DeterminePrizeLevel(lotteryType, redHits, blueHits, draw.DrawNumber, draw.DrawDate);
                var isHit = prizeLevel > 0;
                var prizeAmount = isHit
                    ? LotteryPrizeResolver.ResolvePrizeAmount(draw.PrizeGradesJson, prizeLevel)
                    : 0m;

                updates.Add(new VerificationUpdate
                {
                    Id = row.Id,
                    DrawNumber = draw.DrawNumber,
                    IsHit = isHit,
                    HitRedCount = redHits,
                    HitBlueCount = blueHits,
                    PrizeLevel = prizeLevel,
                    PrizeAmount = prizeAmount
                });

                summary.TotalVerified++;

                if (isHit)
                {
                    summary.HitCount++;
                    summary.TotalPrizeAmount += prizeAmount;
                    summary.HitDetails.Add(new HitDetail
                    {
                        DrawNumber = draw.DrawNumber,
                        RedHits = redHits,
                        BlueHits = blueHits,
                        PrizeLevel = prizeLevel,
                        PrizeAmount = prizeAmount,
                        Numbers = $"{FormatNumbers(predictedReds)} + {FormatNumbers(predictedBlues)}"
                    });

                    if (summary.BestPrizeLevel == 0 || prizeLevel < summary.BestPrizeLevel)
                    {
                        summary.BestPrizeLevel = prizeLevel;
                        summary.BestPrizeDescription = GetPrizeDescription(lotteryType, prizeLevel);
                    }
                }
            }

            // 4. 事务内批量更新
            if (updates.Count > 0)
            {
                await using var tx = await _db.BeginTransactionScopeAsync(ct);
                var updateResult = await ExecuteBatchUpdateAsync(updates, ct);
                if (!updateResult.IsSuccess)
                {
                    await tx.RollbackAsync(ct);
                    return Result<VerificationSummary>.Failure(updateResult.ErrorCode!, updateResult.Message!);
                }
                await tx.CommitAsync(ct);
            }

            summary.EncouragementMessage = GenerateEncouragement(summary);

            _logger.Info($"购彩验证完成: 验证{summary.TotalVerified}条, 命中{summary.HitCount}条, 待开奖{summary.PendingCount}条");
            return Result<VerificationSummary>.Success(summary);
        }
        catch (Exception ex)
        {
            _logger.Error($"验证购彩记录失败: {ex.Message}");
            return Result<VerificationSummary>.FromException<VerificationSummary>(ex);
        }
    }

    /// <summary>
    /// 获取购彩记录列表（分页，可按状态筛选）
    /// </summary>
    public async Task<Result<List<UserPurchaseRecord>>> GetPurchasesAsync(LotteryType lotteryType, LotteryPurchaseFilter filter = LotteryPurchaseFilter.All, int pageNo = 1, int pageSize = 30, CancellationToken ct = default)
    {
        try
        {
            var offset = (pageNo - 1) * pageSize;
            var filterClause = BuildFilterClause(filter);

            var sql = $@"SELECT p.id, p.lottery_type, p.draw_number, p.red_numbers, p.blue_numbers, p.algorithm,
                               p.purchase_date, p.is_verified, p.is_hit, p.hit_red_count, p.hit_blue_count,
                               p.prize_level, p.prize_amount, p.created_at,
                               d.red_numbers_json AS actual_red_numbers, d.blue_numbers_json AS actual_blue_numbers
                        FROM nc_lottery_user_purchases p
                        LEFT JOIN nc_lottery_draws d
                               ON d.lottery_type = p.lottery_type AND d.draw_number = p.draw_number
                        WHERE p.lottery_type = $1{filterClause}
                        ORDER BY p.created_at DESC
                        LIMIT $2 OFFSET $3";

            var result = await _db.QueryAsync<PurchaseRow>(sql, ct, lotteryType.ToString(), pageSize, offset);
            if (!result.IsSuccess)
                return Result<List<UserPurchaseRecord>>.Failure(result.ErrorCode!, result.Message!);

            var records = result.Value.Select(ToRecord).ToList();
            return Result<List<UserPurchaseRecord>>.Success(records);
        }
        catch (Exception ex)
        {
            _logger.Error($"获取购彩记录失败: {ex.Message}");
            return Result<List<UserPurchaseRecord>>.FromException<List<UserPurchaseRecord>>(ex);
        }
    }

    /// <summary>
    /// 获取购彩记录汇总统计
    /// </summary>
    public async Task<Result<LotteryPurchaseSummary>> GetPurchaseSummaryAsync(LotteryType lotteryType, CancellationToken ct = default)
    {
        try
        {
            var sql = @"
                SELECT COUNT(*) AS total_count,
                       COUNT(*) FILTER (WHERE is_verified) AS verified_count,
                       COUNT(*) FILTER (WHERE is_hit) AS hit_count,
                       COALESCE(SUM(CASE WHEN is_hit THEN prize_amount ELSE 0 END), 0) AS total_prize,
                       MIN(CASE WHEN is_hit THEN prize_level END) AS best_prize_level
                FROM nc_lottery_user_purchases
                WHERE lottery_type = $1";

            var result = await _db.QuerySingleAsync<PurchaseSummaryRow>(sql, ct, lotteryType.ToString());
            if (!result.IsSuccess)
                return Result<LotteryPurchaseSummary>.Failure(result.ErrorCode!, result.Message!);

            var row = result.Value;
            if (row == null)
                return Result<LotteryPurchaseSummary>.Success(new LotteryPurchaseSummary());

            var summary = new LotteryPurchaseSummary
            {
                TotalCount = row.TotalCount,
                VerifiedCount = row.VerifiedCount,
                PendingCount = row.TotalCount - row.VerifiedCount,
                HitCount = row.HitCount,
                TotalPrizeAmount = row.TotalPrize,
                BestPrizeLevel = row.BestPrizeLevel ?? 0
            };
            summary.BestPrizeDescription = summary.BestPrizeLevel > 0
                ? GetPrizeDescription(lotteryType, summary.BestPrizeLevel)
                : "";

            return Result<LotteryPurchaseSummary>.Success(summary);
        }
        catch (Exception ex)
        {
            _logger.Error($"获取购彩汇总失败: {ex.Message}");
            return Result<LotteryPurchaseSummary>.FromException<LotteryPurchaseSummary>(ex);
        }
    }

    /// <summary>
    /// 获取待验证记录数
    /// </summary>
    public async Task<Result<int>> GetUnverifiedCountAsync(LotteryType lotteryType, CancellationToken ct = default)
    {
        try
        {
            var sql = "SELECT COUNT(*) FROM nc_lottery_user_purchases WHERE lottery_type = $1 AND is_verified = FALSE";
            var result = await _db.ExecuteScalarAsync<int?>(sql, ct, lotteryType.ToString());
            return Result<int>.Success(result.Value ?? 0);
        }
        catch (Exception ex)
        {
            return Result<int>.FromException<int>(ex);
        }
    }

    /// <summary>
    /// 获取购彩记录总数（可按状态筛选）
    /// </summary>
    public async Task<Result<int>> GetPurchaseCountAsync(LotteryType lotteryType, LotteryPurchaseFilter filter = LotteryPurchaseFilter.All, CancellationToken ct = default)
    {
        try
        {
            var filterClause = BuildFilterClause(filter);
            var sql = $"SELECT COUNT(*) FROM nc_lottery_user_purchases WHERE lottery_type = $1{filterClause}";
            var result = await _db.ExecuteScalarAsync<int?>(sql, ct, lotteryType.ToString());
            return Result<int>.Success(result.Value ?? 0);
        }
        catch (Exception ex)
        {
            return Result<int>.FromException<int>(ex);
        }
    }

    #region 私有方法

    private static string BuildFilterClause(LotteryPurchaseFilter filter) => filter switch
    {
        LotteryPurchaseFilter.Pending => " AND is_verified = FALSE",
        LotteryPurchaseFilter.Hit => " AND is_verified = TRUE AND is_hit = TRUE",
        LotteryPurchaseFilter.Miss => " AND is_verified = TRUE AND is_hit = FALSE",
        _ => string.Empty
    };

    private static UserPurchaseRecord ToRecord(PurchaseRow row) => new()
    {
        Id = row.Id,
        LotteryType = Enum.TryParse<LotteryType>(row.LotteryType, true, out var lt) ? lt : LotteryType.SSQ,
        DrawNumber = row.DrawNumber ?? "",
        RedNumbersJson = row.RedNumbers ?? "[]",
        BlueNumbersJson = row.BlueNumbers ?? "[]",
        Algorithm = row.Algorithm ?? "",
        PurchaseDate = row.PurchaseDate,
        IsVerified = row.IsVerified,
        IsHit = row.IsHit,
        HitRedCount = row.HitRedCount,
        HitBlueCount = row.HitBlueCount,
        PrizeLevel = row.PrizeLevel,
        PrizeAmount = row.PrizeAmount,
        CreatedAt = row.CreatedAt,
        ActualRedNumbersJson = row.ActualRedNumbers ?? "[]",
        ActualBlueNumbersJson = row.ActualBlueNumbers ?? "[]"
    };

    /// <summary>
    /// 为单条购彩记录匹配开奖：优先使用已有期号，否则取购彩日期当天及之后最早的一期。
    /// </summary>
    private static CandidateDraw? ResolveDraw(PurchaseRow row, List<CandidateDraw> candidates)
    {
        if (!string.IsNullOrWhiteSpace(row.DrawNumber))
        {
            return candidates.FirstOrDefault(c =>
                string.Equals(c.DrawNumber, row.DrawNumber, StringComparison.OrdinalIgnoreCase));
        }

        // candidates 已按 draw_date 升序，取第一个不早于购彩日期的期号
        return candidates.FirstOrDefault(c => c.DrawDate >= row.PurchaseDate);
    }

    private async Task<Result<int>> ExecuteBatchUpdateAsync(List<VerificationUpdate> updates, CancellationToken ct)
    {
        var valueRows = new List<string>(updates.Count);
        var parameters = new List<object>(updates.Count * 7);
        var paramIndex = 1;

        foreach (var u in updates)
        {
            valueRows.Add($"(${paramIndex++}::int, ${paramIndex++}::varchar, ${paramIndex++}::bool, " +
                          $"${paramIndex++}::int, ${paramIndex++}::int, ${paramIndex++}::int, ${paramIndex++}::numeric)");
            parameters.Add(u.Id);
            parameters.Add(u.DrawNumber);
            parameters.Add(u.IsHit);
            parameters.Add(u.HitRedCount);
            parameters.Add(u.HitBlueCount);
            parameters.Add(u.PrizeLevel);
            parameters.Add(u.PrizeAmount);
        }

        var sql = $@"
            UPDATE nc_lottery_user_purchases AS t
            SET draw_number = v.draw_number,
                is_verified = TRUE,
                is_hit = v.is_hit,
                hit_red_count = v.hit_red_count,
                hit_blue_count = v.hit_blue_count,
                prize_level = v.prize_level,
                prize_amount = v.prize_amount
            FROM (VALUES {string.Join(", ", valueRows)})
                AS v(id, draw_number, is_hit, hit_red_count, hit_blue_count, prize_level, prize_amount)
            WHERE t.id = v.id";

        return await _db.ExecuteNonQueryAsync(sql, ct, parameters.ToArray());
    }

    private static int[] DeserializeNumbers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<int>();

        try
        {
            return JsonSerializer.Deserialize<int[]>(json) ?? Array.Empty<int>();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    private static string FormatNumbers(int[] numbers) =>
        string.Join(",", numbers.Select(n => n.ToString("D2")));

    /// <summary>
    /// 判定奖级。双色球 6 个奖级；大乐透按开奖期号区分规则：
    /// 第 26014 期（2026-01-31 21:10 起售，首期开奖 2026-02-02）起由 9 个奖级优化为 7 个奖级。
    /// </summary>
    private static int DeterminePrizeLevel(LotteryType lotteryType, int redHitCount, int blueHitCount, string? drawNumber, DateTime drawDate)
    {
        if (lotteryType == LotteryType.SSQ)
        {
            if (redHitCount == 6 && blueHitCount == 1) return 1;
            if (redHitCount == 6) return 2;
            if (redHitCount == 5 && blueHitCount == 1) return 3;
            if (redHitCount == 5 || (redHitCount == 4 && blueHitCount == 1)) return 4;
            if (redHitCount == 4 || (redHitCount == 3 && blueHitCount == 1)) return 5;
            if (blueHitCount == 1) return 6;
            return 0;
        }

        return IsDltNewRules(drawNumber, drawDate)
            ? DetermineDltPrizeLevel7(redHitCount, blueHitCount)
            : DetermineDltPrizeLevel9(redHitCount, blueHitCount);
    }

    /// <summary>大乐透是否适用 2026-01-31（第 26014 期）起的新规则（7 个奖级）</summary>
    private static bool IsDltNewRules(string? drawNumber, DateTime drawDate)
    {
        var seq = ParseDltDrawSequence(drawNumber);
        if (seq.HasValue)
            return seq.Value >= 26014;              // 第 26014 期起
        return drawDate.Date >= new DateTime(2026, 2, 2); // 兜底：新规则首期开奖日
    }

    /// <summary>解析大乐透期号序号，兼容 "26014" 与 "2026014" 两种格式；失败返回 null</summary>
    private static int? ParseDltDrawSequence(string? drawNumber)
    {
        if (string.IsNullOrWhiteSpace(drawNumber)) return null;
        var digits = new string(drawNumber.Where(char.IsDigit).ToArray());
        if (digits.Length == 0 || !int.TryParse(digits, out var n)) return null;
        return n >= 100000 ? n % 100000 : n;
    }

    /// <summary>大乐透新规则（2026-01-31 起）：7 个奖级</summary>
    private static int DetermineDltPrizeLevel7(int redHitCount, int blueHitCount)
    {
        if (redHitCount == 5 && blueHitCount == 2) return 1;                       // 5+2
        if (redHitCount == 5 && blueHitCount == 1) return 2;                       // 5+1
        if (redHitCount == 5 || (redHitCount == 4 && blueHitCount == 2)) return 3; // 5+0 / 4+2
        if (redHitCount == 4 && blueHitCount == 1) return 4;                       // 4+1
        if (redHitCount == 4 || (redHitCount == 3 && blueHitCount == 2)) return 5; // 4+0 / 3+2
        if ((redHitCount == 3 && blueHitCount == 1) || (redHitCount == 2 && blueHitCount == 2)) return 6; // 3+1 / 2+2
        if (redHitCount == 3 || (redHitCount == 2 && blueHitCount == 1)
            || (redHitCount == 1 && blueHitCount == 2) || blueHitCount == 2) return 7; // 3+0 / 2+1 / 1+2 / 0+2
        return 0;
    }

    /// <summary>大乐透旧规则（第 26013 期及以前）：9 个奖级</summary>
    private static int DetermineDltPrizeLevel9(int redHitCount, int blueHitCount)
    {
        if (redHitCount == 5 && blueHitCount == 2) return 1;                       // 5+2
        if (redHitCount == 5 && blueHitCount == 1) return 2;                       // 5+1
        if (redHitCount == 5) return 3;                                            // 5+0
        if (redHitCount == 4 && blueHitCount == 2) return 4;                       // 4+2
        if (redHitCount == 4 && blueHitCount == 1) return 5;                       // 4+1
        if (redHitCount == 3 && blueHitCount == 2) return 6;                       // 3+2
        if (redHitCount == 4) return 7;                                            // 4+0
        if ((redHitCount == 3 && blueHitCount == 1) || (redHitCount == 2 && blueHitCount == 2)) return 8; // 3+1 / 2+2
        if (redHitCount == 3 || (redHitCount == 2 && blueHitCount == 1)
            || (redHitCount == 1 && blueHitCount == 2) || blueHitCount == 2) return 9; // 3+0 / 2+1 / 1+2 / 0+2
        return 0;
    }

    private static string GetPrizeDescription(LotteryType lotteryType, int level) => level switch
    {
        1 => "🏆 一等奖！",
        2 => "🥈 二等奖！",
        3 => "🥉 三等奖！",
        4 => "四等奖",
        5 => "五等奖",
        6 => "六等奖",
        7 => "七等奖",
        8 => "八等奖",
        9 => "九等奖",
        _ => $"第{level}等奖"
    };

    private static string GenerateEncouragement(VerificationSummary summary)
    {
        if (summary.TotalVerified == 0 && summary.PendingCount == 0)
            return "暂无待验证记录，生成预测后开奖即可自动验证";

        if (summary.TotalVerified == 0)
            return $"暂无可验证记录，有 {summary.PendingCount} 注待开奖，开奖后同步数据即可验证";

        var prizeText = summary.TotalPrizeAmount > 0 ? $"，合计奖金 ¥{summary.TotalPrizeAmount:N2}" : "";

        if (summary.HitCount == 0)
            return $"本次验证 {summary.TotalVerified} 注，暂未命中。\n继续加油！每一注都是对未来的投资！🎯";

        if (summary.BestPrizeLevel == 1)
            return $"🎉🎉🎉 恭喜！在 {summary.TotalVerified} 注中命中 {summary.HitCount} 注{prizeText}！\n含一等奖 {summary.BestPrizeDescription}！！！";

        if (summary.BestPrizeLevel <= 3)
            return $"🎉 恭喜！在 {summary.TotalVerified} 注中命中 {summary.HitCount} 注{prizeText}！\n最佳成绩：{summary.BestPrizeDescription}！继续保持！";

        return $"在 {summary.TotalVerified} 注中命中 {summary.HitCount} 注{prizeText}！\n最佳成绩：{summary.BestPrizeDescription}，距离大奖越来越近了！💪";
    }

    #endregion

    #region 数据行 DTO

    private class PurchaseRow
    {
        public int Id { get; set; }
        public string LotteryType { get; set; } = "";
        public string? DrawNumber { get; set; }
        public string RedNumbers { get; set; } = "[]";
        public string BlueNumbers { get; set; } = "[]";
        public string Algorithm { get; set; } = "";
        public DateTime PurchaseDate { get; set; }
        public bool IsVerified { get; set; }
        public bool IsHit { get; set; }
        public int HitRedCount { get; set; }
        public int HitBlueCount { get; set; }
        public int PrizeLevel { get; set; }
        public decimal PrizeAmount { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? ActualRedNumbers { get; set; }
        public string? ActualBlueNumbers { get; set; }
    }

    private class CandidateDraw
    {
        public string DrawNumber { get; set; } = "";
        public DateTime DrawDate { get; set; }
        public string RedNumbersJson { get; set; } = "[]";
        public string BlueNumbersJson { get; set; } = "[]";
        public string PrizeGradesJson { get; set; } = "[]";
    }

    private class PurchaseSummaryRow
    {
        public int TotalCount { get; set; }
        public int VerifiedCount { get; set; }
        public int HitCount { get; set; }
        public decimal TotalPrize { get; set; }
        public int? BestPrizeLevel { get; set; }
    }

    private class VerificationUpdate
    {
        public int Id { get; set; }
        public string DrawNumber { get; set; } = "";
        public bool IsHit { get; set; }
        public int HitRedCount { get; set; }
        public int HitBlueCount { get; set; }
        public int PrizeLevel { get; set; }
        public decimal PrizeAmount { get; set; }
    }

    #endregion
}
