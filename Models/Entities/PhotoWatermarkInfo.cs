using System.Globalization;
using NewCosmos.Constants;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 照片水印上下文（2026-11-15，仅「入户排查 / 公示核查」类别在保存时烧进图片）。
/// 采集/插入端组装拍摄时间与 GPS（经纬度已含 ±<see cref="PhotoWatermarkConstants.GpsJitterMeters"/> 米随机偏差）；
/// 姓名/身份证/地址可先留空，由 <c>ArchivePhotoService</c> 落库前按**户主档案**补齐（地址不兜底单位）。
/// </summary>
public sealed record PhotoWatermarkInfo(
    DateTime CapturedAt,
    double? Latitude = null,
    double? Longitude = null,
    string PersonName = "",
    string IdCard = "",
    string Address = "")
{
    /// <summary>
    /// 左下角信息条文本行（缺项自动省略）：
    /// ① 分类 · 户主姓名 · 身份证号；② 地址（林口县柳树镇XXX村村民委员会）；
    /// ③ 2026年11月15日 14时30分；④ 北纬…° 东经…°（GPS 取不到时不出该行）。
    /// </summary>
    public IReadOnlyList<string> BuildInfoLines(string categoryName)
    {
        var lines = new List<string>(4);

        var name = PersonName?.Trim() ?? string.Empty;
        var idCard = IdCard?.Trim() ?? string.Empty;
        lines.Add(name.Length > 0 && idCard.Length > 0
            ? $"{categoryName} · {name} · {idCard}"
            : name.Length > 0
                ? $"{categoryName} · {name}"
                : idCard.Length > 0
                    ? $"{categoryName} · {idCard}"
                    : categoryName);

        var address = Address?.Trim() ?? string.Empty;
        if (address.Length > 0)
            lines.Add(address);

        lines.Add(CapturedAt.ToString("yyyy'年'M'月'd'日' HH'时'mm'分'", CultureInfo.InvariantCulture));

        if (Latitude.HasValue && Longitude.HasValue)
        {
            var lat = Latitude.Value.ToString("0.00000", CultureInfo.InvariantCulture);
            var lng = Longitude.Value.ToString("0.00000", CultureInfo.InvariantCulture);
            lines.Add($"北纬{lat}° 东经{lng}°");
        }

        return lines;
    }
}
