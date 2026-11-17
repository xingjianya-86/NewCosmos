using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.BankFlow;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 快速核验服务：按身份证（+可选姓名）并行聚合查询系统内已有台账。
/// 【只读】不写任何表；【口径】各组独立成败，单组失败只记原因不阻断其余组（§7：失败显式暴露，不以空集冒充无数据）；
/// SQL 全部 $1 位置参数 + 明确列 + LIMIT（§8）。
/// </summary>
public class QuickVerifyService : BaseService, IQuickVerifyService
{
    protected override string ServiceName => "QuickVerifyService";

    private readonly IDatabaseService _db;
    private readonly IPersonSearchService _personSearchService;
    private readonly ISubsidyDataService _subsidyDataService;
    private readonly IBankFlowQueryService _bankFlowQueryService;

    public QuickVerifyService(
        IDatabaseService db,
        IPersonSearchService personSearchService,
        ISubsidyDataService subsidyDataService,
        IBankFlowQueryService bankFlowQueryService,
        ILoggerService logger) : base(logger)
    {
        _db = db;
        _personSearchService = personSearchService;
        _subsidyDataService = subsidyDataService;
        _bankFlowQueryService = bankFlowQueryService;
    }

    public async Task<Result<QuickVerifyBundle>> VerifyAsync(string idCard, string? name, CancellationToken ct = default)
    {
        idCard = (idCard ?? string.Empty).Trim();
        if (!IdCardValidator.IsValid(idCard))
            return Result.Failure<QuickVerifyBundle>(ErrorCodes.VALIDATION_FAILED, "身份证号格式不正确");
        // 校验位允许小写 x，但库存与流水字典键均为大写：入口统一规范化，避免大小写错配查空
        idCard = idCard.ToUpperInvariant();

        // 业务审计点：核验属于按身份证批量调阅敏感台账，必须留痕（身份证脱敏，§10）
        Logger.LogBusiness("快速核验查询", ("IdCard", DataMasker.MaskIdCard(idCard)));

        var bundle = new QuickVerifyBundle { IdCard = idCard };

        // ── 匹配姓名：优先输入值，否则人员库反查（土地承包台账无身份证列，只能按姓名匹配）──
        var queryName = (name ?? string.Empty).Trim();
        if (queryName.Length == 0)
        {
            var nameResult = await ResolveNameAsync(idCard, ct);
            if (nameResult.IsFailure)
                return Result.Failure<QuickVerifyBundle>(
                    nameResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, nameResult.Message!);
            queryName = nameResult.Value ?? string.Empty;
            bundle.NameSource = queryName.Length > 0 ? "人员库反查" : string.Empty;
        }
        else
        {
            bundle.NameSource = "输入";
        }
        bundle.QueryName = queryName;

        // ── 并行查询各组（单组异常在 LoadGroupAsync 内收敛为该组 FailureReason）──
        await Task.WhenAll(
            LoadGroupAsync(bundle.ContractLand, () => QueryContractLandAsync(queryName, ct),
                "按姓名匹配（承包台账无身份证列），存在同名风险"),
            LoadGroupAsync(bundle.HistoryLand, () => QueryHistoryLandAsync(idCard, ct), "按身份证匹配"),
            LoadGroupAsync(bundle.Subsidies, () => QuerySubsidiesAsync(idCard, ct), "按身份证匹配"),
            LoadGroupAsync(bundle.SecurityIdentities, () => QuerySecurityIdentitiesAsync(idCard, ct), "按身份证匹配"),
            LoadGroupAsync(bundle.Assets, () => QueryAssetsAsync(idCard, ct), "按身份证反查历史申请"),
            LoadGroupAsync(bundle.BankFlows, () => QueryBankFlowsAsync(idCard, ct), "按身份证匹配（最新批次）"),
            LoadGroupAsync(bundle.AssetReports, () => QueryAssetReportsAsync(idCard, ct), "按身份证匹配"),
            LoadGroupAsync(bundle.Deaths, () => QueryDeathsAsync(idCard, ct), "按身份证匹配"),
            LoadGroupAsync(bundle.TempReliefs, () => QueryTempReliefsAsync(idCard, ct), "按身份证匹配"),
            LoadGroupAsync(bundle.ElderlyBenefits, () => QueryElderlyAsync(idCard, ct), "按身份证匹配"));

        // 全部组失败 = 数据库级故障：整体显式失败，不把"全空"当成功返回
        if (bundle.AllGroupsFailed)
        {
            var firstFailure = FirstFailureReason(bundle);
            return Result.Failure<QuickVerifyBundle>(ErrorCodes.DB_QUERY_ERROR,
                $"核验查询失败：{firstFailure}");
        }

        return Result.Success(bundle);
    }

