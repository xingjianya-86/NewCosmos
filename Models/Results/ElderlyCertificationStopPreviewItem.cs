using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;

namespace NewCosmos.Models.Results;

/// <summary>
/// 停发预览行：认证任务快照 + 按批次截止口径计算的建议停发原因/应停日期/待截止提示。
/// 仅用于界面展示与逐人跳转，不改变任务实体，也不承载批量操作。
/// </summary>
public sealed class ElderlyCertificationStopPreviewItem
{
    public ElderlyCertificationStopPreviewItem(
        ElderlyCertTask task,
        string? campaignStatus,
        DateTime periodEnd,
        int deadlineStopOffset,
        DateTime today)
    {
        Task = task;

        var reasonKey = ElderlyCertificationStopPreviewHelper.GetSuggestedStopReason(task.CertStatus);
        SuggestedStopReasonKey = reasonKey;
        SuggestedStopReasonName = string.IsNullOrEmpty(reasonKey)
            ? string.Empty
            : ElderlyBenefitConstants.GetStopReasonName(reasonKey);
        SuggestedDueStopDate = ElderlyCertificationStopPreviewHelper.ComputeSuggestedDueStopDate(
            task.CertStatus, task.DeathDate, periodEnd, deadlineStopOffset);
        PresetNote = ElderlyCertificationStopPreviewHelper.BuildPresetNote(task.CertStatus);
        PendingHint = ElderlyCertificationStopPreviewHelper.BuildPendingHint(
            task.CertStatus, campaignStatus, periodEnd, today);
        IsPendingOverdue = ElderlyCertificationStopPreviewHelper.IsOverduePending(
            task.CertStatus, campaignStatus, periodEnd, today);
    }

    public ElderlyCertTask Task { get; }

    public long TaskId => Task.Id;
    public long? ApplicationId => Task.ApplicationId;
    public string Name => Task.Name;
    public string IdCard => Task.IdCard;
    public string VillageDisplay => Task.VillageDisplay;
    public string CertStatusName => Task.CertStatusName;
    public string EvidenceDisplay => Task.EvidenceDisplay;
    public string EvidenceStatusName => Task.EvidenceStatusName;
    public string StopDecisionName => Task.StopDecisionName;
    public string? StopNote => Task.StopNote;
    public string? SuggestedStopReasonKey { get; }
    public string SuggestedStopReasonName { get; }
    public DateTime SuggestedDueStopDate { get; }
    public string? PresetNote { get; }
    public string PendingHint { get; }
    public bool IsPendingOverdue { get; }
    public bool HasPendingHint => !string.IsNullOrEmpty(PendingHint);

    /// <summary>名册 Active 未建档人员没有 application_id，必须先补建档才能停发。</summary>
    public bool CanStop => Task.ApplicationId.HasValue;

    public string StopDisabledHint => CanStop ? string.Empty : "需先补建档";

    public string DecisionDisplay => Task.StopDecision switch
    {
        ElderlyCertificationConstants.StopDecisionDeferred when Task.FollowUpDate is { } followUp =>
            $"暂缓（跟进 {followUp:yyyy-MM-dd}）",
        _ => Task.StopDecisionName
    };

    /// <summary>暂缓/排除可一键恢复为待停发；已停发不可回退。</summary>
    public bool CanResetDecision => Task.StopDecision is
        ElderlyCertificationConstants.StopDecisionDeferred
        or ElderlyCertificationConstants.StopDecisionExcluded;

    public string EventDateDisplay
    {
        get
        {
            if (Task.DeathDate is { } deathDate)
                return $"死亡 {deathDate:yyyy-MM-dd}";
            if (Task.VerifiedAt is { } verifiedAt)
                return $"核查 {verifiedAt:yyyy-MM-dd}";
            return "—";
        }
    }
}
