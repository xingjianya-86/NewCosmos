using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.Templates;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 每月公示文档输出服务：公示人员以 5 个导入库为基数（农村低保/城市低保/低保边缘/特困供养/刚性支出），
/// 合并当前库已审批在保户，排除已退出（当前库 Stopped/死亡）人员；按村分组输出《公共_每月公示名单》模板。
/// </summary>
public interface IPublicityOutputService
{
    /// <summary>取 5 个导入库全部在保家庭（不分月），返回统一公示行（未分组）</summary>
    Task<Result<List<PublicityFamilyRow>>> GetPublicityFamiliesAsync(CancellationToken ct = default);

    /// <summary>
    /// 取指定月份的公示名单（导入库基数 + 当前库在保合并 − 退出排除），返回统一公示行（未分组）。
    /// 退出排除：当前库 status=ApplicationStatusCodes.STOPPED 且 stop_date ≤ 月末、死亡记录 is_household_head=true 的户主身份证。
    /// </summary>
    Task<Result<List<PublicityFamilyRow>>> GetPublicityFamiliesForMonthAsync(int year, int month, CancellationToken ct = default);

    /// <summary>按村分组并排序（村内按类别顺序 → 户主姓名）</summary>
    List<PublicityVillageGroup> GroupByVillage(IEnumerable<PublicityFamilyRow> families);

    /// <summary>
    /// 按村生成每月公示名单文档（模板：公共_每月公示名单）。
    /// 每村每页固定 22 户，超出拆为独立文件；单页一个文件。
    /// </summary>
    Task<Result<PublicityGenerateResult>> GeneratePublicityFilesAsync(int year, int month, CancellationToken ct = default);
}

/// <summary>公示家庭行（5 个导入库统一口径）</summary>
public class PublicityFamilyRow
{
    /// <summary>户主姓名</summary>
    public string ApplicantName { get; set; } = string.Empty;

    /// <summary>户主身份证号（仅取数，公示表格不展示）</summary>
    public string ApplicantIdCard { get; set; } = string.Empty;

    /// <summary>社会救助类别显示名（农村低保/城市低保/低保边缘/特困供养/刚性支出）</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>家庭住址（特困库为 hukou_address）</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>村/社区字段</summary>
    public string Community { get; set; } = string.Empty;

    /// <summary>保障人数</summary>
    public int GuaranteeSize { get; set; }

    /// <summary>基础保障金额（低保=monthly_guarantee_amount；特困=basic_living_cost）</summary>
    public decimal BaseAmount { get; set; }

    /// <summary>家庭分类施保金额</summary>
    public decimal FamilyClassified { get; set; }

    /// <summary>人员分类施保金额</summary>
    public decimal PersonClassified { get; set; }

    /// <summary>分类施保金额 = 家庭 + 人员</summary>
    public decimal ClassifiedAmount => FamilyClassified + PersonClassified;

    /// <summary>照料护理费（仅特困）</summary>
    public decimal CareCost { get; set; }

    /// <summary>合计金额（户/月）</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>联系电话</summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>备注（申请原因/供养方式）</summary>
    public string Remark { get; set; } = string.Empty;

    /// <summary>是否近亲属备案（nc_biz_near_relative_links 命中户主身份证）</summary>
    public bool IsNearRelative { get; set; }
}

/// <summary>按村分组的公示名单</summary>
public class PublicityVillageGroup
{
    public string Village { get; set; } = string.Empty;
    public List<PublicityFamilyRow> Families { get; set; } = new();
    public int Count => Families.Count;
}

/// <summary>公示文档生成结果</summary>
public class PublicityGenerateResult
{
    /// <summary>输出目录</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>生成的村数量</summary>
    public int VillageCount { get; set; }

    /// <summary>生成文件列表</summary>
    public List<PublicityGeneratedFile> Files { get; set; } = new();
}

/// <summary>单个公示文件信息</summary>
public class PublicityGeneratedFile
{
    public string Village { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public int FamilyCount { get; set; }
    public string FilePath { get; set; } = string.Empty;
}

public class PublicityOutputService : BaseService, IPublicityOutputService
{
    protected override string ServiceName => "PublicityOutputService";

    /// <summary>公示单模板名称（对应 nc_biz_templates.name）</summary>
    private const string PublicityTemplateName = "公共_每月公示名单";

    /// <summary>每页最大数据行数（模板第 6~27 行固定 22 行）</summary>
    private const int RowsPerPage = 22;

