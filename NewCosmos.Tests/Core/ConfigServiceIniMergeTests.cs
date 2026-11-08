using NewCosmos.Services.Core;
using Xunit;

namespace NewCosmos.Tests.Core;

/// <summary>
/// ConfigService.MergeMissingIniKeys——network.ini 缺失 OcrServiceUrl 键的启动自愈口径锚点：
/// 老手机私有目录旧副本缺该键 → 扫码识别报"识别服务地址未配置"，启动时按此口径从 assets 补齐。
/// </summary>
public class ConfigServiceIniMergeTests
{
    private const string AssetUrl = "http://10.110.0.2:8788";

    private static readonly IReadOnlyDictionary<string, string> Required =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["OcrServiceUrl"] = AssetUrl };

    [Fact]
    public void 缺键_追加到末尾_注释与已有键原样保留()
    {
        const string input = """
            # 网络接入配置（ZeroTier）
            # 由「系统配置向导 - 网络接入」自动维护。

            [Network]
            Mode=Private
            DbHost=10.110.0.2
            """;

        var (text, changed) = ConfigService.MergeMissingIniKeys(input, Required);

        Assert.True(changed);
        Assert.StartsWith("# 网络接入配置（ZeroTier）", text);
        Assert.Contains("# 由「系统配置向导 - 网络接入」自动维护。", text);
        Assert.Contains("Mode=Private", text);
        Assert.Contains("DbHost=10.110.0.2", text);
        Assert.Contains($"OcrServiceUrl={AssetUrl}", text);
    }

    [Fact]
    public void 空值_回填为assets默认值()
    {
        const string input = "[Network]\nMode=Private\nOcrServiceUrl=\n";

        var (text, changed) = ConfigService.MergeMissingIniKeys(input, Required);

        Assert.True(changed);
        Assert.Contains($"OcrServiceUrl={AssetUrl}", text);
        Assert.DoesNotContain("OcrServiceUrl=\n", text);
    }

    [Fact]
    public void 非空值_不覆盖且文本逐字不变()
    {
        const string input = "[Network]\nOcrServiceUrl=http://my-custom:9999\n";

        var (text, changed) = ConfigService.MergeMissingIniKeys(input, Required);

        Assert.False(changed);
        Assert.Equal(input, text);
    }

    [Fact]
    public void 大小写不同的非空键_同样不覆盖()
    {
        const string input = "[Network]\nocrserviceurl = http://my-custom:9999\n";

        var (text, changed) = ConfigService.MergeMissingIniKeys(input, Required);

        Assert.False(changed);
        Assert.Equal(input, text);
    }

    [Fact]
    public void 带BOM输入_输出无BOM且注释仍被识别()
    {
        // BOM 会粘在首行注释前让 # 判定失效，自愈写回时必须剥掉
        var input = "\uFEFF# 注释\n[Network]\nOcrServiceUrl=\n";

        var (text, changed) = ConfigService.MergeMissingIniKeys(input, Required);

        Assert.True(changed);
        Assert.DoesNotContain('\uFEFF', text);
        Assert.StartsWith("# 注释", text);
        Assert.Contains($"OcrServiceUrl={AssetUrl}", text);
    }

    [Fact]
    public void 无尾换行_追加后独立成行并以换行结尾()
    {
        const string input = "[Network]\nMode=Private";

        var (text, changed) = ConfigService.MergeMissingIniKeys(input, Required);

        Assert.True(changed);
        Assert.Equal($"[Network]\nMode=Private\nOcrServiceUrl={AssetUrl}\n", text);
    }

    [Fact]
    public void CRLF风格_追加与回填均沿用CRLF()
    {
        var input = "[Network]\r\nMode=Private\r\nOcrServiceUrl=\r\n";

        var (text, changed) = ConfigService.MergeMissingIniKeys(input, Required);

        Assert.True(changed);
        Assert.Contains($"OcrServiceUrl={AssetUrl}\r\n", text);
        Assert.DoesNotContain($"OcrServiceUrl={AssetUrl}\n", text);
    }

    [Fact]
    public void assets默认值为空_不改文本()
    {
        // assets 里该键也为空时补空行无意义，保持原样
        var required = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OcrServiceUrl"] = ""
        };
        const string input = "[Network]\nMode=Private\n";

        var (text, changed) = ConfigService.MergeMissingIniKeys(input, required);

        Assert.False(changed);
        Assert.Equal(input, text);
    }
}