    #region 组装载与姓名反查

    /// <summary>装载单组：异常收敛为该组 Failure（UI 显示原因），绝不吞成空集合</summary>
    private static async Task LoadGroupAsync<T>(
        QuickVerifyGroup<T> group, Func<Task<Result<List<T>>>> loader, string matchNote)
    {
        group.MatchNote = matchNote;
        try
        {
            var result = await loader();
            group.IsLoaded = true;
            if (result.IsFailure)
            {
                group.IsFailure = true;
                group.FailureReason = string.IsNullOrWhiteSpace(result.Message) ? "查询失败" : result.Message;
            }
            else
            {
                group.Items = result.Value ?? new List<T>();
            }
        }
        catch (Exception ex)
        {
            group.IsLoaded = true;
            group.IsFailure = true;
            group.FailureReason = ex.Message;
        }
    }

    private static string FirstFailureReason(QuickVerifyBundle b)
    {
        var reasons = new[]
        {
            b.ContractLand.FailureReason, b.HistoryLand.FailureReason, b.Subsidies.FailureReason,
            b.SecurityIdentities.FailureReason, b.Assets.FailureReason, b.BankFlows.FailureReason,
            b.AssetReports.FailureReason, b.Deaths.FailureReason, b.TempReliefs.FailureReason,
            b.ElderlyBenefits.FailureReason
        };
        return reasons.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r)) ?? "未知错误";
    }

    /// <summary>反查姓名：人员库成员表优先，其次申请表（供土地台账按姓名匹配）</summary>
    private async Task<Result<string>> ResolveNameAsync(string idCard, CancellationToken ct)
    {
        const string memberSql = @"
            SELECT name FROM nc_biz_family_members
             WHERE id_card = $1 AND deleted_at IS NULL AND COALESCE(name,'') <> ''
             ORDER BY is_applicant DESC, id ASC
             LIMIT 1";
        var memberResult = await _db.QueryAsync<string>(memberSql, ct, idCard);
        if (memberResult.IsFailure)
            return Result.Failure<string>(memberResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"人员库姓名反查失败: {memberResult.Message}");

        var name = memberResult.Value?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(name))
            return Result.Success(name.Trim());

        const string appSql = @"
            SELECT applicant_name FROM nc_biz_applications
             WHERE applicant_id_card = $1 AND deleted_at IS NULL AND COALESCE(applicant_name,'') <> ''
             ORDER BY created_at DESC
             LIMIT 1";
        var appResult = await _db.QueryAsync<string>(appSql, ct, idCard);
        if (appResult.IsFailure)
            return Result.Failure<string>(appResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"申请表姓名反查失败: {appResult.Message}");

        return Result.Success(appResult.Value?.FirstOrDefault()?.Trim() ?? string.Empty);
    }

    #endregion

    #region ① 土地·外部承包台账（按姓名）

    private async Task<Result<List<LandContractRow>>> QueryContractLandAsync(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<List<LandContractRow>>(ErrorCodes.VALIDATION_FAILED,
                "缺少姓名，无法匹配土地承包台账（台账无身份证列）");

        const string sql = @"
            SELECT c.contractor_name AS contractor_name,
                   COALESCE(c.employer_name,'') AS employer_name,
                   COALESCE(c.data_year,0) AS data_year,
                   COALESCE(p.plot_name,'') AS plot_name,
                   COALESCE(p.plot_code,'') AS plot_code,
                   COALESCE(p.contract_area,0) AS contract_area,
                   COALESCE(p.measured_area,0) AS measured_area,
                   COALESCE(p.measured_area, p.contract_area, 0) AS land_area
              FROM nc_biz_land_contract_contractor c
              LEFT JOIN nc_biz_land_contract_plot p ON c.id = p.contractor_id
             WHERE c.contractor_name = $1
             ORDER BY c.data_year DESC NULLS LAST, p.plot_code
             LIMIT 500";

        var result = await _db.QueryAsync<LandContractRow>(sql, ct, name);
        if (result.IsFailure)
            return Result.Failure<List<LandContractRow>>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"土地承包台账查询失败: {result.Message}");

        LogInfo($"土地承包台账命中 {result.Value?.Count ?? 0} 条（按姓名匹配）");
        return Result.Success(result.Value ?? new List<LandContractRow>());
    }

    #endregion

    #region ② 土地·历史申请确权记录（按身份证）

    private async Task<Result<List<LandHistoryRow>>> QueryHistoryLandAsync(string idCard, CancellationToken ct)
    {
        const string sql = @"
            SELECT r.member_name AS member_name,
                   COALESCE(r.plot_code,'') AS plot_code,
                   COALESCE(r.land_plot_info,'') AS land_plot_info,
                   COALESCE(r.land_area,0) AS land_area,
                   COALESCE(r.land_usage,'') AS land_usage,
                   lc.application_id AS application_id
              FROM nc_biz_land_confirmation_records r
              JOIN nc_biz_land_confirmations lc ON r.confirmation_id = lc.id
             WHERE r.member_id_card = $1
               AND r.deleted_at IS NULL
               AND lc.deleted_at IS NULL
             ORDER BY lc.application_id DESC
             LIMIT 200";

        var result = await _db.QueryAsync<LandHistoryRow>(sql, ct, idCard);
        if (result.IsFailure)
            return Result.Failure<List<LandHistoryRow>>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"历史土地确权查询失败: {result.Message}");

        return Result.Success(result.Value ?? new List<LandHistoryRow>());
    }

    #endregion

    #region ③ 种植补贴（复用权威服务）

    private async Task<Result<List<SubsidyRow>>> QuerySubsidiesAsync(string idCard, CancellationToken ct)
    {
        var result = await _subsidyDataService.GetSubsidiesByIdCardsAsync(new List<string> { idCard }, ct);
        if (result.IsFailure)
            return Result.Failure<List<SubsidyRow>>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"种植补贴查询失败: {result.Message}");

        var rows = (result.Value ?? new List<SubsidyImportRecord>())
            .Select(r => new SubsidyRow
            {
                Name = r.Name,
                SubsidyType = r.SubsidyType,
                Area = r.Area,
                Amount = r.Amount
            }).ToList();
        return Result.Success(rows);
    }

    #endregion

    #region ④ 历史保障身份（复用 PersonSearchService）

    private async Task<Result<List<SecurityIdentityRow>>> QuerySecurityIdentitiesAsync(string idCard, CancellationToken ct)
    {
        var result = await _personSearchService.SearchByIdCardAsync(idCard, ct);
        if (result.IsFailure)
            return Result.Failure<List<SecurityIdentityRow>>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"保障身份查询失败: {result.Message}");

        var rows = (result.Value ?? new List<PersonSearchResult>())
            .Select(p => new SecurityIdentityRow
            {
                SourceDisplay = p.SourceDisplay,
                Name = p.Name,
                // PersonSearchResult 实体未映射 relationship 列：从补查的家庭成员里取该人的关系
                Relationship = p.FamilyMembers?.FirstOrDefault(m =>
                        string.Equals(m.IdCard?.Trim(), idCard, StringComparison.OrdinalIgnoreCase))
                    ?.RelationshipToHead ?? string.Empty,
                Phone = p.Phone,
                Address = string.Join("", new[] { p.City, p.District, p.Town, p.Village }
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
                FamilyMemberCount = p.FamilyMembers?.Count ?? 0
            }).ToList();
        return Result.Success(rows);
    }

    #endregion

    #region ⑤ 资产类（车辆/房产/金融资产：身份证 → 申请ID 两路反查）

    private async Task<Result<List<AssetRow>>> QueryAssetsAsync(string idCard, CancellationToken ct)
    {
        // 两路取申请ID：户主路径（applications.applicant_id_card）+ 成员路径（family_members.id_card）
        const string idSql = @"
            SELECT id FROM nc_biz_applications
             WHERE applicant_id_card = $1 AND deleted_at IS NULL
            UNION
            SELECT application_id FROM nc_biz_family_members
             WHERE id_card = $1 AND deleted_at IS NULL
             LIMIT 200";

        var idResult = await _db.QueryAsync<long>(idSql, ct, idCard);
        if (idResult.IsFailure)
            return Result.Failure<List<AssetRow>>(idResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"资产查询失败（申请索引）: {idResult.Message}");

        var appIds = (idResult.Value ?? new List<long>()).Distinct().ToArray();
        if (appIds.Length == 0)
            return Result.Success(new List<AssetRow>());

        const string vehicleSql = @"
            SELECT '车辆' AS category,
                   COALESCE(NULLIF(TRIM(CONCAT_WS(' ', v.vehicle_type, v.brand, v.model)), ''), '车辆') AS title,
                   COALESCE(NULLIF(v.license_plate,''), '未录车牌') AS detail,
                   COALESCE(v.estimated_value, v.purchase_price, 0) AS amount
              FROM nc_biz_vehicles v
             WHERE v.application_id = ANY($1::bigint[]) AND v.deleted_at IS NULL
             ORDER BY v.id DESC
             LIMIT 100";

        const string propertySql = @"
            SELECT '房产' AS category,
                   COALESCE(NULLIF(p.property_type,''), '房产') AS title,
                   COALESCE(NULLIF(p.address,''), '未录地址') AS detail,
                   COALESCE(p.estimated_value, 0) AS amount
              FROM nc_biz_properties p
             WHERE p.application_id = ANY($1::bigint[]) AND p.deleted_at IS NULL
             ORDER BY p.id DESC
             LIMIT 100";

        const string financeSql = @"
            SELECT '金融资产' AS category,
                   '现金/存款/证券/保险' AS title,
                   COALESCE(NULLIF(CONCAT_WS('；',
                       CASE WHEN f.has_cash THEN '现金 ' || f.cash_amount::text END,
                       CASE WHEN f.has_bank_deposit THEN '存款 ' || f.bank_deposit_amount::text END,
                       CASE WHEN f.has_securities THEN '证券 ' || f.securities_amount::text END,
                       CASE WHEN f.has_commercial_insurance THEN '商保 ' || f.commercial_insurance_amount::text END
                   ), ''), '未录金融资产') AS detail,
                   COALESCE(f.cash_amount,0) + COALESCE(f.bank_deposit_amount,0)
                       + COALESCE(f.securities_amount,0) + COALESCE(f.commercial_insurance_amount,0) AS amount
              FROM nc_biz_financial_assets f
             WHERE f.application_id = ANY($1::bigint[]) AND f.deleted_at IS NULL
             ORDER BY f.id DESC
             LIMIT 50";

        // 三条查询同参数并行（批查 = ANY($1)，禁止循环内逐条 SQL §8）
        var vehicleTask = _db.QueryAsync<AssetRow>(vehicleSql, ct, new object[] { appIds });
        var propertyTask = _db.QueryAsync<AssetRow>(propertySql, ct, new object[] { appIds });
        var financeTask = _db.QueryAsync<AssetRow>(financeSql, ct, new object[] { appIds });
        await Task.WhenAll(vehicleTask, propertyTask, financeTask);

        foreach (var task in new[] { vehicleTask, propertyTask, financeTask })
        {
            if (task.Result.IsFailure)
                return Result.Failure<List<AssetRow>>(task.Result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    $"资产查询失败: {task.Result.Message}");
        }

        var rows = vehicleTask.Result.Value!.Concat(propertyTask.Result.Value!).Concat(financeTask.Result.Value!).ToList();
        return Result.Success(rows);
    }

    #endregion

    #region ⑤b 银行流水（复用权威服务，最新批次）

    private async Task<Result<List<BankFlowRow>>> QueryBankFlowsAsync(string idCard, CancellationToken ct)
    {
        var result = await _bankFlowQueryService.GetByIdCardsAsync(new[] { idCard }, ct);
        if (result.IsFailure)
            return Result.Failure<List<BankFlowRow>>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"银行流水查询失败: {result.Message}");

        var rows = new List<BankFlowRow>();
        if (result.Value != null && result.Value.TryGetValue(idCard, out var data) && data != null)
        {
            rows.Add(new BankFlowRow
            {
                PersonName = data.Record.PersonName,
                TotalBalance = data.Record.TotalBalance,
                MaxFlowAmount = data.Record.MaxFlowAmount,
                IsExceed = data.Record.IsExceed,
                BatchNo = data.BatchNo,
                BankSummary = data.Record.BankSummary
            });
        }
        return Result.Success(rows);
    }

    #endregion

    #region ⑤c 核对报告 / ⑥ 死亡 / ⑥b 临时救助 / ⑥c 高龄

    private async Task<Result<List<VerifyReportRow>>> QueryAssetReportsAsync(string idCard, CancellationToken ct)
    {
        const string sql = @"
            SELECT report_date AS report_date,
                   report_file_name AS report_file_name,
                   version AS version,
                   is_valid AS is_valid
              FROM nc_biz_asset_check_reports
             WHERE head_id_card = $1 OR applicant_id_card = $1
             ORDER BY is_current DESC, version DESC, report_date DESC
             LIMIT 20";

        var result = await _db.QueryAsync<VerifyReportRow>(sql, ct, idCard);
        if (result.IsFailure)
            return Result.Failure<List<VerifyReportRow>>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"核对报告查询失败: {result.Message}");
        return Result.Success(result.Value ?? new List<VerifyReportRow>());
    }

    private async Task<Result<List<DeathRow>>> QueryDeathsAsync(string idCard, CancellationToken ct)
    {
        const string sql = @"
            SELECT member_name AS member_name,
                   COALESCE(relationship_to_head,'') AS relationship_to_head,
                   death_date AS death_date,
                   COALESCE(death_reason,'') AS death_reason
              FROM nc_biz_death_records
             WHERE member_id_card = $1
             ORDER BY death_date DESC NULLS LAST
             LIMIT 20";

        var result = await _db.QueryAsync<DeathRow>(sql, ct, idCard);
        if (result.IsFailure)
            return Result.Failure<List<DeathRow>>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"死亡记录查询失败: {result.Message}");
        return Result.Success(result.Value ?? new List<DeathRow>());
    }

    private async Task<Result<List<TempReliefRow>>> QueryTempReliefsAsync(string idCard, CancellationToken ct)
    {
        const string sql = @"
            SELECT applicant_name AS applicant_name,
                   COALESCE(relief_type,'') AS relief_type,
                   apply_date AS apply_date,
                   COALESCE(status,'') AS status,
                   COALESCE(confirm_amount,0) AS confirm_amount
              FROM nc_biz_temp_relief_applications
             WHERE applicant_id_card = $1 AND deleted_at IS NULL
             ORDER BY apply_date DESC NULLS LAST
             LIMIT 20";

        var result = await _db.QueryAsync<TempReliefRow>(sql, ct, idCard);
        if (result.IsFailure)
            return Result.Failure<List<TempReliefRow>>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"临时救助查询失败: {result.Message}");
        return Result.Success(result.Value ?? new List<TempReliefRow>());
    }

    private async Task<Result<List<ElderlyRow>>> QueryElderlyAsync(string idCard, CancellationToken ct)
    {
        const string sql = @"
            SELECT name AS name,
                   COALESCE(status,'') AS status,
                   apply_date AS apply_date,
                   COALESCE(issue_amount,0) AS issue_amount,
                   COALESCE(category,'') AS category
              FROM nc_biz_elderly_applications
             WHERE id_card = $1 AND deleted_at IS NULL
             ORDER BY apply_date DESC NULLS LAST
             LIMIT 20";

        var result = await _db.QueryAsync<ElderlyRow>(sql, ct, idCard);
        if (result.IsFailure)
            return Result.Failure<List<ElderlyRow>>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"高龄津贴查询失败: {result.Message}");
        return Result.Success(result.Value ?? new List<ElderlyRow>());
    }

    #endregion
}
