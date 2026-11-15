using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using NewCosmos.Constants;

namespace NewCosmos.Helpers;

/// <summary>
/// 定位取点（水印经纬度用，2026-11-15）：运行时权限 → Geolocation 取点 →
/// 叠加 ±<see cref="PhotoWatermarkConstants.GpsJitterMeters"/> 米随机偏差（需求「小幅度偏差」）。
/// 任一环节失败（无权限/超时/无定位硬件/平台拒绝）返回 null —— 调用方省略水印经纬度行，不阻断保存。
/// </summary>
public static class GeoLocationHelper
{
    /// <summary>取当前位置并叠加随机偏差；失败/取消返回 null。</summary>
    public static async Task<(double Latitude, double Longitude)?> GetLocationWithJitterAsync(CancellationToken ct)
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
                return null;

            var request = new GeolocationRequest(
                GeolocationAccuracy.Medium,
                TimeSpan.FromSeconds(PhotoWatermarkConstants.GpsTimeoutSeconds));
            var location = await Geolocation.GetLocationAsync(request, ct);
            if (location == null)
                return null;

            return ApplyJitter(location.Latitude, location.Longitude,
                PhotoWatermarkConstants.GpsJitterMeters);
        }
        catch (OperationCanceledException)
        {
            return null; // 页面离开/令牌取消：正常路径
        }
        catch (Exception)
        {
            return null; // 定位失败不阻断照片保存（水印省略经纬度行）
        }
    }

    /// <summary>
    /// 以当前位置为圆心叠加半径 <paramref name="jitterMeters"/> 米内的均匀随机偏差。
    /// 半径按 sqrt(U) 分布保证平面均匀；公开供单测（偏差范围/确定性种子场景）。
    /// </summary>
    public static (double Latitude, double Longitude) ApplyJitter(
        double latitude, double longitude, double jitterMeters)
    {
        if (jitterMeters <= 0) return (latitude, longitude);

        var radius = jitterMeters * Math.Sqrt(Random.Shared.NextDouble());
        var theta = Random.Shared.NextDouble() * 2 * Math.PI;

        const double metersPerDegreeLat = 111320.0;
        var cosLat = Math.Cos(latitude * Math.PI / 180.0);
        if (Math.Abs(cosLat) < 1e-9) return (latitude, longitude);

        var dLat = radius * Math.Cos(theta) / metersPerDegreeLat;
        var dLng = radius * Math.Sin(theta) / (metersPerDegreeLat * Math.Abs(cosLat));
        return (latitude + dLat, longitude + dLng);
    }
}
