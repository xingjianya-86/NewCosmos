namespace NewCosmos.Models.Entities;

/// <summary>
/// 供养机构实体（对应 nc_biz_support_institutions 表）
/// </summary>
public class SupportInstitution
{
    /// <summary>
    /// 主键ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 机构名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 机构简称
    /// </summary>
    public string Abbreviation { get; set; } = string.Empty;

    /// <summary>
    /// 机构类型（养老院、福利院等）
    /// </summary>
    public string InstitutionType { get; set; } = string.Empty;

    /// <summary>
    /// 机构类别（公立、民营等）
    /// </summary>
    public string InstitutionCategory { get; set; } = string.Empty;

    /// <summary>
    /// 负责人
    /// </summary>
    public string Principal { get; set; } = string.Empty;

    /// <summary>
    /// 负责人身份证
    /// </summary>
    public string PrincipalIdCard { get; set; } = string.Empty;

    /// <summary>
    /// 联系电话
    /// </summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>
    /// 详细地址
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// 城市ID
    /// </summary>
    public int? CityId { get; set; }

    /// <summary>
    /// 城市名称
    /// </summary>
    public string CityName { get; set; } = string.Empty;

    /// <summary>
    /// 区县ID
    /// </summary>
    public int? CountyId { get; set; }

    /// <summary>
    /// 区县名称
    /// </summary>
    public string CountyName { get; set; } = string.Empty;

    /// <summary>
    /// 乡镇ID
    /// </summary>
    public int? TownId { get; set; }

    /// <summary>
    /// 乡镇名称
    /// </summary>
    public string TownName { get; set; } = string.Empty;

    /// <summary>
    /// 村社区ID
    /// </summary>
    public int? VillageId { get; set; }

    /// <summary>
    /// 村社区名称
    /// </summary>
    public string VillageName { get; set; } = string.Empty;

    /// <summary>
    /// 床位数
    /// </summary>
    public int Capacity { get; set; }

    /// <summary>
    /// 当前入住人数
    /// </summary>
    public int CurrentOccupancy { get; set; }

    /// <summary>
    /// 床位费（月）
    /// </summary>
    public decimal BedFee { get; set; }

    /// <summary>
    /// 照料费（月）
    /// </summary>
    public decimal CareFee { get; set; }

    /// <summary>
    /// 总费用（月）
    /// </summary>
    public decimal TotalFee { get; set; }

    /// <summary>
    /// 服务项目（JSON）
    /// </summary>
    public string ServiceItems { get; set; } = string.Empty;

    /// <summary>
    /// 是否有医疗服务
    /// </summary>
    public bool HasMedicalService { get; set; }

    /// <summary>
    /// 是否有康复服务
    /// </summary>
    public bool HasRehabilitationService { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 是否为特困供养机构
    /// </summary>
    public bool IsSpecialCare { get; set; } = true;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 创建人
    /// </summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// 更新人
    /// </summary>
    public string UpdatedBy { get; set; } = string.Empty;

    /// <summary>
    /// 删除时间（软删除）
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// 可用床位数（计算属性）
    /// </summary>
    public int AvailableBeds => Capacity - CurrentOccupancy;

    /// <summary>
    /// 是否有可用床位（计算属性）
    /// </summary>
    public bool HasAvailableBeds => AvailableBeds > 0;
}

/// <summary>
/// 供养机构创建请求
/// </summary>
public class SupportInstitutionCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
    public string InstitutionType { get; set; } = string.Empty;
    public string InstitutionCategory { get; set; } = string.Empty;
    public string Principal { get; set; } = string.Empty;
    public string PrincipalIdCard { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int? CityId { get; set; }
    public string CityName { get; set; } = string.Empty;
    public int? CountyId { get; set; }
    public string CountyName { get; set; } = string.Empty;
    public int? TownId { get; set; }
    public string TownName { get; set; } = string.Empty;
    public int? VillageId { get; set; }
    public string VillageName { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public decimal BedFee { get; set; }
    public decimal CareFee { get; set; }
    public decimal TotalFee { get; set; }
    public string ServiceItems { get; set; } = string.Empty;
    public bool HasMedicalService { get; set; }
    public bool HasRehabilitationService { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsSpecialCare { get; set; } = true;
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// 供养机构更新请求
/// </summary>
public class SupportInstitutionUpdateRequest
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
    public string InstitutionType { get; set; } = string.Empty;
    public string InstitutionCategory { get; set; } = string.Empty;
    public string Principal { get; set; } = string.Empty;
    public string PrincipalIdCard { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int? CityId { get; set; }
    public string CityName { get; set; } = string.Empty;
    public int? CountyId { get; set; }
    public string CountyName { get; set; } = string.Empty;
    public int? TownId { get; set; }
    public string TownName { get; set; } = string.Empty;
    public int? VillageId { get; set; }
    public string VillageName { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public decimal BedFee { get; set; }
    public decimal CareFee { get; set; }
    public decimal TotalFee { get; set; }
    public string ServiceItems { get; set; } = string.Empty;
    public bool HasMedicalService { get; set; }
    public bool HasRehabilitationService { get; set; }
    public bool IsActive { get; set; }
    public bool IsSpecialCare { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}
