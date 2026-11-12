using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 重病判定核心（SevereDiseaseJudge）口径锚点——与服务端 deploy\ocr\ocr_api.py 确定性层同规则。
/// 覆盖：精确/单向包含/反向禁止（业务裁定）/编码段/别名、服务端响应二次校验、缓存键规范化。
/// 规则改动必须同步服务端并重跑 deploy\ocr\smoke_ai_severity.py（33 例回归）。
/// </summary>
public class SevereDiseaseJudgeTests
{
    /// <summary>单测用清单子集（结构与 severe_disease_catalog.json 一致）</summary>
    private const string TestCatalogJson = """
    {
      "version": "test",
      "items": ["乳腺癌", "肝硬化", "急性脑梗塞", "急性脑梗死", "脑出血", "I型糖尿病",
                "甲亢", "恶性肿瘤", "尿毒症", "慢性肾脏病", "重症精神病"],
      "aliases": {
        "癌症待查": "恶性肿瘤",
        "慢性肾功能不全": "慢性肾脏病",
        "1型糖尿病": "I型糖尿病"
      },
      "code_ranges": [
        { "letter": "C", "min": 0, "max": 99, "item": "恶性肿瘤" },
        { "letter": "E", "min": 10, "max": 10, "item": "I型糖尿病" },
        { "letter": "I", "min": 61, "max": 61, "item": "脑出血" }
      ]
    }
    """;

    private static SevereDiseaseJudge Create() => SevereDiseaseJudge.FromJson(TestCatalogJson);

    // ── ① 精确命中 ──────────────────────────────────────────────

    [Xunit.Fact]
    public void 精确命中_清单项()
    {
        var ok = Create().TryJudgeDeterministic("乳腺癌", "", out var j);
        Assert.True(ok);
        Assert.Equal(SevereDiseaseJudgment.VerdictSevere, j.Verdict);
        Assert.Equal("乳腺癌", j.MatchedItem);
        Assert.Equal(SevereDiseaseJudgment.SourceDeterministic, j.Source);
        Assert.True(j.AutoEligible);
    }

    [Xunit.Fact]
    public void 精确命中_别名_归一到规范项()
    {
        Assert.True(Create().TryJudgeDeterministic("癌症待查", "", out var j));
        Assert.Equal("恶性肿瘤", j.MatchedItem);
        Assert.Contains("别名", j.Reason);
    }

    // ── ② 单向包含（输入⊃清单项）与反向禁止（业务裁定）────────────

    [Xunit.Fact]
    public void 单向包含_输入含清单项()
    {
        Assert.True(Create().TryJudgeDeterministic("慢性乙型肝炎肝硬化", "", out var j));
        Assert.Equal("肝硬化", j.MatchedItem);
    }

    [Xunit.Fact]
    public void 单向包含_别名包含_最长优先()
    {
        Assert.True(Create().TryJudgeDeterministic("慢性肾功能不全4期", "", out var j));
        Assert.Equal("慢性肾脏病", j.MatchedItem);
    }

    [Xunit.Fact]
    public void 反向禁止_短输入不命中长清单项_脑梗塞不算()
        // 业务裁定 2026-11-11：脑梗塞无"急性"不算（一年恢复期）；反向包含会让
        // "脑梗塞"⊂"急性脑梗塞" 误命中——本用例防规则回潮。
        => Assert.False(Create().TryJudgeDeterministic("脑梗塞", "", out _));

    [Xunit.Fact]
    public void 反向禁止_糖尿病未分型不算()
        => Assert.False(Create().TryJudgeDeterministic("糖尿病", "", out _));

    // ── ③ 编码段 ────────────────────────────────────────────────

    [Theory]
    [InlineData("C80.9", "恶性肿瘤")]     // 癌症待查/未特指部位（业务裁定：算）
    [InlineData("C50.9", "恶性肿瘤")]
    [InlineData("E10.9", "I型糖尿病")]
    [InlineData("I61.9", "脑出血")]
    public void 编码段_命中(string code, string expectedItem)
    {
        Assert.True(Create().TryJudgeDeterministic("某某诊断", code, out var j));
        Assert.Equal(expectedItem, j.MatchedItem);
    }

    [Theory]
    [InlineData("E11.9")]   // 2型糖尿病不在段表（裁定：不算）
    [InlineData("I63.9")]   // 脑梗死刻意不入表（裁定：无"急性"不算）
    [InlineData("J44.9")]
    [InlineData("")]
    public void 编码段_未命中(string code)
        => Assert.False(Create().TryJudgeDeterministic("某某诊断", code, out _));

    // ── 规范化与空输入 ───────────────────────────────────────────

