using System.ComponentModel.DataAnnotations;
using NewCosmos.Constants;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 申请实体（纯 POCO，对应 nc_biz_applications 表）
/// </summary>
public class Application
{
    /// <summary>
    /// 申请ID，主键    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 申请编号
    /// </summary>
    public string ApplicationNo { get; set; } = string.Empty;

    /// <summary>
    /// 申请人姓名    /// </summary>
    [Required(ErrorMessage = "申请人姓名不能为")]
    public string ApplicantName { get; set; } = string.Empty;

    /// <summary>
    /// 申请人身份证号    /// </summary>
    [Required(ErrorMessage = "申请人身份证号不能为")]
    public string ApplicantIdCard { get; set; } = string.Empty;

    /// <summary>
    /// 申请人手机号
    /// </summary>
    public string ApplicantPhone { get; set; } = string.Empty;

    /// <summary>
    /// 户籍类型
    /// </summary>
    public string HukouType { get; set; } = string.Empty;

    /// <summary>
    /// 性别
    /// </summary>
    public string Gender { get; set; } = string.Empty;

    /// <summary>
    /// 民族
    /// </summary>
    public string Ethnicity { get; set; } = string.Empty;

    /// <summary>
    /// 婚姻状况
    /// </summary>
    public string MaritalStatus { get; set; } = string.Empty;

    /// <summary>
    /// 文化程度
    /// </summary>
    public string EducationLevel { get; set; } = string.Empty;

    /// <summary>
    /// 政治面貌
    /// </summary>
    public string PoliticalStatus { get; set; } = string.Empty;

    /// <summary>
    /// 户籍地址（合并字符串，由 ID 推导的冗余显示值）
    /// </summary>
    public string HukouAddress { get; set; } = string.Empty;

    /// <summary>
    /// 户籍城市 ID（对应 nc_regions_cities.id）    /// </summary>
    public int? HukouCityId { get; set; }

    /// <summary>
    /// 户籍区县 ID（对应 nc_regions_counties.id）    /// </summary>
    public int? HukouCountyId { get; set; }

    /// <summary>
    /// 户籍乡镇 ID（对应 nc_regions_towns.id）    /// </summary>
    public int? HukouTownId { get; set; }

    /// <summary>
    /// 户籍社区/?ID（对应 nc_regions_villages.id）    /// </summary>
    public int? HukouVillageId { get; set; }

    /// <summary>
    /// 残疾证号（如有）    /// </summary>
    public string DisabilityCardNo { get; set; } = string.Empty;

    /// <summary>
    /// 省份
    /// </summary>
    public string Province { get; set; } = string.Empty;

    /// <summary>
    /// 城市ID（对应 nc_regions_cities.id）    /// </summary>
    public int? CityId { get; set; }

    /// <summary>
    /// 区县ID（对应 nc_regions_counties.id）    /// </summary>
    public int? CountyId { get; set; }

    /// <summary>
    /// 乡镇ID（对应 nc_regions_towns.id）    /// </summary>
    public int? TownId { get; set; }

    /// <summary>
    /// ?社区ID（对应 nc_regions_villages.id）    /// </summary>
    public int? VillageId { get; set; }

    /// <summary>
    /// 城市名称
    /// </summary>
    public string City { get; set; } = string.Empty;

    /// <summary>
    /// 区县名称
    /// </summary>
    public string District { get; set; } = string.Empty;

    /// <summary>
    /// 乡镇名称
    /// </summary>
    public string Town { get; set; } = string.Empty;

    /// <summary>
    /// 社区    /// </summary>
    public string Community { get; set; } = string.Empty;

    /// <summary>
    ///     /// </summary>
    public string Village { get; set; } = string.Empty;

    /// <summary>
    /// 详细地址
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// 户籍地址值对象（映射的HukouAddress + Province/City/District/Town/Community 等字段）
    /// 注意：此属性不能EF Core 直接映射，通过映射代码读写
    /// </summary>
    public AddressInfo HukouAddressInfo { get; set; } = new AddressInfo();

    /// <summary>
    /// 现住地址值对象（映射的Province/City/District/Town/Community/Address 字段    /// 注意：此属性不能EF Core 直接映射，通过映射代码读写
    /// </summary>
    public AddressInfo ResidentialAddressInfo { get; set; } = new AddressInfo();

    /// <summary>
    /// 银行名称
    /// </summary>
    public string BankName { get; set; } = string.Empty;

    /// <summary>
    /// 银行账号
    /// </summary>
    public string BankAccount { get; set; } = string.Empty;

    /// <summary>
    /// 身体状况
    /// </summary>
    public string PhysicalCondition { get; set; } = string.Empty;

    /// <summary>
    /// 疾病名称
    /// </summary>
    public string DiseaseName { get; set; } = string.Empty;

    /// <summary>
    /// 二级疾病名称
    /// </summary>
    public string SecondaryDiseaseName { get; set; } = string.Empty;

