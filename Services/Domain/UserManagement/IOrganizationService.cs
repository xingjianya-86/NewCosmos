using NewCosmos.Constants;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.UserManagement;

/// <summary>
/// 组织服务接口
/// </summary>
public interface IOrganizationService
{
    /// <summary>
    /// 根据ID获取组织
    /// </summary>
    Task<Result<Organization>> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// 按名称获取组织（名称匹配；不存在返回 Value=null）
    /// </summary>
    Task<Result<Organization>> GetByNameAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// 获取所有组织列    /// </summary>
    Task<Result<List<Organization>>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取子组织列    /// </summary>
    Task<Result<List<Organization>>> GetChildrenAsync(int parentId, CancellationToken ct = default);

    /// <summary>
    /// 创建组织
    /// </summary>
    Task<Result<int>> CreateAsync(OrganizationCreateRequest request, CancellationToken ct = default);

    /// <summary>
    /// 创建组织（使用保存请求）
    /// </summary>
    Task<Result<int>> CreateAsync(OrganizationSaveRequest request, CancellationToken ct = default);

    /// <summary>
    /// 更新组织
    /// </summary>
    Task<Result> UpdateAsync(OrganizationUpdateRequest request, CancellationToken ct = default);

    /// <summary>
    /// 更新组织（带id ??   /// </summary>
    Task<Result> UpdateAsync(int id, OrganizationSaveRequest request, CancellationToken ct = default);

    /// <summary>
    /// 保存组织（新增或更新    /// </summary>
    Task<Result> SaveAsync(OrganizationSaveRequest request, CancellationToken ct = default);

    /// <summary>
    /// 删除组织
    /// </summary>
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// 获取组织树结    /// </summary>
    Task<Result<List<OrganizationTreeNode>>> GetTreeAsync(CancellationToken ct = default);

    /// <summary>
    /// 检查组织名称是否存    /// </summary>
    Task<Result<bool>> CheckNameExistsAsync(string name, int? excludeId = null, CancellationToken ct = default);

    /// <summary>
    /// 获取组织下的用户数量
    /// </summary>
    Task<Result<int>> GetUserCountAsync(int organizationId, CancellationToken ct = default);

    /// <summary>
    /// 预览CSV文件导入数据
    /// </summary>
    OrganizationCsvPreview PreviewCsvImport(string filePath, int previewCount = 5);

    /// <summary>
    /// 从CSV文件导入组织数据
    /// </summary>
    Task<ImportResult> ImportFromCsvAsync(string filePath, IProgress<string> progress = null, CancellationToken ct = default);
}

/// <summary>
/// CSV导入预览结果
/// </summary>
public class OrganizationCsvPreview
{
    public int TotalRows { get; set; }
    public List<OrganizationPreviewRow> PreviewRows { get; set; } = new();
}

/// <summary>
/// 组织预览    /// </summary>
public class OrganizationPreviewRow
{
    public int RowNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
    public string Principal { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}

/// <summary>
/// 组织实体 - 对应数据库 organizations     /// </summary>
public class Organization
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
    public string Principal { get; set; } = string.Empty;
    public string PrincipalIdentityCard { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public string ParentName { get; set; } = string.Empty;
    public string InstitutionNature { get; set; } = string.Empty;
    public string InstitutionType { get; set; } = string.Empty;
    public string InstitutionCategory { get; set; } = string.Empty;
    public bool IsSpecialCareInstitution { get; set; }
    public int? CityId { get; set; }
    public string CityName { get; set; } = string.Empty;
    public int? CountyId { get; set; }
    public int? TownId { get; set; }
    public int? VillageId { get; set; }
    public string CountyName { get; set; } = string.Empty;
    public string TownName { get; set; } = string.Empty;
    public string VillageName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    /// <summary>
    /// 运行时计算的层级深度（不从数据库读取）
    /// </summary>
    public int CalculatedDepth { get; set; }
}

/// <summary>
/// 组织树节    /// </summary>
public class OrganizationTreeNode
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
    public int Depth { get; set; }
    public int IndentWidth => Depth * 20;
    public List<OrganizationTreeNode> Children { get; set; } = new();
}

/// <summary>
/// 组织创建请求
/// </summary>
public class OrganizationCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
    public string Principal { get; set; } = string.Empty;
    public string PrincipalIdentityCard { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public string InstitutionNature { get; set; } = string.Empty;
    public string InstitutionType { get; set; } = string.Empty;
    public string InstitutionCategory { get; set; } = string.Empty;
    public bool IsSpecialCareInstitution { get; set; }
    public int? CityId { get; set; }
    public int? CountyId { get; set; }
    public int? TownId { get; set; }
    public int? VillageId { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// 组织更新请求
/// </summary>
public class OrganizationUpdateRequest
{
    public int OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
    public string Principal { get; set; } = string.Empty;
    public string PrincipalIdentityCard { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public string InstitutionNature { get; set; } = string.Empty;
    public string InstitutionType { get; set; } = string.Empty;
    public string InstitutionCategory { get; set; } = string.Empty;
    public bool IsSpecialCareInstitution { get; set; }
    public int? CityId { get; set; }
    public int? CountyId { get; set; }
    public int? TownId { get; set; }
    public int? VillageId { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// 组织保存请求（通用）
/// </summary>
public class OrganizationSaveRequest
{
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
    public string Principal { get; set; } = string.Empty;
    public string PrincipalIdentityCard { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public string InstitutionNature { get; set; } = string.Empty;
    public string InstitutionType { get; set; } = string.Empty;
    public string InstitutionCategory { get; set; } = string.Empty;
    public bool IsSpecialCareInstitution { get; set; }
    public int? CityId { get; set; }
    public int? CountyId { get; set; }
    public int? TownId { get; set; }
    public int? VillageId { get; set; }
    public string SavedBy { get; set; } = string.Empty;
}

/// <summary>
/// 角色实体
/// </summary>
public class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = ApplicationStatusCodes.ACTIVE;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
