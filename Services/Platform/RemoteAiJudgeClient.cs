using System.Text;
using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Services.Platform;

/// <summary>
/// 内网 AI 重病判定客户端（deploy\ocr\ocr_api.py /ai/severity，2026-11-12 P1）。
/// Singleton 共享 HttpClient；请求只含疾病名称/编码（无公民 PII，§12 边界比图片宽松）；
/// 地址复用 network.ini OcrServiceUrl——AI 与 OCR 是同一服务的两条路由。
/// 响应一律经 SevereDiseaseJudge.NormalizeServerJudgment 双侧校验（matched_item∈清单才保留，
/// 客户端不信任裸响应）。失败一律 Result.Failure 显式上抛（§7），禁止吞异常返回默认值。
/// </summary>
public sealed class RemoteAiJudgeClient : IAiJudgeClient
{
    private readonly IConfigService _configService;
    private readonly ILoggerService _logger;

    /// <summary>Singleton 共享 HttpClient（超时见 AiJudgeConstants.JudgeTimeoutSeconds）</summary>
    private static readonly HttpClient _http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = global::System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        });
        client.Timeout = TimeSpan.FromSeconds(AiJudgeConstants.JudgeTimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NewCosmos-App/1.0");
        return client;
    }

    public RemoteAiJudgeClient(IConfigService configService, ILoggerService logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ResolveBaseUrl());

    public Task<Result> CheckHealthAsync(CancellationToken ct = default) => HealthCoreAsync(ct);

    private async Task<Result> HealthCoreAsync(CancellationToken ct)
    {
        var baseUrl = ResolveBaseUrl();
        if (baseUrl is null)
            return Result.Failure(ErrorCodes.AI_SERVICE_UNREACHABLE, "判定服务地址未配置（network.ini OcrServiceUrl）");

        try
        {
            using var response = await _http.GetAsync($"{baseUrl}/health", ct);
            if (!response.IsSuccessStatusCode)
                return Result.Failure(ErrorCodes.AI_SERVICE_UNREACHABLE, $"判定服务健康检查失败 HTTP {(int)response.StatusCode}");
            return Result.Success();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result.Failure(ErrorCodes.CANCELLED, "操作已取消");
        }
        catch (Exception ex)
        {
            _logger.Warn($"AI 判定服务健康检查失败: {ex.Message}");
            return Result.Failure(ErrorCodes.AI_SERVICE_UNREACHABLE, "判定服务未连接（请确认内网/ZeroTier 在线或联系管理员）");
        }
    }

    public async Task<Result<SevereDiseaseJudgment>> JudgeAsync(string? name, string? code, CancellationToken ct = default)
    {
        name = (name ?? string.Empty).Trim();
        code = (code ?? string.Empty).Trim();
        if (name.Length == 0)
            return Result.Failure<SevereDiseaseJudgment>(ErrorCodes.AI_JUDGE_FAILED,
                "疾病名称为空，无法判定（请先由疾病编码带出名称）");

        var baseUrl = ResolveBaseUrl();
        if (baseUrl is null)
            return Result.Failure<SevereDiseaseJudgment>(ErrorCodes.AI_SERVICE_UNREACHABLE,
                "判定服务地址未配置（network.ini OcrServiceUrl）");

        try
        {
            using var content = new StringContent(
                JsonSerializer.Serialize(new { name, code }), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync($"{baseUrl}{AiJudgeConstants.SeverityPath}", content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var errCode = response.StatusCode switch
                {
                    global::System.Net.HttpStatusCode.BadRequest => ErrorCodes.AI_JUDGE_FAILED,
                    global::System.Net.HttpStatusCode.BadGateway => ErrorCodes.AI_SERVICE_UNREACHABLE,
                    _ => ErrorCodes.AI_JUDGE_FAILED,
                };
                _logger.Warn($"AI 判定失败 HTTP {(int)response.StatusCode}: {Truncate(body, 200)}");
                return Result.Failure<SevereDiseaseJudgment>(errCode,
                    errCode == ErrorCodes.AI_SERVICE_UNREACHABLE
                        ? "判定服务模型不可用，请稍后重试"
                        : "判定服务处理失败，请稍后重试");
            }

            var parsed = ParseResponse(body);
            if (parsed is null)
            {
                _logger.Warn("AI 判定响应解析失败（格式不符）");
                return Result.Failure<SevereDiseaseJudgment>(ErrorCodes.AI_JUDGE_FAILED, "判定服务返回数据异常");
            }

            var (judgment, elapsedMs) = parsed.Value;
            // 业务审计点（判定影响资格：自动预勾选/施保/免劳力，须留痕）；
            // 只记判定结论与编码，不记人员身份信息（§10）。
            _logger.LogBusiness("AI 重病判定完成",
                ("Verdict", judgment.Verdict),
                ("MatchedItem", judgment.MatchedItem),
                ("Source", judgment.Source),
                ("AutoEligible", judgment.AutoEligible.ToString()),
                ("Code", code),
                ("ElapsedMs", elapsedMs.ToString()));
            return Result.Success(judgment);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result.Failure<SevereDiseaseJudgment>(ErrorCodes.CANCELLED, "操作已取消");
        }
        catch (TaskCanceledException)
        {
            _logger.Warn($"AI 判定超时（{AiJudgeConstants.JudgeTimeoutSeconds}s）: {code}");
            return Result.Failure<SevereDiseaseJudgment>(ErrorCodes.AI_TIMEOUT, "判定服务响应超时，请稍后重试");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI 判定请求失败");
            return Result.Failure<SevereDiseaseJudgment>(ErrorCodes.AI_SERVICE_UNREACHABLE,
                "判定服务未连接（请确认内网/ZeroTier 在线或联系管理员）");
        }
    }

    /// <summary>
    /// 解析服务端响应（契约见 deploy\ocr\ocr_api.py / README），
    /// 并经 SevereDiseaseJudge.Default.NormalizeServerJudgment 校验（matched_item∈清单等）；
    /// 异常格式返回 null 由调用方显式失败。
    /// </summary>
    private static (SevereDiseaseJudgment Judgment, int ElapsedMs)? ParseResponse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty(AiJudgeConstants.ResponseKeys.Ok, out var ok) || !ok.GetBoolean())
                return null;

            var verdict = GetString(root, AiJudgeConstants.ResponseKeys.Verdict);
            var matched = GetString(root, AiJudgeConstants.ResponseKeys.MatchedItem);
            var reason = GetString(root, AiJudgeConstants.ResponseKeys.Reason);
            var source = GetString(root, AiJudgeConstants.ResponseKeys.Source);
            var policyVersion = GetString(root, AiJudgeConstants.ResponseKeys.PolicyVersion);
            var model = GetString(root, AiJudgeConstants.ResponseKeys.Model);
            var confidence = root.TryGetProperty(AiJudgeConstants.ResponseKeys.Confidence, out var conf)
                             && conf.TryGetDouble(out var c) ? c : 0.0;
            var elapsed = root.TryGetProperty(AiJudgeConstants.ResponseKeys.ElapsedMs, out var el)
                          && el.TryGetInt32(out var ms) ? ms : 0;

            var judgment = SevereDiseaseJudge.Default.NormalizeServerJudgment(
                verdict, matched, confidence, reason, source, policyVersion, model);
            return (judgment, elapsed);
        }
        catch (JsonException)
        {
            return null;
        }

        static string GetString(JsonElement root, string key)
            => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? string.Empty
                : string.Empty;
    }

    /// <summary>
    /// 解析服务地址：network.ini OcrServiceUrl（AI 与 OCR 同服务）。
    /// 只允许 http(s) 端点（同 RemoteOcrRecognizeClient 口径）；空=未启用，调用方降级为人工判定。
    /// </summary>
    private string? ResolveBaseUrl()
    {
        var url = _configService.GetNetworkOptions().OcrServiceUrl?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https")) return null;
        return url;
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";
}
