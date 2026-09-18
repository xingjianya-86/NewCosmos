using CommunityToolkit.Mvvm.ComponentModel;
using NewCosmos.Constants;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 临时救助申请实体（对应 nc_biz_temp_relief_applications 表）
/// </summary>
public class TempReliefApplication
{
    public long Id { get; set; }

    /// <summary>申请编号（LZ+yyyyMMdd+seq:D4）</summary>
    public string ApplicationNo { get; set; } = string.Empty;

    /// <summary>救助类型：Large大额 / Small小额</summary>
    public string ReliefType { get; set; } = TempReliefConstants.ReliefTypeLarge;

    /// <summary>申请年份（由申请日期派生，年度唯一约束用）</summary>
    public string ApplyYear { get; set; } = string.Empty;

    /// <summary>申请人来源：ImportTab / Application</summary>
    public string SourceType { get; set; } = TempReliefConstants.SourceImportTab;

    /// <summary>来源表名</summary>
    public string SourceTable { get; set; } = string.Empty;

    /// <summary>来源家庭/申请记录ID</summary>
    public long SourceFamilyId { get; set; }

    /// <summary>户籍类型快照（农村/城镇；rural/urban 台账按表名固定，其余取来源表 hukou_type 列）</summary>
    public string HukouType { get; set; } = string.Empty;

    /// <summary>申请人姓名</summary>
    public string ApplicantName { get; set; } = string.Empty;

    /// <summary>申请人身份证号</summary>
    public string ApplicantIdCard { get; set; } = string.Empty;

    /// <summary>性别</summary>
    public string Gender { get; set; } = string.Empty;

    /// <summary>年龄</summary>
    public int? Age { get; set; }

    /// <summary>联系电话</summary>
    public string Phone { get; set; } = string.Empty;

    // ── 救助对象快照（从家庭成员中选定；户主恒为 applicant_*，不得变更） ──
    /// <summary>救助对象姓名（空时视为申请人/户主本人）</summary>
    public string? BeneficiaryName { get; set; }

    /// <summary>救助对象身份证号</summary>
    public string? BeneficiaryIdCard { get; set; }

    /// <summary>救助对象性别</summary>
    public string? BeneficiaryGender { get; set; }

    /// <summary>救助对象年龄</summary>
    public int? BeneficiaryAge { get; set; }

    /// <summary>救助对象与户主关系</summary>
    public string? BeneficiaryRelation { get; set; }

    /// <summary>家庭住址（省+市+县区+镇社区+详细地址拼接）</summary>
    public string FamilyAddress { get; set; } = string.Empty;

    /// <summary>家庭住址-省</summary>
    public string FamilyProvince { get; set; } = string.Empty;

    /// <summary>家庭住址-市</summary>
    public string FamilyCity { get; set; } = string.Empty;

    /// <summary>家庭住址-县区</summary>
    public string FamilyDistrict { get; set; } = string.Empty;

    /// <summary>家庭住址-镇社区</summary>
    public string FamilyTown { get; set; } = string.Empty;

    /// <summary>家庭住址-详细地址</summary>
    public string FamilyDetail { get; set; } = string.Empty;

    /// <summary>户籍所在地（省+市+县区+镇社区拼接）</summary>
    public string HukouAddress { get; set; } = string.Empty;

    /// <summary>户籍所在地-省</summary>
    public string HukouProvince { get; set; } = string.Empty;

    /// <summary>户籍所在地-市</summary>
    public string HukouCity { get; set; } = string.Empty;

    /// <summary>户籍所在地-县区</summary>
    public string HukouDistrict { get; set; } = string.Empty;

    /// <summary>户籍所在地-镇社区</summary>
    public string HukouTown { get; set; } = string.Empty;

    /// <summary>所在镇/街道（公示表用）</summary>
    public string Town { get; set; } = string.Empty;

    /// <summary>所在村/社区（公示表用）</summary>
    public string Village { get; set; } = string.Empty;

    /// <summary>家庭共同生活成员数</summary>
    public int? FamilySize { get; set; }

    /// <summary>家庭类别（低保/特困/边缘等）</summary>
    public string FamilyCategory { get; set; } = string.Empty;

    /// <summary>开户行（银行卡开户行）</summary>
    public string BankName { get; set; } = string.Empty;

