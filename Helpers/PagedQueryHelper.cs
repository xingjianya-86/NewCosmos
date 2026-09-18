using NewCosmos.Models.Results;
using NewCosmos.Services.Database;

namespace NewCosmos.Helpers;

/// <summary>
/// SQL 动态条件构建器：按添加顺序生成 $n 位置参数。
/// 条件模板中的 {0}{1}… 依序映射本条新增参数（同一占位符可重复出现以复用同一参数）；
/// 无参数调用直接把模板原文作为条件（如常量过滤列）。
/// 模板必须是代码内常量字符串，禁止携带用户输入——用户输入只能经 values 走位置参数。
/// </summary>
public sealed class SqlConditionBuilder
{
    private readonly List<string> _conditions = new();
    private readonly List<object?> _parameters = new();

    /// <summary>已收集的条件参数个数（分页 LIMIT/OFFSET 的占位序号接续此值）</summary>
    public int ParamCount => _parameters.Count;

    /// <summary>追加一个条件。template 含 {0}{1}… 时按序替换为 $n。</summary>
    public SqlConditionBuilder Add(string template, params object?[] values)
    {
        var baseIndex = _parameters.Count;
        foreach (var v in values)
            _parameters.Add(v);

        if (values.Length == 0)
        {
            _conditions.Add(template);
            return this;
        }

        var args = new object[values.Length];
        for (var i = 0; i < values.Length; i++)
            args[i] = "$" + (baseIndex + i + 1);
        _conditions.Add(string.Format(template, args));
        return this;
    }

    /// <summary>condition 为 true 时才追加条件（占位序号仅在真正追加时递增）。</summary>
    public SqlConditionBuilder AddIf(bool condition, string template, params object?[] values)
        => condition ? Add(template, values) : this;

    /// <summary>生成 " AND c1 AND c2…"（无条件返回空串。适用于基线 SQL 已自带 WHERE）</summary>
    public string ToAndClause()
        => _conditions.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", _conditions);

    /// <summary>生成 " WHERE c1 AND c2…"（无条件返回空串）</summary>
    public string ToWhereClause()
        => _conditions.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", _conditions);

    /// <summary>当前全部条件参数（供 COUNT / 页查询共用）</summary>
    public object?[] GetParameters() => _parameters.ToArray();
}

/// <summary>
/// 标准分页查询执行器：动态条件拼装 → COUNT（失败即透传）→ 追加 LIMIT/OFFSET → 页查询（失败即透传）→ PagedResult。
/// countSql 使用 {WHERE} 占位；querySql 使用 {WHERE} 与 {PAGE} 占位，
/// {PAGE} 展开为 " LIMIT $a OFFSET $b"，占位序号接续条件参数之后。
/// </summary>
public static class PagedQueryHelper
{
    public static async Task<Result<PagedResult<T>>> RunAsync<T>(
        IDatabaseService db,
        string countSqlTemplate,
        string querySqlTemplate,
        SqlConditionBuilder conditions,
        int pageIndex,
        int pageSize,
        CancellationToken ct = default)
    {
        var whereClause = conditions.ToWhereClause();
        var condParams = conditions.GetParameters();
        var pageParams = new List<object?>(condParams) { pageSize, (pageIndex - 1) * pageSize };
        var pageClause = $" LIMIT ${ParamCountFor(conditions)} OFFSET ${conditions.ParamCount + 2}";

        var countSql = countSqlTemplate.Replace("{WHERE}", whereClause);
        var countResult = await db.ExecuteScalarAsync<long>(countSql, ct, condParams);
        if (countResult.IsFailure)
            return Result.Failure<PagedResult<T>>(countResult.ErrorCode!, countResult.Message!);

        var querySql = querySqlTemplate.Replace("{WHERE}", whereClause).Replace("{PAGE}", pageClause);
        var listResult = await db.QueryAsync<T>(querySql, ct, pageParams.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<T>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<T>.FromList(
            listResult.Value, pageIndex, pageSize, Convert.ToInt32(countResult.Value)));
    }

    private static int ParamCountFor(SqlConditionBuilder conditions) => conditions.ParamCount + 1;
}
