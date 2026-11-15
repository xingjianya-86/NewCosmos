using System.Globalization;
using System.Text.RegularExpressions;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;

namespace NewCosmos.Helpers;

/// <summary>
/// 档案照片命名规则（2026-11-04 定稿）：
/// <c>姓名_身份证后4位_类别_编号</c>（如「张三_1234_身份证_01」）。
/// 编号 = 该人 + 该类别内的流水号，**往下递增、不覆盖**（重拍新照片编号继续往后走）。
/// 姓名缺失时省略姓名段（如「1234_身份证_01」）；类别显示名中的 '/' 替换为全角 '／'
/// （病历/诊断证明、银行卡/一卡通 等含斜杠，会破坏文件名与导出路径）。
/// </summary>
public static class PhotoNameBuilder
{
    private static readonly Regex TrailingSeqRegex = new(@"_(\d{1,9})$", RegexOptions.Compiled);

    /// <summary>姓名段识别（TryParsePersonName 用）：2~8 个汉字。</summary>
    private static readonly Regex PersonNameRegex = new(@"^[一-龥]{2,8}$", RegexOptions.Compiled);

    /// <summary>
    /// 计算该人该类别下一编号：max(现有照片名可解析的最大流水号, 现有照片总数) + 1。
    /// 旧数据（PhotoName 为 null 或自由备注）不产生流水号，由照片总数兜底，
    /// 保证「往下走」——即使中间删除过照片也不会回退复用。
    /// </summary>
    public static int NextSequence(IReadOnlyCollection<ArchivePhoto> existingPhotos)
    {
        var maxParsed = 0;
        foreach (var photo in existingPhotos)
        {
            if (string.IsNullOrWhiteSpace(photo.PhotoName)) continue;
            var m = TrailingSeqRegex.Match(photo.PhotoName);
            if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                maxParsed = Math.Max(maxParsed, n);
        }

        return Math.Max(maxParsed, existingPhotos.Count) + 1;
    }

    /// <summary>按规则生成 PhotoName。</summary>
    /// <param name="personName">姓名（空则省略姓名段）</param>
    /// <param name="idCard">身份证号（取后 4 位）</param>
    /// <param name="category">照片类别代码（PhotoCategoryConstants）</param>
    /// <param name="sequence">该人该类别内流水号（从 1 起）</param>
    public static string Build(string? personName, string? idCard, string? category, int sequence)
    {
        var idLast4 = IdCardLast4(idCard);
        var categoryName = PhotoCategoryConstants.GetDisplayName(category).Replace('/', '／');
        var seqText = sequence.ToString("D2", CultureInfo.InvariantCulture);

        var name = personName?.Trim();
        return string.IsNullOrEmpty(name)
            ? string.IsNullOrEmpty(idLast4)
                ? $"{categoryName}_{seqText}"
                : $"{idLast4}_{categoryName}_{seqText}"
            : string.IsNullOrEmpty(idLast4)
                ? $"{name}_{categoryName}_{seqText}"
                : $"{name}_{idLast4}_{categoryName}_{seqText}";
    }

    /// <summary>身份证后 4 位（不足 4 位取全号；空返回空串）。</summary>
    public static string IdCardLast4(string? idCard)
    {
        var trimmed = idCard?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return string.Empty;
        return trimmed.Length <= 4 ? trimmed : trimmed[^4..];
    }

    /// <summary>
    /// 从 PhotoName 反解析姓名段（打印抬头等场景回退用），解析不出返回空串。
    /// 识别规则（对 <see cref="Build"/> 的产出与旧数据双兼容）：
    /// ① 首段为纯数字（「1234_身份证_01」无姓名格式）→ 空；
    /// ② 首段为已知类别显示名（「身份证_01」无姓名格式，含 '/'→'／' 归一）→ 空；
    /// ③ 首段必须是 2~8 个汉字的姓名，否则（旧自由备注、乱串）→ 空，宁缺勿错。
    /// </summary>
    public static string TryParsePersonName(string? photoName)
    {
        if (string.IsNullOrWhiteSpace(photoName)) return string.Empty;

        var segments = photoName.Split('_');
        if (segments.Length < 2) return string.Empty;

        var first = segments[0].Trim();
        if (first.Length == 0) return string.Empty;

        // ① 数字开头 = 身份证后4位段在前（无姓名格式）
        if (first.All(char.IsDigit)) return string.Empty;

        // ② 类别名开头 = 类别_编号 格式（无姓名格式）
        foreach (var (_, name) in PhotoCategoryConstants.All)
        {
            var normalized = name.Replace('/', '／');
            if (string.Equals(first, normalized, StringComparison.Ordinal) ||
                string.Equals(first, name, StringComparison.Ordinal))
                return string.Empty;
        }
        if (string.Equals(first, "其他", StringComparison.Ordinal)) return string.Empty;

        // ③ 2~8 个汉字才认作姓名
        return PersonNameRegex.IsMatch(first) ? first : string.Empty;
    }

    /// <summary>把 PhotoName 清洗为合法文件名（导出/打印用）：替换平台非法字符。</summary>
    public static string SanitizeFileName(string? photoName)
    {
        if (string.IsNullOrWhiteSpace(photoName)) return string.Empty;
        var invalid = Path.GetInvalidFileNameChars();
        var chars = photoName.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars).Trim('.', ' ', '_');
    }

    /// <summary>
    /// 导出文件名（2026-11-04 命名规则，桌面查询页 / 手机采集页共用，2026-11-15 收敛两份重复实现）：
    /// 优先用 PhotoName（姓名_后4位_类别_编号）；旧数据无名时回退 {类别}_{时间}_{Id}（本身已含 Id）；
    /// <paramref name="forceIdSuffix"/> 在有名时追加 _{Id} 防同名覆盖（相册导出/同名重导出用）。
    /// </summary>
    public static string BuildExportFileName(ArchivePhoto photo, string format, bool forceIdSuffix = false)
    {
        var ext = PhotoFormatConstants.GetExtension(format);
        var baseName = SanitizeFileName(photo.PhotoName);

        if (string.IsNullOrEmpty(baseName))
            baseName = $"{photo.PhotoCategory}_{photo.CreatedAt:yyyyMMdd_HHmmss}_{photo.Id}";
        else if (forceIdSuffix)
            baseName = $"{baseName}_{photo.Id}";

        return baseName + ext;
    }
}