    /// <summary>银行卡号/一卡通账号（来源台账预填；汇总表"一卡通账号"取此列）</summary>
    public string BankAccount { get; set; } = string.Empty;

    /// <summary>享受的政策救助情况</summary>
    public string PolicyEnjoyed { get; set; } = string.Empty;

    /// <summary>家庭成员状态</summary>
    public string FamilyMemberStatus { get; set; } = string.Empty;

    /// <summary>申请救助原因/困难情况</summary>
    public string DifficultyReason { get; set; } = string.Empty;

    /// <summary>主困难类型（疾病/意外灾害/教育支出/其他困难，存中文）</summary>
    public string DifficultyType { get; set; } = string.Empty;

    /// <summary>疾病明细（困难类型为疾病时，可多条）</summary>
    public List<TempReliefDisease> Diseases { get; set; } = new();

    /// <summary>意外灾害明细（困难类型为意外灾害时，可多条）</summary>
    public List<TempReliefAccident> Accidents { get; set; } = new();

    /// <summary>教育支出明细（困难类型为教育支出时，可多条）</summary>
    public List<TempReliefEducation> Educations { get; set; } = new();

    /// <summary>申请日期</summary>
    public DateTime? ApplyDate { get; set; }

    /// <summary>填报单位</summary>
    public string ReportUnit { get; set; } = string.Empty;

    /// <summary>填报时间</summary>
    public DateTime? ReportTime { get; set; }

    /// <summary>小额所选定额档位名（冗余）</summary>
    public string SmallAmountLevel { get; set; } = string.Empty;

    /// <summary>确定金额（小额=档位金额；大额为空）</summary>
    public decimal? ConfirmAmount { get; set; }

    /// <summary>公示开始日期</summary>
    public DateTime? PublicizeStartDate { get; set; }

    /// <summary>公示结束日期</summary>
    public DateTime? PublicizeEndDate { get; set; }

    /// <summary>入户核实情况</summary>
    public string VerifyResult { get; set; } = string.Empty;

    /// <summary>乡镇验收人（民政助理）</summary>
    public string AcceptancePerson { get; set; } = string.Empty;

    /// <summary>状态（Draft/Confirmed/Stopped）</summary>
    public string Status { get; set; } = TempReliefConstants.StatusDraft;

    public DateTime? ConfirmedAt { get; set; }
    public string ConfirmedBy { get; set; } = string.Empty;
    public DateTime? StoppedAt { get; set; }
    public string StoppedBy { get; set; } = string.Empty;
    public string StopReason { get; set; } = string.Empty;

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime? DeletedAt { get; set; }

    /// <summary>救助类型中文名（界面绑定）</summary>
    public string ReliefTypeName => TempReliefConstants.GetReliefTypeName(ReliefType);

    /// <summary>状态中文名（界面绑定）</summary>
    public string StatusName => TempReliefConstants.GetStatusName(Status);

    /// <summary>来源中文名（界面绑定）</summary>
    public string SourceName => TempReliefConstants.GetSourceName(SourceType);

    /// <summary>确定金额显示（小额展示，如 1000元）</summary>
    public string ConfirmAmountDisplay => ConfirmAmount.HasValue ? $"{ConfirmAmount.Value:F2}元" : string.Empty;

    /// <summary>公示日期区间显示（2026-01-01 至 2026-01-07）</summary>
    public string PublicizeRangeDisplay
    {
        get
        {
            if (PublicizeStartDate == null && PublicizeEndDate == null) return string.Empty;
            var start = PublicizeStartDate?.ToString("yyyy-MM-dd") ?? string.Empty;
            var end = PublicizeEndDate?.ToString("yyyy-MM-dd") ?? string.Empty;
            if (string.IsNullOrEmpty(start) && string.IsNullOrEmpty(end)) return string.Empty;
            if (string.IsNullOrEmpty(end)) return start;
            return $"{start} 至 {end}";
        }
    }
}

/// <summary>
/// 临时救助家庭成员明细实体（对应 nc_biz_temp_relief_members 表）
/// </summary>
public class TempReliefMember
{
    public long Id { get; set; }

    /// <summary>关联临时救助申请ID</summary>
    public long ApplicationId { get; set; }

    public string MemberName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;

    /// <summary>与户主关系</summary>
    public string Relation { get; set; } = string.Empty;

