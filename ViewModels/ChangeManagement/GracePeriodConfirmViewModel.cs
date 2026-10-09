using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;

namespace NewCosmos.ViewModels.ChangeManagement;

/// <summary>渐退期确认页参数（Step5 分类判定结果快照，确认页只读展示 + 可选月数）</summary>
public sealed record GracePeriodConfirmParameter(
    string OriginalClassification,
    string NewClassification,
    decimal SubsistenceStandard,
    decimal PerCapitaMonthly,
    decimal TotalAnnualIncome,
    int FamilySize,
    decimal GuaranteeAmount,
    int Months,
    DateTime StartDate,
    DateTime EndDate,
    decimal? OriginalClassifiedAmount = null,
    decimal? NewClassifiedAmount = null);

/// <summary>渐退期确认结果：确认 => Confirmed=true 并携带所选月数；取消 => Confirmed=false</summary>
public sealed record GracePeriodConfirmResult(bool Confirmed, int SelectedMonths);

/// <summary>
/// 渐退期确认弹窗 ViewModel（桌面/手机共用）：
/// 结果采用 TaskCompletionSource 模式（与 MemberChangeReasonPopup 一致），弹窗由调用方 Push/Pop。
/// 不继承 ViewModelBase（审计豁免）：纯 TCS 结果弹窗，无服务调用/异步 IO，IsBusy/ExecuteAsync 无用武之地。
/// </summary>
public partial class GracePeriodConfirmViewModel : ObservableObject
{
    private readonly TaskCompletionSource<GracePeriodConfirmResult> _tcs = new();
    private DateTime _startDate;

    /// <summary>弹窗结果：确认 => Confirmed=true + SelectedMonths；取消 => Confirmed=false</summary>
    public Task<GracePeriodConfirmResult> Result => _tcs.Task;

    /// <summary>可选渐退期月数（MIN_MONTHS–MAX_MONTHS）</summary>
    public ObservableCollection<int> MonthOptions { get; } = new(
        Enumerable.Range(GracePeriodConstants.MIN_MONTHS,
            GracePeriodConstants.MAX_MONTHS - GracePeriodConstants.MIN_MONTHS + 1));

    [ObservableProperty]
    private string _title = "渐退期确认";

    [ObservableProperty]
    private string _subtitle = "分类判定命中渐退资格，请确认是否进入渐退期";

    [ObservableProperty]
    private string _originalClassification = string.Empty;

    [ObservableProperty]
    private string _newClassification = string.Empty;

    [ObservableProperty]
    private string _subsistenceStandardText = string.Empty;

    [ObservableProperty]
    private string _perCapitaMonthlyText = string.Empty;

    [ObservableProperty]
    private string _totalAnnualIncomeText = string.Empty;

    [ObservableProperty]
    private string _familySizeText = string.Empty;

    [ObservableProperty]
    private string _guaranteeAmountText = string.Empty;

    [ObservableProperty]
    private string _periodText = string.Empty;

    [ObservableProperty]
    private string _hintText = string.Empty;

    /// <summary>分类施保变化行是否显示（链档案原/现分类施保有差异时）</summary>
    [ObservableProperty]
    private bool _isClassifiedChangeVisible;

    /// <summary>分类施保变化行文本，如 "208.00 → 104.00 元/月（减发 104.00 元）"</summary>
    [ObservableProperty]
    private string _classifiedChangeText = string.Empty;

    /// <summary>所选渐退期月数（Picker，默认判定结果月数/6）</summary>
    [ObservableProperty]
    private int _selectedMonth = GracePeriodConstants.DEFAULT_MONTHS;

    partial void OnSelectedMonthChanged(int value)
    {
        if (_startDate == default) return;
        var months = Math.Clamp(value, GracePeriodConstants.MIN_MONTHS, GracePeriodConstants.MAX_MONTHS);
        var end = _startDate.AddMonths(months).AddDays(-1);
        PeriodText = $"{_startDate:yyyy-MM-dd} 至 {end:yyyy-MM-dd}（{months} 个月）";
    }

    /// <summary>按 Step5 判定快照初始化（确认后由调用方应用所选月数并保存）</summary>
    public void Initialize(GracePeriodConfirmParameter parameter)
    {
        OriginalClassification = string.IsNullOrWhiteSpace(parameter.OriginalClassification)
            ? "（无）"
            : parameter.OriginalClassification;
        NewClassification = string.IsNullOrWhiteSpace(parameter.NewClassification)
            ? "（无）"
            : parameter.NewClassification;
        SubsistenceStandardText = parameter.SubsistenceStandard.ToString("N2");
        PerCapitaMonthlyText = parameter.PerCapitaMonthly.ToString("N2");
        TotalAnnualIncomeText = parameter.TotalAnnualIncome.ToString("N2");
        FamilySizeText = parameter.FamilySize.ToString();
        GuaranteeAmountText = parameter.GuaranteeAmount.ToString("N2");

        _startDate = parameter.StartDate;
        var months = GracePeriodConstants.IsValidMonths(parameter.Months)
            ? parameter.Months
            : GracePeriodConstants.DEFAULT_MONTHS;
        // 先设 startDate 再选月数，触发 OnSelectedMonthChanged 重算区间
        SelectedMonth = months;
        var end = parameter.StartDate.AddMonths(months).AddDays(-1);
        PeriodText = $"{parameter.StartDate:yyyy-MM-dd} 至 {end:yyyy-MM-dd}（{months} 个月）";

        HintText = "确认后本次分类判定将写入渐退期状态并在保存时激活渐退记录；取消则不应用本次渐退，可返回修改数据后重新判定。";

        // 分类施保变化行：仅链档案（原/现均有值且不同）显示——户主死亡进入渐退期减去死者份额场景
        if (parameter.OriginalClassifiedAmount is decimal oc && parameter.NewClassifiedAmount is decimal nc && oc != nc)
        {
            var diff = oc - nc;
            IsClassifiedChangeVisible = true;
            ClassifiedChangeText = diff > 0
                ? $"{oc:N2} → {nc:N2} 元/月（减发 {diff:N2} 元）"
                : $"{oc:N2} → {nc:N2} 元/月（增发 {-diff:N2} 元）";
        }
        else
        {
            IsClassifiedChangeVisible = false;
            ClassifiedChangeText = string.Empty;
        }
    }

    /// <summary>确认（回传所选月数）</summary>
    [RelayCommand]
    private void Confirm()
    {
        var months = Math.Clamp(SelectedMonth, GracePeriodConstants.MIN_MONTHS, GracePeriodConstants.MAX_MONTHS);
        _tcs.TrySetResult(new GracePeriodConfirmResult(true, months));
    }

    /// <summary>取消</summary>
    [RelayCommand]
    private void Cancel() => _tcs.TrySetResult(new GracePeriodConfirmResult(false, 0));
}