    /// <summary>
    /// 疾病编码（ICD-10）
    /// </summary>
    public string DiseaseCode { get; set; } = string.Empty;

    /// <summary>
    /// 是否重病（经办人人工判定复选框）
    /// </summary>
    public bool IsSevereDisease { get; set; }

    /// <summary>
    /// 残疾类型
    /// </summary>
    public string DisabilityType { get; set; } = string.Empty;

    /// <summary>
    /// 残疾等级
    /// </summary>
    public string DisabilityLevel { get; set; } = string.Empty;

    /// <summary>
    /// 健康状况
    /// </summary>
    public string HealthStatus { get; set; } = string.Empty;

    /// <summary>
    /// 从业情况
    /// </summary>
    public string EmploymentStatus { get; set; } = string.Empty;

    /// <summary>
    /// 工作单位
    /// </summary>
    public string WorkUnit { get; set; } = string.Empty;

    /// <summary>
    /// 收入来源
    /// </summary>
    public string IncomeSource { get; set; } = string.Empty;

    /// <summary>
    /// 是否单独救助
    /// </summary>
    public bool IsSingleRescue { get; set; }

    /// <summary>
    /// 救助方式
    /// </summary>
    public string SupportMode { get; set; } = string.Empty;

    /// <summary>
    /// 申请原因
    /// </summary>
    public string ApplicationReason { get; set; } = string.Empty;

    /// <summary>
    /// 申请原因详细
    /// </summary>
    public string ApplicationReasonDetail { get; set; } = string.Empty;

    /// <summary>
    /// 照料护理人类    /// </summary>
    public string CaregiverType { get; set; } = string.Empty;

    /// <summary>
    /// 特困供养分类
    /// </summary>
    public string DestituteSupportType { get; set; } = string.Empty;

    /// <summary>
    /// 特困供养机构ID
    /// </summary>
    public long SupportInstitutionId { get; set; }

    /// <summary>
    /// 特困供养机构名称
    /// </summary>
    public string SupportInstitutionName { get; set; } = string.Empty;

    /// <summary>
    /// 特困供养机构费用（月）
    /// </summary>
    public decimal SupportInstitutionFee { get; set; }

    /// <summary>
    /// 家庭人数
    /// </summary>
    public int FamilySize { get; set; } = 1;

    /// <summary>
    /// 核定家庭人数
    /// </summary>
    public int ConfirmedFamilySize { get; set; }

    /// <summary>
    /// 务工收入合计（月值）
    /// </summary>
    public decimal WorkIncomeTotal { get; set; }

    /// <summary>
    /// 经营收入合计（月值）
    /// </summary>
    public decimal BusinessIncomeTotal { get; set; }

    /// <summary>
    /// 财产性收入合计（月值）
    /// </summary>
    public decimal PropertyIncomeTotal { get; set; }

    /// <summary>
    /// 转移性收入合计（月值）
    /// </summary>
    public decimal TransferIncomeTotal { get; set; }

    /// <summary>
    /// 其他收入合计（月值）
    /// </summary>
    public decimal OtherIncomeTotal { get; set; }

    /// <summary>
    /// 家庭总收入（月值 = 年收入 ÷ 12，分解显示口径；权威值见 TotalAnnualIncome）
    /// </summary>
    public decimal TotalFamilyIncome { get; set; }

    /// <summary>
    /// 人均收入（月值 = 年人均 ÷ 12，分解显示口径；权威值见 PerCapitaAnnualIncome）
    /// </summary>
    public decimal PerCapitaIncome { get; set; }

    /// <summary>
    /// 刚性支出（月值）
    /// </summary>
    public decimal RigidExpenditure { get; set; }

    /// <summary>
    /// 赡养费收入（年值 = Σ每笔年赡养费；不再存月值）
    /// </summary>
    public decimal AlimonyIncome { get; set; }

    /// <summary>
    /// 家庭年总收入（年值权威口径 = Σ月项×12 + 赡养年值 + 土地年值 + 补贴年值 − 刚性×12，一次性舍入）
    /// </summary>
    public decimal TotalAnnualIncome { get; set; }

    /// <summary>
    /// 人均年收入（年值权威口径 = 家庭年总收入 ÷ 家庭人数）
    /// </summary>
    public decimal PerCapitaAnnualIncome { get; set; }

    /// <summary>
    /// 是否符合条件
    /// </summary>
    public bool IsEligible { get; set; }

    /// <summary>
    /// 分类结果
    /// </summary>
    public string? ClassificationResult { get; set; } = string.Empty;

    /// <summary>
    /// 分类补贴类型
    /// </summary>
    public string ClassifiedSubsidyType { get; set; } = string.Empty;

    /// <summary>
    /// 分类补贴金额
    /// </summary>
    public decimal ClassifiedSubsidyAmount { get; set; }

    /// <summary>
    /// 户月保障金额
    /// </summary>
    public decimal HouseholdMonthlyGuaranteeAmount { get; set; }