    public string IdCard { get; set; } = string.Empty;

    /// <summary>工作单位</summary>
    public string WorkUnit { get; set; } = string.Empty;

    /// <summary>年收入（元）</summary>
    public decimal? AnnualIncome { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>
/// 临时救助申请人候选（跨导入台账库 + 低保申请库检索）
/// </summary>
public class TempReliefCandidate
{
    /// <summary>来源家庭/申请记录ID</summary>
    public long SourceFamilyId { get; set; }

    /// <summary>来源表名</summary>
    public string SourceTable { get; set; } = string.Empty;

    /// <summary>户籍类型快照（农村/城镇；rural/urban 台账按表名固定，其余取来源表 hukou_type 列）</summary>
    public string HukouType { get; set; } = string.Empty;

    /// <summary>户主/申请人姓名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>身份证号</summary>
    public string IdCard { get; set; } = string.Empty;

    /// <summary>性别</summary>
    public string Gender { get; set; } = string.Empty;

    /// <summary>联系电话</summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>家庭住址</summary>
    public string FamilyAddress { get; set; } = string.Empty;

    /// <summary>家庭住址-省</summary>
    public string Province { get; set; } = string.Empty;

    /// <summary>家庭住址-市</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>家庭住址-县区</summary>
    public string District { get; set; } = string.Empty;

    /// <summary>家庭住址-详细地址（来源表 address 列/门牌等）</summary>
    public string DetailAddress { get; set; } = string.Empty;

    /// <summary>户籍地址（来源表 hukou_address；缺失时以家庭住址兜底）</summary>
    public string HukouAddress { get; set; } = string.Empty;

    /// <summary>所在镇/街道</summary>
    public string Town { get; set; } = string.Empty;

    /// <summary>所在村/社区</summary>
    public string Village { get; set; } = string.Empty;

    /// <summary>家庭人口</summary>
    public int? FamilySize { get; set; }

    /// <summary>
    /// 家庭类别（默认由来源表推导；低保申请库来源可被认定分类覆盖，如 农村低保/特困分散供养）
    /// </summary>
    private string? _familyCategory;

    public string FamilyCategory
    {
        get => string.IsNullOrEmpty(_familyCategory)
            ? TempReliefConstants.GetFamilyCategoryByTable(SourceTable)
            : _familyCategory;
        set => _familyCategory = value;
    }

    /// <summary>来源中文名</summary>
    public string SourceName => TempReliefConstants.GetSourceNameByTable(SourceTable);

    /// <summary>开户行（银行卡开户行；来源台账预填）</summary>
    public string BankName { get; set; } = string.Empty;

    /// <summary>银行卡号（来源台账预填）</summary>
    public string BankAccount { get; set; } = string.Empty;

    /// <summary>一卡通账号（来源台账预填；预填银行卡号时优先）</summary>
    public string OneCardAccount { get; set; } = string.Empty;

    /// <summary>家庭成员（快照：姓名；审批表成员行用）</summary>
    public List<TempReliefMemberSnapshot> Members { get; set; } = new();

    /// <summary>当年是否已申请（禁选标记）</summary>
    public bool HasAppliedThisYear { get; set; }
}

/// <summary>
/// 候选人家庭成员快照（审批表 5 行成员用）
/// </summary>
public class TempReliefMemberSnapshot
{
    public string MemberName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string Relation { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string WorkUnit { get; set; } = string.Empty;
    public decimal? AnnualIncome { get; set; }
}

/// <summary>
/// 疾病明细实体（对应 nc_biz_temp_relief_diseases 表，困难类型=疾病时可多条）
/// </summary>
public partial class TempReliefDisease : ObservableObject
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }

    /// <summary>
    /// ICD-10 编码 → 疾病名称 内置字典（由 ViewModel 加载 icd10_common.json 后填充；空字典不影响手动录入）
    /// </summary>
    public static IReadOnlyDictionary<string, string> CodeToNameMap { get; set; } =
        new Dictionary<string, string>();

    /// <summary>疾病诊断结果</summary>
    [ObservableProperty]
    private string _diseaseName = string.Empty;

    [ObservableProperty]
    private string _diseaseCode = string.Empty;

    /// <summary>患病成员姓名（该条疾病归属的家庭成员；空表示未指定）</summary>
    [ObservableProperty]
    private string _memberName = string.Empty;

    /// <summary>疾病编码变更时，命中内置字典自动回填疾病诊断结果</summary>
    partial void OnDiseaseCodeChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var key = value.Trim().ToUpperInvariant();
        if (CodeToNameMap.TryGetValue(key, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            DiseaseName = name;
        }
    }

    /// <summary>所在医院</summary>
    [ObservableProperty]
    private string _hospital = string.Empty;

    /// <summary>治疗开始日期（未填为默认值）</summary>
    [ObservableProperty]
    private DateTime _treatStartDate = DateTime.MinValue;

    /// <summary>治疗结束日期（未填为默认值）</summary>
    [ObservableProperty]
    private DateTime _treatEndDate = DateTime.MinValue;

    /// <summary>医疗费用总额（元）</summary>
    [ObservableProperty]
    private decimal? _medicalTotal;

    /// <summary>医保/其他报销金额（元）</summary>
    [ObservableProperty]
    private decimal? _insurancePaid;

    /// <summary>个人自付费用（元）</summary>
    [ObservableProperty]
    private decimal? _selfPaid;

    public int SortOrder { get; set; }
}

/// <summary>
/// 意外灾害明细实体（对应 nc_biz_temp_relief_accidents 表，困难类型=意外灾害时可多条）
/// </summary>
public partial class TempReliefAccident : ObservableObject
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }

    /// <summary>意外/灾害类型（火灾/交通事故/溺水/人身伤害/其他意外）</summary>
    [ObservableProperty]
    private string _accidentType = string.Empty;

    /// <summary>受灾成员姓名（该条灾害归属的家庭成员；空表示未指定）</summary>
    [ObservableProperty]
    private string _memberName = string.Empty;

    /// <summary>发生时间（未填为默认值）</summary>
    [ObservableProperty]
    private DateTime _happenDate = DateTime.MinValue;

    /// <summary>发生地点</summary>
    [ObservableProperty]
    private string _happenPlace = string.Empty;

    /// <summary>人身伤害/伤残情况</summary>
    [ObservableProperty]
    private string _injurySituation = string.Empty;

    /// <summary>财产损失金额（元）</summary>
    [ObservableProperty]
    private decimal? _propertyLoss;

    /// <summary>已获保险/赔偿金额（元）</summary>
    [ObservableProperty]
    private decimal? _compensationPaid;

    /// <summary>责任认定情况（交警/消防认定、第三方责任）</summary>
    [ObservableProperty]
    private string _responsibilityDesc = string.Empty;

    /// <summary>佐证材料说明（公安/消防/交警证明等）</summary>
    [ObservableProperty]
    private string _materialDesc = string.Empty;

    public int SortOrder { get; set; }
}

