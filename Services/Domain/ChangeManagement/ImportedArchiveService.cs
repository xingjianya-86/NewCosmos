using System.Globalization;
using System.Text;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.SocialAssistance;

namespace NewCosmos.Services.Domain.ChangeManagement;

/// <summary>
/// 导入库档案服务实现
/// 说明：5 个导入库的表结构由 Excel 导入服务（BaseCombinedImportService 系列）写入，
/// 各域列略有差异，以下按域给出显式 SQL 表达式（表名全部来自本类白名单，禁止外部传入）。
/// </summary>
public class ImportedArchiveService : BaseService, IImportedArchiveService
{
    protected override string ServiceName => "ImportedArchiveService";

    private readonly IDatabaseService _db;
    private readonly IApplicationService _applicationService;
    private readonly IElderlyApplicationService _elderlyService;

    public ImportedArchiveService(
        IDatabaseService db,
        IApplicationService applicationService,
        IElderlyApplicationService elderlyService,
        ILoggerService logger) : base(logger)
    {
        _db = db;
        _applicationService = applicationService;
        _elderlyService = elderlyService;
    }

    #region 导入库白名单映射

    /// <summary>
    /// 导入库表映射（表达式均以 f 为家庭表别名、p 为成员表别名）
    /// </summary>
    private sealed record LibraryDef(
        ArchiveSearchSource Source,
        string FamiliesTable,
        string PersonsTable,
        string AddressExpr,           // 家庭住址表达式（特困库为 hukou_address）
        string FamilySizeExpr,        // 家庭人口数表达式
        string AmountExpr,            // 保障金额合计表达式
        string PerCapitaIncomeExpr,   // 人均年收入表达式
        string HukouTypeExpr,         // 户口类型表达式（低保两库按域硬编码 城镇/农村）
        string SupportModeExpr,       // 供养方式表达式
        string MemberIncomeExpr,      // 成员年收入表达式（月收入×12 折算）
        string MemberBirthDateExpr,   // 成员出生日期表达式
        string MemberAgeExpr,         // 成员年龄表达式
        string MemberWorkCapacityExpr, // 成员劳动能力表达式
        string MonthlyGuaranteeExpr,   // 户月保障金额表达式（低保=monthly_guarantee_amount；特困=basic_living_cost；其余 NULL）
        string FamilyClassifiedExpr,   // 家庭分类施保表达式（低保/特困=family_classified_amount；其余 NULL）
        string MemberClassifiedExpr,   // 成员分类施保表达式（低保=total_classified_amount；特困=care_cost；其余 NULL）
        string FirstReceiveExpr,        // 首次享受月份表达式（建档时间/编号依据，无此列的库用 NULL::varchar）
        string ApplyReasonExpr,         // 申请原因表达式（特困库无此列，用 NULL::varchar）
        string BankNameExpr,            // 开户行表达式（无此列的库用 ''）
        string BankAccountExpr          // 银行卡号/一卡通账号表达式（优先 one_card_account；无此列的库用 ''）
    );

    private static readonly IReadOnlyList<LibraryDef> Libraries = new[]
    {
        new LibraryDef(ArchiveSearchSource.RuralSubsistence,
            "nc_biz_rural_subsistence_families", "nc_biz_rural_subsistence_persons",
            "f.address",
            "f.family_size", "f.total_amount", "f.per_capita_income",
            "'农村'", "NULL::varchar",
            "p.annual_income", "p.birth_date", "p.age", "p.work_capacity",
            "f.monthly_guarantee_amount", "f.family_classified_amount", "p.total_classified_amount",
            "f.first_receive_month", "f.apply_reason",
            "COALESCE(f.bank_name,'')", "COALESCE(NULLIF(f.one_card_account,''), f.bank_account, '')"),
        new LibraryDef(ArchiveSearchSource.UrbanSubsistence,
            "nc_biz_urban_subsistence_families", "nc_biz_urban_subsistence_persons",
            "f.address",
            "f.family_size", "f.total_amount", "f.per_capita_monthly_income * 12",
            "'城镇'", "NULL::varchar",
            "p.monthly_income * 12", "p.birth_date", "p.age", "p.work_capacity",
            "f.monthly_guarantee_amount", "f.family_classified_amount", "p.total_classified_amount",
            "f.first_receive_month", "f.apply_reason",
            "COALESCE(f.bank_name,'')", "COALESCE(NULLIF(f.one_card_account,''), f.bank_account, '')"),
        new LibraryDef(ArchiveSearchSource.LowIncomeEdge,
            "nc_biz_low_income_edge_families", "nc_biz_low_income_edge_persons",
            "f.address",
            "f.guarantee_size", "NULL::numeric", "NULL::numeric",
            "f.hukou_type", "NULL::varchar",
            "p.monthly_income * 12", "NULL::date", "NULL::integer", "p.work_capacity",
            "NULL::numeric", "NULL::numeric", "NULL::numeric",
            "f.include_month", "f.apply_reason",
            "''", "''"),
        new LibraryDef(ArchiveSearchSource.Destitute,
            "nc_biz_destitute_families", "nc_biz_destitute_persons",
            "f.hukou_address",
            "f.family_size", "f.total_amount", "NULL::numeric",
            "f.hukou_type", "f.support_mode",
            "p.annual_income", "p.birth_date", "p.age", "NULL::varchar",
            "f.basic_living_cost", "f.family_classified_amount", "p.care_cost",
            "f.first_receive_month", "NULL::varchar",
            "COALESCE(f.bank_name,'')", "COALESCE(NULLIF(f.one_card_account,''), f.bank_account, '')"),
        new LibraryDef(ArchiveSearchSource.RigidExpenditure,
            "nc_biz_rigid_expenditure_families", "nc_biz_rigid_expenditure_persons",
            "f.address",
            "f.guarantee_size", "NULL::numeric", "NULL::numeric",
            "f.hukou_type", "NULL::varchar",
            "p.monthly_income * 12", "NULL::date", "NULL::integer", "p.work_capacity",
            "NULL::numeric", "NULL::numeric", "NULL::numeric",
            "f.include_month", "f.apply_reason",
            "''", "''"),
    };

