using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.UserManagement;

/// <summary>
/// 登录会话服务实现（数据库权威）。
/// 所有时间判断使用数据库 now()/timestamptz，避免客户端改时钟延长会话。
/// 会话自身的 SQL 一律走 ISessionGuard.Bypass()，否则被吊销后连心跳/登出都写不进去。
/// </summary>
public sealed class UserSessionService : BaseService, IUserSessionService
{
    protected override string ServiceName => "UserSessionService";

    private const string NotifyChannel = "nc_session_events";
    private static readonly TimeSpan PolicyCacheTtl = TimeSpan.FromSeconds(60);

    private readonly IDatabaseService _db;
    private readonly ISessionGuard _guard;
    private readonly ISchemaSyncService _schemaSync;
    private readonly SemaphoreSlim _policyLock = new(1, 1);
    private readonly SemaphoreSlim _schemaLock = new(1, 1);

    private const string SessionTableName = "nc_sys_user_sessions";
    private int _schemaEnsured;

    private SessionPolicy _cachedPolicy = SessionPolicy.CreateDefault();
    private DateTime _policyCachedAtUtc = DateTime.MinValue;
    private DateTime _lastCleanupUtc = DateTime.MinValue;
    private readonly object _cleanupGate = new();

    public UserSessionService(IDatabaseService db, ILoggerService logger, ISessionGuard guard, ISchemaSyncService schemaSync)
        : base(logger)
    {
        _db = db;
        _guard = guard;
        _schemaSync = schemaSync;
    }

