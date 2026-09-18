using CommunityToolkit.Mvvm.ComponentModel;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 房产实体（纯 POCO，对应 nc_biz_properties 表）
/// </summary>
public class FamilyProperty
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public long MemberId { get; set; }
    public string PropertyType { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string HousingStructure { get; set; } = string.Empty;
    public string HousingNature { get; set; } = string.Empty;
    public decimal Area { get; set; }
    public int RoomCount { get; set; }
    public int BuildYear { get; set; }
    public decimal EstimatedValue { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DeletedAt { get; set; }
}

/// <summary>
/// 车辆实体（纯 POCO，对应 nc_biz_vehicles 表）
/// </summary>
public class Vehicle
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public long MemberId { get; set; }
    public string VehicleType { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string LicensePlate { get; set; } = string.Empty;
    public int PurchaseYear { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal EstimatedValue { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DeletedAt { get; set; }
}

/// <summary>
/// 农业机械实体（纯 POCO，对应 nc_biz_machineries 表）
/// </summary>
public class Machinery
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public long MemberId { get; set; }
    public string MachineryType { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public decimal Horsepower { get; set; }
    public int PurchaseYear { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal EstimatedValue { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DeletedAt { get; set; }
}

/// <summary>
/// 金融资产实体（纯 POCO，对应 nc_biz_financial_assets 表）
/// </summary>
public class FinancialAsset
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public bool HasCash { get; set; }
    public decimal CashAmount { get; set; }
    public bool HasBankDeposit { get; set; }
    public decimal BankDepositAmount { get; set; }
    public bool HasSecurities { get; set; }
    public decimal SecuritiesAmount { get; set; }
    public bool HasCommercialInsurance { get; set; }
    public string CommercialInsuranceType { get; set; } = string.Empty;
    public decimal CommercialInsuranceAmount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DeletedAt { get; set; }
}

/// <summary>
/// 土地确权（承包方）实体（纯 POCO，对应 nc_biz_land_confirmations 表）
/// </summary>
public class LandConfirmation
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public string ContractorName { get; set; } = string.Empty;
    public string VillageName { get; set; } = string.Empty;
    public decimal TotalConfirmedArea { get; set; }
    public decimal TotalShares { get; set; }
    public int TotalPersonCount { get; set; }
    public decimal DryFieldArea { get; set; }
    public decimal WetFieldArea { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DeletedAt { get; set; }
}

/// <summary>
/// 土地确权记录实体（支持属性变更通知，对应 nc_biz_land_confirmation_records 表）
/// </summary>
public partial class LandConfirmationRecord : ObservableObject
{
    public long Id { get; set; }
    public long ConfirmationId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string MemberIdCard { get; set; } = string.Empty;

    [ObservableProperty]
    private string _landPlotInfo = string.Empty;

    public string PlotCode { get; set; } = string.Empty;
    public decimal ContractArea { get; set; }
    public decimal MeasuredArea { get; set; }

    [ObservableProperty]
    private decimal _landArea;

    [ObservableProperty]
    private string _landUsage = Constants.DictionaryConstants.LandUsage.SELF_FARM;

    [ObservableProperty]
    private decimal _unitPrice;

    /// <summary>
    /// 土地价值 = 面积 × 单价
    /// </summary>
    public decimal LandValue => Math.Round(LandArea * UnitPrice, 2);

    [ObservableProperty]
    private bool _isImported;

    partial void OnLandUsageChanged(string value)
    {
        OnPropertyChanged(nameof(LandValue));
    }

    partial void OnLandAreaChanged(decimal value)
    {
        OnPropertyChanged(nameof(LandValue));
    }

    partial void OnUnitPriceChanged(decimal value)
    {
        OnPropertyChanged(nameof(LandValue));
    }

    public string AreaDisplay => ContractArea > 0 && MeasuredArea > 0
        ? $"{ContractArea:F2}/{MeasuredArea:F2}"
        : LandArea > 0 ? LandArea.ToString("F2") : "0";
}
