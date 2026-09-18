namespace NewCosmos.Models.Entities;

/// <summary>
/// 入户调查实体（纯 POCO，对对应 nc_biz_household_surveys 表）
/// </summary>
public class HouseholdSurvey
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public DateTime SurveyDate { get; set; }
    public string SurveyorName { get; set; } = string.Empty;
    public string SurveyorOrganization { get; set; } = string.Empty;
    public string RespondentName { get; set; } = string.Empty;
    public string RespondentRelation { get; set; } = string.Empty;
    public string ApplicationReason { get; set; } = string.Empty;
    public string ApplicationReasonDetail { get; set; } = string.Empty;
    public string SurveyConclusion { get; set; } = "属实";
    public string SurveyNotes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; }
    public DateTime DeletedAt { get; set; }
}

/// <summary>
/// 林地实体（纯 POCO，对对应 nc_biz_forest_lands 表）
/// </summary>
public class ForestLand
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public string TreeSpecies { get; set; } = string.Empty;
    public decimal Area { get; set; }
    public string GrowthCycle { get; set; } = string.Empty;
    public bool IsMature { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DeletedAt { get; set; }
}

/// <summary>
/// 养殖收入实体（纯 POCO，对对应 nc_biz_breeding_incomes 表）
/// </summary>
public class BreedingIncome
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public string BreedingType { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal AnnualIncome { get; set; }
    public string Remark { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DeletedAt { get; set; }
}

/// <summary>
/// 就业成本实体（纯 POCO，对对应 nc_biz_employment_costs 表）
/// </summary>
public class EmploymentCost
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public long MemberId { get; set; }
    public string CostType { get; set; } = string.Empty;
    public decimal AnnualAmount { get; set; }
    public string Remark { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DeletedAt { get; set; }
}
