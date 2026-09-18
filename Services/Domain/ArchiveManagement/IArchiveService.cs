using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Services.Domain.ArchiveManagement;

public interface IArchiveService
{
    Task<Archive?> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default);

    /// <summary>创建档案记录；插入失败抛 BusinessException（不再返回 Id=0 的对象）</summary>
    Task<Archive> CreateAsync(Archive archive, CancellationToken ct = default);

    /// <summary>写入单条档案文件记录；失败抛 BusinessException</summary>
    Task AddFileAsync(long archiveId, ArchiveFile file, CancellationToken ct = default);

    /// <summary>在单个事务内创建档案并写入全部文件记录，任一步失败整体回滚</summary>
    Task<Result<Archive>> CreateWithFilesAsync(Archive archive, IReadOnlyList<ArchiveFile> files, CancellationToken ct = default);

    /// <summary>
    /// 幂等归档：同一申请已有档案则仅追加文件记录；无档案则新建档案并写入文件记录。
    /// 供一事一议申报表等补充材料多次生成归档使用。
    /// </summary>
    Task<Result<Archive>> CreateOrAppendFilesAsync(long applicationId, string archiveType, string classification, string archivedBy, IReadOnlyList<ArchiveFile> files, CancellationToken ct = default);

    Task<List<ArchiveFile>> GetFilesAsync(long archiveId, CancellationToken ct = default);

    Task<Result> SyncSchemaAsync(IProgress<ProgressContext> progress = null, CancellationToken ct = default);
}
