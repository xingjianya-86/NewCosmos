using NewCosmos.Constants;
using System.Globalization;
using System.Text.Json;

namespace NewCosmos.Helpers;

/// <summary>
/// 村屯在线地理编码（Nominatim / OpenStreetMap，2026-11-15 水印二改）：
/// 纯 URL 组装与响应解析（可单测）；网络 IO 与坐标列回写在 ArchivePhotoService 内。
/// 调用约定：每个村屯每进程只查一次（服务侧负缓存），失败只省略水印经纬度行。
/// </summary>
public static class NominatimGeocoder
{
    /// <summary>组装搜索 URL：q = 县 + 镇 + 村（空级跳过），限定中国、首条结果。</summary>
    public static string BuildSearchUrl(string? county, string? town, string? village)
    {
        var q = string.Join(' ', new[] { county, town, village }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim()));

        return $"{PhotoWatermarkConstants.GeocodeApiUrl}?format=jsonv2&limit=1&countrycodes=cn&q={Uri.EscapeDataString(q)}";
    }

    /// <summary>
    /// 解析搜索响应（JSON 数组，取首个带数值坐标的元素）：返回 (经度, 纬度)；无结果/格式异常返回 null。
    /// </summary>
    public static (double Longitude, double Latitude)? ParseSearchResponse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (!el.TryGetProperty("lat", out var latEl) || !el.TryGetProperty("lon", out var lonEl))
                    continue;

                if (double.TryParse(latEl.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                    double.TryParse(lonEl.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                {
                    return (lon, lat);
                }
            }

            return null;
        }
        catch (JsonException ex)
        {
            Serilog.Log.Debug(ex, "[NominatimGeocoder] 地理编码响应解析失败，水印省略坐标");
            return null;
        }
    }
}
