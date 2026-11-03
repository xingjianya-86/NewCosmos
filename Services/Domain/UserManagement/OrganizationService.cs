using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Import;
using NewCosmos.Services.System;

namespace NewCosmos.Services.Domain.UserManagement;

public class OrganizationService : BaseService, IOrganizationService
{
    protected override string ServiceName => "OrganizationService";
    private readonly IDatabaseService _db;
    private readonly IRegionService _regionService;

    /// <summary>
    /// 组织查询统一投影：明确列（AGENTS §8，禁 SELECT *）+ 上级机构与四级区划 JOIN。
    /// 单条/列表查询共用，改列只改这里（原实现是 4 段逐字重复的 SQL）。
    /// </summary>
    private const string OrgSelect = @"
        SELECT o.id, o.name, o.abbreviation, o.principal, o.principal_identity_card, o.phone,
               o.address, o.postal_code, o.parent_id, o.institution_nature, o.institution_type,
               o.institution_category, o.is_special_care_institution,
               o.province_id, o.city_id, o.county_id, o.town_id, o.village_id,
               o.created_at, o.updated_at,
               p.name AS parent_name,
               rp.province_name, rcity.city_name, rc.county_name, rt.town_name, rv.village_name
        FROM nc_sys_organizations o
             LEFT JOIN nc_sys_organizations p  ON o.parent_id   = p.id
             LEFT JOIN nc_regions_provinces rp ON o.province_id = rp.id
             LEFT JOIN nc_regions_cities    rcity ON o.city_id    = rcity.id
             LEFT JOIN nc_regions_counties  rc    ON o.county_id  = rc.id
             LEFT JOIN nc_regions_towns     rt    ON o.town_id    = rt.id
             LEFT JOIN nc_regions_villages  rv    ON o.village_id = rv.id";

    public OrganizationService(IDatabaseService db, IRegionService regionService, ILoggerService logger) : base(logger)
    {
        _db = db;
        _regionService = regionService;
    }

    public async Task<Result<Organization>> GetByIdAsync(int id, CancellationToken ct = default)
    {
        LogInfo($"获取组织: Id={id}");
        var sql = OrgSelect + " WHERE o.id = $1";
        return await _db.QuerySingleAsync<Organization>(sql, ct, id);
    }

