using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 经济明细数据保存/加载服务接口
/// </summary>
public interface IEconomicDetailService
{
    /// <summary>
    /// 保存所有经济明细（删除旧数据+插入新数据，事务内）
    /// </summary>
    Task<Result> SaveAllAsync(long applicationId,
        List<LaborIncome> laborIncomes,
        List<BusinessIncome> businessIncomes,
        List<PropertyIncome> propertyIncomes,
        List<TransferIncome> transferIncomes,
        List<OtherIncome> otherIncomes,
        List<Subsidy> subsidies,
        List<BreedingIncome> breedingIncomes,
        List<RigidExpenditure> rigidExpenditures,
        List<FamilyProperty> familyProperties,
        List<Vehicle> vehicles,
        List<Machinery> machineries,
        List<FinancialAsset> financialAssets,
        List<LandRegistration> landRegistrations,
        List<LandConfirmationGroup> landConfirmationGroups,
        CancellationToken ct = default);

    /// <summary>
    /// 加载所有经济明细
    /// </summary>
    Task<Result<EconomicDetailData>> LoadAllAsync(long applicationId, CancellationToken ct = default);
}

/// <summary>
/// 经济明细数据集合
/// </summary>
public class EconomicDetailData
{
    public List<LaborIncome> LaborIncomes { get; set; } = new();
    public List<BusinessIncome> BusinessIncomes { get; set; } = new();
    public List<PropertyIncome> PropertyIncomes { get; set; } = new();
    public List<TransferIncome> TransferIncomes { get; set; } = new();
    public List<OtherIncome> OtherIncomes { get; set; } = new();
    public List<Subsidy> Subsidies { get; set; } = new();
    public List<RigidExpenditure> RigidExpenditures { get; set; } = new();
    public List<FamilyProperty> FamilyProperties { get; set; } = new();
    public List<Vehicle> Vehicles { get; set; } = new();
    public List<Machinery> Machineries { get; set; } = new();
    public List<FinancialAsset> FinancialAssets { get; set; } = new();
    public List<BreedingIncome> BreedingIncomes { get; set; } = new();
    public List<LandRegistration> LandRegistrations { get; set; } = new();
    public List<LandConfirmationGroup> LandConfirmationGroups { get; set; } = new();
}
