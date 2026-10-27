namespace NewCosmos.Models.Entities;

public class RegionCity
{
    public int Id { get; set; }
    public string CityName { get; set; } = string.Empty;
    public string CityCode { get; set; } = string.Empty;

    /// <summary>所属省ID（2026-10-26 加，真级联省；由 city_code 前2位回填，未回填为 null）</summary>
    public int? ProvinceId { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
