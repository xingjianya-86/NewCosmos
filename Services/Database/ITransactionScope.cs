namespace NewCosmos.Services.Database;

/// <summary>
/// 事务作用域：由 IDatabaseService.BeginTransactionScopeAsync 创建。
/// 作用域存活期间，当前异步流上的所有查询自动加入本事务。
/// 用法：
/// <code>
/// await using var tx = await _db.BeginTransactionScopeAsync(ct);
/// // ... 一系列 _db.ExecuteNonQueryAsync / QueryAsync 自动挂到事务上 ...
/// await tx.CommitAsync(ct);   // 未 Commit 时 DisposeAsync 自动回滚
/// </code>
/// </summary>
public interface ITransactionScope : IAsyncDisposable
{
    /// <summary>
    /// 提交事务
    /// </summary>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>
    /// 显式回滚事务（不调用时，未提交的作用域在 DisposeAsync 中自动回滚）
    /// </summary>
    Task RollbackAsync(CancellationToken ct = default);
}
