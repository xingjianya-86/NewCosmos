using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.System;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 快速核验 · 模拟测算 页 ViewModel（单屏合并版，低收入模块首页功能卡入口）。
/// 一次输入（人信息 + 三个有数据来源的收入项）→「开始核算」并行执行：
///   ① 收入核算（见 .Income.cs：赡养费默认=低保标准×20%、土地三用途、补贴 → 权威年值口径 → 标准区间）
///   ② 台账核验 10 组（IQuickVerifyService，只读，供佐证与「从核验带入」）
/// 不依赖历史档案（对象可为从未申请过的人）；全程只读不写库。
/// </summary>
public partial class QuickVerifyViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerService _logger;
    private readonly IDialogService _dialogService;
    private readonly IQuickVerifyService _quickVerifyService;
    private readonly IIncomeCalculationService _incomeCalculationService;
    private readonly IStandardConfigService _standardConfigService;

    /// <summary>最近一次核验原始结果（「从核验带入」取数用，见 .Income.cs）</summary>
    private QuickVerifyBundle? _lastBundle;

    /// <summary>全部分组视图（过滤前；报告需要"无记录"名单，见 .Report.cs）</summary>
    private List<QuickVerifyGroupView> _allGroupViews = new();

    /// <summary>上次核算采用的低保标准（带入/批量分配后自动回算收入用）</summary>
    private decimal _lastStandard;

    #region 抽象属性实现

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    #endregion

    public QuickVerifyViewModel(
        IServiceProvider serviceProvider,
        IDialogService dialogService,
        ILoggerService logger,
        IQuickVerifyService quickVerifyService,
        IIncomeCalculationService incomeCalculationService,
        IStandardConfigService standardConfigService)
    {
        _serviceProvider = serviceProvider;
        _dialogService = dialogService;
        _logger = logger;
        _quickVerifyService = quickVerifyService;
        _incomeCalculationService = incomeCalculationService;
        _standardConfigService = standardConfigService;
        Title = "快速核验 · 模拟测算";
        SelectedHukouType = HukouTypeOptions[0];   // 默认农村（本系统以农村乡镇业务为主）
    }

    #region 核验结果展示

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowVerifyHint))]
    [NotifyPropertyChangedFor(nameof(CanExportReport))]
    private bool _hasVerifyResult;

    /// <summary>尚未核验时的引导提示</summary>
    public bool ShowVerifyHint => !HasVerifyResult;

    /// <summary>核验完成但所有分组均零命中（无数据、无失败）</summary>
    [ObservableProperty]
    private bool _hasVerifyEmpty;

    /// <summary>核验摘要行：身份证（脱敏）· 匹配姓名与来源 · 命中组数</summary>
    [ObservableProperty]
    private string _verifySummary = string.Empty;

    [ObservableProperty]
    private ObservableCollection<QuickVerifyGroupView> _verifyGroups = new();

    #endregion

    #region 主命令：开始核算（收入 + 核验一次跑完）

    [RelayCommand]
    private async Task RunAllAsync()
    {
        var inputError = ValidateIncomeInputs();
        if (inputError != null)
        {
            await _dialogService.DisplayAlertAsync("提示", inputError, "确定");
            return;
        }
        if (IsBusy) return;

        var idCard = (SimIdCard ?? string.Empty).Trim().ToUpperInvariant();
        var isRural = SelectedHukouType?.Key == ClassificationConstants.HukouType.RURAL;
        var hukou = isRural ? "Rural" : "Urban";
        var standardType = isRural ? "RuralSubsistenceStandard" : "UrbanSubsistenceStandard";

        try
        {
            IsBusy = true;
            LoadingMessage = "正在核算与核验...";

            // ── 并行：台账核验 10 组 + 读取低保标准（收入区间与赡养费默认值都依赖标准）──
            var verifyTask = _quickVerifyService.VerifyAsync(idCard, SimName, CancellationToken);
            var stdResult = await _standardConfigService.GetStandardValueAsync(standardType, hukou, ct: CancellationToken);
            var verifyResult = await verifyTask;

            // 标准缺失 = 显式失败（禁止回退过期硬编码标准，与申请表/分类判定同纪律）
            if (stdResult.IsFailure || stdResult.Value <= 0)
            {
                await _dialogService.DisplayAlertAsync("标准缺失",
                    stdResult.Message ?? $"低保标准配置缺失或无效（{standardType}/{hukou}），请在「数据中心-标准配置管理」维护后重试。",
                    "确定");
                return;
            }

            // 核验整体失败（DB 级）→ 显式中止；部分组失败已在分组内展示原因
            if (verifyResult.IsFailure)
            {
                await _dialogService.DisplayAlertAsync("核验失败", verifyResult.Message ?? "台账查询失败", "确定");
                return;
            }

            ComputeIncome(stdResult.Value);
            _lastStandard = stdResult.Value;
            ApplyBundle(verifyResult.Value!);

            _logger.LogBusiness("快速核验核算",
                ("IdCard", DataMasker.MaskIdCard(idCard)),
                ("Hukou", hukou),
                ("FamilySize", SimFamilySizeText),
                ("Children", SimChildrenText));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 「从核验带入」前置：校验身份证 → 未核验/证号或姓名已变化时自动先跑一次核验。
    /// 返回身份证（成功）或 null（已弹提示/取消）。
    /// </summary>
    private async Task<string?> EnsureVerifiedForImportAsync()
    {
        var idCard = (SimIdCard ?? string.Empty).Trim().ToUpperInvariant();
        if (!IdCardValidator.IsValid(idCard))
        {
            await _dialogService.DisplayAlertAsync("提示", "请先输入正确的 18 位身份证号", "确定");
            return null;
        }

        // 已核验且证号未变、姓名未变（留空=沿用人员库反查名）→ 直接用
        var bundle = _lastBundle;
        var name = (SimName ?? string.Empty).Trim();
        var nameChanged = bundle != null && name.Length > 0 &&
                          !string.Equals(bundle.QueryName, name, StringComparison.Ordinal);
        if (bundle != null && bundle.IdCard == idCard && !nameChanged)
            return idCard;

        var result = await ExecuteAsync(
            () => _quickVerifyService.VerifyAsync(idCard, SimName, CancellationToken),
            "正在核验台账...");
        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("核验失败", result.Message ?? "台账查询失败", "确定");
            return null;
        }

        ApplyBundle(result.Value!);
        return idCard;
    }

    /// <summary>
    /// 带入/批量分配等输入变化后：若已出过核算结果，按当前输入立即回算收入（无需再点「开始核算」）。
    /// 纯内存计算、不查库；尚无结果或标准缺失时不动作。
    /// </summary>
    private void RecomputeIncomeIfResultExists()
    {
        if (HasIncomeResult && _lastStandard > 0)
            ComputeIncome(_lastStandard);
    }

    [RelayCommand]
    private void ClearAll()
    {
        // 人信息与收入输入
        SimName = string.Empty;
        SimIdCard = string.Empty;
        SimFamilySizeText = string.Empty;
        SimChildrenText = string.Empty;
        SimPerChildFeeText = string.Empty;
        SelectedHukouType = HukouTypeOptions[0];
        LandSelfAreaText = string.Empty;
        LandSubleaseAreaText = string.Empty;
        LandContractAreaText = string.Empty;
        SubsidyAnnualText = string.Empty;
        LandImportHint = string.Empty;
        SubsidyImportHint = string.Empty;
        _lastImportedLandTotal = 0;

        // 结果
        HasIncomeResult = false;
        IncomeLines.Clear();
        AnnualIncomeText = string.Empty;
        ZoneKind = string.Empty;
        HasVerifyResult = false;
        HasVerifyEmpty = false;
        VerifySummary = string.Empty;
        VerifyGroups.Clear();
        _allGroupViews = new();
        _lastBundle = null;
        _lastStandard = 0;
    }

    #endregion

    #region 核验分组展示构建

    /// <summary>把服务结果转成展示用分组视图（一次性构建，无变更通知需求）</summary>
    private void ApplyBundle(QuickVerifyBundle bundle)
    {
        _lastBundle = bundle;
        var groups = new List<QuickVerifyGroupView>();

        groups.Add(BuildGroup("🌾 土地·承包台账（外部）", bundle.ContractLand,
            r => new QuickVerifyItemView
            {
                Title = string.IsNullOrWhiteSpace(r.PlotName) ? $"地块 {r.PlotCode}" : $"{r.PlotName} · {r.PlotCode}",
                Detail = $"承包方 {r.ContractorName}｜发包方 {r.EmployerName}｜数据年份 {r.DataYear}",
                Value = $"{r.LandArea:F2} 亩"
            }));

        groups.Add(BuildGroup("🌾 土地·历史申请确权", bundle.HistoryLand,
            r => new QuickVerifyItemView
            {
                Title = $"{r.MemberName} · {r.PlotCode}",
                Detail = $"{r.LandPlotInfo}｜{r.LandUsage}｜来源档案 #{r.ApplicationId}",
                Value = $"{r.LandArea:F2} 亩"
            }));

        groups.Add(BuildGroup("🌱 种植补贴（最新年段）", bundle.Subsidies, r =>
        {
            // 面积型/金额型互推（与「导入农业补贴」同口径）：面积缺省按 金额÷单价 回算，金额缺省按 面积×单价 折算
            var price = SubsidyPriceConstants.GetPrice(r.SubsidyType);
            var area = r.Area > 0 ? r.Area
                : (price > 0 && r.Amount > 0 ? Math.Round(r.Amount / price, 2) : 0m);
            var amount = r.Amount > 0 ? r.Amount
                : (price > 0 && r.Area > 0 ? Math.Round(r.Area * price, 2) : 0m);
            var areaNote = r.Area <= 0 && area > 0 ? "（金额÷单价回算）" : string.Empty;
            var amountNote = r.Amount <= 0 && amount > 0 ? "（按单价折算）" : string.Empty;

            return new QuickVerifyItemView
            {
                Title = $"{r.SubsidyType} · {r.Name}",
                Detail = $"面积 {area:F2} 亩{areaNote}",
                Value = $"{amount:N2} 元{amountNote}"
            };
        }));

        groups.Add(BuildGroup("🛡 历史保障身份", bundle.SecurityIdentities,
            r => new QuickVerifyItemView
            {
                Title = $"{r.SourceDisplay} · {r.Name}",
                Detail = BuildIdentityDetail(r),
                Value = $"成员 {r.FamilyMemberCount} 人"
            }));

        groups.Add(BuildGroup("🚗 资产（车辆/房产/金融）", bundle.Assets,
            r => new QuickVerifyItemView
            {
                Title = $"{r.Category} · {r.Title}",
                Detail = r.Detail,
                Value = $"{r.Amount:N2} 元"
            }));

        groups.Add(BuildGroup("🏦 银行流水（最新批次）", bundle.BankFlows,
            r => new QuickVerifyItemView
            {
                Title = $"{r.PersonName} · 余额 {r.TotalBalance:N2} 元",
                Detail = $"12个月最高流水 {r.MaxFlowAmount:N2} 元" +
                         (string.IsNullOrWhiteSpace(r.BankSummary) ? "" : $"｜{r.BankSummary}"),
                Badge = r.IsExceed ? "超阈值" : string.Empty,
                Value = r.BatchNo
            }));

        groups.Add(BuildGroup("📄 经济状况核对报告", bundle.AssetReports,
            r => new QuickVerifyItemView
            {
                Title = r.FileName,
                Detail = $"报告日期 {(r.ReportDate?.ToString("yyyy-MM-dd") ?? "—")}｜版本 v{r.Version}",
                Badge = r.IsValid ? string.Empty : "文件名存疑"
            }));

        groups.Add(BuildGroup("🕯 死亡记录", bundle.Deaths,
            r => new QuickVerifyItemView
            {
                Title = $"{r.MemberName} · {r.RelationshipToHead}",
                Detail = $"死亡日期 {(r.DeathDate?.ToString("yyyy-MM-dd") ?? "—")}" +
                         (string.IsNullOrWhiteSpace(r.DeathReason) ? "" : $"｜{r.DeathReason}")
            }));

        groups.Add(BuildGroup("⛑ 临时救助", bundle.TempReliefs,
            r => new QuickVerifyItemView
            {
                Title = $"{r.ApplicantName} · {r.ReliefType}",
                Detail = $"申请 {(r.ApplyDate?.ToString("yyyy-MM-dd") ?? "—")}｜状态 {r.Status}",
                Value = $"{r.ConfirmAmount:N2} 元"
            }));

        groups.Add(BuildGroup("🧓 高龄津贴", bundle.ElderlyBenefits,
            r => new QuickVerifyItemView
            {
                Title = $"{r.Name} · {r.Category}",
                Detail = $"受理 {(r.ApplyDate?.ToString("yyyy-MM-dd") ?? "—")}｜状态 {r.Status}",
                Value = $"{r.IssueAmount:N2} 元/月"
            }));

        // 留存全部分组（报告需要"无记录"名单），界面上只渲染有数据/失败的组
        _allGroupViews = groups;
        VerifyGroups = new ObservableCollection<QuickVerifyGroupView>(
            groups.Where(g => g.HasItems || g.IsFailure).ToList());   // 零命中分组整体隐藏，失败组保留（§7）
        HasVerifyEmpty = groups.All(g => !g.HasItems && !g.IsFailure);

        var hitGroups = groups.Count(g => g.HasItems);
        var nameText = string.IsNullOrWhiteSpace(bundle.QueryName)
            ? "未匹配到姓名"
            : $"{bundle.QueryName}（{bundle.NameSource}）";
        VerifySummary = $"身份证 {DataMasker.MaskIdCard(bundle.IdCard)} · 匹配姓名 {nameText} · {hitGroups} 组有数据（无数据分组已隐藏，只读核验不写入任何数据）";
        HasVerifyResult = true;
    }

    private static string BuildIdentityDetail(SecurityIdentityRow r)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.Relationship)) parts.Add($"关系 {r.Relationship}");
        if (!string.IsNullOrWhiteSpace(r.Phone)) parts.Add($"电话 {r.Phone}");
        if (!string.IsNullOrWhiteSpace(r.Address)) parts.Add(r.Address);
        return string.Join("｜", parts);
    }

    private static QuickVerifyGroupView BuildGroup<T>(
        string title,
        QuickVerifyGroup<T> group,
        Func<T, QuickVerifyItemView> mapper)
    {
        var items = group.IsFailure
            ? new ObservableCollection<QuickVerifyItemView>()
            : new ObservableCollection<QuickVerifyItemView>(group.Items.Select(mapper));

        return new QuickVerifyGroupView
        {
            Title = title,
            IsFailure = group.IsFailure,
            FailureText = group.FailureReason,
            MatchNote = group.MatchNote,
            CountText = group.IsFailure || group.Count == 0 ? string.Empty : $"共 {group.Count} 条",
            HasItems = items.Count > 0,
            // 零命中分组在 ApplyBundle 里整体隐藏（不渲染），此处只保留有数据/失败的组
            Items = items
        };
    }

    #endregion
}

/// <summary>核验分组展示视图（一次性构建，属性只读）</summary>
public class QuickVerifyGroupView
{
    public string Title { get; init; } = string.Empty;
    public bool IsFailure { get; init; }
    public string FailureText { get; init; } = string.Empty;
    public string MatchNote { get; init; } = string.Empty;
    public string CountText { get; init; } = string.Empty;
    public bool HasItems { get; init; }
    public bool ShowMatchNote => !string.IsNullOrEmpty(MatchNote);
    public ObservableCollection<QuickVerifyItemView> Items { get; init; } = new();
}

/// <summary>核验单行（主行/次行/右侧值/徽标，空值即隐藏）</summary>
public class QuickVerifyItemView
{
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Badge { get; init; } = string.Empty;
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
    public bool HasValue => !string.IsNullOrEmpty(Value);
    public bool HasBadge => !string.IsNullOrEmpty(Badge);
}
