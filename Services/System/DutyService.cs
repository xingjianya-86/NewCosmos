using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;
using NewCosmos.Models.Schema;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Import;
using NewCosmos.Services.Templates;
using NewCosmos.Services.Utilities;
using OfficeOpenXml;

namespace NewCosmos.Services.System;

/// <summary>
/// 值班管理服务：成员分组维护、月度值班表轮转生成（每组×每日期类型独立轮转线）、
/// 手工换人、游标维护、功能设置与打印导出。
/// 轮转规则：
/// - 工作日/休息日 = 领导1+男1+女1；法定节假日 = 领导1+中层1+男1+女1；
/// - 每组在每种日期类型下是独立轮转序列，游标持久化；
/// - 同一值班日内一人只出一个班（按 领导→中层→男→女 优先级选人，冲突时顺延下一位，被跳过者视为已消耗轮次）；
/// - 开关「周六周日同一带班领导」开启时，休息日领导以周末对为轮转单位（周日复用周六人选，不推进游标）。
/// </summary>
public interface IDutyService
{
    /// <summary>确保值班相关表存在（幂等）</summary>
    Task<Result> EnsureTablesExistAsync(CancellationToken ct = default);

    #region 成员管理

    /// <summary>获取全部成员（含分组归属）</summary>
    Task<Result<List<DutyMemberView>>> GetMembersAsync(CancellationToken ct = default);

    /// <summary>新建/编辑成员（含分组归属、入职/离职时间；Id=0 新建）</summary>
    Task<Result> SaveMemberAsync(DutyMemberSave member, CancellationToken ct = default);

    /// <summary>启用/停用成员（停用后不再参与轮转，历史班次快照保留）</summary>
    Task<Result> SetMemberActiveAsync(long memberId, bool isActive, CancellationToken ct = default);

    /// <summary>删除成员（班次 member_name 快照保留，轮转游标置空）</summary>
    Task<Result> DeleteMemberAsync(long memberId, CancellationToken ct = default);

    /// <summary>组内顺序上移/下移（direction: -1=上移，1=下移）</summary>
    Task<Result> MoveMemberAsync(long memberId, string groupCode, int direction, CancellationToken ct = default);

    /// <summary>获取请假记录（memberId 为空时取全部）</summary>
    Task<Result<List<DutyLeaveView>>> GetLeavesAsync(long? memberId, CancellationToken ct = default);

    /// <summary>
    /// 新增请假记录（区间含首尾；生成值班表时区间内顺延跳过并消耗轮次；允许补登记过去日期，须填事由）。
    /// 返回被删除排班数据的已生成月份（yyyy-MM，升序；不自动重新生成）。
    /// </summary>
    Task<Result<List<string>>> SaveLeaveAsync(DutyLeaveSave leave, CancellationToken ct = default);

    /// <summary>删除请假记录；返回被删除排班数据的已生成月份（yyyy-MM，升序）</summary>
    Task<Result<List<string>>> DeleteLeaveAsync(long leaveId, CancellationToken ct = default);

    /// <summary>
    /// 预览请假增删将删除的已生成月份（yyyy-MM，升序）：请假开始月 → 最后已生成月。
    /// 供界面补登记确认提醒用（删除后需人工重新生成）。
    /// </summary>
    Task<Result<List<string>>> GetLeaveAffectedMonthsAsync(DateTime start, CancellationToken ct = default);

    /// <summary>
    /// 补齐生成缺失月份（yyyy-MM 列表，升序逐月、已生成跳过、不 force 覆盖）。
    /// 用于跨月串班/代班"待生效"的一键补齐：生成后重放自动生效。返回实际生成月份数。
    /// </summary>
    Task<Result<int>> GenerateMissingMonthsAsync(IReadOnlyList<string> months, CancellationToken ct = default);

    /// <summary>批量导入成员预览：解析 Excel + 行级校验 + 新增/更新计数（同名=更新）</summary>
    Task<Result<DutyMemberImportPreview>> PreviewMemberImportAsync(string filePath, CancellationToken ct = default);

    /// <summary>批量导入成员（部分成功+报告：错误行跳过，其余事务内导入；同名=更新）</summary>
    Task<Result<DutyMemberImportResult>> ImportMembersAsync(string filePath, CancellationToken ct = default);

    /// <summary>生成批量导入模板 xlsx 到指定目录（Sheet1=数据表头，Sheet2=填写说明）</summary>
    Task<Result<string>> ExportMemberImportTemplateAsync(string outputDir, CancellationToken ct = default);

    #endregion

    #region 生成与查询

    /// <summary>获取月度值班表（未生成月份仅返回日期类型判定）</summary>
    Task<Result<DutyMonthSchedule>> GetMonthScheduleAsync(int year, int month, CancellationToken ct = default);

    /// <summary>按月生成值班表（force=true 时删除该月既有班次重新生成，游标继续消耗）</summary>
    Task<Result<DutyGenerationSummary>> GenerateMonthAsync(int year, int month, bool force, CancellationToken ct = default);

    /// <summary>删除该月全部班次（便于重新生成）</summary>
    Task<Result> ClearMonthAsync(int year, int month, CancellationToken ct = default);

    #endregion

    #region 班务调整（串班/代班）

    /// <summary>获取班务调整记录（changeType 为空取全部；串班 SWAP / 代班 SUBSTITUTE）</summary>
    Task<Result<List<DutyShiftChangeView>>> GetShiftChangesAsync(string? changeType, CancellationToken ct = default);

    /// <summary>新增串班/代班（串班=相互对调两天的班/代班=单向顶班一次；限同组、限今天及以后；班次即时变更并留痕）</summary>
    Task<Result> SaveShiftChangeAsync(DutyShiftChangeSave change, CancellationToken ct = default);

    /// <summary>撤销班务调整（标记已撤销并强制重生成该班次所在月，其余未撤销调整自动重放）</summary>
    Task<Result> CancelShiftChangeAsync(long changeId, CancellationToken ct = default);

    /// <summary>改期：串班修改乙方班日 / 代班修改被代班日（更新记录后重生成新旧日期所在月，重放自动应用新日期）</summary>
    Task<Result> RescheduleShiftChangeAsync(long changeId, DateTime newDate, CancellationToken ct = default);

    /// <summary>获取某成员今天及以后的班次列表（串班/代班选班用）</summary>
    Task<Result<List<DutyFutureSchedule>>> GetMemberFutureSchedulesAsync(long memberId, CancellationToken ct = default);

    #endregion

    #region 轮转游标

    /// <summary>查看全部轮转线游标状态</summary>
    Task<Result<List<DutyRotationStateView>>> GetRotationStatesAsync(CancellationToken ct = default);

    /// <summary>重置指定轮转线（删除游标记录，下次生成从头轮转）</summary>
    Task<Result> ResetRotationAsync(string groupCode, string dateType, CancellationToken ct = default);

    #endregion

    #region 设置

    /// <summary>休息日（周六/周日）带班领导两天同人开关</summary>
    Task<Result<bool>> GetRestdayLeaderSamePairAsync(CancellationToken ct = default);

    /// <summary>保存休息日带班领导开关</summary>
    Task<Result> SaveRestdayLeaderSamePairAsync(bool enabled, CancellationToken ct = default);

    #endregion

    #region 打印导出

    /// <summary>按政务值班表模板渲染指定月（占位符填充；模板缺失时返回失败）</summary>
    Task<Result<byte[]>> RenderMonthByTemplateAsync(int year, int month, CancellationToken ct = default);

    /// <summary>连续导出政务值班表（起止月含边界，支持跨年；每月一个文件，文件已存在则覆盖）</summary>
    Task<Result<List<string>>> ExportGovRangeAsync(int startYear, int startMonth, int endYear, int endMonth, string outputDir, CancellationToken ct = default);

    /// <summary>打印月度值班表（按模板渲染后走 Office COM 打印）</summary>
    Task<Result> PrintMonthAsync(int year, int month, string printerName, int copies, CancellationToken ct = default);

    /// <summary>导出月度值班表 PDF 到指定目录（模板渲染后走 Office COM 转换）</summary>
    Task<Result<string>> ExportMonthPdfAsync(int year, int month, string outputDir, CancellationToken ct = default);

    #endregion
}

public class DutyService : BaseService, IDutyService
{
    protected override string ServiceName => "DutyService";

    private static readonly string[] DutyTables =
    {
        "nc_duty_members",
        "nc_duty_member_groups",
        "nc_duty_leaves",
        "nc_duty_rotation_state",
        "nc_duty_schedules",
        "nc_duty_settings",
        "nc_duty_shift_changes"
    };

    private readonly IDatabaseService _dbService;
    private readonly ISchemaSyncService _schemaSync;
    private readonly ISchemaService _schemaService;
    private readonly IHolidayService _holidayService;
    private readonly ITemplateService _templateService;
    private readonly ITemplateEngineFactory _templateEngineFactory;
    private readonly PerformanceOptions _perfOptions;
    private readonly StorageOptions _storageOptions;
    private readonly ILoggerService _logger;
    private bool _tablesReady;

    public DutyService(
        IDatabaseService dbService,
        ISchemaSyncService schemaSync,
        ISchemaService schemaService,
        IHolidayService holidayService,
        ITemplateService templateService,
        ITemplateEngineFactory templateEngineFactory,
        PerformanceOptions perfOptions,
        StorageOptions storageOptions,
        ILoggerService logger) : base(logger)
    {
        _dbService = dbService;
        _schemaSync = schemaSync;
        _schemaService = schemaService;
        _holidayService = holidayService;
        _templateService = templateService;
        _templateEngineFactory = templateEngineFactory;
        _perfOptions = perfOptions;
        _storageOptions = storageOptions;
        _logger = logger;
    }

    #region Schema 管理

    public async Task<Result> EnsureTablesExistAsync(CancellationToken ct = default)
    {
        if (_tablesReady) return Result.Success();

        LogInfo("执行值班表结构检查");

        var tableList = string.Join("', '", DutyTables);
        var checkSql = $@"
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name IN ('{tableList}')";

        var existingResult = await _dbService.QueryAsync<string>(checkSql, ct);
        if (!existingResult.IsSuccess)
            return existingResult;

        var existingTables = existingResult.Value ?? new List<string>();
        var existingSet = existingTables.ToHashSet();
        var missing = DutyTables.Where(t => !existingSet.Contains(t)).ToList();

        // 逐表创建缺失表（已存在的表跳过重建，绝不破坏性重建）
        foreach (var table in missing)
        {
            LogInfo($"创建缺失表: {table}");
            var syncResult = await _schemaSync.SyncTableSchemaAsync(table, allowDrop: false, null, ct);
            if (syncResult.IsFailure)
            {
                LogError($"同步表结构失败: {table}, {syncResult.Message}");
                return syncResult;
            }
        }

        // 幂等补列：已存在的表按 YAML 权威定义对比实际列，缺失列自动补齐
        //（YAML 加列后应用启动即生效，如 nc_duty_shift_changes.to_duty_date；列名/类型/默认值均来自受控 YAML，非用户输入）
        foreach (var table in existingSet)
        {
            var schema = _schemaService.GetTableSchema(table);
            if (schema is null) continue;

            var colsResult = await _dbService.QueryAsync<string>(
                "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = $1",
                ct, table);
            if (!colsResult.IsSuccess)
                return colsResult;
            var existingCols = (colsResult.Value ?? new List<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var col in schema.Columns.Where(c => !existingCols.Contains(c.Name)))
            {
                var defaultSql = string.IsNullOrWhiteSpace(col.Default) ? string.Empty : $" DEFAULT {col.Default}";
                var alterSql = $"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS {col.Name} {col.Type}{defaultSql}";
                var alterResult = await _dbService.ExecuteNonQueryAsync(alterSql, ct);
                if (alterResult.IsFailure)
                {
                    LogError($"补齐缺失列失败: {table}.{col.Name}, {alterResult.Message}");
                    return alterResult;
                }
                LogInfo($"补齐缺失列: {table}.{col.Name} {col.Type}");
            }
        }

        _tablesReady = true;
        LogInfo($"值班相关表结构就绪（本次新建 {missing.Count} 张）");
        return Result.Success();
    }

    #endregion

    #region 日期类型判定

    /// <summary>
    /// 判定值班日期类型：法定放假日→HOLIDAY；调休补班→WORKDAY；周六日→RESTDAY；其他→WORKDAY
    /// </summary>
    private string ClassifyDate(DateTime date)
    {
        if (_holidayService.IsLegalHoliday(date)) return DutyConstants.DateTypes.HOLIDAY;
        if (_holidayService.IsMakeupWorkday(date)) return DutyConstants.DateTypes.WORKDAY;
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return DutyConstants.DateTypes.RESTDAY;
        return DutyConstants.DateTypes.WORKDAY;
    }

    /// <summary>日期类型对应的出班组</summary>
    private static string[] RequiredGroups(string dateType) =>
        dateType == DutyConstants.DateTypes.HOLIDAY
            ? DutyConstants.Groups.HolidayRequired
            : DutyConstants.Groups.RegularRequired;

    #endregion

    #region 成员管理

    public async Task<Result<List<DutyMemberView>>> GetMembersAsync(CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<List<DutyMemberView>>(ensureResult.ErrorCode!, ensureResult.Message!);

        LogInfo("获取值班成员列表");

        var memberSql = @"
            SELECT id, name, phone, is_active, sort_order, remark, created_at, updated_at
            FROM nc_duty_members
            ORDER BY sort_order, id";
        var memberResult = await _dbService.QueryAsync<DutyMemberView>(memberSql, ct);
        if (!memberResult.IsSuccess)
            return Result.Failure<List<DutyMemberView>>(memberResult.ErrorCode!, memberResult.Message!);

        var groupSql = @"
            SELECT id, member_id, group_code, sort_order
            FROM nc_duty_member_groups";
        var groupResult = await _dbService.QueryAsync<DutyGroupAssignment>(groupSql, ct);
        if (!groupResult.IsSuccess)
            return Result.Failure<List<DutyMemberView>>(groupResult.ErrorCode!, groupResult.Message!);
        var groups = groupResult.Value ?? new List<DutyGroupAssignment>();

        var members = memberResult.Value ?? new List<DutyMemberView>();
        var groupLookup = groups.ToLookup(g => g.MemberId);
        foreach (var member in members)
        {
            member.Groups = groupLookup[member.Id]
                .OrderBy(g => g.SortOrder)
                .ToList();
        }

        return Result.Success(members);
    }

    public async Task<Result> SaveMemberAsync(DutyMemberSave member, CancellationToken ct = default)
    {
        if (member == null || string.IsNullOrWhiteSpace(member.Name))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "成员姓名不能为空");

