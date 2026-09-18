using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.ElderlyBenefits;

/// <summary>
/// 普惠高龄身份判类服务：读取低保/低保边缘/特困/低收入导入库数据，按身份证比对人员身份。
/// </summary>
public class ElderlyCategoryService : BaseService
{
    protected override string ServiceName => "ElderlyCategoryService";

    private readonly IDatabaseService _db;

    public ElderlyCategoryService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    /// <summary>
    /// 按身份证比对导入库，返回身份结果（IdentityFlag + 命中来源表）。
    /// 单表单独 EXISTS 查询并对"表不存在"容错（导入库未初始化时跳过）。
    /// </summary>
    public async Task<Result<ElderlyIdentityResult>> MatchIdentityAsync(string idCard, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idCard))
            return Result.Failure<ElderlyIdentityResult>(ErrorCodes.VALIDATION_FAILED, "身份证号不能为空");

        // 命中优先级：低保对象（城乡）> 低保边缘 > 特困 > 低收入；任一命中即返回
        var probes = new[]
        {
            new { Table = "nc_biz_urban_subsistence_persons", Flag = ElderlyBenefitConstants.IdentityLowSubsidy },
            new { Table = "nc_biz_rural_subsistence_persons", Flag = ElderlyBenefitConstants.IdentityLowSubsidy },
            new { Table = "nc_biz_low_income_edge_persons", Flag = ElderlyBenefitConstants.IdentityLowIncomeEdge },
            new { Table = "nc_biz_destitute_persons", Flag = ElderlyBenefitConstants.IdentityDestitute },
            new { Table = "nc_biz_low_income_persons", Flag = ElderlyBenefitConstants.IdentityLowIncome }
        };

        foreach (var probe in probes)
        {
            var exists = await TableExistsAsync(probe.Table, ct);
            if (!exists) continue;

            var sql = $"SELECT EXISTS(SELECT 1 FROM {probe.Table} WHERE id_card = $1)";
            var result = await _db.ExecuteScalarAsync(sql, ct, idCard);
            if (result.IsFailure) continue;

            if (Convert.ToBoolean(result.Value))
            {
                LogDebug($"身份证命中导入库: {probe.Table}");
                return Result.Success(new ElderlyIdentityResult(probe.Flag, probe.Table));
            }
        }

        // 未命中导入台账 → 扩展查低保申请库（nc_biz_applications）：
        // 身份证为已审批(Approved)档案的户主或家庭成员即按分类识别身份（低保>低保边缘>特困，刚性支出不算）。
        var appIdentity = await MatchApplicationIdentityAsync(idCard, ct);
        if (appIdentity != null)
            return Result.Success(appIdentity);

        return Result.Success(new ElderlyIdentityResult(ElderlyBenefitConstants.IdentityNone, string.Empty));
    }

    /// <summary>
    /// 从低保申请库（nc_biz_applications）识别身份：已审批档案的户主或家庭成员按 classification_result 映射。
    /// 优先级：最低生活保障 > 低保边缘 > 特困（刚性支出等不视为高身份）；未命中返回 null。
    /// </summary>
    private async Task<ElderlyIdentityResult?> MatchApplicationIdentityAsync(string idCard, CancellationToken ct)
    {
        try
        {
            const string sql = @"
                SELECT a.classification_result
                FROM nc_biz_applications a
                WHERE a.deleted_at IS NULL AND a.status = 'Approved'
                  AND (a.applicant_id_card = $1
                       OR EXISTS (SELECT 1 FROM nc_biz_family_members fm
                                  WHERE fm.application_id = a.id AND fm.id_card = $1 AND fm.deleted_at IS NULL))
                  AND a.classification_result IS NOT NULL
                ORDER BY a.updated_at DESC
                LIMIT 5";
            var result = await _db.QueryAsync<string>(sql, ct, idCard);
            if (result.IsFailure || result.Value == null || result.Value.Count == 0)
                return null;

            foreach (var code in result.Value)
            {
                if (ClassificationConstants.IsCodeSubsistence(code))
                    return new ElderlyIdentityResult(ElderlyBenefitConstants.IdentityLowSubsidy, "nc_biz_applications");
            }
            foreach (var code in result.Value)
            {
                if (ClassificationConstants.IsCodeLowIncome(code))
                    return new ElderlyIdentityResult(ElderlyBenefitConstants.IdentityLowIncomeEdge, "nc_biz_applications");
            }
            foreach (var code in result.Value)
            {
                if (ClassificationConstants.IsCodeDestitute(code))
                    return new ElderlyIdentityResult(ElderlyBenefitConstants.IdentityDestitute, "nc_biz_applications");
            }
            return null;
        }
        catch (Exception ex)
        {
            LogWarn($"申请库身份比对失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 检查导入库表是否存在（不存在时跳过，避免查询报错）
    /// </summary>
    private async Task<bool> TableExistsAsync(string table, CancellationToken ct)
    {
        var sql = "SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_name = $1)";
        var result = await _db.ExecuteScalarAsync(sql, ct, table);
        return result.IsSuccess && Convert.ToBoolean(result.Value);
    }
}

/// <summary>
/// 身份比对结果
/// </summary>
public class ElderlyIdentityResult
{
    public ElderlyIdentityResult(string identityFlag, string sourceTable)
    {
        IdentityFlag = identityFlag;
        SourceTable = sourceTable;
    }

    /// <summary>身份比对结果</summary>
    public string IdentityFlag { get; }

    /// <summary>命中导入库表名</summary>
    public string SourceTable { get; }
}
