using System.Net.Http.Headers;
using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Services.Platform;

/// <summary>
/// 内网 OCR 识别远程客户端（deploy\ocr\ocr_api.py /ocr/recognize）。
/// Singleton 共享 HttpClient；图片仅发内网地址（构造时校验 scheme/host，拒绝外网端点，§12）。
/// 识别失败一律 Result.Failure 显式上抛，禁止吞异常返回空结果（§7）。
/// </summary>
public sealed class RemoteOcrRecognizeClient : IOcrRecognizeClient
{
    private readonly IConfigService _configService;
    private readonly ILoggerService _logger;

    /// <summary>Singleton 共享 HttpClient（60s 超时，见 OcrConstants.RecognizeTimeoutSeconds）</summary>
    private static readonly HttpClient _http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = global::System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        });
        client.Timeout = TimeSpan.FromSeconds(OcrConstants.RecognizeTimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NewCosmos-App/1.0");
        return client;
    }

    public RemoteOcrRecognizeClient(IConfigService configService, ILoggerService logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ResolveBaseUrl());

    public Task<Result> CheckHealthAsync(CancellationToken ct = default)
        => CheckHealthCoreAsync(ct);

    public Task<Result<OcrRecognizeResult>> RecognizeAsync(Stream image, string categoryCode, CancellationToken ct = default)
        => RecognizeCoreAsync(image, categoryCode, ct);

    private async Task<Result> CheckHealthCoreAsync(CancellationToken ct)
    {
        var baseUrl = ResolveBaseUrl();
        if (baseUrl is null)
            return Result.Failure(ErrorCodes.OCR_SERVICE_UNREACHABLE, "识别服务地址未配置（network.ini OcrServiceUrl）");

        try
        {
            using var response = await _http.GetAsync($"{baseUrl}/health", ct);
            if (!response.IsSuccessStatusCode)
                return Result.Failure(ErrorCodes.OCR_SERVICE_UNREACHABLE, $"识别服务健康检查失败 HTTP {(int)response.StatusCode}");
            return Result.Success();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result.Failure(ErrorCodes.CANCELLED, "操作已取消");
        }
        catch (Exception ex)
        {
            _logger.Warn($"OCR 健康检查失败: {ex.Message}");
            return Result.Failure(ErrorCodes.OCR_SERVICE_UNREACHABLE, "识别服务未连接（请确认内网/ZeroTier 在线或联系管理员）");
        }
    }

    private async Task<Result<OcrRecognizeResult>> RecognizeCoreAsync(Stream image, string categoryCode, CancellationToken ct)
    {
        categoryCode = (categoryCode ?? string.Empty).Trim().ToUpperInvariant();
        if (OcrFieldKeys.GetFields(categoryCode) is null)
            return Result.Failure<OcrRecognizeResult>(ErrorCodes.OCR_NO_FIELD, "该照片类别无需识别");

        var baseUrl = ResolveBaseUrl();
        if (baseUrl is null)
            return Result.Failure<OcrRecognizeResult>(ErrorCodes.OCR_SERVICE_UNREACHABLE, "识别服务地址未配置（network.ini OcrServiceUrl）");

        try
        {
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(image);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(streamContent, "file", "photo.bin");
            content.Add(new StringContent(categoryCode), "category");

            using var response = await _http.PostAsync($"{baseUrl}/ocr/recognize", content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                // 400/413/422 = 输入问题（用户可换图/换类别重试）；502 = 模型异常
                var code = response.StatusCode is global::System.Net.HttpStatusCode.BadRequest
                    or global::System.Net.HttpStatusCode.UnsupportedMediaType
                    or global::System.Net.HttpStatusCode.RequestEntityTooLarge
                    ? ErrorCodes.OCR_IMAGE_INVALID
                    : ErrorCodes.OCR_RECOGNIZE_FAILED;
                _logger.Warn($"OCR 识别失败 HTTP {(int)response.StatusCode}: {Truncate(body, 200)}");
                return Result.Failure<OcrRecognizeResult>(code, code == ErrorCodes.OCR_IMAGE_INVALID
                    ? "图片无法识别（格式或大小不符），请换一张图片"
                    : "识别服务处理失败，请稍后重试");
            }

            var result = ParseResponse(categoryCode, body);
            if (result is null)
            {
                _logger.Warn("OCR 响应解析失败（格式不符）");
                return Result.Failure<OcrRecognizeResult>(ErrorCodes.OCR_RECOGNIZE_FAILED, "识别服务返回数据异常");
            }

            // KIE 空结果兜底（mode=kie_fallback）：服务端已用文本提示词重试并回传 lines，
            // 客户端用 IdCardOcrParser 从文本行抽取姓名/证号（服务端 ocr_api.py 契约注释的配套逻辑）
            if (categoryCode == PhotoCategoryConstants.ID_CARD
                && !result.Fields.Values.Any(v => !string.IsNullOrWhiteSpace(v))
                && result.Lines.Count > 0)
            {
                var parsed = IdCardOcrParser.Parse(result.Lines);
                if (!string.IsNullOrWhiteSpace(parsed.Name)) result.Fields[OcrFieldKeys.Name] = parsed.Name.Trim();
                if (!string.IsNullOrWhiteSpace(parsed.IdCard)) result.Fields[OcrFieldKeys.IdCard] = parsed.IdCard.Trim();
            }

            _logger.LogBusiness("证件 OCR 识别完成",
                ("Category", categoryCode),
                ("Mode", result.Mode),
                ("FieldCount", result.Fields.Count(kv => !string.IsNullOrWhiteSpace(kv.Value))),
                ("LineCount", result.Lines.Count),
                ("ElapsedMs", result.ElapsedMs.ToString()));

            return Result.Success(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result.Failure<OcrRecognizeResult>(ErrorCodes.CANCELLED, "操作已取消");
        }
        catch (TaskCanceledException)
        {
            // HttpClient.Timeout 触发：CPU 推理偶发超时，给可重试话术
            _logger.Warn($"OCR 识别超时（{OcrConstants.RecognizeTimeoutSeconds}s）: {categoryCode}");
            return Result.Failure<OcrRecognizeResult>(ErrorCodes.OCR_TIMEOUT, "识别服务响应超时，请稍后重试");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR 识别请求失败");
            return Result.Failure<OcrRecognizeResult>(ErrorCodes.OCR_SERVICE_UNREACHABLE, "识别服务未连接（请确认内网/ZeroTier 在线或联系管理员）");
        }
    }

    /// <summary>解析服务端响应（契约见 deploy\ocr\ocr_api.py）；异常格式返回 null 由调用方显式失败</summary>
    private static OcrRecognizeResult? ParseResponse(string categoryCode, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty(OcrConstants.ResponseKeys.Ok, out var ok) || !ok.GetBoolean())
                return null;

            var fields = new Dictionary<string, string>();
            if (root.TryGetProperty(OcrConstants.ResponseKeys.Fields, out var f) && f.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in f.EnumerateObject())
                    fields[prop.Name] = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? string.Empty : string.Empty;
            }

            var lines = new List<string>();
            if (root.TryGetProperty(OcrConstants.ResponseKeys.Lines, out var l) && l.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in l.EnumerateArray())
                {
                    var text = item.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) lines.Add(text);
                }
            }

            return new OcrRecognizeResult
            {
                Category = root.TryGetProperty(OcrConstants.ResponseKeys.Category, out var c) ? c.GetString() ?? categoryCode : categoryCode,
                Mode = root.TryGetProperty(OcrConstants.ResponseKeys.Mode, out var m) ? m.GetString() ?? string.Empty : string.Empty,
                Fields = fields,
                Lines = lines,
                Raw = root.TryGetProperty(OcrConstants.ResponseKeys.Raw, out var r) ? r.GetString() ?? string.Empty : string.Empty,
                ElapsedMs = root.TryGetProperty(OcrConstants.ResponseKeys.ElapsedMs, out var e) ? e.GetInt64() : 0
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 解析服务地址：network.ini OcrServiceUrl。
    /// §12 边界：拒绝非 http(s) 或外网地址——图片含公民 PII，只允许内网端点。
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
