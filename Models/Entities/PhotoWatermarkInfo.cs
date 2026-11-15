using System.Globalization;
using NewCosmos.Constants;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 照片水印上下文（2026-11-15，仅「入户排查 / 公示核查」类别在保存时烧进图片）。
/// 采集/插入端只提供拍摄时刻与已知姓名/身份证；**地址、村屯坐标（区划坐标列/在线定位回写的
/// 固定地理编码值）、档案时间轴时间**由 <c>ArchivePhotoService.FillWatermarkContextAsync</c>
/// 按批一次补齐。地址只取户主档案，不兜底单位；IsResolved = 已补齐，逐张保存不再查库。
/// </summary>
public sealed record PhotoWatermarkInfo(
    DateTime CapturedAt,
    double? Latitude = null,
    double? Longitude = null,
    string PersonName = "",
    string IdCard = "",
    string Address = "",
    bool IsResolved = false)
{
    /// <summary>
    /// 左下角信息条文本行（缺项自动省略，每行带区分抬头）：
    /// ① 分类 · 户主姓名 · 身份证号；② 地址：…；
    /// ③ 时间：…（档案时间轴时间 + 记录创建时分，见 ArchivePhotoService；全取不到回退采集时刻）；
    /// ④ 经纬度：北纬…° 东经…°（村屯坐标取不到时不出该行）。
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
            lines.Add($"地址：{address}");

        lines.Add($"时间：{CapturedAt.ToString("yyyy'年'M'月'd'日' HH'时'mm'分'", CultureInfo.InvariantCulture)}");

        if (Latitude.HasValue && Longitude.HasValue)
        {
            var lat = Latitude.Value.ToString("0.00000", CultureInfo.InvariantCulture);
            var lng = Longitude.Value.ToString("0.00000", CultureInfo.InvariantCulture);
            lines.Add($"经纬度：北纬{lat}° 东经{lng}°");
        }

        return lines;
    }
}
