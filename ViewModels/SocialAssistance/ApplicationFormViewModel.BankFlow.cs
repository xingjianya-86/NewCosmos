using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.BankFlow;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 申请表单 - 银行流水核对带入（Step3 财产信息区）。
///
/// 「从核对数据带入」按当前家庭成员身份证从最新核对批次读取数据：
///   - 命中成员的合计余额汇总 → 金融资产-银行存款（FinancialAsset.BankDepositAmount）；
///   - 成员流水明细（合计余额 / 12月最高流水 / 各银行摘要）展示在下方；
///   - 超阈值 = 家庭合计余额 &gt; 月低保标准 × 30 × 家庭人数，仅红字提示，不自动决策（权威判定仍走分类流程）。
/// 金融资产按户存储不动（FinancialAssets 集合），核对明细按人留在核对表。
/// </summary>
public partial class ApplicationFormViewModel
{
    /// <summary>核对数据服务（懒解析：构造函数参数已达 20+，经 _serviceProvider DI 解析，不 new）</summary>
    private IBankFlowQueryService? _bankFlowQueryServiceInstance;

    private IBankFlowQueryService BankFlowQuery =>
        _bankFlowQueryServiceInstance ??= _serviceProvider.GetRequiredService<IBankFlowQueryService>();

    /// <summary>核对带入结果摘要（命中人数、批次、合计余额、12月最高流水）</summary>
    [ObservableProperty]
    private string _bankFlowSummary = string.Empty;

    /// <summary>超阈值红字提示（空=未超阈值或未带入；仅提示，不自动决策）</summary>
    [ObservableProperty]
    private string _bankFlowExceedHint = string.Empty;

    /// <summary>是否已带入核对数据（决定明细区显隐）</summary>
    [ObservableProperty]
    private bool _hasBankFlowData;

    /// <summary>成员核对流水明细（含未命中成员，未命中明确标注）</summary>
    [ObservableProperty]
    private ObservableCollection<BankFlowMemberRow> _bankFlowMemberRows = new();

    /// <summary>核对数据中是否存在 60 马力以上农机（仅提示，不自动决策）</summary>
    [ObservableProperty]
    private bool _hasBankFlowMachineryExceed;

