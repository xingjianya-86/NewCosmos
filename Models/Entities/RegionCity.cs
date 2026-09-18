namespace NewCosmos.Models.Entities;

public class RegionCity
{
    public int Id { get; set; }
    public string CityName { get; set; } = string.Empty;
    public string CityCode { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
