using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Services.Domain.ArchiveManagement;

public interface ITemplateService
{
    /// <summary>全部模板元数据。Failure=查询失败（禁止把 DB 故障洗成空列表）。</summary>
    Task<Result<List<Template>>> GetAllAsync(CancellationToken ct = default);

    Task<List<Template>> GetByCategoriesAsync(string[] categories, CancellationToken ct = default);

    /// <summary>按 Id 取模板元数据。Success(null)=无此行；Failure=查询失败（禁止把 DB 故障伪装成"模板不存在"）。</summary>
    Task<Result<Template?>> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>按名称取模板元数据。Success(null)=无此行；Failure=查询失败（禁止把 DB 故障伪装成"模板不存在"）。</summary>
    Task<Result<Template?>> GetByNameAsync(string name, CancellationToken ct = default);

    Task<Template> SaveAsync(string name, string fileType, byte[] fileData, string[] categories, string configJson, CancellationToken ct = default);

    Task<Result> DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>取模板文件字节。Success(空)=无此行或文件列为空；Failure=查询失败（禁止洗成空数组）。</summary>
    Task<Result<byte[]>> GetFileDataAsync(long id, CancellationToken ct = default);

    Task<string> ExportTemplateAsync(long id, string folderPath, string fileName, CancellationToken ct = default);

    Task UpdateConfigJsonAsync(long id, string configJson, CancellationToken ct = default);

    Task UpdateCategoriesAsync(long id, string[] categories, CancellationToken ct = default);

    Task ReorderAsync(long templateId, int newSortOrder, CancellationToken ct = default);

    Task ReplaceFileAsync(long id, byte[] fileData, string fileType, CancellationToken ct = default);
}