    /// <summary>模板数据起始行（表头第 5 行，数据从第 6 行开始）</summary>
    private const int DataStartRow = 6;

    private readonly IDatabaseService _db;
    private readonly ITemplateService _templateService;
    private readonly ITemplateEngineFactory _engineFactory;
    private readonly IOrganizationService _organizationService;

    public PublicityOutputService(
        IDatabaseService db,
        ILoggerService logger,
        ITemplateService templateService,
        ITemplateEngineFactory engineFactory,
        IOrganizationService organizationService) : base(logger)
    {
        _db = db;
        _templateService = templateService;
        _engineFactory = engineFactory;
        _organizationService = organizationService;
    }

    /// <summary>类别显示排序（村内顺序）</summary>
    private static readonly Dictionary<string, int> CategoryOrder = new(StringComparer.Ordinal)
    {
        ["农村低保"] = 0,
        ["城市低保"] = 1,
        ["特困供养"] = 2,
        ["低保边缘"] = 3,
        ["刚性支出"] = 4
    };

    public async Task<Result<List<PublicityFamilyRow>>> GetPublicityFamiliesAsync(CancellationToken ct = default)
    {
        LogInfo("获取导入库公示家庭列表");
        try
        {
            var result = await QueryImportedFamiliesAsync(ct);
            if (result.IsFailure)
            {
                LogError($"公示家庭查询失败: {result.Message}");
                return Result.Failure<List<PublicityFamilyRow>>(
                    string.IsNullOrEmpty(result.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : result.ErrorCode,
                    result.Message ?? "公示家庭查询失败");
            }

            var rows = result.Value ?? new List<PublicityFamilyRow>();
            LogInfo($"公示家庭取数完成: {rows.Count} 户");
            return Result.Success(rows);
        }
        catch (Exception ex)
        {
            LogException(ex, "GetPublicityFamiliesAsync");
            return Result.FromException<List<PublicityFamilyRow>>(ex);
        }
    }

    public async Task<Result<List<PublicityFamilyRow>>> GetPublicityFamiliesForMonthAsync(int year, int month, CancellationToken ct = default)
    {
        LogInfo($"获取公示名单(月度合并): {year}-{month}");
        try
        {
            // 1. 基数 = 5 个导入库全量在保台账
            var importedResult = await QueryImportedFamiliesAsync(ct);
            if (importedResult.IsFailure)
            {
                LogError($"导入库公示家庭查询失败: {importedResult.Message}");
                return Result.Failure<List<PublicityFamilyRow>>(
                    string.IsNullOrEmpty(importedResult.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : importedResult.ErrorCode,
                    importedResult.Message ?? "导入库公示家庭查询失败");
            }
            var imported = importedResult.Value ?? new List<PublicityFamilyRow>();

            // 2. 月末（含），用于判断停保时间是否在本月末之前
            var monthEnd = new DateTime(year, month, 1).AddMonths(1).AddDays(-1);

            // 3. 退出排除集合：当前库已停保(Stopped 且 stop_date ≤ 月末) + 死亡户主
            var exitedResult = await LoadExitedIdCardSetAsync(monthEnd, ct);
            if (exitedResult.IsFailure)
                return Result.Failure<List<PublicityFamilyRow>>(exitedResult.ErrorCode!, exitedResult.Message!);
            var exitedIds = exitedResult.Value ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 4. 当前库在保户（Approved 且未停保），作为新增纳入的补充来源
            var currentResult = await QueryActiveCurrentFamiliesAsync(monthEnd, ct);
            if (currentResult.IsFailure)
                return Result.Failure<List<PublicityFamilyRow>>(currentResult.ErrorCode!, currentResult.Message!);
            var current = currentResult.Value ?? new List<PublicityFamilyRow>();

            // 5. 合并去重：导入库为基数，当前库仅补充导入库中没有的户；两者均剔除退出身份证
            var merged = new Dictionary<string, PublicityFamilyRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in imported)
            {
                if (string.IsNullOrWhiteSpace(f.ApplicantIdCard)) continue;
                if (exitedIds.Contains(f.ApplicantIdCard)) continue;
                merged.TryAdd(f.ApplicantIdCard, f);
            }
            foreach (var f in current)
            {
                if (string.IsNullOrWhiteSpace(f.ApplicantIdCard)) continue;
                if (exitedIds.Contains(f.ApplicantIdCard)) continue;
                merged.TryAdd(f.ApplicantIdCard, f);
            }

            // 6. 近亲属备案标记（批量命中户主身份证）
            await MarkNearRelativeAsync(merged.Values.ToList(), ct);

            var rows = merged.Values.ToList();
            LogInfo($"公示名单(月度合并)取数完成: 导入库 {imported.Count} + 当前库 {current.Count} - 退出 {exitedIds.Count} = {rows.Count} 户");
            return Result.Success(rows);
        }
        catch (Exception ex)
        {
            LogException(ex, "GetPublicityFamiliesForMonthAsync");
            return Result.FromException<List<PublicityFamilyRow>>(ex);
        }
    }

    /// <summary>
    /// 按村生成每月公示名单文档（模板：公共_每月公示名单，每页固定 22 行，单页一个文件）。
    /// 输出目录：输出\公示文件\{yyyy-MM}\
    /// </summary>
    public async Task<Result<PublicityGenerateResult>> GeneratePublicityFilesAsync(int year, int month, CancellationToken ct = default)
    {
        LogInfo($"生成每月公示名单文档: {year}-{month}");
        try
        {
            var listResult = await GetPublicityFamiliesForMonthAsync(year, month, ct);
            if (listResult.IsFailure)
                return Result.Failure<PublicityGenerateResult>(listResult.ErrorCode!, listResult.Message!);
            var families = listResult.Value ?? new List<PublicityFamilyRow>();
            if (families.Count == 0)
                return Result.Failure<PublicityGenerateResult>(ErrorCodes.NOT_FOUND, "公示名单无数据");

            // 解析公示模板（按名称查 nc_biz_templates）
            var template = await _templateService.GetByNameAsync(PublicityTemplateName, ct);
            if (template == null)
                return Result.Failure<PublicityGenerateResult>(ErrorCodes.TEMPLATE_NOT_FOUND, $"公示模板不存在: {PublicityTemplateName}");
            if (template.FileType is not ("xlsx" or "excel"))
                return Result.Failure<PublicityGenerateResult>(ErrorCodes.TEMPLATE_NOT_FOUND, $"公示模板类型不支持: {template.FileType}");

            // 表头：所在县/所在镇/所在镇电话（当前用户组织机构）
            var county = "-";
            var town = "-";
            var townPhone = "-";
            var orgId = App.CurrentUserOrganizationId;
            if (orgId is > 0)
            {
                var orgResult = await _organizationService.GetByIdAsync(orgId.Value, ct);
                if (orgResult.IsSuccess && orgResult.Value != null)
                {
                    county = string.IsNullOrWhiteSpace(orgResult.Value.CountyName) ? "林口县" : orgResult.Value.CountyName;
                    town = string.IsNullOrWhiteSpace(orgResult.Value.TownName) ? "-" : orgResult.Value.TownName;
                    townPhone = string.IsNullOrWhiteSpace(orgResult.Value.Phone) ? "-" : orgResult.Value.Phone;
                }
            }
            // 公示周期：整月周期（所选年月自然月）
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var publicityPeriod = $"{year}年{month}月1日至{year}年{month}月{daysInMonth}日";

            var groups = GroupByVillage(families);
            var outputDir = Path.GetFullPath(Path.Combine("输出", "公示文件", $"{year:D4}-{month:D2}"));
            Directory.CreateDirectory(outputDir);

            var result = new PublicityGenerateResult { OutputDirectory = outputDir, VillageCount = groups.Count };

            foreach (var group in groups)
            {
                var safeVillage = SanitizeFileName(group.Village);
                var pageCount = (int)Math.Ceiling(group.Count / (double)RowsPerPage);

                for (var page = 0; page < pageCount; page++)
                {
                    ct.ThrowIfCancellationRequested();
                    var slice = group.Families.Skip(page * RowsPerPage).Take(RowsPerPage).ToList();

                    var baseName = pageCount == 1
                        ? $"{safeVillage}_社会救助对象公示单"
                        : $"{safeVillage}_社会救助对象公示单_第{page + 1}页";
                    var filePath = Path.Combine(outputDir, $"{baseName}.xlsx");

                    var fields = BuildPublicityFields(county, town, publicityPeriod, townPhone, slice, group.Village);
                    var renderResult = await RenderPageAsync(template.Id, fields, slice.Count, filePath, ct);
                    if (renderResult.IsFailure)
                        return Result.Failure<PublicityGenerateResult>(renderResult.ErrorCode!, renderResult.Message!);

                    result.Files.Add(new PublicityGeneratedFile
                    {
                        Village = group.Village,
                        PageNumber = page + 1,
                        FamilyCount = slice.Count,
                        FilePath = filePath
                    });
                }
            }

            Logger.LogBusiness("每月公示名单生成完成",
                ("Year", year), ("Month", month), ("Villages", result.VillageCount), ("Files", result.Files.Count));
            return Result.Success(result);
        }
        catch (Exception ex)
        {
            LogException(ex, "GeneratePublicityFilesAsync");
            return Result.FromException<PublicityGenerateResult>(ex);
        }
    }

    public List<PublicityVillageGroup> GroupByVillage(IEnumerable<PublicityFamilyRow> families)
    {
        var list = families.ToList();
        foreach (var f in list)
        {
            f.Community ??= "";
            f.Address ??= "";
        }

        var groups = list
            .GroupBy(f => PublicityVillageResolver.Resolve(f.Address, f.Community))
            .Select(g => new PublicityVillageGroup
            {
                Village = g.Key,
                Families = g
                    .OrderBy(f => CategoryOrder.TryGetValue(f.Category, out var o) ? o : 99)
                    .ThenBy(f => f.ApplicantName, StringComparer.Ordinal)
                    .ToList()
            })
            .OrderBy(g => g.Count)
            .ToList();

        return groups;
    }

    // ─────────────────────────── 私有辅助 ───────────────────────────

    /// <summary>5 个导入库全量在保家庭 UNION（表名全部来自本服务白名单，禁止外部传入）。</summary>
    private async Task<Result<List<PublicityFamilyRow>>> QueryImportedFamiliesAsync(CancellationToken ct)
    {
        // 列统一为 snake_case，由 IDatabaseService 自动映射为 PascalCase 属性。
        const string sql = @"
                SELECT '农村低保' AS category, applicant_name, applicant_id_card, phone, address, community,
                       COALESCE(NULLIF(guarantee_size, 0), family_size, 0) AS guarantee_size,
                       COALESCE(monthly_guarantee_amount, 0) AS base_amount,
                       COALESCE(family_classified_amount, 0) AS family_classified,
                       COALESCE(person_classified_amount, 0) AS person_classified,
                       0 AS care_cost,
                       COALESCE(total_amount, 0) AS total_amount,
                       COALESCE(apply_reason, '') AS remark
                  FROM nc_biz_rural_subsistence_families
                UNION ALL
                SELECT '城市低保' AS category, applicant_name, applicant_id_card, phone, address, community,
                       COALESCE(NULLIF(guarantee_size, 0), family_size, 0) AS guarantee_size,
                       COALESCE(monthly_guarantee_amount, 0) AS base_amount,
                       COALESCE(family_classified_amount, 0) AS family_classified,
                       COALESCE(person_classified_amount, 0) AS person_classified,
                       0 AS care_cost,
                       COALESCE(total_amount, 0) AS total_amount,
                       COALESCE(apply_reason, '') AS remark
                  FROM nc_biz_urban_subsistence_families
                UNION ALL
                SELECT '低保边缘' AS category, applicant_name, applicant_id_card, phone, address, community,
                       COALESCE(guarantee_size, 0) AS guarantee_size,
                       0 AS base_amount, 0 AS family_classified, 0 AS person_classified, 0 AS care_cost,
                       0 AS total_amount,
                       COALESCE(apply_reason, '') AS remark
                  FROM nc_biz_low_income_edge_families
                UNION ALL
                SELECT '特困供养' AS category, applicant_name, applicant_id_card, phone, hukou_address AS address, community,
                       COALESCE(NULLIF(destitute_count, 0), family_size, 0) AS guarantee_size,
                       COALESCE(basic_living_cost, 0) AS base_amount,
                       COALESCE(family_classified_amount, 0) AS family_classified,
                       COALESCE(person_classified_amount, 0) AS person_classified,
                       COALESCE(care_cost, 0) AS care_cost,
                       COALESCE(total_amount, 0) AS total_amount,
                       COALESCE(support_mode, '') AS remark
                  FROM nc_biz_destitute_families
                UNION ALL
                SELECT '刚性支出' AS category, applicant_name, applicant_id_card, phone, address, community,
                       COALESCE(guarantee_size, 0) AS guarantee_size,
                       0 AS base_amount, 0 AS family_classified, 0 AS person_classified, 0 AS care_cost,
                       0 AS total_amount,
                       COALESCE(apply_reason, '') AS remark
                  FROM nc_biz_rigid_expenditure_families";

        return await _db.QueryAsync<PublicityFamilyRow>(sql, ct);
    }

    /// <summary>
    /// 退出排除集合：当前库已停保(Stopped 且 stop_date ≤ 月末)的户主身份证 + 死亡记录中 is_household_head=true 的身份证。
    /// </summary>
    private async Task<Result<HashSet<string>>> LoadExitedIdCardSetAsync(DateTime monthEnd, CancellationToken ct)
    {
        try
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 停保户主（按月判断：停保日期 <= 月末 → 该月已不在保）；
            // 停旧建新接续（同大类继续享受，如成员变更/同类别复核/户主变更）不算退出，避免在保户被永久排除出公示
            var stoppedResult = await _db.QueryAsync<IdCardRow>(
                $@"SELECT applicant_id_card FROM nc_biz_applications
                  WHERE status = '{ApplicationStatusCodes.STOPPED}' AND stop_date IS NOT NULL AND stop_date <= $1::date AND deleted_at IS NULL
                  {StoppedArchiveFilter.NotRebuildContinuationSql("nc_biz_applications")}",
                ct, monthEnd);
            if (stoppedResult.IsFailure)
                return Result.Failure<HashSet<string>>(stoppedResult.ErrorCode!, stoppedResult.Message!);
            foreach (var r in stoppedResult.Value ?? new List<IdCardRow>())
            {
                if (!string.IsNullOrWhiteSpace(r.IdCard)) ids.Add(r.IdCard.Trim());
            }

            // 死亡户主
            var deathResult = await _db.QueryAsync<IdCardRow>(
                @"SELECT member_id_card AS id_card FROM nc_biz_death_records WHERE is_household_head = true",
                ct);
            if (deathResult.IsFailure)
                return Result.Failure<HashSet<string>>(deathResult.ErrorCode!, deathResult.Message!);
            foreach (var r in deathResult.Value ?? new List<IdCardRow>())
            {
                if (!string.IsNullOrWhiteSpace(r.IdCard)) ids.Add(r.IdCard.Trim());
            }

            return Result.Success(ids);
        }
        catch (Exception ex)
        {
            LogException(ex, "LoadExitedIdCardSetAsync");
            return Result.FromException<HashSet<string>>(ex);
        }
    }

