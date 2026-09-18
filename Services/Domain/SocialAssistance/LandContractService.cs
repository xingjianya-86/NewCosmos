using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.SocialAssistance;

public class LandContractService : BaseService, ILandContractService
{
    protected override string ServiceName => "LandContractService";
    private readonly IDatabaseService _db;

    public LandContractService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<Result<List<LandConfirmationRecord>>> GetRecordsByNamesAsync(
        List<string> names, CancellationToken ct = default)
    {
        if (names == null || names.Count == 0)
            return Result.Success(new List<LandConfirmationRecord>());

        var placeholders = string.Join(", ", names.Select((_, i) => $"${i + 1}"));
        var sql = $@"
            SELECT c.contractor_name as member_name,
                   c.contractor_name as member_id_card,
                   p.plot_name as land_plot_info,
                   p.plot_code,
                   p.contract_area,
                   p.measured_area,
                   COALESCE(p.measured_area, p.contract_area) as land_area
            FROM nc_biz_land_contract_contractor c
            LEFT JOIN nc_biz_land_contract_plot p ON c.id = p.contractor_id
            WHERE c.contractor_name IN ({placeholders})
            ORDER BY c.contractor_name, p.plot_code";

        LogInfo($"查询土地确权: names={string.Join(",", names)}");
        return await _db.QueryAsync<LandConfirmationRecord>(sql, ct, names.Cast<object>().ToArray());
    }

    // 注：原 GetRecordsByIdCardsAsync 已删除——nc_biz_land_contract_contractor 表没有身份证列，
    // 该方法是 GetRecordsByNamesAsync 的复制粘贴（WHERE 仍按姓名匹配），传入身份证永远返回空，
    // 且全项目无调用方。如需按身份证查询，需先给表补充 id_card 列并回填数据。
}
