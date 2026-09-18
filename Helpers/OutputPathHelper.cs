namespace NewCosmos.Helpers;

/// <summary>
/// 通用输出路径生成    /// 目录结构: 输出/{分类}/{日期}/{主体名_主体ID}/{文件类型_时间戳}.{扩展名}
/// </summary>
public static class OutputPathHelper
{
    private static readonly string OUTPUT_ROOT = "输出";

    /// <summary>
    /// 获取完整输出文件路径
    /// 输出/{category}/{date}/{subjectName_subjectId}/{fileType}_{timestamp}.{extension}
    /// </summary>
    public static string GetFilePath(
        string category,
        string subjectName,
        string subjectId,
        string fileType,
        string extension,
        DateTime? date = null,
        string? timestamp = null)
    {
        var effectiveDate = date ?? DateTime.Today;
        // 历史 Bug："N2" 是数值格式符，DateTime.ToString("N2") 输出字面量 "N2"——
        // 导致日期目录恒为 "N2"、文件名无时间戳，同人同模板每次打印互相覆盖，
        // 且打印记录 file_path 全部指向同一个被覆盖的文件（打印留痕失效）。
        var effectiveTimestamp = timestamp ?? DateTime.Now.ToString("yyyyMMddHHmmssfff");

        var dateStr = effectiveDate.ToString("yyyy-MM-dd");
        var subjectDir = $"{subjectName}_{subjectId}";
        var fileName = $"{fileType}_{effectiveTimestamp}{extension}";

        var relativePath = Path.Combine(OUTPUT_ROOT, category, dateStr, subjectDir, fileName);
        return Path.GetFullPath(relativePath);
    }

    /// <summary>
    /// 获取同目录下其他扩展名的文件路径
    /// </summary>
    public static string GetSiblingPath(string sourcePath, string newExtension)
    {
        var dir = Path.GetDirectoryName(sourcePath) ?? ".";
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        return Path.Combine(dir, $"{fileName}{newExtension}");
    }

    /// <summary>
    /// 确保输出目录存在
    /// </summary>
    public static string EnsureDirectoryExists(
        string category,
        string subjectName,
        string subjectId,
        DateTime? date = null)
    {
        var effectiveDate = date ?? DateTime.Today;
        var dateStr = effectiveDate.ToString("yyyy-MM-dd");
        var subjectDir = $"{subjectName}_{subjectId}";

        var dirPath = Path.Combine(OUTPUT_ROOT, category, dateStr, subjectDir);
        var fullPath = Path.GetFullPath(dirPath);

        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
        }

        return fullPath;
    }

    /// <summary>
    /// 获取主体目录路径（不含文件名）    /// </summary>
    public static string GetSubjectDirectory(
        string category,
        string subjectName,
        string subjectId,
        DateTime? date = null)
    {
        var effectiveDate = date ?? DateTime.Today;
        var dateStr = effectiveDate.ToString("yyyy-MM-dd");
        var subjectDir = $"{subjectName}_{subjectId}";

        var relativePath = Path.Combine(OUTPUT_ROOT, category, dateStr, subjectDir);
        return Path.GetFullPath(relativePath);
    }

    /// <summary>
    /// 获取业务类型对应的中文分类名
    /// </summary>
    public static string GetCategoryDisplayName(string businessType)
    {
        return businessType switch
        {
            "AssetVerification" => "资产核查",
            "FamilyApplication" => "社会救助申请",
            "MonthlyReport" => "月报表",
            "EconomicReview" => "经济复核",
            "ArchiveQuery" => "档案查询",
            "TemporaryAssistance" => "临时救助",
            "TempRelief" => "临时救助",
            "ElderlyBenefits" => "普惠高龄",
            "LowIncomeProof" => "低保证明",
            _ => businessType
        };
    }
}