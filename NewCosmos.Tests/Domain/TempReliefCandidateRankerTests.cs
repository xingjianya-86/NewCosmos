using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Domain.TempRelief;
using Xunit;

namespace NewCosmos.Tests.Domain;

/// <summary>
/// 临时救助候选人检索结果的排序与去重（TempReliefCandidateRanker 唯一实现）。
/// 锁点（2026-11-15 修复）：**当前库（nc_biz_applications）永远排在历史导入台账之前**——
/// 旧实现按姓名稳定排序，台账行先入列就压过当前库行，出现「已是 11 月低保户却先冒出其他困难人员台账」；
/// 同一身份证已在当前库建档的，其台账重复行不再列出（避免选中带出台账旧快照的家庭类别/银行/家庭人数）。
/// </summary>
public class TempReliefCandidateRankerTests
{
    private const string Current = "nc_biz_applications";
    private const string OtherLedger = "nc_biz_other_difficulty_families";
    private const string RuralLedger = "nc_biz_rural_subsistence_families";

    private static TempReliefCandidate C(string table, long id, string name, string idCard) => new()
    {
        SourceTable = table,
        SourceFamilyId = id,
        Name = name,
        IdCard = idCard
    };

    [Fact]
    public void Order_当前库排在历史台账之前()
    {
        // 入参模拟真实查询顺序：AllowedSourceTables 先台账、后当前库
        var rows = new List<TempReliefCandidate>
        {
            C(OtherLedger, 9, "张三", "2310001"),
            C(RuralLedger, 4, "张三", "2310001"),
            C(Current, 100, "张三", "2310001")
        };

        var ordered = TempReliefCandidateRanker.Order(rows, "2310001");

        Assert.Equal(Current, ordered[0].SourceTable);
        Assert.Equal(OtherLedger, ordered[1].SourceTable);
        Assert.Equal(RuralLedger, ordered[2].SourceTable);
    }

    [Fact]
    public void Order_当前库优先级高于精确命中_不被台账精确行压过()
    {
        // 台账行与关键词完全相等、当前库行只是姓名命中：口径仍是当前库在前
        var rows = new List<TempReliefCandidate>
        {
            C(OtherLedger, 3, "李四", "2310001"),
            C(Current, 77, "李四", "2310009")
        };

        var ordered = TempReliefCandidateRanker.Order(rows, "2310001");

        Assert.Equal(Current, ordered[0].SourceTable);
        Assert.Equal(OtherLedger, ordered[1].SourceTable);
    }

    [Fact]
    public void Order_同源内身份证精确命中在前_其余按姓名()
    {
        var rows = new List<TempReliefCandidate>
        {
            C(Current, 11, "Amy", "2310007"),  // 姓名字典序在前，但非精确命中
            C(Current, 12, "Bob", "2310008"),
            C(Current, 10, "Zoe", "2310001")   // 身份证精确命中 → 置顶（哪怕姓名排最后）
        };

        var ordered = TempReliefCandidateRanker.Order(rows, "2310001");

        Assert.Equal(new long[] { 10, 11, 12 }, ordered.Select(r => r.SourceFamilyId));
    }

    [Fact]
    public void Order_同源同名取记录ID较大者在前()
    {
        var rows = new List<TempReliefCandidate>
        {
            C(Current, 10, "赵七", "2310001"),
            C(Current, 99, "赵七", "2310001")
        };

        var ordered = TempReliefCandidateRanker.Order(rows, "赵七");

        Assert.Equal(new long[] { 99, 10 }, ordered.Select(r => r.SourceFamilyId));
    }

    [Fact]
    public void Order_身份证精确命中忽略大小写()
    {
        var rows = new List<TempReliefCandidate>
        {
            C(Current, 1, "张三", "2310001X"),
            C(Current, 2, "张三", "2310001Y")
        };

        var ordered = TempReliefCandidateRanker.Order(rows, "2310001x");

        Assert.Equal(1, ordered[0].SourceFamilyId);
    }

    [Fact]
    public void Order_不修改入参()
    {
        var rows = new List<TempReliefCandidate>
        {
            C(OtherLedger, 9, "张三", "2310001"),
            C(Current, 100, "张三", "2310001")
        };

        TempReliefCandidateRanker.Order(rows, "2310001");

        Assert.Equal(OtherLedger, rows[0].SourceTable);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void ExcludeFiledLedgerRows_已建档身份证的台账行被剔除_当前库行保留()
    {
        var rows = new List<TempReliefCandidate>
        {
            C(Current, 100, "张三", "2310001"),   // 当前库（哪怕只是作为成员命中）一律保留
            C(OtherLedger, 9, "张三", "2310001"), // 已在当前库建档 → 剔除
            C(RuralLedger, 4, "李四", "2310002")  // 只在台账存在 → 保留
        };

        var filtered = TempReliefCandidateRanker.ExcludeFiledLedgerRows(rows, new[] { "2310001" });

        Assert.Equal(2, filtered.Count);
        Assert.DoesNotContain(filtered, c => c.SourceTable == OtherLedger);
        Assert.Contains(filtered, c => c.SourceTable == RuralLedger);
        Assert.Contains(filtered, c => c.SourceTable == Current);
    }

    [Fact]
    public void ExcludeFiledLedgerRows_身份证比较忽略大小写()
    {
        var rows = new List<TempReliefCandidate> { C(OtherLedger, 9, "张三", "2310001x") };

        var filtered = TempReliefCandidateRanker.ExcludeFiledLedgerRows(rows, new[] { "2310001X" });

        Assert.Empty(filtered);
    }

    [Fact]
    public void ExcludeFiledLedgerRows_空已建档集_原样返回含顺序()
    {
        var rows = new List<TempReliefCandidate>
        {
            C(OtherLedger, 9, "张三", "2310001"),
            C(Current, 100, "张三", "2310001")
        };

        var filtered = TempReliefCandidateRanker.ExcludeFiledLedgerRows(rows, Array.Empty<string>());

        Assert.Equal(2, filtered.Count);
        Assert.Equal(OtherLedger, filtered[0].SourceTable);
        Assert.Equal(Current, filtered[1].SourceTable);
    }

    [Fact]
    public void 常量锚点_当前库即白名单唯一且居末位的表()
    {
        // 排序/去重的锚点必须与来源白名单一致，否则去重与排序会各说各话
        Assert.Equal("nc_biz_applications", TempReliefConstants.CurrentSourceTable);
        Assert.Equal(TempReliefConstants.CurrentSourceTable, TempReliefConstants.AllowedSourceTables.Last());
        Assert.Single(TempReliefConstants.AllowedSourceTables, t => t == TempReliefConstants.CurrentSourceTable);
    }
}
