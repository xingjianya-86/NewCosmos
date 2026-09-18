using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.UserManagement;

public class OrganizationService : BaseService, IOrganizationService
{
    protected override string ServiceName => "OrganizationService";
    private readonly IDatabaseService _db;

    public OrganizationService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<Result<Organization>> GetByIdAsync(int id, CancellationToken ct = default)
    {
        LogInfo($"获取组织: Id={id}");
        var sql = @"SELECT o.*, p.name as parent_name,
                    rcity.city_name,
                    rc.county_name, rt.town_name, rv.village_name
                    FROM nc_sys_organizations o 
                    LEFT JOIN nc_sys_organizations p ON o.parent_id = p.id 
                    LEFT JOIN nc_regions_cities rcity ON o.city_id = rcity.id
                    LEFT JOIN nc_regions_counties rc ON o.county_id = rc.id
                    LEFT JOIN nc_regions_towns rt ON o.town_id = rt.id
                    LEFT JOIN nc_regions_villages rv ON o.village_id = rv.id
                    WHERE o.id = $1";
        return await _db.QuerySingleAsync<Organization>(sql, ct, id);
    }

    public async Task<Result<Organization>> GetByNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Organization>(ErrorCodes.VALIDATION_FAILED, "组织名称不能为空");
        LogInfo($"按名称获取组织: {name}");
        var sql = @"SELECT o.*, p.name as parent_name,
                    rcity.city_name,
                    rc.county_name, rt.town_name, rv.village_name
                    FROM nc_sys_organizations o 
                    LEFT JOIN nc_sys_organizations p ON o.parent_id = p.id 
                    LEFT JOIN nc_regions_cities rcity ON o.city_id = rcity.id
                    LEFT JOIN nc_regions_counties rc ON o.county_id = rc.id
                    LEFT JOIN nc_regions_towns rt ON o.town_id = rt.id
                    LEFT JOIN nc_regions_villages rv ON o.village_id = rv.id
                    WHERE o.name = $1
                    LIMIT 1";
        return await _db.QuerySingleAsync<Organization>(sql, ct, name);
    }

    public async Task<Result<List<Organization>>> GetAllAsync(CancellationToken ct = default)
    {
        LogInfo("获取所有组织");
        var sql = @"SELECT o.*, p.name as parent_name,
                    rcity.city_name,
                    rc.county_name, rt.town_name, rv.village_name
                    FROM nc_sys_organizations o 
                    LEFT JOIN nc_sys_organizations p ON o.parent_id = p.id 
                    LEFT JOIN nc_regions_cities rcity ON o.city_id = rcity.id
                    LEFT JOIN nc_regions_counties rc ON o.county_id = rc.id
                    LEFT JOIN nc_regions_towns rt ON o.town_id = rt.id
                    LEFT JOIN nc_regions_villages rv ON o.village_id = rv.id
                    ORDER BY o.created_at";
        return await _db.QueryAsync<Organization>(sql, ct);
    }

    public async Task<Result<List<Organization>>> GetChildrenAsync(int parentId, CancellationToken ct = default)
    {
        LogInfo($"获取子组织: ParentId={parentId}");
        var sql = @"SELECT o.*, p.name as parent_name,
                    rcity.city_name,
                    rc.county_name, rt.town_name, rv.village_name
                    FROM nc_sys_organizations o 
                    LEFT JOIN nc_sys_organizations p ON o.parent_id = p.id 
                    LEFT JOIN nc_regions_cities rcity ON o.city_id = rcity.id
                    LEFT JOIN nc_regions_counties rc ON o.county_id = rc.id
                    LEFT JOIN nc_regions_towns rt ON o.town_id = rt.id
                    LEFT JOIN nc_regions_villages rv ON o.village_id = rv.id
                    WHERE o.parent_id = $1
                    ORDER BY o.created_at";
        return await _db.QueryAsync<Organization>(sql, ct, parentId);
    }

    public async Task<Result<int>> CreateAsync(OrganizationCreateRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));
        ValidateNotNullOrEmpty(request.Name, nameof(request.Name));

        LogInfo($"创建组织: {request.Name}");

        var existsResult = await CheckNameExistsAsync(request.Name, null, ct);
        if (existsResult.IsSuccess && existsResult.Value)
            return Result.Failure<int>(ErrorCodes.DUPLICATE_ID_CARD, "组织名称已存在");

        var sql = @"INSERT INTO nc_sys_organizations 
            (name, abbreviation, principal, principal_identity_card, phone, address, postal_code, parent_id, 
             institution_nature, institution_type, institution_category,
             is_special_care_institution, city_id, county_id, town_id, village_id, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, NOW(), NOW())
            RETURNING id";

        var insertResult = await _db.ExecuteScalarAsync(sql, ct,
            request.Name, request.Abbreviation, request.Principal, request.PrincipalIdentityCard, request.Phone,
            request.Address, request.PostalCode, request.ParentId,
            request.InstitutionNature, request.InstitutionType, request.InstitutionCategory,
            request.IsSpecialCareInstitution, request.CityId, request.CountyId, request.TownId, request.VillageId);

        if (insertResult.IsFailure)
            return Result.Failure<int>(insertResult.ErrorCode!, insertResult.Message!);

        var orgId = Convert.ToInt32(insertResult.Value);
        LogInfo($"组织创建成功: Id={orgId}");
        Logger.LogBusiness("创建组织", ("OrganizationId", orgId), ("Name", request.Name));
        return Result.Success(orgId);
    }

    public async Task<Result<int>> CreateAsync(OrganizationSaveRequest request, CancellationToken ct = default)
    {
        var createRequest = new OrganizationCreateRequest
        {
            Name = request.Name,
            Abbreviation = request.Abbreviation,
            Principal = request.Principal,
            PrincipalIdentityCard = request.PrincipalIdentityCard,
            Phone = request.Phone,
            Address = request.Address,
            PostalCode = request.PostalCode,
            ParentId = request.ParentId,
            InstitutionNature = request.InstitutionNature,
            InstitutionType = request.InstitutionType,
            InstitutionCategory = request.InstitutionCategory,
            IsSpecialCareInstitution = request.IsSpecialCareInstitution,
            CityId = request.CityId,
            CountyId = request.CountyId,
            TownId = request.TownId,
            VillageId = request.VillageId,
            CreatedBy = request.SavedBy
        };
        return await CreateAsync(createRequest, ct);
    }

    public async Task<Result> UpdateAsync(OrganizationUpdateRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));

        LogInfo($"更新组织: Id={request.OrganizationId}");

        var orgResult = await GetByIdAsync(request.OrganizationId, ct);
        if (orgResult.IsFailure)
            return Result.Failure(orgResult.ErrorCode!, orgResult.Message!);

        var org = orgResult.Value;
        if (org == null)
            return Result.Failure(ErrorCodes.NOT_FOUND, "组织不存在");

        var sql = @"UPDATE nc_sys_organizations SET 
            name = $1, abbreviation = $2, principal = $3, principal_identity_card = $4, phone = $5,
            address = $6, postal_code = $7, parent_id = $8,
            institution_nature = $9, institution_type = $10, institution_category = $11,
            is_special_care_institution = $12, city_id = $13, county_id = $14, town_id = $15, village_id = $16,
            updated_at = NOW()
            WHERE id = $17";

        var updateResult = await _db.ExecuteNonQueryAsync(sql, ct,
            request.Name, request.Abbreviation, request.Principal, request.PrincipalIdentityCard, request.Phone,
            request.Address, request.PostalCode, request.ParentId,
            request.InstitutionNature, request.InstitutionType, request.InstitutionCategory,
            request.IsSpecialCareInstitution, request.CityId, request.CountyId, request.TownId, request.VillageId,
            request.OrganizationId);

        if (updateResult.IsFailure)
            return Result.Failure(updateResult.ErrorCode!, updateResult.Message!);

        LogInfo($"组织更新成功: Id={request.OrganizationId}");
        return Result.Success();
    }

    public async Task<Result> UpdateAsync(int id, OrganizationSaveRequest request, CancellationToken ct = default)
    {
        var updateRequest = new OrganizationUpdateRequest
        {
            OrganizationId = id,
            Name = request.Name,
            Abbreviation = request.Abbreviation,
            Principal = request.Principal,
            PrincipalIdentityCard = request.PrincipalIdentityCard,
            Phone = request.Phone,
            Address = request.Address,
            PostalCode = request.PostalCode,
            ParentId = request.ParentId,
            InstitutionNature = request.InstitutionNature,
            InstitutionType = request.InstitutionType,
            InstitutionCategory = request.InstitutionCategory,
            IsSpecialCareInstitution = request.IsSpecialCareInstitution,
            CityId = request.CityId,
            CountyId = request.CountyId,
            TownId = request.TownId,
            VillageId = request.VillageId,
            UpdatedBy = request.SavedBy
        };
        return await UpdateAsync(updateRequest, ct);
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        LogInfo($"删除组织: Id={id}");

        var orgResult = await GetByIdAsync(id, ct);
        if (orgResult.IsFailure)
            return Result.Failure(orgResult.ErrorCode!, orgResult.Message!);

        var org = orgResult.Value;
        if (org == null)
            return Result.Failure(ErrorCodes.NOT_FOUND, "组织不存在");

        var childrenResult = await GetChildrenAsync(id, ct);
        if (childrenResult.IsSuccess && childrenResult.Value.Count > 0)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "存在子组织，无法删除");

        var userCountResult = await GetUserCountAsync(id, ct);
        if (userCountResult.IsSuccess && userCountResult.Value > 0)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "组织下存在用户，无法删除");

        var sql = "DELETE FROM nc_sys_organizations WHERE id = $1";
        var deleteResult = await _db.ExecuteNonQueryAsync(sql, ct, id);

        if (deleteResult.IsFailure)
            return Result.Failure(deleteResult.ErrorCode!, deleteResult.Message!);

        LogInfo($"组织删除成功: Id={id}");
        return Result.Success();
    }

    public async Task<Result<List<OrganizationTreeNode>>> GetTreeAsync(CancellationToken ct = default)
    {
        LogInfo("获取组织树结构");

        var allOrgsResult = await GetAllAsync(ct);
        if (allOrgsResult.IsFailure)
            return Result.Failure<List<OrganizationTreeNode>>(allOrgsResult.ErrorCode!, allOrgsResult.Message!);

        var allOrgs = allOrgsResult.Value;
        
        var depthMap = await CalculateDepthsAsync(allOrgs, ct);
        
        var rootNodes = BuildTree(allOrgs, null, depthMap);

        return Result.Success(rootNodes);
    }

    public async Task<Result<bool>> CheckNameExistsAsync(string name, int? excludeId = null, CancellationToken ct = default)
    {
        ValidateNotNullOrEmpty(name, nameof(name));

        string sql;
        object[] parameters;

        if (excludeId.HasValue)
        {
            sql = "SELECT COUNT(*) FROM nc_sys_organizations WHERE name = $1 AND id != $2";
            parameters = new object[] { name, excludeId.Value };
        }
        else
        {
            sql = "SELECT COUNT(*) FROM nc_sys_organizations WHERE name = $1";
            parameters = new object[] { name };
        }

        var result = await _db.ExecuteScalarAsync(sql, ct, parameters);
        if (result.IsFailure)
            return Result.Failure<bool>(result.ErrorCode!, result.Message!);

        return Result.Success(result.Value > 0);
    }

    public async Task<Result<int>> GetUserCountAsync(int organizationId, CancellationToken ct = default)
    {
        var sql = "SELECT COUNT(*) FROM nc_sys_users WHERE organization_id = $1";
        var result = await _db.ExecuteScalarAsync(sql, ct, organizationId);
        if (result.IsFailure)
            return Result.Failure<int>(result.ErrorCode!, result.Message!);

        return Result.Success(Convert.ToInt32(result.Value));
    }

    public OrganizationCsvPreview PreviewCsvImport(string filePath, int previewCount = 5)
    {
        LogInfo($"预览CSV导入: {filePath}");
        var lines = File.ReadAllLines(filePath);
        if (lines.Length < 2)
        {
            return new OrganizationCsvPreview { TotalRows = 0 };
        }

        var headers = ParseCsvLine(lines[0]);
        var headerMap = BuildHeaderMap(headers);

        if (!headerMap.ContainsKey("名称") && !headerMap.ContainsKey("name"))
        {
            LogWarn("CSV文件缺少「名称」列");
            return new OrganizationCsvPreview { TotalRows = 0 };
        }

        var totalRows = lines.Length - 1;
        var previewCountActual = Math.Min(previewCount, totalRows);
        var previewRows = new List<OrganizationPreviewRow>();

        for (int i = 1; i <= previewCountActual; i++)
        {
            var cols = ParseCsvLine(lines[i]);
            previewRows.Add(new OrganizationPreviewRow
            {
                RowNumber = i,
                Name = GetColumnValue(cols, headerMap, "名称", "name"),
                Abbreviation = GetColumnValue(cols, headerMap, "简称", "abbreviation"),
                Principal = GetColumnValue(cols, headerMap, "负责人", "principal"),
                Phone = GetColumnValue(cols, headerMap, "电话", "phone"),
                Address = GetColumnValue(cols, headerMap, "地址", "address")
            });
        }

        LogInfo($"CSV预览完成: 共{totalRows}行, 预览{previewCountActual}行");
        return new OrganizationCsvPreview
        {
            TotalRows = totalRows,
            PreviewRows = previewRows
        };
    }

    public async Task<ImportResult> ImportFromCsvAsync(string filePath, IProgress<string> progress = null, CancellationToken ct = default)
    {
        LogInfo($"开始CSV导入: {filePath}");
        var result = new ImportResult { FilePath = filePath };

        try
        {
            var lines = await File.ReadAllLinesAsync(filePath, ct);
            if (lines.Length < 2)
            {
                return ImportResult.Failed("文件无有效数据");
            }

            var headers = ParseCsvLine(lines[0]);
            var headerMap = BuildHeaderMap(headers);

            var totalRows = lines.Length - 1;
            var successCount = 0;
            var errorCount = 0;

            for (int i = 1; i < lines.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    totalRows--;
                    continue;
                }

                var cols = ParseCsvLine(lines[i]);
                var rowNum = i;

                var request = new OrganizationSaveRequest
                {
                    Name = GetColumnValue(cols, headerMap, "名称", "name"),
                    Abbreviation = GetColumnValue(cols, headerMap, "简称", "abbreviation"),
                    Principal = GetColumnValue(cols, headerMap, "负责人", "principal"),
                    Phone = GetColumnValue(cols, headerMap, "电话", "phone"),
                    Address = GetColumnValue(cols, headerMap, "地址", "address"),
                    PostalCode = GetColumnValue(cols, headerMap, "邮编", "postal_code", "postalcode"),
                    InstitutionNature = GetColumnValue(cols, headerMap, "机构性质", "institution_nature"),
                    InstitutionType = GetColumnValue(cols, headerMap, "机构类型", "institution_type"),
                    SavedBy = "Import"
                };

                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    errorCount++;
                    result.Errors.Add($"第 {rowNum} 行：名称不能为空");
                    progress?.Report($"第 {rowNum} 行：名称不能为空");
                    continue;
                }

                try
                {
                    var saveResult = await SaveAsync(request, ct);
                    if (saveResult.IsSuccess)
                    {
                        successCount++;
                    }
                    else
                    {
                        errorCount++;
                        result.Errors.Add($"第 {rowNum} 行：{saveResult.Message}");
                        progress?.Report($"第 {rowNum} 行：{saveResult.Message}");
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    result.Errors.Add($"第 {rowNum} 行：{ex.Message}");
                    progress?.Report($"第 {rowNum} 行：{ex.Message}");
                }
            }

            result.Success = true;
            result.ImportedCount = successCount;
            result.ErrorCount = errorCount;
            result.Message = $"导入完成：成功 {successCount}，失败 {errorCount}";
            Logger.LogBusiness("组织CSV导入完成", ("FilePath", filePath), ("Success", successCount), ("Failure", errorCount));
            LogInfo($"CSV导入完成: 成功{successCount}, 失败{errorCount}");
        }
        catch (OperationCanceledException)
        {
            LogWarn("CSV导入被取消");
            return ImportResult.Failed("导入已取消");
        }
        catch (Exception ex)
        {
            LogError($"CSV导入失败: {ex.Message}");
            return ImportResult.Failed($"导入失败: {ex.Message}");
        }

        return result;
    }

    private static string[] ParseCsvLine(string line)
    {
        return line.Split('\t', ',');
    }

    private static Dictionary<string, int> BuildHeaderMap(string[] headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++)
        {
            var key = headers[i].Trim().ToLowerInvariant();
            if (!map.ContainsKey(key))
                map[key] = i;
        }
        return map;
    }

    private static string GetColumnValue(string[] cols, Dictionary<string, int> headerMap, params string[] keys)
    {
        foreach (var key in keys)
        {
            var lowerKey = key.ToLowerInvariant();
            if (headerMap.TryGetValue(lowerKey, out var index) && index < cols.Length)
                return cols[index].Trim();
        }
        return string.Empty;
    }

    public async Task<Result> SaveAsync(OrganizationSaveRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));

        if (request.Id.HasValue && request.Id.Value > 0)
        {
            return await UpdateAsync(request.Id.Value, request, ct);
        }
        else
        {
            var result = await CreateAsync(request, ct);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);
            return Result.Success();
        }
    }

    private Task<Dictionary<int, int>> CalculateDepthsAsync(List<Organization> allOrgs, CancellationToken ct)
    {
        var depthMap = new Dictionary<int, int>();

        var rootOrgs = allOrgs.Where(o => o.ParentId == null).ToList();
        foreach (var root in rootOrgs)
        {
            depthMap[root.Id] = 0;
            CalculateChildDepths(allOrgs, root.Id, 1, depthMap);
        }

        return Task.FromResult(depthMap);
    }

    private void CalculateChildDepths(List<Organization> allOrgs, int? parentId, int currentDepth, Dictionary<int, int> depthMap)
    {
        var children = allOrgs.Where(o => o.ParentId == parentId).ToList();
        foreach (var child in children)
        {
            depthMap[child.Id] = currentDepth;
            CalculateChildDepths(allOrgs, child.Id, currentDepth + 1, depthMap);
        }
    }

    private static List<OrganizationTreeNode> BuildTree(List<Organization> allOrgs, int? parentId, Dictionary<int, int> depthMap)
    {
        return allOrgs
            .Where(o => o.ParentId == parentId)
            .Select(o => new OrganizationTreeNode
            {
                Id = o.Id,
                Name = o.Name,
                Abbreviation = o.Abbreviation,
                Depth = depthMap.GetValueOrDefault(o.Id, 0),
                Children = BuildTree(allOrgs, o.Id, depthMap)
            })
            .OrderBy(o => o.Name)
            .ToList();
    }
}