        var invalidGroup = member.GroupCodes.FirstOrDefault(g => !DutyConstants.Groups.IsValid(g));
        if (invalidGroup != null)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, $"无效的组编码: {invalidGroup}");

        var distinctGroups = member.GroupCodes.Distinct().ToList();
        if (distinctGroups.Count == 0)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "请至少选择一个所属组");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        await using var tx = await _dbService.BeginTransactionScopeAsync(ct);
        try
        {
            long memberId;
            if (member.Id == 0)
            {
                var insertSql = @"
                    INSERT INTO nc_duty_members (name, phone, is_active, join_date, exit_date, sort_order, remark, created_at, updated_at)
                    VALUES ($1, $2, $3, $4, $5,
                            COALESCE((SELECT MAX(sort_order) + 1 FROM nc_duty_members), 0), $6, NOW(), NOW())
                    RETURNING id";
                var insertResult = await _dbService.ExecuteScalarAsync<long>(insertSql, ct,
                    member.Name.Trim(), member.Phone?.Trim() ?? string.Empty, member.IsActive,
                    member.JoinDate, member.ExitDate, member.Remark?.Trim() ?? string.Empty);
                if (insertResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return insertResult;
                }
                memberId = insertResult.Value;
            }
            else
            {
                var existsResult = await _dbService.ExecuteScalarAsync<long>(
                    "SELECT COUNT(*) FROM nc_duty_members WHERE id = $1", ct, member.Id);
                if (!existsResult.IsSuccess || existsResult.Value == 0)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure(ErrorCodes.DUTY_MEMBER_NOT_FOUND, "未找到要编辑的成员");
                }

                var updateResult = await _dbService.ExecuteNonQueryAsync(@"
                    UPDATE nc_duty_members
                    SET name = $2, phone = $3, is_active = $4, join_date = $5, exit_date = $6, remark = $7, updated_at = NOW()
                    WHERE id = $1", ct,
                    member.Id, member.Name.Trim(), member.Phone?.Trim() ?? string.Empty, member.IsActive,
                    member.JoinDate, member.ExitDate, member.Remark?.Trim() ?? string.Empty);
                if (updateResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return updateResult;
                }
                memberId = member.Id;
            }

            // 分组归属：删除移除的组，新增缺失的组（新增组追加到该组轮转队列末尾）
            var existingSql = "SELECT id, group_code FROM nc_duty_member_groups WHERE member_id = $1";
            var existingResult = await _dbService.QueryAsync<DutyGroupAssignment>(existingSql, ct, memberId);
            if (!existingResult.IsSuccess)
            {
                await tx.RollbackAsync(ct);
                return existingResult;
            }
            var existing = existingResult.Value ?? new List<DutyGroupAssignment>();
            var existingCodes = existing.Select(g => g.GroupCode).ToHashSet();

            foreach (var removed in existing.Where(g => !distinctGroups.Contains(g.GroupCode)))
            {
                var deleteResult = await _dbService.ExecuteNonQueryAsync(
                    "DELETE FROM nc_duty_member_groups WHERE id = $1", ct, removed.Id);
                if (deleteResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return deleteResult;
                }
            }

            foreach (var added in distinctGroups.Where(g => !existingCodes.Contains(g)))
            {
                var insertGroupSql = @"
                    INSERT INTO nc_duty_member_groups (member_id, group_code, sort_order)
                    VALUES ($1, $2,
                            COALESCE((SELECT MAX(sort_order) + 1 FROM nc_duty_member_groups WHERE group_code = $2), 0))";
                var insertGroupResult = await _dbService.ExecuteNonQueryAsync(insertGroupSql, ct, memberId, added);
                if (insertGroupResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return insertGroupResult;
                }
            }

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "保存值班成员");
            return Result.FromException(ex);
        }

        Logger.LogBusiness("保存值班成员",
            ("MemberId", member.Id),
            ("Name", DataMasker.MaskName(member.Name.Trim())),
            ("Groups", string.Join(",", distinctGroups)));
        return Result.Success();
    }

    public async Task<Result> SetMemberActiveAsync(long memberId, bool isActive, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        var result = await _dbService.ExecuteNonQueryAsync(@"
            UPDATE nc_duty_members SET is_active = $2, updated_at = NOW() WHERE id = $1", ct, memberId, isActive);
        if (result.IsSuccess)
        {
            Logger.LogBusiness("切换值班成员状态", ("MemberId", memberId), ("IsActive", isActive));
            return Result.Success();
        }
        return result;
    }

    public async Task<Result> DeleteMemberAsync(long memberId, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        // FK 级联：分组记录级联删除；班次 member_id 与轮转游标置空（member_name 快照保留）
        var result = await _dbService.ExecuteNonQueryAsync("DELETE FROM nc_duty_members WHERE id = $1", ct, memberId);
        if (result.IsSuccess)
        {
            Logger.LogBusiness("删除值班成员", ("MemberId", memberId));
            return Result.Success();
        }
        return result;
    }

    public async Task<Result> MoveMemberAsync(long memberId, string groupCode, int direction, CancellationToken ct = default)
    {
        if (!DutyConstants.Groups.IsValid(groupCode))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "无效的组编码");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        // 组内按 sort_order, id 排序后交换相邻两项的 sort_order
        var listSql = @"
            SELECT id, member_id, sort_order
            FROM nc_duty_member_groups
            WHERE group_code = $1
            ORDER BY sort_order, id";
        var listResult = await _dbService.QueryAsync<DutyGroupAssignment>(listSql, ct, groupCode);
        if (!listResult.IsSuccess)
            return listResult;

        var list = listResult.Value ?? new List<DutyGroupAssignment>();
        var index = list.FindIndex(g => g.MemberId == memberId);
        if (index < 0)
            return Result.Failure(ErrorCodes.DUTY_MEMBER_NOT_FOUND, "该成员不属于指定组");

        var target = direction < 0 ? index - 1 : index + 1;
        if (target < 0 || target >= list.Count)
            return Result.Success(); // 已在边界，无需移动

        await using var tx = await _dbService.BeginTransactionScopeAsync(ct);
        try
        {
            var swapFirst = await _dbService.ExecuteNonQueryAsync(
                "UPDATE nc_duty_member_groups SET sort_order = $2 WHERE id = $1", ct, list[target].Id, list[index].SortOrder);
            if (swapFirst.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return swapFirst;
            }
            var swapSecond = await _dbService.ExecuteNonQueryAsync(
                "UPDATE nc_duty_member_groups SET sort_order = $2 WHERE id = $1", ct, list[index].Id, list[target].SortOrder);
            if (swapSecond.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return swapSecond;
            }
            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "调整组内顺序");
            return Result.FromException(ex);
        }

        LogInfo($"组内顺序调整: {DutyConstants.Groups.DisplayName(groupCode)}, MemberId={memberId}, Direction={direction}");
        return Result.Success();
    }

    #endregion

    #region 请假管理

    public async Task<Result<List<DutyLeaveView>>> GetLeavesAsync(long? memberId, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<List<DutyLeaveView>>(ensureResult.ErrorCode!, ensureResult.Message!);

        var sql = memberId.HasValue
            ? @"
                SELECT l.id, l.member_id, m.name AS member_name, l.start_date, l.end_date, l.reason, l.created_at
                FROM nc_duty_leaves l
                JOIN nc_duty_members m ON m.id = l.member_id
                WHERE l.member_id = $1
                ORDER BY l.start_date, l.id"
            : @"
                SELECT l.id, l.member_id, m.name AS member_name, l.start_date, l.end_date, l.reason, l.created_at
                FROM nc_duty_leaves l
                JOIN nc_duty_members m ON m.id = l.member_id
                ORDER BY l.start_date, l.id";

        var result = memberId.HasValue
            ? await _dbService.QueryAsync<DutyLeaveView>(sql, ct, memberId.Value)
            : await _dbService.QueryAsync<DutyLeaveView>(sql, ct);
        return result.IsSuccess && result.Value is not null
            ? Result.Success(result.Value)
            : Result.Success(new List<DutyLeaveView>());
    }

    public async Task<Result<List<string>>> SaveLeaveAsync(DutyLeaveSave leave, CancellationToken ct = default)
    {
        if (leave == null || leave.MemberId <= 0)
            return Result.Failure<List<string>>(ErrorCodes.VALIDATION_FAILED, "请假成员无效");
        if (leave.EndDate.Date < leave.StartDate.Date)
            return Result.Failure<List<string>>(ErrorCodes.DUTY_LEAVE_INVALID_RANGE, "结束日期不能早于开始日期");
        // 补登记过去日期：允许（会级联重排历史月份），但必须填写事由以便留痕
        if (leave.StartDate.Date < DateTime.Today && string.IsNullOrWhiteSpace(leave.Reason))
            return Result.Failure<List<string>>(ErrorCodes.DUTY_LEAVE_BACKFILL_REASON_REQUIRED, "补登记过去日期的请假必须填写事由");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<List<string>>(ensureResult.ErrorCode!, ensureResult.Message!);

        var existsResult = await _dbService.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM nc_duty_members WHERE id = $1", ct, leave.MemberId);
        if (!existsResult.IsSuccess || existsResult.Value == 0)
            return Result.Failure<List<string>>(ErrorCodes.DUTY_MEMBER_NOT_FOUND, "未找到请假成员");

        var sql = @"
            INSERT INTO nc_duty_leaves (member_id, start_date, end_date, reason, created_at)
            VALUES ($1, $2, $3, $4, NOW())";
        var result = await _dbService.ExecuteNonQueryAsync(sql, ct,
            leave.MemberId, leave.StartDate.Date, leave.EndDate.Date, leave.Reason?.Trim() ?? string.Empty);
        if (result.IsFailure)
            return Result.Failure<List<string>>(result.ErrorCode!, result.Message!);

        Logger.LogBusiness("新增值班请假",
            ("MemberId", leave.MemberId),
            ("Range", $"{leave.StartDate:yyyy-MM-dd}~{leave.EndDate:yyyy-MM-dd}"));

        // 请假改变轮转消耗（跨月接续以上月最后消耗人为准），受影响月份旧数据不再有效：
        // 删除（不自动重新生成，由值班表页人工生成或界面选择"立即重新生成"）
        return await DeleteAffectedGeneratedMonthsAsync(leave.StartDate.Date, ct);
    }

    public async Task<Result<List<string>>> DeleteLeaveAsync(long leaveId, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<List<string>>(ensureResult.ErrorCode!, ensureResult.Message!);

        var leaveResult = await _dbService.QuerySingleAsync<DutyLeave>(
            "SELECT id, member_id, start_date, end_date FROM nc_duty_leaves WHERE id = $1", ct, leaveId);
        if (!leaveResult.IsSuccess)
            return Result.Failure<List<string>>(leaveResult.ErrorCode!, leaveResult.Message!);
        if (leaveResult.Value is null)
            return Result.Failure<List<string>>(ErrorCodes.DUTY_CHANGE_NOT_FOUND, "未找到请假记录");

        var result = await _dbService.ExecuteNonQueryAsync("DELETE FROM nc_duty_leaves WHERE id = $1", ct, leaveId);
        if (result.IsFailure)
            return Result.Failure<List<string>>(result.ErrorCode!, result.Message!);

        Logger.LogBusiness("删除值班请假记录", ("LeaveId", leaveId));

        // 撤销请假同样改变轮转消耗 → 删除受影响月份已生成数据（不自动重新生成）
        return await DeleteAffectedGeneratedMonthsAsync(leaveResult.Value.StartDate.Date, ct);
    }

    /// <summary>
    /// 删除请假增删影响范围内的已生成月份班次：从请假开始月到"最后已生成月"，逐月整月删除，**不自动重新生成**。
    /// 生成算法以"上月最后消耗人"跨月接续（GetLastScheduleMemberAsync），请假改变轮转消耗后
    /// 受影响月份旧数据不再有效；删除后由用户在值班表页按需重新生成。
    /// </summary>
    private async Task<Result<List<string>>> DeleteAffectedGeneratedMonthsAsync(DateTime leaveStart, CancellationToken ct)
    {
        var monthsResult = await GetAffectedGeneratedMonthsAsync(leaveStart, ct);
        if (monthsResult.IsFailure)
            return Result.Failure<List<string>>(monthsResult.ErrorCode!, monthsResult.Message!);

        var deleted = new List<string>();
        foreach (var (y, m) in monthsResult.Value!)
        {
            var delResult = await _dbService.ExecuteNonQueryAsync(
                "DELETE FROM nc_duty_schedules WHERE year = $1 AND month = $2", ct, y, m);
            if (delResult.IsFailure)
                return Result.Failure<List<string>>(delResult.ErrorCode!, $"删除 {y}-{m:D2} 值班表数据失败：{delResult.Message}");
            deleted.Add($"{y}-{m:D2}");
            LogInfo($"请假联动：已删除 {y}-{m:D2} 值班表数据（等待人工重新生成）");
        }
        return Result.Success(deleted);
    }

    /// <summary>
    /// 计算请假增删影响的已生成月份：请假开始月 → 最后已生成月，仅返回实际已生成班次的月份（升序）。
    /// </summary>
    private async Task<Result<List<(int Year, int Month)>>> GetAffectedGeneratedMonthsAsync(
        DateTime leaveStart, CancellationToken ct)
    {
        var maxResult = await _dbService.ExecuteScalarAsync<long?>(
            "SELECT MAX(year * 100 + month) FROM nc_duty_schedules", ct);
        if (!maxResult.IsSuccess)
            return Result.Failure<List<(int, int)>>(maxResult.ErrorCode!, maxResult.Message!);
        if (maxResult.Value is not > 0)
            return Result.Success(new List<(int, int)>());

        var lastYear = (int)(maxResult.Value.Value / 100);
        var lastMonth = (int)(maxResult.Value.Value % 100);

        var months = new List<(int, int)>();
        var from = new DateTime(leaveStart.Year, leaveStart.Month, 1);
        var to = new DateTime(lastYear, lastMonth, 1);
        for (var m = from; m <= to; m = m.AddMonths(1))
        {
            var countResult = await _dbService.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM nc_duty_schedules WHERE year = $1 AND month = $2", ct, m.Year, m.Month);
            if (countResult.IsFailure)
                return Result.Failure<List<(int, int)>>(countResult.ErrorCode!, countResult.Message!);
            if (countResult.Value > 0)
                months.Add((m.Year, m.Month));
        }
        return Result.Success(months);
    }

    public async Task<Result<List<string>>> GetLeaveAffectedMonthsAsync(
        DateTime start, CancellationToken ct = default)
    {
        var monthsResult = await GetAffectedGeneratedMonthsAsync(start, ct);
        if (monthsResult.IsFailure)
            return Result.Failure<List<string>>(monthsResult.ErrorCode!, monthsResult.Message!);

        return Result.Success(monthsResult.Value!
            .Select(x => $"{x.Year}-{x.Month:D2}")
            .ToList());
    }

    public async Task<Result<int>> GenerateMissingMonthsAsync(
        IReadOnlyList<string> months, CancellationToken ct = default)
    {
        if (months is null || months.Count == 0)
            return Result.Success(0);

        // 解析并去重（yyyy-MM）
        var parsed = new List<(int Year, int Month)>();
        foreach (var raw in months)
        {
            var parts = (raw ?? string.Empty).Split('-');
            if (parts.Length != 2
                || !int.TryParse(parts[0], out var y) || y < 2000 || y > 2100
                || !int.TryParse(parts[1], out var m) || m < 1 || m > 12)
            {
                return Result.Failure<int>(ErrorCodes.VALIDATION_FAILED, $"月份格式无效：{raw}");
            }
            if (!parsed.Contains((y, m)))
                parsed.Add((y, m));
        }
        parsed.Sort();

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<int>(ensureResult.ErrorCode!, ensureResult.Message!);

        var from = new DateTime(parsed[0].Year, parsed[0].Month, 1);
        var to = new DateTime(parsed[^1].Year, parsed[^1].Month, 1);
        var generatedCount = 0;
        for (var m = from; m <= to; m = m.AddMonths(1))
        {
            var countResult = await _dbService.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM nc_duty_schedules WHERE year = $1 AND month = $2", ct, m.Year, m.Month);
            if (countResult.IsFailure)
                return Result.Failure<int>(countResult.ErrorCode!, countResult.Message!);
            if (countResult.Value > 0)
                continue;   // 已生成月跳过

            var genResult = await GenerateMonthAsync(m.Year, m.Month, force: false, ct);
            if (genResult.IsFailure)
                return Result.Failure<int>(genResult.ErrorCode!,
                    $"补齐生成 {m:yyyy-MM} 失败（已生成 {generatedCount} 个月）：{genResult.Message}");

            generatedCount++;
            LogInfo($"跨月串班/代班补齐：{m:yyyy-MM} 值班表已生成");
        }
        return Result.Success(generatedCount);
    }

    /// <summary>
    /// 加载全部请假区间（生成值班表用，内存判定）：memberId → 区间列表（含首尾）
    /// </summary>
    private async Task<Result<List<(long MemberId, DateTime Start, DateTime End)>>> LoadLeaveRangesAsync(CancellationToken ct)
    {
        var sql = "SELECT member_id, start_date, end_date FROM nc_duty_leaves";
        var result = await _dbService.QueryAsync<DutyLeave>(sql, ct);
        if (!result.IsSuccess)
            return Result.Failure<List<(long, DateTime, DateTime)>>(result.ErrorCode!, result.Message!);

        var ranges = (result.Value ?? new List<DutyLeave>())
            .Select(l => (l.MemberId, l.StartDate.Date, l.EndDate.Date))
            .ToList();
        return Result.Success(ranges);
    }

    private static bool IsOnLeave(
        List<(long MemberId, DateTime Start, DateTime End)> leaveRanges, long memberId, DateTime date) =>
        leaveRanges.Any(r => r.MemberId == memberId && r.Start <= date && r.End >= date);

    #endregion

    #region 班务调整（串班/代班）

    public async Task<Result<List<DutyShiftChangeView>>> GetShiftChangesAsync(string? changeType, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<List<DutyShiftChangeView>>(ensureResult.ErrorCode!, ensureResult.Message!);

        var sql = @"
            SELECT sc.id, sc.change_type, sc.from_member_id, sc.from_member_name, sc.duty_date,
                   sc.to_member_id, sc.to_member_name, sc.to_duty_date, sc.group_code, sc.reason,
                   sc.is_cancelled, sc.cancelled_at, sc.created_at,
                   COALESCE(fm.phone, '') AS from_member_phone,
                   COALESCE(tm.phone, '') AS to_member_phone
            FROM nc_duty_shift_changes sc
            LEFT JOIN nc_duty_members fm ON fm.id = sc.from_member_id
            LEFT JOIN nc_duty_members tm ON tm.id = sc.to_member_id
            WHERE ($1::character varying IS NULL OR sc.change_type = $1::character varying)
            ORDER BY sc.created_at DESC, sc.id DESC";
        var result = changeType is null
            ? await _dbService.QueryAsync<DutyShiftChangeView>(sql, ct, DBNull.Value)
            : await _dbService.QueryAsync<DutyShiftChangeView>(sql, ct, changeType);
        if (!result.IsSuccess || result.Value is null)
            return Result.Success(new List<DutyShiftChangeView>());

        await ApplyReplayStatusAsync(result.Value, ct);
        return Result.Success(result.Value);
    }

    /// <summary>
    /// 批量计算班务调整的重放状态（列表展示）：一次 ANY 批查班次后逐条判定，
    /// 已生效/待生效（缺对方月份或待重建）/已失效（轮转已变化）/已撤销。
    /// </summary>
    private async Task ApplyReplayStatusAsync(List<DutyShiftChangeView> changes, CancellationToken ct)
    {
        foreach (var c in changes.Where(x => x.IsCancelled))
        {
            c.ReplayStatus = DutyConstants.ChangeReplayStatuses.Cancelled;
            c.StatusText = "已撤销";
            c.StatusColorHex = DutyConstants.ChangeReplayStatuses.ColorHex(c.ReplayStatus);
        }

        var active = changes.Where(x => !x.IsCancelled).ToList();
        if (active.Count == 0) return;

        var dates = active
            .SelectMany(c => new[] { c.DutyDate.Date, c.ToDutyDate?.Date ?? default })
            .Where(d => d != default)
            .Distinct()
            .ToArray();

        var rowResult = await _dbService.QueryAsync<DutySchedule>(@"
            SELECT duty_date, group_code, member_id, rotation_member_id
            FROM nc_duty_schedules
            WHERE duty_date = ANY($1)", ct, dates);
        if (!rowResult.IsSuccess)
        {
            LogWarn($"班务调整状态计算失败（班次批查）：{rowResult.Message}");
            foreach (var c in active)
            {
                c.ReplayStatus = DutyConstants.ChangeReplayStatuses.PendingRebuild;
                c.StatusText = "待生效（状态待刷新）";
                c.StatusColorHex = DutyConstants.ChangeReplayStatuses.ColorHex(c.ReplayStatus);
            }
            return;
        }

        var rows = (rowResult.Value ?? new List<DutySchedule>())
            .GroupBy(s => (s.DutyDate.Date, s.GroupCode))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var c in active)
        {
            var missing = new List<string>();
            if (!rows.TryGetValue((c.DutyDate.Date, c.GroupCode), out var fromRow))
                missing.Add($"{c.DutyDate:yyyy-MM}");
            DutySchedule? toRow = null;
            if (c.ChangeType == DutyConstants.ChangeTypes.SWAP && c.ToDutyDate.HasValue
                && !rows.TryGetValue((c.ToDutyDate.Value.Date, c.GroupCode), out toRow))
            {
                missing.Add($"{c.ToDutyDate.Value:yyyy-MM}");
            }

            if (missing.Count > 0)
            {
                c.ReplayStatus = DutyConstants.ChangeReplayStatuses.PendingUnbuilt;
                c.StatusText = $"待生效（缺 {string.Join("、", missing.Distinct().OrderBy(x => x))}）";
                c.StatusColorHex = DutyConstants.ChangeReplayStatuses.ColorHex(c.ReplayStatus);
                continue;
            }

            bool applied;
            bool rotationMatched;
            if (c.ChangeType == DutyConstants.ChangeTypes.SWAP)
            {
                applied = c.FromMemberId.HasValue && c.ToMemberId.HasValue
                          && fromRow!.MemberId == c.ToMemberId && toRow!.MemberId == c.FromMemberId;
                var fromRot = fromRow!.RotationMemberId ?? fromRow.MemberId;
                var toRot = toRow!.RotationMemberId ?? toRow.MemberId;
                rotationMatched = fromRot == c.FromMemberId && toRot == c.ToMemberId;
            }
            else
            {
                applied = c.ToMemberId.HasValue && fromRow!.MemberId == c.ToMemberId;
                var fromRot = fromRow!.RotationMemberId ?? fromRow.MemberId;
                rotationMatched = fromRot == c.FromMemberId;
            }

            if (applied)
            {
                c.ReplayStatus = DutyConstants.ChangeReplayStatuses.Applied;
                c.StatusText = "已生效";
            }
            else if (rotationMatched)
            {
                c.ReplayStatus = DutyConstants.ChangeReplayStatuses.PendingRebuild;
                c.StatusText = "待生效（重新生成后生效）";
            }
            else
            {
                c.ReplayStatus = DutyConstants.ChangeReplayStatuses.Invalid;
                c.StatusText = "已失效（轮转已变化，需改期或撤销）";
            }
            c.StatusColorHex = DutyConstants.ChangeReplayStatuses.ColorHex(c.ReplayStatus);
        }
    }

    public async Task<Result> SaveShiftChangeAsync(DutyShiftChangeSave change, CancellationToken ct = default)
    {
        if (change is null || !DutyConstants.ChangeTypes.IsValid(change.ChangeType))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "班务调整类型无效");
        if (change.FromMemberId <= 0 || change.ToMemberId <= 0)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "班务调整人员无效");
        if (change.FromMemberId == change.ToMemberId)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_MEMBER_INVALID, "顶班人员不能与原班人员相同");
        if (change.DutyDate.Date < DateTime.Today)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_INVALID_DATE, "仅支持调整今天及以后的班次");

        var isSwap = change.ChangeType == DutyConstants.ChangeTypes.SWAP;
        var toDutyDate = change.ToDutyDate?.Date;
        if (isSwap)
        {
            if (toDutyDate is null)
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "串班需选择对方的班次日期");
            if (toDutyDate.Value < DateTime.Today)
                return Result.Failure(ErrorCodes.DUTY_SHIFT_INVALID_DATE, "仅支持调整今天及以后的班次");
            if (toDutyDate.Value == change.DutyDate.Date)
                return Result.Failure(ErrorCodes.DUTY_SHIFT_INVALID_DATE, "串班的两个班次不能是同一天");
        }

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        // 甲方的班次必须存在且属于甲方
        var fromScheduleResult = await _dbService.QuerySingleAsync<DutySchedule>(@"
            SELECT id, duty_date, date_type, group_code, member_id, member_name, year, month
            FROM nc_duty_schedules
            WHERE duty_date = $1 AND member_id = $2
            LIMIT 1", ct, change.DutyDate.Date, change.FromMemberId);
        if (!fromScheduleResult.IsSuccess)
            return Result.Failure(fromScheduleResult.ErrorCode!, fromScheduleResult.Message!);
        if (fromScheduleResult.Value is null)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_NOT_FOUND, "该日期没有此人的值班班次");
        var fromSchedule = fromScheduleResult.Value;

        // 占用校验：同一班次同一时间只允许一条生效调整（防重复创建互相覆盖；改期走 Reschedule）
        var occupiedFrom = await CountActiveChangesOnDateAsync(fromSchedule.DutyDate, fromSchedule.GroupCode, null, ct);
        if (!occupiedFrom.IsSuccess)
            return Result.Failure(occupiedFrom.ErrorCode!, occupiedFrom.Message!);
        if (occupiedFrom.Value > 0)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_ALREADY_ADJUSTED, "甲方该日期的班次已有一条生效的调整记录，请先撤销或改期");

        DutySchedule? toSchedule = null;
        if (isSwap)
        {
            // 乙方的班次必须存在且属于乙方（被换出的班日）
            var toScheduleResult = await _dbService.QuerySingleAsync<DutySchedule>(@"
                SELECT id, duty_date, date_type, group_code, member_id, member_name, year, month
                FROM nc_duty_schedules
                WHERE duty_date = $1 AND member_id = $2
                LIMIT 1", ct, toDutyDate!.Value, change.ToMemberId);
            if (!toScheduleResult.IsSuccess)
                return Result.Failure(toScheduleResult.ErrorCode!, toScheduleResult.Message!);
            if (toScheduleResult.Value is null)
                return Result.Failure(ErrorCodes.DUTY_SHIFT_NOT_FOUND, "对方在该日期没有值班班次，无法对调");
            toSchedule = toScheduleResult.Value;

            // 占用校验：乙方班次同样不可被其他生效调整占用
            var occupiedTo = await CountActiveChangesOnDateAsync(toSchedule.DutyDate, toSchedule.GroupCode, null, ct);
            if (!occupiedTo.IsSuccess)
                return Result.Failure(occupiedTo.ErrorCode!, occupiedTo.Message!);
            if (occupiedTo.Value > 0)
                return Result.Failure(ErrorCodes.DUTY_SHIFT_ALREADY_ADJUSTED, "乙方该日期的班次已有一条生效的调整记录，请先撤销或改期");

            // 串班限同组：两个班次必须同组
            if (toSchedule.GroupCode != fromSchedule.GroupCode)
                return Result.Failure(ErrorCodes.DUTY_SHIFT_MEMBER_INVALID, "串班的两个班次必须属于同一值班组");
        }

        // 乙方人员：存在 + 启用 + 当日可用（入职/离职边界）
        var memberResult = await _dbService.QuerySingleAsync<DutyMemberView>(
            "SELECT id, name, is_active, join_date, exit_date FROM nc_duty_members WHERE id = $1",
            ct, change.ToMemberId);
        if (!memberResult.IsSuccess)
            return Result.Failure(memberResult.ErrorCode!, memberResult.Message!);
        if (memberResult.Value is null || !memberResult.Value.IsActive)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_MEMBER_INVALID, "对方人员不存在或已停用");
        var toMember = memberResult.Value;
        if (toMember.JoinDate.HasValue && toMember.JoinDate.Value > change.DutyDate.Date)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_MEMBER_INVALID, "对方人员在调整日期尚未入职");
        if (toMember.ExitDate.HasValue && toMember.ExitDate.Value < change.DutyDate.Date)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_MEMBER_INVALID, "对方人员在调整日期已离职");

        // 同组校验：乙方必须与甲方班次同组
        var inGroupResult = await _dbService.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM nc_duty_member_groups WHERE member_id = $1 AND group_code = $2",
            ct, change.ToMemberId, fromSchedule.GroupCode);
        if (!inGroupResult.IsSuccess || inGroupResult.Value == 0)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_MEMBER_INVALID, "对方人员与原班人员不在同一值班组，串班/代班限同组");

        // 请假校验：调整后实际顶班者不得处于请假期间
        // （串班=乙方上甲方班日 X、甲方上乙方班日 Y；代班=乙方上 X 日）
        var leaveResult = await LoadLeaveRangesAsync(ct);
        if (leaveResult.IsFailure)
            return Result.Failure(leaveResult.ErrorCode!, leaveResult.Message!);
        var leaveRanges = leaveResult.Value;

        if (IsOnLeave(leaveRanges, change.ToMemberId, change.DutyDate.Date))
            return Result.Failure(ErrorCodes.DUTY_SHIFT_MEMBER_INVALID,
                $"{toMember.Name} 在 {change.DutyDate:yyyy-MM-dd} 处于请假期间，无法{(isSwap ? "串班" : "代班")}");
        if (isSwap && IsOnLeave(leaveRanges, change.FromMemberId, toDutyDate!.Value))
            return Result.Failure(ErrorCodes.DUTY_SHIFT_MEMBER_INVALID,
                $"{fromSchedule.MemberName} 在 {toDutyDate:yyyy-MM-dd} 处于请假期间，无法串班");

        // 互占校验（串班）：对调后双方各值一天一班
        if (isSwap)
        {
            var busyResult = await _dbService.ExecuteScalarAsync<long>(@"
                SELECT COUNT(*) FROM nc_duty_schedules
                WHERE duty_date = $1 AND member_id = $2 AND id <> $3",
                ct, change.DutyDate.Date, change.ToMemberId, fromSchedule.Id);
            if (!busyResult.IsSuccess)
                return Result.Failure(busyResult.ErrorCode!, busyResult.Message!);
            if (busyResult.Value > 0)
                return Result.Failure(ErrorCodes.DUTY_MEMBER_CONFLICT, "对方在该日期已有其他班次，无法对调");

            var busyBackResult = await _dbService.ExecuteScalarAsync<long>(@"
                SELECT COUNT(*) FROM nc_duty_schedules
                WHERE duty_date = $1 AND member_id = $2 AND id <> $3",
                ct, toDutyDate!.Value, change.FromMemberId, toSchedule!.Id);
            if (!busyBackResult.IsSuccess)
                return Result.Failure(busyBackResult.ErrorCode!, busyBackResult.Message!);
            if (busyBackResult.Value > 0)
                return Result.Failure(ErrorCodes.DUTY_MEMBER_CONFLICT, "转让人在对方班次日期已有其他班次，无法对调");
        }

        var typeName = DutyConstants.ChangeTypes.DisplayName(change.ChangeType);
        var reason = change.Reason?.Trim() ?? string.Empty;
        var fromName = fromSchedule.MemberName;

        await using var tx = await _dbService.BeginTransactionScopeAsync(ct);
        try
        {
            // 留痕（from/to 姓名快照，成员删除后历史仍可显示）
            var insertResult = await _dbService.ExecuteNonQueryAsync(@"
                INSERT INTO nc_duty_shift_changes
                    (change_type, from_member_id, from_member_name, duty_date, to_member_id, to_member_name, to_duty_date, group_code, reason, is_cancelled, created_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, FALSE, NOW())",
                ct, change.ChangeType, change.FromMemberId, fromName, change.DutyDate.Date,
                change.ToMemberId, toMember.Name, isSwap ? toDutyDate : null, fromSchedule.GroupCode, reason);
            if (insertResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return insertResult;
            }

            if (isSwap)
            {
                // 相互对调：甲方班日改乙方上，乙方班日改甲方上（两班均留痕）
                var updateA = await _dbService.ExecuteNonQueryAsync(@"
                    UPDATE nc_duty_schedules
                    SET member_id = $1, member_name = $2, is_adjusted = TRUE, remark = $3
                    WHERE id = $4", ct,
                    change.ToMemberId, toMember.Name,
                    $"串班：{fromName}({change.DutyDate:MM-dd}) ↔ {toMember.Name}({toDutyDate:MM-dd}){(string.IsNullOrEmpty(reason) ? string.Empty : $"（{reason}）")}",
                    fromSchedule.Id);
                if (updateA.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return updateA;
                }

                var updateB = await _dbService.ExecuteNonQueryAsync(@"
                    UPDATE nc_duty_schedules
                    SET member_id = $1, member_name = $2, is_adjusted = TRUE, remark = $3
                    WHERE id = $4", ct,
                    change.FromMemberId, fromName,
                    $"串班：{fromName}({change.DutyDate:MM-dd}) ↔ {toMember.Name}({toDutyDate:MM-dd}){(string.IsNullOrEmpty(reason) ? string.Empty : $"（{reason}）")}",
                    toSchedule!.Id);
                if (updateB.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return updateB;
                }
            }
            else
            {
                // 代班：代班人顶上一次（单班次变更）
                var updateResult = await _dbService.ExecuteNonQueryAsync(@"
                    UPDATE nc_duty_schedules
                    SET member_id = $1, member_name = $2, is_adjusted = TRUE, remark = $3
                    WHERE id = $4", ct,
                    change.ToMemberId, toMember.Name,
                    $"代班：{toMember.Name} 代 {fromName}{(string.IsNullOrEmpty(reason) ? string.Empty : $"（{reason}）")}",
                    fromSchedule.Id);
                if (updateResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return updateResult;
                }
            }

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            return Result.FromException(ex);
        }

        Logger.LogBusiness($"值班{typeName}",
            ("Date", isSwap ? $"{change.DutyDate:yyyy-MM-dd}↔{toDutyDate:yyyy-MM-dd}" : change.DutyDate.ToString("yyyy-MM-dd")),
            ("Group", fromSchedule.GroupCode),
            ("From", DataMasker.MaskName(fromName)),
            ("To", DataMasker.MaskName(toMember.Name)),
            ("Reason", reason));
        return Result.Success();
    }

    public async Task<Result> CancelShiftChangeAsync(long changeId, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        var changeResult = await _dbService.QuerySingleAsync<DutyShiftChange>(
            "SELECT id, change_type, from_member_id, from_member_name, duty_date, to_member_id, to_member_name, to_duty_date, group_code, is_cancelled FROM nc_duty_shift_changes WHERE id = $1",
            ct, changeId);
        if (!changeResult.IsSuccess)
            return Result.Failure(changeResult.ErrorCode!, changeResult.Message!);
        if (changeResult.Value is null)
            return Result.Failure(ErrorCodes.DUTY_CHANGE_NOT_FOUND, "未找到班务调整记录");
        if (changeResult.Value.IsCancelled)
            return Result.Failure(ErrorCodes.DUTY_CHANGE_NOT_FOUND, "该记录已撤销，不能重复撤销");

        var cancelResult = await _dbService.ExecuteNonQueryAsync(@"
            UPDATE nc_duty_shift_changes
            SET is_cancelled = TRUE, cancelled_at = NOW()
            WHERE id = $1", ct, changeId);
        if (cancelResult.IsFailure) return cancelResult;

        Logger.LogBusiness("撤销班务调整",
            ("ChangeId", changeId),
            ("Type", DutyConstants.ChangeTypes.DisplayName(changeResult.Value.ChangeType)),
            ("From", DataMasker.MaskName(changeResult.Value.FromMemberName)),
            ("To", DataMasker.MaskName(changeResult.Value.ToMemberName)),
            ("Date", changeResult.Value.DutyDate.ToString("yyyy-MM-dd")));

        // 强制重生成受影响月份（跨月对调：甲方班日月 ∪ 乙方班日月）：
        // 被撤记录不再重放，其余未撤销调整自动重放，班次自然恢复
        var rebuildDates = new List<DateTime> { changeResult.Value.DutyDate };
        if (changeResult.Value.ToDutyDate.HasValue)
            rebuildDates.Add(changeResult.Value.ToDutyDate.Value);
        var rebuildResult = await RebuildMonthsOnDatesAsync(rebuildDates, ct);
        return rebuildResult.IsFailure ? rebuildResult : Result.Success();
    }

    public async Task<Result> RescheduleShiftChangeAsync(long changeId, DateTime newDate, CancellationToken ct = default)
    {
        if (newDate.Date < DateTime.Today)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_INVALID_DATE, "仅支持改期到今天及以后");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        var changeResult = await _dbService.QuerySingleAsync<DutyShiftChange>(
            "SELECT id, change_type, from_member_id, from_member_name, duty_date, to_member_id, to_member_name, to_duty_date, group_code, is_cancelled FROM nc_duty_shift_changes WHERE id = $1",
            ct, changeId);
        if (!changeResult.IsSuccess)
            return Result.Failure(changeResult.ErrorCode!, changeResult.Message!);
        var change = changeResult.Value;
        if (change is null || change.IsCancelled)
            return Result.Failure(ErrorCodes.DUTY_CHANGE_NOT_FOUND, "未找到班务调整记录，或记录已撤销");

        var isSwap = change.ChangeType == DutyConstants.ChangeTypes.SWAP;
        // 改期目标端：串班=乙方的班日（乙方换一天出班）；代班=被代班日（甲方换一天被代）
        var releaserId = (isSwap ? change.ToMemberId : change.FromMemberId)!.Value;   // 换出班次的人
        var receiverId = (isSwap ? change.FromMemberId : change.ToMemberId)!.Value;   // 顶上的人
        var receiverName = isSwap ? change.FromMemberName : change.ToMemberName;
        var oldDate = (isSwap ? change.ToDutyDate : (DateTime?)change.DutyDate)!.Value;

        if (newDate.Date == oldDate)
            return Result.Success();   // 日期未变化

        // 对端班日不可与改期目标重合（串班：不能把乙方的班换到甲方的班日上）
        if (isSwap && newDate.Date == change.DutyDate.Date)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_INVALID_DATE, "改期后的班日不能与甲方的班日为同一天");

        // 新日期必须存在换出人的班次（该班次将被顶班人顶上）
        var newScheduleResult = await _dbService.QuerySingleAsync<DutySchedule>(@"
            SELECT id, duty_date, date_type, group_code, member_id, member_name, year, month
            FROM nc_duty_schedules
            WHERE duty_date = $1 AND member_id = $2
            LIMIT 1", ct, newDate.Date, releaserId);
        if (!newScheduleResult.IsSuccess)
            return Result.Failure(newScheduleResult.ErrorCode!, newScheduleResult.Message!);
        if (newScheduleResult.Value is null)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_NOT_FOUND, "对方在新日期没有值班班次，无法改期");
        var newSchedule = newScheduleResult.Value;
        if (newSchedule.GroupCode != change.GroupCode)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_MEMBER_INVALID, "新日期的班次与原调整不属于同一值班组");

        // 占用校验：新日期班次未被其他生效调整占用（排除本记录自身）
        var occupied = await CountActiveChangesOnDateAsync(newSchedule.DutyDate, newSchedule.GroupCode, changeId, ct);
        if (!occupied.IsSuccess)
            return Result.Failure(occupied.ErrorCode!, occupied.Message!);
        if (occupied.Value > 0)
            return Result.Failure(ErrorCodes.DUTY_SHIFT_ALREADY_ADJUSTED, "新日期的班次已有一条生效的调整记录，请先撤销或改期");

        // 串班互占校验：改期后顶班人（甲方）在新日期不能已有其他班次（对调后一人一天一班）
        if (isSwap)
        {
            var otherDate = change.DutyDate.Date;
            var busyResult = await _dbService.ExecuteScalarAsync<long>(@"
                SELECT COUNT(*) FROM nc_duty_schedules
                WHERE duty_date = $1 AND member_id = $2 AND id <> $3",
                ct, newDate.Date, receiverId, newSchedule.Id);
            if (!busyResult.IsSuccess)
                return Result.Failure(busyResult.ErrorCode!, busyResult.Message!);
            if (busyResult.Value > 0 && otherDate != newDate.Date)
                return Result.Failure(ErrorCodes.DUTY_MEMBER_CONFLICT, "顶班人在新日期已有其他班次，无法改期");
        }

        // 更新记录日期（SWAP=to_duty_date；SUB=duty_date）
        var updateSql = isSwap
            ? "UPDATE nc_duty_shift_changes SET to_duty_date = $1 WHERE id = $2"
            : "UPDATE nc_duty_shift_changes SET duty_date = $1 WHERE id = $2";
        var updateResult = await _dbService.ExecuteNonQueryAsync(updateSql, ct, newDate.Date, changeId);
        if (updateResult.IsFailure) return updateResult;

        Logger.LogBusiness("班务调整改期",
            ("ChangeId", changeId),
            ("Type", DutyConstants.ChangeTypes.DisplayName(change.ChangeType)),
            ("OldDate", oldDate.ToString("yyyy-MM-dd")),
            ("NewDate", newDate.Date.ToString("yyyy-MM-dd")),
            ("From", DataMasker.MaskName(change.FromMemberName)),
            ("To", DataMasker.MaskName(change.ToMemberName)));

        // 重生成新旧日期所在月：新日期按记录重放应用（顶班人顶上），旧日期恢复生成值
        var rebuildResult = await RebuildMonthsOnDatesAsync(
            new[] { oldDate, newDate, change.DutyDate, change.ToDutyDate ?? change.DutyDate }, ct);
        return rebuildResult.IsFailure ? rebuildResult : Result.Success();
    }

    public async Task<Result<List<DutyFutureSchedule>>> GetMemberFutureSchedulesAsync(long memberId, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<List<DutyFutureSchedule>>(ensureResult.ErrorCode!, ensureResult.Message!);

        var result = await _dbService.QueryAsync<DutyFutureSchedule>(@"
            SELECT id AS schedule_id, duty_date, date_type, group_code, member_id, member_name, is_adjusted
            FROM nc_duty_schedules
            WHERE member_id = $1 AND duty_date >= $2
            ORDER BY duty_date", ct, memberId, DateTime.Today);
        return result.IsSuccess && result.Value is not null
            ? Result.Success(result.Value)
            : Result.Success(new List<DutyFutureSchedule>());
    }

    /// <summary>统计某日期班次上生效的班务调整记录数（同组同日唯一班次；excludeChangeId 用于改期排除自身）</summary>
    private async Task<Result<long>> CountActiveChangesOnDateAsync(DateTime dutyDate, string groupCode, long? excludeChangeId, CancellationToken ct)
    {
        var result = await _dbService.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM nc_duty_shift_changes
            WHERE is_cancelled = FALSE AND group_code = $1
              AND (duty_date = $2 OR to_duty_date = $2)
              AND ($3 IS NULL OR id <> $3)",
            ct, groupCode, dutyDate.Date, excludeChangeId);
        return result.IsSuccess ? Result.Success(result.Value) : Result.Failure<long>(result.ErrorCode!, result.Message!);
    }

    /// <summary>
    /// 强制重生成给定日期覆盖的、已生成过班次的月份（日期去重后按月执行；未生成月不触碰）。
    /// 用于班务调整的撤销/改期联动：重生成后按最新记录重放，班次自动恢复或应用新日期。
    /// </summary>
    private async Task<Result> RebuildMonthsOnDatesAsync(IEnumerable<DateTime> dates, CancellationToken ct)
    {
        var monthSet = new SortedSet<(int Year, int Month)>();
        foreach (var d in dates)
        {
            if (d == default) continue;
            monthSet.Add((d.Year, d.Month));
        }

        foreach (var (y, m) in monthSet)
        {
            var countResult = await _dbService.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM nc_duty_schedules WHERE year = $1 AND month = $2", ct, y, m);
            if (!countResult.IsSuccess)
                return Result.Failure(countResult.ErrorCode!, countResult.Message!);
            if (countResult.Value == 0) continue;   // 未生成月不触碰

            var genResult = await GenerateMonthAsync(y, m, force: true, ct);
            if (genResult.IsFailure)
                return Result.Failure(genResult.ErrorCode!, $"重生成 {y}-{m:D2} 值班表失败：{genResult.Message}");
            LogInfo($"班务调整联动：{y}-{m:D2} 值班表已强制重生成");
        }
        return Result.Success();
    }

    /// <summary>班务调整重放结果：生效/待生效/跳过统计 + 待生效缺月份（不落库）</summary>
    private sealed class ShiftReplayOutcome
    {
        public int AppliedCount { get; set; }
        public int PendingCount { get; set; }
        public int SkippedCount { get; set; }
        public List<string> MissingMonths { get; } = new();

        public void AddMissing(DateTime date)
        {
            var text = $"{date:yyyy-MM}";
            if (!MissingMonths.Contains(text))
                MissingMonths.Add(text);
        }
    }

    /// <summary>
    /// 重放指定月份的未撤销串班/代班记录到已生成班次（生成事务内调用）。
    /// 链式合理性：串班两班成对预检（两班"轮转消耗人"必须分别为甲/乙方）都匹配才成对替换；
    /// 代班单班校验（轮转消耗人=被代者）才替换；不匹配则整条跳过并记日志。
    /// 返回统计：生效条数 / 待生效条数（缺对方月份）/ 跳过条数（轮转变化或命中请假）与缺失月份。
    /// </summary>
    private async Task<ShiftReplayOutcome> ApplyShiftChangesAsync(int year, int month, CancellationToken ct)
    {
        var outcome = new ShiftReplayOutcome();
        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1);

        var changesResult = await _dbService.QueryAsync<DutyShiftChange>(@"
            SELECT id, change_type, from_member_id, from_member_name, duty_date,
                   to_member_id, to_member_name, to_duty_date, group_code, reason
            FROM nc_duty_shift_changes
            WHERE is_cancelled = FALSE
              AND ((duty_date >= $1 AND duty_date < $2) OR (to_duty_date IS NOT NULL AND to_duty_date >= $1 AND to_duty_date < $2))
            ORDER BY duty_date, id", ct, monthStart, monthEnd);
        if (!changesResult.IsSuccess)
            return outcome;   // 查询失败不阻断生成（班次保持生成结果），记录告警
        var changes = changesResult.Value ?? new List<DutyShiftChange>();
        if (changes.Count == 0) return outcome;

        // 请假区间（重放校验：顶班人处于请假期间则跳过该条，不把请假人员排回班）
        var leaveResult = await LoadLeaveRangesAsync(ct);
        var leaveRanges = leaveResult.IsSuccess
            ? leaveResult.Value
            : new List<(long MemberId, DateTime Start, DateTime End)>();
        if (!leaveResult.IsSuccess)
            LogWarn($"班务调整重放：请假区间加载失败，本次重放不校验请假（{leaveResult.Message}）");

        foreach (var c in changes)
        {
            var typeName = DutyConstants.ChangeTypes.DisplayName(c.ChangeType);
            var remark = $"{typeName}：{(c.ChangeType == DutyConstants.ChangeTypes.SWAP
                ? $"{c.FromMemberName}({c.DutyDate:MM-dd}) ↔ {c.ToMemberName}({c.ToDutyDate:MM-dd})"
                : $"{c.ToMemberName} 代 {c.FromMemberName}")}{(string.IsNullOrEmpty(c.Reason) ? string.Empty : $"（{c.Reason}）")}";

            if (c.ChangeType == DutyConstants.ChangeTypes.SWAP)
            {
                // 串班成对预检：两班的"轮转消耗人"（rotation_member_id，生成时写入、调班不覆盖）
                // 必须分别为甲/乙方——用消耗事实判断，不受本记录或其他调整已改的显示值影响
                var pairResult = await _dbService.QueryAsync<DutySchedule>(@"
                    SELECT id, duty_date, member_id, rotation_member_id FROM nc_duty_schedules
                    WHERE (duty_date = $1 AND group_code = $3)
                       OR (duty_date = $2 AND group_code = $3)",
                    ct, c.DutyDate, c.ToDutyDate!.Value, c.GroupCode);
                var pair = pairResult.Value ?? new List<DutySchedule>();
                var fromSchedule = pair.FirstOrDefault(s => s.DutyDate == c.DutyDate.Date);
                var toSchedule = pair.FirstOrDefault(s => s.DutyDate == c.ToDutyDate.Value);

                // 待生效：任一侧月份未生成（记录缺失月份，供"补齐生成"）
                if (fromSchedule is null || toSchedule is null)
                {
                    if (fromSchedule is null) outcome.AddMissing(c.DutyDate);
                    if (toSchedule is null) outcome.AddMissing(c.ToDutyDate.Value);
                    outcome.PendingCount++;
                    LogWarn($"班务调整重放待生效（对方月份未生成）：changeId={c.Id} {c.DutyDate:yyyy-MM-dd}↔{c.ToDutyDate:yyyy-MM-dd} {typeName}");
                    continue;
                }

                var fromRot = fromSchedule.RotationMemberId ?? fromSchedule.MemberId;
                var toRot = toSchedule.RotationMemberId ?? toSchedule.MemberId;
                if (fromRot != c.FromMemberId || toRot != c.ToMemberId)
                {
                    outcome.SkippedCount++;
                    LogWarn($"班务调整重放跳过（对调双方班次状态已变化）：changeId={c.Id} {c.DutyDate:yyyy-MM-dd}↔{c.ToDutyDate:yyyy-MM-dd} {typeName}");
                    continue;
                }

                // 请假校验：对调后实际顶班者（乙方上 X 日、甲方上 Y 日）处于请假期间则不重放
                if ((c.ToMemberId.HasValue && IsOnLeave(leaveRanges, c.ToMemberId.Value, c.DutyDate.Date))
                    || (c.FromMemberId.HasValue && c.ToDutyDate.HasValue
                        && IsOnLeave(leaveRanges, c.FromMemberId.Value, c.ToDutyDate.Value)))
                {
                    outcome.SkippedCount++;
                    LogWarn($"班务调整重放跳过（命中请假区间）：changeId={c.Id} {c.DutyDate:yyyy-MM-dd}↔{c.ToDutyDate:yyyy-MM-dd} {typeName}");
                    continue;
                }

                var updateA = await _dbService.ExecuteNonQueryAsync(@"
                    UPDATE nc_duty_schedules
                    SET member_id = $1, member_name = $2, is_adjusted = TRUE, remark = $3
                    WHERE id = $4", ct, c.ToMemberId, c.ToMemberName, remark, fromSchedule.Id);
                var updateB = await _dbService.ExecuteNonQueryAsync(@"
                    UPDATE nc_duty_schedules
                    SET member_id = $1, member_name = $2, is_adjusted = TRUE, remark = $3
                    WHERE id = $4", ct, c.FromMemberId, c.FromMemberName, remark, toSchedule.Id);
                if (updateA.IsFailure || updateB.IsFailure)
                {
                    LogWarn($"班务调整重放失败：changeId={c.Id} {c.DutyDate:yyyy-MM-dd}↔{c.ToDutyDate:yyyy-MM-dd} {typeName}");
                }
                else
                {
                    outcome.AppliedCount++;
                }
            }
            else
            {
                // 代班：单班校验（轮转消耗人=被代者，用 rotation 列判断——不受其他显示调整影响）才替换
                if (c.ToMemberId.HasValue && IsOnLeave(leaveRanges, c.ToMemberId.Value, c.DutyDate.Date))
                {
                    outcome.SkippedCount++;
                    LogWarn($"班务调整重放跳过（代班人处于请假期间）：changeId={c.Id} {c.DutyDate:yyyy-MM-dd} " +
                            $"{DataMasker.MaskName(c.FromMemberName)}→{DataMasker.MaskName(c.ToMemberName)}");
                    continue;
                }

                var updateResult = await _dbService.ExecuteNonQueryAsync(@"
                    UPDATE nc_duty_schedules
                    SET member_id = $1, member_name = $2, is_adjusted = TRUE, remark = $3
                    WHERE duty_date = $4 AND group_code = $5 AND COALESCE(rotation_member_id, member_id) = $6",
                    ct, c.ToMemberId, c.ToMemberName, remark, c.DutyDate, c.GroupCode, c.FromMemberId);
                if (updateResult.IsFailure)
                {
                    LogWarn($"班务调整重放失败：changeId={c.Id} {c.DutyDate:yyyy-MM-dd} {typeName}，{updateResult.Message}");
                }
                else if (updateResult.Value == 0)
                {
                    outcome.SkippedCount++;
                    LogWarn($"班务调整重放跳过（班次不存在或被代者已变化）：changeId={c.Id} {c.DutyDate:yyyy-MM-dd} " +
                                   $"{DataMasker.MaskName(c.FromMemberName)}→{DataMasker.MaskName(c.ToMemberName)}");
                }
                else
                {
                    outcome.AppliedCount++;
                }
            }
        }

        return outcome;
    }

    #endregion

    #region 批量导入成员（队列名单模式：每组一列，从上到下即轮转顺序）

    /// <summary>组列头 → 组编码（列头即 DutyConstants.Groups.DisplayName）</summary>
    private static readonly Dictionary<string, string> MemberGroupColumns = new()
    {
        [DutyConstants.Groups.DisplayName(DutyConstants.Groups.LEADER)] = DutyConstants.Groups.LEADER,
        [DutyConstants.Groups.DisplayName(DutyConstants.Groups.MIDDLE)] = DutyConstants.Groups.MIDDLE,
        [DutyConstants.Groups.DisplayName(DutyConstants.Groups.MALE)] = DutyConstants.Groups.MALE,
        [DutyConstants.Groups.DisplayName(DutyConstants.Groups.FEMALE)] = DutyConstants.Groups.FEMALE,
    };

    /// <summary>
    /// 解析名单文件（预览/执行共用）：Sheet1 第 1 行为「序号 + 组名」交替配对的 8 列布局
    /// （序号|领导组、序号|中层管理组、序号|男生组、序号|女生组）。
    /// 序号 = 该成员在该组的轮转顺序（sort_order，按值升序；允许跳号，组内不得重复、必填正整数）。
    /// 行级空档（序号与姓名均空）跳过继续；无法识别的列/配对缺失/序号或姓名缺失/组内序号重复/超长计入错误。
    /// 返回 (各组名单, 错误列表)；执行导入时错误列表非空即整体拒绝（名单必须无歧义）。
    /// </summary>
    private Result<(List<DutyGroupRoster> Rosters, List<string> Errors)> ParseMemberRosterSheetAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return Result.Failure<(List<DutyGroupRoster>, List<string>)>(ErrorCodes.FILE_NOT_FOUND, "导入文件不存在");

        using var reader = SheetReaderFactory.Create(filePath);
        if (reader.IsEmpty)
            return Result.Failure<(List<DutyGroupRoster>, List<string>)>(ErrorCodes.FILE_FORMAT_ERROR, "导入文件内容为空");

        var rosters = new List<DutyGroupRoster>();
        var errors = new List<string>();
        var recognizedColumns = 0;

        // 列头配对：识别「序号」列 + 紧随的组名列
        var pairings = new List<(int SortCol, int NameCol, string GroupCode, string GroupName)>();
        int? pendingSortCol = null;
        for (var col = 1; col <= reader.ColumnCount; col++)
        {
            var header = reader.GetCellText(1, col).Trim();
            if (string.IsNullOrWhiteSpace(header)) continue;

            if (header == "序号")
            {
                if (pendingSortCol.HasValue)
                {
                    errors.Add("模板布局错误：连续出现两个「序号」列（缺少配对的组名列）");
                }
                pendingSortCol = col;
                continue;
            }

            if (MemberGroupColumns.TryGetValue(header, out var groupCode))
            {
                if (!pendingSortCol.HasValue)
                {
                    errors.Add($"模板布局错误：组列「{header}」缺少配对的「序号」列，请使用「下载导入模板」生成的标准模板");
                }
                else
                {
                    pairings.Add((pendingSortCol.Value, col, groupCode, header));
                    pendingSortCol = null;
                    recognizedColumns++;
                }
                continue;
            }

            errors.Add($"无法识别的列「{header}」（模板仅支持「序号 + 组名」配对布局，请使用标准模板）");
        }

        if (recognizedColumns == 0)
        {
            return Result.Failure<(List<DutyGroupRoster>, List<string>)>(
                ErrorCodes.VALIDATION_FAILED, "未识别到任何组名单列（表头须为：序号+领导组 / 序号+中层管理组 / 序号+男生组 / 序号+女生组）");
        }

        foreach (var (sortCol, nameCol, groupCode, groupName) in pairings)
        {
            var roster = new DutyGroupRoster { GroupCode = groupCode };
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            var seenSeqs = new HashSet<int>();

            for (var row = 2; row <= reader.RowCount; row++)
            {
                var seqText = reader.GetCellText(row, sortCol).Trim();
                var name = reader.GetCellText(row, nameCol).Trim();

                if (string.IsNullOrWhiteSpace(seqText) && string.IsNullOrWhiteSpace(name))
                    continue;   // 行级空档：跳过继续

                if (string.IsNullOrWhiteSpace(name))
                {
                    errors.Add($"「{groupName}」第 {row} 行：姓名为空（序号「{seqText}」无对应姓名）");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(seqText))
                {
                    errors.Add($"「{groupName}」第 {row} 行：序号为空（姓名「{name}」缺少序号）");
                    continue;
                }
                if (!int.TryParse(seqText, out var seq) || seq <= 0)
                {
                    errors.Add($"「{groupName}」第 {row} 行：序号无效「{seqText}」（须为正整数）");
                    continue;
                }
                if (name.Length > 50)
                {
                    errors.Add($"「{groupName}」第 {row} 行姓名超长（{name.Length} 字，上限 50）");
                    continue;
                }

                if (!seenNames.Add(name))
                {
                    errors.Add($"「{groupName}」第 {row} 行姓名「{name}」在该列内重复");
                    continue;
                }
                if (!seenSeqs.Add(seq))
                {
                    errors.Add($"「{groupName}」第 {row} 行序号 {seq} 在该列内重复（姓名「{name}」）");
                    continue;
                }

                roster.Entries.Add(new DutyRosterEntry { Seq = seq, Name = name });
            }

            rosters.Add(roster);
        }

        return Result.Success((rosters, errors));
    }

    /// <summary>
    /// 解析「轮转设置」工作表（Sheet2，可缺省）：起始日期（全局）+ 每组**三条轮转线**分别的当前序列。
    /// 布局：「起始日期」行（B 列=日期）；表头行（组名 | 工作日序列 | 休息日序列 | 法定节假日序列）；数据行（组名 + 各线姓名）。
    /// 每条线留空 = 该线从名单头开始；当前序列成员须在该组名单内（校验在调用方）。
    /// </summary>
    private Result<(DateTime? AnchorDate, Dictionary<(string Group, string DateType), string> CurrentSeqByLine)> ParseRotationSettingsAsync(string filePath)
    {
        try
        {
            ExcelPackage.License.SetNonCommercialOrganization("民政社会救助管理系统");
            using var package = new ExcelPackage(new FileInfo(filePath));
            if (package.Workbook.Worksheets.Count < 2)
                return Result.Success<(DateTime? AnchorDate, Dictionary<(string Group, string DateType), string> CurrentSeqByLine)>(
                    (null, new Dictionary<(string, string), string>()));   // 无设置页：纯名单导入

            var sheet = package.Workbook.Worksheets[1];
            var currentSeqByLine = new Dictionary<(string Group, string DateType), string>();
            DateTime? anchorDate = null;

            // 表头行定位：A 列 =「组名」的行，B/C/D 列 → 轮转线
            var lineColumns = new Dictionary<int, string>();   // 列号 → 日期类型
            for (var row = 1; row <= sheet.Dimension?.End.Row; row++)
            {
                var key = sheet.Cells[row, 1].Text?.Trim() ?? string.Empty;

                if (key == "起始日期")
                {
                    var value = sheet.Cells[row, 2].Text?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(value) && !DateTime.TryParse(value, out _))
                        return Result.Failure<(DateTime?, Dictionary<(string, string), string>)>(
                            ErrorCodes.VALIDATION_FAILED, $"轮转设置：起始日期无效「{value}」（格式：yyyy-MM-dd）");
                    if (!string.IsNullOrWhiteSpace(value))
                        anchorDate = DateTime.Parse(value).Date;
                    continue;
                }

                if (key == "组名")
                {
                    lineColumns.Clear();
                    for (var col = 2; col <= sheet.Dimension.End.Column; col++)
                    {
                        var header = sheet.Cells[row, col].Text?.Trim() ?? string.Empty;
                        var dateType = DateTypeColumnHeaders.FirstOrDefault(kv => kv.Value == header).Key;
                        if (dateType != null)
                            lineColumns[col] = dateType;
                    }
                    continue;
                }

                if (MemberGroupColumns.TryGetValue(key, out var groupCode))
                {
                    foreach (var (col, dateType) in lineColumns)
                    {
                        var value = sheet.Cells[row, col].Text?.Trim() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(value))
                            currentSeqByLine[(groupCode, dateType)] = value;
                    }
                }
                // 其他行（说明等）忽略
            }

            return Result.Success((anchorDate, currentSeqByLine));
        }
        catch (Exception ex)
        {
            return Result.Failure<(DateTime?, Dictionary<(string, string), string>)>(
                ErrorCodes.FILE_FORMAT_ERROR, $"轮转设置解析失败：{ex.Message}");
        }
    }

    /// <summary>轮转设置页的线列头 → 日期类型（列头文本 → DateTypes 常量）</summary>
    private static readonly Dictionary<string, string> DateTypeColumnHeaders = new()
    {
        [DutyConstants.DateTypes.DisplayName(DutyConstants.DateTypes.WORKDAY)] = DutyConstants.DateTypes.WORKDAY,
        [DutyConstants.DateTypes.DisplayName(DutyConstants.DateTypes.RESTDAY)] = DutyConstants.DateTypes.RESTDAY,
        [DutyConstants.DateTypes.DisplayName(DutyConstants.DateTypes.HOLIDAY)] = DutyConstants.DateTypes.HOLIDAY,
    };

    public async Task<Result<DutyMemberImportPreview>> PreviewMemberImportAsync(string filePath, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<DutyMemberImportPreview>(ensureResult.ErrorCode!, ensureResult.Message!);

        var parseResult = ParseMemberRosterSheetAsync(filePath);
        if (!parseResult.IsSuccess)
            return Result.Failure<DutyMemberImportPreview>(parseResult.ErrorCode!, parseResult.Message!);

        var (rosters, parseErrors) = parseResult.Value;

        // 与库内现状做差异汇总（新增/对齐/移出）
        var membersResult = await GetMembersAsync(ct);
        if (!membersResult.IsSuccess)
            return Result.Failure<DutyMemberImportPreview>(membersResult.ErrorCode!, membersResult.Message!);
        var existingMembers = membersResult.Value ?? new List<DutyMemberView>();
        var existingByName = existingMembers.ToDictionary(m => m.Name, StringComparer.Ordinal);

        var preview = new DutyMemberImportPreview();
        var excelMembers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var roster in rosters)
        {
            preview.Rosters.Add(roster);
            foreach (var entry in roster.Entries)
            {
                excelMembers.Add(entry.Name);
                if (!existingByName.ContainsKey(entry.Name)) preview.NewCount++;
                else preview.UpdateCount++;
            }

            if (roster.Entries.Count == 0)
            {
                preview.ClearedGroupNames.Add(roster.GroupName);
            }
        }

        // 库内在组但名单未列的 (成员, 组) 计数
        foreach (var member in existingMembers)
        {
            foreach (var g in member.Groups)
            {
                var roster = rosters.FirstOrDefault(r => r.GroupCode == g.GroupCode);
                if (roster == null || !roster.Entries.Any(e => e.Name == member.Name))
                {
                    preview.RemoveCount++;
                }
            }
        }

        preview.TotalMembers = excelMembers.Count;
        preview.Errors.AddRange(parseErrors);

        // 轮转设置（起始日期 + 各线当前序列）
        var settingsResult = ParseRotationSettingsAsync(filePath);
        if (!settingsResult.IsSuccess)
            return Result.Failure<DutyMemberImportPreview>(settingsResult.ErrorCode!, settingsResult.Message!);
        var (anchorDate, currentSeqByLine) = settingsResult.Value;
        preview.AnchorDate = anchorDate;
        foreach (var (key, memberName) in currentSeqByLine)
        {
            if (!preview.CurrentSeqByLine.TryGetValue(key.Group, out var lines))
            {
                lines = new Dictionary<string, string>();
                preview.CurrentSeqByLine[key.Group] = lines;
            }
            lines[key.DateType] = memberName;

            var roster = rosters.FirstOrDefault(r => r.GroupCode == key.Group);
            if (roster == null || !roster.Entries.Any(e => e.Name == memberName))
            {
                preview.Errors.Add($"轮转设置：{DutyConstants.Groups.DisplayName(key.Group)} 的{DutyConstants.DateTypes.DisplayName(key.DateType)}当前序列「{memberName}」不在该组名单内");
            }
        }

        LogInfo($"值班成员导入预览: {preview.SummaryText}，校验错误 {preview.Errors.Count} 条");
        return Result.Success(preview);
    }

    /// <summary>
    /// 全量对齐导入：以 Excel 四列名单为准重建各组成员与轮转顺序。
    /// 名单有库无 → 新增；名单有库有 → 对齐归属与位次（仅差异写库）；库内在组但名单未列 → 从组移除（只摘组不删人）。
    /// </summary>
    public async Task<Result<DutyMemberImportResult>> ImportMembersAsync(string filePath, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<DutyMemberImportResult>(ensureResult.ErrorCode!, ensureResult.Message!);

        var parseResult = ParseMemberRosterSheetAsync(filePath);
        if (!parseResult.IsSuccess)
            return Result.Failure<DutyMemberImportResult>(parseResult.ErrorCode!, parseResult.Message!);
        var (rosters, parseErrors) = parseResult.Value;

        if (rosters.All(r => r.Entries.Count == 0))
            return Result.Failure<DutyMemberImportResult>(ErrorCodes.VALIDATION_FAILED, "名单为空：四个组的名单列均无成员");

        // 对齐语义下名单必须无歧义：存在解析错误（序号/姓名/重复等）时整体拒绝
        if (parseErrors.Count > 0)
        {
            return Result.Failure<DutyMemberImportResult>(ErrorCodes.VALIDATION_FAILED,
                $"导入文件存在 {parseErrors.Count} 条校验错误，请修正后重试：{parseErrors.First()}");
        }

        // 轮转设置（锚点 + 各线当前序列）：当前序列成员必须在该组名单内
        var settingsResult = ParseRotationSettingsAsync(filePath);
        if (!settingsResult.IsSuccess)
            return Result.Failure<DutyMemberImportResult>(settingsResult.ErrorCode!, settingsResult.Message!);
        var (anchorDate, currentSeqByLine) = settingsResult.Value;
        foreach (var (key, memberName) in currentSeqByLine)
        {
            var roster = rosters.FirstOrDefault(r => r.GroupCode == key.Group);
            if (roster == null || !roster.Entries.Any(e => e.Name == memberName))
            {
                return Result.Failure<DutyMemberImportResult>(ErrorCodes.VALIDATION_FAILED,
                    $"轮转设置：{DutyConstants.Groups.DisplayName(key.Group)} 的{DutyConstants.DateTypes.DisplayName(key.DateType)}当前序列「{memberName}」不在该组名单内");
            }
        }

        // 名单 → 成员视角：name → (组 → 显式序号)
        var excelMemberGroups = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var roster in rosters)
        {
            foreach (var entry in roster.Entries)
            {
                if (!excelMemberGroups.TryGetValue(entry.Name, out var groups))
                {
                    groups = new Dictionary<string, int>();
                    excelMemberGroups[entry.Name] = groups;
                }
                // 同一组的名单列唯一且列内已查重；防御：同人同组取最小序号
                if (!groups.ContainsKey(roster.GroupCode))
                    groups[roster.GroupCode] = entry.Seq;
            }
        }

        // 库内现状
        var membersResult = await GetMembersAsync(ct);
        if (!membersResult.IsSuccess)
            return Result.Failure<DutyMemberImportResult>(membersResult.ErrorCode!, membersResult.Message!);
        var existingMembers = membersResult.Value ?? new List<DutyMemberView>();
        var existingByName = existingMembers.ToDictionary(m => m.Name, StringComparer.Ordinal);

        var result = new DutyMemberImportResult();

        await using var tx = await _dbService.BeginTransactionScopeAsync(ct);
        try
        {
            // 1. 名单成员：新增 / 对齐归属与位次
            foreach (var (name, targetGroups) in excelMemberGroups)
            {
                var existing = existingByName.TryGetValue(name, out var found) ? found : null;

                if (existing == null)
                {
                    var insertResult = await _dbService.ExecuteScalarAsync<long>(@"
                        INSERT INTO nc_duty_members (name, is_active, sort_order, created_at, updated_at)
                        VALUES ($1, TRUE, COALESCE((SELECT MAX(sort_order) + 1 FROM nc_duty_members), 0), NOW(), NOW())
                        RETURNING id", ct, name);
                    if (insertResult.IsFailure)
                        throw new BusinessException(insertResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, insertResult.Message ?? "新增成员失败");
                    var memberId = insertResult.Value;

                    foreach (var (group, seq) in targetGroups)
                    {
                        var g = await _dbService.ExecuteNonQueryAsync(
                            "INSERT INTO nc_duty_member_groups (member_id, group_code, sort_order) VALUES ($1, $2, $3)",
                            ct, memberId, group, seq);
                        if (g.IsFailure)
                            throw new BusinessException(g.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, g.Message ?? "写入组归属失败");
                    }
                    result.NewCount++;
                    continue;
                }

                // 对齐：目标组集 vs 现有组集
                var existingGroupCodes = existing.Groups.Select(g => g.GroupCode).ToHashSet();
                var targetGroupCodes = targetGroups.Keys.ToHashSet();

                // 移除：现有但不在名单中的组（全量对齐语义）
                foreach (var removedGroup in existingGroupCodes.Where(c => !targetGroupCodes.Contains(c)))
                {
                    var d = await _dbService.ExecuteNonQueryAsync(
                        "DELETE FROM nc_duty_member_groups WHERE member_id = $1 AND group_code = $2",
                        ct, existing.Id, removedGroup);
                    if (d.IsFailure)
                        throw new BusinessException(d.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, d.Message ?? "移除组归属失败");
                    result.RemoveCount++;
                }

                // 新增：名单中有但成员尚不属于的组
                foreach (var (group, seq) in targetGroups.Where(kv => !existingGroupCodes.Contains(kv.Key)))
                {
                    var g = await _dbService.ExecuteNonQueryAsync(
                        "INSERT INTO nc_duty_member_groups (member_id, group_code, sort_order) VALUES ($1, $2, $3)",
                        ct, existing.Id, group, seq);
                    if (g.IsFailure)
                        throw new BusinessException(g.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, g.Message ?? "写入组归属失败");
                }

                // 位次修正：已在目标组但位次变化（仅差异才写库，重复导入幂等）
                foreach (var (group, seq) in targetGroups.Where(kv => existingGroupCodes.Contains(kv.Key)))
                {
                    var currentSeq = existing.Groups.First(g => g.GroupCode == group).SortOrder;
                    if (currentSeq != seq)
                    {
                        var u = await _dbService.ExecuteNonQueryAsync(
                            "UPDATE nc_duty_member_groups SET sort_order = $3 WHERE member_id = $1 AND group_code = $2",
                            ct, existing.Id, group, seq);
                        if (u.IsFailure)
                            throw new BusinessException(u.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, u.Message ?? "更新组内顺序失败");
                    }
                }

                result.UpdateCount++;
            }

            // 2. 库内成员不在名单中 → 从其所有组移除（只摘组不删人；成对批量删除，保持 (member_id, group_code) 配对语义）
            var removePairs = new List<(long MemberId, string GroupCode)>();
            foreach (var member in existingMembers)
            {
                if (excelMemberGroups.ContainsKey(member.Name)) continue;
                foreach (var g in member.Groups)
                    removePairs.Add((member.Id, g.GroupCode));
            }

            if (removePairs.Count > 0)
            {
                var removeMemberIds = removePairs.Select(p => p.MemberId).ToArray();
                var removeGroupCodes = removePairs.Select(p => p.GroupCode).ToArray();
                var d = await _dbService.ExecuteNonQueryAsync(
                    @"DELETE FROM nc_duty_member_groups
                      WHERE (member_id, group_code) IN (SELECT * FROM unnest($1::bigint[], $2::text[]))",
                    ct, removeMemberIds, removeGroupCodes);
                if (d.IsFailure)
                    throw new BusinessException(d.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, d.Message ?? "移除组归属失败");
                result.RemoveCount += removePairs.Count;
            }

            // 3. 轮转锚点：有起始日期时，按轮转线分别设置游标为「当前序列的前一位」
            //    （未填写的线从名单头开始；生成锚点所在月时自动从锚点日起，锚点日当天各线即轮到其当前序列成员）
            if (anchorDate.HasValue)
            {
                foreach (var group in DutyConstants.Groups.DisplayOrder)
                {
                    var roster = rosters.FirstOrDefault(r => r.GroupCode == group);
                    var ordered = roster?.Entries.OrderBy(e => e.Seq).ToList() ?? new List<DutyRosterEntry>();

                    foreach (var dateType in new[] { DutyConstants.DateTypes.WORKDAY, DutyConstants.DateTypes.RESTDAY, DutyConstants.DateTypes.HOLIDAY })
                    {
                        long? lastMemberId = null;
                        var lineKey = (group, dateType);
                        if (currentSeqByLine.TryGetValue(lineKey, out var currentName) && roster != null)
                        {
                            var idx = ordered.FindIndex(e => e.Name == currentName);
                            if (idx > 0)
                            {
                                var prev = existingByName.TryGetValue(ordered[idx - 1].Name, out var prevFound)
                                    ? prevFound
                                    : null;
                                if (prev == null)
                                {
                                    // 前一位是本次新增成员：取其新 ID
                                    var idResult = await _dbService.ExecuteScalarAsync<long>(
                                        "SELECT id FROM nc_duty_members WHERE name = $1 LIMIT 1", ct, ordered[idx - 1].Name);
                                    if (!idResult.IsSuccess)
                                        throw new BusinessException(idResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, idResult.Message ?? "定位当前序列前一位失败");
                                    lastMemberId = idResult.Value;
                                }
                                else
                                {
                                    lastMemberId = prev.Id;
                                }
                            }
                            // idx == 0 → lastMemberId 保持 NULL（从名单头开始）
                        }

                        var upsertResult = await _dbService.ExecuteNonQueryAsync(@"
                            INSERT INTO nc_duty_rotation_state (group_code, date_type, last_member_id, updated_at)
                            VALUES ($1, $2, $3, NOW())
                            ON CONFLICT (group_code, date_type)
                            DO UPDATE SET last_member_id = EXCLUDED.last_member_id, updated_at = NOW()",
                            ct, group, dateType, lastMemberId);
                        if (upsertResult.IsFailure)
                            throw new BusinessException(upsertResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, upsertResult.Message ?? "设置轮转游标失败");
                    }
                }

                // 锚点日期持久化（生成该月值班表时自动从此日起）
                var anchorResult = await _dbService.ExecuteNonQueryAsync(@"
                    INSERT INTO nc_duty_settings (setting_key, setting_value, remark, updated_at)
                    VALUES ($1, $2, NOW(), NOW())
                    ON CONFLICT (setting_key)
                    DO UPDATE SET setting_value = EXCLUDED.setting_value, updated_at = NOW()",
                    ct, DutyConstants.SettingKeys.IMPORT_ANCHOR_DATE, anchorDate.Value.ToString("yyyy-MM-dd"));
                if (anchorResult.IsFailure)
                    throw new BusinessException(anchorResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, anchorResult.Message ?? "写入轮转锚点失败");

                // 锚点月校准起点固化（12 条线：起点=当前序列的前一位；锚点月重生成恒定从此起，结果确定）
                foreach (var group in DutyConstants.Groups.DisplayOrder)
                {
                    foreach (var dateType in new[] { DutyConstants.DateTypes.WORKDAY, DutyConstants.DateTypes.RESTDAY, DutyConstants.DateTypes.HOLIDAY })
                    {
                        long? anchorLast = null;
                        var lineKey = (group, dateType);
                        if (currentSeqByLine.TryGetValue(lineKey, out var currentName))
                        {
                            var roster = rosters.FirstOrDefault(r => r.GroupCode == group);
                            var ordered = roster?.Entries.OrderBy(e => e.Seq).ToList() ?? new List<DutyRosterEntry>();
                            var idx = ordered.FindIndex(e => e.Name == currentName);
                            if (idx > 0)
                            {
                                var prevName = ordered[idx - 1].Name;
                                if (existingByName.TryGetValue(prevName, out var prevFound))
                                {
                                    anchorLast = prevFound.Id;
                                }
                                else
                                {
                                    var idResult = await _dbService.ExecuteScalarAsync<long>(
                                        "SELECT id FROM nc_duty_members WHERE name = $1 LIMIT 1", ct, prevName);
                                    if (!idResult.IsSuccess)
                                        throw new BusinessException(idResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, idResult.Message ?? "定位锚点校准起点失败");
                                    anchorLast = idResult.Value;
                                }
                            }
                            // idx == 0 → NULL（从名单头开始）
                        }

                        var anchorStartKey = DutyConstants.SettingKeys.AnchorStartKey(group, dateType);
                        var anchorStartResult = await _dbService.ExecuteNonQueryAsync(@"
                            INSERT INTO nc_duty_settings (setting_key, setting_value, remark, updated_at)
                            VALUES ($1, $2, $3, NOW())
                            ON CONFLICT (setting_key)
                            DO UPDATE SET setting_value = EXCLUDED.setting_value, updated_at = NOW()",
                            ct, anchorStartKey, anchorLast?.ToString() ?? string.Empty,
                            $"锚点月校准起点：{group}/{dateType}");
                        if (anchorStartResult.IsFailure)
                            throw new BusinessException(anchorStartResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, anchorStartResult.Message ?? "写入锚点校准起点失败");
                    }
                }

                result.AnchorDate = anchorDate.Value;
            }

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "批量导入值班成员名单");
            return Result.FromException<DutyMemberImportResult>(ex);
        }

        result.Success = true;
        result.Message = result.SummaryText;
        Logger.LogBusiness("批量导入值班成员名单",
            ("File", global::System.IO.Path.GetFileName(filePath)),
            ("New", result.NewCount),
            ("Updated", result.UpdateCount),
            ("Removed", result.RemoveCount));
        return Result.Success(result);
    }

    public async Task<Result<string>> ExportMemberImportTemplateAsync(string outputDir, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(outputDir))
            return Result.Failure<string>(ErrorCodes.VALIDATION_FAILED, "输出目录不能为空");

        try
        {
            ExcelPackage.License.SetNonCommercialOrganization("民政社会救助管理系统");

            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("数据");
            // 8 列布局：序号 | 组名 × 4（序号 = 该组轮转顺序，按值升序；组内唯一、允许跳号）
            for (var pair = 0; pair < 4; pair++)
            {
                var groupCode = DutyConstants.Groups.DisplayOrder[pair];
                var sortCell = sheet.Cells[1, pair * 2 + 1];
                sortCell.Value = "序号";
                sortCell.Style.Font.Bold = true;
                sortCell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;

                var nameCell = sheet.Cells[1, pair * 2 + 2];
                nameCell.Value = DutyConstants.Groups.DisplayName(groupCode);
                nameCell.Style.Font.Bold = true;
                nameCell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;

                sheet.Column(pair * 2 + 1).Width = 8;
                sheet.Column(pair * 2 + 2).Width = 16;
            }

            var settings = package.Workbook.Worksheets.Add("轮转设置");
            // 布局：起始日期行 → 表头行（组名 | 工作日序列 | 休息日序列 | 法定节假日序列）→ 各组数据行
            settings.Cells[1, 1].Value = "起始日期";
            settings.Cells[1, 1].Style.Font.Bold = true;
            settings.Cells[1, 2].Style.Numberformat.Format = "yyyy-mm-dd";

            settings.Cells[2, 1].Value = "说明：起始日期（选填）= 从该日起按名单轮转，导入后生成该月值班表将自动从此日起。";
            settings.Cells[2, 1].Style.WrapText = true;
            settings.Cells[3, 1].Value = "各线「当前序列」= 下个值班的人（须在该组名单内）。每组有三条独立轮转线（工作日/休息日/法定节假日），可分别指定；留空的线从名单头开始。中层管理组仅法定节假日线参与值班。";

            settings.Cells[5, 1].Value = "组名";
            settings.Cells[5, 2].Value = DutyConstants.DateTypes.DisplayName(DutyConstants.DateTypes.WORKDAY) + "序列";
            settings.Cells[5, 3].Value = DutyConstants.DateTypes.DisplayName(DutyConstants.DateTypes.RESTDAY) + "序列";
            settings.Cells[5, 4].Value = DutyConstants.DateTypes.DisplayName(DutyConstants.DateTypes.HOLIDAY) + "序列";
            for (var col = 1; col <= 4; col++)
            {
                settings.Cells[5, col].Style.Font.Bold = true;
                settings.Cells[5, col].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
            }

            var groupRows = new[] { DutyConstants.Groups.LEADER, DutyConstants.Groups.MIDDLE, DutyConstants.Groups.MALE, DutyConstants.Groups.FEMALE };
            for (var i = 0; i < groupRows.Length; i++)
            {
                settings.Cells[6 + i, 1].Value = DutyConstants.Groups.DisplayName(groupRows[i]);
            }

            settings.Column(1).Width = 16;
            settings.Column(2).Width = 14;
            settings.Column(3).Width = 14;
            settings.Column(4).Width = 14;

            var guideHelp = package.Workbook.Worksheets.Add("填写说明");
            var guideLines = new[]
            {
                "值班成员名单批量导入模板 - 填写说明",
                "",
                "1. 「数据」工作表为 8 列：每组的「序号 + 姓名」成对填写。序号就是该成员在本组的值班轮转顺序",
                "   （按序号从小到大依次轮转，序号小的先值班），同组内序号不得重复，建议按 1、2、3… 连续填写；允许跳号（如 10、20、30）以便后续插队。",
                "2. 序号为必填项：只填姓名不填序号、或只填序号不填姓名的行会报错。",
                "3. 同一个人可以出现在多个组的名单里（如中层管理组成员同时属男生组），表示一人多组；各组的序号相互独立。",
                "4. 名单中间可以留空档：读取时跳过整行空档继续。",
                "5. 全量对齐：以本次 Excel 名单为准——名单里没有列到的库内成员将从其所在组移除（只摘组不删人）；",
                "   某组整列为空 = 该组名单将被清空，导入前会强提醒确认。",
                "6. 电话/入职离职时间/启用/备注等字段不在名单模板内，请导入后在成员列表中单条编辑维护。",
                "7. 本页为说明页，导入时不会读取；请仅在「数据」工作表填写名单。",
            };
            for (var i = 0; i < guideLines.Length; i++)
            {
                guideHelp.Cells[i + 1, 1].Value = guideLines[i];
                if (i == 0) guideHelp.Cells[i + 1, 1].Style.Font.Bold = true;
            }
            guideHelp.Column(1).Width = 110;

            Directory.CreateDirectory(outputDir);
            var filePath = global::System.IO.Path.Combine(outputDir, DutyConstants.TemplateSettings.MemberImportTemplateFileName);
            var bytes = await package.GetAsByteArrayAsync(ct);
            await File.WriteAllBytesAsync(filePath, bytes, ct);

            LogInfo($"值班成员导入模板生成完成: {filePath}");
            return Result.Success(filePath);
        }
        catch (Exception ex)
        {
            LogException(ex, "生成值班成员导入模板");
            return Result.FromException<string>(ex);
        }
    }

    #endregion
    #region 月度值班表查询

    public async Task<Result<DutyMonthSchedule>> GetMonthScheduleAsync(int year, int month, CancellationToken ct = default)
    {
        if (year < 2000 || year > 2100 || month < 1 || month > 12)
            return Result.Failure<DutyMonthSchedule>(ErrorCodes.DUTY_INVALID_DATE, "年月无效");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<DutyMonthSchedule>(ensureResult.ErrorCode!, ensureResult.Message!);

        var scheduleSql = @"
            SELECT s.id, s.duty_date, s.date_type, s.group_code, s.member_id, s.rotation_member_id, s.member_name,
                   COALESCE(m.phone, '') AS phone, s.year, s.month, s.is_adjusted, s.remark, s.created_at
            FROM nc_duty_schedules s
            LEFT JOIN nc_duty_members m ON m.id = s.member_id
            WHERE s.year = $1 AND s.month = $2
            ORDER BY s.duty_date";
        var scheduleResult = await _dbService.QueryAsync<DutySchedule>(scheduleSql, ct, year, month);
        if (!scheduleResult.IsSuccess)
            return Result.Failure<DutyMonthSchedule>(scheduleResult.ErrorCode!, scheduleResult.Message!);

        var schedules = scheduleResult.Value ?? new List<DutySchedule>();
        var scheduleLookup = schedules.ToLookup(s => (s.DutyDate.Date, s.GroupCode));
        var dateLookup = schedules.ToLookup(s => s.DutyDate.Date);

        var samePairResult = await GetRestdayLeaderSamePairAsync(ct);
        var monthSchedule = new DutyMonthSchedule
        {
            Year = year,
            Month = month,
            IsGenerated = schedules.Count > 0,
            RestdayLeaderSamePair = samePairResult.IsSuccess && samePairResult.Value
        };

        var days = DateTime.DaysInMonth(year, month);
        for (var day = 1; day <= days; day++)
        {
            var date = new DateTime(year, month, day);
            var daySchedules = dateLookup[date.Date].ToList();

            // 已固化班次的日期类型以生成时快照为准（节假日数据后续变动不影响历史展示）
            var dateType = daySchedules.Count > 0
                ? daySchedules[0].DateType
                : ClassifyDate(date);

            var row = new DutyDayRow
            {
                DutyDate = date,
                DateType = dateType,
                HolidayName = dateType == DutyConstants.DateTypes.HOLIDAY
                    ? _holidayService.GetLegalHolidayName(date) ?? string.Empty
                    : string.Empty
            };
            var required = RequiredGroups(dateType);

            foreach (var group in DutyConstants.Groups.DisplayOrder)
            {
                var schedule = scheduleLookup[(date, group)].FirstOrDefault();
                row.Cells.Add(new DutyScheduleCell
                {
                    ScheduleId = schedule?.Id ?? 0,
                    DutyDate = date,
                    MemberId = schedule?.MemberId,
                    GroupCode = group,
                    GroupName = DutyConstants.Groups.DisplayName(group),
                    MemberName = schedule?.MemberName ?? string.Empty,
                    Phone = schedule?.Phone ?? string.Empty,
                    IsAdjusted = schedule?.IsAdjusted ?? false,
                    IsRequired = required.Contains(group)
                });
            }

            monthSchedule.Days.Add(row);
        }

        return Result.Success(monthSchedule);
    }

    #endregion

    #region 月度值班表生成（轮转核心）

    public async Task<Result<DutyGenerationSummary>> GenerateMonthAsync(int year, int month, bool force, CancellationToken ct = default)
    {
        if (year < 2000 || year > 2100 || month < 1 || month > 12)
            return Result.Failure<DutyGenerationSummary>(ErrorCodes.DUTY_INVALID_DATE, "年月无效");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<DutyGenerationSummary>(ensureResult.ErrorCode!, ensureResult.Message!);

        // 已生成校验
        var countResult = await _dbService.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM nc_duty_schedules WHERE year = $1 AND month = $2", ct, year, month);
        if (!countResult.IsSuccess)
            return Result.Failure<DutyGenerationSummary>(countResult.ErrorCode!, countResult.Message!);
        if (countResult.Value > 0 && !force)
            return Result.Failure<DutyGenerationSummary>(ErrorCodes.DUTY_SCHEDULE_EXISTS, $"{year} 年 {month} 月值班表已生成");

        // 加载轮转队列：组 → (成员ID, 姓名) 按 sort_order, id 排序（仅启用成员）
        var queueResult = await LoadActiveQueuesAsync(ct);
        if (!queueResult.IsSuccess)
            return Result.Failure<DutyGenerationSummary>(queueResult.ErrorCode!, queueResult.Message!);
        var queues = queueResult.Value;

        if (queues.Values.Sum(q => q.Count) == 0)
            return Result.Failure<DutyGenerationSummary>(ErrorCodes.DUTY_NO_MEMBERS, "没有可用的值班成员");

        // 加载轮转游标（仅作"上月无班次"时的兜底起点：首月/锚点月校准；后续月份以班次事实接续）
        var rotationResult = await _dbService.QueryAsync<DutyRotationState>(
            "SELECT id, group_code, date_type, last_member_id, updated_at FROM nc_duty_rotation_state", ct);
        if (!rotationResult.IsSuccess)
            return Result.Failure<DutyGenerationSummary>(rotationResult.ErrorCode!, rotationResult.Message!);
        var rotationMap = (rotationResult.Value ?? new List<DutyRotationState>())
            .GroupBy(r => (r.GroupCode, r.DateType))
            .ToDictionary(g => g.Key, g => g.First().LastMemberId);

        // 请假区间（生成时逐日判定：区间内成员顺延跳过并消耗轮次）
        var leaveResult = await LoadLeaveRangesAsync(ct);
        if (!leaveResult.IsSuccess)
            return Result.Failure<DutyGenerationSummary>(leaveResult.ErrorCode!, leaveResult.Message!);
        var leaveRanges = leaveResult.Value;

        var samePairResult = await GetRestdayLeaderSamePairAsync(ct);
        var sameLeaderPair = samePairResult.IsSuccess && samePairResult.Value;

        // 逐日推演（内存计算，事务内一次性落库）
        var summary = new DutyGenerationSummary();
        var pendingSchedules = new List<(DateTime Date, string DateType, string Group, long MemberId, string MemberName)>();
        var rotationUpdates = new Dictionary<(string Group, string DateType), long>();
        // 该线本月是否已初始化游标（首日取快照起点，之后走逐日推进的内存游标）
        var initializedLines = new HashSet<(string Group, string DateType)>();

        var days = DateTime.DaysInMonth(year, month);
        var startDay = 1;
        var isAnchorMonth = false;

        // 轮转锚点截断：导入设置的锚点落在本月时，仅从锚点日起生成
        //（锚点日当天即轮到"当前序列"成员；之前的日期不生成，保持空白/旧安排）
        var anchorSetting = await GetSettingValueAsync(DutyConstants.SettingKeys.IMPORT_ANCHOR_DATE, ct);
        if (anchorSetting.IsSuccess &&
            DateTime.TryParse(anchorSetting.Value, out var anchor) &&
            anchor.Year == year && anchor.Month == month)
        {
            isAnchorMonth = true;
            startDay = anchor.Day;
            summary.AnchorDay = startDay;
            LogInfo($"轮转锚点生效：{year}-{month:D2} 从 {startDay} 日起生成");
        }

        for (var day = startDay; day <= days; day++)
        {
            var date = new DateTime(year, month, day);
            var dateType = ClassifyDate(date);
            switch (dateType)
            {
                case DutyConstants.DateTypes.WORKDAY: summary.WorkdayCount++; break;
                case DutyConstants.DateTypes.RESTDAY: summary.RestdayCount++; break;
                default: summary.HolidayCount++; break;
            }

            var selected = new HashSet<long>();
            var required = RequiredGroups(dateType);

            foreach (var group in DutyConstants.Groups.SelectionOrder)
            {
                if (!required.Contains(group)) continue;

                // 开关规则：休息日的周日复用周六所选领导（不推进领导组休息日游标）
                if (group == DutyConstants.Groups.LEADER &&
                    dateType == DutyConstants.DateTypes.RESTDAY &&
                    sameLeaderPair &&
                    date.DayOfWeek == DayOfWeek.Sunday)
                {
                    var saturdayLeader = await TryGetSaturdayLeaderAsync(date.AddDays(-1), pendingSchedules, ct);
                    if (saturdayLeader.HasValue)
                    {
                        pendingSchedules.Add((date, dateType, group, saturdayLeader.Value.Id, saturdayLeader.Value.Name));
                        continue;
                    }
                    // 周六无领导班次（如领导组无人）时按正常轮转处理
                }

                if (!queues.TryGetValue(group, out var queue) || queue.Count == 0)
                {
                    LogWarn($"组 {group} 无启用成员，{date:yyyy-MM-dd} {DutyConstants.Groups.DisplayName(group)} 班次缺位");
                    continue;
                }

                // 从起点人之后取第一位；同日已选者冲突顺延（被跳过者视为已消耗轮次）
                // 起点语义（该线本月首次选人，四级回退）：
                //   ① 上月该线最后值班人（班次事实，直接查班次表——跨月无缝接续，重生成结果天然确定）
                //   ② 锚点月校准起点（nc_duty_settings 锚点校准值；仅锚点月且上月无班次时命中）
                //   ③ 轮转游标（上月无班次且无锚点校准的兜底：系统首月）
                //   ④ 名单头（游标为空）
                // 后续日期 → 使用逐日推进后的内存游标
                var lineKey = (group, dateType);
                long? lastMemberId;
                if (initializedLines.Add(lineKey))
                {
                    lastMemberId = await GetLastScheduleMemberAsync(year, month, group, dateType, ct);
                    if (!lastMemberId.HasValue && isAnchorMonth)
                        lastMemberId = await GetAnchorStartMemberAsync(group, dateType, ct);
                    if (!lastMemberId.HasValue)
                        rotationMap.TryGetValue(lineKey, out lastMemberId);
                    rotationMap[lineKey] = lastMemberId;             // 初始化内存游标，供后续日期推进
                }
                else
                {
                    rotationMap.TryGetValue(lineKey, out lastMemberId);
                }
                var startIndex = 0;
                if (lastMemberId.HasValue)
                {
                    var idx = queue.FindIndex(m => m.Id == lastMemberId.Value);
                    if (idx >= 0) startIndex = idx + 1;
                    // 起点人已被删除/停用时从头轮转（队列仅含启用成员，按 ID 定位，与姓名无关）
                }

                (long Id, string Name)? chosen = null;
                for (var i = 0; i < queue.Count; i++)
                {
                    var candidate = queue[(startIndex + i) % queue.Count];
                    // 节假日规则：中层干部组成员只在中层班值班，其他班组直接跳过（不消耗轮次）
                    if (dateType == DutyConstants.DateTypes.HOLIDAY &&
                        group != DutyConstants.Groups.MIDDLE &&
                        candidate.IsMiddle)
                    {
                        summary.UnavailableSkippedCount++;
                        continue;
                    }
                    // 同人一天一班冲突 / 当日请假 / 入职离职边界 → 顺延跳过（被跳过者视为已消耗轮次）
                    if (selected.Contains(candidate.Id) ||
                        !candidate.AvailableOn(date) ||
                        IsOnLeave(leaveRanges, candidate.Id, date))
                    {
                        summary.UnavailableSkippedCount++;
                        continue;
                    }
                    chosen = (candidate.Id, candidate.Name);
                    break;
                }

                if (chosen == null)
                {
                    // 极端情况：组内仅 1 人且已当日出班，班次缺位
                    LogWarn($"组 {group} 在 {date:yyyy-MM-dd} 全员冲突，班次缺位");
                    continue;
                }

                selected.Add(chosen.Value.Id);
                pendingSchedules.Add((date, dateType, group, chosen.Value.Id, chosen.Value.Name));
                // 修复：同步推进内存游标——此前 rotationMap 从不更新，每天从同一起点取人导致全月同一人
                rotationMap[(group, dateType)] = chosen.Value.Id;
                rotationUpdates[(group, dateType)] = chosen.Value.Id;
            }
        }

        if (pendingSchedules.Count == 0)
            return Result.Failure<DutyGenerationSummary>(ErrorCodes.DUTY_NO_MEMBERS, "未生成任何班次，请检查各组成员配置");

        // 事务落库
        var replay = new ShiftReplayOutcome();
        await using var tx = await _dbService.BeginTransactionScopeAsync(ct);
        try
        {
            if (force)
            {
                var deleteResult = await _dbService.ExecuteNonQueryAsync(
                    "DELETE FROM nc_duty_schedules WHERE year = $1 AND month = $2", ct, year, month);
                if (deleteResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<DutyGenerationSummary>(deleteResult.ErrorCode!, deleteResult.Message!);
                }
            }

            // 多行 VALUES 批写（占位骨架 + $n 位置参数；rotation_member_id 复用同占位；ON CONFLICT 兜底防重复）
            // rotation_member_id = 轮转消耗事实（选中成员），串班/代班等显示调整不覆盖该列
            var (scheduleValues, scheduleArgs) = MultiRowValuesBuilder.Build(
                pendingSchedules.Count, 7,
                i =>
                {
                    var s = pendingSchedules[i];
                    return new object?[] { s.Date, s.DateType, s.Group, s.MemberId, s.MemberName, year, month };
                },
                o => $"(${o}, ${o + 1}, ${o + 2}, ${o + 3}, ${o + 3}, ${o + 4}, ${o + 5}, ${o + 6})");
            var insertResult = await _dbService.ExecuteNonQueryAsync(@"
                INSERT INTO nc_duty_schedules (duty_date, date_type, group_code, member_id, rotation_member_id, member_name, year, month)
                VALUES " + scheduleValues + @"
                ON CONFLICT (duty_date, group_code) DO NOTHING", ct, scheduleArgs);
            if (insertResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<DutyGenerationSummary>(insertResult.ErrorCode!, insertResult.Message!);
            }

            // 游标批量 upsert（锚点月冻结游标：起点恒定取锚点校准值，保证重生成结果确定；后续月份由班次事实接续，不依赖游标）
            // 多行 VALUES 批写：updated_at 用 NOW() 字面量，仅 3 个位置参数/行
            if (!isAnchorMonth && rotationUpdates.Count > 0)
            {
                var rotationList = rotationUpdates.ToList();
                var (rotationValues, rotationArgs) = MultiRowValuesBuilder.Build(
                    rotationList.Count, 3,
                    i => new object?[] { rotationList[i].Key.Group, rotationList[i].Key.DateType, rotationList[i].Value },
                    o => $"(${o}, ${o + 1}, ${o + 2}, NOW())");
                var upsertSql = @"
                    INSERT INTO nc_duty_rotation_state (group_code, date_type, last_member_id, updated_at)
                    VALUES " + rotationValues + @"
                    ON CONFLICT (group_code, date_type)
                    DO UPDATE SET last_member_id = EXCLUDED.last_member_id, updated_at = NOW()";
                var upsertResult = await _dbService.ExecuteNonQueryAsync(upsertSql, ct, rotationArgs);
                if (upsertResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<DutyGenerationSummary>(upsertResult.ErrorCode!, upsertResult.Message!);
                }
            }

            // 重放未撤销的串班/代班调整（force 重生成不丢手工调整；链式校验：班次值班人=记录原班人员才替换）
            replay = await ApplyShiftChangesAsync(year, month, ct);

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "生成月度值班表");
            return Result.FromException<DutyGenerationSummary>(ex);
        }

        summary.ScheduleCount = pendingSchedules.Count;
        replay.MissingMonths.Sort(StringComparer.Ordinal);
        summary.ReplayedChangeCount = replay.AppliedCount;
        summary.PendingChangeCount = replay.PendingCount;
        summary.PendingChangeMonths = replay.MissingMonths;
        summary.SkippedChangeCount = replay.SkippedCount;
        Logger.LogBusiness("生成月度值班表",
            ("Year", year),
            ("Month", month),
            ("ScheduleCount", summary.ScheduleCount),
            ("Workday", summary.WorkdayCount),
            ("Restday", summary.RestdayCount),
            ("Holiday", summary.HolidayCount),
            ("ConflictSkipped", summary.ConflictSkippedCount),
            ("SameLeaderPair", sameLeaderPair),
            ("ChangeApplied", replay.AppliedCount),
            ("ChangePending", replay.PendingCount),
            ("ChangeSkipped", replay.SkippedCount));

        return Result.Success(summary);
    }

    /// <summary>
    /// 查询上月该轮转线的最后消耗人（跨月无缝接续的事实依据）；上月无班次返回 null。
    /// 读 rotation_member_id（轮转消耗事实，不随串班/代班变化；旧数据为空回退 member_id）。
    /// 跨年：1 月的上月 = 上一年 12 月。按 ID 定位（与姓名无关，不受改名/重名影响）。
    /// </summary>
    private async Task<long?> GetLastScheduleMemberAsync(int year, int month, string groupCode, string dateType, CancellationToken ct)
    {
        var (prevYear, prevMonth) = month == 1 ? (year - 1, 12) : (year, month - 1);
        var result = await _dbService.ExecuteScalarAsync<long?>(
            @"SELECT COALESCE(rotation_member_id, member_id) FROM nc_duty_schedules
              WHERE year = $1 AND month = $2 AND group_code = $3 AND date_type = $4 AND member_id IS NOT NULL
              ORDER BY duty_date DESC LIMIT 1",
            ct, prevYear, prevMonth, groupCode, dateType);
        if (!result.IsSuccess)
        {
            LogWarn($"查询上月末值班人失败（{prevYear}-{prevMonth:D2} {groupCode}/{dateType}），该线回退轮转游标：{result.Message}");
            return null;
        }
        return result.Value;
    }

    /// <summary>
    /// 读取锚点月某轮转线的校准起点（nc_duty_settings，key=duty_anchor_start_{组}_{类型}）；
    /// 值缺失或解析失败返回 null（调用方回退轮转游标）。锚点月起点恒定取此值 → 重生成结果确定。
    /// </summary>
    private async Task<long?> GetAnchorStartMemberAsync(string groupCode, string dateType, CancellationToken ct)
    {
        var key = DutyConstants.SettingKeys.AnchorStartKey(groupCode, dateType);
        var setting = await GetSettingValueAsync(key, ct);
        if (!setting.IsSuccess || string.IsNullOrWhiteSpace(setting.Value))
            return null;
        return long.TryParse(setting.Value, out var id) ? id : null;
    }

    /// <summary>
    /// 查找指定周六的带班领导：优先取本次生成事务内已推演的班次，
    /// 否则查库中已固化班次（覆盖跨月周末对：上月已生成、本月初的周日）。
    /// </summary>
    private async Task<(long Id, string Name)?> TryGetSaturdayLeaderAsync(
        DateTime saturday,
        List<(DateTime Date, string DateType, string Group, long MemberId, string MemberName)> pendingSchedules,
        CancellationToken ct)
    {
        var pending = pendingSchedules.FirstOrDefault(s =>
            s.Date == saturday && s.Group == DutyConstants.Groups.LEADER);
        if (pending != default)
            return (pending.MemberId, pending.MemberName);

        var sql = @"
            SELECT member_id, member_name
            FROM nc_duty_schedules
            WHERE duty_date = $1 AND group_code = $2";
        var result = await _dbService.QueryAsync<DutySchedule>(sql, ct, saturday, DutyConstants.Groups.LEADER);
        var schedule = result.Value?.FirstOrDefault();
        return schedule == null ? null : (schedule.MemberId ?? 0, schedule.MemberName);
    }

    public async Task<Result> ClearMonthAsync(int year, int month, CancellationToken ct = default)
    {
        if (year < 2000 || year > 2100 || month < 1 || month > 12)
            return Result.Failure(ErrorCodes.DUTY_INVALID_DATE, "年月无效");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        var result = await _dbService.ExecuteNonQueryAsync(
            "DELETE FROM nc_duty_schedules WHERE year = $1 AND month = $2", ct, year, month);
        if (result.IsSuccess)
        {
            Logger.LogBusiness("清空月度值班表", ("Year", year), ("Month", month));
            return Result.Success();
        }
        return result;
    }

    #endregion

    #region 轮转游标

    public async Task<Result<List<DutyRotationStateView>>> GetRotationStatesAsync(CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<List<DutyRotationStateView>>(ensureResult.ErrorCode!, ensureResult.Message!);

        var sql = @"
            SELECT r.group_code, r.date_type, r.last_member_id, m.name AS last_member_name, r.updated_at
            FROM nc_duty_rotation_state r
            LEFT JOIN nc_duty_members m ON m.id = r.last_member_id
            ORDER BY r.group_code, r.date_type";
        var result = await _dbService.QueryAsync<DutyRotationStateView>(sql, ct);
        return result.IsSuccess && result.Value is not null
            ? Result.Success(result.Value)
            : Result.Success(new List<DutyRotationStateView>());
    }

    public async Task<Result> ResetRotationAsync(string groupCode, string dateType, CancellationToken ct = default)
    {
        if (!DutyConstants.Groups.IsValid(groupCode) || !DutyConstants.DateTypes.IsValid(dateType))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "组编码或日期类型无效");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        var result = await _dbService.ExecuteNonQueryAsync(
            "DELETE FROM nc_duty_rotation_state WHERE group_code = $1 AND date_type = $2", ct, groupCode, dateType);
        if (result.IsSuccess)
        {
            Logger.LogBusiness("重置值班轮转游标", ("Group", groupCode), ("DateType", dateType));
            return Result.Success();
        }
        return result;
    }

    #endregion

    #region 设置

    public async Task<Result<bool>> GetRestdayLeaderSamePairAsync(CancellationToken ct = default)
    {
        var valueResult = await GetSettingValueAsync(DutyConstants.SettingKeys.RESTDAY_LEADER_SAME_PAIR, ct);
        if (!valueResult.IsSuccess)
            return Result.Failure<bool>(valueResult.ErrorCode!, valueResult.Message!);
        return Result.Success(valueResult.Value == DutyConstants.SettingOn);
    }

    public async Task<Result> SaveRestdayLeaderSamePairAsync(bool enabled, CancellationToken ct = default)
    {
        return await SaveSettingAsync(
            DutyConstants.SettingKeys.RESTDAY_LEADER_SAME_PAIR,
            enabled ? DutyConstants.SettingOn : DutyConstants.SettingOff,
            "休息日（周六/周日）带班领导两天同人", ct);
    }

    private async Task<Result> SaveSettingAsync(string key, string value, string remark, CancellationToken ct)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        var sql = @"
            INSERT INTO nc_duty_settings (setting_key, setting_value, remark, updated_at)
            VALUES ($1, $2, $3, NOW())
            ON CONFLICT (setting_key)
            DO UPDATE SET setting_value = EXCLUDED.setting_value, updated_at = NOW()";
        var result = await _dbService.ExecuteNonQueryAsync(sql, ct, key, value, remark);
        if (result.IsSuccess)
        {
            LogInfo($"保存值班设置: {key} = {value}");
            return Result.Success();
        }
        return result;
    }

    private async Task<Result<string>> GetSettingValueAsync(string key, CancellationToken ct)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<string>(ensureResult.ErrorCode!, ensureResult.Message!);

        var sql = "SELECT setting_value FROM nc_duty_settings WHERE setting_key = $1";
        var result = await _dbService.ExecuteScalarAsync<string>(sql, ct, key);
        return result.IsSuccess
            ? Result.Success(result.Value ?? string.Empty)
            : Result.Failure<string>(result.ErrorCode!, result.Message!);
    }

    #endregion

    #region 加载轮转队列

    /// <summary>组编码 → 启用成员队列（按组内 sort_order, id 排序；入职/离职时间用于逐日可用性判定）</summary>
    private async Task<Result<Dictionary<string, List<DutyQueueMember>>>> LoadActiveQueuesAsync(CancellationToken ct)
    {
        var sql = @"
            SELECT g.group_code, m.id, m.name, m.join_date, m.exit_date,
                   EXISTS (SELECT 1 FROM nc_duty_member_groups g2
                           WHERE g2.member_id = m.id AND g2.group_code = 'MIDDLE') AS is_middle
            FROM nc_duty_member_groups g
            JOIN nc_duty_members m ON m.id = g.member_id
            WHERE m.is_active
            ORDER BY g.group_code, g.sort_order, m.id";
        var result = await _dbService.QueryAsync<DutyQueueRow>(sql, ct);
        if (!result.IsSuccess)
            return Result.Failure<Dictionary<string, List<DutyQueueMember>>>(result.ErrorCode!, result.Message!);

        var queues = new Dictionary<string, List<DutyQueueMember>>();
        foreach (var row in result.Value ?? new List<DutyQueueRow>())
        {
            if (!queues.TryGetValue(row.GroupCode, out var list))
            {
                list = new List<DutyQueueMember>();
                queues[row.GroupCode] = list;
            }
            list.Add(new DutyQueueMember(row.Id, row.Name, row.JoinDate, row.ExitDate, row.IsMiddle));
        }
        return Result.Success(queues);
    }

    /// <summary>轮转队列成员（含入职/离职时间、是否中层干部，供逐日可用性判定与节假日排除）</summary>
    private sealed record DutyQueueMember(long Id, string Name, DateTime? JoinDate, DateTime? ExitDate, bool IsMiddle)
    {
        /// <summary>值班日当天是否可参与：已入职且未离职（按日精确判定）</summary>
        public bool AvailableOn(DateTime date)
        {
            if (JoinDate.HasValue && JoinDate.Value > date) return false;
            if (ExitDate.HasValue && ExitDate.Value < date) return false;
            return true;
        }
    }

    private sealed class DutyQueueRow
    {
        public string GroupCode { get; set; } = string.Empty;
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime? JoinDate { get; set; }
        public DateTime? ExitDate { get; set; }
        public bool IsMiddle { get; set; }
    }

    #endregion

    #region 打印导出

    /// <summary>
    /// 构建政务值班表占位符字典：
    /// {日期N}、{星期与农历N}、{领导姓名N}、{中层姓名N}、{女生姓名N}（白班）、{男生姓名N}（夜班），
    /// 以及 {领导电话N}、{中层电话N}、{女生电话N}、{男生电话N}（N=1..31，手机号实时取自成员表）。
    /// 非法定节假日中层列填"—"；班次缺位填"—"；N 超出当月天数填空串（保留模板行结构）。
    /// 老模板不含电话占位符时多余字典项不参与替换，完全向后兼容。
    /// </summary>
    private Dictionary<string, string> BuildGovTemplateFields(DutyMonthSchedule monthSchedule)
    {
        var fields = new Dictionary<string, string>();
        var daysInMonth = DateTime.DaysInMonth(monthSchedule.Year, monthSchedule.Month);

        for (var day = 1; day <= 31; day++)
        {
            if (day > daysInMonth)
            {
                fields[$"{{日期{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                fields[$"{{星期与农历{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                fields[$"{{领导姓名{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                fields[$"{{领导电话{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                fields[$"{{中层姓名{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                fields[$"{{中层电话{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                fields[$"{{女生姓名{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                fields[$"{{女生电话{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                fields[$"{{男生姓名{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                fields[$"{{男生电话{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                continue;
            }

            var row = monthSchedule.Days[day - 1];
            var empty = DutyConstants.OutputRules.EmptyCellPlaceholder;
            var middleName = row.IsHoliday
                ? (string.IsNullOrEmpty(row.MiddleCell.MemberName) ? empty : row.MiddleCell.MemberName)
                : empty; // 中层仅法定节假日出场（确认口径：非节假日填"—"）

            // 电话跟随姓名：无值班人时电话同样填"—"；有值班人但未填手机号时留空
            fields[$"{{日期{day}}}"] = row.DutyDate.ToString(DutyConstants.OutputRules.DateCellFormat);
            fields[$"{{星期与农历{day}}}"] = LunarCalendarHelper.GetWeekAndLunarString(row.DutyDate, row.HolidayName);
            fields[$"{{领导姓名{day}}}"] = string.IsNullOrEmpty(row.LeaderCell.MemberName) ? empty : row.LeaderCell.MemberName;
            fields[$"{{领导电话{day}}}"] = string.IsNullOrEmpty(row.LeaderCell.MemberName) ? empty : row.LeaderCell.Phone;
            fields[$"{{中层姓名{day}}}"] = middleName;
            fields[$"{{中层电话{day}}}"] = middleName == empty ? empty : row.MiddleCell.Phone;
            fields[$"{{女生姓名{day}}}"] = string.IsNullOrEmpty(row.FemaleCell.MemberName) ? empty : row.FemaleCell.MemberName;
            fields[$"{{女生电话{day}}}"] = string.IsNullOrEmpty(row.FemaleCell.MemberName) ? empty : row.FemaleCell.Phone;
            fields[$"{{男生姓名{day}}}"] = string.IsNullOrEmpty(row.MaleCell.MemberName) ? empty : row.MaleCell.MemberName;
            fields[$"{{男生电话{day}}}"] = string.IsNullOrEmpty(row.MaleCell.MemberName) ? empty : row.MaleCell.Phone;
        }

        return fields;
    }

    /// <summary>
    /// 获取政务值班表模板（按固定名称取模板库 nc_biz_templates，与其他模块模板存储方式一致）。
    /// 库中不存在时显式失败，提示在文书模板管理页上传名为「模板_政务值班表」的 Excel 模板。
    /// 注意：GetByNameAsync 只返回元数据（不含 file_data），渲染时按 Id 经工厂加载（updated_at 缓存）。
    /// </summary>
    private async Task<Result<Template>> EnsureGovTemplateAsync(CancellationToken ct)
    {
        var existing = await _templateService.GetByNameAsync(DutyConstants.TemplateSettings.GovDutyTemplateName, ct);
        if (existing != null)
            return Result.Success(existing);

        LogError($"模板库中未找到政务值班表模板: {DutyConstants.TemplateSettings.GovDutyTemplateName}");
        return Result.Failure<Template>(ErrorCodes.TEMPLATE_NOT_FOUND,
            $"模板库中未找到名为「{DutyConstants.TemplateSettings.GovDutyTemplateName}」的模板，" +
            $"请先在「文书模板管理」页上传该模板（类型选 Excel，名称须与上述完全一致）");
    }

    public async Task<Result<byte[]>> RenderMonthByTemplateAsync(int year, int month, CancellationToken ct = default)
    {
        if (year < 2000 || year > 2100 || month < 1 || month > 12)
            return Result.Failure<byte[]>(ErrorCodes.DUTY_INVALID_DATE, "年月无效");

        var monthResult = await GetMonthScheduleAsync(year, month, ct);
        if (!monthResult.IsSuccess || monthResult.Value == null)
            return Result.Failure<byte[]>(monthResult.ErrorCode!, monthResult.Message!);

        var templateResult = await EnsureGovTemplateAsync(ct);
        if (!templateResult.IsSuccess || templateResult.Value == null)
            return Result.Failure<byte[]>(templateResult.ErrorCode!, templateResult.Message!);

        try
        {
            // 模板库链路：updated_at 缓存失效机制由 TemplateEngineFactory 维护（管理页替换后即刻生效）
            var engine = await _templateEngineFactory.CreateFromTemplateIdAsync(templateResult.Value.Id, ct);
            engine.ReplaceFields(BuildGovTemplateFields(monthResult.Value));
            var bytes = await engine.SaveAsync(ct);
            LogInfo($"政务值班表模板渲染完成: {year}-{month}");
            return Result.Success(bytes);
        }
        catch (Exception ex)
        {
            LogException(ex, "渲染政务值班表模板");
            return Result.FromException<byte[]>(ex);
        }
    }

    public async Task<Result<List<string>>> ExportGovRangeAsync(
        int startYear, int startMonth, int endYear, int endMonth, string outputDir, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(outputDir))
            return Result.Failure<List<string>>(ErrorCodes.VALIDATION_FAILED, "输出目录不能为空");
        if (startYear < 2000 || startYear > 2100 || endYear < 2000 || endYear > 2100 ||
            startMonth < 1 || startMonth > 12 || endMonth < 1 || endMonth > 12)
            return Result.Failure<List<string>>(ErrorCodes.DUTY_INVALID_DATE, "年月无效");

        var startValue = startYear * 12 + startMonth;
        var endValue = endYear * 12 + endMonth;
        if (startValue > endValue)
            return Result.Failure<List<string>>(ErrorCodes.VALIDATION_FAILED, "起始月份不能晚于结束月份");
        if (endValue - startValue + 1 > 36)
            return Result.Failure<List<string>>(ErrorCodes.VALIDATION_FAILED, "连续导出最多 36 个月");

        var written = new List<string>();
        var (curYear, curMonth) = (startYear, startMonth);
        while (curYear * 12 + curMonth <= endValue)
        {
            var renderResult = await RenderMonthByTemplateAsync(curYear, curMonth, ct);
            if (!renderResult.IsSuccess || renderResult.Value == null)
            {
                return Result.Failure<List<string>>(renderResult.ErrorCode!,
                    $"已导出 {written.Count} 个文件，{curYear} 年 {curMonth} 月失败：{renderResult.Message}");
            }

            var fileName = string.Format(DutyConstants.TemplateSettings.ExportFileNameFormat, curYear, curMonth);
            var filePath = Path.Combine(outputDir, fileName);
            await File.WriteAllBytesAsync(filePath, renderResult.Value, ct);
            written.Add(filePath);

            (curYear, curMonth) = curMonth == 12 ? (curYear + 1, 1) : (curYear, curMonth + 1);
        }

        Logger.LogBusiness("连续导出政务值班表",
            ("Range", $"{startYear}-{startMonth:D2} ~ {endYear}-{endMonth:D2}"),
            ("Files", written.Count),
            ("OutputDir", outputDir));
        return Result.Success(written);
    }

    public async Task<Result> PrintMonthAsync(int year, int month, string printerName, int copies, CancellationToken ct = default)
    {
        var renderResult = await RenderMonthByTemplateAsync(year, month, ct);
        if (!renderResult.IsSuccess || renderResult.Value == null)
            return renderResult;

        var tempPath = TemplateEngineHelpers.NewTempFilePath(_storageOptions, ".xlsx");
        try
        {
            await File.WriteAllBytesAsync(tempPath, renderResult.Value, ct);
            // 引擎为短命工具对象（与 TemplateEngineFactory 的创建方式一致），打印后即弃
            var engine = new ExcelEngine(_logger, _perfOptions, _storageOptions)
            {
                PrinterName = printerName ?? string.Empty
            };
            await engine.PrintFromFileAsync(tempPath, copies, ct);
            Logger.LogBusiness("打印月度值班表",
                ("Year", year), ("Month", month), ("Printer", printerName ?? "默认打印机"), ("Copies", copies));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "打印月度值班表");
            return Result.FromException(ex);
        }
        finally
        {
            TemplateEngineHelpers.TryDeleteFile(tempPath, _logger, ServiceName);
        }
    }

    public async Task<Result<string>> ExportMonthPdfAsync(int year, int month, string outputDir, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(outputDir))
            return Result.Failure<string>(ErrorCodes.VALIDATION_FAILED, "输出目录不能为空");

        var renderResult = await RenderMonthByTemplateAsync(year, month, ct);
        if (!renderResult.IsSuccess || renderResult.Value == null)
            return Result.Failure<string>(renderResult.ErrorCode!, renderResult.Message!);

        var tempPath = TemplateEngineHelpers.NewTempFilePath(_storageOptions, ".xlsx");
        try
        {
            await File.WriteAllBytesAsync(tempPath, renderResult.Value, ct);
            var engine = new ExcelEngine(_logger, _perfOptions, _storageOptions);
            engine.LoadFromFile(tempPath);
            var pdfPath = await engine.ExportPdfToFileAsync(outputDir, ct);
            Logger.LogBusiness("导出值班表 PDF", ("Year", year), ("Month", month), ("PdfPath", pdfPath));
            return Result.Success(pdfPath);
        }
        catch (Exception ex)
        {
            LogException(ex, "导出值班表 PDF");
            return Result.FromException<string>(ex);
        }
        finally
        {
            TemplateEngineHelpers.TryDeleteFile(tempPath, _logger, ServiceName);
        }
    }

    #endregion
}
