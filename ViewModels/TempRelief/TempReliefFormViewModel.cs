using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.TempRelief;
using NewCosmos.Services.Utilities;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

    #region 救助类型

    [ObservableProperty]
    private string _reliefType = TempReliefConstants.ReliefTypeLarge;

    public bool IsLargeType => ReliefType == TempReliefConstants.ReliefTypeLarge;
    public bool IsSmallType => ReliefType == TempReliefConstants.ReliefTypeSmall;

    partial void OnReliefTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsLargeType));
        OnPropertyChanged(nameof(IsSmallType));
        if (!_suppressReload && value == TempReliefConstants.ReliefTypeSmall)
        {
            SafeFireAndForget(async () => await LoadSmallAmountLevelsAsync());
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

    /// <summary>选中的小额档位金额</summary>
    [ObservableProperty]
    private string _confirmAmountDisplay = string.Empty;

    private decimal _confirmAmount;

    partial void OnSelectedSmallAmountLevelChanged(string value)
    {
        UpdateConfirmAmount();
    }

    private void UpdateConfirmAmount()
    {
        var level = SmallAmountLevels.FirstOrDefault(s => s.StandardName == SelectedSmallAmountLevel);
        if (level != null)
        {
            _confirmAmount = level.StandardValue;
            ConfirmAmountDisplay = $"{_confirmAmount:F2} 元";
        }
        else
        {
            _confirmAmount = 0m;
            ConfirmAmountDisplay = string.Empty;
        }
    }

    private List<ConfigStandard> SmallAmountLevels { get; set; } = new();

    private async Task LoadSmallAmountLevelsAsync()
    {
        var result = await _applicationService.GetSmallAmountLevelsAsync(CancellationToken);
        if (result.IsFailure || result.Value == null)
        {
            _logger.LogError(new Exception(result.Message ?? "未知错误"), "加载小额定额档位失败");
            return;
        }

        SmallAmountLevels = result.Value;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            SmallAmountLevelOptions.Clear();
            foreach (var level in SmallAmountLevels)
            {
                SmallAmountLevelOptions.Add(level.StandardName);
            }
            if (SmallAmountLevelOptions.Count > 0)
                SelectedSmallAmountLevel = SmallAmountLevelOptions[0];
        });
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
    private string _applicantAddress = string.Empty;

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

    private void RebuildMemberNameOptions()
    {
        var names = new List<string>();
        if (!string.IsNullOrWhiteSpace(ApplicantName)) names.Add(ApplicantName);
        foreach (var m in Members)
        {
            if (string.IsNullOrWhiteSpace(m.MemberName)) continue;
            if (!names.Contains(m.MemberName)) names.Add(m.MemberName);
        }
        MemberNameOptions.Clear();
        foreach (var n in names) MemberNameOptions.Add(n);
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

    /// <summary>申请日期变化 → 重算 C 类时间轴公示日期（固定每月10日~12日；调查截止（公示前一工作日）后即顺延下月）</summary>
    partial void OnApplyDateChanged(DateTime value)
    {
        if (_suppressReload) return;
        SafeFireAndForget(ApplyPublicizeDefaultsAsync);
    }

    /// <summary>家庭类别变化 → 重算 C 类时间轴（简化/普通程序影响调查核实工作日数）</summary>
    partial void OnApplicantFamilyCategoryChanged(string value)
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

    partial void OnHospitalChanged(string value) => RegenerateOnSharedDiseaseFieldChanged();

    /// <summary>治疗开始日期（疾病明细共享，区域顶部填写一次）</summary>
    [ObservableProperty]
    private DateTime _treatStartDate = DateTime.Today;

    partial void OnTreatStartDateChanged(DateTime value) => RegenerateOnSharedDiseaseFieldChanged();

    /// <summary>治疗结束日期（疾病明细共享，区域顶部填写一次）</summary>
    [ObservableProperty]
    private DateTime _treatEndDate = DateTime.Today;

    partial void OnTreatEndDateChanged(DateTime value) => RegenerateOnSharedDiseaseFieldChanged();

    /// <summary>医疗费用总额（元，疾病明细共享，区域顶部填写一次）</summary>
    [ObservableProperty]
    private decimal? _medicalTotal;

    partial void OnMedicalTotalChanged(decimal? value) => RegenerateOnSharedDiseaseFieldChanged();

    /// <summary>医保/其他报销金额（元，疾病明细共享，区域顶部填写一次）</summary>
    [ObservableProperty]
    private decimal? _insurancePaid;

    partial void OnInsurancePaidChanged(decimal? value) => RegenerateOnSharedDiseaseFieldChanged();

    /// <summary>个人自付费用（元，疾病明细共享，区域顶部填写一次）</summary>
    [ObservableProperty]
    private decimal? _selfPaid;

    partial void OnSelfPaidChanged(decimal? value) => RegenerateOnSharedDiseaseFieldChanged();

    /// <summary>
    /// 是否为"多家医院"模式：开启后每条疾病明细独立填写医院/日期/费用；关闭时使用顶部共享值。
    /// </summary>
    [ObservableProperty]
    private bool _isMultiHospitalMode;

    partial void OnIsMultiHospitalModeChanged(bool value)
    {
        if (_suppressReload) return;
        if (Diseases.Count == 0) return;

        if (!value)
        {
            // 从多医院 → 共享模式：用第一条记录的值回填共享字段
            var first = Diseases[0];
            Hospital = first.Hospital ?? string.Empty;
            TreatStartDate = first.TreatStartDate == DateTime.MinValue ? DateTime.Today : first.TreatStartDate;
            TreatEndDate = first.TreatEndDate == DateTime.MinValue ? DateTime.Today : first.TreatEndDate;
            MedicalTotal = first.MedicalTotal;
            InsurancePaid = first.InsurancePaid;
            SelfPaid = first.SelfPaid;
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

    partial void OnMembersChanged(ObservableCollection<TempReliefMember> value)
    {
        // 集合整体替换时重挂事件（LoadAsync/选择候选人均会重建集合）
        if (value != null) value.CollectionChanged += (_, __) => RebuildMemberNameOptions();
        RebuildMemberNameOptions();
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
        Members.CollectionChanged += (_, _) => RebuildMemberNameOptions();
        RebuildMemberNameOptions();
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
    /// 户籍地址=当前用户组织机构、公示日期=C线固定窗口每月10日~12日（调查截止后顺延下月）、入户核实默认文本
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
            if (result.IsSuccess && result.Value != null)
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

            var civilAssistant = usersResult.Value.FirstOrDefault(u => u.Position == "民政助理");
            return civilAssistant?.FullName ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Warn($"获取乡镇验收人默认值失败: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 公示日期默认值：按【申请日期】归属 C 线窗口（公示固定每月10日~12日）：
    /// 取第一个"申请日 ≤ 该窗口调查截止日"的月份（9号及以前归当月，10号起顺延下月），否则顺延；
    /// 低保等特殊群体调查期按5个工作日、普通按10个工作日。
    /// 申请日期约束进调查窗口：窗口已开启（起日 ≤ 今天）且申请日早于起日时上推到起日；
    /// 窗口未开启（起日为未来）保持原值，不写入未来日期；已在窗口内则不动。填报时间不参与计算。
    /// 公示日期仅用于新建/草稿场景；编辑或查看已保存档案时公示日期保留库中原值，不经此方法重算
    /// （申请日期的窗口约束见 LoadAsync 非草稿分支）。
    /// </summary>
    private async Task ApplyPublicizeDefaultsAsync()
    {
        try
        {
            var generation = Interlocked.Increment(ref _publicizeCalcGeneration);
            var timelineService = _serviceProvider.GetRequiredService<IBusinessTimelineService>();
            var applyDate = ApplyDate == default ? DateTime.Today : ApplyDate;
            var simplified = TempReliefConstants.IsSimplifiedProcedure(ApplicantFamilyCategory, _sourceTable);
            var timeline = await timelineService.CalculateTempReliefForApplyDateAsync(applyDate, simplified);

            // 仅最新一次计算生效：连续触发（如快速连续修改申请日期）时丢弃过期结果
            if (generation != Volatile.Read(ref _publicizeCalcGeneration))
                return;

            _suppressReload = true;
            try
            {
                PublicizeStartDateValue = timeline.PublicityStartDate;
                PublicizeEndDateValue = timeline.PublicityEndDate;
                HasPublicizeDates = true;

                ClampApplyDateIntoWindow(timeline);
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

    /// <summary>
    /// 申请日期约束进调查窗口：窗口已开启（起日 ≤ 今天）且申请日早于起日 → 上推到起日；
    /// 窗口未开启（起日为未来）→ 不推，避免写入未来日期；已在窗口内或晚于起日 → 不动。
    /// 上界不处理：窗口归属算法已保证申请日 ≤ 调查截止日。跨年同样生效
    /// （12月申请归属次年1月窗口时，由"起日 ≤ 今天"这道闸决定是否上推）。
    /// </summary>
    private void ClampApplyDateIntoWindow(TimelineResult timeline)
    {
        var start = timeline.InvestigationStartDate.Date;
        if (start > DateTime.Today) return;
        if (ApplyDate.Date >= start) return;
        ApplyDate = start;
    }

}
