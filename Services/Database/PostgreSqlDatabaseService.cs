using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using NewCosmos.Constants;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using Npgsql;

namespace NewCosmos.Services.Database;

/// <summary>
/// PostgreSQL 数据库服务实现
/// 使用 $1, $2, $3... 位置参数（符合 AGENTS.md 规范）
///
/// 事务模型：AsyncLocal 环境事务。
/// - 无事务：每次查询从 Npgsql 连接池取独立连接，用完归还。
/// - 有事务：事务作用域（连接+事务）存放在 AsyncLocal 中，只对当前异步流可见；
///   并发的其他操作各走各的连接，互不干扰。
/// - 同一事务内的命令经 CommandGate 串行化（Npgsql 单连接不支持并发命令）。
/// </summary>
public class PostgreSqlDatabaseService : BaseService, IDatabaseService, IDisposable
{
    protected override string ServiceName => "PostgreSqlDatabaseService";

    private readonly DatabaseOptions _options;
    private readonly PerformanceOptions _perfOptions;
    private readonly string _connectionString;
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache = new();

    /// <summary>编译后的属性赋值委托缓存（替代反射 SetValue，热路径性能关键）</summary>
    private static readonly ConcurrentDictionary<(Type Type, string Name), Action<object, object?>> _setterCache = new();

    /// <summary>已记录警告的"类型+列名"集合（每查询类型每列只记一次，避免日志膨胀）</summary>
    private static readonly ConcurrentDictionary<string, bool> _warnedUnmappedColumns = new();
    private static ILoggerService? _staticLogger;
    private bool _disposed;

    /// <summary>
    /// 当前异步流的环境事务。写入必须发生在非 async 方法帧内（见 BeginTransactionAsync 注释）。
    /// </summary>
    private readonly AsyncLocal<AmbientTransaction?> _ambient = new();

    public PostgreSqlDatabaseService(DatabaseOptions options, PerformanceOptions perfOptions, ILoggerService logger)
        : base(logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _perfOptions = perfOptions ?? throw new ArgumentNullException(nameof(perfOptions));
        _connectionString = BuildConnectionString();
        _staticLogger ??= logger;
    }

    public bool HasTransaction => _ambient.Value?.IsActive ?? false;

    #region 环境事务载体

    /// <summary>
    /// 环境事务：一个作用域独占一条连接和一个事务。
    /// IsActive 是唯一权威状态——AsyncLocal 中残留的已完成作用域是无害的
    /// （提交/回滚发生在更深的 async 帧时，置 null 不会传播回调用方）。
    /// </summary>
    private sealed class AmbientTransaction
    {
        public NpgsqlConnection? Connection;
        public NpgsqlTransaction? Transaction;

        /// <summary>事务内命令串行门：同一连接上不允许并发命令</summary>
        public readonly SemaphoreSlim CommandGate = new(1, 1);

        private int _state; // 0=初始化中 1=活动 2=已完成
        public bool IsActive => Volatile.Read(ref _state) == 1;
        public void MarkActive() => Interlocked.Exchange(ref _state, 1);

        /// <summary>标记完成；返回 false 表示已被别处完成（防止重复提交/回滚）</summary>
        public bool TryMarkCompleted() => Interlocked.Exchange(ref _state, 2) == 1;
    }

    private AmbientTransaction? GetActiveScope()
    {
        var scope = _ambient.Value;
        return scope is { IsActive: true } ? scope : null;
    }

    #endregion

    #region 连接管理

    /// <summary>
    /// 从连接池获取一条新连接（带重试）。与事务无关——事务连接由作用域持有。
    /// </summary>
    private async Task<NpgsqlConnection> GetPooledConnectionAsync(CancellationToken ct = default)
    {
        var maxRetries = Math.Max(1, _perfOptions.MaxRetries);
        var retryCount = 0;

        while (retryCount < maxRetries)
        {
            NpgsqlConnection? connection = null;
            try
            {
                connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(ct);
                return connection;
            }
            catch (Exception ex) when (IsConnectionException(ex) && retryCount < maxRetries - 1)
            {
                connection?.Dispose();
                retryCount++;
                Logger.Warn($"数据库连接失败（{ex.GetType().Name}），第 {retryCount} 次重试");

                var delay = _perfOptions.ExponentialBackoff
                    ? _perfOptions.RetryDelayMilliseconds * (int)Math.Pow(2, retryCount - 1)
                    : _perfOptions.RetryDelayMilliseconds;

                await Task.Delay(delay, ct);
            }
            catch (Exception ex)
            {
                connection?.Dispose();
                Logger.LogError(ex, "获取数据库连接失败");
                throw new BusinessException(ErrorCodes.DB_CONNECTION_FAILED, $"数据库连接失败: {ex.Message}");
            }
        }

        throw new BusinessException(ErrorCodes.DB_CONNECTION_FAILED, "数据库连接超时");
    }