    #endregion

    /// <summary>关系代码→中文的 SQL 表达式（字典 FamilyRelationships；未命中回退原值）。使用别名 p（人员表）。</summary>
    private const string RelationshipDisplaySql =
        "COALESCE((SELECT item_value FROM nc_dict_items WHERE category = 'FamilyRelationships' AND item_key = p.relationship AND is_active), NULLIF(p.relationship, ''))";

    #region 行映射类型

    /// <summary>导入库家庭行（snake_case 列自动映射 PascalCase 属性）</summary>
    private sealed class ImportedFamilyRow
    {
        public int Source { get; set; }
        public long SourceId { get; set; }
        public string SourceTable { get; set; } = string.Empty;
        public string ApplicantName { get; set; } = string.Empty;
        public string ApplicantIdCard { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? Province { get; set; }
        public string? City { get; set; }
        public string? District { get; set; }
        public string? Street { get; set; }
        public string? Community { get; set; }
        public int FamilySize { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal? PerCapitaIncome { get; set; }
        public string? HukouType { get; set; }
        public string? SupportMode { get; set; }
        public decimal? MonthlyGuaranteeAmount { get; set; }
        public decimal? FamilyClassifiedAmount { get; set; }
        public string? FirstReceiveMonth { get; set; }
        public string? ApplyReason { get; set; }
        public string? BankName { get; set; }
        public string? BankAccount { get; set; }
        public string MatchedPersonInfo { get; set; } = string.Empty;
    }

    /// <summary>导入库成员行</summary>
    private sealed class ImportedMemberRow
    {
        public string Name { get; set; } = string.Empty;
        public string? IdCard { get; set; }
        public string? Relationship { get; set; }
        public string? Gender { get; set; }
        public DateTime? BirthDate { get; set; }
        public int? Age { get; set; }
        public string? Ethnicity { get; set; }
        public string? MaritalStatus { get; set; }
        public string? EducationLevel { get; set; }
        public string? PoliticalStatus { get; set; }
        public string? EmploymentStatus { get; set; }
        public decimal? AnnualIncome { get; set; }
        public bool IsDisabled { get; set; }
        public string? DisabilityType { get; set; }
        public string? DisabilityLevel { get; set; }
        public string? WorkCapacity { get; set; }
        public string? HealthStatus { get; set; }
        public decimal? ClassifiedSubsidyAmount { get; set; }
    }

    #endregion

    #region 搜索

    /// <inheritdoc />
    public async Task<Result<List<ArchiveSearchResultItem>>> SearchAsync(string keyword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return Result.Success(new List<ArchiveSearchResultItem>());

        try
        {
            var sql = BuildSearchSql();
            var result = await _db.QueryAsync<ImportedFamilyRow>(sql, ct, $"%{keyword.Trim()}%");
            if (result.IsFailure)
                return Result.Failure<List<ArchiveSearchResultItem>>(result.ErrorCode!, result.Message!);

            // 同户去重：家庭直查命中（UNION 中排前）优先于成员命中
            var items = result.Value!
                .GroupBy(r => (r.SourceTable, r.SourceId))
                .Select(g => g.First())
                .Select(MapToItem)
                .ToList();

            return Result.Success(items);
        }
        catch (Exception ex)
        {
            LogException(ex, "SearchAsync");
            return Result.FromException<List<ArchiveSearchResultItem>>(ex);
        }
    }

    /// <summary>
    /// 组合搜索 SQL：5 个域 ×（家庭直查 + 成员回查），家庭命中排前，总体 LIMIT 20
    /// </summary>
    private static string BuildSearchSql()
    {
        var arms = new List<string>(Libraries.Count * 2);
        arms.AddRange(Libraries.Select(BuildFamilyArm));
        arms.AddRange(Libraries.Select(BuildPersonArm));
        return string.Join("\nUNION ALL\n", arms.Select(a => $"({a})")) + "\nLIMIT 20";
    }

    /// <summary>家庭查询的统一列清单（matchedPersonExpr 由调用方给出）</summary>
    private static string BuildFamilyColumns(LibraryDef d, string matchedPersonExpr) => $"""
        {(int)d.Source} AS source, f.id AS source_id, '{d.FamiliesTable}' AS source_table,
               f.applicant_name, f.applicant_id_card, f.phone, {d.AddressExpr} AS address,
               f.province, f.city, f.district, f.street, f.community,
               COALESCE({d.FamilySizeExpr}, 0) AS family_size,
               COALESCE({d.AmountExpr}, 0) AS total_amount,
               {d.PerCapitaIncomeExpr} AS per_capita_income,
               {d.HukouTypeExpr} AS hukou_type,
               {d.SupportModeExpr} AS support_mode,
               {d.MonthlyGuaranteeExpr} AS monthly_guarantee_amount,
               {d.FamilyClassifiedExpr} AS family_classified_amount,
               {d.FirstReceiveExpr} AS first_receive_month,
               {d.ApplyReasonExpr} AS apply_reason,
               {d.BankNameExpr} AS bank_name,
               {d.BankAccountExpr} AS bank_account,
               {matchedPersonExpr} AS matched_person_info
        """;

    /// <summary>家庭直查 SELECT 臂</summary>
    private static string BuildFamilyArm(LibraryDef d) => $"""
        SELECT {BuildFamilyColumns(d, "''")}
          FROM {d.FamiliesTable} f
         WHERE f.applicant_name ILIKE $1 OR f.applicant_id_card ILIKE $1
         LIMIT 10
        """;

    /// <summary>成员命中回查户主的 SELECT 臂（按 head_id_card 关联，兼容 family_id 未回填的数据）</summary>
    private static string BuildPersonArm(LibraryDef d) => $"""
        SELECT {BuildFamilyColumns(d, $"'命中成员：' || p.name || COALESCE('（' || {RelationshipDisplaySql} || '）', '')")}
          FROM {d.PersonsTable} p
          JOIN {d.FamiliesTable} f ON f.applicant_id_card = p.head_id_card
         WHERE p.name ILIKE $1 OR p.id_card ILIKE $1
         LIMIT 10
        """;

    private static ArchiveSearchResultItem MapToItem(ImportedFamilyRow row)
    {
        var source = (ArchiveSearchSource)row.Source;
        var derivedCode = DeriveClassificationCode(source, row.HukouType, row.SupportMode);
        return new ArchiveSearchResultItem
        {
            Source = source,
            SourceTable = row.SourceTable,
            SourceId = row.SourceId,
            ApplicantName = row.ApplicantName,
            ApplicantIdCard = row.ApplicantIdCard,
            Address = row.Address ?? string.Empty,
            FamilySize = row.FamilySize,
            TotalAmount = row.TotalAmount,
            PerCapitaIncome = row.PerCapitaIncome ?? 0,
            HukouType = row.HukouType ?? string.Empty,
            MatchedPersonInfo = row.MatchedPersonInfo,
            DerivedClassificationCode = derivedCode,
            DerivedClassificationName = string.IsNullOrEmpty(derivedCode)
                ? string.Empty
                : ClassificationConstants.ConvertToFullName(derivedCode)
        };
    }

    /// <summary>按来源推导分类认定等级代码（低保按域定城乡；特困按供养方式；其余按户口类型）</summary>
    private static string DeriveClassificationCode(ArchiveSearchSource source, string? hukouType, string? supportMode)
    {
        var isUrban = ClassificationConstants.HukouType.IsHukouUrban(hukouType ?? string.Empty);
        return source switch
        {
            ArchiveSearchSource.RuralSubsistence => ClassificationConstants.RuralSubsistence,
            ArchiveSearchSource.UrbanSubsistence => ClassificationConstants.UrbanSubsistence,
            ArchiveSearchSource.LowIncomeEdge => isUrban
                ? ClassificationConstants.UrbanLowIncome : ClassificationConstants.RuralLowIncome,
            ArchiveSearchSource.Destitute => DeriveDestituteCode(supportMode),
            ArchiveSearchSource.RigidExpenditure => isUrban
                ? ClassificationConstants.UrbanRigidExpenditure : ClassificationConstants.RuralRigidExpenditure,
            _ => string.Empty
        };
    }

    private static string DeriveDestituteCode(string? supportMode)
    {
        var mode = supportMode ?? string.Empty;
        var isCentralized = mode.Contains("集中");
        var isUrban = mode.Contains("城市");
        return (isUrban, isCentralized) switch
        {
            (true, true) => ClassificationConstants.UrbanDestituteCentralized,
            (true, false) => ClassificationConstants.UrbanDestituteScattered,
            (false, true) => ClassificationConstants.RuralDestituteCentralized,
            _ => ClassificationConstants.RuralDestituteScattered
        };
    }

    /// <summary>
    /// 特困供养方式规范化：导入库中文值（如"农村分散供养/农村集中供养/城市集中供养"）→ 系统字典码（Scattered/Centralized）
    /// </summary>
    private static string NormalizeSupportMode(string supportMode)
    {
        if (string.IsNullOrWhiteSpace(supportMode)) return string.Empty;
        return supportMode.Contains("集中") ? "Centralized" : "Scattered";
    }

    /// <summary>
    /// 解析导入库首次享受月份（如 "2026-03" / "2026-3"）为当月1日；
    /// 无法解析返回 null（调用方回退当前时间）
    /// </summary>
    private static DateTime? ParseFirstReceiveMonth(string? firstReceiveMonth)
    {
        if (string.IsNullOrWhiteSpace(firstReceiveMonth)) return null;
        if (DateTime.TryParseExact(firstReceiveMonth.Trim(), "yyyy-MM", null,
                DateTimeStyles.None, out var parsed))
            return new DateTime(parsed.Year, parsed.Month, 1);
        if (DateTime.TryParseExact(firstReceiveMonth.Trim(), "yyyy-M", null,
                DateTimeStyles.None, out var parsed2))
            return new DateTime(parsed2.Year, parsed2.Month, 1);
        return null;
    }

    #endregion

    #region 家庭详情（补全表单初始化）

    /// <inheritdoc />
    public async Task<Result<ImportedFamilyDetail>> GetImportedFamilyDetailAsync(ArchiveSearchResultItem item, CancellationToken ct = default)
    {
        if (item == null || item.Source == ArchiveSearchSource.Current)
            return Result.Failure<ImportedFamilyDetail>(ErrorCodes.VALIDATION_FAILED, "仅导入库档案支持读取家庭详情");

        var def = Libraries.FirstOrDefault(d => d.FamiliesTable == item.SourceTable);
        if (def == null)
            return Result.Failure<ImportedFamilyDetail>(ErrorCodes.VALIDATION_FAILED, $"未登记的导入库表: {item.SourceTable}");

        try
        {
            var familyResult = await _db.QuerySingleAsync<ImportedFamilyRow>(BuildFamilyByIdSql(def), ct, item.SourceId);
            if (familyResult.IsFailure || familyResult.Value == null)
                return Result.Failure<ImportedFamilyDetail>(ErrorCodes.RECORD_NOT_FOUND, "导入库家庭记录不存在或已被清理");

            var f = familyResult.Value;

            var detail = new ImportedFamilyDetail
            {
                SourceDisplay = item.SourceDisplay,
                DerivedClassificationCode = DeriveClassificationCode(
                    (ArchiveSearchSource)f.Source, f.HukouType, f.SupportMode),
                ApplicantName = f.ApplicantName,
                ApplicantIdCard = f.ApplicantIdCard,
                Phone = f.Phone,
                Address = f.Address,
                Province = f.Province,
                City = f.City,
                District = f.District,
                Town = f.Street,
                Community = f.Community,
                FamilySize = f.FamilySize,
                TotalAmount = f.TotalAmount,
                PerCapitaIncome = f.PerCapitaIncome,
                HukouType = f.HukouType,
                SupportMode = f.SupportMode,
                MonthlyGuaranteeAmount = f.MonthlyGuaranteeAmount ?? 0,
                FamilyClassifiedAmount = f.FamilyClassifiedAmount ?? 0,
                FirstReceiveMonth = f.FirstReceiveMonth ?? string.Empty,
                ApplyReason = f.ApplyReason ?? string.Empty
            };

            // 读取同户成员（银行账号在部分导入库家庭表有列，这里仅返回成员；银行由表单补全）
            var membersResult = await _db.QueryAsync<ImportedMemberRow>(BuildMembersSql(def), ct, f.ApplicantIdCard);
            if (membersResult.IsSuccess && membersResult.Value != null)
            {
                foreach (var m in membersResult.Value)
                {
                    detail.Members.Add(new ImportedMemberDetail
                    {
                        Name = m.Name,
                        IdCard = m.IdCard,
                        Relationship = m.Relationship,
                        Gender = m.Gender,
                        BirthDate = m.BirthDate,
                        Age = m.Age,
                        Ethnicity = m.Ethnicity,
                        MaritalStatus = m.MaritalStatus,
                        EducationLevel = m.EducationLevel,
                        PoliticalStatus = m.PoliticalStatus,
                        EmploymentStatus = m.EmploymentStatus,
                        AnnualIncome = m.AnnualIncome,
                        IsDisabled = m.IsDisabled,
                        DisabilityType = m.DisabilityType,
                        DisabilityLevel = m.DisabilityLevel,
                        WorkCapacity = m.WorkCapacity,
                        HealthStatus = m.HealthStatus,
                        ClassifiedSubsidyAmount = m.ClassifiedSubsidyAmount ?? 0
                    });
                }

                // 分类施保按人累加：Σ(各成员分类施保金额) + 家庭分类施保
                detail.ClassifiedSubsidyAmount = detail.FamilyClassifiedAmount
                    + detail.Members.Sum(x => x.ClassifiedSubsidyAmount);
            }

            return Result.Success(detail);
        }
        catch (Exception ex)
        {
            LogException(ex, "GetImportedFamilyDetailAsync");
            return Result.FromException<ImportedFamilyDetail>(ex);
        }
    }

    #endregion

    #region 迁移建档

    /// <inheritdoc />
    public async Task<Result<ImportedMigrationResult>> MigrateToCurrentAsync(
        ArchiveSearchResultItem item, string? classificationCode, CancellationToken ct = default)
    {
        if (item == null || item.Source == ArchiveSearchSource.Current)
            return Result.Failure<ImportedMigrationResult>(ErrorCodes.VALIDATION_FAILED, "仅导入库档案需要建档迁移");

        var def = Libraries.FirstOrDefault(d => d.FamiliesTable == item.SourceTable);
        if (def == null)
            return Result.Failure<ImportedMigrationResult>(ErrorCodes.VALIDATION_FAILED, $"未登记的导入库表: {item.SourceTable}");

        try
        {
            // 幂等查重：同一来源行只允许建档一次
            var existing = await _db.ExecuteScalarAsync<long?>(
                "SELECT id FROM nc_biz_applications WHERE source_type = 'ImportedArchive' AND source_table = $1 AND source_id = $2 AND deleted_at IS NULL LIMIT 1",
                ct, item.SourceTable, item.SourceId);
            if (existing.IsFailure)
                return Result.Failure<ImportedMigrationResult>(existing.ErrorCode!, existing.Message!);
            if (existing.Value is > 0)
                return Result.Success(new ImportedMigrationResult(existing.Value.Value, true, 0));

            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            // 读取导入库家庭行
            var familyResult = await _db.QuerySingleAsync<ImportedFamilyRow>(BuildFamilyByIdSql(def), ct, item.SourceId);
            if (familyResult.IsFailure || familyResult.Value == null)
                return Result.Failure<ImportedMigrationResult>(ErrorCodes.RECORD_NOT_FOUND, "导入库家庭记录不存在或已被清理");

            var f = familyResult.Value;

            // 建档时间依据导入库纳入月份（first_receive_month，如 "2026-03" → 当月1日）；
            // 无纳入时间（edge/rigid 库）回退当前时间
            var enrolledDate = ParseFirstReceiveMonth(f.FirstReceiveMonth);

            // 生成新档案编号（按纳入月份编号，使建档编号与建档时间同月）
            var appNoResult = await _applicationService.GetNextApplicationNoAsync(enrolledDate ?? DateTime.Now, ct);
            if (appNoResult.IsFailure)
                return Result.Failure<ImportedMigrationResult>(appNoResult.ErrorCode!, appNoResult.Message!);

            var operatorName = string.IsNullOrEmpty(App.CurrentUserName) ? "system" : App.CurrentUserName;
            var hukouType = ClassificationConstants.HukouType.Normalize(f.HukouType ?? string.Empty);
            var supportModeCode = NormalizeSupportMode(f.SupportMode ?? string.Empty);

            // 读取并迁入同户成员（户主行用于带出户主基本信息到档案主表）
            var membersResult = await _db.QueryAsync<ImportedMemberRow>(BuildMembersSql(def), ct, f.ApplicantIdCard);
            if (membersResult.IsFailure)
                return Result.Failure<ImportedMigrationResult>(membersResult.ErrorCode!, membersResult.Message!);

            var members = membersResult.Value ?? new List<ImportedMemberRow>();
            var head = members.FirstOrDefault(m => !string.IsNullOrEmpty(m.IdCard)
                && string.Equals(m.IdCard, f.ApplicantIdCard, StringComparison.OrdinalIgnoreCase))
                ?? members.FirstOrDefault()
                ?? new ImportedMemberRow();

            // 金额计算：户月保障（家庭级）、分类施保（家庭分类 + 按人累加）
            var monthlyGuarantee = f.MonthlyGuaranteeAmount ?? 0;
            var classifiedSubsidy = (f.FamilyClassifiedAmount ?? 0)
                + members.Sum(m => m.ClassifiedSubsidyAmount ?? 0);

            // 导入库 per_capita_income 为年人均口径，写入当前库时：
            // 年值列（total_annual_income / per_capita_annual_income）原样折算，
            // 月值列（total_family_income / per_capita_income）= 年值÷12（分解显示口径）。
            var perCapitaAnnual = f.PerCapitaIncome ?? 0;
            var effectiveSize = f.FamilySize > 0 ? f.FamilySize : 1;
            var totalAnnual = Math.Round(perCapitaAnnual * effectiveSize, 2);
            var perCapitaMonthly = Math.Round(perCapitaAnnual / 12m, 2);
            var totalMonthly = Math.Round(totalAnnual / 12m, 2);

            // 插入档案主表（导入库建档默认草稿：status=Draft、current_step=1，
            // 待用户提交/补全后再推进为已建档未提交(step5)/已完结(step6)；
            // is_eligible 按分类码判定（有效码=符合）；溯源列登记出处；
            // created_at 用导入库纳入月份，使档案建立时间不落在当月；
            // 户主基本信息从成员表户主行带出，值与系统字典码对齐；保障金额按家庭/人员规则映射）
            var isEligible = !string.IsNullOrEmpty(classificationCode)
                && ClassificationConstants.IsCodeStop(classificationCode) == false;
            var archivedAt = enrolledDate ?? DateTime.Now;
            var insertResult = await _db.ExecuteScalarAsync<long?>("""
                INSERT INTO nc_biz_applications
                    (application_no, applicant_name, applicant_id_card, applicant_phone,
                     family_size, per_capita_income, total_family_income,
                     total_annual_income, per_capita_annual_income,
                     total_guarantee_amount,
                     household_monthly_guarantee_amount, classified_subsidy_amount,
                     province, city, district, town, community, address, hukou_type,
                     gender, ethnicity, marital_status, education_level, political_status,
                     health_status, disability_type, disability_level, employment_status,
                     support_mode, classification_result, is_eligible, status, current_step,
                     source_type, source_table, source_id, chain_type,
                     created_by, updated_by, created_at, updated_at, bank_name, bank_account)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15,
                        $16, $17, $18, $19, $20,
                        $21, $22, $23, $24, $25, $26, $27, $28, $29, $30, $31, 'Draft', 1,
                        'ImportedArchive', $32, $33, 'ImportedArchive', $34, $34, $35, $35, $36, $37)
                RETURNING id
                """, ct,
                appNoResult.Value!, f.ApplicantName, f.ApplicantIdCard, f.Phone,
                f.FamilySize, perCapitaMonthly, totalMonthly,
                totalAnnual, perCapitaAnnual,
                f.TotalAmount,
                monthlyGuarantee, classifiedSubsidy,
                f.Province, f.City, f.District, f.Street, f.Community, f.Address, hukouType,
                head.Gender, head.Ethnicity, head.MaritalStatus, head.EducationLevel, head.PoliticalStatus,
                head.HealthStatus, head.DisabilityType, head.DisabilityLevel, head.EmploymentStatus,
                supportModeCode, classificationCode, isEligible,
                f.SourceTable, f.SourceId,
                operatorName, archivedAt,
                f.BankName ?? string.Empty, f.BankAccount ?? string.Empty);

            if (insertResult.IsFailure || insertResult.Value is not > 0)
                return Result.Failure<ImportedMigrationResult>(
                    insertResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    insertResult.Message ?? "建立新版本档案失败");

            var newApplicationId = insertResult.Value.Value;

            // 批量迁入同户成员
            if (members.Count > 0)
            {
                var memberInsert = await _db.ExecuteNonQueryAsync(
                    BuildMembersInsertSql(newApplicationId, members, f.ApplicantIdCard, out var memberParams), ct, memberParams);
                if (memberInsert.IsFailure)
                    return Result.Failure<ImportedMigrationResult>(memberInsert.ErrorCode!, memberInsert.Message!);
            }

            Logger.LogBusiness("导入库建档迁移",
                ("SourceTable", f.SourceTable), ("SourceId", f.SourceId),
                ("ApplicantName", DataMasker.MaskName(f.ApplicantName)),
                ("ApplicantIdCard", DataMasker.MaskIdCard(f.ApplicantIdCard)),
                ("NewApplicationId", newApplicationId),
                ("MemberCount", members.Count),
                ("Classification", classificationCode ?? "(重新判定)"),
                ("HukouType", hukouType),
                ("SupportMode", supportModeCode));

            await tx.CommitAsync(ct);

            // 建档完成后：自动为年满80周岁的成员创建高龄补贴草稿
            var autoCreatedElderlyIds = await TryCreateElderlyBenefitsAsync(head, members, f, ct);

            return Result.Success(new ImportedMigrationResult(newApplicationId, false, members.Count, autoCreatedElderlyIds));
        }
        catch (Exception ex)
        {
            LogException(ex, "MigrateToCurrentAsync");
            return Result.FromException<ImportedMigrationResult>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result> DeleteImportedFamilyByApplicationAsync(long applicationId, CancellationToken ct = default)
    {
        try
        {
            // 读取档案溯源：source_type='ImportedArchive' 的导入库家庭行
            var sourceResult = await _db.QuerySingleAsync<SourceRefRow>(
                "SELECT source_table, source_id, applicant_id_card FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL",
                ct, applicationId);
            if (sourceResult.IsFailure)
                return Result.Failure(sourceResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    sourceResult.Message ?? "读取档案来源失败");

            var source = sourceResult.Value;
            if (source == null
                || string.IsNullOrWhiteSpace(source.SourceTable)
                || source.SourceId is not > 0
                || string.IsNullOrWhiteSpace(source.ApplicantIdCard))
            {
                return Result.Success();
            }

            var def = Libraries.FirstOrDefault(d => d.FamiliesTable == source.SourceTable);
            if (def == null)
            {
                LogWarn($"删除导入库家庭：未登记的表 {source.SourceTable}，跳过（不删除）");
                return Result.Success();
            }

            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            // 先删同户人员（按户主身份证关联），再删家庭行，避免外键悬挂
            var deletePersons = await _db.ExecuteNonQueryAsync(
                $"DELETE FROM {def.PersonsTable} WHERE head_id_card = $1", ct, source.ApplicantIdCard);
            if (deletePersons.IsFailure)
                return Result.Failure(deletePersons.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    deletePersons.Message ?? "删除导入库人员失败");

            var deleteFamily = await _db.ExecuteNonQueryAsync(
                $"DELETE FROM {def.FamiliesTable} WHERE id = $1", ct, source.SourceId.Value);
            if (deleteFamily.IsFailure)
                return Result.Failure(deleteFamily.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    deleteFamily.Message ?? "删除导入库家庭失败");

            await tx.CommitAsync(ct);

            Logger.LogBusiness("数据补全后删除导入库家庭",
                ("ApplicationId", applicationId),
                ("SourceTable", source.SourceTable),
                ("SourceId", source.SourceId.Value),
                ("ApplicantIdCard", DataMasker.MaskIdCard(source.ApplicantIdCard)));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "DeleteImportedFamilyByApplicationAsync");
            return Result.FromException(ex);
        }
    }

    /// <summary>档案溯源行（snake_case 列自动映射 PascalCase 属性）</summary>
    private sealed class SourceRefRow
    {
        public string? SourceTable { get; set; }
        public long? SourceId { get; set; }
        public string? ApplicantIdCard { get; set; }
    }

    /// <summary>
    /// 检查户主及所有迁入成员，若年满80周岁（精确到月）且未登记过高龄补贴，
    /// 则自动创建 Draft 状态的高龄补贴草稿。
    /// </summary>
    private async Task<List<long>> TryCreateElderlyBenefitsAsync(
        ImportedMemberRow head,
        List<ImportedMemberRow> members,
        ImportedFamilyRow f,
        CancellationToken ct)
    {
        var result = new List<long>();
        var allPeople = new List<ImportedMemberRow> { head };
        allPeople.AddRange(members);

        foreach (var m in allPeople)
        {
            if (string.IsNullOrWhiteSpace(m.IdCard) || !IdCardValidator.IsValid(m.IdCard))
                continue;

            // 精确到月：当前年月 >= 出生年月 + 80年（即 ageMonths >= 960）
            var birthDate = IdCardValidator.ExtractBirthDate(m.IdCard);
            if (birthDate == null) continue;
            var ageMonths = (DateTime.Today.Year - birthDate.Value.Year) * 12
                          + (DateTime.Today.Month - birthDate.Value.Month);
            if (ageMonths < ElderlyBenefitConstants.Threshold80 * 12) continue;

            // 查重：已登记过高龄补贴则跳过
            var exists = await _elderlyService.CheckIdCardExistsAsync(m.IdCard, null, ct);
            if (exists.IsSuccess && exists.Value) continue;

            // 调用 EvaluateAsync 获取分类 + 补发分段
            var eval = await _elderlyService.EvaluateAsync(m.IdCard, DateTime.Now, ct);
            if (eval.IsFailure) continue;

            // 构建高龄补贴登记（从成员 + 家庭行填充字段）
            var gender = string.IsNullOrEmpty(m.Gender) ? eval.Value.Gender : m.Gender;
            var app = new ElderlyApplication
            {
                Name = m.Name,
                IdCard = m.IdCard,
                Gender = gender,
                BirthDate = eval.Value.BirthDate,
                Phone = f.Phone ?? string.Empty,
                HukouAddress = f.Address ?? string.Empty,
                FamilyAddress = f.Address ?? string.Empty,
                Category = eval.Value.Category,
                IdentityFlag = eval.Value.IdentityFlag,
                IdentitySource = eval.Value.IdentitySource,
                IssueStartMonth = eval.Value.IssueStartMonth,
                IssueAmount = eval.Value.IssueAmount,
                Status = ElderlyBenefitConstants.StatusDraft,
                ApplyDate = DateTime.Now
            };

            var segments = eval.Value.Payback?.Segments?
                .Select(s => new ElderlyPaybackSegment
                {
                    SegmentStartMonth = s.SegmentStartMonth,
                    SegmentEndMonth = s.SegmentEndMonth,
                    MonthlyAmount = s.MonthlyAmount,
                    Months = s.Months,
                    SegmentAmount = s.SegmentAmount
                }).ToList() ?? new List<ElderlyPaybackSegment>();

            var createResult = await _elderlyService.CreateAsync(app, segments, ct);
            if (createResult.IsSuccess)
            {
                result.Add(createResult.Value);
                Logger.LogBusiness("建档自动创建高龄补贴草稿",
                    ("Name", DataMasker.MaskName(m.Name)),
                    ("IdCard", DataMasker.MaskIdCard(m.IdCard!)),
                    ("Age", (ageMonths / 12).ToString()),
                    ("Category", eval.Value.Category));
            }
        }

        return result;
    }

    /// <summary>按主键读取家庭行（列与搜索臂一致，matched_person_info 置空）</summary>
    private static string BuildFamilyByIdSql(LibraryDef d) => $"""
        SELECT {BuildFamilyColumns(d, "''")}
          FROM {d.FamiliesTable} f
         WHERE f.id = $1
         LIMIT 1
        """;

    /// <summary>读取同户全部成员（按户主身份证关联，户主排前）</summary>
    private static string BuildMembersSql(LibraryDef d) => $"""
        SELECT p.name, p.id_card, p.relationship, p.gender,
               {d.MemberBirthDateExpr} AS birth_date, {d.MemberAgeExpr} AS age,
               p.ethnicity, p.marital_status, p.education_level, p.political_status,
               p.employment_status, {d.MemberIncomeExpr} AS annual_income,
               COALESCE(p.is_disabled, false) AS is_disabled, p.disability_type, p.disability_level,
               {d.MemberWorkCapacityExpr} AS work_capacity, p.health_status,
               {d.MemberClassifiedExpr} AS classified_subsidy_amount
          FROM {d.PersonsTable} p
         WHERE p.head_id_card = $1
         ORDER BY (p.id_card = $1) DESC, p.id
        """;

    /// <summary>
    /// 构造家庭成员批量插入 SQL（多行 VALUES；application_id 全行复用 $1）
    /// </summary>
    private static string BuildMembersInsertSql(long applicationId, List<ImportedMemberRow> members, string headIdCard, out object?[] parameters)
    {
        var paramList = new List<object?> { applicationId }; // $1 = application_id（全行复用）
        var sb = new StringBuilder("""
            INSERT INTO nc_biz_family_members
                (application_id, name, id_card, is_applicant, gender, birth_date, age, ethnicity,
                 marital_status, education_level, political_status, relationship_to_head,
                 employment_status, annual_income, is_disabled, disability_type, disability_level,
                 work_capacity, health_status, created_at, updated_at)
            VALUES
            """);

        for (var i = 0; i < members.Count; i++)
        {
            var m = members[i];
            var isApplicant = !string.IsNullOrEmpty(m.IdCard)
                && string.Equals(m.IdCard, headIdCard, StringComparison.OrdinalIgnoreCase);
            // 户主行的关系统一规范为"本人"（导入数据中为 Head/空等）
            var relationship = isApplicant
                ? "本人"
                : (string.IsNullOrWhiteSpace(m.Relationship) ? null : m.Relationship);

            var p = paramList.Count + 1; // 本行第一个参数的序号（$1 已被 application_id 占用）
            paramList.Add(m.Name);
            paramList.Add(m.IdCard);
            paramList.Add(isApplicant);
            paramList.Add(m.Gender);
            paramList.Add(m.BirthDate);
            paramList.Add(m.Age);
            paramList.Add(m.Ethnicity);
            paramList.Add(m.MaritalStatus);
            paramList.Add(m.EducationLevel);
            paramList.Add(m.PoliticalStatus);
            paramList.Add(relationship);
            paramList.Add(m.EmploymentStatus);
            paramList.Add(m.AnnualIncome);
            paramList.Add(m.IsDisabled);
            paramList.Add(m.DisabilityType);
            paramList.Add(m.DisabilityLevel);
            paramList.Add(m.WorkCapacity);
            paramList.Add(m.HealthStatus);

            sb.Append(i == 0 ? "\n(" : ",\n(");
            sb.Append($"$1,${p},${p + 1},${p + 2},${p + 3},${p + 4},${p + 5},${p + 6},${p + 7},${p + 8},${p + 9},${p + 10},${p + 11},${p + 12},${p + 13},${p + 14},${p + 15},${p + 16},${p + 17},NOW(),NOW())");
        }

        parameters = paramList.ToArray();
        return sb.ToString();
    }

    #endregion
}
