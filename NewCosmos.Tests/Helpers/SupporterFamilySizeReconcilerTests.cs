using NewCosmos.Helpers;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 赡养人「家庭人口」双编辑口调和（SupporterFamilySizeReconciler）测试。
///
/// 背景：Step2 成员卡 FamilyMember.FamilySize 与 Step3 赡养明细 Supporter.SupporterFamilySize
/// 落在同一列 nc_biz_family_members.family_size，保存时 SaveFamilyMembers 先写、SaveSupporters
/// 后写覆盖 → 必须在保存前调和。判定唯一依据 = 加载基线（只比现值分不清谁改过）。
/// </summary>
public class SupporterFamilySizeReconcilerTests
{
    // ── 规则1：两侧同值 → 不动 ──

    [Fact]
    public void Resolve_两侧同值_不动()
    {
        var r = SupporterFamilySizeReconciler.Resolve(hasBaseline: true, baseline: 3, memberValue: 3, supporterValue: 3);
        Assert.False(r.Changed);
        Assert.Equal(3, r.MemberValue);
        Assert.Equal(3, r.SupporterValue);
    }

    [Fact]
    public void Resolve_两侧同值且等于基线_不动()
    {
        var r = SupporterFamilySizeReconciler.Resolve(true, 5, 5, 5);
        Assert.False(r.Changed);
    }

    // ── 规则2：Step2 为空 → 以 Step3 为准（含归一化差异 null vs 1）──

    [Fact]
    public void Resolve_成员侧为空_取支持侧()
    {
        // 基线 NULL + 支持侧加载兜底成 1：不应判为"Step2 被清空"，而是归一到 1
        var r = SupporterFamilySizeReconciler.Resolve(true, null, null, 1);
        Assert.True(r.Changed);
        Assert.Equal(1, r.MemberValue);
        Assert.Equal(1, r.SupporterValue);
    }

    [Fact]
    public void Resolve_成员侧为空且支持侧为5_取支持侧()
    {
        var r = SupporterFamilySizeReconciler.Resolve(true, null, null, 5);
        Assert.True(r.Changed);
        Assert.Equal(5, r.MemberValue);
        Assert.Equal(5, r.SupporterValue);
    }

    // ── 规则3：Step2 偏离基线 → Step2 为准 ──

    [Fact]
    public void Resolve_仅Step2改过_支持侧跟随()
    {
        // 用户在 Step2 把家庭人口 3 → 5，Step3 未动仍是 3
        var r = SupporterFamilySizeReconciler.Resolve(true, 3, 5, 3);
        Assert.True(r.Changed);
        Assert.Equal(5, r.MemberValue);
        Assert.Equal(5, r.SupporterValue);
    }

    [Fact]
    public void Resolve_无基线即本会话新增_成员侧优先()
    {
        // 新增赡养人（不在加载基线里）且两侧不等（如 Supporter 仍为默认 1）
        var r = SupporterFamilySizeReconciler.Resolve(false, null, 4, 1);
        Assert.True(r.Changed);
        Assert.Equal(4, r.MemberValue);
        Assert.Equal(4, r.SupporterValue);
    }

    [Fact]
    public void Resolve_两侧都改过冲突_以Step2为准()
    {
        // 同一次编辑里 Step2 改成 5、Step3 改成 2 → 按约定以 Step2（成员卡）为准
        var r = SupporterFamilySizeReconciler.Resolve(true, 3, 5, 2);
        Assert.True(r.Changed);
        Assert.Equal(5, r.MemberValue);
        Assert.Equal(5, r.SupporterValue);
    }

    // ── 规则4：仅 Step3 改过 → Step3 为准 ──

    [Fact]
    public void Resolve_仅Step3改过_成员侧跟随()
    {
        // 用户在 Step3 把家庭人数 3 → 4，Step2 卡未动仍是 3
        var r = SupporterFamilySizeReconciler.Resolve(true, 3, 3, 4);
        Assert.True(r.Changed);
        Assert.Equal(4, r.MemberValue);
        Assert.Equal(4, r.SupporterValue);
    }

    // ── 归一化：支持侧 0/负数按 1 兜底（与 Initialization 加载口径一致）──

    [Fact]
    public void Resolve_支持侧为0_按1兜底()
    {
        var r = SupporterFamilySizeReconciler.Resolve(true, 1, 1, 0);
        Assert.False(r.Changed);
        Assert.Equal(1, r.SupporterValue);
    }

    [Fact]
    public void Resolve_支持侧为负数_按1兜底()
    {
        var r = SupporterFamilySizeReconciler.Resolve(true, 1, 1, -3);
        Assert.False(r.Changed);
        Assert.Equal(1, r.SupporterValue);
    }

    // ── 调和后必然两侧同值（保存顺序不再影响结果）──

    [Theory]
    [InlineData(true, 3, 5, 3)]    // 仅 Step2 改
    [InlineData(true, 3, 3, 5)]    // 仅 Step3 改
    [InlineData(true, 3, 5, 2)]    // 两侧都改（冲突）
    [InlineData(true, null, null, 1)] // 基线空 + 归一化
    [InlineData(false, null, 4, 1)]   // 本会话新增
    [InlineData(true, 2, null, 2)]    // 成员侧被清空
    public void Resolve_调和后两侧必同值(bool hasBaseline, int? baseline, int? memberValue, int supporterValue)
    {
        var r = SupporterFamilySizeReconciler.Resolve(hasBaseline, baseline, memberValue, supporterValue);
        Assert.Equal(r.SupporterValue, r.MemberValue);
    }
}