    public async Task<Result<Organization>> GetByNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Organization>(ErrorCodes.VALIDATION_FAILED, "组织名称不能为空");
        LogInfo($"按名称获取组织: {name}");
        var sql = OrgSelect + " WHERE o.name = $1 LIMIT 1";
        return await _db.QuerySingleAsync<Organization>(sql, ct, name);
    }

    public async Task<Result<List<Organization>>> GetAllAsync(CancellationToken ct = default)
    {
        LogInfo("获取所有组织");
        var sql = OrgSelect + " ORDER BY o.created_at";
        return await _db.QueryAsync<Organization>(sql, ct);
    }

    public async Task<Result<List<Organization>>> GetChildrenAsync(int parentId, CancellationToken ct = default)
    {
        LogInfo($"获取子组织: ParentId={parentId}");
        var sql = OrgSelect + " WHERE o.parent_id = $1 ORDER BY o.created_at";
        return await _db.QueryAsync<Organization>(sql, ct, parentId);
    }

    public async Task<Result<int>> CreateAsync(OrganizationCreateRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));
        ValidateNotNullOrEmpty(request.Name, nameof(request.Name));

        LogInfo($"创建组织: {request.Name}");

        var existsResult = await CheckNameExistsAsync(request.Name, null, ct);
        if (existsResult.IsSuccess && existsResult.Value)
            return Result.Failure<int>(ErrorCodes.ORGANIZATION_NAME_DUPLICATE, "组织名称已存在");
        if (existsResult.IsFailure)
            return Result.Failure<int>(existsResult.ErrorCode!, existsResult.Message!);

        var sql = @"INSERT INTO nc_sys_organizations 
            (name, abbreviation, principal, principal_identity_card, phone, address, postal_code, parent_id, 
             institution_nature, institution_type, institution_category,
             is_special_care_institution, province_id, city_id, county_id, town_id, village_id, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, NOW(), NOW())
            RETURNING id";

        // 泛型版取 RETURNING id（AGENTS §8：旧版无泛型 API 无行时返回 Success(0)，勿用于 RETURNING id）
        var insertResult = await _db.ExecuteScalarAsync<int?>(sql, ct,
            request.Name, request.Abbreviation, request.Principal, request.PrincipalIdentityCard, request.Phone,
            request.Address, request.PostalCode, request.ParentId,
            request.InstitutionNature, request.InstitutionType, request.InstitutionCategory,
            request.IsSpecialCareInstitution, request.ProvinceId, request.CityId, request.CountyId,
            request.TownId, request.VillageId);

        if (insertResult.IsFailure)
        {
            // 唯一索引 ux_nc_sys_organizations_name 兜底：
            // 应用层 CheckNameExistsAsync 与 INSERT 之间存在并发窗口，查重通过≠一定能插进去
            if (insertResult.ErrorCode == ErrorCodes.DB_UNIQUE_VIOLATION)
                return Result.Failure<int>(ErrorCodes.ORGANIZATION_NAME_DUPLICATE, "组织名称已存在");

            return Result.Failure<int>(insertResult.ErrorCode!, insertResult.Message!);
        }

        if (insertResult.Value is null or <= 0)
            return Result.Failure<int>(ErrorCodes.OPERATION_FAILED, "创建组织失败：未返回新组织ID");

        var orgId = insertResult.Value.Value;
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
            ProvinceId = request.ProvinceId,
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
            is_special_care_institution = $12, province_id = $13, city_id = $14, county_id = $15,
            town_id = $16, village_id = $17,
            updated_at = NOW()
            WHERE id = $18";

        var updateResult = await _db.ExecuteNonQueryAsync(sql, ct,
            request.Name, request.Abbreviation, request.Principal, request.PrincipalIdentityCard, request.Phone,
            request.Address, request.PostalCode, request.ParentId,
            request.InstitutionNature, request.InstitutionType, request.InstitutionCategory,
            request.IsSpecialCareInstitution, request.ProvinceId, request.CityId, request.CountyId,
            request.TownId, request.VillageId,
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

        // 校验子组织/用户数：查询失败必须显式失败，不能当成"没有子组织/没有用户"继续删
        // （失败当空集合 = 绕过保护直接 DELETE，违反 AGENTS §7）
        var childrenResult = await GetChildrenAsync(id, ct);
        if (childrenResult.IsFailure)
            return Result.Failure(childrenResult.ErrorCode!, childrenResult.Message!);
        if (childrenResult.Value is { Count: > 0 })
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "存在子组织，无法删除");

        var userCountResult = await GetUserCountAsync(id, ct);
        if (userCountResult.IsFailure)
            return Result.Failure(userCountResult.ErrorCode!, userCountResult.Message!);
        if (userCountResult.Value > 0)
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

        return Result.Success(BuildTree(allOrgsResult.Value ?? new List<Organization>()));
    }

    /// <summary>
    /// 由全量组织列表在内存构建组织树（不查库）。
    /// 按 ParentId 建 Lookup，复杂度 O(n)；原实现对每个节点线性扫描全表（O(n²)）。
    /// 带环保护：parent_id 成环（A→B→A）时旧实现会无限递归 → StackOverflow 不可捕获、直接崩进程。
    /// </summary>
    public List<OrganizationTreeNode> BuildTree(List<Organization> allOrgs)
    {
        if (allOrgs == null || allOrgs.Count == 0)
            return new List<OrganizationTreeNode>();

        var byId = new Dictionary<int, Organization>(allOrgs.Count);
        foreach (var org in allOrgs)
            byId.TryAdd(org.Id, org);          // 主键不重复，TryAdd 仅防御脏数据

        var byParent = allOrgs.ToLookup(o => o.ParentId);
        var depthMap = new Dictionary<int, int>(allOrgs.Count);
        var queue = new Queue<(int Id, int Depth)>();

        void EnqueueRoot(Organization org)
        {
            if (depthMap.ContainsKey(org.Id)) return;
            depthMap[org.Id] = 0;
            queue.Enqueue((org.Id, 0));
        }

        void Bfs()
        {
            while (queue.Count > 0)
            {
                var (id, depth) = queue.Dequeue();
                foreach (var child in byParent[id])
                {
                    if (depthMap.ContainsKey(child.Id)) continue;   // 已入队：环在此截断
                    depthMap[child.Id] = depth + 1;
                    queue.Enqueue((child.Id, depth + 1));
                }
            }
        }

        // 根 = 无上级，或上级不存在（孤儿节点不当根会整棵子树丢失）
        foreach (var org in allOrgs)
            if (org.ParentId == null || !byId.ContainsKey(org.ParentId.Value))
                EnqueueRoot(org);
        Bfs();

        // 仍未访问的必然是 parent_id 成环（A→B→A）：标记出来，稍后作为"额外根"补进树里，
        // 否则这些机构会从树上凭空消失（旧实现更糟——无限递归直接 StackOverflow 崩进程）
        var cycleRoots = new List<Organization>();
        foreach (var org in allOrgs)
        {
            if (depthMap.ContainsKey(org.Id)) continue;
            LogWarn($"组织树检测到 parent_id 成环，节点降级为根: Id={org.Id}, ParentId={org.ParentId}");
            cycleRoots.Add(org);
            EnqueueRoot(org);
            Bfs();
        }

        var visited = new HashSet<int>(allOrgs.Count);
        var nodes = BuildNodes(byParent, null, depthMap, visited);
        foreach (var org in cycleRoots)
        {
            if (visited.Contains(org.Id)) continue;   // 已被同环的另一个根带出
            nodes.AddRange(BuildNodes(byParent, org.Id, depthMap, visited));
        }

        return nodes.OrderBy(n => n.Name).ToList();
    }

    /// <summary>
    /// 递归组装树节点。<paramref name="visited"/> 全树共享：每个节点最多出现一次，
    /// 同时保证成环的 parent 链不会无限下探。
    /// </summary>
    private static List<OrganizationTreeNode> BuildNodes(
        ILookup<int?, Organization> byParent, int? parentId,
        Dictionary<int, int> depthMap, HashSet<int> visited)
    {
        var nodes = new List<OrganizationTreeNode>();
        foreach (var org in byParent[parentId])
        {
            if (!visited.Add(org.Id)) continue;      // 已出现：环或重复父链在此截断

            nodes.Add(new OrganizationTreeNode
            {
                Id = org.Id,
                Name = org.Name,
                Abbreviation = org.Abbreviation,
                Depth = depthMap.GetValueOrDefault(org.Id, 0),
                Children = BuildNodes(byParent, org.Id, depthMap, visited)
            });
        }
        // 排序保持与原实现一致（OrderBy + 默认比较器 = CurrentCulture），避免改动可见的树顺序
        return nodes.OrderBy(n => n.Name).ToList();
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

    /// <summary>
    /// 预览导入文件（Excel 与 CSV/TSV 均可）。
    /// 原实现无论扩展名都 <c>File.ReadAllLines</c> + <c>Split</c>，选 .xlsx 读到的是二进制乱码。
    /// </summary>
    public OrganizationCsvPreview PreviewImport(string filePath, int previewCount = 5)
    {
        LogInfo($"预览组织导入: {filePath}");

        TabularData table;
        try
        {
            table = TabularFileReader.Read(filePath);
        }
        catch (Exception ex)
        {
            LogWarn($"读取导入文件失败: {ex.Message}");
            return new OrganizationCsvPreview { TotalRows = 0 };
        }

        if (table.IsEmpty)
            return new OrganizationCsvPreview { TotalRows = 0 };

        var headerMap = BuildHeaderMap(table.Headers);
        if (!headerMap.ContainsKey("名称") && !headerMap.ContainsKey("name"))
        {
            LogWarn("导入文件缺少「名称」列");
            return new OrganizationCsvPreview { TotalRows = 0 };
        }

        var totalRows = table.Rows.Count;
        var take = Math.Min(previewCount, totalRows);
        var previewRows = new List<OrganizationPreviewRow>(take);

        for (var i = 0; i < take; i++)
        {
            var cols = table.Rows[i];
            previewRows.Add(new OrganizationPreviewRow
            {
                RowNumber = i + 2,                  // 文件行号：第 1 行是表头
                Name = GetColumnValue(cols, headerMap, "名称", "name"),
                Abbreviation = GetColumnValue(cols, headerMap, "简称", "abbreviation"),
                Principal = GetColumnValue(cols, headerMap, "负责人", "principal"),
                Phone = GetColumnValue(cols, headerMap, "电话", "phone"),
                Address = GetColumnValue(cols, headerMap, "地址", "address"),
                Region = JoinRegionText(cols, headerMap)
            });
        }

        LogInfo($"导入预览完成: 共{totalRows}行, 预览{take}行");
        return new OrganizationCsvPreview { TotalRows = totalRows, PreviewRows = previewRows };
    }

    /// <summary>
    /// 从 Excel / CSV / TSV 导入组织数据。
    /// 三处与原实现不同（都是实际踩过的坑）：
    ///   ① 统一走 <see cref="TabularFileReader"/>，Excel 不再被当文本读成乱码；
    ///   ② 新增市/县/乡镇/村四列，按名称解析为 ID，**解析不出就整行报错**（不再静默留空）；
    ///   ③ 先全量校验再单事务分批多行 INSERT（原实现逐行 SELECT 查重 + 逐行 INSERT = N+1，且无事务）。
    /// </summary>
    public async Task<ImportResult> ImportFromFileAsync(string filePath, IProgress<string> progress = null, CancellationToken ct = default)
    {
        LogInfo($"开始组织导入: {filePath}");
        var result = new ImportResult { FilePath = filePath };

        try
        {
            var table = TabularFileReader.Read(filePath);
            if (table.IsEmpty)
                return ImportResult.Failed("文件无有效数据");

            var headerMap = BuildHeaderMap(table.Headers);
            if (!headerMap.ContainsKey("名称") && !headerMap.ContainsKey("name"))
                return ImportResult.Failed("文件缺少「名称」列");

            // ── 1) 解析 + 校验 + 区划解析（此阶段不写库） ──
            var resolver = new RegionImportResolver(_regionService, Logger);
            var pending = new List<OrganizationCreateRequest>();
            var errorCount = 0;
            var seenNames = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < table.Rows.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var cols = table.Rows[i];
                var fileLine = i + 2;                 // 第 1 行是表头

                var name = GetColumnValue(cols, headerMap, "名称", "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    errorCount++;
                    result.Errors.Add($"第 {fileLine} 行：名称不能为空");
                    continue;
                }

                if (!seenNames.Add(name))
                {
                    errorCount++;
                    result.Errors.Add($"第 {fileLine} 行：名称「{name}」在文件内重复");
                    continue;
                }

                var request = new OrganizationCreateRequest
                {
                    Name = name,
                    Abbreviation = GetColumnValue(cols, headerMap, "简称", "abbreviation"),
                    Principal = GetColumnValue(cols, headerMap, "负责人", "principal"),
                    Phone = GetColumnValue(cols, headerMap, "电话", "phone"),
                    Address = GetColumnValue(cols, headerMap, "地址", "address"),
                    PostalCode = GetColumnValue(cols, headerMap, "邮编", "postal_code", "postalcode"),
                    InstitutionNature = GetColumnValue(cols, headerMap, "机构性质", "institution_nature"),
                    InstitutionType = GetColumnValue(cols, headerMap, "机构类型", "institution_type"),
                    InstitutionCategory = GetColumnValue(cols, headerMap, "机构类别", "institution_category"),
                    CreatedBy = "Import"
                };

                var regionError = await resolver.ResolveAsync(request, cols, headerMap, ct);
                if (regionError != null)
                {
                    errorCount++;
                    result.Errors.Add($"第 {fileLine} 行：{regionError}");
                    continue;
                }

                pending.Add(request);
                if (fileLine % 200 == 0)
                    progress?.Report($"已解析 {i} 行…");
            }

            // ── 2) 名称查重：一次取全量，替代原实现的逐行 SELECT ──
            if (pending.Count > 0)
            {
                var existingResult = await _db.QueryAsync<string>(
                    "SELECT name FROM nc_sys_organizations", ct);
                if (existingResult.IsFailure)
                    return ImportResult.Failed($"查询已有组织失败，已中止导入：{existingResult.Message}");

                var existing = new HashSet<string>(
                    existingResult.Value ?? new List<string>(), StringComparer.Ordinal);

                var toInsert = new List<OrganizationCreateRequest>(pending.Count);
                foreach (var req in pending)
                {
                    if (existing.Contains(req.Name))
                    {
                        errorCount++;
                        result.Errors.Add($"「{req.Name}」已存在，跳过");
                    }
                    else
                    {
                        toInsert.Add(req);
                    }
                }

                // ── 3) 单事务 + 分批多行 VALUES（§8：批量写入不用逐行 INSERT） ──
                if (toInsert.Count > 0)
                {
                    const int batchSize = 200;
                    await using var tx = await _db.BeginTransactionScopeAsync(ct);

                    for (var offset = 0; offset < toInsert.Count; offset += batchSize)
                    {
                        ct.ThrowIfCancellationRequested();

                        var count = Math.Min(batchSize, toInsert.Count - offset);
                        var batch = toInsert.GetRange(offset, count);
                        var (sql, args) = BuildBulkInsertSql(batch);

                        var insertResult = await _db.ExecuteNonQueryAsync(sql, ct, args);
                        if (insertResult.IsFailure)
                        {
                            // 不 Commit → Dispose 自动回滚，不留下半截机构树
                            LogError($"组织导入写库失败（已回滚）: {insertResult.ErrorCode} {insertResult.Message}");
                            return ImportResult.Failed($"写入数据库失败（已回滚）：{insertResult.Message}");
                        }

                        progress?.Report($"已写入 {offset + count}/{toInsert.Count} 条");
                    }

                    await tx.CommitAsync(ct);
                }

                result.ImportedCount = toInsert.Count;
            }

            result.Success = true;
            result.ErrorCount = errorCount;
            result.Message = $"导入完成：成功 {result.ImportedCount}，失败 {errorCount}";
            Logger.LogBusiness("组织导入完成", ("FilePath", filePath),
                               ("Success", result.ImportedCount), ("Failure", errorCount));
            LogInfo($"组织导入完成: 成功{result.ImportedCount}, 失败{errorCount}");
        }
        catch (OperationCanceledException)
        {
            LogWarn("组织导入被取消");
            return ImportResult.Failed("导入已取消");
        }
        catch (Exception ex)
        {
            LogError($"组织导入失败: {ex.Message}");
            return ImportResult.Failed($"导入失败: {ex.Message}");
        }

        return result;
    }

    #region 导入辅助（表头映射 / 区划列 / 批量 INSERT）

    // 区划列的中英文别名：不同来源的表头写法差异很大
    private static readonly string[] CityKeys =
        { "地级市", "市", "城市", "所属市", "city", "city_name" };
    private static readonly string[] CountyKeys =
        { "县区", "区县", "县/区", "县", "区", "所属县", "county", "county_name", "district" };
    private static readonly string[] TownKeys =
        { "乡镇/街道", "乡镇", "街道", "镇", "乡", "所属乡镇", "town", "town_name" };
    private static readonly string[] VillageKeys =
        { "村/社区", "村社区", "村", "社区", "所属村", "village", "village_name" };

    private static Dictionary<string, int> BuildHeaderMap(string[] headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++)
        {
            var key = headers[i].Trim().ToLowerInvariant();
            if (key.Length == 0) continue;
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

    /// <summary>预览用：把四列区划拼成一行文本（不解析 ID）</summary>
    private static string JoinRegionText(string[] cols, Dictionary<string, int> headerMap)
    {
        var parts = new[]
        {
            GetColumnValue(cols, headerMap, CityKeys),
            GetColumnValue(cols, headerMap, CountyKeys),
            GetColumnValue(cols, headerMap, TownKeys),
            GetColumnValue(cols, headerMap, VillageKeys)
        };
        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    // 行政区名后缀（按长度降序尝试剥离）："哈尔滨市"↔"哈尔滨"、"道里区"↔"道里"
    private static readonly string[] RegionSuffixes =
    {
        "居民委员会", "村民委员会", "街道办事处", "居民社区",
        "居委会", "村委会", "自治县", "自治旗", "街道", "社区", "市区",
        "市", "县", "区", "旗", "镇", "乡", "村"
    };

    private static string TrimRegionSuffix(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var s = name.Trim();
        foreach (var suffix in RegionSuffixes)
        {
            if (s.Length > suffix.Length && s.EndsWith(suffix, StringComparison.Ordinal))
                return s[..^suffix.Length];
        }
        return s;
    }

    /// <summary>
    /// 名称匹配：精确 → 去后缀精确 → 前缀 → 包含。
    /// 表头里的区划名经常省略或写全后缀（"哈尔滨" vs "哈尔滨市"、"道里" vs "道里区"）。
    /// </summary>
    private static T? MatchByName<T>(IEnumerable<T>? items, string input, Func<T, string?> nameOf) where T : class
    {
        var key = input?.Trim();
        if (string.IsNullOrEmpty(key)) return null;

        var list = items as IList<T> ?? items?.ToList();
        if (list == null || list.Count == 0) return null;

        foreach (var it in list)
            if (string.Equals(nameOf(it)?.Trim(), key, StringComparison.Ordinal))
                return it;

        var trimmedKey = TrimRegionSuffix(key);
        if (trimmedKey != key)
        {
            foreach (var it in list)
                if (TrimRegionSuffix(nameOf(it)) == trimmedKey)
                    return it;
        }

        foreach (var it in list)
        {
            var n = TrimRegionSuffix(nameOf(it));
            if (n.Length < 2) continue;
            if (n.StartsWith(key, StringComparison.Ordinal) || key.StartsWith(n, StringComparison.Ordinal))
                return it;
        }

        foreach (var it in list)
        {
            var n = TrimRegionSuffix(nameOf(it));
            if (n.Length >= 2 && key.Contains(n, StringComparison.Ordinal))
                return it;
        }

        return null;
    }

    // created_at/updated_at 走 NOW()，不占参数位
    private const int BulkInsertParamColumns = 17;

    private const string BulkInsertColumns =
        "name, abbreviation, principal, principal_identity_card, phone, address, postal_code, parent_id, " +
        "institution_nature, institution_type, institution_category, is_special_care_institution, " +
        "city_id, county_id, town_id, village_id, province_id, created_at, updated_at";

    /// <summary>
    /// 多行 VALUES 批量 INSERT。
    /// 只拼 <c>$1..$n</c> 占位符（位置参数，不插值任何值，符合 AGENTS §8）。
    /// </summary>
    private static (string Sql, object[] Args) BuildBulkInsertSql(List<OrganizationCreateRequest> batch)
    {
        var args = new object[batch.Count * BulkInsertParamColumns];
        var rows = new string[batch.Count];

        for (var r = 0; r < batch.Count; r++)
        {
            var b = batch[r];
            var o = r * BulkInsertParamColumns;

            args[o + 0] = b.Name;
            args[o + 1] = b.Abbreviation;
            args[o + 2] = b.Principal;
            args[o + 3] = b.PrincipalIdentityCard;
            args[o + 4] = b.Phone;
            args[o + 5] = b.Address;
            args[o + 6] = b.PostalCode;
            args[o + 7] = (object?)b.ParentId ?? DBNull.Value;
            args[o + 8] = b.InstitutionNature;
            args[o + 9] = b.InstitutionType;
            args[o + 10] = b.InstitutionCategory;
            args[o + 11] = b.IsSpecialCareInstitution;
            args[o + 12] = (object?)b.CityId ?? DBNull.Value;
            args[o + 13] = (object?)b.CountyId ?? DBNull.Value;
            args[o + 14] = (object?)b.TownId ?? DBNull.Value;
            args[o + 15] = (object?)b.VillageId ?? DBNull.Value;
            args[o + 16] = (object?)b.ProvinceId ?? DBNull.Value;

            var placeholders = new string[BulkInsertParamColumns];
            for (var c = 0; c < BulkInsertParamColumns; c++)
                placeholders[c] = "$" + (o + c + 1);

            rows[r] = "(" + string.Join(",", placeholders) + ", NOW(), NOW())";
        }

        var sql = "INSERT INTO nc_sys_organizations (" + BulkInsertColumns + ") VALUES "
                + string.Join(",", rows);
        return (sql, args);
    }

    /// <summary>
    /// 单次导入内的区划名称→ID 解析。
    /// 全量乡镇列表惰性取一次（<c>GetAllTownsGroupedByCountyAsync</c> 不带缓存，逐行调会打库）。
    /// </summary>
    private sealed class RegionImportResolver
    {
        private readonly IRegionService _regions;
        private readonly ILoggerService _logger;
        private List<RegionTown>? _allTowns;

        public RegionImportResolver(IRegionService regions, ILoggerService logger)
        {
            _regions = regions;
            _logger = logger;
        }

        /// <summary>返回 null = 成功（文件没有区划列也算成功，兼容旧文件）；返回非 null = 错误信息，整行不入库</summary>
        public async Task<string?> ResolveAsync(
            OrganizationCreateRequest request, string[] cols,
            Dictionary<string, int> headerMap, CancellationToken ct)
        {
            var cityName = GetColumnValue(cols, headerMap, CityKeys);
            var countyName = GetColumnValue(cols, headerMap, CountyKeys);
            var townName = GetColumnValue(cols, headerMap, TownKeys);
            var villageName = GetColumnValue(cols, headerMap, VillageKeys);

            if (string.IsNullOrWhiteSpace(cityName) && string.IsNullOrWhiteSpace(countyName)
                && string.IsNullOrWhiteSpace(townName) && string.IsNullOrWhiteSpace(villageName))
                return null;

            // ── 地级市 ──
            RegionCity? city = null;
            if (!string.IsNullOrWhiteSpace(cityName))
            {
                var cities = await _regions.GetCitiesAsync(ct);
                if (cities.IsFailure) return $"读取地级市列表失败：{cities.Message}";
                city = MatchByName(cities.Value, cityName, c => c.CityName);
                if (city == null) return $"地级市「{cityName}」未匹配到地区数据";
                request.CityId = city.Id;
                request.ProvinceId = city.ProvinceId;
            }

            // ── 县/区 ──
            RegionCounty? county = null;
            if (!string.IsNullOrWhiteSpace(countyName))
            {
                var counties = city != null
                    ? await _regions.GetCountiesByCityIdAsync(city.Id, ct)
                    : await _regions.GetCountiesAsync(ct);
                if (counties.IsFailure) return $"读取县/区列表失败：{counties.Message}";

                county = MatchByName(counties.Value, countyName, c => c.CountyName);
                if (county == null)
                {
                    return city != null
                        ? $"县/区「{countyName}」不在地级市「{cityName}」下，或未匹配到地区数据"
                        : $"县/区「{countyName}」未匹配到地区数据";
                }
                request.CountyId = county.Id;
            }

            // 只填了县没填市：反推市，避免只存半截区划
            if (city == null && county != null && !string.IsNullOrWhiteSpace(county.CityName))
            {
                var c = await _regions.GetCityByNameAsync(county.CityName, ct);
                if (c.IsSuccess && c.Value != null)
                {
                    city = c.Value;
                    request.CityId = city.Id;
                request.ProvinceId = city.ProvinceId;
                }
            }

            // ── 乡镇/街道 ──
            RegionTown? town = null;
            if (!string.IsNullOrWhiteSpace(townName))
            {
                if (county != null)
                {
                    var towns = await _regions.GetTownsByCountyIdAsync(county.Id, ct);
                    if (towns.IsFailure) return $"读取乡镇列表失败：{towns.Message}";
                    town = MatchByName(towns.Value, townName, t => t.TownName);
                    if (town == null)
                        return $"乡镇/街道「{townName}」不在「{countyName}」下，或未匹配到地区数据";
                }
                else
                {
                    town = await FindTownWithoutContextAsync(townName, ct);
                    if (town == null)
                        return $"乡镇/街道「{townName}」未匹配到地区数据（建议同时填写县/区以便精确定位）";
                }
                request.TownId = town.Id;
            }

            if (county == null && town != null)
            {
                var co = await _regions.GetCountyByIdAsync(town.CountyId, ct);
                if (co.IsSuccess && co.Value != null)
                {
                    county = co.Value;
                    request.CountyId = county.Id;
                    if (city == null && !string.IsNullOrWhiteSpace(county.CityName))
                    {
                        var c = await _regions.GetCityByNameAsync(county.CityName, ct);
                        if (c.IsSuccess && c.Value != null)
                        {
                            city = c.Value;
                            request.CityId = city.Id;
                request.ProvinceId = city.ProvinceId;
                        }
                    }
                }
            }

            // ── 村/社区 ──
            if (!string.IsNullOrWhiteSpace(villageName))
            {
                if (town == null)
                {
                    // 村名在各乡镇之间大量重名，缺上级会误匹配到别的乡镇
                    return $"村/社区「{villageName}」无法定位：请先填写乡镇/街道";
                }

                var villages = await _regions.GetVillagesByTownIdAsync(town.Id, ct);
                if (villages.IsFailure) return $"读取村/社区列表失败：{villages.Message}";

                var village = MatchByName(villages.Value, villageName, v => v.VillageName);
                if (village == null)
                    return $"村/社区「{villageName}」不在「{townName}」下，或未匹配到地区数据";
                request.VillageId = village.Id;
            }

            return null;
        }

        private async Task<RegionTown?> FindTownWithoutContextAsync(string name, CancellationToken ct)
        {
            if (_allTowns == null)
            {
                var grouped = await _regions.GetAllTownsGroupedByCountyAsync(ct);
                if (grouped.IsFailure)
                {
                    _logger.Warn($"读取全量乡镇列表失败: {grouped.ErrorCode} {grouped.Message}");
                    return null;
                }
                _allTowns = grouped.Value == null
                    ? new List<RegionTown>()
                    : grouped.Value.SelectMany(kv => kv.Value).ToList();
            }

            return MatchByName(_allTowns, name, t => t.TownName);
        }
    }

    #endregion

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
}
