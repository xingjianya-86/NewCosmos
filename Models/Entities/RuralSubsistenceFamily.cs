using System.ComponentModel.DataAnnotations;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 农村低保家庭实体（纯 POCO，对对应 nc_biz_rural_subsistence_families 表）
/// </summary>
public class RuralSubsistenceFamily
{
    /// <summary>
    /// 主键ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 户主姓名
    /// </summary>
    [Required(ErrorMessage = "户主姓名不能为空")]
    public string ApplicantName { get; set; } = string.Empty;

    /// <summary>
    /// 户主身份证号
    /// </summary>
    [Required(ErrorMessage = "户主身份证号不能为空")]
    public string ApplicantIdCard { get; set; } = string.Empty;

    /// <summary>
    /// 家庭人口    /// </summary>
    public int FamilySize { get; set; } = 1;

    /// <summary>
    /// 保障人口    /// </summary>
    public int GuaranteeSize { get; set; }

    /// <summary>
    /// 家庭年总收入（元）
    /// </summary>
    public decimal AnnualIncome { get; set; }

    /// <summary>
    /// 家庭年均收入（元    /// </summary>
    public decimal PerCapitaIncome { get; set; }

    /// <summary>
    /// 户月保障金额（元    /// </summary>
    public decimal MonthlyGuaranteeAmount { get; set; }

    /// <summary>
    /// 家庭分类施保总额（元    /// </summary>
    public decimal FamilyClassifiedAmount { get; set; }

    /// <summary>
    /// 人员分类施保总额（元    /// </summary>
    public decimal PersonClassifiedAmount { get; set; }

    /// <summary>
    /// 合计（元    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// 联系电话
    /// </summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>
    /// 家庭住址
    /// </summary>
    public string Address { get; set; } = string.Empty;

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
    /// 低保（特困）证号
    /// </summary>
    public string CertificateNumber { get; set; } = string.Empty;

    /// <summary>
    /// 最初享受月    /// </summary>
    public string FirstReceiveMonth { get; set; } = string.Empty;

    /// <summary>
    /// 申请原因
    /// </summary>
    public string ApplyReason { get; set; } = string.Empty;

    /// <summary>
    /// 救助金计算方    /// </summary>
    public string CalculationMethod { get; set; } = string.Empty;

    /// <summary>
    /// 分档类型
    /// </summary>
    public string GradeType { get; set; } = string.Empty;

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
    /// 拥有产权住房套数
    /// </summary>
    public int HousingCount { get; set; }

    /// <summary>
    /// 住房总面积（平方米）
    /// </summary>
    public decimal HousingArea { get; set; }

    /// <summary>
    /// 金融资产总额（元    /// </summary>
    public decimal FinancialAssets { get; set; }

    /// <summary>
    /// 其他家庭财产情况
    /// </summary>
    public string OtherProperty { get; set; } = string.Empty;

    /// <summary>
    /// 家庭成员姓名（分号分隔）
    /// </summary>
    public string MemberNames { get; set; } = string.Empty;

    /// <summary>
    /// 家庭成员身份证号（分号分隔）
    /// </summary>
    public string MemberIdCards { get; set; } = string.Empty;

    /// <summary>
    /// 导入时间
    /// </summary>
    public DateTime ImportedAt { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}