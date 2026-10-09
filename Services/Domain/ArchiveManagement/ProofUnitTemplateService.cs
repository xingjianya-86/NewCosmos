using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <inheritdoc />
public class ProofUnitTemplateService : BaseService, IProofUnitTemplateService
{
    /// <summary>单位证明模板分类标签（存 nc_biz_templates.categories）</summary>
    public const string ProofCategory = "证明文件";

    protected override string ServiceName => "ProofUnitTemplateService";

    private readonly IDatabaseService _db;
    private readonly ITemplateService _templateService;

    public ProofUnitTemplateService(IDatabaseService db, ITemplateService templateService, ILoggerService logger)
        : base(logger)
    {
        _db = db;
        _templateService = templateService;
    }

    /// <inheritdoc />
    public async Task<Result<List<ProofUnitTemplateItem>>> GetAllAsync(CancellationToken ct = default)
    {
        try
        {
            var sql = @"SELECT u.id, u.unit_name, u.template_id, u.created_at, u.updated_at,
                               t.name AS template_name, t.file_type AS file_type
                        FROM nc_config_proof_unit_templates u
                        LEFT JOIN nc_biz_templates t ON u.template_id = t.id
                        ORDER BY u.unit_name";
            var result = await _db.QueryAsync<ProofUnitTemplateItem>(sql, ct);
            if (result.IsFailure)
                return Result.Failure<List<ProofUnitTemplateItem>>(result.ErrorCode!, result.Message!);
            return Result.Success(result.Value ?? []);
        }
        catch (Exception ex)
        {
            LogException(ex, "GetAllAsync");
            return Result.FromException<List<ProofUnitTemplateItem>>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ProofUnitTemplateItem>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        try
        {
            var sql = @"SELECT u.id, u.unit_name, u.template_id, u.created_at, u.updated_at,
                               t.name AS template_name, t.file_type AS file_type
                        FROM nc_config_proof_unit_templates u
                        LEFT JOIN nc_biz_templates t ON u.template_id = t.id
                        WHERE u.id = $1";
            var result = await _db.QuerySingleAsync<ProofUnitTemplateItem>(sql, ct, id);
            if (result.IsFailure)
                return Result.Failure<ProofUnitTemplateItem>(result.ErrorCode!, result.Message!);
            if (result.Value == null)
                return Result.Failure<ProofUnitTemplateItem>(ErrorCodes.RECORD_NOT_FOUND, "单位模板不存在");
            return Result.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "GetByIdAsync");
            return Result.FromException<ProofUnitTemplateItem>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Template>> GetTemplateByUnitAsync(string unitName, CancellationToken ct = default)
    {
        try
        {
            var sql = "SELECT template_id FROM nc_config_proof_unit_templates WHERE unit_name = $1 LIMIT 1";
            var templateIdResult = await _db.ExecuteScalarAsync<long?>(sql, ct, unitName);
            if (templateIdResult.IsFailure)
                return Result.Failure<Template>(templateIdResult.ErrorCode!, templateIdResult.Message!);
            if (templateIdResult.Value == null || templateIdResult.Value <= 0)
                return Result.Failure<Template>(ErrorCodes.RECORD_NOT_FOUND, $"单位 [{unitName}] 未配置证明模板");

            var templateResult = await _templateService.GetByIdAsync(templateIdResult.Value.Value, ct);
            if (templateResult.IsFailure)
                return Result.Failure<Template>(templateResult.ErrorCode!, templateResult.Message!);
            var template = templateResult.Value;
            if (template == null)
                return Result.Failure<Template>(ErrorCodes.RECORD_NOT_FOUND, $"单位 [{unitName}] 的证明模板不存在");
            return Result.Success(template);
        }
        catch (Exception ex)
        {
            LogException(ex, "GetTemplateByUnitAsync");
            return Result.FromException<Template>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ProofUnitTemplate>> CreateAsync(
        string unitName, byte[] fileData, string fileName, string fileType,
        string? configJson, int? operatorId, CancellationToken ct = default)
    {
        var name = unitName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<ProofUnitTemplate>(ErrorCodes.VALIDATION_FAILED, "请输入接收单位名称");
        if (fileData == null || fileData.Length == 0)
            return Result.Failure<ProofUnitTemplate>(ErrorCodes.VALIDATION_FAILED, "请选择模板文件");

        try
        {
            // 单位名唯一校验
            var exists = await _db.ExecuteScalarAsync<long?>(
                "SELECT id FROM nc_config_proof_unit_templates WHERE unit_name = $1 LIMIT 1", ct, name);
            if (exists.IsFailure)
                return Result.Failure<ProofUnitTemplate>(exists.ErrorCode!, exists.Message!);
            if (exists.Value is > 0)
                return Result.Failure<ProofUnitTemplate>(ErrorCodes.VALIDATION_FAILED, $"单位 [{name}] 已配置证明模板，请直接修改");

            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            // 事务内建模板 + 写映射
            var templateName = string.IsNullOrWhiteSpace(fileName)
                ? $"{name}证明模板"
                : Path.GetFileNameWithoutExtension(fileName);
            var template = await _templateService.SaveAsync(
                templateName, fileType, fileData, new[] { ProofCategory }, configJson ?? string.Empty, ct);

            var insert = await _db.ExecuteNonQueryAsync(
                @"INSERT INTO nc_config_proof_unit_templates (unit_name, template_id, created_by, updated_by, created_at)
                  VALUES ($1, $2, $3, $3, NOW())",
                ct, name, template.Id, operatorId);
            if (insert.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ProofUnitTemplate>(insert.ErrorCode!, insert.Message!);
            }

            await tx.CommitAsync(ct);

            LogInfo($"新增单位模板: {name} (TemplateId={template.Id})");
            Logger.LogBusiness("新增证明单位模板", ("UnitName", name), ("TemplateId", template.Id.ToString()));
            return Result.Success(new ProofUnitTemplate
            {
                UnitName = name,
                TemplateId = template.Id,
                CreatedBy = operatorId,
                CreatedAt = DateTime.Now
            });
        }
        catch (Exception ex)
        {
            LogException(ex, "CreateAsync");
            return Result.FromException<ProofUnitTemplate>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result> UpdateAsync(long id, string? unitName, byte[]? fileData, string? fileType,
        int? operatorId, CancellationToken ct = default)
    {
        try
        {
            var current = await GetByIdAsync(id, ct);
            if (current.IsFailure)
                return Result.Failure(current.ErrorCode!, current.Message!);
            var item = current.Value!;

            var newName = string.IsNullOrWhiteSpace(unitName) ? item.UnitName : unitName.Trim();
            if (newName != item.UnitName)
            {
                var exists = await _db.ExecuteScalarAsync<long?>(
                    "SELECT id FROM nc_config_proof_unit_templates WHERE unit_name = $1 AND id <> $2 LIMIT 1",
                    ct, newName, id);
                if (exists.IsFailure)
                    return Result.Failure(exists.ErrorCode!, exists.Message!);
                if (exists.Value is > 0)
                    return Result.Failure(ErrorCodes.VALIDATION_FAILED, $"单位 [{newName}] 已配置证明模板");
            }

            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            // 替换模板文件（保留原模板记录，仅替换字节与类型）
            if (fileData != null && fileData.Length > 0 && !string.IsNullOrWhiteSpace(fileType))
            {
                try
                {
                    await _templateService.ReplaceFileAsync(item.TemplateId, fileData, fileType, ct);
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure(ErrorCodes.DB_QUERY_ERROR, $"替换模板文件失败: {ex.Message}");
                }
            }

            var update = await _db.ExecuteNonQueryAsync(
                "UPDATE nc_config_proof_unit_templates SET unit_name = $1, updated_by = $2, updated_at = NOW() WHERE id = $3",
                ct, newName, operatorId, id);
            if (update.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(update.ErrorCode!, update.Message!);
            }

            await tx.CommitAsync(ct);

            LogInfo($"更新单位模板: {id} → {newName}");
            Logger.LogBusiness("更新证明单位模板", ("Id", id.ToString()), ("UnitName", newName));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "UpdateAsync");
            return Result.FromException(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(long id, CancellationToken ct = default)
    {
        try
        {
            var result = await _db.ExecuteNonQueryAsync(
                "DELETE FROM nc_config_proof_unit_templates WHERE id = $1", ct, id);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);
            if (result.Value <= 0)
                return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "单位模板不存在");

            LogInfo($"删除单位模板: {id}");
            Logger.LogBusiness("删除证明单位模板", ("Id", id.ToString()));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "DeleteAsync");
            return Result.FromException(ex);
        }
    }
}