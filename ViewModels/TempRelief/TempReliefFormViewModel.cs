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

    /// <summary>申请日期变化 → 重算 C 类时间轴公示日期（固定每月10日~12日，过12日滚下月）</summary>
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
    public bool IsDiseaseType => _difficultyType == TempReliefConstants.DifficultyTypeDisease;

    /// <summary>是否为意外灾害困难（控制意外灾害明细区显隐）</summary>
    public bool IsAccidentType => _difficultyType == TempReliefConstants.DifficultyTypeAccident;

    /// <summary>是否为教育支出困难（控制教育支出明细区显隐）</summary>
    public bool IsEducationType => _difficultyType == TempReliefConstants.DifficultyTypeEducation;

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
    /// 户籍地址=当前用户组织机构、公示日期=C线固定窗口每月10日~12日（过12日滚下月）、入户核实默认文本
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
    /// 取第一个"申请日 ≤ 该窗口调查核实起日"的月份，否则顺延；低保等特殊群体按5个工作日、普通按10个工作日。
    /// 仅用于新建/草稿场景；编辑或查看已保存档案时公示日期保留库中原值，不经此方法重算
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
    /// 加载已有申请（编辑/查看模式）
    /// </summary>
    public async Task LoadAsync(long id)
    {
        _id = id;
        _suppressReload = true;
        var isDraft = false;
        await ExecuteAsync(async () =>
        {
            var result = await _applicationService.GetByIdAsync(id, CancellationToken);
            if (result.IsFailure || result.Value == null)
                return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "申请记录不存在");

            var app = result.Value;
            isDraft = string.Equals(app.Status, TempReliefConstants.StatusDraft, StringComparison.Ordinal);
            ReliefType = app.ReliefType;
            _sourceTable = app.SourceTable;
            _sourceFamilyId = app.SourceFamilyId;
            _hukouType = app.HukouType;

            ApplicantName = app.ApplicantName;
            ApplicantIdCard = app.ApplicantIdCard;
            ApplicantGender = app.Gender;
            ApplicantAge = app.Age;
            ApplicantPhone = app.Phone;
            ApplicantProvince = app.FamilyProvince;
            ApplicantCity = app.FamilyCity;
            ApplicantDistrict = app.FamilyDistrict;
            ApplicantTown = string.IsNullOrWhiteSpace(app.FamilyTown) ? app.Town : app.FamilyTown;
            ApplicantVillage = string.IsNullOrWhiteSpace(app.Village)
                ? TempReliefConstants.DefaultVillage
                : app.Village;
            ApplicantDetailAddress = app.FamilyDetail;
            ApplicantAddress = app.FamilyAddress;
            HukouProvince = app.HukouProvince;
            HukouCity = app.HukouCity;
            HukouDistrict = app.HukouDistrict;
            HukouTown = app.HukouTown;
            if (!isDraft)
            {
                ApplicantHukou = app.HukouAddress;
            }
            ApplicantFamilySize = app.FamilySize;
            ApplicantFamilyCategory = TempReliefConstants.NormalizeFamilyCategory(app.FamilyCategory);
            BankName = app.BankName;
            BankAccount = app.BankAccount;

            // 草稿读入：申请日期自动重置为当前日期（与新建一致）
            ApplyDate = isDraft ? DateTime.Today : (app.ApplyDate ?? DateTime.Today);
            DifficultyReason = app.DifficultyReason;
            DifficultyType = string.IsNullOrWhiteSpace(app.DifficultyType)
                ? TempReliefConstants.DifficultyTypeOther
                : app.DifficultyType;
            PolicyEnjoyed = app.PolicyEnjoyed;
            FamilyMemberStatus = app.FamilyMemberStatus;
            ReportUnit = app.ReportUnit;
            ReportTime = app.ReportTime;
            VerifyResult = app.VerifyResult;
            PublicizeStartDateValue = app.PublicizeStartDate ?? DateTime.Today;
            PublicizeEndDateValue = app.PublicizeEndDate ?? DateTime.Today;
            HasPublicizeDates = app.PublicizeStartDate.HasValue || app.PublicizeEndDate.HasValue;
            AcceptancePerson = app.AcceptancePerson;

            // 救助对象快照回显（旧数据为空 → 稍后默认户主补全）
            BeneficiaryName = app.BeneficiaryName ?? string.Empty;
            BeneficiaryIdCard = app.BeneficiaryIdCard ?? string.Empty;
            BeneficiaryGender = app.BeneficiaryGender ?? string.Empty;
            BeneficiaryAge = app.BeneficiaryAge;
            BeneficiaryRelation = app.BeneficiaryRelation ?? string.Empty;

            if (OperationMode == FormOperationMode.Edit)
            {
                IsCandidateSelected = true;
                CandidateSummary = $"已选择：{app.ApplicantName}（{TempReliefConstants.GetSourceName(app.SourceType)}·{TempReliefConstants.GetFamilyCategoryByTable(app.SourceTable)}）";
            }

            await LoadMembersAsync(id);
            // 成员装载后立即重建患病/受灾成员候选：必须早于疾病/灾害明细装载，
            // 否则明细行 Picker 的 ItemsSource 为空，已保存的 member_name 无法回显
            RebuildMemberNameOptions();
            await LoadDifficultyDetailsAsync(id);

            // 成员已加载 → 构建救助对象选项（优先匹配已存身份证；空则默认户主并补全快照）
            RebuildBeneficiaryOptions(string.IsNullOrWhiteSpace(BeneficiaryIdCard) ? null : BeneficiaryIdCard);

            return Result.Success();
        }, "加载申请信息...");
        _suppressReload = false;

        // 读入已有申请：救助原因取保存值，视为"已有内容"，类型/明细变化前不自动覆盖
        _reasonDirty = true;

        // 草稿读入：户籍地址按当前用户组织机构重新规划，公示日期按 C 线固定窗口重算
        if (isDraft)
        {
            await ResolveHukouFromOrganizationAsync(CancellationToken);
            await ApplyPublicizeDefaultsAsync();
        }

        // 小额：加载档位并回显
        if (ReliefType == TempReliefConstants.ReliefTypeSmall)
        {
            await LoadSmallAmountLevelsAsync();
            var app0 = (await _applicationService.GetByIdAsync(id, CancellationToken)).Value;
            if (app0 != null && !string.IsNullOrEmpty(app0.SmallAmountLevel))
            {
                _suppressReload = true;
                SelectedSmallAmountLevel = app0.SmallAmountLevel;
                _confirmAmount = app0.ConfirmAmount ?? 0m;
                ConfirmAmountDisplay = _confirmAmount > 0 ? $"{_confirmAmount:F2} 元" : string.Empty;
                _suppressReload = false;
            }
        }
    }

    private async Task LoadMembersAsync(long id)
    {
        var membersResult = await _applicationService.GetMembersByApplicationIdAsync(id, CancellationToken);
        if (membersResult.IsFailure || membersResult.Value == null) return;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Members.Clear();
            foreach (var m in membersResult.Value)
            {
                if (IsApplicantItself(m)) continue;
                Members.Add(m);
            }
            if (Members.Count == 0)
            {
                Members.Add(new TempReliefMember());
            }
        });
    }

    /// <summary>
    /// 判定成员是否为户主本人（申请人）：身份证匹配优先，缺失时以关系+姓名兜底
    /// </summary>
    private bool IsApplicantItself(TempReliefMember m)
    {
        if (string.IsNullOrWhiteSpace(m.IdCard) && string.IsNullOrWhiteSpace(ApplicantIdCard))
        {
            return !string.IsNullOrWhiteSpace(m.Relation)
                && TempReliefConstants.GetRelationshipDisplayName(m.Relation) == TempReliefConstants.RelationHead
                && string.Equals(m.MemberName?.Trim(), ApplicantName?.Trim(), StringComparison.Ordinal);
        }
        return !string.IsNullOrWhiteSpace(m.IdCard)
            && string.Equals(m.IdCard.Trim(), ApplicantIdCard?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>同上，针对候选成员快照（TempReliefMemberSnapshot）</summary>
    private bool IsApplicantItself(TempReliefMemberSnapshot m)
    {
        if (string.IsNullOrWhiteSpace(m.IdCard) && string.IsNullOrWhiteSpace(ApplicantIdCard))
        {
            return !string.IsNullOrWhiteSpace(m.Relation)
                && TempReliefConstants.GetRelationshipDisplayName(m.Relation) == TempReliefConstants.RelationHead
                && string.Equals(m.MemberName?.Trim(), ApplicantName?.Trim(), StringComparison.Ordinal);
        }
        return !string.IsNullOrWhiteSpace(m.IdCard)
            && string.Equals(m.IdCard.Trim(), ApplicantIdCard?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoadDifficultyDetailsAsync(long id)
    {
        var diseases = await _applicationService.GetDiseasesByApplicationIdAsync(id, CancellationToken);
        var accidents = await _applicationService.GetAccidentsByApplicationIdAsync(id, CancellationToken);
        var educations = await _applicationService.GetEducationsByApplicationIdAsync(id, CancellationToken);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Diseases.Clear();
            if (diseases.IsSuccess && diseases.Value != null)
                foreach (var d in diseases.Value) { SubscribeDetailChanges(d); Diseases.Add(d); }

            // 检测多医院模式：如果各条疾病记录的 Hospital 不全相同，则自动开启
            if (diseases.IsSuccess && diseases.Value != null && diseases.Value.Count > 1)
            {
                var distinctHospitals = diseases.Value
                    .Where(d => !string.IsNullOrWhiteSpace(d.Hospital))
                    .Select(d => d.Hospital!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                IsMultiHospitalMode = distinctHospitals.Count > 1;
            }
            else
            {
                IsMultiHospitalMode = false;
            }

            if (IsMultiHospitalMode)
            {
                // 多医院模式：每条记录各自保留值，共享字段留空
                Hospital = string.Empty;
            }
            else
            {
                // 共享模式：回填第一条非空记录到顶部共享字段
                Hospital = diseases.IsSuccess && diseases.Value != null
                    ? diseases.Value.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Hospital))?.Hospital ?? string.Empty
                    : string.Empty;

                if (diseases.IsSuccess && diseases.Value != null)
                {
                    var first = diseases.Value.FirstOrDefault();
                    if (first != null)
                    {
                        TreatStartDate = first.TreatStartDate == DateTime.MinValue ? DateTime.Today : first.TreatStartDate;
                        TreatEndDate = first.TreatEndDate == DateTime.MinValue ? DateTime.Today : first.TreatEndDate;
                        MedicalTotal = first.MedicalTotal;
                        InsurancePaid = first.InsurancePaid;
                        SelfPaid = first.SelfPaid;
                    }
                }
            }

            Accidents.Clear();
            if (accidents.IsSuccess && accidents.Value != null)
                foreach (var a in accidents.Value) { SubscribeDetailChanges(a); Accidents.Add(a); }

            Educations.Clear();
            if (educations.IsSuccess && educations.Value != null)
                foreach (var e in educations.Value) { SubscribeDetailChanges(e); Educations.Add(e); }
        });
    }

    [RelayCommand]
    private async Task SearchCandidatesAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchKeyword))
        {
            await _dialogService.DisplayAlertAsync("提示", "请输入姓名或身份证号进行检索", "确定");
            return;
        }

        if (IsBusy)
        {
            _logger.Warn("检索候选人被 IsBusy 占用拦截");
            await _dialogService.DisplayAlertAsync("提示", "正在处理其他操作，请稍候再试", "确定");
            return;
        }

        var kw = SearchKeyword.Trim();
        _logger.Info($"检索候选人开始: keyword={kw}");

        await ExecuteAsync(async () =>
        {
            var result = await _applicationService.SearchCandidatesAsync(kw, null, CancellationToken);
            if (result.IsFailure || result.Value == null)
            {
                _logger.Warn($"检索候选人失败: {result.ErrorCode} {result.Message}");
                await _dialogService.DisplayAlertAsync("提示", result.Message ?? "检索失败", "确定");
                return Result.Failure(ErrorCodes.NOT_FOUND, result.Message ?? "检索失败");
            }

            var list = result.Value;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Candidates.Clear();
                foreach (var c in list)
                {
                    Candidates.Add(c);
                }
            });

            _logger.Info($"检索候选人完成: keyword={kw}, 命中={list.Count}");
            if (list.Count == 0)
            {
                SafeFireAndForget(async () => await _dialogService.DisplayAlertAsync("提示", "未检索到符合条件的申请人", "确定"));
            }
            return Result.Success();
        }, "检索申请人...");
    }

    [RelayCommand]
    private async Task SelectCandidateAsync()
    {
        if (SelectedCandidate == null) return;

        if (SelectedCandidate.HasAppliedThisYear)
        {
            await _dialogService.DisplayAlertAsync("年度限制", $"该申请人本年度已申请过临时救助，每年仅限一次", "确定");
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _applicationService.GetCandidateDetailAsync(
                SelectedCandidate.SourceTable, SelectedCandidate.SourceFamilyId, null, CancellationToken);
            if (result.IsFailure || result.Value == null)
                return Result.Failure(ErrorCodes.NOT_FOUND, result.Message ?? "获取申请人详情失败");

            var c = result.Value;
            if (c.HasAppliedThisYear)
            {
                return Result.Failure(ErrorCodes.ANNUAL_LIMIT_EXCEEDED, $"该申请人本年度已申请过临时救助，每年仅限一次");
            }

            _sourceTable = c.SourceTable;
            _sourceFamilyId = c.SourceFamilyId;
            _hukouType = c.HukouType;

            ApplicantName = c.Name;
            ApplicantIdCard = c.IdCard;
            ApplicantGender = c.Gender;
            ApplicantPhone = c.Phone;
            ApplicantProvince = c.Province;
            ApplicantCity = c.City;
            ApplicantDistrict = c.District;
            ApplicantTown = c.Town;
            ApplicantVillage = string.IsNullOrWhiteSpace(c.Village)
                ? TempReliefConstants.DefaultVillage
                : c.Village;
            // 详细地址：从家庭住址（完整地址）提取村名之后的最后一部分，无则空
            ApplicantDetailAddress = ExtractDetailFromAddress(c.FamilyAddress);
            // 家庭住址文本保留来源原始完整地址（打印模板用，避免与分列拼接重复）
            ApplicantAddress = c.FamilyAddress;
            // 户籍地址统一从当前用户组织机构获取（新建/草稿时已回填，候选人不覆盖）
            ApplicantFamilySize = c.FamilySize;
            ApplicantFamilyCategory = TempReliefConstants.NormalizeFamilyCategory(c.FamilyCategory);
            // 家庭类别确定后重算公示窗口（特殊群体5个工作日/普通10个工作日会影响归属月份）
            await ApplyPublicizeDefaultsAsync();
            // 银行信息：来源台账预填（一卡通账号优先，其次银行卡号；开户行取 bank_name）
            BankName = c.BankName;
            BankAccount = string.IsNullOrWhiteSpace(c.OneCardAccount) ? c.BankAccount : c.OneCardAccount;

            var age = Helpers.IdCardValidator.ExtractAge(c.IdCard);
            if (age.HasValue)
                ApplicantAge = age.Value;

            // 选择新申请人：视为新表单，重置救助原因标志、困难类型与明细（避免沿用上一位申请人数据）
            _reasonDirty = false;
            _suppressReasonDirty = false;
            DifficultyType = TempReliefConstants.DifficultyTypeOther;
            Diseases.Clear();
            Accidents.Clear();
            Educations.Clear();
            // 救助原因：按家庭类别生成默认文本（其他困难类型）
            RegenerateDifficultyReason();

            if (string.IsNullOrWhiteSpace(PolicyEnjoyed))
            {
                var policyResult = await _applicationService.GetPolicySnapshotAsync(c.IdCard, CancellationToken);
                if (policyResult.IsSuccess && policyResult.Value is { Count: > 0 })
                    PolicyEnjoyed = string.Join("；", policyResult.Value) + "。";
            }

            if (string.IsNullOrWhiteSpace(FamilyMemberStatus))
            {
                var statusResult = await _applicationService.GetFamilyMemberStatusAsync(c.IdCard, CancellationToken);
                if (statusResult.IsSuccess && !string.IsNullOrWhiteSpace(statusResult.Value))
                    FamilyMemberStatus = statusResult.Value;
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                Members.Clear();
                if (c.Members != null && c.Members.Count > 0)
                {
                    foreach (var m in c.Members)
                    {
                        if (IsApplicantItself(m)) continue;
                        Members.Add(new TempReliefMember
                        {
                            MemberName = m.MemberName,
                            Gender = m.Gender,
                            Relation = m.Relation,
                            IdCard = m.IdCard,
                            WorkUnit = m.WorkUnit,
                            AnnualIncome = m.AnnualIncome
                        });
                    }
                }
                if (Members.Count == 0)
                {
                    Members.Add(new TempReliefMember());
                }

                // 救助对象：默认户主（申请人），可在申请信息区改选家庭成员
                RebuildBeneficiaryOptions();
                RebuildMemberNameOptions();

                IsCandidateSelected = true;
                CandidateSummary = $"已选择：{c.Name}（{c.SourceName}）";
                Candidates.Clear();
            });

            _logger.LogBusiness("选择临时救助申请人",
                ("SourceTable", c.SourceTable), ("SourceFamilyId", c.SourceFamilyId),
                ("Name", Services.Core.DataMasker.MaskName(c.Name)));
            return Result.Success();
        }, "选择申请人...");
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var validation = Validate();
        if (validation != null)
        {
            await _dialogService.DisplayAlertAsync("提示", validation, "确定");
            return;
        }

        await ExecuteAsync(async () =>
        {
            // 保存兜底：未手动编辑过救助原因时按当前困难类型与明细重新生成
            if (!_reasonDirty) RegenerateDifficultyReason();
            var app = BuildApplication();

            if (OperationMode == FormOperationMode.Create)
            {
                var result = await _applicationService.CreateAsync(app, Members.ToList(), CancellationToken);
                if (!result.IsSuccess)
                {
                    await _dialogService.DisplayAlertAsync("保存失败", result.Message ?? "保存失败，请重试", "确定");
                    return result;
                }
                _id = result.Value;
            }
            else
            {
                var result = await _applicationService.UpdateAsync(app, Members.ToList(), CancellationToken);
                if (!result.IsSuccess)
                {
                    await _dialogService.DisplayAlertAsync("保存失败", result.Message ?? "保存失败，请重试", "确定");
                    return result;
                }
            }

            _logger.LogBusiness("临时救助申请保存成功", ("ApplicationId", _id));
            await _dialogService.ShowSnackBarAsync("保存成功");
            await CloseAsync();
            return Result.Success();
        }, "保存申请...");
    }

    [RelayCommand]
    private async Task SaveAndConfirmAsync()
    {
        if (!IsSavable) return;

        var validation = Validate();
        if (validation != null)
        {
            await _dialogService.DisplayAlertAsync("提示", validation, "确定");
            return;
        }

        await ExecuteAsync(async () =>
        {
            // 保存兜底：未手动编辑过救助原因时按当前困难类型与明细重新生成
            if (!_reasonDirty) RegenerateDifficultyReason();
            var app = BuildApplication();

            if (OperationMode == FormOperationMode.Create)
            {
                var result = await _applicationService.CreateAsync(app, Members.ToList(), CancellationToken);
                if (!result.IsSuccess)
                {
                    await _dialogService.DisplayAlertAsync("保存失败", result.Message ?? "保存失败，请重试", "确定");
                    return result;
                }
                _id = result.Value;
            }
            else
            {
                var result = await _applicationService.UpdateAsync(app, Members.ToList(), CancellationToken);
                if (!result.IsSuccess)
                {
                    await _dialogService.DisplayAlertAsync("保存失败", result.Message ?? "保存失败，请重试", "确定");
                    return result;
                }
            }

            var confirmResult = await _applicationService.ConfirmAsync(_id, App.CurrentUserName, CancellationToken);
            if (!confirmResult.IsSuccess)
            {
                await _dialogService.DisplayAlertAsync("确认失败", confirmResult.Message ?? "确认失败，请重试", "确定");
                return confirmResult;
            }

            _logger.LogBusiness("临时救助申请保存并确认成功", ("ApplicationId", _id));
            await _dialogService.ShowSnackBarAsync("保存并确认成功");

            // 保存确认成功 → 跳转档案输出页（复用 ArchiveOutputPage）
            var savedResult = await _applicationService.GetByIdAsync(_id, CancellationToken);
            if (savedResult.IsSuccess && savedResult.Value != null)
            {
                var savedApp = savedResult.Value;

                // 加载困难明细子表（GetByIdAsync 仅主表，打印需携带明细）
                var diseaseResult = await _applicationService.GetDiseasesByApplicationIdAsync(_id, CancellationToken);
                if (diseaseResult.IsSuccess && diseaseResult.Value != null)
                    savedApp.Diseases = diseaseResult.Value;
                var accidentResult = await _applicationService.GetAccidentsByApplicationIdAsync(_id, CancellationToken);
                if (accidentResult.IsSuccess && accidentResult.Value != null)
                    savedApp.Accidents = accidentResult.Value;
                var educationResult = await _applicationService.GetEducationsByApplicationIdAsync(_id, CancellationToken);
                if (educationResult.IsSuccess && educationResult.Value != null)
                    savedApp.Educations = educationResult.Value;

                var membersResult = await _applicationService.GetMembersByApplicationIdAsync(_id, CancellationToken);
                var memberList = membersResult.IsSuccess ? membersResult.Value ?? new() : new List<TempReliefMember>();
                var contactUnitPhone = (await _applicationService.GetReportUnitPhoneAsync(savedApp.ReportUnit, CancellationToken)).Value ?? string.Empty;
                var timelineService = _serviceProvider.GetRequiredService<IBusinessTimelineService>();
                var (acceptanceDate, investigationDate) = await TempReliefPrintDataBuilder.ResolveScheduleAsync(timelineService, savedApp);
                var fields = TempReliefPrintDataBuilder.BuildSingleFields(
                    savedApp, memberList, contactUnitPhone, acceptanceDate, investigationDate);

                PrintNavigationData.BusinessType = TempReliefConstants.BusinessType;
                PrintNavigationData.BusinessId = _id;
                PrintNavigationData.Classification = savedApp.ReliefType;
                PrintNavigationData.FieldData = fields;
                PrintNavigationData.TableData = new();
                PrintNavigationData.SupporterTableData = null;

                await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveOutputPage>();
            }
            else
            {
                await CloseAsync();
            }
            return Result.Success();
        }, "保存并确认...");
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await CloseAsync();
    }

    /// <summary>
    /// 救助原因默认文本：以家庭类别填空（类别来自来源表/低保认定分类；取不到时用"困难群众"）
    /// </summary>
    private static string BuildDefaultDifficultyReason(string? familyCategory)
    {
        var category = string.IsNullOrWhiteSpace(familyCategory) ? "困难群众" : familyCategory;
        return $"申请人家庭为{category}对象，因突发困难导致基本生活暂时陷入困境，特申请临时救助。";
    }

    /// <summary>
    /// 救助原因自动生成入口：抑制 dirty 标记后按当前困难类型与明细重新生成
    /// </summary>
    private void RegenerateDifficultyReason()
    {
        _suppressReasonDirty = true;
        DifficultyReason = BuildDifficultyReasonText();
        _suppressReasonDirty = false;
    }

    /// <summary>
    /// 按困难类型及其明细生成救助原因文本；
    /// 疾病/意外灾害/教育支出类型在明细未填时也生成体现该类型的特征文本（切换类型立即可见变化）
    /// </summary>
    private string BuildDifficultyReasonText()
    {
        switch (DifficultyType)
        {
            case TempReliefConstants.DifficultyTypeDisease:
                var diseaseText = BuildDiseaseReasonText();
                if (!string.IsNullOrWhiteSpace(diseaseText)) return diseaseText;
                return "申请人家庭成员因患疾病，医疗费用支出较大，造成家庭基本生活暂时陷入困境，特申请临时救助。";
            case TempReliefConstants.DifficultyTypeAccident:
                var accidentText = BuildAccidentReasonText();
                if (!string.IsNullOrWhiteSpace(accidentText)) return accidentText;
                return "申请人家庭成员因意外灾害，家庭基本生活暂时陷入困境，特申请临时救助。";
            case TempReliefConstants.DifficultyTypeEducation:
                var educationText = BuildEducationReasonText();
                if (!string.IsNullOrWhiteSpace(educationText)) return educationText;
                return "申请人家庭成员因家庭子女就学，教育支出较大，造成家庭基本生活暂时陷入困境，特申请临时救助。";
        }
        return BuildDefaultDifficultyReason(ApplicantFamilyCategory);
    }

    /// <summary>
    /// 疾病救助原因（详细）：逐条列病名与编码；共享模式取顶部共享值，多医院模式逐条取记录值
    /// </summary>
    private string BuildDiseaseReasonText()
    {
        var diseases = Diseases
            .Where(d => !string.IsNullOrWhiteSpace(d.DiseaseName) || !string.IsNullOrWhiteSpace(d.DiseaseCode))
            .ToList();
        if (diseases.Count == 0) return string.Empty;

        var name = string.IsNullOrWhiteSpace(ApplicantName) ? "家庭成员" : ApplicantName;

        // 病名（含编码），多条时逐条列举；条目归属成员姓名有值时前置
        var diseaseText = diseases.Count == 1
            ? OwnerPrefix(diseases[0].MemberName) + diseases[0].DiseaseName
                + (string.IsNullOrWhiteSpace(diseases[0].DiseaseCode) ? "" : $"（ICD-10编码：{diseases[0].DiseaseCode}）")
            : "以下疾病：" + string.Join("；", diseases.Select((d, i) =>
                $"{i + 1}、{OwnerPrefix(d.MemberName)}{d.DiseaseName}" + (string.IsNullOrWhiteSpace(d.DiseaseCode) ? "" : $"（编码{d.DiseaseCode}）")));

        if (IsMultiHospitalMode)
        {
            // 多医院模式：每条疾病与其医院/日期/费用合并为一条，编号 1、2… 逐条列出，避免两套编号混淆
            var segments = diseases.Select((d, i) =>
            {
                var diseasePart = OwnerPrefix(d.MemberName) + d.DiseaseName
                    + (string.IsNullOrWhiteSpace(d.DiseaseCode) ? "" : $"（编码{d.DiseaseCode}）");
                var hospital = string.IsNullOrWhiteSpace(d.Hospital) ? "医院" : d.Hospital;
                var date = FormatTreatRange(d.TreatStartDate, d.TreatEndDate);
                var cost = BuildSingleDiseaseCostText(d);
                var seg = string.IsNullOrWhiteSpace(date)
                    ? $"于{hospital}住院治疗"
                    : $"于{date}在{hospital}住院治疗";
                if (!string.IsNullOrWhiteSpace(cost)) seg += $"，{cost}";
                return $"{diseasePart}，{seg}";
            }).ToList();

            var body = segments.Count == 1
                ? $"因患{segments[0]}"
                : "因患以下疾病：" + string.Join("；", segments.Select((s, i) => $"{i + 1}、{s}"));

            return $"申请人家庭成员{name}{body}，医疗费用支出较大，造成家庭基本生活暂时陷入困境，特申请临时救助。";
        }
        else
        {
            // 共享模式：取顶部共享值
            var dateText = FormatTreatRange(TreatStartDate, TreatEndDate);
            var hospitalText = string.IsNullOrWhiteSpace(Hospital) ? "医院" : Hospital;

            var body = string.IsNullOrWhiteSpace(dateText)
                ? $"于{hospitalText}住院治疗"
                : $"于{dateText}在{hospitalText}住院治疗";

            var costText = BuildDiseaseCostText();
            var tail = string.IsNullOrWhiteSpace(costText)
                ? "，医疗费用支出较大，造成家庭基本生活暂时陷入困境，特申请临时救助。"
                : $"，{costText}，个人自付费用较高，造成家庭基本生活暂时陷入困境，特申请临时救助。";

            return $"申请人家庭成员{name}因患{diseaseText}，{body}{tail}";
        }
    }

    /// <summary>
    /// 疾病费用构成文本（共享值，缺省段省略）；均未填时返回空串
    /// </summary>
    private string BuildDiseaseCostText()
    {
        var parts = new List<string>();
        if (MedicalTotal.HasValue) parts.Add($"本次医疗费用总额{MedicalTotal.Value:F2}元");
        if (InsurancePaid.HasValue) parts.Add($"经基本医疗保险及大病保险等报销{InsurancePaid.Value:F2}元");
        if (SelfPaid.HasValue) parts.Add($"个人自付{SelfPaid.Value:F2}元");
        return string.Join("，", parts);
    }

    /// <summary>单条疾病费用构成文本（多医院模式逐条用）</summary>
    private static string BuildSingleDiseaseCostText(TempReliefDisease d)
    {
        var parts = new List<string>();
        if (d.MedicalTotal.HasValue) parts.Add($"费用总额{d.MedicalTotal.Value:F2}元");
        if (d.InsurancePaid.HasValue) parts.Add($"报销{d.InsurancePaid.Value:F2}元");
        if (d.SelfPaid.HasValue) parts.Add($"自付{d.SelfPaid.Value:F2}元");
        return string.Join("，", parts);
    }

    /// <summary>明细条目归属成员姓名前缀（有值时"姓名 "，否则空）</summary>
    private static string OwnerPrefix(string? memberName)
        => string.IsNullOrWhiteSpace(memberName) ? "" : memberName.Trim() + " ";

    /// <summary>
    /// 意外灾害救助原因：按明细逐条列类型/时间/地点/伤亡/损失赔偿
    /// </summary>
    private string BuildAccidentReasonText()
    {
        var accidents = Accidents
            .Where(a => !string.IsNullOrWhiteSpace(a.AccidentType)
                || a.HappenDate != DateTime.MinValue
                || !string.IsNullOrWhiteSpace(a.HappenPlace)
                || !string.IsNullOrWhiteSpace(a.InjurySituation))
            .ToList();
        if (accidents.Count == 0) return string.Empty;

        var name = string.IsNullOrWhiteSpace(ApplicantName) ? "家庭成员" : ApplicantName;
        var items = accidents.Select(a =>
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(a.AccidentType)) parts.Add(OwnerPrefix(a.MemberName) + a.AccidentType);
            else if (!string.IsNullOrWhiteSpace(a.MemberName)) parts.Add(a.MemberName);
            if (a.HappenDate != DateTime.MinValue) parts.Add(a.HappenDate.ToString("yyyy年M月d日"));
            if (!string.IsNullOrWhiteSpace(a.HappenPlace)) parts.Add(a.HappenPlace);
            var seg = string.Join("，", parts);
            if (!string.IsNullOrWhiteSpace(a.InjurySituation)) seg += $"（{a.InjurySituation}）";
            var lossParts = new List<string>();
            if (a.PropertyLoss.HasValue) lossParts.Add($"财产损失{a.PropertyLoss.Value:F2}元");
            if (a.CompensationPaid.HasValue) lossParts.Add($"已获保险及赔偿{a.CompensationPaid.Value:F2}元");
            if (lossParts.Count > 0) seg += "，" + string.Join("，", lossParts);
            return seg;
        }).ToList();

        var body = items.Count == 1
            ? $"因{items[0]}"
            : "因发生以下意外灾害：" + string.Join("；", items.Select((s, i) => $"{i + 1}、{s}"));

        return $"申请人家庭成员{name}{body}，家庭基本生活暂时陷入困境，特申请临时救助。";
    }

    /// <summary>
    /// 教育支出救助原因：按明细逐条列学生/阶段/学校/学费
    /// </summary>
    private string BuildEducationReasonText()
    {
        var educations = Educations
            .Where(e => !string.IsNullOrWhiteSpace(e.StudentName)
                || !string.IsNullOrWhiteSpace(e.EducationStage)
                || !string.IsNullOrWhiteSpace(e.SchoolName)
                || e.TuitionFee.HasValue)
            .ToList();
        if (educations.Count == 0) return string.Empty;

        var name = string.IsNullOrWhiteSpace(ApplicantName) ? "家庭成员" : ApplicantName;
        var items = educations.Select(e =>
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(e.StudentName)) parts.Add(e.StudentName);
            if (!string.IsNullOrWhiteSpace(e.EducationStage)) parts.Add($"就读{e.EducationStage}");
            if (!string.IsNullOrWhiteSpace(e.SchoolName)) parts.Add(e.SchoolName);
            if (!string.IsNullOrWhiteSpace(e.SchoolDurationDisplay)) parts.Add(e.SchoolDurationDisplay);
            var text = string.Join("，", parts);
            if (e.TuitionFee.HasValue) text += $"（学费{e.TuitionFee.Value:F2}元）";
            return text;
        }).ToList();

        var body = items.Count == 1
            ? $"因家庭子女就学，{items[0]}"
            : "因家庭子女就学，教育支出明细如下：" + string.Join("；", items.Select((s, i) => $"{i + 1}、{s}"));

        return $"申请人家庭成员{name}{body}，学费等教育支出较大，家庭基本生活暂时陷入困境，特申请临时救助。";
    }

    /// <summary>
    /// 治疗起止日期区间文本；均未填返回空串；起止同日时仅显示单日
    /// </summary>
    private static string FormatTreatRange(DateTime start, DateTime end)
    {
        if (start == DateTime.MinValue && end == DateTime.MinValue) return string.Empty;
        if (start == DateTime.MinValue) return end.ToString("yyyy年M月d日");
        if (end == DateTime.MinValue) return start.ToString("yyyy年M月d日");
        if (start.Date == end.Date) return start.ToString("yyyy年M月d日");
        return $"{start:yyyy年M月d日}至{end:yyyy年M月d日}";
    }

    private string? Validate()
    {
        if (!IsCandidateSelected && string.IsNullOrEmpty(ApplicantName))
            return "请先从导入台账或低保申请库中选择申请人";

        if (string.IsNullOrEmpty(ApplicantIdCard))
            return "申请人身份证号缺失";

        if (string.IsNullOrWhiteSpace(BeneficiaryIdCard) || string.IsNullOrWhiteSpace(BeneficiaryName))
            return "请选择申请成员作为救助对象";

        if (string.IsNullOrWhiteSpace(DifficultyReason))
            return "请填写申请救助原因/困难情况";

        if (DifficultyType == TempReliefConstants.DifficultyTypeDisease)
        {
            if (Diseases.Count == 0)
                return "困难类型为疾病，请至少添加一条疾病明细";
            if (Diseases.Any(d => string.IsNullOrWhiteSpace(d.DiseaseName) && string.IsNullOrWhiteSpace(d.DiseaseCode)))
                return "请填写每条疾病明细的诊断结果或疾病编码";
            if (IsMultiHospitalMode)
            {
                // 多医院模式：每条须填医院，每条自付不能为负
                if (Diseases.Any(d => string.IsNullOrWhiteSpace(d.Hospital)))
                    return "多医院模式下，请为每条疾病明细填写所在医院";
                if (Diseases.Any(d => d.SelfPaid.HasValue && d.SelfPaid.Value < 0))
                    return "自费费用不能为负数";
            }
            else
            {
                // 共享模式：原有验证
                if (SelfPaid.HasValue && SelfPaid.Value < 0)
                    return "自费费用不能为负数";
            }
        }

        if (DifficultyType == TempReliefConstants.DifficultyTypeAccident && Accidents.Count == 0)
            return "困难类型为意外灾害，请至少添加一条意外灾害明细";

        if (DifficultyType == TempReliefConstants.DifficultyTypeEducation && Educations.Count == 0)
            return "困难类型为教育支出，请至少添加一条教育支出明细";

        if (ReliefType == TempReliefConstants.ReliefTypeSmall && string.IsNullOrEmpty(SelectedSmallAmountLevel))
            return "小额快速救助请选择定额档位";

        if (HasPublicizeDates && PublicizeEndDateValue < PublicizeStartDateValue)
            return "公示结束日期不能早于开始日期";

        return null;
    }

    private TempReliefApplication BuildApplication()
    {
        var app = new TempReliefApplication
        {
            Id = _id,
            ReliefType = ReliefType,
            SourceType = TempReliefConstants.IsAllowedSourceTable(_sourceTable) && _sourceTable == "nc_biz_applications"
                ? TempReliefConstants.SourceApplication
                : TempReliefConstants.SourceImportTab,
            SourceTable = _sourceTable,
            SourceFamilyId = _sourceFamilyId,
            HukouType = _hukouType,
            ApplicantName = ApplicantName,
            ApplicantIdCard = ApplicantIdCard,
            Gender = ApplicantGender,
            Age = ApplicantAge,
            Phone = ApplicantPhone,
            FamilyProvince = ApplicantProvince,
            FamilyCity = ApplicantCity,
            FamilyDistrict = ApplicantDistrict,
            FamilyTown = ApplicantTown,
            FamilyDetail = ApplicantDetailAddress,
            FamilyAddress = ApplicantAddress,
            HukouProvince = HukouProvince,
            HukouCity = HukouCity,
            HukouDistrict = HukouDistrict,
            HukouTown = HukouTown,
            HukouAddress = BuildHukouAddress(),
            Town = ApplicantTown,
            Village = ApplicantVillage,
            FamilySize = ApplicantFamilySize,
            FamilyCategory = ApplicantFamilyCategory,
            BankName = BankName?.Trim() ?? string.Empty,
            BankAccount = BankAccount?.Trim() ?? string.Empty,
            PolicyEnjoyed = PolicyEnjoyed,
            FamilyMemberStatus = FamilyMemberStatus,
            DifficultyReason = DifficultyReason,
            DifficultyType = string.IsNullOrWhiteSpace(DifficultyType)
                ? TempReliefConstants.DifficultyTypeOther
                : DifficultyType,
            ApplyDate = ApplyDate,
            Diseases = Diseases.ToList(),
            Accidents = Accidents.ToList(),
            Educations = Educations.ToList(),
            ReportUnit = ReportUnit,
            ReportTime = ReportTime,
            VerifyResult = VerifyResult,
            PublicizeStartDate = HasPublicizeDates ? PublicizeStartDateValue : null,
            PublicizeEndDate = HasPublicizeDates ? PublicizeEndDateValue : null,
            AcceptancePerson = AcceptancePerson,
            BeneficiaryName = BeneficiaryName,
            BeneficiaryIdCard = BeneficiaryIdCard,
            BeneficiaryGender = BeneficiaryGender,
            BeneficiaryAge = BeneficiaryAge,
            BeneficiaryRelation = BeneficiaryRelation
        };

        // 医院为明细区顶部共享值：仅共享模式保存前写入每条疾病明细
        if (!IsMultiHospitalMode)
        {
            foreach (var d in app.Diseases)
            {
                d.Hospital = Hospital;
            }

            // 治疗起止/费用/报销/自付为明细区顶部共享值：保存前写入每条疾病明细
            foreach (var d in app.Diseases)
            {
                d.TreatStartDate = TreatStartDate;
                d.TreatEndDate = TreatEndDate;
                d.MedicalTotal = MedicalTotal;
                d.InsurancePaid = InsurancePaid;
                d.SelfPaid = SelfPaid;
            }
        }
        // 多医院模式下，每条记录已各自填写，无需覆盖

        if (ReliefType == TempReliefConstants.ReliefTypeSmall)
        {
            app.SmallAmountLevel = SelectedSmallAmountLevel;
            app.ConfirmAmount = _confirmAmount > 0 ? _confirmAmount : null;
        }
        else
        {
            // 大额救助金额不填写
            app.SmallAmountLevel = string.Empty;
            app.ConfirmAmount = null;
        }

        return app;
    }

    private async Task CloseAsync()
    {
        await Helpers.WindowNavigator.CurrentPage!.Navigation.PopAsync();
        RestoreWindowTitleFromNavigation();
    }
}
