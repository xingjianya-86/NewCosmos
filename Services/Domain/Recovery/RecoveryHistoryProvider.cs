using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.Recovery;

/// <summary>
/// 追缴历史数据取数器实现（只读，不做金额推算）。
///
/// 时间轴构造口径（唯一实现，勿在 ViewModel/Service 另抄）：
///   ① 现值（Ledger）：低保/城市低保取 申请主表 与 台账 中非 0 的户月保障金，
///      生效月 = 台账 首保月(first_receive_month) 优先，退化到 申请创建月；
///      刚性支出取 申请主表.rigid_expenditure；临时救助取 confirm_amount；高龄取 issue_amount。
///   ② 保障金变更记录（ChangeRecord）：仅 nc_biz_applications 系来源
///      （lc 低保/城市低保/刚性支出——其 sourceId 即 nc_biz_applications.id），
///      取 change_type ∈ {FundChange, CategoryStop} 且 new_guarantee_amount 非空者，
///      生效月 = 变更日期所在月。分类施保（ClassifiedSubsidyReduce/Add）金额**不并入**保障金口径，故排除。
///   ③ 渐退期原标准（GracePeriod）：**仅当现值缺失**（户月保障金为 0/台账行已不存在）时补充
///      nc_biz_grace_periods.original_guarantee_amount，生效月 = 渐退期起月。
///
/// 明确不做的事（保留给人工）：不使用变更记录的 old_guarantee_amount 反推"更早标准"
/// （旧标准的生效起点无据可查，反推=编造金额），因此历史不全时会返回不足的时间轴。
/// </summary>
public class RecoveryHistoryProvider : BaseService, IRecoveryHistoryProvider
{
    private readonly IDatabaseService _db;

    protected override string ServiceName => "RecoveryHistoryProvider";

