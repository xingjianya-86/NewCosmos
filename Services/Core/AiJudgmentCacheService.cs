using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Core;

/// <summary>
/// AI 判定缓存服务（nc_sys_ai_judgments，2026-11-12 P1）。
/// 读：input_key + policy_version 双条件命中（版本=随包清单 sha256 前16位，清单发版即失效）；
/// 写：ON CONFLICT(input_key) upsert，失效旧版本行被覆盖，无需清理任务。
/// 结果回程统一经 SevereDiseaseJudge.NormalizeServerJudgment 校验（防脏行绕过防线）。
/// Singleton 无每操作可变状态（§6）。
/// </summary>
public sealed class AiJudgmentCacheService : IAiJudgmentCacheService
{
    private readonly IDatabaseService _db;
    private readonly ILoggerService _logger;

    public AiJudgmentCacheService(IDatabaseService dbService, ILoggerService logger)
    {
        _db = dbService;
        _logger = logger;
    }

    public async Task<Result<SevereDiseaseJudgment?>> GetAsync(string inputKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(inputKey))
            return Result<SevereDiseaseJudgment?>.Success(null);

        var policyVersion = SevereDiseaseJudge.Default.PolicyVersion;
        const string sql = """
            SELECT verdict, matched_item, confidence, reason, source, policy_version, model
            FROM nc_sys_ai_judgments
            WHERE input_key = $1 AND policy_version = $2
            LIMIT 1
            """;
        var rows = await _db.QueryAsync<AiJudgmentCacheRow>(sql, ct, inputKey, policyVersion);
        if (rows.IsFailure)
        {
            _logger.Warn($"AI 判定缓存读取失败: {rows.ErrorCode} {rows.Message}");
            return Result<SevereDiseaseJudgment?>.Failure(rows.ErrorCode, rows.Message);
        }
        var row = rows.Value?.FirstOrDefault();
        if (row == null)
            return Result<SevereDiseaseJudgment?>.Success(null); // 未命中/版本已变：正常未命中，非错误

        var judgment = SevereDiseaseJudge.Default.NormalizeServerJudgment(
            row.Verdict, row.MatchedItem, row.Confidence, row.Reason,
            row.Source, row.PolicyVersion, row.Model);
        return Result<SevereDiseaseJudgment?>.Success(judgment);
    }

    public async Task<Result> SetAsync(string inputKey, SevereDiseaseJudgment judgment, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(inputKey) || judgment == null)
            return Result.Success();
        if (judgment.Source != SevereDiseaseJudgment.SourceLlm)
            return Result.Success(); // 确定性结果毫秒可得，不入缓存

        const string sql = """
            INSERT INTO nc_sys_ai_judgments
                (input_key, verdict, matched_item, confidence, reason, source, policy_version, model, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, NOW(), NOW())
            ON CONFLICT (input_key) DO UPDATE SET
                verdict = EXCLUDED.verdict,
                matched_item = EXCLUDED.matched_item,
                confidence = EXCLUDED.confidence,
                reason = EXCLUDED.reason,
                source = EXCLUDED.source,
                policy_version = EXCLUDED.policy_version,
                model = EXCLUDED.model,
                updated_at = NOW()
            """;
        var affected = await _db.ExecuteNonQueryAsync(sql, ct,
            inputKey,
            judgment.Verdict,
            judgment.MatchedItem,
            judgment.Confidence,
            judgment.Reason,
            judgment.Source,
            SevereDiseaseJudge.Default.PolicyVersion, // 缓存有效性锚点=客户端随包版本（读侧同源比对）
            judgment.Model);
        if (affected.IsFailure)
        {
            _logger.Warn($"AI 判定缓存写入失败: {affected.ErrorCode} {affected.Message}");
            return Result.Failure(affected.ErrorCode, affected.Message);
        }
        return Result.Success();
    }
}

/// <summary>nc_sys_ai_judgments 行映射（列 snake_case → 属性 PascalCase，见 AGENTS §4）</summary>
internal sealed class AiJudgmentCacheRow
{
    public string Verdict { get; set; } = string.Empty;
    public string MatchedItem { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}