    /// <summary>
    /// 确保 nc_sys_user_sessions 存在：升级部署后首次登录前表可能尚未创建，
    /// SchemaSyncService 以 YAML 为权威来源建表（表已存在时它拒绝 DROP，正好可视作"已就绪"）。
    /// </summary>
    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _schemaEnsured) == 1) return;

        await _schemaLock.WaitAsync(ct);
        try
        {
            if (Volatile.Read(ref _schemaEnsured) == 1) return;

            using (_guard.Bypass())
            {
                // 先查是否已存在：避免 SchemaSyncService 对已存在表走"拒绝 DROP"分支并刷 Warn
                var exists = await _db.ExecuteScalarAsync<long>(
                    "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = $1",
                    ct, SessionTableName);

                if (exists.IsFailure)
                {
                    LogWarn($"检查会话表是否存在失败: {exists.Message}");
                    return;
                }

                if (exists.Value > 0)
                {
                    Volatile.Write(ref _schemaEnsured, 1);
                    return;
                }

                var result = await _schemaSync.SyncTableSchemaAsync(SessionTableName, allowDrop: false, progress: null, ct);
                if (result.IsSuccess || (result.Message?.Contains("已存在", StringComparison.Ordinal) ?? false))
                {
                    Volatile.Write(ref _schemaEnsured, 1);
                    LogWarn("会话表不存在，已按 YAML 自动创建（建议部署时执行 docs/migrations/20261023_user_sessions.sql 预估表结构）");
                }
                else
                {
                    // 并发首次登录时另一客户端可能已建表：复查一次，存在即视为就绪
                    var recheck = await _db.ExecuteScalarAsync<long>(
                        "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = $1",
                        ct, SessionTableName);

                    if (recheck.IsSuccess && recheck.Value > 0)
                    {
                        Volatile.Write(ref _schemaEnsured, 1);
                    }
                    else
                    {
                        LogWarn($"确保会话表存在失败（本次登录可能失败）: {result.Message}");
                    }
                }
            }
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    #region 策略

    public async Task<SessionPolicy> GetPolicyAsync(CancellationToken ct = default)
    {
        if (DateTime.UtcNow - _policyCachedAtUtc < PolicyCacheTtl)
            return _cachedPolicy;

        await _policyLock.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow - _policyCachedAtUtc < PolicyCacheTtl)
                return _cachedPolicy;

            var policy = SessionPolicy.CreateDefault();
            using (_guard.Bypass())
            {
                var rows = await _db.QueryAsync<ConfigRow>(
                    "SELECT config_key, config_value FROM nc_config_system_standards WHERE category = $1",
                    ct, SessionConstants.ConfigCategory);

                if (rows.IsSuccess && rows.Value is { Count: > 0 })
                {
                    var map = rows.Value
                        .Where(r => !string.IsNullOrWhiteSpace(r.ConfigKey))
                        .GroupBy(r => r.ConfigKey, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => (int)g.Last().ConfigValue, StringComparer.OrdinalIgnoreCase);

                    int Get(string key, int fallback) =>
                        map.TryGetValue(key, out var value) && value > 0 ? value : fallback;

                    policy = new SessionPolicy
                    {
                        Mode = Get(SessionConstants.ConfigKeyPolicy, SessionConstants.DefaultPolicy),
                        MaxDesktop = Get(SessionConstants.ConfigKeyMaxDesktop, SessionConstants.DefaultMaxDesktop),
                        MaxMobile = Get(SessionConstants.ConfigKeyMaxMobile, SessionConstants.DefaultMaxMobile),
                        HeartbeatDesktopSeconds = Get(SessionConstants.ConfigKeyHeartbeatDesktop, SessionConstants.DefaultHeartbeatDesktopSeconds),
                        HeartbeatMobileSeconds = Get(SessionConstants.ConfigKeyHeartbeatMobile, SessionConstants.DefaultHeartbeatMobileSeconds),
                        OnlineWindowSeconds = Get(SessionConstants.ConfigKeyOnlineWindow, SessionConstants.DefaultOnlineWindowSeconds),
                        AbsoluteDays = Get(SessionConstants.ConfigKeyAbsoluteDays, SessionConstants.DefaultAbsoluteDays),
                        GraceSeconds = Get(SessionConstants.ConfigKeyGraceSeconds, SessionConstants.DefaultGraceSeconds)
                    };
                }
                else if (rows.IsFailure)
                {
                    // 配置表缺失/不可读不阻断登录：用默认策略，只记 Warn
                    LogWarn($"读取会话策略失败，使用默认值: {rows.Message}");
                }
            }

            _cachedPolicy = policy;
            _policyCachedAtUtc = DateTime.UtcNow;
            return policy;
        }
        finally
        {
            _policyLock.Release();
        }
    }

    #endregion

    #region 登录裁决

    public async Task<Result<SessionCreationResult>> CreateAsync(
        int userId,
        string deviceId,
        string deviceKind,
        string deviceName,
        string appVersion,
        string? clientIp,
        CancellationToken ct = default)
    {
        try
        {
            await EnsureSchemaAsync(ct);
            var policy = await GetPolicyAsync(ct);
            using var bypass = _guard.Bypass();
            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            // 同一账号并发登录串行化：否则两台设备可能都查不到活动会话 → 都插入 → 双活
            var lockResult = await _db.ExecuteNonQueryAsync("SELECT pg_advisory_xact_lock($1::bigint)", ct, userId);
            if (lockResult.IsFailure)
                return Result.Failure<SessionCreationResult>(lockResult.ErrorCode!, lockResult.Message!);

            // 先清理已到期的活动行（只影响本人，代价可忽略）
            await _db.ExecuteNonQueryAsync(
                @"UPDATE nc_sys_user_sessions
                  SET status = 'expired', end_reason = $1, ended_at = now()
                  WHERE user_id = $2 AND status = 'active' AND expires_at <= now()",
                ct, SessionConstants.EndReasonExpired, userId);

            var activeResult = await _db.QueryAsync<ActiveSessionRow>(
                @"SELECT session_id, device_kind, device_id, last_seen_at,
                         (last_seen_at >= now() - make_interval(secs => $2::double precision)) AS is_online
                  FROM nc_sys_user_sessions
                  WHERE user_id = $1 AND status = 'active'
                  FOR UPDATE",
                ct, userId, policy.OnlineWindowSeconds);

            if (activeResult.IsFailure)
                return Result.Failure<SessionCreationResult>(activeResult.ErrorCode!, activeResult.Message!);

            var active = activeResult.Value ?? new List<ActiveSessionRow>();
            var toReplace = SelectSessionsToReplace(active, policy, deviceId, deviceKind);

            if (toReplace.Count > 0)
            {
                var ids = toReplace.Select(s => s.SessionId).ToArray();
                var updateResult = await _db.ExecuteNonQueryAsync(
                    @"UPDATE nc_sys_user_sessions
                      SET status = 'replaced', end_reason = $1, ended_at = now()
                      WHERE session_id = ANY($2) AND status = 'active'",
                    ct, SessionConstants.EndReasonOtherDeviceLogin, ids);

                if (updateResult.IsFailure)
                    return Result.Failure<SessionCreationResult>(updateResult.ErrorCode!, updateResult.Message!);

                foreach (var id in ids)
                    await NotifyAsync(id, SessionConstants.EndReasonOtherDeviceLogin, ct);
            }

            var newSessionId = Guid.NewGuid();
            var insertResult = await _db.QuerySingleAsync<InsertedSessionRow>(
                @"INSERT INTO nc_sys_user_sessions
                      (session_id, user_id, device_id, device_kind, device_name, app_version,
                       login_at, last_seen_at, expires_at, status, client_ip)
                  VALUES ($1, $2, $3, $4, $5, $6,
                          now(), now(), now() + make_interval(days => $7::int), 'active', $8)
                  RETURNING session_id, expires_at",
                ct,
                newSessionId, userId, deviceId, deviceKind, deviceName, appVersion,
                policy.AbsoluteDays, clientIp ?? string.Empty);

            if (insertResult.IsFailure || insertResult.Value == null)
                return Result.Failure<SessionCreationResult>(
                    insertResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    insertResult.Message ?? "创建登录会话失败");

            await tx.CommitAsync(ct);

            await CleanupOldSessionsAsync(ct);

            LogInfo($"创建登录会话成功: UserId={userId}, Kind={deviceKind}, Replaced={toReplace.Count}");
            Logger.LogSecurity("登录会话建立", ("UserId", userId), ("DeviceKind", deviceKind),
                ("DeviceName", deviceName), ("Replaced", toReplace.Count));

            return Result.Success(new SessionCreationResult
            {
                SessionId = insertResult.Value.SessionId,
                ExpiresAt = insertResult.Value.ExpiresAt,
                ReplacedCount = toReplace.Count
            });
        }
        catch (Exception ex)
        {
            LogError(ex, "创建登录会话");
            return Result.FromException<SessionCreationResult>(ex);
        }
    }

    /// <summary>
    /// 按策略选出需要结束的活动会话：
    /// - 单会话：全部；
    /// - 按端配额：同端离线行一律让位，同端在线行保留（配额-1）条最新的；
    /// - 多端并存：只替换同一安装设备的旧会话（同机重复登录不留垃圾行）。
    /// </summary>
    private static List<ActiveSessionRow> SelectSessionsToReplace(
        List<ActiveSessionRow> active, SessionPolicy policy, string deviceId, string deviceKind)
    {
        var result = new List<ActiveSessionRow>();
        if (active.Count == 0) return result;

        switch (policy.Mode)
        {
            case SessionConstants.PolicyMulti:
                result.AddRange(active.Where(s => string.Equals(s.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)));
                return result;

            case SessionConstants.PolicySingle:
                result.AddRange(active);
                return result;

            default: // PolicyPerDevice
                var sameKind = active
                    .Where(s => string.Equals(s.DeviceKind, deviceKind, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var max = string.Equals(deviceKind, SessionConstants.DeviceKindAndroid, StringComparison.OrdinalIgnoreCase)
                    ? policy.MaxMobile
                    : policy.MaxDesktop;
                var keepNewest = Math.Max(0, max - 1);

                var online = sameKind.Where(s => s.IsOnline)
                    .OrderByDescending(s => s.LastSeenAt)
                    .ToList();
                result.AddRange(sameKind.Where(s => !s.IsOnline));
                result.AddRange(online.Skip(keepNewest));

                // 同一设备重复登录：无论配额多少都替换自己的旧会话
                result.AddRange(active.Where(s =>
                    string.Equals(s.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
                    && !result.Contains(s)));

                return result
                    .GroupBy(s => s.SessionId)
                    .Select(g => g.First())
                    .ToList();
        }
    }

    #endregion

    #region 校验与心跳

    public async Task<Result<SessionValidationResult>> ValidateAsync(Guid sessionId, string deviceId, CancellationToken ct = default)
    {
        try
        {
            await EnsureSchemaAsync(ct);
            using var bypass = _guard.Bypass();
            var record = await _db.QuerySingleAsync<ValidationRow>(
                @"SELECT session_id, user_id, device_id, status, end_reason, expires_at,
                         (expires_at <= now()) AS is_expired
                  FROM nc_sys_user_sessions
                  WHERE session_id = $1",
                ct, sessionId);

            if (record.IsFailure)
                return Result.Failure<SessionValidationResult>(record.ErrorCode!, record.Message!);

            if (record.Value == null)
                return Result.Success(new SessionValidationResult { Status = SessionValidationStatus.NotFound });

            var row = record.Value;

            if (!string.Equals(row.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                // 会话令牌被复制到其他设备：结束该会话，要求重新登录
                await EndExpiredAsync(sessionId, SessionConstants.EndReasonDeviceMismatch, ct);
                return Result.Success(new SessionValidationResult
                {
                    Status = SessionValidationStatus.NotFound,
                    EndReason = SessionConstants.EndReasonDeviceMismatch
                });
            }

            if (string.Equals(row.Status, SessionConstants.StatusActive, StringComparison.OrdinalIgnoreCase))
            {
                if (row.IsExpired)
                {
                    await EndExpiredAsync(sessionId, SessionConstants.EndReasonExpired, ct);
                    return Result.Success(new SessionValidationResult
                    {
                        Status = SessionValidationStatus.Expired,
                        EndReason = SessionConstants.EndReasonExpired,
                        ExpiresAt = row.ExpiresAt
                    });
                }

                return Result.Success(new SessionValidationResult
                {
                    Status = SessionValidationStatus.Active,
                    ExpiresAt = row.ExpiresAt
                });
            }

            return Result.Success(new SessionValidationResult
            {
                Status = MapStatus(row.Status),
                EndReason = row.EndReason,
                ExpiresAt = row.ExpiresAt
            });
        }
        catch (Exception ex)
        {
            LogError(ex, "校验登录会话");
            return Result.FromException<SessionValidationResult>(ex);
        }
    }

    public async Task<Result<SessionTouchResult>> TouchAsync(Guid sessionId, CancellationToken ct = default)
    {
        try
        {
            using var bypass = _guard.Bypass();
            var update = await _db.ExecuteNonQueryAsync(
                @"UPDATE nc_sys_user_sessions
                  SET last_seen_at = now()
                  WHERE session_id = $1 AND status = 'active' AND expires_at > now()",
                ct, sessionId);

            if (update.IsFailure)
                return Result.Failure<SessionTouchResult>(update.ErrorCode!, update.Message!);

            if (update.Value > 0)
            {
                return Result.Success(new SessionTouchResult
                {
                    IsActive = true,
                    Status = SessionValidationStatus.Active
                });
            }

            var record = await _db.QuerySingleAsync<ValidationRow>(
                @"SELECT session_id, user_id, device_id, status, end_reason, expires_at,
                         (expires_at <= now()) AS is_expired
                  FROM nc_sys_user_sessions
                  WHERE session_id = $1",
                ct, sessionId);

            if (record.IsFailure)
                return Result.Failure<SessionTouchResult>(record.ErrorCode!, record.Message!);

            if (record.Value == null)
            {
                return Result.Success(new SessionTouchResult
                {
                    IsActive = false,
                    Status = SessionValidationStatus.NotFound
                });
            }

            var row = record.Value;
            var status = string.Equals(row.Status, SessionConstants.StatusActive, StringComparison.OrdinalIgnoreCase)
                ? (row.IsExpired ? SessionValidationStatus.Expired : SessionValidationStatus.Active)
                : MapStatus(row.Status);

            return Result.Success(new SessionTouchResult
            {
                IsActive = status == SessionValidationStatus.Active,
                Status = status,
                EndReason = row.EndReason
                    ?? (status == SessionValidationStatus.Expired ? SessionConstants.EndReasonExpired : null)
            });
        }
        catch (Exception ex)
        {
            LogError(ex, "刷新会话心跳");
            return Result.FromException<SessionTouchResult>(ex);
        }
    }

    #endregion

    #region 结束会话

    public async Task<Result> RevokeAsync(Guid sessionId, string reason, int? byUserId = null, CancellationToken ct = default)
    {
        try
        {
            using var bypass = _guard.Bypass();
            var status = string.Equals(reason, SessionConstants.EndReasonUserLogout, StringComparison.OrdinalIgnoreCase)
                ? SessionConstants.StatusLogout
                : SessionConstants.StatusKicked;

            var result = await _db.ExecuteNonQueryAsync(
                @"UPDATE nc_sys_user_sessions
                  SET status = $2, end_reason = $1, ended_by = $3, ended_at = now()
                  WHERE session_id = $4 AND status = 'active'",
                ct, reason, status, byUserId, sessionId);

            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);

            if (result.Value > 0)
            {
                await NotifyAsync(sessionId, reason, ct);
                Logger.LogSecurity("登录会话结束", ("SessionId", sessionId), ("Reason", reason), ("ByUserId", byUserId ?? 0));
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            LogError(ex, "结束登录会话");
            return Result.FromException(ex);
        }
    }

    public async Task<Result<int>> RevokeAllForUserAsync(int userId, string reason, int? byUserId = null,
        Guid? exceptSessionId = null, CancellationToken ct = default)
    {
        try
        {
            using var bypass = _guard.Bypass();
            var status = string.Equals(reason, SessionConstants.EndReasonUserLogout, StringComparison.OrdinalIgnoreCase)
                ? SessionConstants.StatusLogout
                : SessionConstants.StatusKicked;

            var sql = exceptSessionId.HasValue
                ? @"UPDATE nc_sys_user_sessions
                    SET status = $2, end_reason = $1, ended_by = $3, ended_at = now()
                    WHERE user_id = $4 AND status = 'active' AND session_id <> $5
                    RETURNING session_id"
                : @"UPDATE nc_sys_user_sessions
                    SET status = $2, end_reason = $1, ended_by = $3, ended_at = now()
                    WHERE user_id = $4 AND status = 'active'
                    RETURNING session_id";

            var result = exceptSessionId.HasValue
                ? await _db.QueryAsync<SessionIdRow>(sql, ct, reason, status, byUserId, userId, exceptSessionId.Value)
                : await _db.QueryAsync<SessionIdRow>(sql, ct, reason, status, byUserId, userId);

            if (result.IsFailure)
                return Result.Failure<int>(result.ErrorCode!, result.Message!);

            var ids = result.Value ?? new List<SessionIdRow>();
            foreach (var row in ids)
                await NotifyAsync(row.SessionId, reason, ct);

            if (ids.Count > 0)
                Logger.LogSecurity("结束用户全部会话", ("UserId", userId), ("Reason", reason), ("Count", ids.Count), ("ByUserId", byUserId ?? 0));

            return Result.Success(ids.Count);
        }
        catch (Exception ex)
        {
            LogError(ex, "结束用户全部会话");
            return Result.FromException<int>(ex);
        }
    }

    public async Task<Result<int>> RevokeAllActiveAsync(string reason, int? byUserId = null,
        Guid? exceptSessionId = null, IReadOnlyCollection<int>? organizationIds = null,
        CancellationToken ct = default)
    {
        try
        {
            using var bypass = _guard.Bypass();
            var status = string.Equals(reason, SessionConstants.EndReasonUserLogout, StringComparison.OrdinalIgnoreCase)
                ? SessionConstants.StatusLogout
                : SessionConstants.StatusKicked;

            var parameters = new List<object?> { reason, status, byUserId };
            var conditions = new List<string> { "s.user_id = u.id", "s.status = 'active'", "s.expires_at > now()" };

            if (exceptSessionId.HasValue)
            {
                parameters.Add(exceptSessionId.Value);
                conditions.Add($"s.session_id <> ${parameters.Count}");
            }

            if (organizationIds != null)
            {
                parameters.Add(organizationIds.ToArray());
                conditions.Add($"u.organization_id = ANY(${parameters.Count})");
            }

            var sql = $@"UPDATE nc_sys_user_sessions s
                         SET status = $2, end_reason = $1, ended_by = $3, ended_at = now()
                         FROM nc_sys_users u
                         WHERE {string.Join(" AND ", conditions)}
                         RETURNING s.session_id";

            var result = await _db.QueryAsync<SessionIdRow>(sql, ct, parameters.ToArray());

            if (result.IsFailure)
                return Result.Failure<int>(result.ErrorCode!, result.Message!);

            var ids = result.Value ?? new List<SessionIdRow>();
            foreach (var row in ids)
                await NotifyAsync(row.SessionId, reason, ct);

            if (ids.Count > 0)
                Logger.LogSecurity("全部会话强制下线", ("Reason", reason), ("Count", ids.Count), ("ByUserId", byUserId ?? 0));

            return Result.Success(ids.Count);
        }
        catch (Exception ex)
        {
            LogError(ex, "全部会话强制下线");
            return Result.FromException<int>(ex);
        }
    }

    #endregion

    #region 在线列表

    public async Task<Result<List<UserSessionRecord>>> ListActiveAsync(
        IReadOnlyCollection<int>? organizationIds = null, CancellationToken ct = default)
    {
        try
        {
            await EnsureSchemaAsync(ct);
            var policy = await GetPolicyAsync(ct);
            using var bypass = _guard.Bypass();

            var sql = @"SELECT s.id, s.session_id, s.user_id,
                               u.username, u.full_name, o.name AS organization_name,
                               s.device_id, s.device_kind, s.device_name, s.app_version,
                               s.login_at, s.last_seen_at, s.expires_at, s.status,
                               s.end_reason, s.ended_by, s.ended_at, s.client_ip,
                               (s.last_seen_at >= now() - make_interval(secs => $1::double precision)) AS is_online
                        FROM nc_sys_user_sessions s
                        INNER JOIN nc_sys_users u ON u.id = s.user_id
                        LEFT JOIN nc_sys_organizations o ON o.id = u.organization_id
                        WHERE s.status = 'active' AND s.expires_at > now()";

            var parameters = new List<object?> { policy.OnlineWindowSeconds };
            if (organizationIds != null)
            {
                sql += " AND u.organization_id = ANY($2)";
                parameters.Add(organizationIds.ToArray());
            }

            sql += " ORDER BY is_online DESC, s.last_seen_at DESC LIMIT 500";

            var result = await _db.QueryAsync<UserSessionRecord>(sql, ct, parameters.ToArray());
            if (result.IsFailure)
                return Result.Failure<List<UserSessionRecord>>(result.ErrorCode!, result.Message!);

            return Result.Success(result.Value ?? new List<UserSessionRecord>());
        }
        catch (Exception ex)
        {
            LogError(ex, "查询在线会话");
            return Result.FromException<List<UserSessionRecord>>(ex);
        }
    }

    #endregion

    #region 私有

    private Task<Result<int>> EndExpiredAsync(Guid sessionId, string reason, CancellationToken ct) =>
        _db.ExecuteNonQueryAsync(
            @"UPDATE nc_sys_user_sessions
              SET status = 'expired', end_reason = $1, ended_at = now()
              WHERE session_id = $2 AND status = 'active'",
            ct, reason, sessionId);

    /// <summary>终态会话按保留期清理（每 12 小时最多一次，避免会话表无限增长）</summary>
    private async Task CleanupOldSessionsAsync(CancellationToken ct)
    {
        lock (_cleanupGate)
        {
            if (DateTime.UtcNow - _lastCleanupUtc < TimeSpan.FromHours(12)) return;
            _lastCleanupUtc = DateTime.UtcNow;
        }

        try
        {
            var result = await _db.ExecuteNonQueryAsync(
                @"DELETE FROM nc_sys_user_sessions
                  WHERE status <> 'active'
                    AND ended_at IS NOT NULL
                    AND ended_at < now() - make_interval(days => $1::int)",
                ct, SessionConstants.RetentionDays);

            if (result.IsFailure)
                LogWarn($"清理过期会话记录失败: {result.Message}");
        }
        catch (Exception ex)
        {
            LogWarn($"清理过期会话记录异常: {ex.Message}");
        }
    }

    private Task<Result<int>> NotifyAsync(Guid sessionId, string reason, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { sessionId, reason });
        return _db.ExecuteNonQueryAsync("SELECT pg_notify($1, $2)", ct, NotifyChannel, payload);
    }

    private static SessionValidationStatus MapStatus(string? status) => status?.ToLowerInvariant() switch
    {
        SessionConstants.StatusReplaced => SessionValidationStatus.Replaced,
        SessionConstants.StatusKicked => SessionValidationStatus.Kicked,
        SessionConstants.StatusLogout => SessionValidationStatus.LoggedOut,
        SessionConstants.StatusExpired => SessionValidationStatus.Expired,
        SessionConstants.StatusActive => SessionValidationStatus.Active,
        _ => SessionValidationStatus.NotFound
    };

    private sealed class ConfigRow
    {
        public string ConfigKey { get; set; } = string.Empty;
        public decimal ConfigValue { get; set; }
    }

    private sealed class ActiveSessionRow
    {
        public Guid SessionId { get; set; }
        public string DeviceKind { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public DateTime LastSeenAt { get; set; }
        public bool IsOnline { get; set; }
    }

    private sealed class InsertedSessionRow
    {
        public Guid SessionId { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    private sealed class ValidationRow
    {
        public Guid SessionId { get; set; }
        public int UserId { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? EndReason { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool IsExpired { get; set; }
    }

    private sealed class SessionIdRow
    {
        public Guid SessionId { get; set; }
    }

    #endregion
}