    /// <summary>
    /// 人员分类保障总金    /// </summary>
    public decimal PersonCategoryProtectionTotalAmount { get; set; }

    /// <summary>
    /// 照料护理补贴金额
    /// </summary>
    public decimal CaregiverSubsidyAmount { get; set; }

    /// <summary>
    /// 保障金总额
    /// </summary>
    public decimal TotalGuaranteeAmount { get; set; }

    /// <summary>
    /// 是否处于渐退    /// </summary>
    public bool IsInGracePeriod { get; set; }

    /// <summary>
    /// 渐退期月    /// </summary>
    public int? GracePeriodMonths { get; set; }

    /// <summary>
    /// 渐退期开始日    /// </summary>
    public DateTime? GracePeriodStartDate { get; set; }

    /// <summary>
    /// 渐退期结束日    /// </summary>
    public DateTime? GracePeriodEndDate { get; set; }

    /// <summary>
    /// 渐退前原分类结果
    /// </summary>
    public string? OriginalClassificationResult { get; set; } = string.Empty;

    /// <summary>
    /// 渐退前原保障金额
    /// </summary>
    public decimal? OriginalGuaranteeAmount { get; set; }

    /// <summary>
    /// 停保原因
    /// </summary>
    public string StopReason { get; set; } = string.Empty;

    /// <summary>
    /// 停保日期
    /// </summary>
    public DateTime StopDate { get; set; }

    /// <summary>
    /// 停保备注
    /// </summary>
    public string StopRemark { get; set; } = string.Empty;

    /// <summary>
    /// 是否通过一事一议豁免
    /// </summary>
    public bool IsSpecialApproval { get; set; }

    /// <summary>
    /// 关联 nc_biz_special_approvals.id（会议审议结果）
    /// </summary>
    public long? SpecialApprovalId { get; set; }

    /// <summary>
    /// 申请状    /// </summary>
    public string Status { get; set; } = ApplicationStatusCodes.DRAFT;

    /// <summary>
    /// 当前步骤
    /// </summary>
    public int CurrentStep { get; set; } = 1;

    /// <summary>
    /// 提交时间
    /// </summary>
    public DateTime SubmitAt { get; set; }

    /// <summary>
    /// 提交    /// </summary>
    public string SubmitBy { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 创建    /// </summary>
    public string? CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// 更新    /// </summary>
    public string UpdatedBy { get; set; } = string.Empty;

    /// <summary>
    /// 删除时间（软删除标记    /// </summary>
    public DateTime DeletedAt { get; set; }

    /// <summary>
    /// 乐观并发版本    /// </summary>
    public int Version { get; set; } = 1;
    /// <summary>
    /// 原始申请ID（用于追溯变更链
    /// </summary>
    public long OriginalApplicationId { get; set; }

    /// <summary>
    /// 数据来源类型（NewApplication 原生 / ImportedArchive 导入库建档等）
    /// </summary>
    public string? SourceType { get; set; }

    /// <summary>
    /// 档案链类型（original_application_id 由来）：
    /// CategoryRebuild 经济复核停旧建新 / HeadChange 户主变更 / HouseholdDeath 户主死亡 /
    /// SingleRescue 户内单人保（旧档案不停止）/ ImportedArchive 导入建档
    /// </summary>
    public string? ChainType { get; set; }

    /// <summary>
    /// 数据来源表名（导入库建档时为导入库表，如 nc_biz_rural_subsistence_families）
    /// </summary>
    public string? SourceTable { get; set; }

    /// <summary>
    /// 数据补全完成时间（导入库建档档案：NULL=待补全，非空=已完成补全，此后可进入经济复核模式）
    /// </summary>
    public DateTime? DataCompletedAt { get; set; }

    /// <summary>
    /// 首次成为保障对象（归档/审批通过）时间；新增及审批时间统计口径，写入一次不覆盖
    /// </summary>
    public DateTime? FirstApprovedAt { get; set; }

    // ── 土地相关 ──

    /// <summary>
    /// 家庭土地面积（亩    /// </summary>
    public decimal FamilyLandArea { get; set; }

    /// <summary>
    /// 自种土地面积（亩    /// </summary>
    public decimal SelfFarmedLandArea { get; set; }

    /// <summary>
    /// 转租土地面积（亩    /// </summary>
    public decimal SubleasedLandArea { get; set; }

    /// <summary>
    /// 承包土地面积（亩    /// </summary>
    public decimal ContractedLandArea { get; set; }

    /// <summary>
    /// 土地收入合计（元    /// </summary>
    public decimal LandIncomeTotal { get; set; }

    /// <summary>
    /// 补贴合计（元    /// </summary>
    public decimal SubsidyTotal { get; set; }

    /// <summary>
    /// 确权土地总面积（亩）
    /// </summary>
    public decimal TotalConfirmedLandArea { get; set; }

    /// <summary>
    /// 确权人数
    /// </summary>
    public int ConfirmedPersonCount { get; set; }
}