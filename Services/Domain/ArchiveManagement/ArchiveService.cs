using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.ArchiveManagement;

public class ArchiveService : BaseService, IArchiveService
{
    protected override string ServiceName => "ArchiveService";
    private readonly IDatabaseService _db;

    public ArchiveService(IDatabaseService db, ILoggerService logger) : base(logger) { _db = db; }

    public async Task<Archive?> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        var sql = "SELECT * FROM nc_biz_archives WHERE application_id = $1 AND deleted_at IS NULL ORDER BY archived_at DESC LIMIT 1";
        var result = await _db.QuerySingleAsync<Archive>(sql, ct, applicationId);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<Archive> CreateAsync(Archive archive, CancellationToken ct = default)
    {
        archive.ArchivedAt = DateTime.Now;

        var sql = @"INSERT INTO nc_biz_archives (application_id, archive_no, archive_type, classification_result, archived_by, output_files)
                     VALUES ($1,$2,$3,$4,$5,$6::jsonb) RETURNING id";

        // archive_no 追加 3 位随机尾号：纯毫秒时间戳在批量归档时同一毫秒内必撞
        // UNIQUE 约束。加随机尾号后仍撞车（1/1000）则重新生成后再试，最多 3 次。
        // 注意：若当前已处于环境事务，INSERT 失败会令整个事务进入 aborted 状态，
        // 事务内重试同一语句只会得到 25P02，因此仅在无环境事务时才做撞号重试。
        var maxAttempts = _db.HasTransaction ? 1 : 3;
        for (var attempt = 1; ; attempt++)
        {
            archive.ArchiveNo = GenerateArchiveNo();

            var result = await _db.ExecuteScalarAsync(sql, ct,
                archive.ApplicationId, archive.ArchiveNo, archive.ArchiveType,
                archive.ClassificationResult, archive.ArchivedBy, archive.OutputFiles);

            if (result.IsSuccess)
            {
                // 插入失败不再静默返回 Id=0 的档案对象（调用方会拿 Id=0 去挂文件），
                // 接口签名返回 Archive 而非 Result，故以异常中断
                archive.Id = result.Value;
                return archive;
            }

            if (attempt < maxAttempts && IsUniqueViolation(result))
            {
                LogWarn($"档案编号撞号，重新生成后重试: {archive.ArchiveNo} (第 {attempt} 次)");
                continue;
            }

            throw new BusinessException(
                string.IsNullOrEmpty(result.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : result.ErrorCode,
                $"创建档案记录失败: {result.Message}");
        }
    }

    private static string GenerateArchiveNo() =>
        $"ARC{DateTime.Now:yyyyMMddHHmmssfff}{Random.Shared.Next(0, 1000):D3}";

    /// <summary>
    /// 判断写入失败是否由 UNIQUE 约束冲突引起（错误码映射不总可靠，兼看消息文本）
    /// </summary>
    private static bool IsUniqueViolation(Result result)
    {
        if (result.ErrorCode == ErrorCodes.DB_UNIQUE_VIOLATION) return true;
        var msg = result.Message ?? string.Empty;
        return msg.Contains("23505") || msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }

    public async Task AddFileAsync(long archiveId, ArchiveFile file, CancellationToken ct = default)
    {
        var sql = @"INSERT INTO nc_biz_archive_files (archive_id, template_id, file_name, file_type, file_size, output_path)
                    VALUES ($1,$2,$3,$4,$5,$6)";
        var result = await _db.ExecuteNonQueryAsync(sql, ct,
            archiveId, file.TemplateId, file.FileName, file.FileType, file.FileSize, file.OutputPath);
        // 归档文件记录写失败必须暴露：否则"归档成功"但文件清单缺行，档案不完整
        if (result.IsFailure)
            throw new BusinessException(
                string.IsNullOrEmpty(result.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : result.ErrorCode,
                $"保存档案文件记录失败: {file.FileName} - {result.Message}");
    }

    /// <summary>
    /// 在单个事务内创建档案并写入全部文件记录：任一步失败整体回滚，
    /// 不会留下"有档案无文件"或半截文件清单的中间状态。
    /// （CreateAsync / AddFileAsync 保留为兼容旧调用方的独立入口）
    /// </summary>
    public async Task<Result<Archive>> CreateWithFilesAsync(Archive archive, IReadOnlyList<ArchiveFile> files, CancellationToken ct = default)
    {
        ValidateNotNull(archive, nameof(archive));

        try
        {
            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var created = await CreateAsync(archive, ct);
            foreach (var file in files ?? Array.Empty<ArchiveFile>())
                await AddFileAsync(created.Id, file, ct);

            await tx.CommitAsync(ct);
            LogInfo($"归档创建完成: ArchiveNo={created.ArchiveNo}, Files={files?.Count ?? 0}");
            return Result.Success(created);
        }
        catch (Exception ex)
        {
            // 事务作用域未 Commit 时 DisposeAsync 已自动回滚
            LogException(ex, "归档创建（含文件）");
            return Result.FromException<Archive>(ex);
        }
    }

    public async Task<List<ArchiveFile>> GetFilesAsync(long archiveId, CancellationToken ct = default)
    {
        var sql = "SELECT * FROM nc_biz_archive_files WHERE archive_id = $1 ORDER BY id";
        var result = await _db.QueryAsync<ArchiveFile>(sql, ct, archiveId);
        return (result.IsSuccess && result.Value != null) ? result.Value : new List<ArchiveFile>();
    }

    /// <summary>
    /// 幂等归档：同一申请已有档案则仅追加文件记录；无档案则新建档案并写入文件记录。
    /// </summary>
    public async Task<Result<Archive>> CreateOrAppendFilesAsync(
        long applicationId, string archiveType, string classification,
        string archivedBy, IReadOnlyList<ArchiveFile> files, CancellationToken ct = default)
    {
        ValidateNotNull(files, nameof(files));

        try
        {
            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var existing = await GetByApplicationIdAsync(applicationId, ct);

            Archive archive;
            if (existing != null && existing.Id > 0)
            {
                // 已归档：仅补挂文件，不重复建档
                archive = existing;
                foreach (var file in files)
                    await AddFileAsync(archive.Id, file, ct);
                LogInfo($"一事一议归档追加文件: ApplicationId={applicationId}, ArchiveNo={archive.ArchiveNo}, Files={files.Count}");
            }
            else
            {
                archive = new Archive
                {
                    ApplicationId = applicationId,
                    ArchiveType = archiveType,
                    ClassificationResult = classification,
                    ArchivedBy = archivedBy
                };
                var created = await CreateAsync(archive, ct);
                foreach (var file in files)
                    await AddFileAsync(created.Id, file, ct);
                archive = created;
                LogInfo($"一事一议归档创建: ApplicationId={applicationId}, ArchiveNo={created.ArchiveNo}, Files={files.Count}");
            }

            await tx.CommitAsync(ct);
            return Result.Success(archive);
        }
        catch (Exception ex)
        {
            LogException(ex, "一事一议归档（含文件）");
            return Result.FromException<Archive>(ex);
        }
    }

    public async Task<Result> SyncSchemaAsync(IProgress<ProgressContext> progress = null, CancellationToken ct = default)
    {
        LogInfo("开始执行模式同步");

        const int totalSteps = 5;

        var steps = new List<(string Name, string Sql)>
        {
            ("创建档案归档", @"
                CREATE TABLE IF NOT EXISTS nc_biz_archives (
                    id                      BIGSERIAL       PRIMARY KEY,
                    application_id          BIGINT          NOT NULL,
                    archive_no              VARCHAR(32)     NOT NULL UNIQUE,
                    archive_type            VARCHAR(50),
                    classification_result   VARCHAR(100),
                    archived_at             TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    archived_by             VARCHAR(100),
                    output_files            JSONB,
                    created_at              TIMESTAMP       DEFAULT CURRENT_TIMESTAMP,
                    deleted_at              TIMESTAMP
                )"),

            ("创建档案申请ID索引", @"
                CREATE INDEX IF NOT EXISTS idx_archives_app_id ON nc_biz_archives(application_id)"),

            ("创建档案类型索引", @"
                CREATE INDEX IF NOT EXISTS idx_archives_type ON nc_biz_archives(archive_type)"),

            ("创建档案文件", @"
                CREATE TABLE IF NOT EXISTS nc_biz_archive_files (
                    id                  BIGSERIAL       PRIMARY KEY,
                    archive_id          BIGINT          NOT NULL,
                    template_id         VARCHAR(100),
                    file_name           VARCHAR(500),
                    file_type           VARCHAR(20),
                    file_size           BIGINT,
                    output_path         VARCHAR(1000),
                    created_at          TIMESTAMP       DEFAULT CURRENT_TIMESTAMP
                )"),

            ("创建档案文件索引", @"
                CREATE INDEX IF NOT EXISTS idx_archive_files_archive_id ON nc_biz_archive_files(archive_id)")
        };

        for (var i = 0; i < steps.Count; i++)
        {
            if (progress != null)
            {
                progress?.Report(new ProgressContext
                {
                    CurrentStep = i + 1,
                    TotalSteps = totalSteps,
                    CurrentStepName = steps[i].Name
                });
            }

            var sqlResult = await _db.ExecuteNonQueryAsync(steps[i].Sql, ct);
            if (sqlResult.IsFailure)
            {
                LogError($"步骤 [{steps[i].Name}] 执行失败");
                return sqlResult;
            }

            await Task.Delay(50, ct);
        }

        LogInfo("模式同步完成");
        return Result.Success();
    }
}
