using NewCosmos.Constants;
using Xunit;

namespace NewCosmos.Tests.Constants;

/// <summary>
/// 死亡申报台账常量（2026-11-08 修订口径）：
/// · 状态机收敛 Pending → Archived / Revoked（Verified「已核实」已删：不可达且全库 0 行）；
/// · 死因选项唯一来源 = 字典 DeathReasons（常量只留「其他」哨兵，2026-11-08 死因双写统一）；
/// · 规范化格式固定小集合：证明类型 / 遗体处置三分支 / 矛盾纠纷。
/// </summary>
public class DeathDeclarationConstantsTests
{
    // ── 状态 ──

    [Theory]
    [InlineData(DeathDeclarationConstants.StatusPending, "待核实")]
    [InlineData(DeathDeclarationConstants.StatusRevoked, "已作废")]
    [InlineData(DeathDeclarationConstants.StatusArchived, "已归档")]
    public void GetStatusName_已知状态_映射中文(string status, string expected)
        => Assert.Equal(expected, DeathDeclarationConstants.GetStatusName(status));

    [Fact]
    public void GetStatusName_Verified已删_未知状态原样返回()
    {
        // Verified 常量已随 P1-5 删除：历史值不再有中文映射，原样返回（禁止洗成空串）
        Assert.Equal("Verified", DeathDeclarationConstants.GetStatusName("Verified"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetStatusName_空值_返回空串(string? status)
        => Assert.Equal(string.Empty, DeathDeclarationConstants.GetStatusName(status));

    // ── 死因「其他」哨兵（选项本身走字典，不再有常量数组）──

    [Theory]
    [InlineData("其他", true)]
    [InlineData(" 其他 ", true)]   // Trim 后比较
    [InlineData("自然死亡", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsOtherOption_哨兵判定(string? value, bool expected)
        => Assert.Equal(expected, DeathDeclarationConstants.IsOtherOption(value));

    // ── 规范化格式固定小集合（2026-11-08）──

    [Fact]
    public void CertTypeOptions_两证_顺序与内容()
        => Assert.Equal(
            new[] { "死亡医学证明", "非正常死亡证明" },
            DeathDeclarationConstants.CertTypeOptions);

    [Fact]
    public void DispositionOptions_三分支_含其他哨兵()
    {
        Assert.Equal(
            new[] { "火化", "安葬", "其他" },
            DeathDeclarationConstants.DispositionOptions);
        // 「其他」分支与死因/地点手填哨兵共用同一字面量（IsOtherOption 通用）
        Assert.Equal(DeathDeclarationConstants.DeathReasonOther, DeathDeclarationConstants.DispositionOther);
    }

    [Fact]
    public void DisputeStatusOptions_无有两选()
    {
        Assert.Equal(new[] { "无", "有" }, DeathDeclarationConstants.DisputeStatusOptions);
        Assert.Equal("无", DeathDeclarationConstants.DisputeNone);
        Assert.Equal("有", DeathDeclarationConstants.DisputeHas);
    }

    // ── 重复申报窗口（组句/校验依赖的既有口径）──

    [Fact]
    public void DuplicateWindowDays_为3()
        => Assert.Equal(3, DeathDeclarationConstants.DuplicateWindowDays);
}
