using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.ArchiveManagement;

public class TemplateService : BaseService, ITemplateService
{
    protected override string ServiceName => "TemplateService";
    private readonly IDatabaseService _db;

    public TemplateService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<List<Template>> GetAllAsync(CancellationToken ct = default)
    {
        LogInfo("获取所有模板");
        var sql = "SELECT id, name, file_type, categories, config_json, sort_order, created_at, updated_at FROM nc_biz_templates ORDER BY sort_order, id";
        var result = await _db.QueryAsync<Template>(sql, ct);
        return (result.IsSuccess && result.Value != null) ? result.Value : [];
    }

    public async Task<List<Template>> GetByCategoriesAsync(string[] categories, CancellationToken ct = default)
    {
        LogInfo($"按分类查询模板 [{string.Join(", ", categories)}]");
        var sql = "SELECT id, name, file_type, categories, config_json, sort_order, created_at, updated_at FROM nc_biz_templates WHERE categories && $1 ORDER BY sort_order, id";
        var result = await _db.QueryAsync<Template>(sql, ct, (object)categories);
        if (result.IsFailure)
        {
            LogError($"操作失败");
            throw new InvalidOperationException($"执行查询失败: {result.Message}");
        }
        return result.Value ?? [];
    }

    public async Task<Template?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        LogInfo($"获取模板: {id}");
        var sql = "SELECT id, name, file_type, categories, config_json, created_at, updated_at FROM nc_biz_templates WHERE id = $1";
        var result = await _db.QuerySingleAsync<Template>(sql, ct, id);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<Template?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        LogInfo($"按名称获取模板: {name}");
        var sql = "SELECT id, name, file_type, categories, config_json, created_at, updated_at FROM nc_biz_templates WHERE name = $1";
        var result = await _db.QuerySingleAsync<Template>(sql, ct, name);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<Template> SaveAsync(string name, string fileType, byte[] fileData, string[] categories, string configJson, CancellationToken ct = default)
    {
        LogInfo($"保存模板: {name}");
        fileType = TemplateFileTypes.Normalize(fileType);

        var sql = @"INSERT INTO nc_biz_templates (name, file_type, file_data, categories, config_json, created_at)
                     VALUES ($1, $2, $3, $4, $5::jsonb, NOW())
                                           RETURNING id, created_at";

        var result = await _db.QuerySingleAsync<Template>(sql, ct,
            name, fileType, fileData,
            categories ?? Array.Empty<string>(),
            string.IsNullOrEmpty(configJson) ? null : configJson);

        if (!result.IsSuccess || result.Value == null)
            throw new InvalidOperationException($"保存模板失败: {result.Message}");

        return result.Value;
    }

