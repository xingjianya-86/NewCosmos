using NewCosmos.Constants;
using NewCosmos.Helpers;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 停保/退出统计 SQL 片段（AGENTS §8 单点口径）——重点锁死"真·首拒"排除三要素，
/// 防止再次把首次申请即被拒的档案（app110）算进退出对象/停保统计。
/// </summary>
public class StoppedArchiveFilterTests
{
    [Fact]
    public void 首拒排除片段_包含三要素()
    {
        var sql = StoppedArchiveFilter.NotFirstRefusalSql("a");

        // ① 自身为停保码 ② 从未审批享受 ③ 链上无在保前分类
        Assert.Contains("a.classification_result IN (", sql);
        Assert.Contains("a.first_approved_at IS NULL", sql);
        Assert.Contains("NOT EXISTS", sql);
        Assert.Contains("c.old_classification NOT IN (", sql);
        Assert.Contains("c.application_id = a.id OR c.new_application_id = a.id", sql);
    }

    [Fact]
    public void 首拒排除片段_覆盖全部停保码()
    {
        var sql = StoppedArchiveFilter.NotFirstRefusalSql("a");

        foreach (var code in ClassificationConstants.StopCategoryCodes)
            Assert.Contains($"'{code}'", sql);
    }

    [Fact]
    public void 首拒排除片段_按别名替换()
    {
        var sql = StoppedArchiveFilter.NotFirstRefusalSql("nc_biz_applications");

        Assert.Contains("nc_biz_applications.first_approved_at IS NULL", sql);
        Assert.Contains("nc_biz_applications.id", sql);
        Assert.DoesNotContain(" a.", sql);
    }

    [Fact]
    public void 首拒排除片段_以AND_NOT开头可直接追加WHERE()
    {
        var sql = StoppedArchiveFilter.NotFirstRefusalSql("a").TrimStart();

        Assert.StartsWith("AND NOT (", sql);
    }
}