    /// <summary>
    /// 当前库在保户（Approved 且未停保），作为"新增纳入"的补充来源。
    /// 类别显示名映射为与导入库一致的口径（农村低保/城市低保/低保边缘/特困供养/刚性支出）。
    /// </summary>
    private async Task<Result<List<PublicityFamilyRow>>> QueryActiveCurrentFamiliesAsync(DateTime monthEnd, CancellationToken ct)
    {
        const string sql = @"
                SELECT
                    CASE classification_result
                        WHEN 'RuralSubsistence' THEN '农村低保'
                        WHEN 'UrbanSubsistence' THEN '城市低保'
                        WHEN 'RuralLowIncome' THEN '低保边缘'
                        WHEN 'UrbanLowIncome' THEN '低保边缘'
                        WHEN 'RuralLowIncomeSingle' THEN '低保边缘'
                        WHEN 'UrbanLowIncomeSingle' THEN '低保边缘'
                        WHEN 'RuralDestituteScattered' THEN '特困供养'
                        WHEN 'RuralDestituteCentralized' THEN '特困供养'
                        WHEN 'UrbanDestituteScattered' THEN '特困供养'
                        WHEN 'UrbanDestituteCentralized' THEN '特困供养'
                        WHEN 'RuralRigidExpenditure' THEN '刚性支出'
                        WHEN 'UrbanRigidExpenditure' THEN '刚性支出'
                        ELSE COALESCE(classification_result, '')
                    END AS category,
                    applicant_name, applicant_id_card, COALESCE(applicant_phone, '') AS phone,
                    COALESCE(address, '') AS address, COALESCE(community, '') AS community,
                    COALESCE(NULLIF(confirmed_family_size, 0), family_size, 0) AS guarantee_size,
                    COALESCE(household_monthly_guarantee_amount, 0) AS base_amount,
                    COALESCE(classified_subsidy_amount, 0) AS family_classified,
                    COALESCE(person_category_protection_total_amount, 0) AS person_classified,
                    COALESCE(caregiver_subsidy_amount, 0) AS care_cost,
                    COALESCE(total_guarantee_amount, 0) AS total_amount,
                    COALESCE(application_reason, '') AS remark
                  FROM nc_biz_applications
                 WHERE status = $2
                   AND (stop_date IS NULL OR stop_date >= $1::date)
                   AND deleted_at IS NULL";
        try
        {
            var result = await _db.QueryAsync<PublicityFamilyRow>(sql, ct, monthEnd, ApplicationStatusCodes.APPROVED);
            if (result.IsFailure)
                return Result.Failure<List<PublicityFamilyRow>>(result.ErrorCode!, result.Message!);
            return Result.Success(result.Value ?? new List<PublicityFamilyRow>());
        }
        catch (Exception ex)
        {
            LogException(ex, "QueryActiveCurrentFamiliesAsync");
            return Result.FromException<List<PublicityFamilyRow>>(ex);
        }
    }

