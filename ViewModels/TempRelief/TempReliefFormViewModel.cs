using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.TempRelief;
using NewCosmos.Services.Utilities;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NewCosmos.ViewModels.TempRelief;

/// <summary>
/// 临时救助申请表单 ViewModel
/// 分区：0 类型与申请人 → 1 申请信息 → 2 金额(小额)与公示核实 → 3 家庭成员
/// 申请人来源仅限导入台账库 + 低保申请库（选择式，不可自由录入）
/// </summary>
public partial class TempReliefFormViewModel : ViewModelBase
{
    private readonly ITempReliefService _applicationService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IDialogService _dialogService = null!;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    private long _id;
    private bool _suppressReload;
    private string _lastLargePaymentChannel = TempReliefConstants.PaymentCounty;

    /// <summary>公示日期默认值的计算代际计数：连续触发时仅最新一次结果生效</summary>
    private int _publicizeCalcGeneration;

    /// <summary>用户是否手动编辑过救助原因（为 true 时类型/明细变化不再自动覆盖）</summary>
    private bool _reasonDirty;

    /// <summary>程序自动生成救助原因时抑制 dirty 标记的瞬时标志</summary>
    private bool _suppressReasonDirty;

    #region 操作模式

    [ObservableProperty]
    private FormOperationMode _operationMode = FormOperationMode.Create;

    public bool IsCreateMode => OperationMode == FormOperationMode.Create;
    public bool IsViewMode => OperationMode == FormOperationMode.View;
    public bool IsEditMode => OperationMode == FormOperationMode.Edit;
    public bool IsEditable => OperationMode != FormOperationMode.View;
    public bool IsSavable => OperationMode != FormOperationMode.View;
    public bool IsNewMode => OperationMode == FormOperationMode.Create;

    #endregion

    #region 表单分区 Tab

    [ObservableProperty]
    private int _selectedSection;

    public bool IsSection0Selected => SelectedSection == 0;
    public bool IsSection1Selected => SelectedSection == 1;
    public bool IsSection2Selected => SelectedSection == 2;
    public bool IsSection3Selected => SelectedSection == 3;

    /// <summary>
    /// 分区视觉顺序：类型与申请人(0) → 家庭成员(3) → 申请信息(1) → 金额与公示(2)。
    /// 上一步/下一步按此映射导航；Tab 栏点击直接按编号切换。
    /// </summary>
    private static readonly int[] SectionOrder = { 0, 3, 1, 2 };

    private int PositionOf(int section)
    {
        var idx = Array.IndexOf(SectionOrder, section);
        return idx >= 0 ? idx : 0;
    }

    public bool CanGoPrevious => PositionOf(SelectedSection) > 0;
    public bool CanGoNext => PositionOf(SelectedSection) < SectionOrder.Length - 1;
    public bool IsSaveAndConfirmVisible => IsSavable && IsSection2Selected; // 视觉最后一步：金额与公示