    /// <summary>
    /// 从核对数据带入：按申请人+家庭成员身份证匹配最新核对批次，汇总填入银行存款并展示成员流水明细。
    /// </summary>
    [RelayCommand]
    private async Task ImportBankFlowAsync()
    {
        if (IsBusy) return;

        var idCards = CollectFamilyIdCards();
        if (idCards.Count == 0)
        {
            await ShowBankFlowMessageAsync("请先填写申请人身份证号或添加家庭成员");
            return;
        }

        IsBusy = true;
        try
        {
            var result = await BankFlowQuery.GetByIdCardsAsync(idCards, CancellationToken);
            if (result.IsFailure)
            {
                await ShowBankFlowMessageAsync($"核对数据查询失败: {result.Message}");
                _logger.Warn($"银行流水核对带入查询失败: {result.Message}");
                return;
            }

            var data = result.Value ?? new Dictionary<string, BankFlowPersonData>();
            if (data.Count == 0)
            {
                HasBankFlowData = false;
                BankFlowMemberRows.Clear();
                BankFlowSummary = string.Empty;
                BankFlowExceedHint = string.Empty;
                HasBankFlowMachineryExceed = false;
                await ShowBankFlowMessageAsync(
                    "最新核对批次中未找到当前家庭成员的身份证数据，请先在「数据中心 → 数据导入」导入核对表");
                return;
            }

            // 明细行：申请人在前，其余成员按录入顺序；命中/未命中都展示
            var rows = new List<BankFlowMemberRow>();
            decimal totalBalance = 0, maxFlow = 0;
            foreach (var idCard in idCards)
            {
                var key = idCard.Trim().ToUpperInvariant();
                if (data.TryGetValue(key, out var person))
                {
                    totalBalance += person.Record.TotalBalance;
                    if (person.Record.MaxFlowAmount > maxFlow) maxFlow = person.Record.MaxFlowAmount;
                    rows.Add(new BankFlowMemberRow
                    {
                        Name = person.Record.PersonName,
                        IdCard = person.Record.IdCard,
                        Matched = true,
                        TotalBalanceText = person.Record.TotalBalanceDisplay,
                        MaxFlowText = person.Record.MaxFlowAmountDisplay,
                        BankSummary = person.Record.BankSummary,
                        Exceed = person.Record.IsExceed,
                        MachineryExceed = person.Record.IsMachineryExceed
                    });
                }
                else
                {
                    var member = FindMemberByIdCard(idCard);
                    rows.Add(new BankFlowMemberRow
                    {
                        Name = member?.Name ?? (idCard == ApplicantIdCard?.Trim() ? "申请人" : ""),
                        IdCard = idCard,
                        Matched = false,
                        TotalBalanceText = "—",
                        MaxFlowText = "—",
                        BankSummary = "核对数据中无此身份证"
                    });
                }
            }

            // 合计余额 → 金融资产-银行存款（按户存储：确保集合有载体行）
            EnsureFinancialAssetRow();
            FinancialAsset.HasBankDeposit = totalBalance > 0;
            FinancialAsset.BankDepositAmount = totalBalance;
            OnPropertyChanged(nameof(FinancialAsset));

            var batchNo = data.Values.First().BatchNo;
            var matchedCount = rows.Count(r => r.Matched);
            BankFlowMemberRows = new ObservableCollection<BankFlowMemberRow>(rows);
            HasBankFlowData = true;
            BankFlowSummary =
                $"批次 {batchNo}：命中 {matchedCount}/{idCards.Count} 人，" +
                $"合计余额 {totalBalance:N2} 元，12月最高流水 {maxFlow:N2} 元" +
                (matchedCount < idCards.Count ? $"（{idCards.Count - matchedCount} 人未在核对数据中，已在明细标注）" : "");

            // 超阈值红字提示（30 倍数 Constants 单点；标准读标准配置，缺失则不判定不误导）
            BankFlowExceedHint = await BuildExceedHintAsync(totalBalance, idCards.Count);
            HasBankFlowMachineryExceed = rows.Any(r => r.MachineryExceed);

            _logger.LogBusiness("银行流水核对带入完成",
                ("Matched", matchedCount),
                ("Total", idCards.Count),
                ("BatchNo", batchNo),
                ("TotalBalance", totalBalance));
        }
        catch (OperationCanceledException)
        {
            // 页面离开导致查询取消，不弹错误
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "银行流水核对带入失败");
            await ShowBankFlowMessageAsync($"核对数据带入失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>超阈值提示：家庭合计 &gt; 月低保标准 × 30 × 家庭人数（仅红字提示，不自动决策）。</summary>
    private async Task<string> BuildExceedHintAsync(decimal totalBalance, int idCardCount)
    {
        try
        {
            bool isRural = HukouType?.Key != "Urban";
            var standardType = isRural ? "RuralSubsistenceStandard" : "UrbanSubsistenceStandard";
            var stdResult = await _standardConfigService.GetStandardValueAsync(standardType, ct: CancellationToken);
            if (!stdResult.IsSuccess || stdResult.Value <= 0)
            {
                _logger.Warn($"低保标准读取失败，跳过超阈值提示：{stdResult.Message}");
                return string.Empty;
            }

            // 家庭人数以已录入成员为准（至少 1 人 = 申请人）
            var familySize = Math.Max(1, Math.Max(FamilyMembers.Count, idCardCount));
            var threshold = stdResult.Value * BankFlowConstants.BalanceLimitMonths * familySize;

            if (totalBalance <= threshold) return string.Empty;

            return string.Format(BankFlowConstants.BalanceExceedFormat,
                totalBalance.ToString("N2"), familySize, threshold.ToString("N2"));
        }
        catch (Exception ex)
        {
            _logger.Warn($"超阈值提示计算异常（不影响带入）: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>收集待匹配身份证：申请人在前，其余家庭成员去重去空。</summary>
    private List<string> CollectFamilyIdCards()
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var applicant = ApplicantIdCard?.Trim();
        if (!string.IsNullOrEmpty(applicant) && seen.Add(applicant))
            list.Add(applicant);

        foreach (var member in FamilyMembers)
        {
            var idCard = member.IdCard?.Trim();
            if (!string.IsNullOrEmpty(idCard) && seen.Add(idCard))
                list.Add(idCard);
        }
        return list;
    }

    private FamilyMember? FindMemberByIdCard(string idCard)
        => FamilyMembers.FirstOrDefault(m => string.Equals(m.IdCard?.Trim(), idCard.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 保证 FinancialAsset 有集合载体：加载后两者同引用（改动即入集合）；新建户为空集合时补一行。
    /// </summary>
    private void EnsureFinancialAssetRow()
    {
        if (FinancialAssets.Count == 0)
        {
            FinancialAssets.Add(FinancialAsset);
        }
        else if (!ReferenceEquals(FinancialAssets[0], FinancialAsset))
        {
            // 集合与单对象脱节（如重载后未同步）：以集合首行为准，保持保存链路一致
            FinancialAsset = FinancialAssets[0];
        }
    }

    private async Task ShowBankFlowMessageAsync(string message)
    {
        var dialog = _serviceProvider.GetService<IDialogService>();
        if (dialog != null)
            await dialog.ShowSnackBarAsync(message, Components.SnackBarType.Warning);
    }
}

/// <summary>成员核对流水明细行（Step3 展示用，不落库）</summary>
public class BankFlowMemberRow
{
    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public bool Matched { get; set; }
    public string TotalBalanceText { get; set; } = "—";
    public string MaxFlowText { get; set; } = "—";
    public string BankSummary { get; set; } = string.Empty;
    public bool Exceed { get; set; }
    public bool MachineryExceed { get; set; }
}
