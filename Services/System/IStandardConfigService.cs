using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.System;

public interface IStandardConfigService
{
    Task<Result<List<IncomeStandard>>> GetActiveIncomeStandardsAsync(CancellationToken ct = default);
    Task<Result<List<ClassifiedSubsidyStandard>>> GetActiveSubsidyStandardsAsync(CancellationToken ct = default);
    Task<Result<List<DestituteSupportStandard>>> GetActiveDestituteStandardsAsync(CancellationToken ct = default);
    Task<Result<List<SystemConfigStandard>>> GetSystemConfigsAsync(string category = null, CancellationToken ct = default);
    Task<Result> UpdateSystemConfigAsync(int configId, decimal newValue, CancellationToken ct = default);
    Task<Result<StandardConfigOverview>> GetOverviewAsync(CancellationToken ct = default);

    Task<Result<List<ConfigStandard>>> GetConfigStandardsAsync(string standardType = null, CancellationToken ct = default);
    Task<Result<ConfigStandard>> GetConfigStandardByTypeAsync(string standardType, string hukouType = null, string supportMode = null, CancellationToken ct = default);
    Task<Result<decimal>> GetStandardValueAsync(string standardType, string hukouType = null, string supportMode = null, CancellationToken ct = default);
    Task<Result<int>> CreateConfigStandardAsync(ConfigStandard standard, CancellationToken ct = default);
    Task<Result> UpdateConfigStandardAsync(ConfigStandard standard, CancellationToken ct = default);
    Task<Result> DeleteConfigStandardAsync(long standardId, CancellationToken ct = default);
    Task<Result> SyncSchemaAsync(IProgress<ProgressContext> progress = null, CancellationToken ct = default);
    Task<Result> InitializeFromSeedAsync(IProgress<ProgressContext> progress = null, CancellationToken ct = default);
}

public class IncomeStandard
{
    public int Id { get; set; }
    public string HukouType { get; set; } = string.Empty;
    public decimal MonthlyStandard { get; set; }
    public DateTime EffectiveStartDate { get; set; }
    public DateTime EffectiveEndDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ClassifiedSubsidyStandard
{
    public int Id { get; set; }
    public string HukouType { get; set; } = string.Empty;
    public decimal PerPersonAmount { get; set; }
    public DateTime EffectiveStartDate { get; set; }
    public DateTime EffectiveEndDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DestituteSupportStandard
{
    public int Id { get; set; }
    public string HukouType { get; set; } = string.Empty;
    public string SupportMode { get; set; } = string.Empty;
    public decimal MonthlyStandard { get; set; }
    public DateTime EffectiveStartDate { get; set; }
    public DateTime EffectiveEndDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SystemConfigStandard
{
    public int Id { get; set; }
    public string ConfigKey { get; set; } = string.Empty;
    public decimal ConfigValue { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime EffectiveStartDate { get; set; }
    public DateTime EffectiveEndDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class StandardConfigOverview
{
    public int IncomeStandardsCount { get; set; }
    public int SubsidyStandardsCount { get; set; }
    public int DestituteStandardsCount { get; set; }
    public int SystemConfigsCount { get; set; }
    public decimal RuralIncomeStandard { get; set; }
    public decimal UrbanIncomeStandard { get; set; }
    public decimal RuralSubsidyAmount { get; set; }
    public decimal UrbanSubsidyAmount { get; set; }
    public int ConfigStandardsCount { get; set; }
}
