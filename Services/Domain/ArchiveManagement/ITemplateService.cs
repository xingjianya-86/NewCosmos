using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Services.Domain.ArchiveManagement;

public interface ITemplateService
{
    Task<List<Template>> GetAllAsync(CancellationToken ct = default);

    Task<List<Template>> GetByCategoriesAsync(string[] categories, CancellationToken ct = default);

    Task<Template?> GetByIdAsync(long id, CancellationToken ct = default);

    Task<Template?> GetByNameAsync(string name, CancellationToken ct = default);

    Task<Template> SaveAsync(string name, string fileType, byte[] fileData, string[] categories, string configJson, CancellationToken ct = default);

    Task<Result> DeleteAsync(long id, CancellationToken ct = default);

    Task<byte[]> GetFileDataAsync(long id, CancellationToken ct = default);

    Task<string> ExportTemplateAsync(long id, string folderPath, string fileName, CancellationToken ct = default);

    Task UpdateConfigJsonAsync(long id, string configJson, CancellationToken ct = default);

    Task UpdateCategoriesAsync(long id, string[] categories, CancellationToken ct = default);

    Task ReorderAsync(long templateId, int newSortOrder, CancellationToken ct = default);

    Task ReplaceFileAsync(long id, byte[] fileData, string fileType, CancellationToken ct = default);
}