/// <summary>
/// 教育支出明细实体（对应 nc_biz_temp_relief_educations 表，困难类型=教育支出时可多条）
/// </summary>
public partial class TempReliefEducation : ObservableObject
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }

    /// <summary>就学家庭成员姓名</summary>
    [ObservableProperty]
    private string _studentName = string.Empty;

    /// <summary>就学阶段（学前教育/小学/初中/高中/中职/高职/大学）</summary>
    [ObservableProperty]
    private string _educationStage = string.Empty;

    /// <summary>就读学校</summary>
    [ObservableProperty]
    private string _schoolName = string.Empty;

    /// <summary>学制：3=三年制/4=四年制/5=五年制</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SchoolDurationDisplay))]
    private int? _schoolDuration;

    /// <summary>学制显示文本（Picker 双向绑定用，落库存数值）</summary>
    public string SchoolDurationDisplay
    {
        get => _schoolDuration switch
        {
            3 => "三年制",
            4 => "四年制",
            5 => "五年制",
            _ => string.Empty
        };
        set
        {
            _schoolDuration = value switch
            {
                "三年制" => 3,
                "四年制" => 4,
                "五年制" => 5,
                _ => null
            };
        }
    }

    /// <summary>学费/住宿费（扣除教育救助后自付，元）</summary>
    [ObservableProperty]
    private decimal? _tuitionFee;

    /// <summary>缴费时间（默认本日）</summary>
    [ObservableProperty]
    private DateTime _feeDate = DateTime.Today;

    public int SortOrder { get; set; }
}
