using NewCosmos.Models.Entities;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.System;

/// <summary>
/// 地区服务接口
/// 提供黑龙江省行政区划数据查询（地级市、县区、乡镇、村/社区四级    /// </summary>
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
