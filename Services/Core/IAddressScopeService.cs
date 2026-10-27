using NewCosmos.Models.Results;
using NewCosmos.Services.Domain.UserManagement;

namespace NewCosmos.Services.Core;

/// <summary>账号管辖范围（地址级联限制的唯一口径）</summary>
public sealed class AddressScope
{
    /// <summary>
    /// 账号绑定到哪一级：0=未绑区划 1=省 2=市 3=县 4=镇 5=村。
    /// 该级**及以上全部锁定**（只能选本单位的值），**以下才开放选择**。
    /// </summary>
    public int LockLevel { get; set; }

    /// <summary>是否可用于限制（false=账号未绑定任何区划 → 表单整个地址区禁用）</summary>
    public bool IsAvailable => LockLevel > 0;

    public int? ProvinceId { get; set; }
    public int? CityId { get; set; }
    public int? CountyId { get; set; }
    public int? TownId { get; set; }
    public int? VillageId { get; set; }

    public string ProvinceName { get; set; } = string.Empty;
    public string CityName { get; set; } = string.Empty;
    public string CountyName { get; set; } = string.Empty;
    public string TownName { get; set; } = string.Empty;
    public string VillageName { get; set; } = string.Empty;

    /// <summary>锁定级判定：账号层级 ≥ 该级 → 锁定（Picker 置灰禁点、只留本单位一项）</summary>
    public bool IsProvinceLocked => LockLevel >= 1;
    public bool IsCityLocked => LockLevel >= 2;
    public bool IsCountyLocked => LockLevel >= 3;
    public bool IsTownLocked => LockLevel >= 4;
    public bool IsVillageLocked => LockLevel >= 5;

    /// <summary>范围说明文案（表单顶部提示用）</summary>
    public string ScopeDescription => LockLevel switch
    {
        >= 5 => $"户籍地址限本单位管辖范围（{ProvinceName}{CityName}{CountyName}{TownName}{VillageName}）",
        >= 4 => $"户籍地址限本单位管辖范围（{ProvinceName}{CityName}{CountyName}{TownName}，村/社区可选）",
        >= 3 => $"户籍地址限本单位管辖范围（{ProvinceName}{CityName}{CountyName}，乡镇村可选）",
        >= 2 => $"户籍地址限本单位管辖范围（{ProvinceName}{CityName}，县乡村可选）",
        >= 1 => $"户籍地址限本省范围（{ProvinceName}）",
        _ => "当前账号未绑定区划，地址不可选择"
    };

    /// <summary>按组织解析锁定层级（自村向上取第一个已绑定的层级）</summary>
    public static int ComputeLockLevel(Organization org)
    {
        if (org.VillageId is > 0 && !string.IsNullOrWhiteSpace(org.VillageName)) return 5;
        if (org.TownId is > 0 && !string.IsNullOrWhiteSpace(org.TownName)) return 4;
        if (org.CountyId is > 0 && !string.IsNullOrWhiteSpace(org.CountyName)) return 3;
        if (org.CityId is > 0 && !string.IsNullOrWhiteSpace(org.CityName)) return 2;
        if (org.ProvinceId is > 0 && !string.IsNullOrWhiteSpace(org.ProvinceName)) return 1;
        return 0;
    }
}

/// <summary>账号管辖范围解析（进程内缓存一次；组织区划变更后需重启应用）</summary>
public interface IAddressScopeService
{
    /// <summary>解析当前账号管辖范围。Failure=账号未配置组织或组织查询失败。</summary>
    Task<Result<AddressScope>> ResolveAsync(CancellationToken ct = default);
}
