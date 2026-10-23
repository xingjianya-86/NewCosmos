using NewCosmos.Models.Results;
using OfficeOpenXml;
using OfficeOpenXml.DataValidation;
using OfficeOpenXml.Style;

namespace NewCosmos.Services.Import;

/// <summary>
/// 导入模板导出器：按 <see cref="ImportTemplateSpec"/> 在本地目录生成标准 Excel 模板。
/// 数据表只放表头（不放示例行，避免示例被误导入）；分类列加下拉数据验证从源头约束取值；
/// 填写说明放第二个工作表。列格式按表头关键字自动识别（身份证文本 / 金额两位小数 / 年份整数）。
/// </summary>
public static class ImportTemplateExporter
{
    /// <summary>
    /// 导出模板到指定目录。
    /// </summary>
    /// <returns>成功返回生成的完整文件路径；文件名冲突时自动追加 _1/_2 序号，不覆盖已有文件。</returns>
    public static Result<string> Export(ImportTemplateSpec spec, string folderPath)
    {
        if (spec == null)
            return Result.Failure<string>("TEMPLATE_NOT_FOUND", "该导入类型暂无模板");

        if (string.IsNullOrWhiteSpace(folderPath))
            return Result.Failure<string>("INVALID_FOLDER", "导出目录不能为空");

        try
        {
            Directory.CreateDirectory(folderPath);

            ExcelPackage.License.SetNonCommercialOrganization("民政社会救助管理系统");
            using var package = new ExcelPackage();

            BuildDataSheet(package, spec);
            BuildInstructionSheet(package, spec);

            var target = ResolveTargetPath(folderPath, spec.FileName);
            package.SaveAs(new FileInfo(target));

            return Result.Success(target);
        }
        catch (Exception ex)
        {
            return Result.Failure<string>("TEMPLATE_EXPORT_FAILED", $"导出模板失败: {ex.Message}");
        }
    }

    private static void BuildDataSheet(ExcelPackage package, ImportTemplateSpec spec)
    {
        var sheet = package.Workbook.Worksheets.Add(spec.SheetName);

        for (var c = 0; c < spec.Headers.Length; c++)
        {
            var cell = sheet.Cells[1, c + 1];
            cell.Value = spec.Headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.Size = 12;
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(global::System.Drawing.Color.FromArgb(0xDC, 0xF7, 0xFD));
            cell.Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
        }

        // 列格式与宽度（按表头关键字）。**格式打在整列上**：
        // 早期版本只套到第 5000 行，超出的行一旦被 Excel 当数字解析就会毁掉 18 位身份证（曾有 15766 行的实测文件）
        var categoryCol = 0;
        for (var c = 1; c <= spec.Headers.Length; c++)
        {
            var header = spec.Headers[c - 1];
            if (header.Contains("身份证"))
            {
                sheet.Column(c).Width = 22;
                sheet.Column(c).Style.Numberformat.Format = "@";   // 整列文本，防 18 位变科学计数
            }
            else if (header.Contains("金额"))
            {
                sheet.Column(c).Width = 16;
                sheet.Column(c).Style.Numberformat.Format = "#,##0.00";
            }
            else if (header.Contains("年份") || header.Contains("年度") || header.Contains("年段"))
            {
                sheet.Column(c).Width = 12;
                sheet.Column(c).Style.Numberformat.Format = "0";
            }
            else
            {
                sheet.Column(c).Width = header.Contains("分类") ? 16 : 14;
            }

            if (header.Contains("分类")) categoryCol = c;
        }

        // 分类列下拉数据验证：只允许规范名，从源头杜绝写错导致整份导入失败（范围覆盖到 10 万行）
        if (categoryCol > 0 && spec.CategoryList is { Length: > 0 })
        {
            var address = sheet.Cells[2, categoryCol, 100000, categoryCol].Address;
            var validation = sheet.DataValidations.AddListValidation(address);
            foreach (var value in spec.CategoryList) validation.Formula.Values.Add(value);
            validation.ShowErrorMessage = true;
            validation.AllowBlank = false;
            validation.ErrorStyle = ExcelDataValidationWarningStyle.stop;
            validation.ErrorTitle = "分类不合法";
            validation.Error = "只能选择：" + string.Join("、", spec.CategoryList);
            validation.ShowInputMessage = true;
            validation.PromptTitle = "补贴分类";
            validation.Prompt = "从下拉中选择；全部分类见「填写说明」工作表";
        }

        sheet.Row(1).Height = 30;
        sheet.Cells[1, 1, 1, spec.Headers.Length].AutoFilter = true;
        sheet.View.FreezePanes(2, 1);
    }

    private static void BuildInstructionSheet(ExcelPackage package, ImportTemplateSpec spec)
    {
        var sheet = package.Workbook.Worksheets.Add("填写说明");
        for (var i = 0; i < spec.Instructions.Length; i++)
        {
            sheet.Cells[i + 1, 1].Value = spec.Instructions[i];
        }
        sheet.Column(1).Width = 130;
    }

    private static string ResolveTargetPath(string folderPath, string fileName)
    {
        var target = Path.Combine(folderPath, fileName);
        if (!File.Exists(target)) return target;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(folderPath, $"{name}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(folderPath, $"{name}_{DateTime.Now:yyyyMMddHHmmss}{ext}");
    }
}
