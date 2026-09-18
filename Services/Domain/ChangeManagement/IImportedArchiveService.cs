using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.ChangeManagement;

/// <summary>
/// 导入库建档迁移结果
/// </summary>
/// <param name="ApplicationId">当前库档案ID（新建或已存在）</param>
/// <param name="AlreadyExisted">是否此前已建档（幂等命中）</param>
/// <param name="MigratedMemberCount">本次迁入的家庭成员数</param>
/// <param name="AutoCreatedElderlyIds">自动创建的高龄补贴草稿ID列表</param>
public sealed record ImportedMigrationResult(long ApplicationId, bool AlreadyExisted, int MigratedMemberCount, List<long>? AutoCreatedElderlyIds = null);

/// <summary>
/// 导入库成员详情（供补全表单初始化展示）
/// </summary>
public class ImportedMemberDetail
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
    public decimal ClassifiedSubsidyAmount { get; set; }
}

/// <summary>
/// 导入库家庭完整详情（含成员，供补全表单从未建档记录初始化展示）
/// </summary>
public class ImportedFamilyDetail
{
    /// <summary>来源显示名（如"农村最低生活保障对象"）</summary>
    public string SourceDisplay { get; set; } = string.Empty;

    /// <summary>分类认定等级代码（按来源推导）</summary>
    public string DerivedClassificationCode { get; set; } = string.Empty;

    public string ApplicantName { get; set; } = string.Empty;
    public string ApplicantIdCard { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Province { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public string? Town { get; set; }
    public string? Community { get; set; }
    public int FamilySize { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal? PerCapitaIncome { get; set; }
    public string? HukouType { get; set; }
    public string? SupportMode { get; set; }

    /// <summary>户月保障金额（低保=monthly_guarantee_amount；特困=basic_living_cost）</summary>
    public decimal MonthlyGuaranteeAmount { get; set; }

    /// <summary>家庭分类施保金额（家庭级定值）</summary>
    public decimal FamilyClassifiedAmount { get; set; }

    /// <summary>分类施保合计 = 家庭分类施保 + Σ(各成员分类施保)（按人判断累加）</summary>
    public decimal ClassifiedSubsidyAmount { get; set; }

    /// <summary>首次享受月份（如 "2026-03"；无此列的库为空）</summary>
    public string FirstReceiveMonth { get; set; } = string.Empty;

    /// <summary>申请原因（农村/城市低保、低收入边缘、刚性支出库有 apply_reason 列；特困库无此列，为空）</summary>
    public string ApplyReason { get; set; } = string.Empty;

    /// <summary>同户成员（户主排前）</summary>
    public List<ImportedMemberDetail> Members { get; set; } = new();
}

/// <summary>
/// 导入库档案服务：跨 5 个历史导入库（农村低保/城市低保/低收入边缘/特困供养/刚性支出）
/// 检索家庭档案，并支持整户迁移到当前库建立新版本档案
/// </summary>
public interface IImportedArchiveService
{
    /// <summary>
    /// 按姓名/身份证号搜索 5 个导入库（家庭主表 + 成员表，成员命中自动回查户主）
    /// </summary>
    Task<Result<List<ArchiveSearchResultItem>>> SearchAsync(string keyword, CancellationToken ct = default);

    /// <summary>
    /// 读取导入库家庭完整详情（含同户成员），供补全表单从未建档记录初始化展示
    /// </summary>
    Task<Result<ImportedFamilyDetail>> GetImportedFamilyDetailAsync(ArchiveSearchResultItem item, CancellationToken ct = default);

    /// <summary>
    /// 将导入库家庭整户迁移到当前库，建立新版本档案（幂等：同一来源行只建档一次）
    /// </summary>
    /// <param name="item">搜索选中的导入库记录</param>
    /// <param name="classificationCode">分类认定等级代码；null 表示暂不设置（后续重新判定）</param>
    Task<Result<ImportedMigrationResult>> MigrateToCurrentAsync(ArchiveSearchResultItem item, string? classificationCode, CancellationToken ct = default);

    /// <summary>
    /// 数据补全并写入数据库后：按档案ID删除其对应的导入库家庭行及同户人员行。
    /// 依据 nc_biz_applications.source_table / source_id / applicant_id_card 定位并删除。
    /// </summary>
    Task<Result> DeleteImportedFamilyByApplicationAsync(long applicationId, CancellationToken ct = default);
}
