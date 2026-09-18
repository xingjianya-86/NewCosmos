namespace NewCosmos.Models.Entities;

/// <summary>
/// 地址信息值对象（可复用于户籍地址、现住地址等）
/// </summary>
public class AddressInfo
{
    public string Province { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string Town { get; set; } = string.Empty;
    public string Community { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;

    /// <summary>
    /// 城市ID
    /// </summary>
    public int? CityId { get; set; }

    /// <summary>
    /// 区县ID
    /// </summary>
    public int? CountyId { get; set; }

    /// <summary>
    /// 乡镇ID
    /// </summary>
    public int? TownId { get; set; }

    /// <summary>
    /// ?社区ID
    /// </summary>
    public int? VillageId { get; set; }

    /// <summary>
    /// 完整地址拼接
    /// </summary>
    public string FullAddress => string.Concat(
        Province ?? string.Empty,
        City ?? string.Empty,
        District ?? string.Empty,
        Town ?? string.Empty,
        Community ?? string.Empty,
        Detail ?? string.Empty);
}