    /// <summary>批量标记近亲属备案（户主身份证命中 nc_biz_near_relative_links.id_card）。</summary>
    private async Task MarkNearRelativeAsync(List<PublicityFamilyRow> families, CancellationToken ct)
    {
        if (families == null || families.Count == 0) return;

        var cards = families
            .Select(f => f.ApplicantIdCard?.Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (cards.Length == 0) return;

        try
        {
            var result = await _db.QueryAsync<IdCardRow>(
                @"SELECT DISTINCT id_card FROM nc_biz_near_relative_links
                  WHERE id_card IS NOT NULL AND id_card <> '' AND id_card = ANY($1::text[])",
                ct, new object[] { cards });
            if (result.IsFailure || result.Value == null) return;

            var hit = new HashSet<string>(
                result.Value.Select(r => r.IdCard?.Trim()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!),
                StringComparer.OrdinalIgnoreCase);
            foreach (var f in families)
            {
                if (!string.IsNullOrWhiteSpace(f.ApplicantIdCard) && hit.Contains(f.ApplicantIdCard.Trim()))
                    f.IsNearRelative = true;
            }
        }
        catch (Exception ex)
        {
            LogWarn($"近亲属备案标记失败（按未备案处理）: {ex.Message}");
        }
    }

    /// <summary>组装单页公示单字段字典（表头 + 22 行占位符）。</summary>
    private static Dictionary<string, string> BuildPublicityFields(
        string county, string town, string publicityPeriod, string townPhone,
        List<PublicityFamilyRow> slice, string village)
    {
        var fields = new Dictionary<string, string>
        {
            ["{所在县}"] = county,
            ["{所在镇}"] = town,
            ["{当前月公示周期}"] = publicityPeriod,
            ["{所在镇电话}"] = townPhone
        };

        for (var i = 0; i < slice.Count; i++)
        {
            var f = slice[i];
            var n = i + 1;
            fields[$"{{救助姓名{n}}}"] = f.ApplicantName ?? "";
            fields[$"{{救助人所在村屯{n}}}"] = village;
            fields[$"{{救助类型{n}}}"] = f.Category ?? "";
            fields[$"{{救助人数{n}}}"] = Math.Max(0, f.GuaranteeSize).ToString();
            fields[$"{{是否近亲属备案{n}}}"] = f.IsNearRelative ? "是" : "否";
        }

        return fields;
    }

    /// <summary>渲染单页：Load 模板 → ReplaceFields → 删除尾部空行 → 保存文件。</summary>
    private async Task<Result> RenderPageAsync(long templateId, Dictionary<string, string> fields, int familyCount, string filePath, CancellationToken ct)
    {
        try
        {
            using var engine = await _engineFactory.CreateFromTemplateIdAsync(templateId, ct);
            engine.ReplaceFields(fields);

            // 末页不足 22 户：删除模板尾部空行（数据从第 6 行开始，删到第 27 行）
            if (familyCount < RowsPerPage)
                engine.DeleteRows(DataStartRow + familyCount, DataStartRow + RowsPerPage - 1);

            await engine.SaveToFileAsync(filePath, ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, $"RenderPageAsync: {filePath}");
            return Result.FromException(ex);
        }
    }

    /// <summary>文件名安全化（去除 Windows 非法字符与路径分隔符）。</summary>
    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "未分组";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray().Where(c => !invalid.Contains(c) && c != '/' && c != '\\').ToArray();
        return new string(chars).Trim();
    }

    private sealed class IdCardRow
    {
        public string? IdCard { get; set; }
    }
}