    partial void OnSelectedSectionChanged(int value)
    {
        OnPropertyChanged(nameof(IsSection0Selected));
        OnPropertyChanged(nameof(IsSection1Selected));
        OnPropertyChanged(nameof(IsSection2Selected));
        OnPropertyChanged(nameof(IsSection3Selected));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(IsSaveAndConfirmVisible));
        PreviousSectionCommand.NotifyCanExecuteChanged();
        NextSectionCommand.NotifyCanExecuteChanged();
    }

    private bool CanGoPreviousMethod() => CanGoPrevious;
    private bool CanGoNextMethod() => CanGoNext;

    [RelayCommand(CanExecute = nameof(CanGoPreviousMethod))]
    private void PreviousSection()
    {
        var pos = PositionOf(SelectedSection);
        if (pos > 0) SelectedSection = SectionOrder[pos - 1];
    }

    [RelayCommand(CanExecute = nameof(CanGoNextMethod))]
    private void NextSection()
    {
        var pos = PositionOf(SelectedSection);
        if (pos < SectionOrder.Length - 1) SelectedSection = SectionOrder[pos + 1];
    }

    [RelayCommand]
    private void SwitchSection(string? sectionIndex)
    {
        if (int.TryParse(sectionIndex, out var idx))
        {
            SelectedSection = idx;
        }
    }

    #endregion

    #region 两大类（人群状态）

    /// <summary>人群状态：Insured 在保障人群内 / Uninsured 不在保障人群内</summary>
    [ObservableProperty]
    private string _populationStatus = TempReliefConstants.PopulationInsured;

    public bool IsInsured => PopulationStatus == TempReliefConstants.PopulationInsured;
    public bool IsUninsured => !IsInsured;

    /// <summary>非在册手工登记：未选中任何台账候选时，申请人字段可编辑</summary>
    public bool IsManualEntry => IsUninsured && string.IsNullOrEmpty(_sourceTable);

    /// <summary>在册必须走台账选择（只读）；非在册手工登记可编辑；已选候选时只读</summary>
    public bool IsApplicantReadOnly => !IsManualEntry;

    partial void OnIsCandidateSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsManualEntry));
        OnPropertyChanged(nameof(IsApplicantReadOnly));
    }


    partial void OnPopulationStatusChanged(string value)
    {
        OnPropertyChanged(nameof(IsInsured));
        OnPropertyChanged(nameof(IsUninsured));
        OnPropertyChanged(nameof(IsManualEntry));
        OnPropertyChanged(nameof(IsApplicantReadOnly));
        if (_suppressReload) return;

        // 切换大类必须重选来源：在册走四类台账，非在册可手工登记或从其他困难/申请库选
        _sourceTable = string.Empty;
        _sourceFamilyId = 0;
        Candidates.Clear();
        SelectedCandidate = null;
        IsCandidateSelected = false;
        CandidateSummary = string.Empty;
        OnPropertyChanged(nameof(IsManualEntry));
        OnPropertyChanged(nameof(IsApplicantReadOnly));
    }

    [RelayCommand]
    private void SelectInsured() => PopulationStatus = TempReliefConstants.PopulationInsured;

    [RelayCommand]
    private void SelectUninsured() => PopulationStatus = TempReliefConstants.PopulationUninsured;

    #endregion

    #region 救助类型

    [ObservableProperty]
    private string _reliefType = TempReliefConstants.ReliefTypeLarge;

    public bool IsLargeType => ReliefType == TempReliefConstants.ReliefTypeLarge;
    public bool IsSmallType => ReliefType == TempReliefConstants.ReliefTypeSmall;

    /// <summary>发放渠道：County 上级部门发放 / Reserve 备用金发放（小额固定 Reserve）</summary>
    [ObservableProperty]
    private string _paymentChannel = TempReliefConstants.PaymentCounty;

    public bool IsCountyChannel => PaymentChannel == TempReliefConstants.PaymentCounty;
    public bool IsReserveChannel => PaymentChannel == TempReliefConstants.PaymentReserve;

    /// <summary>常规·备用金：显示手工金额输入</summary>
    public bool IsLargeReserve => IsLargeType && IsReserveChannel;

    partial void OnPaymentChannelChanged(string value)
    {
        OnPropertyChanged(nameof(IsCountyChannel));
        OnPropertyChanged(nameof(IsReserveChannel));
        OnPropertyChanged(nameof(IsLargeReserve));
        if (!_suppressReload && value == TempReliefConstants.PaymentReserve)
            SafeFireAndForget(async () => await LoadSmallAmountLevelsAsync());
    }

    [RelayCommand]
    private void SelectCountyChannel() => PaymentChannel = TempReliefConstants.PaymentCounty;

    [RelayCommand]
    private void SelectReserveChannel() => PaymentChannel = TempReliefConstants.PaymentReserve;

    partial void OnReliefTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsLargeType));
        OnPropertyChanged(nameof(IsSmallType));
        OnPropertyChanged(nameof(IsLargeReserve));
        if (_suppressReload) return;

        if (value == TempReliefConstants.ReliefTypeSmall)
        {
            // 小额快救固定备用金；免公示、免经济状况核对（保留入户调查记录）
            _lastLargePaymentChannel = PaymentChannel;
            PaymentChannel = TempReliefConstants.PaymentReserve;
            HasPublicizeDates = false;
            SafeFireAndForget(async () => await LoadSmallAmountLevelsAsync());
        }
        else
        {
            // 切回常规救助：恢复上次渠道与 5 天公示曲线
            PaymentChannel = _lastLargePaymentChannel;
            // 小额档位金额不沿用到常规；常规备用金由用户手工填写
            ConfirmAmountInput = string.Empty;
            _confirmAmount = 0m;
            _confirmAmountMax = 0m;
            SafeFireAndForget(async () => await ApplyPublicizeDefaultsAsync());
        }
    }

    [RelayCommand]
    private void SelectLargeType() => ReliefType = TempReliefConstants.ReliefTypeLarge;

    [RelayCommand]
    private void SelectSmallType() => ReliefType = TempReliefConstants.ReliefTypeSmall;

    #endregion

    #region 小额定额档位

    [ObservableProperty]
    private ObservableCollection<string> _smallAmountLevelOptions = new();

    [ObservableProperty]
    private string _selectedSmallAmountLevel = string.Empty;

    /// <summary>确定金额（可编辑：默认=户籍对应低保月标准，只可下调不可上调）</summary>
    [ObservableProperty]
    private string _confirmAmountInput = string.Empty;

    /// <summary>小额定额说明（基数 × 档位 × 人数 / 金额上限提示）</summary>
    [ObservableProperty]
    private string _smallAmountHint = string.Empty;

    /// <summary>家庭遇困人数（含户主）输入；≠ 家庭人口</summary>
    [ObservableProperty]
    private string _reliefPersonCountInput = "1";

    /// <summary>家庭遇困人数（解析后的落库值；0 = 非法，保存前拦截）</summary>
    public int ReliefPersonCount { get; private set; } = 1;

    partial void OnReliefPersonCountInputChanged(string value)
    {
        ReliefPersonCount = int.TryParse(value?.Trim(), out var count) && count > 0 ? count : 0;
        OnPropertyChanged(nameof(ReliefPersonCountHint));
        if (!_suppressReload) UpdateConfirmAmount();
    }

    /// <summary>遇困人数提示（家庭人口上限来自成员明细统一口径）</summary>
    public string ReliefPersonCountHint => ApplicantFamilySize is > 0
        ? $"家庭人口 {ApplicantFamilySize} 人；遇困人数含户主，须 >0 且 ≤ {ApplicantFamilySize}"
        : "遇困人数含户主，须大于 0";

    /// <summary>确定金额（落库值；输入非法时为 0，保存前会被校验拦截）</summary>
    private decimal _confirmAmount;

    /// <summary>当前档位对应的低保月标准（确定金额上限）</summary>
    private decimal _confirmAmountMax;

    partial void OnSelectedSmallAmountLevelChanged(string value)
    {
        UpdateConfirmAmount();
    }

    partial void OnConfirmAmountInputChanged(string value)
    {
        _confirmAmount = TempReliefConstants.TryParseConfirmAmount(value, out var amount) ? amount : 0m;
    }

    /// <summary>当前生效的城市低保月标准（金额基数）</summary>
    private decimal _baseStandard;

    /// <summary>
    /// 档位/人数变化：确定金额默认按政策公式重算（人数 × 城市低保标准 × 系数，进一法到元），
    /// 用户仍可手动下调；存量旧档位仅回显、不按新公式重算。
    /// </summary>
    private void UpdateConfirmAmount()
    {
        var level = SmallAmountLevels.FirstOrDefault(s => s.StandardName == SelectedSmallAmountLevel);
        if (level == null)
        {
            _confirmAmountMax = 0m;
            _confirmAmount = 0m;
            ConfirmAmountInput = string.Empty;
            SmallAmountHint = BuildSmallAmountHint();
            return;
        }

        // 存量旧档位（无政策档位码）：保留原金额回显，提示改选政策档位
        if (TempReliefConstants.TierMultiplier(level.StandardType) <= 0)
        {
            _confirmAmountMax = level.StandardValue;
            _confirmAmount = level.StandardValue;
            ConfirmAmountInput = level.StandardValue > 0
                ? level.StandardValue.ToString("0.##", CultureInfo.InvariantCulture)
                : string.Empty;
            SmallAmountHint = BuildSmallAmountHint();
            return;
        }

        _confirmAmountMax = TempReliefConstants.GetSmallTierCeiling(
            _baseStandard, ReliefPersonCount, level.StandardType);
        _confirmAmount = _confirmAmountMax;
        ConfirmAmountInput = _confirmAmountMax > 0
            ? _confirmAmountMax.ToString("0.##", CultureInfo.InvariantCulture)
            : string.Empty;
        SmallAmountHint = BuildSmallAmountHint();
    }

    private string BuildSmallAmountHint()
    {
        if (ReliefPersonCount <= 0)
            return "请填写家庭遇困人数（含户主，须大于 0）。";
        if (_baseStandard <= 0)
            return "未取到当前生效的城市低保月标准，无法自动核定金额。";

        var level = SmallAmountLevels.FirstOrDefault(s => s.StandardName == SelectedSmallAmountLevel);
        var multiplier = TempReliefConstants.TierMultiplier(level?.StandardType);
        if (multiplier <= 0)
            return "存量档位仅用于回显；请改选政策档位（50%/75%/100%）。";

        return $"小额快救：{ReliefPersonCount} 人 × 城市低保标准 {_baseStandard:F2} 元 × {multiplier:0.##} = {_confirmAmountMax:F2} 元（进一法到元）；可下调，不得超过该上限。小额快救免公示、免经济状况核对。";
    }

    private List<ConfigStandard> SmallAmountLevels { get; set; } = new();

    private async Task LoadSmallAmountLevelsAsync()
    {
        // 政策第十条：基数 = 当前有效的城市低保月标准最高值，不按户籍分流。
        var baseResult = await _applicationService.GetReliefBaseStandardAsync(CancellationToken);
        if (baseResult.IsFailure)
        {
            _logger.LogError(new Exception(baseResult.Message ?? "未知错误"), "加载城市低保月标准失败");
            _baseStandard = 0m;
            SmallAmountHint = baseResult.Message ?? "未取到当前生效的城市低保月标准，无法自动核定金额。";
            return;
        }

        _baseStandard = baseResult.Value;

        // 常规救助只需要基数（备用金上限校验），不构建小额档位
        if (IsLargeType)
        {
            SmallAmountLevels = new();
            SmallAmountLevelOptions.Clear();
            SelectedSmallAmountLevel = string.Empty;
            SmallAmountHint = $"备用金上限 = 城市低保标准 {_baseStandard:F2} × 遇困人数 × 6；手工填写金额。";
            return;
        }

        SmallAmountLevels = TempReliefConstants.SmallTierCodes
            .Select(code => new ConfigStandard
            {
                StandardType = code,
                StandardName = $"{TempReliefConstants.GetTierLabel(code)}档",
                StandardValue = _baseStandard,
                Unit = "元/月",
                IsActive = true,
                EffectiveStartDate = DateTime.Today
            })
            .ToList();

        // 已在 UI 线程时同步应用：BeginInvokeOnMainThread 在 Windows 上恒入队，
        // 会晚于 LoadAsync 的存量档位兜底执行，把选中项改回第一档 → 存量金额回显丢失。
        void ApplyLevelOptions()
        {
            SmallAmountLevelOptions.Clear();
            foreach (var level in SmallAmountLevels)
            {
                SmallAmountLevelOptions.Add(level.StandardName);
            }
            if (SmallAmountLevelOptions.Count > 0)
                SelectedSmallAmountLevel = SmallAmountLevelOptions[0];
        }

        if (MainThread.IsMainThread)
            ApplyLevelOptions();
        else
            MainThread.BeginInvokeOnMainThread(ApplyLevelOptions);
    }

    #endregion

    #region 申请人（来源选择）

    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    [ObservableProperty]
    private ObservableCollection<TempReliefCandidate> _candidates = new();

    [ObservableProperty]
    private TempReliefCandidate? _selectedCandidate;

    [ObservableProperty]
    private bool _isCandidateSelected;

    [ObservableProperty]
    private string _candidateSummary = string.Empty;

    // 回填后的申请人信息（快照）
    [ObservableProperty]
    private string _applicantName = string.Empty;

    [ObservableProperty]
    private string _applicantIdCard = string.Empty;

    [ObservableProperty]
    private string _applicantGender = string.Empty;

    [ObservableProperty]
    private int? _applicantAge;

    [ObservableProperty]
    private string _applicantPhone = string.Empty;

    [ObservableProperty]
    private string _applicantHukou = string.Empty;

    [ObservableProperty]
    private string _applicantTown = string.Empty;

    [ObservableProperty]
    private string _applicantVillage = string.Empty;

    /// <summary>家庭住址-省</summary>
    [ObservableProperty]
    private string _applicantProvince = string.Empty;

    /// <summary>家庭住址-市</summary>
    [ObservableProperty]
    private string _applicantCity = string.Empty;

    /// <summary>家庭住址-县区</summary>
    [ObservableProperty]
    private string _applicantDistrict = string.Empty;

    /// <summary>家庭住址-详细地址</summary>
    [ObservableProperty]
    private string _applicantDetailAddress = string.Empty;

    /// <summary>户籍所在地-省</summary>
    [ObservableProperty]
    private string _hukouProvince = string.Empty;

    /// <summary>户籍所在地-市</summary>
    [ObservableProperty]
    private string _hukouCity = string.Empty;

    /// <summary>户籍所在地-县区</summary>
    [ObservableProperty]
    private string _hukouDistrict = string.Empty;

    /// <summary>户籍所在地-镇社区</summary>
    [ObservableProperty]
    private string _hukouTown = string.Empty;

    [ObservableProperty]
    private int? _applicantFamilySize;

    partial void OnApplicantFamilySizeChanged(int? value)
    {
        OnPropertyChanged(nameof(ReliefPersonCountHint));
    }

    [ObservableProperty]
    private string _applicantFamilyCategory = string.Empty;

    /// <summary>开户行（银行卡开户行）</summary>
    [ObservableProperty]
    private string _bankName = string.Empty;

    /// <summary>银行卡号/一卡通账号</summary>
    [ObservableProperty]
    private string _bankAccount = string.Empty;

    /// <summary>来源表名</summary>
    private string _sourceTable = string.Empty;

    /// <summary>来源家庭ID</summary>
    private long _sourceFamilyId;

    /// <summary>户籍类型快照（农村/城镇；勾选清单城乡判定指定字段）</summary>
    private string _hukouType = string.Empty;

    #endregion

    #region 救助对象（从家庭成员中选定；户主/申请人恒定不变）

    /// <summary>救助对象选项（户主 + 全部家庭成员）</summary>
    public ObservableCollection<BeneficiaryOption> BeneficiaryOptions { get; } = new();

    [ObservableProperty]
    private BeneficiaryOption? _selectedBeneficiary;

    [ObservableProperty]
    private string _beneficiaryName = string.Empty;

    [ObservableProperty]
    private string _beneficiaryIdCard = string.Empty;

    [ObservableProperty]
    private string _beneficiaryGender = string.Empty;

    [ObservableProperty]
    private int? _beneficiaryAge;

    [ObservableProperty]
    private string _beneficiaryRelation = string.Empty;

    /// <summary>已选定非户主成员作为救助对象</summary>
    public bool IsNonHeadBeneficiary => SelectedBeneficiary is { IsHead: false };

    /// <summary>选中救助对象 → 回填快照字段（户主/申请人字段不受影响）</summary>
    partial void OnSelectedBeneficiaryChanged(BeneficiaryOption? value)
    {
        if (_suppressReload) return;
        ApplyBeneficiarySnapshot(value);
        OnPropertyChanged(nameof(IsNonHeadBeneficiary));
    }

    private void ApplyBeneficiarySnapshot(BeneficiaryOption? value)
    {
        if (value == null) return;
        BeneficiaryName = value.Name;
        BeneficiaryIdCard = value.IdCard;
        BeneficiaryGender = value.Gender;
        BeneficiaryAge = value.Age;
        BeneficiaryRelation = value.Relation;
    }

    /// <summary>
    /// 重建救助对象选项：户主（Applicant 快照，默认选中）+ 全部家庭成员（按身份证去重）。
    /// preferredIdCard：编辑回显时优先匹配的救助对象身份证。
    /// </summary>
    private void RebuildBeneficiaryOptions(string? preferredIdCard = null)
    {
        var options = new List<BeneficiaryOption>();
        if (!string.IsNullOrWhiteSpace(ApplicantName))
            options.Add(new BeneficiaryOption(
                ApplicantName, ApplicantIdCard, ApplicantGender, ApplicantAge,
                "户主", IsHead: true));

        foreach (var m in Members)
        {
            if (string.IsNullOrWhiteSpace(m.MemberName)) continue;
            if (options.Any(o => o.IdCard == m.IdCard)) continue;
            options.Add(new BeneficiaryOption(
                m.MemberName, m.IdCard ?? "", m.Gender ?? "",
                Helpers.IdCardValidator.ExtractAge(m.IdCard ?? ""),
                string.IsNullOrWhiteSpace(m.Relation) ? "家庭成员" : m.Relation,
                IsHead: false));
        }

        BeneficiaryOptions.Clear();
        foreach (var o in options) BeneficiaryOptions.Add(o);

        // 匹配优先项，否则默认户主
        var match = string.IsNullOrWhiteSpace(preferredIdCard)
            ? null
            : options.FirstOrDefault(o => o.IdCard == preferredIdCard);
        SelectedBeneficiary = match ?? options.FirstOrDefault();

        // 抑制期间 SelectedBeneficiary 变更不触发回填，此处兜底写快照
        if (SelectedBeneficiary != null && _suppressReload)
            ApplyBeneficiarySnapshot(SelectedBeneficiary);
    }

    /// <summary>明细行"患病/受灾成员"候选名单：户主 + 家庭成员姓名（供疾病/灾害行 Picker 使用）</summary>
    public ObservableCollection<string> MemberNameOptions { get; } = new();

    /// <summary>
    /// 重建成员候选名单。两条硬约束：
    /// ① 内容与现状一致时**不动集合**——Clear()+Add 会让绑定中的 Picker 重置选中，
    ///    并把空值 TwoWay 回写进明细行（曾导致重新进入草稿后"患病成员"全变空）；
    /// ② 已存明细行里的成员姓名一并纳入候选——成员行被删/改名后，历史值仍能正常显示。
    /// </summary>
    private void RebuildMemberNameOptions()
    {
        var names = new List<string>();
        if (!string.IsNullOrWhiteSpace(ApplicantName)) names.Add(ApplicantName.Trim());

        void Include(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            var name = raw.Trim();
            if (!names.Contains(name)) names.Add(name);
        }

        foreach (var m in Members) Include(m.MemberName);
        foreach (var d in Diseases) Include(d.MemberName);
        foreach (var a in Accidents) Include(a.MemberName);

        // 差异同步：只删多余、只补缺失，绝不整体 Clear
        for (var i = MemberNameOptions.Count - 1; i >= 0; i--)
        {
            if (!names.Contains(MemberNameOptions[i])) MemberNameOptions.RemoveAt(i);
        }
        foreach (var name in names)
        {
            if (!MemberNameOptions.Contains(name)) MemberNameOptions.Add(name);
        }
    }

    /// <summary>
    /// 照片查看/采集：进入**档案照片查询页**（与低收入、高龄同口径）——检索/预览/批量推送打印/插入；
    /// 要就地拍照走查询页的「打开采集页」，申请 ID 与识别回填上下文随参数透传，
    /// 「拍照 → OCR → 回填表单」能力保留。照片按身份证归档，三模块共享同一份人员照片。
    /// </summary>
    [RelayCommand]
    private async Task OpenPhotosAsync()
    {
        if (string.IsNullOrWhiteSpace(ApplicantIdCard))
        {
            var choice = await _dialogService.DisplayActionSheetAsync(
                "身份证号缺失", "取消", null, "扫描证件", "拍照/选图存入待归属");
            if (choice == "扫描证件")
            {
                await ScanIdCardAsync();
                return;
            }
            if (choice == "拍照/选图存入待归属")
            {
                await NavigateToPageAsync<Pages.Mobile.MobileArchivePhotoPage, ArchivePhotoParameter>(
                    new ArchivePhotoParameter(
                        string.Empty,
                        _id > 0 ? _id : null,
                        ApplicantName,
                        AssignContext: this));
            }
            return;
        }

        _logger.LogBusiness("临时救助-档案照片", ("ApplicationId", _id.ToString()));
        await NavigateToPageAsync<Pages.ArchiveManagement.ArchivePhotoQueryPage, ArchivePhotoQueryParameter>(
            new ArchivePhotoQueryParameter(
                ApplicantIdCard.Trim(),
                ApplicationId: _id > 0 ? _id : null,
                AssignContext: this));
    }

    /// <summary>
    /// 扫描证件（不要求先有身份证）：相机/选图 → OCR → 定向回填姓名/身份证/详细地址。
    /// </summary>
    [RelayCommand]
    private async Task ScanIdCardAsync()
    {
        try
        {
            var completion = new TaskCompletionSource<int>();
            await NavigateToPageAsync<Pages.Mobile.MobileOcrScanPage, OcrScanParameter>(
                new OcrScanParameter(this, applied => completion.TrySetResult(applied)));

            var applied = await completion.Task;
            if (applied > 0)
                await _dialogService.ShowSnackBarAsync($"已回填 {applied} 条识别结果");

            _logger.LogBusiness("临时救助-证件扫描", ("Applied", applied.ToString()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "临时救助证件扫描失败");
            await _dialogService.DisplayAlertAsync("提示", "扫描失败，请重试或手动录入", "确定");
        }
    }

    /// <summary>救助对象选项条目</summary>
    public record BeneficiaryOption(
        string Name, string IdCard, string Gender, int? Age, string Relation, bool IsHead)
    {
        public string DisplayName => IsHead ? $"{Name}（户主）" : $"{Name}（{Relation}）";
    }

    #endregion

    #region 申请信息

    [ObservableProperty]
    private DateTime _applyDate = DateTime.Today;

    /// <summary>
    /// 归属批次提示（只读）：新建/草稿时显示本次申请落在哪个 C 线批次与逐笔时间曲线，
    /// 例："本次归属：2026年11月批次（公示 11-11~11-13，验收 11-14）"。
    /// 批次 = 申请日 ≤ 当月 13 号归本月（仅作标签）；曲线 = 申请日+1 公示起、+3 止、+4 验收。
    /// 编辑/查看已存档案保留库中原值、不重算，故为空。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBatchHint))]
    private string _batchHintText = string.Empty;

    /// <summary>归属批次提示是否显示（配合 StringNotEmptyConverter 控制 Label 显隐）</summary>
    public bool HasBatchHint => !string.IsNullOrWhiteSpace(BatchHintText);

    /// <summary>申请日期变化 → 重算逐笔 5 天曲线（公示起 = 申请日+1、止 = +3、验收 = +4）与批次标签</summary>
    partial void OnApplyDateChanged(DateTime value)
    {
        if (_suppressReload) return;
        SafeFireAndForget(ApplyPublicizeDefaultsAsync);
    }

    [ObservableProperty]
    private string _difficultyReason = string.Empty;

    /// <summary>救助原因变更：加载中或程序自动生成（_suppressReasonDirty）时不标记，否则视为用户手动编辑</summary>
    partial void OnDifficultyReasonChanged(string value)
    {
        if (_suppressReload) return;
        if (_suppressReasonDirty) return;
        _reasonDirty = true;
    }

    /// <summary>家庭类别选项（7 类）</summary>
    public string[] FamilyCategoryOptions => TempReliefConstants.FamilyCategoryOptions;

    /// <summary>困难类型选项（疾病/意外灾害/教育支出/其他困难）</summary>
    public string[] DifficultyTypeOptions => TempReliefConstants.DifficultyTypeOptions;

    [ObservableProperty]
    private string _difficultyType = string.Empty;

    /// <summary>是否为疾病困难（控制疾病明细区显隐）</summary>
    public bool IsDiseaseType => DifficultyType == TempReliefConstants.DifficultyTypeDisease;

    /// <summary>是否为意外灾害困难（控制意外灾害明细区显隐）</summary>
    public bool IsAccidentType => DifficultyType == TempReliefConstants.DifficultyTypeAccident;

    /// <summary>是否为教育支出困难（控制教育支出明细区显隐）</summary>
    public bool IsEducationType => DifficultyType == TempReliefConstants.DifficultyTypeEducation;

    /// <summary>意外/灾害类型选项</summary>
    public string[] AccidentTypeOptions => TempReliefConstants.AccidentTypeOptions;

    /// <summary>就学阶段选项</summary>
    public string[] EducationStageOptions => TempReliefConstants.EducationStageOptions;

    /// <summary>学年制选项（三年制/四年制/五年制）</summary>
    public string[] SchoolDurationOptions => TempReliefConstants.SchoolDurationOptions;

    /// <summary>所在医院（疾病明细共享，区域顶部填写一次）</summary>
    [ObservableProperty]
    private string _hospital = string.Empty;

    partial void OnHospitalChanged(string value)
    {
        PropagateSharedValuesToRows();
        RegenerateOnSharedDiseaseFieldChanged();
    }

    /// <summary>治疗开始日期（疾病明细共享，区域顶部填写一次）</summary>
    [ObservableProperty]
    private DateTime _treatStartDate = DateTime.Today;

    partial void OnTreatStartDateChanged(DateTime value)
    {
        PropagateSharedValuesToRows();
        RegenerateOnSharedDiseaseFieldChanged();
    }

    /// <summary>治疗结束日期（疾病明细共享，区域顶部填写一次）</summary>
    [ObservableProperty]
    private DateTime _treatEndDate = DateTime.Today;

    partial void OnTreatEndDateChanged(DateTime value)
    {
        PropagateSharedValuesToRows();
        RegenerateOnSharedDiseaseFieldChanged();
    }

    /// <summary>医疗费用总额（元，疾病明细共享，区域顶部填写一次）</summary>
    [ObservableProperty]
    private decimal? _medicalTotal;

    partial void OnMedicalTotalChanged(decimal? value)
    {
        PropagateSharedValuesToRows();
        RegenerateOnSharedDiseaseFieldChanged();
    }

    /// <summary>医保/其他报销金额（元，疾病明细共享，区域顶部填写一次）</summary>
    [ObservableProperty]
    private decimal? _insurancePaid;

    partial void OnInsurancePaidChanged(decimal? value)
    {
        PropagateSharedValuesToRows();
        RegenerateOnSharedDiseaseFieldChanged();
    }

    /// <summary>个人自付费用（元，疾病明细共享，区域顶部填写一次）</summary>
    [ObservableProperty]
    private decimal? _selfPaid;

    partial void OnSelfPaidChanged(decimal? value) => RegenerateOnSharedDiseaseFieldChanged();

    /// <summary>
    /// 共享模式下把顶部共享值实时同步到每条疾病明细（新增行/改顶部字段时调用）。
    /// 保持"行间取值始终一致"，保存护栏（BuildApplication.HasRowValueDivergence）才不会误触发；
    /// 加载中（_suppressReload）与逐条模式不处理——加载期共享值本就取自明细行本身。
    /// </summary>
    private void PropagateSharedValuesToRows()
    {
        if (_suppressReload) return;
        if (IsMultiHospitalMode) return;
        if (Diseases.Count == 0) return;

        foreach (var d in Diseases)
        {
            d.Hospital = Hospital;
            d.TreatStartDate = TreatStartDate;
            d.TreatEndDate = TreatEndDate;
            d.MedicalTotal = MedicalTotal;
            d.InsurancePaid = InsurancePaid;
            d.SelfPaid = SelfPaid;
        }
    }

    /// <summary>
    /// 是否为"逐条填写"模式（原名"多家医院"）：开启后每条疾病明细独立填写医院/日期/费用；
    /// 关闭时使用顶部共享值。同一家医院的多次住院也需开启（否则逐行日期会被共享值压平）。
    /// </summary>
    [ObservableProperty]
    private bool _isMultiHospitalMode;

    partial void OnIsMultiHospitalModeChanged(bool value)
    {
        if (_suppressReload) return;
        if (Diseases.Count == 0) return;

        if (!value)
        {
            // 从逐条 → 共享模式：用第一条记录的值回填共享字段，
            // 并立即把共享值写回每条明细——使各行取值一致。
            // 这样用户"手动关闭开关"的统一意图在保存时不再被护栏当成异常差异拦下
            // （护栏只拦"未主动统一却被覆盖"的路径，见 BuildApplication）。
            var first = Diseases[0];
            Hospital = first.Hospital ?? string.Empty;
            TreatStartDate = first.TreatStartDate == DateTime.MinValue ? DateTime.Today : first.TreatStartDate;
            TreatEndDate = first.TreatEndDate == DateTime.MinValue ? DateTime.Today : first.TreatEndDate;
            MedicalTotal = first.MedicalTotal;
            InsurancePaid = first.InsurancePaid;
            SelfPaid = first.SelfPaid;

            foreach (var d in Diseases)
            {
                d.Hospital = Hospital;
                d.TreatStartDate = TreatStartDate;
                d.TreatEndDate = TreatEndDate;
                d.MedicalTotal = MedicalTotal;
                d.InsurancePaid = InsurancePaid;
                d.SelfPaid = SelfPaid;
            }
        }
        else
        {
            // 从共享 → 多医院模式：把共享字段值写入每条记录
            foreach (var d in Diseases)
            {
                d.Hospital = Hospital;
                d.TreatStartDate = TreatStartDate;
                d.TreatEndDate = TreatEndDate;
                d.MedicalTotal = MedicalTotal;
                d.InsurancePaid = InsurancePaid;
                d.SelfPaid = SelfPaid;
            }
        }
    }

    /// <summary>疾病共享字段（医院/治疗日期/费用等）变化时，未手动编辑过救助原因则重新生成</summary>
    private void RegenerateOnSharedDiseaseFieldChanged()
    {
        if (_suppressReload) return;
        if (_reasonDirty) return;
        RegenerateDifficultyReason();
    }

    [ObservableProperty]
    private ObservableCollection<TempReliefDisease> _diseases = new();

    [ObservableProperty]
    private ObservableCollection<TempReliefAccident> _accidents = new();

    [ObservableProperty]
    private ObservableCollection<TempReliefEducation> _educations = new();

    partial void OnDifficultyTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsDiseaseType));
        OnPropertyChanged(nameof(IsAccidentType));
        OnPropertyChanged(nameof(IsEducationType));

        // 加载/初始化中（读草稿回显）不重置标志、不自动生成
        if (_suppressReload) return;

        // 困难类型变更：清空重置两个标志，恢复"未手动编辑"状态，按新类型重新生成救助原因
        _reasonDirty = false;
        _suppressReasonDirty = false;
        RegenerateDifficultyReason();
    }

    [RelayCommand]
    private void AddDisease()
    {
        if (Diseases.Count >= TempReliefConstants.MaxDetailRows)
        {
            SafeFireAndForget(async () => await _dialogService.DisplayAlertAsync("提示", $"最多添加 {TempReliefConstants.MaxDetailRows} 条疾病明细", "确定"));
            return;
        }
        var item = new TempReliefDisease
        {
            TreatStartDate = DateTime.Today,
            TreatEndDate = DateTime.Today
        };
        // 共享模式：新行直接以顶部共享值初始化（否则新行的默认"今天"与既有行的共享日期不同，
        // 保存时会被误判为行间差异，触发护栏拒绝回写并误切逐条模式）
        if (!IsMultiHospitalMode)
        {
            item.Hospital = Hospital;
            item.TreatStartDate = TreatStartDate;
            item.TreatEndDate = TreatEndDate;
            item.MedicalTotal = MedicalTotal;
            item.InsurancePaid = InsurancePaid;
            item.SelfPaid = SelfPaid;
        }
        item.PropertyChanged += OnDetailPropertyChanged;
        Diseases.Add(item);
        if (!_reasonDirty) RegenerateDifficultyReason();
    }

    [RelayCommand]
    private void RemoveDisease(TempReliefDisease? disease)
    {
        if (disease == null) return;
        disease.PropertyChanged -= OnDetailPropertyChanged;
        Diseases.Remove(disease);
        if (!_reasonDirty) RegenerateDifficultyReason();
    }

    [RelayCommand]
    private void AddAccident()
    {
        if (Accidents.Count >= TempReliefConstants.MaxDetailRows)
        {
            SafeFireAndForget(async () => await _dialogService.DisplayAlertAsync("提示", $"最多添加 {TempReliefConstants.MaxDetailRows} 条意外灾害明细", "确定"));
            return;
        }
        var item = new TempReliefAccident();
        item.PropertyChanged += OnDetailPropertyChanged;
        Accidents.Add(item);
        if (!_reasonDirty) RegenerateDifficultyReason();
    }

    [RelayCommand]
    private void RemoveAccident(TempReliefAccident? accident)
    {
        if (accident == null) return;
        accident.PropertyChanged -= OnDetailPropertyChanged;
        Accidents.Remove(accident);
        if (!_reasonDirty) RegenerateDifficultyReason();
    }

    [RelayCommand]
    private void AddEducation()
    {
        if (Educations.Count >= TempReliefConstants.MaxDetailRows)
        {
            SafeFireAndForget(async () => await _dialogService.DisplayAlertAsync("提示", $"最多添加 {TempReliefConstants.MaxDetailRows} 条教育支出明细", "确定"));
            return;
        }
        var item = new TempReliefEducation();
        item.PropertyChanged += OnDetailPropertyChanged;
        Educations.Add(item);
        if (!_reasonDirty) RegenerateDifficultyReason();
    }

    [RelayCommand]
    private void RemoveEducation(TempReliefEducation? education)
    {
        if (education == null) return;
        education.PropertyChanged -= OnDetailPropertyChanged;
        Educations.Remove(education);
        if (!_reasonDirty) RegenerateDifficultyReason();
    }

    /// <summary>
    /// 明细字段变化（如输入病名/金额等）→ 未手动编辑过救助原因时按最新明细重新生成
    /// </summary>
    private void OnDetailPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressReload) return;
        if (_reasonDirty) return;
        RegenerateDifficultyReason();
    }

    /// <summary>为明细项订阅字段变化监听（加载明细回显时用）</summary>
    private void SubscribeDetailChanges(object item)
    {
        switch (item)
        {
            case TempReliefDisease d: d.PropertyChanged += OnDetailPropertyChanged; break;
            case TempReliefAccident a: a.PropertyChanged += OnDetailPropertyChanged; break;
            case TempReliefEducation ed: ed.PropertyChanged += OnDetailPropertyChanged; break;
        }
    }

    [ObservableProperty]
    private string _policyEnjoyed = string.Empty;

    [ObservableProperty]
    private string _familyMemberStatus = string.Empty;

    [ObservableProperty]
    private string _reportUnit = string.Empty;

    [ObservableProperty]
    private DateTime? _reportTime;

    [ObservableProperty]
    private string _verifyResult = string.Empty;

    /// <summary>公示开始日期（DatePicker 绑定用，非空）</summary>
    [ObservableProperty]
    private DateTime _publicizeStartDateValue = DateTime.Today;

    /// <summary>公示结束日期（DatePicker 绑定用，非空）</summary>
    [ObservableProperty]
    private DateTime _publicizeEndDateValue = DateTime.Today;

    /// <summary>是否已填写公示日期（区分"未设置"与"默认今天"）</summary>
    [ObservableProperty]
    private bool _hasPublicizeDates;

    partial void OnPublicizeStartDateValueChanged(DateTime value)
    {
        if (_suppressReload) return;
        HasPublicizeDates = true;
    }

    partial void OnPublicizeEndDateValueChanged(DateTime value)
    {
        if (_suppressReload) return;
        HasPublicizeDates = true;
    }

    [ObservableProperty]
    private string _acceptancePerson = string.Empty;

    #endregion

    #region 家庭成员

    [ObservableProperty]
    private ObservableCollection<TempReliefMember> _members = new();

    /// <summary>
    /// 家庭人口来源快照（台账 guarantee_size / 档案 family_size / 已保存记录值）：
    /// 仅"有效家庭成员为 0"时作为兜底，避免单人户或纯台账记录被打成空/1。
    /// </summary>
    private int? _familySizeSeed;

    partial void OnMembersChanged(ObservableCollection<TempReliefMember> value)
    {
        // 集合整体替换时重挂事件（LoadAsync/选择候选人均会重建集合）
        if (value != null) value.CollectionChanged += OnMembersCollectionChanged;
        RebuildMemberNameOptions();
        RefreshApplicantFamilySize();
    }

    private void OnMembersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 逐行订阅姓名变更：成员姓名被就地改写（新增空行后补名字）也要刷新家庭人口
        if (e.NewItems != null)
            foreach (TempReliefMember m in e.NewItems) m.PropertyChanged += OnMemberPropertyChanged;
        if (e.OldItems != null)
            foreach (TempReliefMember m in e.OldItems) m.PropertyChanged -= OnMemberPropertyChanged;
        // Reset（Clear）拿不到旧项：旧成员对象随集合丢弃，其订阅一并被回收，无泄漏

        RebuildMemberNameOptions();
        RefreshApplicantFamilySize();
    }

    private void OnMemberPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TempReliefMember.MemberName))
            RefreshApplicantFamilySize();
    }

    /// <summary>
    /// 按统一口径重算"家庭人口"（户主 1 人 + 有效家庭成员行数，赡养抚养扶养义务人不计入；
    /// 口径唯一实现见 <see cref="TempReliefConstants.ResolveFamilySize"/>），并同步只读显示字段。
    /// </summary>
    /// <returns>重算后的家庭人口（保存时以此值落库）</returns>
    private int? RefreshApplicantFamilySize()
    {
        var effective = TempReliefConstants.CountEffectiveMembers(Members.Select(m => m.MemberName));
        var resolved = TempReliefConstants.ResolveFamilySize(_familySizeSeed, effective);
        if (ApplicantFamilySize != resolved)
        {
            if (MainThread.IsMainThread) ApplicantFamilySize = resolved;
            else MainThread.BeginInvokeOnMainThread(() => ApplicantFamilySize = resolved);
        }
        return resolved;
    }

    [RelayCommand]
    private void AddMember()
    {
        Members.Add(new TempReliefMember());
    }

    [RelayCommand]
    private void RemoveMember(TempReliefMember? member)
    {
        if (member == null) return;
        if (Members.Count <= 1)
        {
            SafeFireAndForget(async () => await _dialogService.DisplayAlertAsync("提示", "至少保留一名家庭成员", "确定"));
            return;
        }
        Members.Remove(member);
    }

    #endregion

    public TempReliefFormViewModel(
        ITempReliefService applicationService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        IDialogService dialogService)
    {
        _applicationService = applicationService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _dialogService = dialogService;
        Title = "临时救助申请";
        LoadIcd10Dictionary();

        // 字段初始化不经过属性 setter，OnMembersChanged 不会触发；
        // 此处显式挂集合订阅并首次重建候选，保证编辑/查看模式下成员选项可用
        Members.CollectionChanged += OnMembersCollectionChanged;
        RebuildMemberNameOptions();
        RefreshApplicantFamilySize();
    }

    /// <summary>
    /// 加载内置 ICD-10 疾病字典（icd10_common.json，随程序集分发），填充到 TempReliefDisease.CodeToNameMap；
    /// 字典加载已抽到共享助手 Icd10Catalog（临时救助与低收入申请表单共用一份）；
    /// 文件缺失/解析失败时保持空字典，不影响手动录入。
    /// </summary>
    private static void LoadIcd10Dictionary()
    {
        TempReliefDisease.CodeToNameMap = Icd10Catalog.CodeToNameMap;
    }

    partial void OnOperationModeChanged(FormOperationMode value)
    {
        OnPropertyChanged(nameof(IsCreateMode));
        OnPropertyChanged(nameof(IsViewMode));
        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(IsSavable));
        OnPropertyChanged(nameof(IsNewMode));
        OnPropertyChanged(nameof(IsSaveAndConfirmVisible));
    }

    public async Task InitializeAsync()
    {
        if (OperationMode == FormOperationMode.Create)
        {
            _id = 0;
            _suppressReload = true;
            ReliefType = TempReliefConstants.ReliefTypeLarge;
            ApplyDate = DateTime.Today;
            _suppressReload = false;
            ReportTime = DateTime.Now;
            await ApplyCreateDefaultsAsync();
        }
    }

    /// <summary>
    /// 新建模式默认值：填报单位=当前登录用户所属单位、乡镇验收人=本单位民政助理、
    /// 户籍地址=当前用户组织机构、公示日期=逐笔 5 天曲线（公示起=申请日次日、止=+3、验收=+4）、入户核实默认文本
    /// </summary>
    private async Task ApplyCreateDefaultsAsync()
    {
        ReportUnit = await GetReportUnitAsync(CancellationToken);
        AcceptancePerson = await GetCivilAssistantNameAsync(CancellationToken);
        await ResolveHukouFromOrganizationAsync(CancellationToken);
        await ApplyPublicizeDefaultsAsync();
        if (string.IsNullOrWhiteSpace(VerifyResult))
            VerifyResult = "已入户核实，申请人家庭实际情况与申报一致。";
    }

    /// <summary>
    /// 户籍地址默认值：从当前登录用户组织机构获取（省=黑龙江省，市/县区/镇社区=登录单位）
    /// </summary>
    private async Task ResolveHukouFromOrganizationAsync(CancellationToken ct)
    {
        var orgId = App.CurrentUserOrganizationId;
        if (!orgId.HasValue)
        {
            ClearHukou();
            return;
        }

        try
        {
            var orgService = _serviceProvider.GetRequiredService<NewCosmos.Services.Domain.UserManagement.IOrganizationService>();
            var result = await orgService.GetByIdAsync(orgId.Value, ct);
            if (result.IsFailure || result.Value == null)
            {
                ClearHukou();
                return;
            }

            var org = result.Value;
            HukouProvince = "黑龙江省";
            HukouCity = org.CityName ?? string.Empty;
            HukouDistrict = org.CountyName ?? string.Empty;
            HukouTown = org.TownName ?? string.Empty;
            ApplicantHukou = BuildHukouAddress();
        }
        catch (Exception ex)
        {
            _logger.Warn($"获取户籍地址默认值失败: {ex.Message}");
            ClearHukou();
        }
    }

    /// <summary>拼接户籍地址文本（省+市+县区+镇社区，空段跳过）</summary>
    private string BuildHukouAddress()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(HukouProvince)) parts.Add(HukouProvince);
        if (!string.IsNullOrWhiteSpace(HukouCity)) parts.Add(HukouCity);
        if (!string.IsNullOrWhiteSpace(HukouDistrict)) parts.Add(HukouDistrict);
        if (!string.IsNullOrWhiteSpace(HukouTown)) parts.Add(HukouTown);
        return string.Concat(parts);
    }

    /// <summary>
    /// 拼接家庭住址文本（落库 family_address；打印/月报「家庭住址」列直取本列）。
    /// 唯一拼装入口 <c>AddressResolver.BuildAddressFromText</c>（县+镇+村+门牌，自动去重、
    /// <c>-</c> 占位清空、<c>村民委员会/村委会</c>→<c>XX村</c>、<c>XX镇本级</c>→无村）。
    /// <para>2026-11-05 统一：原实现照抄来源台账原文（有省全量、有只到村、有「镇直」），
    /// 且改详细地址不回写本列；现改为保存时按分列重拼，与档案封面、文书直出同口径。</para>
    /// </summary>
    private string BuildFamilyAddress()
    {
        // 「村委会」是来源无村时的 UI 占位值（TempReliefConstants.DefaultVillage），按无村处理
        var village = string.IsNullOrWhiteSpace(ApplicantVillage)
            || ApplicantVillage == TempReliefConstants.DefaultVillage
            ? string.Empty
            : ApplicantVillage;
        return Helpers.AddressResolver.BuildAddressFromText(
            ApplicantDistrict, ApplicantTown, village, ApplicantDetailAddress);
    }

    private void ClearHukou()
    {
        HukouProvince = string.Empty;
        HukouCity = string.Empty;
        HukouDistrict = string.Empty;
        HukouTown = string.Empty;
        ApplicantHukou = string.Empty;
    }

    /// <summary>
    /// 从完整家庭住址提取村名之后的"最后一部分"作为详细地址；无村名标记或村名后无内容则留空
    /// 例："柳树镇万水村38号" → "38号"；"林口县柳树镇复兴村委会" → ""；"复兴村村民委员会" → ""（不残留"委会/民委员会"）
    /// 原子组 (?>...) 锁定完整村名（村委会/村民委员会/社区/村），防止回溯把村名拆残
    /// </summary>
    private static string ExtractDetailFromAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return string.Empty;
        var m = Regex.Match(address, @"(.+?(?>(?:村委会|村民委员会|社区|村)))([^村社区]+)$");
        return m.Success ? m.Groups[2].Value.Trim() : string.Empty;
    }

    /// <summary>
    /// 填报单位默认值：当前登录用户所属组织名称（单位全称，取不到时回退登录用户名）
    /// </summary>
    private async Task<string> GetReportUnitAsync(CancellationToken ct)
    {
        var orgId = App.CurrentUserOrganizationId;
        if (!orgId.HasValue)
            return string.IsNullOrWhiteSpace(App.CurrentUserName) ? string.Empty : App.CurrentUserName;

        try
        {
            var orgService = _serviceProvider.GetRequiredService<NewCosmos.Services.Domain.UserManagement.IOrganizationService>();
            var result = await orgService.GetByIdAsync(orgId.Value, ct);
            if (result.HasValue)
            {
                var org = result.Value;
                if (!string.IsNullOrWhiteSpace(org.Name)) return org.Name;
                if (!string.IsNullOrWhiteSpace(org.TownName)) return org.TownName;
                if (!string.IsNullOrWhiteSpace(org.CountyName)) return org.CountyName;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"获取填报单位默认值失败: {ex.Message}");
        }
        return string.IsNullOrWhiteSpace(App.CurrentUserName) ? string.Empty : App.CurrentUserName;
    }

    /// <summary>
    /// 乡镇验收人默认值：本单位职位为"民政助理"的用户姓名；未配置返回空（不写"未配置"占位）
    /// </summary>
    private async Task<string> GetCivilAssistantNameAsync(CancellationToken ct)
    {
        var orgId = App.CurrentUserOrganizationId;
        if (!orgId.HasValue) return string.Empty;

        try
        {
            var userService = _serviceProvider.GetRequiredService<NewCosmos.Services.Domain.UserManagement.IUserService>();
            var usersResult = await userService.GetByOrganizationAsync(orgId.Value);
            if (usersResult.IsFailure || usersResult.Value == null || usersResult.Value.Count == 0)
                return string.Empty;

            var civilAssistant = usersResult.Value.FirstOrDefault(u => u.Position == PositionTitles.CivilAssistant);
            return civilAssistant?.FullName ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Warn($"获取乡镇验收人默认值失败: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// C 线逐笔 5 天曲线与批次标签（新建/草稿场景，2026-11-13 起）：
    /// ① 申请日期先修正为「不晚于今日」（仅未来日期被拉回今天，不做回填钳制）；
    /// ② 曲线：公示起 = 申请日+1、公示止 = 申请日+3（恒 3 天）、验收日 = 申请日+4（自动推导，不手填）；
    /// ③ 批次仅作标签：申请日 ≤ 当月验收日(13号) 归本月，否则顺延下月（跨年接续），与公示/验收脱钩。
    /// 申请日期本身不改写。公示日期仅用于新建/草稿场景；编辑或查看已保存档案时保留库中原值、不重算。
    /// </summary>
    private async Task ApplyPublicizeDefaultsAsync()
    {
        try
        {
            // 小额快救：免公示（政策第二十一条），不生成公示起止
            if (ReliefType == TempReliefConstants.ReliefTypeSmall)
            {
                _suppressReload = true;
                try
                {
                    HasPublicizeDates = false;
                }
                finally
                {
                    _suppressReload = false;
                }
                return;
            }

            var generation = Interlocked.Increment(ref _publicizeCalcGeneration);
            var timelineService = _serviceProvider.GetRequiredService<IBusinessTimelineService>();

            // 申请日期自动修正：不晚于今日
            var corrected = ApplyDate == default || ApplyDate.Date > DateTime.Today ? DateTime.Today : ApplyDate.Date;
            // 批次标签（申请日 ≤ A 归本月）与逐笔曲线分开取：曲线才是公示/验收的唯一口径
            var batch = await timelineService.CalculateTempReliefForApplyDateAsync(corrected);
            var curve = timelineService.BuildTempReliefCurve(corrected);

            // 仅最新一次计算生效：连续触发（如快速连续修改申请日期）时丢弃过期结果
            if (generation != Volatile.Read(ref _publicizeCalcGeneration))
                return;

            _suppressReload = true;
            try
            {
                // 只回拉未来日期到今天；过去日期原样保留
                if (ApplyDate.Date != corrected)
                    ApplyDate = corrected;

                PublicizeStartDateValue = curve.PublicityStartDate;
                PublicizeEndDateValue = curve.PublicityEndDate;
                HasPublicizeDates = true;
                BatchHintText = BuildBatchHint(batch, curve);
            }
            finally
            {
                _suppressReload = false;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"生成公示日期默认值失败: {ex.Message}");
        }
    }

    /// <summary>归属批次提示文案：本次归属：{年}年{月}批次（公示 起~止，验收 逐笔验收日）</summary>
    private static string BuildBatchHint(TimelineResult batch, TimelineResult curve)
        => $"本次归属：{batch.Year}年{batch.Month}月批次" +
           $"（公示 {curve.PublicityStartDate:MM-dd}~{curve.PublicityEndDate:MM-dd}，验收 {curve.AuditDate:MM-dd}）";

}
