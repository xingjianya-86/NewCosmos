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

        // 身份证列表以单个 text[] 参数传入（= ANY($1)），参数个数与 N 无关；
        // 年度种植补贴（四合一长表）单表取最新年段：一人一分类一行，
        // 面积型（存量转换）取 area、金额型（新模板导入）取 amount，同人多来源文件按分类求和合并。
        // 金额且无面积者（地力/大豆品种等无单价分类）由调用方按 当日单价 反推或走金额型行。
        const string sql = @"
            SELECT MAX(name) AS name,
                   id_card,
                   category AS subsidy_type,
                   COALESCE(SUM(area), 0) AS area,
                   COALESCE(SUM(amount), 0) AS amount
              FROM nc_biz_annual_planting_subsidy
             WHERE id_card = ANY($1::text[])
               AND data_year = (SELECT MAX(data_year) FROM nc_biz_annual_planting_subsidy)
             GROUP BY id_card, category
            HAVING COALESCE(SUM(area), 0) > 0 OR COALESCE(SUM(amount), 0) > 0
             ORDER BY MAX(name), category";

        LogInfo($"查询补贴数据: {idCards.Count}张身份证");
        // 注意：string[] 必须包一层 object[]，否则会被 params object[] 展开成多个参数，
        // $1 将变成单个字符串而非数组，导致 ANY($1) 解析失败（42809/22P02）
        return await _db.QueryAsync<SubsidyImportRecord>(sql, ct, new object[] { idCards.ToArray() });
    }
}
