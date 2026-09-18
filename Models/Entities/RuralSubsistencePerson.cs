using System.ComponentModel.DataAnnotations;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 农村低保人员实体（纯 POCO，对对应 nc_biz_rural_subsistence_persons 表）
/// </summary>
public class RuralSubsistencePerson
{
    /// <summary>
    /// 主键ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 家庭ID（关联nc_biz_rural_subsistence_families    /// </summary>
    public long FamilyId { get; set; }

    /// <summary>
    /// 户主身份证号（关联键    /// </summary>
    [Required(ErrorMessage = "户主身份证号不能为空")]
    public string HeadIdCard { get; set; } = string.Empty;

    /// <summary>
    /// 姓名
    /// </summary>
    [Required(ErrorMessage = "姓名不能为空")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 身份证号
    /// </summary>
    [Required(ErrorMessage = "身份证号不能为空")]
    public string IdCard { get; set; } = string.Empty;

    /// <summary>
    /// 与户主关    /// </summary>
    public string Relationship { get; set; } = string.Empty;

    /// <summary>
    /// 性别
    /// </summary>
    public string Gender { get; set; } = string.Empty;

    /// <summary>
    /// 出生日期
    /// </summary>
    public DateTime BirthDate { get; set; }

    /// <summary>
    /// 年龄
    /// </summary>
    public int Age { get; set; }

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
    /// 就业状况
    /// </summary>
    public string EmploymentStatus { get; set; } = string.Empty;

    /// <summary>
    /// 年收入（元）
    /// </summary>
    public decimal AnnualIncome { get; set; }

    /// <summary>
    /// 是否残疾    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    /// 残疾证号
    /// </summary>
    public string DisabilityCertificate { get; set; } = string.Empty;

    /// <summary>
    /// 残疾类别
    /// </summary>
    public string DisabilityType { get; set; } = string.Empty;

    /// <summary>
    /// 残疾等级
    /// </summary>
    public string DisabilityLevel { get; set; } = string.Empty;

    /// <summary>
    /// 患病病种
    /// </summary>
    public string DiseaseType { get; set; } = string.Empty;

    /// <summary>
    /// 劳动能力
    /// </summary>
    public string WorkCapacity { get; set; } = string.Empty;

    /// <summary>
    /// 健康状况
    /// </summary>
    public string HealthStatus { get; set; } = string.Empty;

    /// <summary>
    /// 参保类型
    /// </summary>
    public string InsuranceType { get; set; } = string.Empty;

    /// <summary>
    /// 最初享受月    /// </summary>
    public string FirstReceiveMonth { get; set; } = string.Empty;

    /// <summary>
    /// 是否建档立卡扶贫对象
    /// </summary>
    public bool IsPovertyHousehold { get; set; }

    /// <summary>
    /// 开户银    /// </summary>
    public string BankName { get; set; } = string.Empty;

    /// <summary>
    /// 银行账户名称
    /// </summary>
    public string BankAccountName { get; set; } = string.Empty;

    /// <summary>
    /// 银行帐号
    /// </summary>
    public string BankAccount { get; set; } = string.Empty;

    /// <summary>
    /// 一卡通账    /// </summary>
    public string OneCardAccount { get; set; } = string.Empty;

    /// <summary>
    /// 救助类型
    /// </summary>
    public string AssistanceType { get; set; } = string.Empty;

    /// <summary>
    /// 户月保障金额（元    /// </summary>
    public decimal MonthlyGuaranteeAmount { get; set; }

    /// <summary>
    /// 家庭分类施保金额（元    /// </summary>
    public decimal FamilyClassifiedAmount { get; set; }

    /// <summary>
    /// 合计分类施保金额（元    /// </summary>
    public decimal TotalClassifiedAmount { get; set; }

    /// <summary>
    /// 救助金计算方    /// </summary>
    public string CalculationMethod { get; set; } = string.Empty;

    /// <summary>
    /// 分档类型
    /// </summary>
    public string GradeType { get; set; } = string.Empty;

    /// <summary>
    /// 合计（元    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// 在读学校名称
    /// </summary>
    public string SchoolName { get; set; } = string.Empty;

    /// <summary>
    /// 在读学校性质
    /// </summary>
    public string SchoolNature { get; set; } = string.Empty;

    /// <summary>
    /// 在读学校入学时间
    /// </summary>
    public string EnrollmentDate { get; set; } = string.Empty;

    /// <summary>
    /// 在职单位名称
    /// </summary>
    public string EmployerName { get; set; } = string.Empty;

    /// <summary>
    /// 在职单位性质
    /// </summary>
    public string EmployerNature { get; set; } = string.Empty;

    /// <summary>
    /// 在职单位地点
    /// </summary>
    public string EmployerLocation { get; set; } = string.Empty;

    /// <summary>
    /// 备注
    /// </summary>
    public string Remark { get; set; } = string.Empty;

    /// <summary>
    /// 所属省    /// </summary>
    public string Province { get; set; } = string.Empty;

    /// <summary>
    /// 所属市    /// </summary>
    public string City { get; set; } = string.Empty;

    /// <summary>
    /// 所属区    /// </summary>
    public string District { get; set; } = string.Empty;

    /// <summary>
    /// 所属街    /// </summary>
    public string Street { get; set; } = string.Empty;

    /// <summary>
    /// 所属社    /// </summary>
    public string Community { get; set; } = string.Empty;

    /// <summary>
    /// 家庭地址
    /// </summary>
    public string FamilyAddress { get; set; } = string.Empty;

    /// <summary>
    /// 低保（特困）证号
    /// </summary>
    public string CertificateNumber { get; set; } = string.Empty;

    /// <summary>
    /// 导入时间
    /// </summary>
    public DateTime ImportedAt { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}