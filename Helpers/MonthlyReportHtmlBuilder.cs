using System.Net;
using System.Text;
using NewCosmos.Services.Domain.Reporting;

namespace NewCosmos.Helpers;

/// <summary>
/// 月报预览 HTML 构建器（手机端 WebView 预览专用）：
/// 将 BuildMonthlyFormsPreviewDataAsync 的数据组装为自包含 HTML 表格，
/// 绕开 Office COM/PDF（Android 无 PDF 生成能力）；打印请走电脑端或推送队列。
/// </summary>
public static class MonthlyReportHtmlBuilder
{
    public static string Build(IReadOnlyList<MonthlyFormPreviewData> sections)
    {
        var sb = new StringBuilder(4096);
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>")
          .Append("<style>")
          .Append("body{font-family:sans-serif;font-size:13px;color:#1f2937;margin:12px;background:#fff;}")
          .Append("h2{font-size:17px;text-align:center;margin:8px 0 4px;}")
          .Append(".meta{color:#6b7280;font-size:12px;margin:0 0 12px;text-align:center;}")
          .Append(".form{margin-bottom:28px;}")
          .Append("table{border-collapse:collapse;width:100%;}")
          .Append("th,td{border:1px solid #9ca3af;padding:4px 6px;text-align:left;word-break:break-all;}")
          .Append("th{background:#f3f4f6;font-weight:600;white-space:nowrap;}")
          .Append("dl{margin:0;}dt{font-weight:600;color:#374151;margin-top:8px;}")
          .Append("dd{margin:0 0 4px 0;color:#1f2937;}")
          .Append(".empty{color:#9ca3af;text-align:center;padding:24px 0;}")
          .Append("</style></head><body>");

        if (sections.Count == 0)
        {
            sb.Append("<div class=\"empty\">无可预览数据</div>");
        }

        foreach (var s in sections)
        {
            sb.Append("<div class=\"form\">");
            sb.Append("<h2>").Append(Escape(s.Title)).Append("</h2>");
            if (!string.IsNullOrWhiteSpace(s.Unit) || !string.IsNullOrWhiteSpace(s.Date))
            {
                sb.Append("<p class=\"meta\">").Append(Escape(s.Unit));
                if (!string.IsNullOrWhiteSpace(s.Unit) && !string.IsNullOrWhiteSpace(s.Date))
                    sb.Append('　');
                sb.Append(Escape(s.Date)).Append("</p>");
            }

            if (s.Rows.Count > 0 && s.Columns.Count > 0)
            {
                sb.Append("<table><thead><tr>");
                foreach (var c in s.Columns)
                    sb.Append("<th>").Append(Escape(c.Header)).Append("</th>");
                sb.Append("</tr></thead><tbody>");
                foreach (var row in s.Rows)
                {
                    sb.Append("<tr>");
                    foreach (var c in s.Columns)
                    {
                        row.TryGetValue(c.FieldKey, out var v);
                        sb.Append("<td>").Append(Escape(v)).Append("</td>");
                    }
                    sb.Append("</tr>");
                }
                sb.Append("</tbody></table>");
            }
            else if (s.Paragraphs.Count > 0)
            {
                sb.Append("<dl>");
                foreach (var (label, value) in s.Paragraphs)
                    sb.Append("<dt>").Append(Escape(label)).Append("</dt><dd>").Append(Escape(value)).Append("</dd>");
                sb.Append("</dl>");
            }
            else
            {
                sb.Append("<div class=\"empty\">本表单无数据</div>");
            }

            sb.Append("</div>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string Escape(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
