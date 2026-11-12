using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Platform;
using NewCosmos.Services.System;

namespace NewCosmos.Services.Domain.SevereDisease;

/// <summary>
/// 重病判定建议服务（2026-11-12 P1）。三级管线：
/// ① 本地确定性（SevereDiseaseJudge，随包清单，毫秒/离线）→ 直接返回，不走网络；
/// ② 缓存（nc_sys_ai_judgments，键含清单版本）→ 命中即返；缓存读失败降级服务端（仅记 Warn，非关键通道）；
/// ③ 服务端 /ai/severity（LLM 兜底语义变体）→ 归一校验后写缓存（写失败不失败判定）。
/// 另含费用口径阈值读取（SevereDiseaseByCostRule，第六十八条，城乡两档）。
/// 失败一律 Result.Failure 透传（§7）：调用方显示"无建议"，不影响表单保存。
/// Singleton，无每操作可变状态（§6）。
/// </summary>
public sealed class SevereDiseaseAdviceService : ISevereDiseaseAdviceService
{
    private readonly IAiJudgeClient _aiClient;
    private readonly IAiJudgmentCacheService _cache;
    private readonly IStandardConfigService _standardConfigs;
    private readonly ILoggerService _logger;

    public SevereDiseaseAdviceService(
        IAiJudgeClient aiClient,
        IAiJudgmentCacheService cache,
        IStandardConfigService standardConfigs,
        ILoggerService logger)
    {
        _aiClient = aiClient;
        _cache = cache;
        _standardConfigs = standardConfigs;
        _logger = logger;
    }

    public SevereDiseaseJudgment? TryDeterministic(string? name, string? code)
        => SevereDiseaseJudge.Default.TryJudgeDeterministic(name, code, out var judgment) ? judgment : null;

    public async Task<Result<SevereDiseaseJudgment>> JudgeAsync(string? name, string? code, CancellationToken ct = default)
    {
        name = (name ?? string.Empty).Trim();
        code = (code ?? string.Empty).Trim();
        if (name.Length == 0)
            return Result.Failure<SevereDiseaseJudgment>(ErrorCodes.AI_JUDGE_FAILED,
                "疾病名称为空，无法判定（请先由疾病编码带出名称）");

        // ① 本地确定性：毫秒级、离线可用，命中即终局（source=deterministic）
        if (SevereDiseaseJudge.Default.TryJudgeDeterministic(name, code, out var deterministic))
            return Result.Success(deterministic);

        // ② 缓存：同输入+同清单版本的历史 LLM 判定
        var key = SevereDiseaseJudge.BuildCacheKey(name, code);
        var cached = await _cache.GetAsync(key, ct);
        if (cached.IsSuccess)
        {
            if (cached.Value is { } hit)
                return Result.Success(hit);
        }
        else
        {
            _logger.Warn($"AI 判定缓存读取失败，降级走服务端: {cached.ErrorCode}");
        }

        // ③ 服务端 LLM 兜底
        var remote = await _aiClient.JudgeAsync(name, code, ct);
        if (remote.IsFailure)
            return Result.Failure<SevereDiseaseJudgment>(remote.ErrorCode, remote.Message);

        var judgment = remote.Value;
        if (judgment.Source == SevereDiseaseJudgment.SourceLlm)
        {
            var saved = await _cache.SetAsync(key, judgment, ct);
            if (saved.IsFailure)
                _logger.Warn($"AI 判定缓存写入失败（判定本身不受影响）: {saved.ErrorCode}");
        }
        return Result.Success(judgment);
    }

    public async Task<Result<decimal?>> GetCostThresholdAsync(bool isRural, CancellationToken ct = default)
    {
        var hukouType = isRural ? "Rural" : "Urban";
        var result = await _standardConfigs.GetStandardValueAsync(
            SevereDiseaseByCostRule.CostThresholdStandardType, hukouType, null, ct);
        if (result.IsFailure)
        {
            _logger.Warn($"费用口径阈值读取失败（降级为不判定）: {result.ErrorCode}");
            return Result<decimal?>.Failure(result.ErrorCode, result.Message);
        }
        // GetStandardValueAsync 缺配置返回 Success(0)：视为未配置，调用方 IsSevereByCost 内按 ≤0 降级
        return Result<decimal?>.Success(result.Value);
    }
}
