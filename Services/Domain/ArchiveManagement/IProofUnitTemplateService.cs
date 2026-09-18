using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 证明单位模板服务：管理"接收单位 → 专属证明模板"映射（全局共享）。
/// 模板文件本体存 nc_biz_templates，本服务负责单位与模板的绑定/维护。
/// </summary>
public interface IProofUnitTemplateService
{
    /// <summary>获取全部单位模板（含模板名称/类型，按创建时间倒序）</summary>
    Task<Result<List<ProofUnitTemplateItem>>> GetAllAsync(CancellationToken ct = default);

    /// <summary>按 ID 获取单位模板</summary>
    Task<Result<ProofUnitTemplateItem>> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>按单位名称获取专属模板（未配置返回失败 RECORD_NOT_FOUND）</summary>
    Task<Result<Template>> GetTemplateByUnitAsync(string unitName, CancellationToken ct = default);

    /// <summary>
    /// 新增单位模板：事务内先建 nc_biz_templates 再写映射。
    /// 单位名重复返回 VALIDATION_FAILED。
    /// </summary>
    /// <param name="unitName">接收单位名称（唯一）</param>
    /// <param name="fileData">模板文件字节</param>
    /// <param name="fileName">模板文件名（用于提取模板名）</param>
    /// <param name="fileType">模板类型（Word/Excel）</param>
    /// <param name="configJson">可选字段映射配置（config_json）</param>
    /// <param name="operatorId">操作人用户ID</param>
    Task<Result<ProofUnitTemplate>> CreateAsync(
        string unitName, byte[] fileData, string fileName, string fileType,
        string? configJson, int? operatorId, CancellationToken ct = default);

    /// <summary>
    /// 更新单位模板：可改单位名、替换模板文件（fileData 非空时替换）。
    /// </summary>
    Task<Result> UpdateAsync(long id, string? unitName, byte[]? fileData, string? fileType,
        int? operatorId, CancellationToken ct = default);

    /// <summary>删除单位模板映射（仅删映射，模板文件保留在 nc_biz_templates）</summary>
    Task<Result> DeleteAsync(long id, CancellationToken ct = default);
}