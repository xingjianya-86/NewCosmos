using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 收入核算 partial（单屏合并版，无 Tab）。
/// 【收入只取三个有数据来源的项】（无台账来源的务工/经营/财产/转移/其他/刚性支出已按需求删除）：
///   ① 子女赡养费：每子女月费默认 = 低保标准 × IncomeTypeConstants.DEFAULT_SUPPORT_FEE_RATIO(20%)，
///      读取机制与申请表完全一致（IStandardConfigService 按户口取档 + 常量比例，失败由 RunAll 显式中止，不回退硬编码）；
///      年值走 Supporter 权威公式（月费×月数×申请表家庭人口×子女数，CalculateAlimonyAnnual 单点）。
///   ② 土地三用途：自种/转包/承包面积 × IncomeTypeConstants.LandUnitPrice 单价，
///      CalculateLandIncome 权威公式；保留「全自种/全转包/全承包」批量分配按钮与「从核验带入」。
///   ③ 种植补贴年值：从核验台账带入（与「导入农业补贴」同口径）或手填。
/// 【不依赖历史档案】对象可为从未申请过的人；全程只读不写库。
/// </summary>
public partial class QuickVerifyViewModel
{
    #region 输入

    [ObservableProperty]
    private string _simName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImportFromVerify))]
    private string _simIdCard = string.Empty;

    /// <summary>身份证填对即可点「从核验带入」——点击时若尚未核验会自动先核验（无需先点开始核算）</summary>
    public bool CanImportFromVerify => IdCardValidator.IsValid((SimIdCard ?? string.Empty).Trim());

    /// <summary>家庭人口（不含户主），判定人口 = 本值 + 1</summary>
    [ObservableProperty]
    private string _simFamilySizeText = string.Empty;

    [ObservableProperty]
    private string _simChildrenText = string.Empty;

    /// <summary>每子女月赡养费：留空 = 低保标准 × 20%（核算时自动回显实际采用值）</summary>
    [ObservableProperty]
    private string _simPerChildFeeText = string.Empty;

    /// <summary>户口性质下拉（决定低保标准档；与申请表 HukouTypeOptions 同款）</summary>
    public List<DictItemOption> HukouTypeOptions { get; } = new()
    {
        new DictItemOption { Key = ClassificationConstants.HukouType.RURAL, Display = "农村户口" },
        new DictItemOption { Key = ClassificationConstants.HukouType.URBAN, Display = "城市户口" }
    };

    [ObservableProperty]
    private DictItemOption? _selectedHukouType;

    // ── 土地三用途面积（亩；留空按 0）──
    [ObservableProperty]
    private string _landSelfAreaText = string.Empty;

    [ObservableProperty]
    private string _landSubleaseAreaText = string.Empty;

    [ObservableProperty]
    private string _landContractAreaText = string.Empty;

    /// <summary>单价展示（常量同源，只读；申请表同款默认值机制）</summary>
    public decimal SelfFarmUnitPrice => IncomeTypeConstants.LandUnitPrice.SELF_FARM;
    public decimal SubleaseUnitPrice => IncomeTypeConstants.LandUnitPrice.SUBLEASE;
    public decimal ContractUnitPrice => IncomeTypeConstants.LandUnitPrice.CONTRACT;

    [ObservableProperty]
    private string _subsidyAnnualText = string.Empty;

    #endregion

    #region 收入核算结果

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIncomeHint))]
    [NotifyPropertyChangedFor(nameof(CanExportReport))]
    private bool _hasIncomeResult;

    /// <summary>尚未核算时的引导提示</summary>
    public bool ShowIncomeHint => !HasIncomeResult;

    [ObservableProperty]
    private ObservableCollection<SimLineView> _incomeLines = new();

    [ObservableProperty]
    private string _annualIncomeText = string.Empty;

    [ObservableProperty]
    private string _perCapitaAnnualText = string.Empty;

    [ObservableProperty]
    private string _perCapitaMonthlyText = string.Empty;

    [ObservableProperty]
    private string _incomeHukouText = string.Empty;

    [ObservableProperty]
    private string _standardText = string.Empty;

    [ObservableProperty]
    private string _edgeText = string.Empty;

    /// <summary>收入区间：Low=低保区间 / Edge=边缘区间 / Over=超标区间</summary>
    [ObservableProperty]
    private string _zoneKind = string.Empty;

    [ObservableProperty]
    private string _zoneText = string.Empty;

    #endregion

    #region 从核验带入

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLandImportHint))]
    private string _landImportHint = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubsidyImportHint))]
    private string _subsidyImportHint = string.Empty;

    public bool HasLandImportHint => !string.IsNullOrEmpty(LandImportHint);
    public bool HasSubsidyImportHint => !string.IsNullOrEmpty(SubsidyImportHint);

    /// <summary>最近一次带入的台账总面积（「全自种/全转包/全承包」优先按它重新分配）</summary>
    private decimal _lastImportedLandTotal;

    #endregion

    #region 校验与核算

    /// <summary>校验输入；返回 null = 通过，否则返回用户可读错误文案</summary>
    private string? ValidateIncomeInputs()
    {
        if (string.IsNullOrWhiteSpace(SimName))
            return "请输入姓名";
        if (!IdCardValidator.IsValid((SimIdCard ?? string.Empty).Trim()))
            return "请输入正确的 18 位身份证号";
        if (SelectedHukouType == null)
            return "请选择户口性质（决定低保标准档）";
        if (!TryParseCount(SimFamilySizeText, out var _) )
            return "家庭人口（不含户主）请输入 0 或正整数";
        if (!TryParseCount(SimChildrenText, out var _))
            return "子女数量请输入 0 或正整数";

        foreach (var (label, text) in new[]
                 {
                     ("自种面积", LandSelfAreaText), ("转包面积", LandSubleaseAreaText),
                     ("承包面积", LandContractAreaText), ("补贴年值", SubsidyAnnualText)
                 })
        {
            if (!string.IsNullOrWhiteSpace(text) &&
                (!decimal.TryParse(text.Trim(), out var v) || v < 0))
                return $"{label}格式不正确";
        }

        var feeText = (SimPerChildFeeText ?? string.Empty).Trim();
        if (feeText.Length > 0 && (!decimal.TryParse(feeText, out var fee) || fee < 0))
            return "每子女月赡养费格式不正确";

        return null;
    }

    private static bool TryParseCount(string? text, out int value)
    {
        value = 0;
        if (!int.TryParse((text ?? string.Empty).Trim(), out value) || value < 0)
        {
            value = 0;
            return false;
        }
        return true;
    }

    private static decimal ParseAreaOrZero(string? text)
        => decimal.TryParse((text ?? string.Empty).Trim(), out var v) && v >= 0 ? v : 0m;

    /// <summary>
    /// 收入核算（同步；标准由 RunAll 读取后传入，缺失已在 RunAll 显式中止）。
    /// 年总收入仍走 IIncomeCalculationService 权威单点（月项全传 0），保证一次舍入口径不重抄。
    /// </summary>
    private void ComputeIncome(decimal subsistenceStandard)
    {
        var familySize = TryParseCount(SimFamilySizeText, out var excl) ? excl + 1 : 1;
        var children = TryParseCount(SimChildrenText, out var c) ? c : 0;

        // ── 每子女月费：留空 = 低保标准 × 20%（与申请表 GetCachedSubsistenceStandardAsync 同机制）──
        decimal fee;
        var feeText = (SimPerChildFeeText ?? string.Empty).Trim();
        if (feeText.Length > 0 && decimal.TryParse(feeText, out var entered) && entered >= 0)
        {
            fee = entered;
        }
        else
        {
            fee = Math.Round(subsistenceStandard * IncomeTypeConstants.DEFAULT_SUPPORT_FEE_RATIO, 2);
        }
        SimPerChildFeeText = fee.ToString("0.##");   // 回显实际采用值

        // ── 赡养年值：Supporter 实体权威公式（月费×月数×申请表家庭人口），每子女一行 ──
        var supporters = new List<Supporter>();
        for (var i = 0; i < children; i++)
        {
            supporters.Add(new Supporter
            {
                MonthlySupportFee = fee,
                SupportMonths = 12,
                CalcFamilySize = familySize   // 2026-11-12 口径：年费乘数 = 申请表家庭人口
            });
        }
        var alimonyAnnual = _incomeCalculationService.CalculateAlimonyAnnual(supporters);

        // ── 土地年值：三用途面积 × 单价（权威单点）──
        var selfArea = ParseAreaOrZero(LandSelfAreaText);
        var subleaseArea = ParseAreaOrZero(LandSubleaseAreaText);
        var contractArea = ParseAreaOrZero(LandContractAreaText);
        var landAnnual = _incomeCalculationService.CalculateLandIncome(
            selfArea, (double)SelfFarmUnitPrice,
            subleaseArea, (double)SubleaseUnitPrice,
            contractArea, (double)ContractUnitPrice);

        // ── 补贴年值 ──
        var subsidyAnnual = ParseAreaOrZero(SubsidyAnnualText);

        // ── 年家庭总收入（月项与刚性支出均为 0，仍走权威服务保证口径与舍入一致）──
        var annual = _incomeCalculationService.CalculateAnnualFamilyIncome(
            0, 0, 0, 0, 0, alimonyAnnual, landAnnual, subsidyAnnual, 0);
        var perCapitaAnnual = _incomeCalculationService.CalculatePerCapitaAnnual(annual, familySize);
        var perCapitaMonthly = _incomeCalculationService.PerCapitaMonthly(annual, familySize);

        // ── 标准与区间（口径与 ClassificationService 三层判定一致：超标 ≥ 边缘 > 低保）──
        var isRural = SelectedHukouType?.Key == ClassificationConstants.HukouType.RURAL;
        var edgeThreshold = subsistenceStandard * ClassificationConstants.LowIncomeMultiplier;
        var zoneKind = perCapitaMonthly >= edgeThreshold ? "Over"
            : perCapitaMonthly >= subsistenceStandard ? "Edge"
            : "Low";

        // ── 展示 ──
        IncomeLines = new ObservableCollection<SimLineView>(new[]
        {
            new SimLineView(
                $"赡养费年值（{children} 子女 × {fee:N2} 元/月 × 12 × {familySize} 人）",
                $"{alimonyAnnual:N2}"),
            new SimLineView(
                $"土地（自种 {selfArea:0.##} 亩×{SelfFarmUnitPrice:0} + 转包 {subleaseArea:0.##} 亩×{SubleaseUnitPrice:0} + 承包 {contractArea:0.##} 亩×{ContractUnitPrice:0}）",
                $"{landAnnual:N2}"),
            new SimLineView("种植补贴年值", $"{subsidyAnnual:N2}"),
            new SimLineView("══ 年家庭总收入", $"{annual:N2}")
        });

        AnnualIncomeText = $"{annual:N2} 元/年";
        PerCapitaAnnualText = $"{perCapitaAnnual:N2} 元/年";
        PerCapitaMonthlyText = $"{perCapitaMonthly:N2} 元/月";
        IncomeHukouText = isRural == true ? "农村" : "城镇";
        StandardText = $"{subsistenceStandard:N2} 元/月";
        EdgeText = $"{edgeThreshold:N2} 元/月";
        ZoneKind = zoneKind;
        ZoneText = zoneKind switch
        {
            "Low" => $"月人均 {perCapitaMonthly:N2} 元低于低保标准 → 低保区间",
            "Edge" => $"月人均 {perCapitaMonthly:N2} 元在低保~边缘标准之间 → 边缘区间",
            _ => $"月人均 {perCapitaMonthly:N2} 元超出边缘标准上限 → 超标区间"
        };

        HasIncomeResult = true;
    }

    #endregion

    #region 从核验带入 / 土地批量分配

    /// <summary>土地带入：先确保已核验（未核验/证号姓名变了会自动先跑核验），再优先取承包台账合计，
    /// 其次历史确权合计；默认全部计入「自种」格</summary>
    [RelayCommand]
    private async Task ImportLandFromVerifyAsync()
    {
        var idCard = await EnsureVerifiedForImportAsync();
        if (idCard == null) return;

        var bundle = _lastBundle!;
        var total = bundle.ContractLand.Items.Sum(r => r.LandArea);
        var source = "承包台账";
        if (total <= 0)
        {
            total = bundle.HistoryLand.Items.Sum(r => r.LandArea);
            source = "历史确权";
        }
        if (total <= 0)
        {
            LandImportHint = "核验未查到可带入的土地数据";
            return;
        }

        LandSelfAreaText = total.ToString("0.##");
        LandSubleaseAreaText = "0";
        LandContractAreaText = "0";
        _lastImportedLandTotal = total;
        LandImportHint = $"已带入 {total:F2} 亩（来自{source}，默认计入自种，可用下方按钮重新分配）";
        RecomputeIncomeIfResultExists();   // 已有核算结果 → 立即回算
    }

    /// <summary>补贴带入：先确保已核验；金额型取金额、面积型按分类单价折算（与「导入农业补贴」同口径）</summary>
    [RelayCommand]
    private async Task ImportSubsidyFromVerifyAsync()
    {
        var idCard = await EnsureVerifiedForImportAsync();
        if (idCard == null) return;

        var items = _lastBundle!.Subsidies.Items;
        if (items.Count == 0)
        {
            SubsidyImportHint = "核验未查到可带入的补贴数据";
            return;
        }

        decimal sum = 0;
        foreach (var r in items)
        {
            if (r.Amount > 0)
                sum += r.Amount;
            else if (r.Area > 0)
                sum += Math.Round(r.Area * SubsidyPriceConstants.GetPrice(r.SubsidyType), 2);
        }

        SubsidyAnnualText = sum.ToString("0.##");
        SubsidyImportHint = $"已带入 {sum:N2} 元（{items.Count} 条，金额型+面积型折算）";
        RecomputeIncomeIfResultExists();   // 已有核算结果 → 立即回算
    }

    /// <summary>
    /// 土地用途批量分配（全自种/全转包/全承包）：优先按最近带入的台账总面积分配，
    /// 未带入时取三格当前合计；与申请表 Step3「批量设置土地用途」同一交互。
    /// </summary>
    [RelayCommand]
    private void BatchSetLand(string? usage)
    {
        var total = _lastImportedLandTotal > 0
            ? _lastImportedLandTotal
            : ParseAreaOrZero(LandSelfAreaText) + ParseAreaOrZero(LandSubleaseAreaText) + ParseAreaOrZero(LandContractAreaText);
        if (total <= 0)
        {
            LandImportHint = "请先带入或填写土地面积";
            return;
        }

        var fmt = total.ToString("0.##");
        string label;
        switch (usage)
        {
            case "sublease":
                LandSelfAreaText = "0";
                LandSubleaseAreaText = fmt;
                LandContractAreaText = "0";
                label = "转包";
                break;
            case "contract":
                LandSelfAreaText = "0";
                LandSubleaseAreaText = "0";
                LandContractAreaText = fmt;
                label = "承包";
                break;
            default:
                LandSelfAreaText = fmt;
                LandSubleaseAreaText = "0";
                LandContractAreaText = "0";
                label = "自种";
                break;
        }
        LandImportHint = $"已将 {total:F2} 亩全部计入{label}";
        RecomputeIncomeIfResultExists();   // 已有核算结果 → 立即回算
    }

    #endregion
}

/// <summary>收入明细行（标签 + 数值）</summary>
public class SimLineView
{
    public SimLineView(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }
    public string Value { get; }
}
