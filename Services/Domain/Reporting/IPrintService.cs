using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Templates;

namespace NewCosmos.Services.Domain.Reporting;

/// <summary>
/// 打印服务接口
/// </summary>
public interface IPrintService
{
    /// <summary>
    /// 打印档案
    /// </summary>
    Task<Result<byte[]>> PrintArchiveAsync(long archiveId, CancellationToken ct = default);

    /// <summary>
    /// 打印变更确认书
    /// </summary>
    Task<Result<byte[]>> PrintChangeConfirmationAsync(long changeRecordId, CancellationToken ct = default);

    /// <summary>
    /// 渲染月报表表单（8 张 Excel 表 + 2 张会议记录 docx，按"月报表"分类模板查名渲染）
    /// formKey：新增救助明细 / 新增救助明细_低收入 / 停保汇总表 / 停保汇总表_低收入 /
    ///           保障金增发表 / 保障金减发表 / 分类施保金减发人员表（低保） / 人员变动_自然减员月报表 / 会议记录 / 会议记录_一事一议
    /// </summary>
    Task<Result<byte[]>> RenderMonthlyFormAsync(int year, int month, string formKey, string town, CancellationToken ct = default);

    /// <summary>
    /// 批量渲染多张月报表单并合并为一份 PDF（勾选列表预览）
    /// </summary>
    Task<Result<byte[]>> RenderMonthlyFormsMergedAsync(int year, int month, IEnumerable<string> formKeys, string town, CancellationToken ct = default);

    /// <summary>
    /// 渲染月报表表单为源文件字节（xlsx/docx，供导出；双页表单拆多页）
    /// </summary>
    Task<Result<List<MonthlySourceFile>>> RenderMonthlyFormToSourceAsync(int year, int month, string formKey, string town, CancellationToken ct = default);

    /// <summary>
    /// 打印月报表表单（渲染源文件后走 Office COM 打印；双页表单按页打印）
    /// </summary>
    Task<Result<bool>> PrintMonthlyFormAsync(int year, int month, string formKey, string town, string printerName, int copies = 1, CancellationToken ct = default);

    /// <summary>
    /// 渲染普惠高龄新增/停止明细表为 PDF（自然月，模板 142/144，每页 13 行，超行分页合并）
    /// categoryCode: 享受类别代码（CAT1-CAT4），空 = 全部分类
    /// </summary>
    Task<Result<byte[]>> RenderElderlyMonthlyFormAsync(int year, int month, bool isStop, string categoryCode = "", CancellationToken ct = default);

    /// <summary>
    /// 批量渲染普惠高龄明细表并合并为一份 PDF（勾选列表预览）；无数据表单自动跳过
    /// </summary>
    Task<Result<byte[]>> RenderElderlyMonthlyFormsMergedAsync(int year, int month, IEnumerable<string> formKeys, CancellationToken ct = default);

    /// <summary>
    /// 渲染普惠高龄明细表为源文件字节（xlsx，供导出；超 13 人拆多页文件）
    /// categoryCode: 享受类别代码（CAT1-CAT4），空 = 全部分类
    /// </summary>
    Task<Result<List<MonthlySourceFile>>> RenderElderlyMonthlyFormToSourceAsync(int year, int month, bool isStop, string categoryCode = "", CancellationToken ct = default);

    /// <summary>
    /// 打印普惠高龄新增/停止明细表（渲染源文件后走 Office COM 打印；超 13 人分多页）
    /// categoryCode: 享受类别代码（CAT1-CAT4）
    /// </summary>
    Task<Result<bool>> PrintElderlyMonthlyFormAsync(int year, int month, bool isStop, string categoryCode, string printerName, int copies = 1, CancellationToken ct = default);

    /// <summary>
    /// 渲染《普惠高龄津贴调整备案表》为 PDF（月报「满90周岁」逐人一页；首次渲染自动写入待复核队列）。
    /// </summary>
    Task<Result<byte[]>> RenderElderlyAge90FormAsync(int year, int month, CancellationToken ct = default);

    /// <summary>渲染《普惠高龄津贴调整备案表》源文件字节（xlsx，逐人一个文件）</summary>
    Task<Result<List<MonthlySourceFile>>> RenderElderlyAge90FormToSourceAsync(int year, int month, CancellationToken ct = default);

    /// <summary>打印《普惠高龄津贴调整备案表》（月报「满90周岁」逐人一页，走 Office COM）</summary>
    Task<Result<bool>> PrintElderlyAge90FormAsync(int year, int month, string printerName, int copies = 1, CancellationToken ct = default);

    /// <summary>
    /// 打印低保证明
    /// </summary>
    Task<Result<byte[]>> PrintLowIncomeProofAsync(long archiveId, CancellationToken ct = default);

    /// <summary>
    /// 生成PDF（基于模板引擎）
    /// </summary>
    Task<Result<byte[]>> GeneratePdfAsync(long templateId, Dictionary<string, string> fields, CancellationToken ct = default);

    /// <summary>
    /// 生成PDF（基于模板引擎 + 表格数据）
    /// </summary>
    Task<Result<byte[]>> GeneratePdfWithTableAsync(long templateId, Dictionary<string, string> fields, List<Dictionary<string, string>> tableRows, CancellationToken ct = default, IReadOnlyCollection<string>? removeParagraphPlaceholders = null);

    /// <summary>
    /// 获取可用模板列表
    /// </summary>
    Task<Result<List<PrintTemplate>>> GetTemplatesAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取模板引擎工厂（供高级场景使用）
    /// </summary>
    ITemplateEngineFactory GetEngineFactory();
}

/// <summary>
/// 打印模板
/// </summary>
public class PrintTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TemplatePath { get; set; } = string.Empty;
    public string Status { get; set; } = ApplicationStatusCodes.ACTIVE;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 打印数据
/// </summary>
public class PrintData
{
    public string TemplateCode { get; set; } = string.Empty;
    public Dictionary<string, object> Fields { get; set; } = new();
    public List<Dictionary<string, object>> TableData { get; set; } = new();
    public bool ShowGracePeriodTag { get; set; }
    public GracePeriodPrintInfo GracePeriodInfo { get; set; } = new();
}

/// <summary>
/// 月报表单源文件结果（一张双页表单拆为农村/城市两份）
/// </summary>
public class MonthlySourceFile
{
    /// <summary>
    /// 文件名（不含扩展名），如 202608_新增救助明细_低保_农村
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 扩展名（.xlsx / .docx）
    /// </summary>
    public string Extension { get; set; } = ".xlsx";

    /// <summary>
    /// 源文件字节
    /// </summary>
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// 渐退期打印信息
/// </summary>
public class GracePeriodPrintInfo
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string OriginalClassification { get; set; } = string.Empty;
    public decimal OriginalAmount { get; set; }
}