    public RecoveryHistoryProvider(ILoggerService logger, IDatabaseService db) : base(logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    // ── 现值（台账 + 申请主表） ──

    private const string LedgerRuralSql = @"
        SELECT COALESCE(NULLIF(a.household_monthly_guarantee_amount, 0),
                        NULLIF(f.monthly_guarantee_amount, 0), 0) AS monthly_amount,
               COALESCE(f.first_receive_month, to_char(a.created_at, 'YYYY-MM')) AS effective_month,
               'Ledger' AS source
        FROM nc_biz_applications a
        LEFT JOIN nc_biz_rural_subsistence_families f ON f.id = a.source_id
        WHERE a.id = $1
        LIMIT 1";

    private const string LedgerUrbanSql = @"
        SELECT COALESCE(NULLIF(a.household_monthly_guarantee_amount, 0),
                        NULLIF(f.monthly_guarantee_amount, 0), 0) AS monthly_amount,
               COALESCE(f.first_receive_month, to_char(a.created_at, 'YYYY-MM')) AS effective_month,
               'Ledger' AS source
        FROM nc_biz_applications a
        LEFT JOIN nc_biz_urban_subsistence_families f ON f.id = a.source_id
        WHERE a.id = $1
        LIMIT 1";

    private const string LedgerRigidSql = @"
        SELECT COALESCE(a.rigid_expenditure, 0) AS monthly_amount,
               COALESCE(left(f.include_month, 7), to_char(a.created_at, 'YYYY-MM')) AS effective_month,
               'Ledger' AS source
        FROM nc_biz_applications a
        LEFT JOIN nc_biz_rigid_expenditure_families f ON f.id = a.source_id
        WHERE a.id = $1
        LIMIT 1";

    private const string LedgerTempReliefSql = @"
        SELECT COALESCE(confirm_amount, 0) AS monthly_amount,
               COALESCE(to_char(apply_date, 'YYYY-MM'), to_char(created_at, 'YYYY-MM')) AS effective_month,
               'Ledger' AS source
        FROM nc_biz_temp_relief_applications
        WHERE id = $1
        LIMIT 1";

    private const string LedgerElderlySql = @"
        SELECT COALESCE(issue_amount, 0) AS monthly_amount,
               COALESCE(to_char(apply_date, 'YYYY-MM'), to_char(created_at, 'YYYY-MM')) AS effective_month,
               'Ledger' AS source
        FROM nc_biz_elderly_applications
        WHERE id = $1
        LIMIT 1";

    // ── 保障金变更记录（生效月 = 变更月） ──

    private const string FundChangeSql = @"
        SELECT to_char(change_date, 'YYYY-MM') AS effective_month,
               new_guarantee_amount AS monthly_amount,
               'ChangeRecord' AS source
        FROM nc_biz_change_records
        WHERE application_id = $1
          AND deleted_at IS NULL
          AND change_date IS NOT NULL
          AND new_guarantee_amount IS NOT NULL
          AND change_type = ANY($2)
        ORDER BY change_date ASC, id ASC";

    // ── 渐退期原保障金（仅现值缺失时兜底） ──

    private const string GracePeriodSql = @"
        SELECT to_char(start_date, 'YYYY-MM') AS effective_month,
               original_guarantee_amount AS monthly_amount,
               'GracePeriod' AS source
        FROM nc_biz_grace_periods
        WHERE application_id = $1
          AND deleted_at IS NULL
          AND start_date IS NOT NULL
          AND original_guarantee_amount IS NOT NULL
          AND original_guarantee_amount > 0
        ORDER BY start_date ASC, id ASC";

    /// <inheritdoc/>
    public async Task<Result<List<RecoveryMonthlyStandardPoint>>> GetMonthlyStandardsAsync(
        string sourceType, long sourceId, CancellationToken ct = default)
    {
        try
        {
            LogInfo($"获取追缴历史月标准: sourceType={sourceType}, sourceId={sourceId}");

            var points = new List<RecoveryMonthlyStandardPoint>();

            // ① 现值（台账优先）
            var ledgerSql = ResolveLedgerSql(sourceType);
            if (ledgerSql != null)
            {
                var ledger = await _db.QueryAsync<RecoveryMonthlyStandardPoint>(ledgerSql, ct, sourceId);
                if (!ledger.IsSuccess)
                {
                    LogWarn($"读取现值失败: {ledger.Message}");
                    return Result.Failure<List<RecoveryMonthlyStandardPoint>>(
                        ledger.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, ledger.Message ?? "读取来源档案现值失败");
                }

                // 现值为 0 视为"无现值"，不作为时间轴锚点（否则会把 0 当历史标准）
                var hit = ledger.Value.FirstOrDefault(p => p.MonthlyAmount > 0);
                if (hit != null)
                    points.Add(hit);
            }

            var applicationBacked = IsApplicationBackedSource(sourceType);

            // ② 保障金变更记录（仅申请主表系来源；临时救助/高龄的 sourceId 不是申请ID，绝不能拿去比对）
            if (applicationBacked)
            {
                var changeTypes = new[]
                {
                    DictionaryConstants.ChangeType.FUND_CHANGE,
                    DictionaryConstants.ChangeType.CATEGORY_STOP
                };

                var changes = await _db.QueryAsync<RecoveryMonthlyStandardPoint>(
                    FundChangeSql, ct, sourceId, changeTypes);

                if (!changes.IsSuccess)
                {
                    LogWarn($"读取保障金变更记录失败: {changes.Message}");
                    return Result.Failure<List<RecoveryMonthlyStandardPoint>>(
                        changes.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, changes.Message ?? "读取保障金变更记录失败");
                }

                points.AddRange(changes.Value.Where(p => !string.IsNullOrWhiteSpace(p.EffectiveMonth)));
            }

            // ③ 现值缺失时补渐退期原标准
            if (points.Count == 0 && applicationBacked)
            {
                var grace = await _db.QueryAsync<RecoveryMonthlyStandardPoint>(GracePeriodSql, ct, sourceId);
                if (grace.IsSuccess)
                    points.AddRange(grace.Value.Where(p => !string.IsNullOrWhiteSpace(p.EffectiveMonth)));
                else
                    LogWarn($"读取渐退期原标准失败: {grace.Message}");
            }

            // 归一：按生效月升序；同月保留最后加入者（变更记录 > 现值 > 渐退期）
            var ordered = points
                .Where(p => !string.IsNullOrWhiteSpace(p.EffectiveMonth) && p.MonthlyAmount >= 0)
                .OrderBy(p => p.EffectiveMonth, StringComparer.Ordinal)
                .GroupBy(p => p.EffectiveMonth)
                .Select(g => g.Last())
                .ToList();

            LogInfo($"历史月标准时间轴: {ordered.Count} 个生效点");

            return Result.Success(ordered);
        }
        catch (Exception ex)
        {
            LogException(ex, "获取追缴历史月标准");
            return Result.FromException<List<RecoveryMonthlyStandardPoint>>(ex);
        }
    }

    /// <summary>
    /// 来源类型 → 现值取数 SQL；不支持的来源返回 null（无历史可取）。
    /// </summary>
    private static string? ResolveLedgerSql(string sourceType) => sourceType switch
    {
        RecoveryConstants.SOURCE_TYPE_RURAL_SUBSISTENCE => LedgerRuralSql,
        RecoveryConstants.SOURCE_TYPE_URBAN_SUBSISTENCE => LedgerUrbanSql,
        RecoveryConstants.SOURCE_TYPE_RIGID_EXPENDITURE => LedgerRigidSql,
        RecoveryConstants.SOURCE_TYPE_TEMP_RELIEF => LedgerTempReliefSql,
        RecoveryConstants.SOURCE_TYPE_ELDERLY => LedgerElderlySql,
        _ => null
    };

    /// <summary>
    /// 来源是否以 nc_biz_applications.id 为 sourceId（决定能否用变更记录/渐退期按申请ID取数）。
    /// </summary>
    private static bool IsApplicationBackedSource(string sourceType) =>
        sourceType is RecoveryConstants.SOURCE_TYPE_RURAL_SUBSISTENCE
            or RecoveryConstants.SOURCE_TYPE_URBAN_SUBSISTENCE
            or RecoveryConstants.SOURCE_TYPE_RIGID_EXPENDITURE;
}
