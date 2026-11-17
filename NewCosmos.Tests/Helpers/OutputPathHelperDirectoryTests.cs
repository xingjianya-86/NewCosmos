using NewCosmos.Constants;
using NewCosmos.Helpers;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// B 类输出目录 API（2026-11-15 输出目录合理化阶段B）：
/// GetCategoryDirectory（平铺分类）与 GetPeriodDirectory（按期间），
/// 收口此前 8 处散落的 Path.Combine(OutputRoot, "字面量", ...)。
/// </summary>
public class OutputPathHelperDirectoryTests : IDisposable
{
    private readonly string _tempRoot;

    public OutputPathHelperDirectoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"nc_outpath_test_{Guid.NewGuid():N}");
        OutputPathHelper.Configure(_tempRoot, "temp");
    }

    public void Dispose()
    {
        // Configure 对空值是忽略语义（不会重置），故显式还原为未配置态的等价值：
        // OutputRoot 未配置时即返回 Path.GetFullPath(LegacyOutputRoot)
        OutputPathHelper.Configure(Path.GetFullPath(OutputPathHelper.LegacyOutputRoot), "temp");
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); } catch { /* 测试残留可忽略 */ }
    }

    [Fact]
    public void 分类目录_落输出根下且已创建()
    {
        var dir = OutputPathHelper.GetCategoryDirectory(OutputCategories.VerificationReport);

        Assert.Equal(Path.GetFullPath(Path.Combine(_tempRoot, "核查报告")), dir);
        Assert.True(Directory.Exists(dir));
        // 必须在输出根之内（防拼出根外路径）
        Assert.StartsWith(Path.GetFullPath(_tempRoot), dir, StringComparison.Ordinal);
    }

    [Fact]
    public void 期间目录_追加期间段且已创建()
    {
        var dir = OutputPathHelper.GetPeriodDirectory(OutputCategories.MonthlyReport, "202611");

        Assert.Equal(Path.GetFullPath(Path.Combine(_tempRoot, "月报表", "202611")), dir);
        Assert.True(Directory.Exists(dir));
    }

    /// <summary>公示用 yyyy-MM、月报用 yyyyMM——两种期间格式都须支持</summary>
    [Theory]
    [InlineData("202611")]
    [InlineData("2026-11")]
    public void 期间格式_两种均支持(string period)
    {
        var dir = OutputPathHelper.GetPeriodDirectory(OutputCategories.PublicityFile, period);

        Assert.EndsWith(period, dir, StringComparison.Ordinal);
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void 期间为空_抛参数异常而非产出根目录()
    {
        // 空期间若放行会退化成 {根}\{分类}\，与平铺分类目录混淆，必须拒绝
        Assert.Throws<ArgumentException>(() =>
            OutputPathHelper.GetPeriodDirectory(OutputCategories.MonthlyReport, ""));
        Assert.Throws<ArgumentException>(() =>
            OutputPathHelper.GetPeriodDirectory(OutputCategories.MonthlyReport, "   "));
    }

    [Fact]
    public void 分类名均不含路径分隔符_不越级()
    {
        var names = new[]
        {
            OutputCategories.MonthlyReport, OutputCategories.ElderlyReport,
            OutputCategories.VerificationReport, OutputCategories.PublicityFile,
            OutputCategories.ArchivePhoto, OutputCategories.LotteryPrediction,
        };

        foreach (var name in names)
            Assert.DoesNotContain(name, c => c is '\\' or '/' or ':');
    }

    /// <summary>B 类目录名单一来源：6 个分类名两两不同（防同值导致目录互串）</summary>
    [Fact]
    public void 分类名两两不同()
    {
        var names = new[]
        {
            OutputCategories.MonthlyReport, OutputCategories.ElderlyReport,
            OutputCategories.VerificationReport, OutputCategories.PublicityFile,
            OutputCategories.ArchivePhoto, OutputCategories.LotteryPrediction,
        };

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }
}
