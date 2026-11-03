using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using System.Collections.Concurrent;

namespace NewCosmos.Services.Domain.ElderlyBenefits;

/// <summary>
/// 普惠高龄身份判类服务：读取低保/低保边缘/特困/低收入导入库数据，按身份证比对人员身份。
/// </summary>
public class ElderlyCategoryService : BaseService
{
    protected override string ServiceName => "ElderlyCategoryService";

    private readonly IDatabaseService _db;

    /// <summary>
    /// 表存在性进程内缓存（Singleton 服务持有静态不可变语义缓存，符合 AGENTS §6.3）：
    /// 表不会在运行时消失，原实现每 match 5 次 information_schema 查询 × 每人调用
    /// → 1000 人 = 10,000 次往返（首页"高龄身份升级自检"后台任务路径）。
    /// </summary>
    private static readonly ConcurrentDictionary<string, bool> TableExistsCache = new(StringComparer.Ordinal);

    public ElderlyCategoryService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    /// <summary>
    /// 按身份证比对导入库，返回身份结果（IdentityFlag + 命中来源表）。
    /// 2026-11 性能收口：① 表存在性进程缓存（首查后免查）；② 5 表探测合并为一条
    /// UNION ALL 批查（按优先级 MIN(prio) 取首个命中），单人往返 10 次 → 1~2 次。
    /// </summary>
    public async Task<Result<ElderlyIdentityResult>> MatchIdentityAsync(string idCard, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idCard))
            return Result.Failure<ElderlyIdentityResult>(ErrorCodes.VALIDATION_FAILED, "身份证号不能为空");

        // 命中优先级：低保对象（城乡）> 低保边缘 > 特困 > 低收入；任一命中即返回。
        // [SQL 白名单背书] probes 为本方法内硬编码常量表名（无外部输入），UNION ALL 片段
        // 拼接仅由该常量数组生成（check_standards.ps1 SQL1 豁免 unionParts）。
        var probes = new (string Table, string Flag, int Prio)[]
        {
            ("nc_biz_urban_subsistence_persons", ElderlyBenefitConstants.IdentityLowSubsidy, 1),
            ("nc_biz_rural_subsistence_persons", ElderlyBenefitConstants.IdentityLowSubsidy, 2),
            ("nc_biz_low_income_edge_persons", ElderlyBenefitConstants.IdentityLowIncomeEdge, 3),
            ("nc_biz_destitute_persons", ElderlyBenefitConstants.IdentityDestitute, 4),
            ("nc_biz_low_income_persons", ElderlyBenefitConstants.IdentityLowIncome, 5)
        };

        var tableToFlag = new Dictionary<int, (string Table, string Flag)>(probes.Length);
        var unionParts = new List<string>();
        foreach (var (table, flag, prio) in probes)
        {
            if (!await TableExistsAsync(table, ct)) continue;
            tableToFlag[prio] = (table, flag);
            unionParts.Add($"SELECT {prio} AS prio FROM {table} WHERE id_card = $1");
        }

        if (unionParts.Count > 0)
        {
            // COALESCE 归零：无命中时 MIN(prio) 为 NULL，ExecuteScalarAsync 非泛型版映射 long 会炸
            // [SQL 白名单背书] unionSql 仅由上方硬编码 probes 常量数组生成（无外部输入）
            var unionSql = string.Join(" UNION ALL ", unionParts);
            var sql = $"SELECT COALESCE(MIN(prio), 0) FROM ({unionSql}) p";
            var result = await _db.ExecuteScalarAsync(sql, ct, idCard);
            if (result.IsSuccess && result.Value > 0
                && tableToFlag.TryGetValue((int)result.Value, out var hit))
            {
                LogDebug($"身份证命中导入库: {hit.Table}");
                return Result.Success(new ElderlyIdentityResult(hit.Flag, hit.Table));
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
                WHERE a.deleted_at IS NULL AND a.status = $2
                  AND (a.applicant_id_card = $1
                       OR EXISTS (SELECT 1 FROM nc_biz_family_members fm
                                  WHERE fm.application_id = a.id AND fm.id_card = $1 AND fm.deleted_at IS NULL))
                  AND a.classification_result IS NOT NULL
                ORDER BY a.updated_at DESC
                LIMIT 5";
            var result = await _db.QueryAsync<string>(sql, ct, idCard, ApplicationStatusCodes.APPROVED);
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
    /// 检查导入库表是否存在（不存在时跳过，避免查询报错）。
    /// 表存在性进程内缓存：表不会运行时消失，首次查询后免往返。
    /// </summary>
    private async Task<bool> TableExistsAsync(string table, CancellationToken ct)
    {
        if (TableExistsCache.TryGetValue(table, out var cached))
            return cached;

        var sql = "SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_name = $1)";
        var result = await _db.ExecuteScalarAsync(sql, ct, table);
        var exists = result.IsSuccess && Convert.ToBoolean(result.Value);
        TableExistsCache[table] = exists;
        return exists;
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
