namespace NewCosmos.Models.Requests;

public class RegionCitySaveRequest
{
    public int Id { get; set; }
    public string CityName { get; set; } = string.Empty;
    public string CityCode { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class RegionCountySaveRequest
{
    public int Id { get; set; }
    public string CountyName { get; set; } = string.Empty;
    public string? CountyCode { get; set; } = string.Empty;
    public string? CityName { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class RegionTownSaveRequest
{
    public int Id { get; set; }
    public int CountyId { get; set; }
    public string TownName { get; set; } = string.Empty;
    public string? TownCode { get; set; } = string.Empty;
    public string? TownType { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class RegionVillageSaveRequest
{
    public int Id { get; set; }
    public int TownId { get; set; }
    public string VillageName { get; set; } = string.Empty;
    public string? VillageCode { get; set; } = string.Empty;
    public string? VillageType { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
