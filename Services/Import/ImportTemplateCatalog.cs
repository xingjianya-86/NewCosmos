using NewCosmos.Constants;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Import;

/// <summary>
/// 导入模板规格（描述一个导入类型的标准 Excel 模板）。
/// 列定义单一来源 = <see cref="AnnualPlantingColumns"/>，禁止在本文件重抄列名/分类名。
/// </summary>
public sealed class ImportTemplateSpec
{
    public string ImportType { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string SheetName { get; init; } = string.Empty;
    /// <summary>表头行（按顺序）</summary>
    public string[] Headers { get; init; } = Array.Empty<string>();
    /// <summary>分类列的下拉取值（严格集合，非法值导入时报错）；空表示该模板无分类列</summary>
    public string[] CategoryList { get; init; } = Array.Empty<string>();
    /// <summary>「填写说明」工作表的逐行文字</summary>
    public string[] Instructions { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 导入模板目录：按导入类型给出模板规格（当前支持 年度种植补贴）。
/// 新增可导出模板的导入类型时，只在本类登记一处。
/// </summary>
public static class ImportTemplateCatalog
{
    public static ImportTemplateSpec? Get(string importType) => importType switch
    {
        ImportTypeCodes.ANNUAL_PLANTING_SUBSIDY => AnnualPlanting,
        _ => null
    };

    private static readonly ImportTemplateSpec AnnualPlanting = new()
    {
        ImportType = ImportTypeCodes.ANNUAL_PLANTING_SUBSIDY,
        FileName = "年度种植补贴导入模板.xlsx",
        SheetName = "年度种植补贴",
        Headers = AnnualPlantingColumns.GetTemplateHeaders(),
        CategoryList = AnnualPlantingColumns.Categories,
        Instructions = new[]
        {
            "一、用途",
            "  本模板用于「年度种植补贴导入」，一次性导入当年各类土地补贴，替代原 农业补贴(地力)/种植补贴/大豆补贴/轮作补贴 四个入口。",
            "  结构为长表：一行 = 一个年段 × 一个人 × 一个补贴分类。",
            "",
            "二、列说明（共 5 列）",
            "  1. 数据年份（可选）：填 2026 这样的四位年份。规则：本行有值用本行 → 本行为空沿用上一个有效值（通常只在第一行填一次）→",
            "     整列没填则取文件名里的 20xx → 还没有则取当前年。一个文件里可以混多个年份，系统按年段分别幂等写入。",
            "     ※ 若在「数据导入」页顶部填了「导入年份」，则**以页面为准、忽略本列**；若勾了「按区间」，本列年份必须落在区间内，越界整份报错。",
            "  2. 姓名：必填。",
            "  3. 身份证号：必填，15 位或 18 位（末位允许 X/x，如 2310…492X）；单元格保持文本格式，避免被 Excel 转成科学计数法。",
            "  4. 分类：必填，**只能从下拉选择**——" + string.Join(" / ", AnnualPlantingColumns.Categories) + "；",
            "     写成别的（如「玉米」「水稻地表水」）会整份报错并提示行号，该文件不会写入任何数据。",
            "  5. 金额（元）：必填，单位元，支持 1,234.56 这样的写法；为空或 0 的行会被跳过（计为警告）。",
            "",
            "三、金额入账口径",
            "  玉米/大豆/地表水水稻/地下水水稻/地力补贴/轮作补贴 有固定单价，入申请表单时按当日单价反推亩数（存在分级舍入差）；",
            "  大豆品种补贴无统一单价，只记金额（表单内为金额型行，不显示亩数）。",
            "",
            "四、文件名要求",
            "  1. 建议文件名含年份（如「XX村2026补贴.xlsx」），作为「数据年份」列缺失时的兜底。",
            "  2. 地力补贴分「社保卡」「非社保卡」发放时，请在文件名带「社保卡」或「非社保卡」字样，便于来源审计。",
            "",
            "五、导入规则",
            "  1. 同一年段、同一人、同一分类的多行自动求和合并。",
            "  2. 幂等：导入前按（年段 + 证件）整批覆盖，重复导入同一文件不会产生重复数据；",
            "     同一人同一年段出现在多个文件时，后导入的文件覆盖先前记录（单村单文件不会遇到）。",
            "  3. 勾选「导入前清空表数据」＝**只清本次导入实际涉及的年段**（其余年段如历史台账原样保留），清空前自动整库备份；",
            "     不勾选则只覆盖本次文件涉及的证件（同年段、同证件的旧记录）。",
            "  4. 导入前可先在本页点「预览」，导入后用「导出日志」核对各分类人数与金额小计。",
        }
    };
}
