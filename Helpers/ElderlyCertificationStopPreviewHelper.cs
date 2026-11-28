using NewCosmos.Constants;

namespace NewCosmos.Helpers;

/// <summary>
/// 高龄年度认证「停发预览」纯逻辑：认证状态 → 建议停发原因 / 应停日期 / 待截止提示。
/// 只做映射与日期推算，不读写数据库，供 ViewModel 与单元测试共用。
/// </summary>
public static class ElderlyCertificationStopPreviewHelper
{
    /// <summary>无法联系人员预填备注：现有停发原因模板没有 NOT_LOCATED 选项，原因走“不符合年度认证要求”。</summary>
    public const string NotLocatedNote = "无法联系/查找不到本人";

    /// <summary>
    /// 认证状态 → 建议停发原因 key。
    /// Pending/Refused/Failed/NotLocated 归入“不符合年度认证要求”；
    /// Deceased/MovedOut 分别映射去世/户籍迁出；非候选状态返回 null。
    /// </summary>
    public static string? GetSuggestedStopReason(string? certStatus) => certStatus switch
    {
        ElderlyCertificationConstants.CertStatusDeceased => ElderlyBenefitConstants.StopReasonDeceased,
        ElderlyCertificationConstants.CertStatusMovedOut => ElderlyBenefitConstants.StopReasonMovedOut,
        ElderlyCertificationConstants.CertStatusPending
            or ElderlyCertificationConstants.CertStatusRefused
            or ElderlyCertificationConstants.CertStatusFailed
            or ElderlyCertificationConstants.CertStatusNotLocated => ElderlyBenefitConstants.StopReasonCertificationFail,
        _ => null
    };

    /// <summary>
    /// 认证状态 → 预填备注。无法联系人员固定写“无法联系/查找不到本人”，其余为空。
    /// </summary>
    public static string? BuildPresetNote(string? certStatus) =>
        certStatus == ElderlyCertificationConstants.CertStatusNotLocated ? NotLocatedNote : null;

    /// <summary>
    /// 计算建议应停发日期：去世且已知死亡日期取死亡日期；其余取认证截止日 + 批次停发月偏移。
    /// </summary>
    public static DateTime ComputeSuggestedDueStopDate(
        string? certStatus,
        DateTime? deathDate,
        DateTime periodEnd,
        int deadlineStopOffset)
    {
        if (certStatus == ElderlyCertificationConstants.CertStatusDeceased
            && deathDate is { } date
            && date.Date > DateTime.MinValue)
        {
            return date.Date;
        }

        return periodEnd.Date.AddMonths(deadlineStopOffset < 0 ? 0 : deadlineStopOffset);
    }

    /// <summary>
    /// 待核查行的截止提示：批次已关闭或已过截止日 → 逾期未核查；否则仍为待截止。
    /// 非待核查状态返回空串（UI 不显示提示）。
    /// </summary>
    public static string BuildPendingHint(
        string? certStatus,
        string? campaignStatus,
        DateTime periodEnd,
        DateTime today)
    {
        if (certStatus != ElderlyCertificationConstants.CertStatusPending)
            return string.Empty;

        var overdue = campaignStatus == ElderlyCertificationConstants.CampaignStatusClosed
                      || periodEnd.Date < today.Date;
        return overdue ? "逾期未核查" : "待截止";
    }

    /// <summary>待核查行是否已逾期（用于 UI 着色/提示，不影响列表是否显示）。</summary>
    public static bool IsOverduePending(
        string? certStatus,
        string? campaignStatus,
        DateTime periodEnd,
        DateTime today) =>
        certStatus == ElderlyCertificationConstants.CertStatusPending
        && (campaignStatus == ElderlyCertificationConstants.CampaignStatusClosed || periodEnd.Date < today.Date);
}