    private string BuildConnectionString()
    {
        var appName = string.IsNullOrWhiteSpace(_options.ApplicationName) ? "NewCosmos" : _options.ApplicationName;
        return $"Host={_options.Host};Port={_options.Port};Database={_options.DatabaseName};" +
               $"Username={_options.Username};Password={_options.Password};" +
               $"Timeout={_options.ConnectionTimeout};Command Timeout={_options.CommandTimeout};" +
               $"Pooling=true;Minimum Pool Size={_options.MinPoolSize};Maximum Pool Size={_options.MaxPoolSize};" +
               $"SSL Mode={_options.SslMode};Trust Server Certificate={_options.TrustServerCertificate};" +
               $"Include Error Detail={_options.IncludeErrorDetail};" +
               $"Application Name={appName}";
    }

    #endregion

    #region 查询操作

    public async Task<Result<T>> QuerySingleAsync<T>(string sql, CancellationToken ct = default, params object?[]? parameters)
    {
        if (string.IsNullOrEmpty(sql))
            return Result.Failure<T>(ErrorCodes.VALIDATION_FAILED, "SQL 语句不能为空");

        var (scope, gateAcquired, connection) = (GetActiveScope(), false, (NpgsqlConnection?)null);
        var sw = Stopwatch.StartNew();
        try
        {
            (scope, gateAcquired, connection) = await AcquireConnectionAsync(scope, ct);
            await using var command = BuildCommand(connection!, scope?.Transaction, sql, parameters);
            await using var reader = await command.ExecuteReaderAsync(ct);

            if (await reader.ReadAsync(ct))
            {
                var columnMap = BuildColumnMap<T>(reader);
                return Result.Success<T>(MapRow<T>(reader, columnMap)!);
            }

            return Result.Success<T>(default!);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "查询单条记录失败");
            return Result.FromException<T>(ex);
        }
        finally
        {
            sw.Stop();
            if (sw.ElapsedMilliseconds > _perfOptions.SlowOperationThresholdMs)
                Logger.LogSlowQuery(sql, sw.ElapsedMilliseconds, 1);
            await ReleaseConnectionAsync(scope, gateAcquired, connection);
        }
    }

    public async Task<Result<List<T>>> QueryAsync<T>(string sql, CancellationToken ct = default, params object?[]? parameters)
    {
        if (string.IsNullOrEmpty(sql))
            return Result.Failure<List<T>>(ErrorCodes.VALIDATION_FAILED, "SQL 语句不能为空");

        var (scope, gateAcquired, connection) = (GetActiveScope(), false, (NpgsqlConnection?)null);
        var sw = Stopwatch.StartNew();
        try
        {
            (scope, gateAcquired, connection) = await AcquireConnectionAsync(scope, ct);
            await using var command = BuildCommand(connection!, scope?.Transaction, sql, parameters);
            await using var reader = await command.ExecuteReaderAsync(ct);

            var results = new List<T>();
            var columnMap = BuildColumnMap<T>(reader);
            while (await reader.ReadAsync(ct))
            {
                results.Add(MapRow<T>(reader, columnMap)!);
            }

            return Result.Success(results);
        }
        catch (OperationCanceledException)
        {
            Logger.Warn("QueryAsync 被取消");
            return Result.Failure<List<T>>(ErrorCodes.DB_CONNECTION_FAILED, "查询被取消");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "QueryAsync 失败");
            return Result.FromException<List<T>>(ex);
        }
        finally
        {
            sw.Stop();
            if (sw.ElapsedMilliseconds > _perfOptions.SlowOperationThresholdMs)
                Logger.LogSlowQuery(sql, sw.ElapsedMilliseconds, 0);
            await ReleaseConnectionAsync(scope, gateAcquired, connection);
        }
    }

    public async Task<Result<int>> ExecuteNonQueryAsync(string sql, CancellationToken ct = default, params object?[]? parameters)
    {
        if (string.IsNullOrEmpty(sql))
            return Result.Failure<int>(ErrorCodes.VALIDATION_FAILED, "SQL 语句不能为空");

        var (scope, gateAcquired, connection) = (GetActiveScope(), false, (NpgsqlConnection?)null);
        try
        {
            (scope, gateAcquired, connection) = await AcquireConnectionAsync(scope, ct);
            await using var command = BuildCommand(connection!, scope?.Transaction, sql, parameters);
            var affectedRows = await command.ExecuteNonQueryAsync(ct);
            return Result.Success(affectedRows);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "执行非查询失败");
            return Result.FromException<int>(ex);
        }
        finally
        {
            await ReleaseConnectionAsync(scope, gateAcquired, connection);
        }
    }

    public async Task<Result<long>> ExecuteScalarAsync(string sql, CancellationToken ct = default, params object?[]? parameters)
    {
        if (string.IsNullOrEmpty(sql))
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "SQL 语句不能为空");

        var (scope, gateAcquired, connection) = (GetActiveScope(), false, (NpgsqlConnection?)null);
        try
        {
            (scope, gateAcquired, connection) = await AcquireConnectionAsync(scope, ct);
            await using var command = BuildCommand(connection!, scope?.Transaction, sql, parameters);
            var result = await command.ExecuteScalarAsync(ct);

            if (result == null)
            {
                // 历史行为：无行时返回 Success(0)。调用方无法区分"无行"与"值为 0"，
                // 新代码请改用 ExecuteScalarAsync<T>。无行对大部分标量查询（存在性检查等）
                // 是预期结果，降为 Debug 避免热路径持续落盘；排查脏数据时临时改回 Info。
                Logger.Debug($"ExecuteScalarAsync 无结果行，返回 0: {Truncate(sql)}");
                return Result.Success(0L);
            }

            return Result.Success(Convert.ToInt64(result));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "执行标量查询失败");
            return Result.FromException<long>(ex);
        }
        finally
        {
            await ReleaseConnectionAsync(scope, gateAcquired, connection);
        }
    }

    public async Task<Result<T?>> ExecuteScalarAsync<T>(string sql, CancellationToken ct = default, params object?[]? parameters)
    {
        if (string.IsNullOrEmpty(sql))
            return Result.Failure<T?>(ErrorCodes.VALIDATION_FAILED, "SQL 语句不能为空");

        var (scope, gateAcquired, connection) = (GetActiveScope(), false, (NpgsqlConnection?)null);
        try
        {
            (scope, gateAcquired, connection) = await AcquireConnectionAsync(scope, ct);
            await using var command = BuildCommand(connection!, scope?.Transaction, sql, parameters);
            var result = await command.ExecuteScalarAsync(ct);

            if (result == null || result == DBNull.Value)
                return Result.Success<T?>(default);

            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            if (targetType.IsInstanceOfType(result))
                return Result.Success<T?>((T)result);

            return Result.Success<T?>((T)Convert.ChangeType(result, targetType));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "执行标量查询失败");
            return Result.FromException<T?>(ex);
        }
        finally
        {
            await ReleaseConnectionAsync(scope, gateAcquired, connection);
        }
    }

    /// <summary>
    /// 为一次命令获取连接。
    /// 事务内：等待命令串行门后复用事务连接（等待期间事务若已结束，回落到池化连接）。
    /// 事务外：从连接池取新连接。
    /// </summary>
    private async Task<(AmbientTransaction? scope, bool gateAcquired, NpgsqlConnection connection)> AcquireConnectionAsync(
        AmbientTransaction? scope, CancellationToken ct)
    {
        if (scope != null)
        {
            await scope.CommandGate.WaitAsync(ct);
            if (scope.IsActive)
            {
                return (scope, true, scope.Connection!);
            }

            // 等待期间事务已提交/回滚：释放门，按无事务处理
            scope.CommandGate.Release();
        }

        return (null, false, await GetPooledConnectionAsync(ct));
    }

    private async ValueTask ReleaseConnectionAsync(AmbientTransaction? scope, bool gateAcquired, NpgsqlConnection? connection)
    {
        if (gateAcquired)
        {
            scope!.CommandGate.Release();
            return; // 事务连接由作用域持有，不在此处释放
        }

        if (connection != null)
        {
            try
            {
                await connection.DisposeAsync(); // 归还连接池
            }
            catch (Exception ex)
            {
                Logger.Warn($"归还连接时出错: {ex.Message}，强制清池防止脏连接复用");
                try { NpgsqlConnection.ClearPool(connection); } catch { /* 尽力而为 */ }
            }
        }
    }

    #endregion

    #region 事务管理

    // ============================================================================
    // ⚠️ 重要：BeginTransactionAsync / BeginTransactionScopeAsync / CommitTransactionAsync /
    // RollbackTransactionAsync 必须保持为【非 async】方法。
    // AsyncLocal 的写入只有发生在同步方法帧内才会保留在调用方的异步流中；
    // 一旦给这些方法加上 async 关键字，_ambient.Value 的写入会在方法返回时被
    // ExecutionContext 回滚，整个环境事务机制随之失效。
    // ============================================================================

    public Task BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_ambient.Value is { IsActive: true })
        {
            throw new InvalidOperationException("已有活动事务");
        }

        var scope = new AmbientTransaction();
        _ambient.Value = scope; // 同步帧写入：对调用方异步流可见
        return InitializeScopeAsync(scope, ct);
    }

    public Task<ITransactionScope> BeginTransactionScopeAsync(CancellationToken ct = default)
    {
        if (_ambient.Value is { IsActive: true })
        {
            throw new InvalidOperationException("已有活动事务");
        }

        var scope = new AmbientTransaction();
        _ambient.Value = scope; // 同步帧写入：对调用方异步流可见
        return CreateScopeHandleAsync(scope, ct);
    }

    private async Task<ITransactionScope> CreateScopeHandleAsync(AmbientTransaction scope, CancellationToken ct)
    {
        await InitializeScopeAsync(scope, ct);
        return new TransactionScopeHandle(this, scope);
    }

    private async Task InitializeScopeAsync(AmbientTransaction scope, CancellationToken ct)
    {
        try
        {
            scope.Connection = await GetPooledConnectionAsync(ct);
            scope.Transaction = await scope.Connection.BeginTransactionAsync(ct);
            scope.MarkActive();
            Logger.Debug("事务已开始");
        }
        catch
        {
            // 作用域永远不会变为 Active，残留在 AsyncLocal 中无害
            if (scope.Connection != null)
            {
                await scope.Connection.DisposeAsync();
                scope.Connection = null;
            }
            throw;
        }
    }

    public Task CommitTransactionAsync(CancellationToken ct = default)
    {
        var scope = _ambient.Value;
        if (scope is not { IsActive: true })
        {
            throw new InvalidOperationException("没有活动事务");
        }

        _ambient.Value = null; // 同步帧清除（深层帧调用时不传播，以 IsActive 为准）
        return CompleteScopeAsync(scope, commit: true, ct);
    }

    public Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        var scope = _ambient.Value;
        if (scope is not { IsActive: true })
        {
            // 不抛异常：回滚常出现在 catch 块中，抛异常会掩盖原始错误。
            // 典型场景：Commit 失败后 catch 里再调 Rollback——此时事务已被 Commit 路径清理。
            Logger.Warn("RollbackTransactionAsync: 没有活动事务，忽略");
            return Task.CompletedTask;
        }

        _ambient.Value = null; // 同步帧清除
        return CompleteScopeAsync(scope, commit: false, ct);
    }

    private async Task CompleteScopeAsync(AmbientTransaction scope, bool commit, CancellationToken ct)
    {
        // 与同一事务上的在途命令串行化（顺序 await 的正常代码不会在此等待）
        await scope.CommandGate.WaitAsync(CancellationToken.None);
        try
        {
            if (!scope.TryMarkCompleted())
            {
                Logger.Warn("事务已被完成，忽略重复的提交/回滚");
                return;
            }

            try
            {
                if (commit)
                {
                    await scope.Transaction!.CommitAsync(ct);
                    Logger.Debug("事务已提交");
                }
                else
                {
                    await scope.Transaction!.RollbackAsync(ct);
                    Logger.Warn("事务已回滚");
                }
            }
            finally
            {
                if (scope.Transaction != null)
                {
                    await scope.Transaction.DisposeAsync();
                    scope.Transaction = null;
                }
                if (scope.Connection != null)
                {
                    await scope.Connection.DisposeAsync(); // 归还连接池（不再 ClearPool：连接不被跨流共享，不会有脏状态）
                    scope.Connection = null;
                }
            }
        }
        finally
        {
            scope.CommandGate.Release();
        }
    }

    /// <summary>
    /// ITransactionScope 实现：包装环境事务，DisposeAsync 时未提交自动回滚。
    /// </summary>
    private sealed class TransactionScopeHandle : ITransactionScope
    {
        private readonly PostgreSqlDatabaseService _owner;
        private readonly AmbientTransaction _scope;

        public TransactionScopeHandle(PostgreSqlDatabaseService owner, AmbientTransaction scope)
        {
            _owner = owner;
            _scope = scope;
        }

        public Task CommitAsync(CancellationToken ct = default)
        {
            if (_owner._ambient.Value == _scope)
            {
                _owner._ambient.Value = null;
            }
            return _owner.CompleteScopeAsync(_scope, commit: true, ct);
        }

        public Task RollbackAsync(CancellationToken ct = default)
        {
            if (_owner._ambient.Value == _scope)
            {
                _owner._ambient.Value = null;
            }
            return _owner.CompleteScopeAsync(_scope, commit: false, ct);
        }

        public async ValueTask DisposeAsync()
        {
            if (_scope.IsActive)
            {
                _owner.Logger.Warn("事务作用域未提交即被释放，自动回滚");
                await _owner.CompleteScopeAsync(_scope, commit: false, CancellationToken.None);
            }
            if (_owner._ambient.Value == _scope)
            {
                _owner._ambient.Value = null;
            }
        }
    }

    #endregion

    #region 实体映射

    private static bool IsSimpleType(Type type)
    {
        return type.IsPrimitive || type == typeof(string) || type == typeof(decimal) ||
               type == typeof(DateTime) || type == typeof(Guid) || type == typeof(DateTimeOffset) ||
               type == typeof(byte[]) || Nullable.GetUnderlyingType(type) != null;
    }

    /// <summary>
    /// 为本次查询构建"列序号 → 属性"映射（每查询一次，替代旧实现的每行每列线性搜索）。
    /// 简单类型返回 null（直接取第 0 列）。
    /// </summary>
    private static PropertyInfo?[]? BuildColumnMap<T>(NpgsqlDataReader reader)
    {
        var type = typeof(T);
        if (IsSimpleType(type))
            return null;

        var properties = _propertyCache.GetOrAdd(type, t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite)
                .ToArray());

        var map = new PropertyInfo?[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var fieldName = reader.GetName(i);
            // 支持下划线命名到 PascalCase 的转换 (如 password_hash -> PasswordHash)
            map[i] = Array.Find(properties, p =>
                p.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase) ||
                p.Name.Equals(ConvertSnakeToPascal(fieldName), StringComparison.OrdinalIgnoreCase));

            // 未映射列一次性警告（每类型每列只记一次，防止日志膨胀）
            if (map[i] == null)
            {
                var warnKey = $"{type.Name}:{fieldName}";
                if (_warnedUnmappedColumns.TryAdd(warnKey, true))
                {
                    _staticLogger?.LogBusiness(
                        $"[MapRow] {type.Name} 缺少列 \"{fieldName}\" 的映射属性，"
                      + $"该列值将被忽略（如非预期请补充属性或 [Column] 标注）");
                }
            }
        }
        return map;
    }

    private T? MapRow<T>(NpgsqlDataReader reader, PropertyInfo?[]? columnMap)
    {
        var type = typeof(T);

        // 简单类型（string, int, decimal, bool, DateTime, long, double, byte[] 等）：取第 0 列
        if (columnMap == null)
        {
            if (reader.IsDBNull(0))
                return default;

            var value = reader.GetValue(0);
            if (value == DBNull.Value)
                return default;

            var targetType = Nullable.GetUnderlyingType(type) ?? type;
            if (targetType.IsInstanceOfType(value))
                return (T)value;
            return (T)Convert.ChangeType(value, targetType);
        }

        var entity = Activator.CreateInstance<T>();
        if (entity == null)
            return default;

        for (var i = 0; i < columnMap.Length; i++)
        {
            var property = columnMap[i];
            if (property == null)
                continue;

            var val = reader.IsDBNull(i) ? null : reader.GetValue(i);
            if (val == null)
                continue;

            try
            {
                // Npgsql 10: PostgreSQL date → DateOnly，需转换为 DateTime
                var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                object? converted;
                if (val is DateOnly dateOnly)
                {
                    var dateTime = dateOnly.ToDateTime(TimeOnly.MinValue);
                    converted = targetType == typeof(DateTime) ? dateTime : Convert.ChangeType(dateTime, targetType);
                }
                else if (targetType == typeof(string) && val != DBNull.Value)
                    converted = Convert.ToString(val);
                else if (targetType == typeof(DateTime) && val is DateTime dt)
                    converted = dt; // 避免 DateTime 走 ChangeType 的类型选择歧义
                else
                    converted = Convert.ChangeType(val, targetType);

                GetSetter(type, property.Name)(entity, converted);
            }
            catch (Exception ex)
            {
                // 保持"跳过转换失败字段"的历史行为，但留痕：字段静默变默认值曾导致排查困难
                Logger.Debug($"字段映射失败被跳过: {reader.GetName(i)} -> {typeof(T).Name}.{property.Name} ({ex.GetType().Name})");
            }
        }

        return entity;
    }

    /// <summary>
    /// 获取属性的编译赋值委托（首次编译并缓存；不可写属性返回空操作，保持旧行为静默跳过）。
    /// </summary>
    private static Action<object, object?> GetSetter(Type type, string propertyName)
        => _setterCache.GetOrAdd((type, propertyName), key =>
        {
            var property = key.Type.GetProperty(key.Name, BindingFlags.Public | BindingFlags.Instance);
            if (property?.SetMethod == null || !property.CanWrite)
                return static (_, _) => { };

            var target = Expression.Parameter(typeof(object), "target");
            var value = Expression.Parameter(typeof(object), "value");
            var convert = Expression.Convert(value, property.PropertyType);
            var call = Expression.Call(Expression.Convert(target, key.Type), property.SetMethod, convert);
            return Expression.Lambda<Action<object, object?>>(call, target, value).Compile();
        });

    private static string ConvertSnakeToPascal(string snakeCase)
    {
        if (string.IsNullOrEmpty(snakeCase))
            return snakeCase;

        return string.Concat(snakeCase.Split('_')
            .Select(word => word.Length == 0 ? word : char.ToUpper(word[0]) + word[1..]));
    }

    #endregion

    #region 辅助方法

    private static NpgsqlCommand BuildCommand(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, params object?[]? parameters)
    {
        var command = new NpgsqlCommand(sql, connection);

        if (transaction != null)
        {
            command.Transaction = transaction;
        }

        if (parameters != null)
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                var paramValue = parameters[i] ?? DBNull.Value;
                command.Parameters.Add(new NpgsqlParameter { Value = paramValue });
            }
        }

        return command;
    }

    private static string Truncate(string sql) => sql.Length <= 80 ? sql : sql[..80] + "...";

    public async Task<Result<bool>> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            return Result.Success(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "测试数据库连接失败");
            return Result.Failure<bool>(ErrorCodes.DB_CONNECTION_FAILED, ex.Message);
        }
    }

    private static bool IsConnectionException(Exception ex)
    {
        return ex is NpgsqlException
            or TimeoutException
            or global::System.Net.Sockets.SocketException
            or global::System.IO.IOException;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (!_disposed)
        {
            // 环境事务归属各自的异步流，进程退出时由连接池统一回收，这里无共享状态需要清理
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    #endregion
}
