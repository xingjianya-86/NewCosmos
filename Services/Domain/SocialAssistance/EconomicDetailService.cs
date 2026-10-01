using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Exceptions;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.SocialAssistance;

public class EconomicDetailService : BaseService, IEconomicDetailService
{
    protected override string ServiceName => "EconomicDetailService";
    private readonly IDatabaseService _db;

    public EconomicDetailService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<Result> SaveAllAsync(long applicationId,
        List<LaborIncome> laborIncomes,
        List<BusinessIncome> businessIncomes,
        List<PropertyIncome> propertyIncomes,
        List<TransferIncome> transferIncomes,
        List<OtherIncome> otherIncomes,
        List<Subsidy> subsidies,
        List<BreedingIncome> breedingIncomes,
        List<RigidExpenditure> rigidExpenditures,
        List<FamilyProperty> familyProperties,
        List<Vehicle> vehicles,
        List<Machinery> machineries,
        List<FinancialAsset> financialAssets,
        List<LandRegistration> landRegistrations,
        List<LandConfirmationGroup> landConfirmationGroups,
        CancellationToken ct = default)
    {
        LogInfo($"保存经济明细: ApplicationId={applicationId}");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            // ── 第1步：收集旧数据ID（单条 UNION ALL 一次取回全部旧 id）──
            // 替代原先 15 个 Task.Run 假并行：它们全部挂在同一环境事务连接上，
            // 经 CommandGate 串行排队执行，无并行收益，纯线程池上下文切换开销。
            var oldIdResult = await _db.QueryAsync<OldIdRow>(
                @"SELECT 'nc_biz_labor_incomes' AS src, id FROM nc_biz_labor_incomes WHERE application_id = $1
                  UNION ALL SELECT 'nc_biz_business_incomes', id FROM nc_biz_business_incomes WHERE application_id = $1
                  UNION ALL SELECT 'nc_biz_property_incomes', id FROM nc_biz_property_incomes WHERE application_id = $1
                  UNION ALL SELECT 'nc_biz_transfer_incomes', id FROM nc_biz_transfer_incomes WHERE application_id = $1
                  UNION ALL SELECT 'nc_biz_other_incomes', id FROM nc_biz_other_incomes WHERE application_id = $1
                  UNION ALL SELECT 'nc_biz_subsidies', id FROM nc_biz_subsidies WHERE application_id = $1
                  UNION ALL SELECT 'nc_biz_breeding_incomes', id FROM nc_biz_breeding_incomes WHERE application_id = $1
                  UNION ALL SELECT 'nc_biz_rigid_expenditures', id FROM nc_biz_rigid_expenditures WHERE application_id = $1
                  UNION ALL SELECT 'nc_biz_properties', id FROM nc_biz_properties WHERE application_id = $1
                  UNION ALL SELECT 'nc_biz_vehicles', id FROM nc_biz_vehicles WHERE application_id = $1
                  UNION ALL SELECT 'nc_biz_machineries', id FROM nc_biz_machineries WHERE application_id = $1
                  UNION ALL SELECT 'nc_biz_financial_assets', id FROM nc_biz_financial_assets WHERE application_id = $1
                  UNION ALL SELECT 'nc_biz_land_registrations', id FROM nc_biz_land_registrations WHERE application_id = $1
                  UNION ALL SELECT 'nc_biz_land_confirmation_records', id FROM nc_biz_land_confirmation_records
                     WHERE confirmation_id IN (SELECT id FROM nc_biz_land_confirmations WHERE application_id = $1)
                  UNION ALL SELECT 'nc_biz_land_confirmation_persons', id FROM nc_biz_land_confirmation_persons
                     WHERE confirmation_id IN (SELECT id FROM nc_biz_land_confirmations WHERE application_id = $1)
                  UNION ALL SELECT 'nc_biz_land_confirmations', id FROM nc_biz_land_confirmations WHERE application_id = $1",
                ct, applicationId);
            if (oldIdResult.IsFailure)
            {
                LogError($"收集旧经济明细 ID 失败: {oldIdResult.Message}");
                return Result.Failure(ErrorCodes.DB_CONNECTION_FAILED, oldIdResult.Message ?? "查询失败");
            }
            var oldIdsByTable = (oldIdResult.Value ?? new List<OldIdRow>())
                .GroupBy(r => r.Src ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToArray());

            // ── 第2步：先插入所有新数据 ──
            if (laborIncomes.Count > 0) { await InsertLaborIncomesAsync(applicationId, laborIncomes, ct); LogInfo($"插入务工收入: {laborIncomes.Count} 条"); }
            if (businessIncomes.Count > 0) { await InsertBusinessIncomesAsync(applicationId, businessIncomes, ct); LogInfo($"插入经营收入: {businessIncomes.Count} 条"); }
            if (propertyIncomes.Count > 0) { await InsertPropertyIncomesAsync(applicationId, propertyIncomes, ct); LogInfo($"插入财产收入: {propertyIncomes.Count} 条"); }
            if (transferIncomes.Count > 0) { await InsertTransferIncomesAsync(applicationId, transferIncomes, ct); LogInfo($"插入转移收入: {transferIncomes.Count} 条"); }
            if (otherIncomes.Count > 0) { await InsertOtherIncomesAsync(applicationId, otherIncomes, ct); LogInfo($"插入其他收入: {otherIncomes.Count} 条"); }
            if (subsidies.Count > 0) { await InsertSubsidiesAsync(applicationId, subsidies, ct); LogInfo($"插入补贴: {subsidies.Count} 条"); }
            if (breedingIncomes.Count > 0) { await InsertBreedingIncomesAsync(applicationId, breedingIncomes, ct); LogInfo($"插入养殖业收入: {breedingIncomes.Count} 条"); }
            if (rigidExpenditures.Count > 0) { await InsertRigidExpendituresAsync(applicationId, rigidExpenditures, ct); LogInfo($"插入刚性支出: {rigidExpenditures.Count} 条"); }
            if (familyProperties.Count > 0) { await InsertFamilyPropertiesAsync(applicationId, familyProperties, ct); LogInfo($"插入家庭财产: {familyProperties.Count} 条"); }
            if (vehicles.Count > 0) { await InsertVehiclesAsync(applicationId, vehicles, ct); LogInfo($"插入车辆: {vehicles.Count} 条"); }
            if (machineries.Count > 0) { await InsertMachineriesAsync(applicationId, machineries, ct); LogInfo($"插入农机具: {machineries.Count} 条"); }
            if (financialAssets.Count > 0) { await InsertFinancialAssetsAsync(applicationId, financialAssets, ct); LogInfo($"插入金融资产: {financialAssets.Count} 条"); }
            if (landRegistrations.Count > 0) { await InsertLandRegistrationsAsync(applicationId, landRegistrations, ct); LogInfo($"插入土地登记: {landRegistrations.Count} 条"); }
            if (landConfirmationGroups.Count > 0) { await InsertLandConfirmationGroupsAsync(applicationId, landConfirmationGroups, ct); LogInfo($"插入土地确权: {landConfirmationGroups.Count} 组"); }

            // ── 第3步：新数据写入成功后，删除旧数据（子表先于主表，外键约束）──
            var deleteOrder = new[]
            {
                "nc_biz_labor_incomes", "nc_biz_business_incomes", "nc_biz_property_incomes",
                "nc_biz_transfer_incomes", "nc_biz_other_incomes", "nc_biz_subsidies",
                "nc_biz_breeding_incomes", "nc_biz_rigid_expenditures",
                "nc_biz_properties", "nc_biz_vehicles", "nc_biz_machineries", "nc_biz_financial_assets",
                "nc_biz_land_registrations",
                // 土地确权关联旧数据（先子表后主表）
                "nc_biz_land_confirmation_records", "nc_biz_land_confirmation_persons",
                "nc_biz_land_confirmations"
            };
            foreach (var table in deleteOrder)
            {
                if (!oldIdsByTable.TryGetValue(table, out var ids) || ids.Length == 0)
                    continue;
                await ExecuteNonQueryOrThrowAsync(
                    $"DELETE FROM {table} WHERE id = ANY($1::bigint[])", ct, ids);
            }

            LogInfo("旧数据删除完成");

            await tx.CommitAsync(ct);

            LogInfo("经济明细保存完成");
            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogError($"保存经济明细失败: {ex.Message}");
            return Result.Failure(ErrorCodes.DB_CONNECTION_FAILED, ex.Message);
        }
    }

    public async Task<Result<EconomicDetailData>> LoadAllAsync(long applicationId, CancellationToken ct = default)
    {
        LogInfo($"加载经济明细: ApplicationId={applicationId}, threadId={Environment.CurrentManagedThreadId}");
        var startTime = DateTime.Now;

        // 加载失败必须显式失败，绝不能静默返回空集：
        // 若返回空集，UI 会显示"该户无经济明细"，用户点保存后 SaveAllAsync 会先软删旧数据再写入空集，
        // 真实的收入/财产数据就被静默清空了。
        async Task<List<T>> LoadOrThrowAsync<T>(string sql, string name, params object[] args) where T : class
        {
            using var queryCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, queryCts.Token);
            var result = await _db.QueryAsync<T>(sql, linkedCts.Token, args);
            if (result.IsFailure)
            {
                LogError($"{name} 加载失败: {result.Message}");
                throw new BusinessException(ErrorCodes.DB_CONNECTION_FAILED, $"{name} 加载失败: {result.Message}");
            }
            return result.Value ?? new();
        }

        Task<List<T>> SafeLoadAsync<T>(string sql, string name) where T : class
            => LoadOrThrowAsync<T>(sql, name, applicationId);

        try
        {
            var data = new EconomicDetailData();

            // 13 张独立明细表并行加载（每次查询各自从连接池取连接，互不阻塞），
            // 替代原先 12 次串行 await；任一失败仍会经 Task.WhenAll 抛出并走统一错误处理。
            var subsidiesTask = SafeLoadAsync<Subsidy>("SELECT * FROM nc_biz_subsidies WHERE application_id = $1 AND deleted_at IS NULL", "nc_biz_subsidies");
            var laborIncomesTask = SafeLoadAsync<LaborIncome>("SELECT * FROM nc_biz_labor_incomes WHERE application_id = $1 AND deleted_at IS NULL", "nc_biz_labor_incomes");
            var businessIncomesTask = SafeLoadAsync<BusinessIncome>("SELECT * FROM nc_biz_business_incomes WHERE application_id = $1 AND deleted_at IS NULL", "nc_biz_business_incomes");
            var propertyIncomesTask = SafeLoadAsync<PropertyIncome>("SELECT * FROM nc_biz_property_incomes WHERE application_id = $1 AND deleted_at IS NULL", "nc_biz_property_incomes");
            var transferIncomesTask = SafeLoadAsync<TransferIncome>("SELECT * FROM nc_biz_transfer_incomes WHERE application_id = $1 AND deleted_at IS NULL", "nc_biz_transfer_incomes");
            var otherIncomesTask = SafeLoadAsync<OtherIncome>("SELECT * FROM nc_biz_other_incomes WHERE application_id = $1 AND deleted_at IS NULL", "nc_biz_other_incomes");
            var rigidExpendituresTask = SafeLoadAsync<RigidExpenditure>("SELECT * FROM nc_biz_rigid_expenditures WHERE application_id = $1 AND deleted_at IS NULL", "nc_biz_rigid_expenditures");
            var familyPropertiesTask = SafeLoadAsync<FamilyProperty>("SELECT * FROM nc_biz_properties WHERE application_id = $1 AND deleted_at IS NULL", "nc_biz_properties");
            var vehiclesTask = SafeLoadAsync<Vehicle>("SELECT * FROM nc_biz_vehicles WHERE application_id = $1 AND deleted_at IS NULL", "nc_biz_vehicles");
            var machineriesTask = SafeLoadAsync<Machinery>("SELECT * FROM nc_biz_machineries WHERE application_id = $1 AND deleted_at IS NULL", "nc_biz_machineries");
            var financialAssetsTask = SafeLoadAsync<FinancialAsset>("SELECT * FROM nc_biz_financial_assets WHERE application_id = $1 AND deleted_at IS NULL", "nc_biz_financial_assets");
            var breedingIncomesTask = SafeLoadAsync<BreedingIncome>("SELECT * FROM nc_biz_breeding_incomes WHERE application_id = $1 AND deleted_at IS NULL", "nc_biz_breeding_incomes");
            var landRegistrationsTask = SafeLoadAsync<LandRegistration>("SELECT * FROM nc_biz_land_registrations WHERE application_id = $1 AND deleted_at IS NULL", "nc_biz_land_registrations");

            await Task.WhenAll(
                subsidiesTask, laborIncomesTask, businessIncomesTask, propertyIncomesTask,
                transferIncomesTask, otherIncomesTask, rigidExpendituresTask, familyPropertiesTask,
                vehiclesTask, machineriesTask, financialAssetsTask, breedingIncomesTask, landRegistrationsTask);

            data.Subsidies = await subsidiesTask;
            data.LaborIncomes = await laborIncomesTask;
            data.BusinessIncomes = await businessIncomesTask;
            data.PropertyIncomes = await propertyIncomesTask;
            data.TransferIncomes = await transferIncomesTask;
            data.OtherIncomes = await otherIncomesTask;
            data.RigidExpenditures = await rigidExpendituresTask;
            data.FamilyProperties = await familyPropertiesTask;
            data.Vehicles = await vehiclesTask;
            data.Machineries = await machineriesTask;
            data.FinancialAssets = await financialAssetsTask;
            data.BreedingIncomes = await breedingIncomesTask;
            data.LandRegistrations = await landRegistrationsTask;

            var groups = await SafeLoadAsync<LandConfirmationGroup>("SELECT * FROM nc_biz_land_confirmations WHERE application_id = $1 AND deleted_at IS NULL", "nc_biz_land_confirmations");
            if (groups.Count > 0)
            {
                // 批量取回全部组的 records/persons（替代原先每组 2 次查询的 N+1，且改为参数化）
                var groupIds = groups.Select(g => g.Id).ToArray();
                var records = await LoadOrThrowAsync<LandConfirmationRecord>(
                    "SELECT * FROM nc_biz_land_confirmation_records WHERE confirmation_id = ANY($1::bigint[]) AND deleted_at IS NULL ORDER BY id",
                    "nc_biz_land_confirmation_records", groupIds);
                var persons = await LoadOrThrowAsync<LandConfirmationPerson>(
                    "SELECT * FROM nc_biz_land_confirmation_persons WHERE confirmation_id = ANY($1::bigint[]) AND deleted_at IS NULL ORDER BY id",
                    "nc_biz_land_confirmation_persons", groupIds);

                var groupById = groups.ToDictionary(g => g.Id);
                foreach (var record in records)
                {
                    if (groupById.TryGetValue(record.ConfirmationId, out var group))
                        group.Records.Add(record);
                }
                foreach (var person in persons)
                {
                    if (groupById.TryGetValue(person.ConfirmationId, out var group))
                        group.Persons.Add(person);
                }
            }
            data.LandConfirmationGroups = groups;

            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
            LogInfo($"经济明细加载完成, 耗时: {elapsed:F0}ms");
            return Result.Success(data);
        }
        catch (OperationCanceledException)
        {
            LogError($"加载经济明细被取消或超时");
            return Result.Failure<EconomicDetailData>(ErrorCodes.DB_CONNECTION_FAILED, "查询被取消或超时");
        }
        catch (Exception ex)
        {
            LogError($"加载经济明细失败: {ex.Message}");
            return Result.Failure<EconomicDetailData>(ErrorCodes.DB_CONNECTION_FAILED, ex.Message);
        }
    }

    private async Task DeleteAllAsync(long applicationId, CancellationToken ct)
    {
        // 物理 DELETE 而非软删除：保存流程是"先删全部旧明细、再整批重插"，
        // 软删会让每次保存都新增一代死行（长期约 90% 行是垃圾），表和索引无界膨胀。
        // 变更审计依据在 nc_biz_change_snapshots（变更前后快照），不依赖明细表的软删行，
        // 因此这里直接物理删除；不带 deleted_at 过滤，顺带清掉该申请历史遗留的软删垃圾。
        // 存量垃圾的一次性全库清理见 docs/migrations/20260727_purge_soft_deleted_details.sql。
        // （加载侧保留 deleted_at IS NULL 过滤，无害且兼容清理前的旧数据。）
        var tables = new[] {
            "nc_biz_labor_incomes", "nc_biz_business_incomes", "nc_biz_property_incomes",
            "nc_biz_transfer_incomes", "nc_biz_other_incomes", "nc_biz_subsidies",
            "nc_biz_breeding_incomes", "nc_biz_rigid_expenditures",
            "nc_biz_properties", "nc_biz_vehicles", "nc_biz_machineries", "nc_biz_financial_assets",
            "nc_biz_land_registrations"
        };
        foreach (var table in tables)
        {
            await ExecuteNonQueryOrThrowAsync($"DELETE FROM {table} WHERE application_id = $1", ct, applicationId);
        }

        // 物理删除土地确权关联数据（先子表后主表）
        await ExecuteNonQueryOrThrowAsync("DELETE FROM nc_biz_land_confirmation_records WHERE confirmation_id IN (SELECT id FROM nc_biz_land_confirmations WHERE application_id = $1)", ct, applicationId);
        await ExecuteNonQueryOrThrowAsync("DELETE FROM nc_biz_land_confirmation_persons WHERE confirmation_id IN (SELECT id FROM nc_biz_land_confirmations WHERE application_id = $1)", ct, applicationId);
        await ExecuteNonQueryOrThrowAsync("DELETE FROM nc_biz_land_confirmations WHERE application_id = $1", ct, applicationId);
    }

    /// <summary>
    /// 执行非查询并检查结果，失败时抛出异常以触发事务回滚
    /// </summary>
    private async Task ExecuteNonQueryOrThrowAsync(string sql, CancellationToken ct, params object[] parameters)
    {
        var result = await _db.ExecuteNonQueryAsync(sql, ct, parameters);
        if (result.IsFailure)
            throw new BusinessException(result.ErrorCode ?? "DB_ERROR", result.Message ?? "数据保存失败");
    }

    /// <summary>
    /// 执行标量查询并检查结果，失败时抛出异常以触发事务回滚
    /// </summary>
    private async Task<long> ExecuteScalarOrThrowAsync(string sql, CancellationToken ct, params object[] parameters)
    {
        var result = await _db.ExecuteScalarAsync(sql, ct, parameters);
        if (result.IsFailure)
            throw new BusinessException(result.ErrorCode ?? "DB_ERROR", result.Message ?? "数据保存失败");
        return result.Value;
    }

    /// <summary>
    /// 单条多行 INSERT：把 N 行拼成一条 VALUES ($1..),($k..)... 语句一次执行，
    /// 替代逐行 await INSERT 的 N 次往返（行数通常小于 50，参数量远低于上限）。
    /// 列列表和行数组必须包含 created_at 列及其 DateTime.Now 值；
    /// deleted_at 不应出现在列列表中（走 DB 默认 NULL）。
    /// 运行时自校验：列数 ≠ paramsPerRow 或行数组长度 ≠ paramsPerRow 即抛异常，
    /// 杜绝静默参数错位。
    /// </summary>
    private Task InsertRowsAsync(string insertPrefix, int paramsPerRow,
        IReadOnlyList<object?[]> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return Task.CompletedTask;

        // ── 自校验 1：解析列列表列数 ──
        var firstParen = insertPrefix.LastIndexOf('(');
        var lastParen  = insertPrefix.LastIndexOf(')');
        if (firstParen >= 0 && lastParen > firstParen)
        {
            var colList = insertPrefix.Substring(firstParen + 1, lastParen - firstParen - 1);
            var colCount = colList.Split(',').Length;
            if (colCount != paramsPerRow)
                throw new InvalidOperationException(
                    $"InsertRowsAsync 列数({colCount})≠paramsPerRow({paramsPerRow}), SQL: {insertPrefix[..Math.Min(80, insertPrefix.Length)]}…");
        }

        var valueClauses = new List<string>(rows.Count);
        var args         = new List<object?>(rows.Count * paramsPerRow);
        var paramIndex   = 0;

        foreach (var row in rows)
        {
            // ── 自校验 2：行数组长度 ──
            if (row.Length != paramsPerRow)
                throw new InvalidOperationException(
                    $"InsertRowsAsync 行数组长度({row.Length})≠paramsPerRow({paramsPerRow}), 第{valueClauses.Count + 1}行");

            var placeholders = string.Join(",", Enumerable.Range(paramIndex + 1, paramsPerRow).Select(i => $"${i}"));
            valueClauses.Add($"({placeholders})");
            args.AddRange(row);
            paramIndex += paramsPerRow;
        }

        var sql = insertPrefix + " VALUES " + string.Join(",", valueClauses);
        return ExecuteNonQueryOrThrowAsync(sql, ct, args.ToArray()!);
    }

    private Task InsertLaborIncomesAsync(long applicationId, List<LaborIncome> items, CancellationToken ct)
        => InsertRowsAsync(@"INSERT INTO nc_biz_labor_incomes
            (application_id, member_id, member_name, member_id_card, member_age, income_sub_type,
             work_unit, monthly_income, months_worked, annual_income, remark, created_at)", 12,
            items.Select(item => new object?[] {
                applicationId, item.MemberId, item.MemberName, item.MemberIdCard, item.MemberAge,
                item.IncomeSubType, item.WorkUnit,
                item.MonthlyIncome, item.MonthsWorked,
                item.AnnualIncome, item.Remark, DateTime.Now }).ToList(), ct);

    private Task InsertBusinessIncomesAsync(long applicationId, List<BusinessIncome> items, CancellationToken ct)
        => InsertRowsAsync(@"INSERT INTO nc_biz_business_incomes
            (application_id, member_id, member_name, member_id_card, member_age,
             vendor_type, company_name, monthly_income, created_at)", 9,
            items.Select(item => new object?[] {
                applicationId, item.MemberId, item.MemberName, item.MemberIdCard, item.MemberAge,
                item.VendorType, item.CompanyName,
                item.MonthlyIncome, DateTime.Now }).ToList(), ct);

    private Task InsertPropertyIncomesAsync(long applicationId, List<PropertyIncome> items, CancellationToken ct)
        => InsertRowsAsync(@"INSERT INTO nc_biz_property_incomes
            (application_id, member_id, member_name, member_id_card, member_age,
             income_type, property_description, amount, created_at)", 9,
            items.Select(item => new object?[] {
                applicationId, item.MemberId, item.MemberName, item.MemberIdCard, item.MemberAge,
                item.IncomeType, item.PropertyDescription, item.Amount, DateTime.Now }).ToList(), ct);

    private Task InsertTransferIncomesAsync(long applicationId, List<TransferIncome> items, CancellationToken ct)
        => InsertRowsAsync(@"INSERT INTO nc_biz_transfer_incomes
            (application_id, member_id, member_name, member_id_card, member_age,
             income_type, monthly_amount, months_or_times, total_amount, created_at)", 10,
            items.Select(item => new object?[] {
                applicationId, item.MemberId, item.MemberName, item.MemberIdCard, item.MemberAge,
                item.IncomeType, item.MonthlyAmount, item.MonthsOrTimes, item.TotalAmount, DateTime.Now }).ToList(), ct);

    private Task InsertOtherIncomesAsync(long applicationId, List<OtherIncome> items, CancellationToken ct)
        => InsertRowsAsync(@"INSERT INTO nc_biz_other_incomes
            (application_id, income_type, amount, created_at)", 4,
            items.Select(item => new object?[] {
                applicationId, item.IncomeType, item.Amount, DateTime.Now }).ToList(), ct);

    private Task InsertSubsidiesAsync(long applicationId, List<Subsidy> items, CancellationToken ct)
        => InsertRowsAsync(@"INSERT INTO nc_biz_subsidies
            (application_id, subsidy_type, area, unit_price, count, ratio_factor, original_amount, amount, member_name, member_id_card, created_at)", 11,
            items.Select(item => new object?[] {
                applicationId, item.SubsidyType, item.Area, item.UnitPrice, item.Count,
                item.RatioFactor, item.OriginalAmount, item.Amount, item.MemberName, item.MemberIdCard, DateTime.Now }).ToList(), ct);

    private Task InsertBreedingIncomesAsync(long applicationId, List<BreedingIncome> items, CancellationToken ct)
        => InsertRowsAsync(@"INSERT INTO nc_biz_breeding_incomes
            (application_id, breeding_type, quantity, annual_income, remark, created_at)", 6,
            items.Select(item => new object?[] {
                applicationId, item.BreedingType, item.Quantity, item.AnnualIncome, item.Remark, DateTime.Now }).ToList(), ct);

    private Task InsertRigidExpendituresAsync(long applicationId, List<RigidExpenditure> items, CancellationToken ct)
        => InsertRowsAsync(@"INSERT INTO nc_biz_rigid_expenditures
            (application_id, member_id, person_description, remark, expenditure_type, amount, created_at)", 7,
            items.Select(item => new object?[] {
                applicationId, item.MemberId, item.PersonDescription, item.Remark,
                item.ExpenditureType, item.Amount, DateTime.Now }).ToList(), ct);

    private Task InsertFamilyPropertiesAsync(long applicationId, List<FamilyProperty> items, CancellationToken ct)
        => InsertRowsAsync(@"INSERT INTO nc_biz_properties
            (application_id, member_id, property_type, address, housing_structure, housing_nature, area, room_count, build_year, estimated_value, created_at)", 11,
            items.Select(item => new object?[] {
                applicationId, item.MemberId, item.PropertyType, item.Address, item.HousingStructure,
                item.HousingNature, item.Area, item.RoomCount, item.BuildYear, item.EstimatedValue, DateTime.Now }).ToList(), ct);

    private Task InsertVehiclesAsync(long applicationId, List<Vehicle> items, CancellationToken ct)
        => InsertRowsAsync(@"INSERT INTO nc_biz_vehicles
            (application_id, member_id, vehicle_type, brand, model, license_plate,
             purchase_year, purchase_price, estimated_value, created_at)", 10,
            items.Select(item => new object?[] {
                applicationId, item.MemberId, item.VehicleType, item.Brand, item.Model,
                item.LicensePlate, item.PurchaseYear, item.PurchasePrice, item.EstimatedValue, DateTime.Now }).ToList(), ct);

    private Task InsertMachineriesAsync(long applicationId, List<Machinery> items, CancellationToken ct)
        => InsertRowsAsync(@"INSERT INTO nc_biz_machineries
            (application_id, member_id, machinery_type, brand, model, quantity, horsepower,
             purchase_year, purchase_price, estimated_value, created_at)", 11,
            items.Select(item => new object?[] {
                applicationId, item.MemberId, item.MachineryType, item.Brand, item.Model,
                item.Quantity, item.Horsepower, item.PurchaseYear, item.PurchasePrice,
                item.EstimatedValue, DateTime.Now }).ToList(), ct);

    private Task InsertFinancialAssetsAsync(long applicationId, List<FinancialAsset> items, CancellationToken ct)
        => InsertRowsAsync(@"INSERT INTO nc_biz_financial_assets
            (application_id, has_cash, cash_amount, has_bank_deposit, bank_deposit_amount,
             has_securities, securities_amount, has_commercial_insurance, commercial_insurance_type,
             commercial_insurance_amount, created_at)", 11,
            items.Select(item => new object?[] {
                applicationId, item.HasCash, item.CashAmount, item.HasBankDeposit, item.BankDepositAmount,
                item.HasSecurities, item.SecuritiesAmount, item.HasCommercialInsurance,
                item.CommercialInsuranceType, item.CommercialInsuranceAmount, DateTime.Now }).ToList(), ct);

    private Task InsertLandRegistrationsAsync(long applicationId, List<LandRegistration> items, CancellationToken ct)
        => InsertRowsAsync(@"INSERT INTO nc_biz_land_registrations
            (application_id, owner_name, owner_id_card, land_type, land_usage,
             measurement_type, area, price, subtotal, location, created_at)", 11,
            items.Select(item => new object?[] {
                applicationId, item.OwnerName, item.OwnerIdCard, item.LandType, item.LandUsage,
                item.MeasurementType, item.Area, item.Price, item.Subtotal, item.Location, DateTime.Now }).ToList(), ct);

    private async Task InsertLandConfirmationGroupsAsync(long applicationId, List<LandConfirmationGroup> groups, CancellationToken ct)
    {
        foreach (var group in groups)
        {
            var groupSql = @"INSERT INTO nc_biz_land_confirmations
                (application_id, contractor_name, village_name, total_confirmed_area, total_shares,
                 total_person_count, dry_field_area, wet_field_area, created_at, deleted_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,NOW(),NULL) RETURNING id";
            var groupId = await ExecuteScalarOrThrowAsync(groupSql, ct,
                applicationId, group.ContractorName, group.VillageName,
                (decimal)group.TotalArea, (decimal)group.TotalShares, group.PersonCount,
                (decimal)group.DryFieldArea, (decimal)group.WetFieldArea);

            await InsertRowsAsync(@"INSERT INTO nc_biz_land_confirmation_records
                    (confirmation_id, member_name, member_id_card, land_plot_info, plot_code,
                     contract_area, measured_area, land_area, land_usage, unit_price, is_imported, created_at)", 12,
                group.Records.Select(record => new object?[] {
                    groupId, record.MemberName, record.MemberIdCard,
                    record.LandPlotInfo, record.PlotCode, record.ContractArea, record.MeasuredArea,
                    record.LandArea, record.LandUsage, record.UnitPrice, record.IsImported, DateTime.Now }).ToList(), ct);

            await InsertRowsAsync(@"INSERT INTO nc_biz_land_confirmation_persons
                    (confirmation_id, name, id_card, land_status, land_inherit_to,
                     shares_count, total_land_area, is_imported, created_at)", 9,
                group.Persons.Select(person => new object?[] {
                    groupId, person.Name, person.IdCard,
                    person.LandStatus, person.LandInheritTo, person.SharesCount, person.TotalLandArea,
                    person.IsImported, DateTime.Now }).ToList(), ct);
        }
    }

    /// <summary>
    /// 合并收集旧明细 ID 的行载体：src=来源表名（硬编码白名单），id=待删行主键
    /// </summary>
    private sealed class OldIdRow
    {
        public string? Src { get; set; }
        public long Id { get; set; }
    }
}
