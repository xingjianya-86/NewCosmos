using NewCosmos.Models.Entities;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.System;

/// <summary>
/// 地区服务接口
/// 提供黑龙江省行政区划数据查询（地级市、县区、乡镇、村/社区四级
/// </summary>
public interface IRegionService
{
    #region Schema管理

    Task<Result> EnsureTablesExistAsync(CancellationToken ct = default);

    Task<Result> SyncSchemaAsync(IProgress<ProgressContext> progress = null, CancellationToken ct = default);

    #endregion

    #region 数据初始化
    Task<Result> InitializeFromSeedAsync(IProgress<ProgressContext> progress = null, CancellationToken ct = default);

    Task<Result> ClearTablesAsync(CancellationToken ct = default);

    #endregion

    #region 地级市查询
    /// <summary>省级行政区列表（2026-10-26 真级联省；34 个省级行政单位）</summary>
    Task<Result<List<RegionProvince>>> GetProvincesAsync(CancellationToken ct = default);

    /// <summary>按省取地级市（选省 → 筛市 的级联）；省ID非法返回空列表而非报错</summary>
    Task<Result<List<RegionCity>>> GetCitiesByProvinceIdAsync(int provinceId, CancellationToken ct = default);

    Task<Result<List<RegionCity>>> GetCitiesAsync(CancellationToken ct = default);

    Task<Result<RegionCity>> GetCityByIdAsync(int id, CancellationToken ct = default);

    Task<Result<RegionCity>> GetCityByNameAsync(string cityName, CancellationToken ct = default);

    #endregion

    #region 县区查询

    Task<Result<List<RegionCounty>>> GetCountiesAsync(CancellationToken ct = default);

    Task<Result<List<RegionCounty>>> GetCountiesByCityAsync(string cityName, CancellationToken ct = default);

    Task<Result<List<RegionCounty>>> GetCountiesByCityIdAsync(int cityId, CancellationToken ct = default);

    Task<Result<RegionCounty>> GetCountyByIdAsync(int id, CancellationToken ct = default);

    Task<Result<RegionCounty>> GetCountyByCodeAsync(string code, CancellationToken ct = default);

    #endregion

    #region 乡镇查询

    Task<Result<List<RegionTown>>> GetTownsByCountyIdAsync(int countyId, CancellationToken ct = default);

    Task<Result<RegionTown>> GetTownByIdAsync(int id, CancellationToken ct = default);

    Task<Result<List<RegionTown>>> SearchTownsAsync(string keyword, CancellationToken ct = default);

    #endregion

    #region ?社区查询

    Task<Result<List<RegionVillage>>> GetVillagesByTownIdAsync(int townId, CancellationToken ct = default);

    Task<Result<RegionVillage>> GetVillageByIdAsync(int id, CancellationToken ct = default);

    Task<Result<RegionVillage>> GetVillageByCodeAsync(string villageCode, CancellationToken ct = default);

    /// <summary>
    /// 按编码**批量**查村/社区（一次取回），供地址预热使用，替代逐条
    /// <see cref="GetVillageByCodeAsync"/> 造成的 N+1。
    /// 只返回查到的实体；查不到的记入负缓存但不报错；查询失败返回 Failure（不折叠成空集合）。
    /// </summary>
    Task<Result<List<RegionVillage>>> GetVillagesByCodesAsync(IEnumerable<string> villageCodes, CancellationToken ct = default);

    Task<Result<List<RegionVillage>>> SearchVillagesAsync(string keyword, CancellationToken ct = default);

    #endregion

    #region 地级市管理（增删改）

    Task<Result<RegionCity>> CreateCityAsync(RegionCitySaveRequest request, CancellationToken ct = default);

    Task<Result<RegionCity>> UpdateCityAsync(RegionCitySaveRequest request, CancellationToken ct = default);

    Task<Result> DeleteCityAsync(int cityId, CancellationToken ct = default);

    #endregion

    #region 县区管理(增删改）

    Task<Result<RegionCounty>> CreateCountyAsync(RegionCountySaveRequest request, CancellationToken ct = default);

    Task<Result<RegionCounty>> UpdateCountyAsync(RegionCountySaveRequest request, CancellationToken ct = default);

    Task<Result> DeleteCountyAsync(int countyId, CancellationToken ct = default);

    #endregion

    #region 乡镇管理(增删改）

    Task<Result<RegionTown>> CreateTownAsync(RegionTownSaveRequest request, CancellationToken ct = default);

    Task<Result<RegionTown>> UpdateTownAsync(RegionTownSaveRequest request, CancellationToken ct = default);

    Task<Result> DeleteTownAsync(int townId, CancellationToken ct = default);

    #endregion

    #region ?社区管理(增删改）

    Task<Result<RegionVillage>> CreateVillageAsync(RegionVillageSaveRequest request, CancellationToken ct = default);

    Task<Result<RegionVillage>> UpdateVillageAsync(RegionVillageSaveRequest request, CancellationToken ct = default);

    Task<Result> DeleteVillageAsync(int villageId, CancellationToken ct = default);

    #endregion

    #region 树形查询

    Task<Result<List<RegionCounty>>> GetCountiesWithTownsAsync(CancellationToken ct = default);

    Task<Result<Dictionary<string, List<RegionTown>>>> GetAllTownsGroupedByCountyAsync(CancellationToken ct = default);

    #endregion
}
