using NewCosmos.Helpers;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// SQL 条件构建器（AGENTS §8：位置参数 $n 的单点实现）——参数序号与占位映射是注入防线，锁死。
/// </summary>
public class SqlConditionBuilderTests
{
    [Fact]
    public void 空条件_ToWhereClause为空()
    {
        var b = new SqlConditionBuilder();
        Assert.Equal(string.Empty, b.ToWhereClause());
        Assert.Equal(0, b.ParamCount);
    }

    [Fact]
    public void 单条件_生成带序号位置参数()
    {
        var b = new SqlConditionBuilder();
        b.Add("status = {0}", "Approved");

        Assert.Equal(" WHERE status = $1", b.ToWhereClause());
        Assert.Equal(1, b.ParamCount);
        Assert.Equal(new object?[] { "Approved" }, b.GetParameters());
    }

    [Fact]
    public void 多条件_按添加顺序递增序号_And连接()
    {
        var b = new SqlConditionBuilder();
        b.Add("status = {0}", "Approved")
         .Add("town = {0}", "某某镇");

        Assert.Equal(" WHERE status = $1 AND town = $2", b.ToWhereClause());
        Assert.Equal(2, b.ParamCount);
    }

    [Fact]
    public void AddIf_条件为false_不产生条件也不占参数序号()
    {
        var b = new SqlConditionBuilder();
        b.AddIf(false, "year = {0}", 2026)
         .Add("deleted_at IS NULL")
         .AddIf(true, "month = {0}", 10);

        Assert.Equal(" WHERE deleted_at IS NULL AND month = $1", b.ToWhereClause());
        Assert.Equal(1, b.ParamCount);
        Assert.Equal(new object?[] { 10 }, b.GetParameters());
    }

    [Fact]
    public void 同占位符复用_参数只入列一次()
    {
        var b = new SqlConditionBuilder();
        b.Add("head_id_card = {0} OR id_card = {0}", "X");

        Assert.Equal(" WHERE head_id_card = $1 OR id_card = $1", b.ToWhereClause());
        Assert.Equal(1, b.ParamCount);
    }

    [Fact]
    public void ToAndClause_带前导AND拼接()
    {
        var b = new SqlConditionBuilder();
        b.Add("a = {0}", 1);
        // 实现语义：拼接片段（供已有 WHERE 的 SQL 追加），带前导 " AND "
        Assert.Equal(" AND a = $1", b.ToAndClause());
    }
}