    [Xunit.Fact]
    public void 规范化_去空白与全角括号不影响命中()
    {
        Assert.True(Create().TryJudgeDeterministic("  乳腺癌  ", "", out var j));
        Assert.Equal("乳腺癌", j.MatchedItem);
        Assert.True(Create().TryJudgeDeterministic("甲状腺功能亢进（甲亢）", "", out var j2));
        Assert.Equal("甲亢", j2.MatchedItem);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空名称_不判定(string? name)
        => Assert.False(Create().TryJudgeDeterministic(name, "C50.9", out _));

    // ── 服务端响应二次校验 ───────────────────────────────────────

    [Xunit.Fact]
    public void 响应校验_matched_item不在清单_置空且禁止自动勾选()
    {
        var j = Create().NormalizeServerJudgment(
            "severe", "甲状腺癌", 0.9, "胡扯理由", "llm", "abc123");
        Assert.Equal(string.Empty, j.MatchedItem);
        Assert.False(j.AutoEligible);   // 2026-11-11 防线：matched_item∈清单 才允许自动预勾选
        Assert.Equal(SevereDiseaseJudgment.VerdictSevere, j.Verdict);
    }

    [Xunit.Fact]
    public void 响应校验_别名matched_item_归一到规范项()
    {
        var j = Create().NormalizeServerJudgment(
            "severe", "癌症待查", 0.9, "命中", "llm", "abc123");
        Assert.Equal("恶性肿瘤", j.MatchedItem);
        Assert.True(j.AutoEligible);
    }

    [Theory]
    [InlineData("SEVERE", SevereDiseaseJudgment.VerdictSevere)]     // 大小写容错
    [InlineData("not_severe", SevereDiseaseJudgment.VerdictNotSevere)]
    [InlineData("胡说八道", SevereDiseaseJudgment.VerdictUnknown)]  // 坏值降级 unknown
    [InlineData(null, SevereDiseaseJudgment.VerdictUnknown)]
    public void 响应校验_verdict白名单(string? raw, string expected)
    {
        var j = Create().NormalizeServerJudgment(raw, "", 0.5, "r", "llm", "v");
        Assert.Equal(expected, j.Verdict);
        Assert.False(j.AutoEligible);
    }

    [Xunit.Fact]
    public void 响应校验_confidence截断到0到1()
    {
        Assert.Equal(1.0, Create().NormalizeServerJudgment("severe", "乳腺癌", 3.5, "r", "llm", "v").Confidence);
        Assert.Equal(0.0, Create().NormalizeServerJudgment("severe", "乳腺癌", -1, "r", "llm", "v").Confidence);
    }

    // ── 缓存键与政策版本 ─────────────────────────────────────────

    [Xunit.Fact]
    public void 缓存键_等价输入同键()
    {
        var a = SevereDiseaseJudge.BuildCacheKey("  乳腺 癌 ", "i21.9");
        var b = SevereDiseaseJudge.BuildCacheKey("乳腺癌", " I21.9 ");
        Assert.Equal(a, b);
        Assert.NotEqual(a, SevereDiseaseJudge.BuildCacheKey("肺癌", "I21.9"));
    }

    [Xunit.Fact]
    public void 政策版本_16位小写hex()
    {
        var v = Create().PolicyVersion;
        Assert.Equal(16, v.Length);
        Assert.Matches("^[0-9a-f]{16}$", v);
        Assert.NotEqual(v, SevereDiseaseJudge.FromJson(TestCatalogJson + " ").PolicyVersion); // 内容变→版本变
    }

    [Xunit.Fact]
    public void 真实清单文件_可加载且含关键业务裁定()
    {
        var path = FindRepoFile(Path.Combine("Resources", "Icd10", "severe_disease_catalog.json"));
        Assert.NotNull(path);
        var judge = SevereDiseaseJudge.FromJson(File.ReadAllText(path!));
        Assert.True(judge.HasCatalog);
        Assert.True(judge.Items.Count >= 80, $"清单条目 {judge.Items.Count} < 80");
        Assert.Equal(16, judge.PolicyVersion.Length);

        // 业务裁定落档核对（防清单被误改）：
        Assert.Contains("脑出血", judge.Items);
        Assert.Contains("恶性肿瘤", judge.Items);                          // 裁定3：癌症待查算
        Assert.False(judge.TryJudgeDeterministic("脑梗死", "I63.9", out _)); // 裁定1：I63 不入表
        Assert.False(judge.TryJudgeDeterministic("糖尿病", "E11.9", out _)); // 裁定2：未分型不算
        Assert.True(judge.TryJudgeDeterministic("癌症待查", "", out var j)); // 裁定3：别名
        Assert.Equal("恶性肿瘤", j.MatchedItem);
    }

    [Xunit.Fact]
    public void 默认实例_加载失败降级为空清单不抛()
        => Assert.NotNull(SevereDiseaseJudge.Default);

    /// <summary>从测试输出目录向上找仓库文件（测试项目不复制主项目资源）</summary>
    private static string? FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var p = Path.Combine(dir.FullName, relative);
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        return null;
    }
}
