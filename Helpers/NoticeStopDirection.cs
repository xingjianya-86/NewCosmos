using NewCosmos.Constants;

namespace NewCosmos.Helpers;

/// <summary>
/// 《档案_变更告知书》方向②（停止 / 不予认定）判定纯逻辑（ArchiveProductionViewModel 装配端与
/// NewCosmos.Tests 共用；取数来源见 Helpers\ChainPreClassification）。
/// 口径（docs/20261119_告知书停止与不予认定方向判定.md）：
/// 停止告知 = 原先在认定范围内（原分类为有效分类），因收入超标等被停保；
/// 不予认定告知 = 首次申请即被拒（原分类为收入超标/不予认定等停保类）。
/// </summary>
public static class NoticeStopDirection
{
    /// <summary>
    /// 归档终结拒保兜底的原分类取值：链上有在保前分类 → 用前分类（方向=停止，
    /// 丁玉花 app113 型：户主死亡停旧建新产物、前分类=农村低保）；
    /// 取不到或前分类本身是停保码 → 用档案自身停保码（方向=不予认定，app110 真·首拒型）。
    /// </summary>
    public static string ResolveArchiveRefusedOriginal(string? chainPreClassification, string ownStopCode)
        => !string.IsNullOrEmpty(chainPreClassification)
           && !ClassificationConstants.IsCodeStop(chainPreClassification)
            ? chainPreClassification
            : ownStopCode;

    /// <summary>是否原分类在保：true → 「停止/停止享受」，false → 「不予认定」。</summary>
    public static bool WasInScope(string? originalClassification)
        => !ClassificationConstants.IsCodeStop(originalClassification ?? string.Empty);
}
