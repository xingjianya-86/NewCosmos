using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.SocialAssistance;

public class SubsidyDataService : BaseService, ISubsidyDataService
{
    protected override string ServiceName => "SubsidyDataService";
    private readonly IDatabaseService _db;

    public SubsidyDataService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<Result<List<SubsidyImportRecord>>> GetSubsidiesByIdCardsAsync(
        List<string> idCards, CancellationToken ct = default)
    {
        if (idCards == null || idCards.Count == 0)
            return Result.Success(new List<SubsidyImportRecord>());

        // 身份证列表以单个 text[] 参数传入（= ANY($1)），参数个数与 N 无关，
        // 不再受 6N 参数展开的数量上限限制；
        // nc_biz_planting_subsidy 由 4 次扫描 + 4 次重复的 MAX(data_year) 子查询
        // 合并为单次扫描 + LATERAL VALUES 行转列。
        const string sql = @"
            SELECT name, id_card, '地力补贴' as subsidy_type, 0 as area, subsidy_amount as amount
            FROM nc_biz_soil_subsidy
            WHERE id_card = ANY($1::text[]) AND data_year = (SELECT MAX(data_year) FROM nc_biz_soil_subsidy)

            UNION ALL

            SELECT name, id_card, '轮作补贴' as subsidy_type, rotation_area as area, subsidy_amount as amount
            FROM nc_biz_rotation_subsidy
            WHERE id_card = ANY($1::text[]) AND data_year = (SELECT MAX(data_year) FROM nc_biz_rotation_subsidy)

            UNION ALL

            SELECT p.name, p.id_card, t.subsidy_type, t.area, 0 as amount
            FROM nc_biz_planting_subsidy p
            CROSS JOIN LATERAL (VALUES
                ('玉米补贴',   p.corn_acreage),
                ('大豆补贴',   p.soybean_acreage),
                ('地表水水稻', p.rice_surface_water_acreage),
                ('地下水水稻', p.rice_groundwater_acreage)
            ) AS t(subsidy_type, area)
            WHERE p.id_card = ANY($1::text[])
              AND p.data_year = (SELECT MAX(data_year) FROM nc_biz_planting_subsidy)
              AND t.area > 0

            ORDER BY name, subsidy_type";

        LogInfo($"查询补贴数据: {idCards.Count}张身份证");
        // 注意：string[] 必须包一层 object[]，否则会被 params object[] 展开成多个参数，
        // $1 将变成单个字符串而非数组，导致 ANY($1) 解析失败（42809/22P02）
        return await _db.QueryAsync<SubsidyImportRecord>(sql, ct, new object[] { idCards.ToArray() });
    }
}
