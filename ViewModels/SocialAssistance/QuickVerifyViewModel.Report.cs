using CommunityToolkit.Mvvm.Input;
using NewCosmos.Helpers;
using NewCosmos.Services.Core;
using System.Text;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 简易报告 partial：把收入核算 + 台账核验结果拼成纯文本，一键复制到剪贴板（粘贴到微信/Word/台账）。
/// 【同源】收入段复用 <see cref="IncomeLines"/>、核验段复用分组视图（与界面所见完全一致，不重写格式）；
/// 【口径】完整版三段（被核算人/收入核算/台账核验），身份证完整显示（内部核对用，2026-11-15 拍板）；
/// 被隐藏的零命中分组在末尾"无记录：…"一行带过，失败组写明原因。
/// 【审计】LogBusiness 只记动作与脱敏身份证，不记报告全文（§10）。
/// </summary>
public partial class QuickVerifyViewModel
{
    /// <summary>有任一结果（核算或核验）即可导出</summary>
    public bool CanExportReport => HasIncomeResult || HasVerifyResult;

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        if (!CanExportReport)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先「开始核算」或触发核验后再导出", "确定");
            return;
        }

        try
        {
            var text = BuildReportText();
            // 库内现成写法（LotteryPredictionViewModel / ArchiveProductionViewModel 同款）
            await Clipboard.SetTextAsync(text);

            _logger.LogBusiness("导出简易报告",
                ("IdCard", DataMasker.MaskIdCard((SimIdCard ?? string.Empty).Trim())),
                ("Length", text.Length));

            await _dialogService.DisplayAlertAsync("已导出",
                $"报告文本已复制到剪贴板（{text.Length} 字），可直接粘贴到微信/Word/台账。", "确定");
        }
        catch (Exception ex)
        {
            _logger.Error($"导出报告失败: {ex.Message}");
            await _dialogService.DisplayAlertAsync("导出失败", ex.Message, "确定");
        }
    }

    /// <summary>生成报告文本（StringBuilder.AppendLine 按平台换行，保证粘贴到 Windows 目标不串行）</summary>
    private string BuildReportText()
    {
        var sb = new StringBuilder();

        sb.AppendLine("══════ 家庭收入核算报告 ══════");
        sb.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm}");

        // ── 一、被核算人 ──
        var familySize = int.TryParse((SimFamilySizeText ?? string.Empty).Trim(), out var excl) ? excl + 1 : 1;
        var children = int.TryParse((SimChildrenText ?? string.Empty).Trim(), out var c) ? c : 0;
        sb.AppendLine();
        sb.AppendLine("一、被核算人");
        sb.AppendLine($"姓名：{SimName}");
        sb.AppendLine($"身份证号：{(SimIdCard ?? string.Empty).Trim().ToUpperInvariant()}");
        sb.AppendLine($"户口性质：{IncomeHukouText} ｜ 家庭人口：{familySize} 人（含户主）｜ 子女数量：{children} 人");

        // ── 二、收入核算 ──
        sb.AppendLine();
        sb.AppendLine("二、收入核算");
        if (HasIncomeResult)
        {
            foreach (var line in IncomeLines)
                sb.AppendLine($"{line.Label}：{line.Value}");

            sb.AppendLine($"年人均 {PerCapitaAnnualText} ｜ 月人均 {PerCapitaMonthlyText}");
            sb.AppendLine($"低保标准 {StandardText} ｜ 边缘标准上限 {EdgeText}");
            sb.AppendLine($"区间结论：{ZoneText}");
        }
        else
        {
            sb.AppendLine("（未核算——本次仅执行了台账核验）");
        }

        // ── 三、台账核验 ──
        sb.AppendLine();
        sb.AppendLine("三、台账核验（只读）");
        if (_allGroupViews.Count == 0)
        {
            sb.AppendLine("（未核验）");
        }
        else
        {
            var emptyTitles = new List<string>();
            foreach (var g in _allGroupViews)
            {
                if (g.IsFailure)
                {
                    sb.AppendLine($"▶ {g.Title}：查询失败 - {g.FailureText}");
                    continue;
                }
                if (!g.HasItems)
                {
                    emptyTitles.Add(StripIcon(g.Title));
                    continue;
                }

                var header = string.IsNullOrEmpty(g.MatchNote)
                    ? g.Title
                    : $"{g.Title}（{g.MatchNote}）";
                var countText = string.IsNullOrEmpty(g.CountText) ? string.Empty : $" {g.CountText}";
                sb.AppendLine($"▶ {header}{countText}：");

                var i = 0;
                foreach (var item in g.Items)
                {
                    i++;
                    var parts = new List<string> { item.Title };
                    if (item.HasDetail) parts.Add(item.Detail);
                    if (item.HasValue) parts.Add(item.Value);
                    if (item.HasBadge) parts.Add($"【{item.Badge}】");
                    sb.AppendLine($"   {i}. {string.Join("｜", parts)}");
                }
            }

            if (emptyTitles.Count > 0)
                sb.AppendLine($"无记录：{string.Join("、", emptyTitles)}");
        }

        sb.AppendLine("════════════════════════════");
        return sb.ToString();
    }

    /// <summary>去掉标题前的 emoji 前缀，让"无记录：…"名单更干净</summary>
    private static string StripIcon(string title)
    {
        var idx = title.IndexOf(' ');
        return idx > 0 && idx <= 5 ? title[(idx + 1)..] : title;
    }
}
