using NewCosmos.Constants;
using NewCosmos.Models.Entities;

namespace NewCosmos.Services.Domain.TempRelief;

/// <summary>
/// 临时救助候选人检索结果的纯逻辑（去重 + 排序），唯一实现。
/// 从 <see cref="TempReliefService.SearchCandidatesAsync"/> 抽出：无 DB/IO，便于 xUnit 直接覆盖。
///
/// 背景（2026-11-15 修复）：检索按 <see cref="TempReliefConstants.AllowedSourceTables"/> 顺序逐表查，
/// 历史台账在前、当前库在后；旧排序只按「身份证精确命中 → 姓名」，稳定排序让台账行压过当前库行，
/// 出现「已是 11 月低保户，候选列表却先冒出其他困难人员台账」，选中即带出台账旧快照的家庭类别/银行/家庭人数。
/// 现口径与档案查询页、变更页一致：**当前库在前、历史台账在后**。
/// </summary>
public static class TempReliefCandidateRanker
{
    /// <summary>
    /// 排序：① 当前库（<see cref="TempReliefConstants.CurrentSourceTable"/>）在前、历史台账在后
    /// → ② 身份证精确等于关键词 → ③ 姓名 → ④ 记录 ID 较大（较新）者在前。
    /// 不修改入参。
    /// </summary>
    public static List<TempReliefCandidate> Order(
        IReadOnlyList<TempReliefCandidate> candidates, string? keyword)
    {
        var kw = keyword ?? string.Empty;
        return candidates
            .OrderByDescending(c => IsCurrentSource(c))
            .ThenByDescending(c => kw.Length > 0 &&
                string.Equals(c.IdCard, kw, StringComparison.OrdinalIgnoreCase))
            .ThenBy(c => c.Name)
            .ThenByDescending(c => c.SourceFamilyId)
            .ToList();
    }

    /// <summary>
    /// 剔除「本人已是当前库申请人」的台账重复行：同一身份证在当前库已有未删除申请时只保留当前库档案，
    /// 台账是旧快照，留在列表里会被误选（家庭类别/银行/家庭人数全带错）。
    /// 只剔台账行；当前库行（含仅作为家庭成员命中的行）一律保留——此人若只是当前库成员，
    /// 台账里那条才是他本人的户主档案，不能删。
    /// <paramref name="filedIdCards"/> 为空时原样返回（含顺序）。
    /// </summary>
    public static List<TempReliefCandidate> ExcludeFiledLedgerRows(
        IReadOnlyList<TempReliefCandidate> candidates, IReadOnlyCollection<string>? filedIdCards)
    {
        if (candidates.Count == 0 || filedIdCards == null || filedIdCards.Count == 0)
            return candidates.ToList();

        var filed = new HashSet<string>(filedIdCards, StringComparer.OrdinalIgnoreCase);
        return candidates
            .Where(c => IsCurrentSource(c) || !filed.Contains(c.IdCard))
            .ToList();
    }

    private static bool IsCurrentSource(TempReliefCandidate c)
        => string.Equals(c.SourceTable, TempReliefConstants.CurrentSourceTable, StringComparison.Ordinal);
}
