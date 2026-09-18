namespace NewCosmos.Models.Requests;

/// <summary>
/// 申请创建请求
/// </summary>
public class ApplicationCreateRequest
{
    public string ApplicantName { get; set; } = string.Empty;
    public string ApplicantIdCard { get; set; } = string.Empty;
    public string ApplicantPhone { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string Ethnicity { get; set; } = string.Empty;
    public string MaritalStatus { get; set; } = string.Empty;
    public string HukouType { get; set; } = string.Empty;
    public string EducationLevel { get; set; } = string.Empty;
    public string PoliticalStatus { get; set; } = string.Empty;
    public string HukouAddress { get; set; } = string.Empty;
    public string DisabilityCardNo { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string Town { get; set; } = string.Empty;
    public string Community { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PhysicalCondition { get; set; } = string.Empty;
    public string DiseaseName { get; set; } = string.Empty;
    public string SecondaryDiseaseName { get; set; } = string.Empty;
    public string DiseaseCode { get; set; } = string.Empty;
    public bool IsSevereDisease { get; set; }
    public string DisabilityType { get; set; } = string.Empty;
    public string DisabilityLevel { get; set; } = string.Empty;
    public string HealthStatus { get; set; } = string.Empty;
    public string EmploymentStatus { get; set; } = string.Empty;
    public string WorkUnit { get; set; } = string.Empty;
    public string IncomeSource { get; set; } = string.Empty;
    public decimal AnnualIncome { get; set; }
    public int FamilySize { get; set; }
    public string ApplicationReason { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// 申请更新请求
/// </summary>
public class ApplicationUpdateRequest
{
    public long ApplicationId { get; set; }
    public string ApplicantName { get; set; } = string.Empty;
    public string ApplicantIdCard { get; set; } = string.Empty;
    public string ApplicantPhone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Town { get; set; } = string.Empty;
    public string Village { get; set; } = string.Empty;
    public decimal AnnualIncome { get; set; }
    public int FamilySize { get; set; }
    public string Remarks { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string Gender { get; set; } = string.Empty;
    public string Ethnicity { get; set; } = string.Empty;
    public string MaritalStatus { get; set; } = string.Empty;
    public string HukouType { get; set; } = string.Empty;
    public string EducationLevel { get; set; } = string.Empty;
    public string PoliticalStatus { get; set; } = string.Empty;
    public string HukouAddress { get; set; } = string.Empty;
    public string DisabilityCardNo { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string Community { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string BankAccount { get; set; } = string.Empty;
    public string DiseaseName { get; set; } = string.Empty;
    public string DisabilityType { get; set; } = string.Empty;
    public string DisabilityLevel { get; set; } = string.Empty;
    public string HealthStatus { get; set; } = string.Empty;
    public string ApplicationReason { get; set; } = string.Empty;
    public string ApplicationReasonDetail { get; set; } = string.Empty;
    public decimal WorkIncomeTotal { get; set; }
    public decimal BusinessIncomeTotal { get; set; }
    public decimal PropertyIncomeTotal { get; set; }
    public decimal TransferIncomeTotal { get; set; }
    public decimal OtherIncomeTotal { get; set; }
    public decimal AlimonyIncome { get; set; }
    public decimal PerCapitaIncome { get; set; }
    public decimal TotalAnnualIncome { get; set; }
    public decimal PerCapitaAnnualIncome { get; set; }
    public decimal RigidExpenditure { get; set; }
    public decimal FamilyLandArea { get; set; }
    public decimal SelfFarmedLandArea { get; set; }
    public decimal SubleasedLandArea { get; set; }
    public decimal ContractedLandArea { get; set; }
    public decimal LandIncomeTotal { get; set; }
    public decimal SubsidyTotal { get; set; }
    public bool IsEligible { get; set; }
    public string? ClassificationResult { get; set; } = string.Empty;
    public string ClassifiedSubsidyType { get; set; } = string.Empty;
    public decimal ClassifiedSubsidyAmount { get; set; }
    public decimal HouseholdMonthlyGuaranteeAmount { get; set; }
    public decimal CaregiverSubsidyAmount { get; set; }
    public decimal TotalGuaranteeAmount { get; set; }
    public bool IsInGracePeriod { get; set; }
    public int? GracePeriodMonths { get; set; }
    public DateTime? GracePeriodStartDate { get; set; }
    public DateTime? GracePeriodEndDate { get; set; }
    public string? OriginalClassificationResult { get; set; } = string.Empty;
    public decimal? OriginalGuaranteeAmount { get; set; }
    public string CaregiverType { get; set; } = string.Empty;
    public string DestituteSupportType { get; set; } = string.Empty;

    // ── 补齐 UPDATE 缺失字段（与 CreateAsync INSERT 列对齐，避免编辑保存时字段丢失）──
    public int? CityId { get; set; }
    public int? CountyId { get; set; }
    public int? TownId { get; set; }
    public int? VillageId { get; set; }
    public int? HukouCityId { get; set; }
    public int? HukouCountyId { get; set; }
    public int? HukouTownId { get; set; }
    public int? HukouVillageId { get; set; }
    public string PhysicalCondition { get; set; } = string.Empty;
    public string SecondaryDiseaseName { get; set; } = string.Empty;
    public string DiseaseCode { get; set; } = string.Empty;
    public bool IsSevereDisease { get; set; }
    public string EmploymentStatus { get; set; } = string.Empty;
    public string WorkUnit { get; set; } = string.Empty;
    public string IncomeSource { get; set; } = string.Empty;
    public int ConfirmedFamilySize { get; set; }
    public bool IsSingleRescue { get; set; }
    public string SupportMode { get; set; } = string.Empty;
    public long SupportInstitutionId { get; set; }
    public decimal PersonCategoryProtectionTotalAmount { get; set; }
    public int CurrentStep { get; set; } = 1;

    /// <summary>是否通过一事一议豁免</summary>
    public bool IsSpecialApproval { get; set; }

    /// <summary>关联 nc_biz_special_approvals.id（会议审议结果）</summary>
    public long? SpecialApprovalId { get; set; }

    /// <summary>
    /// 乐观并发令牌：调用方加载记录时的 updated_at。
    /// null = 旧调用方未携带令牌，跳过并发检查；非 null 时 UPDATE 仅在
    /// updated_at 未被他人修改的情况下生效（0 行受影响 → CONCURRENCY_CONFLICT）。
    /// </summary>
    public DateTime? LoadedUpdatedAt { get; set; }
}

/// <summary>
/// 申请提交请求
/// </summary>
public class ApplicationSubmitRequest
{
    public long ApplicationId { get; set; }
    public string SubmittedBy { get; set; } = string.Empty;
}

/// <summary>
/// 申请审批请求
/// </summary>
public class ApplicationApproveRequest
{
    public long ApplicationId { get; set; }
    public bool Approved { get; set; }
    public string ApprovalComment { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = string.Empty;
}

/// <summary>
/// 家庭成员创建请求
/// </summary>
public class FamilyMemberCreateRequest
{
    public long ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string Relation { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public int? Age { get; set; }
    public DateTime? BirthDate { get; set; }
    public string Ethnicity { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string HukouType { get; set; } = string.Empty;
    public string HukouAddress { get; set; } = string.Empty;
    public string HomeProvince { get; set; } = string.Empty;
    public string HomeCity { get; set; } = string.Empty;
    public string HomeDistrict { get; set; } = string.Empty;
    public string HomeTown { get; set; } = string.Empty;
    public string HomeVillage { get; set; } = string.Empty;
    public string HomeAddress { get; set; } = string.Empty;
    public string HukouProvince { get; set; } = string.Empty;
    public string HukouCity { get; set; } = string.Empty;
    public string HukouDistrict { get; set; } = string.Empty;
    public string HukouTown { get; set; } = string.Empty;
    public string MaritalStatus { get; set; } = string.Empty;
    public string EducationLevel { get; set; } = string.Empty;
    public string PoliticalStatus { get; set; } = string.Empty;
    public string HealthStatus { get; set; } = string.Empty;
    public bool HasSevereIllness { get; set; }
    public bool HasDisability { get; set; }
    public string DisabilityType { get; set; } = string.Empty;
    public string DisabilityLevel { get; set; } = string.Empty;
    public string DisabilityCertificateNo { get; set; } = string.Empty;
    public string DiseaseCategory { get; set; } = string.Empty;
    public string DiseaseName { get; set; } = string.Empty;
    public string SecondaryDiseaseName { get; set; } = string.Empty;
    public string DiseaseCode { get; set; } = string.Empty;
    public bool IsSevereDisease { get; set; }

    /// <summary>免于劳动力判定（因照顾本户重病/重残亲属）</summary>
    public bool IsLaborExempt { get; set; }

    public bool IsHouseholdHead { get; set; }
    public string MemberCategory { get; set; } = string.Empty;
    public string EmploymentStatus { get; set; } = string.Empty;
    public string WorkUnit { get; set; } = string.Empty;
    public string MainIncomeSource { get; set; } = string.Empty;
    public decimal AnnualIncome { get; set; }

    /// <summary>劳动能力</summary>
    public string WorkCapacity { get; set; } = string.Empty;

    /// <summary>月收入（元）</summary>
    public decimal MonthlyIncomeCapacity { get; set; }

    /// <summary>家庭人口（赡养人自家人口）</summary>
    public int? FamilySize { get; set; }

    /// <summary>赡养人：人员类型（赡养/抚养/扶养）</summary>
    public string PersonType { get; set; } = string.Empty;

    /// <summary>赡养人：年赡养费（元）</summary>
    public decimal AnnualSupportFee { get; set; }

    /// <summary>赡养人：月赡养费（元）</summary>
    public decimal MonthlySupportFee { get; set; }

    /// <summary>赡养人：赡养月数</summary>
    public int? SupportMonths { get; set; }

    /// <summary>赡养人：是否有赡养能力</summary>
    public bool IsSupportAbility { get; set; } = true;

    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// 家庭成员更新请求
/// </summary>
public class FamilyMemberUpdateRequest
{
    public long MemberId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string Relation { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public int? Age { get; set; }
    public DateTime? BirthDate { get; set; }
    public string Ethnicity { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string HukouType { get; set; } = string.Empty;
    public string HukouAddress { get; set; } = string.Empty;
    public string HomeProvince { get; set; } = string.Empty;
    public string HomeCity { get; set; } = string.Empty;
    public string HomeDistrict { get; set; } = string.Empty;
    public string HomeTown { get; set; } = string.Empty;
    public string HomeVillage { get; set; } = string.Empty;
    public string HomeAddress { get; set; } = string.Empty;
    public string HukouProvince { get; set; } = string.Empty;
    public string HukouCity { get; set; } = string.Empty;
    public string HukouDistrict { get; set; } = string.Empty;
    public string HukouTown { get; set; } = string.Empty;
    public string MaritalStatus { get; set; } = string.Empty;
    public string EducationLevel { get; set; } = string.Empty;
    public string PoliticalStatus { get; set; } = string.Empty;
    public string HealthStatus { get; set; } = string.Empty;
    public bool HasSevereIllness { get; set; }
    public bool HasDisability { get; set; }
    public string DisabilityType { get; set; } = string.Empty;
    public string DisabilityLevel { get; set; } = string.Empty;
    public string DisabilityCertificateNo { get; set; } = string.Empty;
    public string DiseaseCategory { get; set; } = string.Empty;
    public string DiseaseName { get; set; } = string.Empty;
    public string SecondaryDiseaseName { get; set; } = string.Empty;
    public string DiseaseCode { get; set; } = string.Empty;
    public bool IsSevereDisease { get; set; }

    /// <summary>免于劳动力判定（因照顾本户重病/重残亲属）</summary>
    public bool IsLaborExempt { get; set; }

    public bool IsHouseholdHead { get; set; }
    public string MemberCategory { get; set; } = string.Empty;
    public string EmploymentStatus { get; set; } = string.Empty;
    public string WorkUnit { get; set; } = string.Empty;
    public string MainIncomeSource { get; set; } = string.Empty;
    public decimal AnnualIncome { get; set; }

    /// <summary>劳动能力</summary>
    public string WorkCapacity { get; set; } = string.Empty;

    /// <summary>月收入（元）</summary>
    public decimal MonthlyIncomeCapacity { get; set; }

    /// <summary>家庭人口（赡养人自家人口）</summary>
    public int? FamilySize { get; set; }

    /// <summary>赡养人：人员类型（赡养/抚养/扶养）</summary>
    public string PersonType { get; set; } = string.Empty;

    /// <summary>赡养人：年赡养费（元）</summary>
    public decimal AnnualSupportFee { get; set; }

    /// <summary>赡养人：月赡养费（元）</summary>
    public decimal MonthlySupportFee { get; set; }

    /// <summary>赡养人：赡养月数</summary>
    public int? SupportMonths { get; set; }

    /// <summary>赡养人：是否有赡养能力</summary>
    public bool IsSupportAbility { get; set; } = true;

    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// 经济信息更新请求
/// </summary>
public class EconomicInfoUpdateRequest
{
    public long ApplicationId { get; set; }
    public decimal WageIncome { get; set; }
    public decimal BusinessIncome { get; set; }
    public decimal PropertyIncome { get; set; }
    public decimal TransferIncome { get; set; }
    public decimal OtherIncome { get; set; }
    public decimal SupportIncome { get; set; }
    public decimal RigidExpenditure { get; set; }
    public decimal LandIncome { get; set; }
    public decimal SubsidyIncome { get; set; }
    public decimal TotalAnnualIncome { get; set; }
    public decimal PerCapitaAnnualIncome { get; set; }
    public decimal FamilySize { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// 变更记录创建请求
/// </summary>
public class ChangeRecordCreateRequest
{
    public long ArchiveId { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public string ChangeReason { get; set; } = string.Empty;
    public string BeforeValue { get; set; } = string.Empty;
    public string AfterValue { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// 渐退期设置请求    /// </summary>
public class GracePeriodSetRequest
{
    public long ArchiveId { get; set; }
    public int Months { get; set; } = 6;
    public string OriginalClassification { get; set; } = string.Empty;
    public decimal OriginalAmount { get; set; }
    public string SetBy { get; set; } = string.Empty;
}

/// <summary>
/// 档案归档请求
/// </summary>
public class ArchiveCreateRequest
{
    public long ApplicationId { get; set; }
    public string Classification { get; set; } = string.Empty;
    public decimal GuaranteeAmount { get; set; }
    public string ArchivedBy { get; set; } = string.Empty;
}

/// <summary>
/// 档案停止请求
/// </summary>
public class ArchiveStopRequest
{
    public long ArchiveId { get; set; }
    public string StopType { get; set; } = string.Empty;
    public string StopReason { get; set; } = string.Empty;
    public string StoppedBy { get; set; } = string.Empty;
}