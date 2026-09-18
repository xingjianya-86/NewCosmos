namespace NewCosmos.Models.Entities;

public class ConfigStandard
{
    public long Id { get; set; }
    public string StandardType { get; set; } = string.Empty;
    public string StandardName { get; set; } = string.Empty;
    public string? HukouType { get; set; } = string.Empty;
    public string? SupportMode { get; set; } = string.Empty;
    public decimal StandardValue { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTime EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate { get; set; }
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string DisplayName => $"{StandardName}（{StandardValue} {Unit}";
    public bool IsEffective => IsActive && 
        EffectiveStartDate <= DateTime.Today && 
        (EffectiveEndDate == null || EffectiveEndDate >= DateTime.Today);
}