    public async Task<Result> DeleteAsync(long id, CancellationToken ct = default)
    {
        LogInfo($"删除模板: {id}");
        try
        {
            var sql = "DELETE FROM nc_biz_templates WHERE id = $1";
            var result = await _db.ExecuteNonQueryAsync(sql, ct, id);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);

            if (result.Value <= 0)
                return Result.Failure(ErrorCodes.TEMPLATE_NOT_FOUND, $"模板不存在（Id={id}），删除未执行");

            LogInfo($"模板删除成功: Id={id}");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, $"删除模板失败: Id={id}");
            return Result.FromException(ex);
        }
    }

    public async Task<byte[]> GetFileDataAsync(long id, CancellationToken ct = default)
    {
        var sql = "SELECT file_data FROM nc_biz_templates WHERE id = $1";
        var result = await _db.QuerySingleAsync<byte[]>(sql, ct, id);
        return result.IsSuccess && result.Value != null ? result.Value : [];
    }

    public async Task<string> ExportTemplateAsync(long id, string folderPath, string fileName, CancellationToken ct = default)
    {
        LogInfo($"导出模板: {fileName}");

        var bytes = await GetFileDataAsync(id, ct);
        if (bytes.Length == 0)
            throw new InvalidOperationException("模板文件数据为空");

        var filePath = Path.Combine(folderPath, fileName);
        await File.WriteAllBytesAsync(filePath, bytes, ct);
        LogInfo($"模板导出成功: {filePath}");
        return filePath;
    }

    public async Task UpdateConfigJsonAsync(long id, string configJson, CancellationToken ct = default)
    {
        LogInfo($"更新模板配置: {id}");
        var sql = "UPDATE nc_biz_templates SET config_json = $1::jsonb, updated_at = NOW() WHERE id = $2";
        await ExecOrThrowAsync(_db, sql, ct, configJson, id);
    }

    public async Task UpdateCategoriesAsync(long id, string[] categories, CancellationToken ct = default)
    {
        LogInfo($"更新模板分类: {id}");
        var sql = "UPDATE nc_biz_templates SET categories = $1, updated_at = NOW() WHERE id = $2";
        await ExecOrThrowAsync(_db, sql, ct, categories, id);
    }

    public async Task ReorderAsync(long templateId, int newSortOrder, CancellationToken ct = default)
    {
        LogInfo($"调整模板排序: {templateId} → {newSortOrder}");
        var sql = "UPDATE nc_biz_templates SET sort_order = $1, updated_at = NOW() WHERE id = $2";
        await ExecOrThrowAsync(_db, sql, ct, newSortOrder, templateId);
    }

    public async Task ReplaceFileAsync(long id, byte[] fileData, string fileType, CancellationToken ct = default)
    {
        LogInfo($"替换模板文件: {id}");
        fileType = TemplateFileTypes.Normalize(fileType);
        var sql = "UPDATE nc_biz_templates SET file_data = $1, file_type = $2, updated_at = NOW() WHERE id = $3";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, fileData, fileType, id);
        if (!result.IsSuccess || result.Value == 0)
            throw new InvalidOperationException($"替换模板文件失败");
    }

    public async Task<Result> SyncSchemaAsync(IProgress<ProgressContext> progress = null, CancellationToken ct = default)
    {
        LogInfo($"开始同步模板表结构");

        const int totalSteps = 3;

        var steps = new List<(string Name, string Sql)>
        {
            ("删除旧表", @"
                DROP TABLE IF EXISTS nc_biz_template_versions CASCADE;
                DROP TABLE IF EXISTS nc_biz_template_scenarios CASCADE;
                DROP TABLE IF EXISTS nc_biz_template_scenario_codes CASCADE;
                DROP TABLE IF EXISTS nc_biz_template_modules CASCADE;
                DROP TABLE IF EXISTS nc_biz_template_module_codes CASCADE;
                DROP TABLE IF EXISTS nc_biz_template_formulas CASCADE;
                DROP TABLE IF EXISTS nc_biz_template_formats CASCADE;
                DROP TABLE IF EXISTS nc_biz_template_text_templates CASCADE;
                DROP TABLE IF EXISTS nc_biz_placeholder_mappings CASCADE;
                DROP TABLE IF EXISTS nc_biz_template_fields CASCADE;
                DROP TABLE IF EXISTS nc_biz_template_field_definitions CASCADE;
                DROP TABLE IF EXISTS nc_biz_template_placeholders CASCADE;
                DROP TABLE IF EXISTS nc_biz_template_field_groups CASCADE"),

            ("创建新模板表", @"
                CREATE TABLE IF NOT EXISTS nc_biz_templates (
                    id              BIGSERIAL       PRIMARY KEY,
                    name            VARCHAR(200)    NOT NULL,
                    file_type       VARCHAR(20)     NOT NULL,
                    file_data       BYTEA           NOT NULL,
                    categories      TEXT[]          DEFAULT '{}',
                    config_json     JSONB,
                    created_at      TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at      TIMESTAMP       DEFAULT CURRENT_TIMESTAMP
                )"),

            ("创建分类索引", @"
                CREATE INDEX IF NOT EXISTS idx_templates_categories ON nc_biz_templates USING GIN(categories)")
        };

        // DROP→CREATE 必须在同一事务内：任一步失败整体回滚，避免"表已删、建表失败"的半毁状态
        await _db.BeginTransactionAsync();
        try
        {
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
                    LogError($"同步失败: {sqlResult.Message}");
                    await _db.RollbackTransactionAsync();
                    return sqlResult;
                }
            }

            await _db.CommitTransactionAsync();
        }
        catch
        {
            await _db.RollbackTransactionAsync();
            throw;
        }

        LogInfo($"模板同步完成");
        return Result.Success();
    }
}
