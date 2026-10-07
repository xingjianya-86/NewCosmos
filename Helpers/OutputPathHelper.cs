namespace NewCosmos.Helpers;

/// <summary>
/// 通用输出路径生成。
/// 目录结构: {输出根}/{分类}/{日期}/{主体名_主体ID}/{文件类型_时间戳}.{扩展名}
/// 输出根由 config/document_output.yaml 的 output.base_directory 决定（App 启动时 Configure 注入）；
/// 未 Configure 时回退历史相对目录"输出"，保持旧行为。
/// 预览临时文件统一落 {输出根}/{temp 子目录}（清理规则见 yaml 的 cleanup 节）。
/// </summary>
public static class OutputPathHelper
{
    /// <summary>历史相对输出根：Configure 缺省时的回退值，同时是迁移源目录</summary>
    public const string LegacyOutputRoot = "输出";

    private static string? _outputRoot;
    private static string _tempSubdirectory = "temp";

    /// <summary>
    /// 启动时注入输出根与预览临时子目录（App.CreateWindow 调用，见 IConfigService.GetDocumentOutputOptions）
    /// </summary>
    public static void Configure(string outputRoot, string tempSubdirectory = "temp")
    {
        if (!string.IsNullOrWhiteSpace(outputRoot))
            _outputRoot = Path.GetFullPath(outputRoot);
        if (!string.IsNullOrWhiteSpace(tempSubdirectory))
            _tempSubdirectory = tempSubdirectory;
    }

    /// <summary>当前输出根（绝对路径；未 Configure 时回退相对"输出"）</summary>
    public static string OutputRoot => _outputRoot ?? Path.GetFullPath(LegacyOutputRoot);

    /// <summary>历史输出根（绝对路径），供一次性迁移定位旧文件</summary>
    public static string LegacyRoot => Path.GetFullPath(LegacyOutputRoot);

    /// <summary>
    /// 预览临时目录（{输出根}/{temp 子目录}），不存在时自动创建。
    /// 预览 PDF 落这里，由启动清理按 yaml cleanup 规则回收。
    /// </summary>
    public static string GetTempDirectory()
    {
        var dir = Path.Combine(OutputRoot, _tempSubdirectory);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 获取完整输出文件路径
    /// {输出根}/{category}/{date}/{subjectName_subjectId}/{fileType}_{timestamp}.{extension}
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

        var fullPath = Path.Combine(OutputRoot, category, dateStr, subjectDir, fileName);
        return Path.GetFullPath(fullPath);
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

        var dirPath = Path.Combine(OutputRoot, category, dateStr, subjectDir);
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

        var fullPath = Path.Combine(OutputRoot, category, dateStr, subjectDir);
        return Path.GetFullPath(fullPath);
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
