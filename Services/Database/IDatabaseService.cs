using NewCosmos.Models.Results;

namespace NewCosmos.Services.Database;

/// <summary>
/// 数据库服务接口
/// 事务模型：环境事务（AsyncLocal，按异步流隔离）。
/// BeginTransactionAsync 之后，同一异步流上的所有查询自动加入事务；
/// 不同异步流（并发操作）互不干扰，各自使用独立的池化连接。
/// </summary>
public interface IDatabaseService
{
    /// <summary>
    /// 查询单条记录
    /// </summary>
    Task<Result<T>> QuerySingleAsync<T>(string sql, CancellationToken ct = default, params object?[]? parameters);

    /// <summary>
    /// 查询多条记录
    /// </summary>
    Task<Result<List<T>>> QueryAsync<T>(string sql, CancellationToken ct = default, params object?[]? parameters);

    /// <summary>
    /// 执行非查询操作
    /// </summary>
    Task<Result<int>> ExecuteNonQueryAsync(string sql, CancellationToken ct = default, params object?[]? parameters);

    /// <summary>
    /// 执行返回标量值的操作（历史 API：结果强转 long，无行时返回 Success(0)）。
    /// 新代码请使用泛型重载 ExecuteScalarAsync&lt;T&gt;。
    /// </summary>
    Task<Result<long>> ExecuteScalarAsync(string sql, CancellationToken ct = default, params object?[]? parameters);

    /// <summary>
    /// 执行返回标量值的操作（泛型版）。
    /// 无行或 SQL NULL 时返回 Success(default)，可与"有值"明确区分。
    /// </summary>
    Task<Result<T?>> ExecuteScalarAsync<T>(string sql, CancellationToken ct = default, params object?[]? parameters);

    /// <summary>
    /// 在当前异步流上开始事务（同一异步流重复开启会抛 InvalidOperationException）
    /// </summary>
    Task BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>
    /// 提交当前异步流上的事务
    /// </summary>
    Task CommitTransactionAsync(CancellationToken ct = default);

    /// <summary>
    /// 回滚当前异步流上的事务（无活动事务时记录警告并返回，不抛异常，
    /// 避免 catch 块中的回滚掩盖原始异常）
    /// </summary>
    Task RollbackTransactionAsync(CancellationToken ct = default);

    /// <summary>
    /// 当前异步流上是否有活动事务
    /// </summary>
    bool HasTransaction { get; }

    /// <summary>
    /// 开始事务并返回作用域对象（推荐的新写法，未 Commit 自动回滚）
    /// </summary>
    Task<ITransactionScope> BeginTransactionScopeAsync(CancellationToken ct = default);

    /// <summary>
    /// 测试数据库连接
    /// </summary>
    Task<Result<bool>> TestConnectionAsync(CancellationToken ct = default);
}
