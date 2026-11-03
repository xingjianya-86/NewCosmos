using NewCosmos.Constants;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.StateMachine;

/// <summary>
/// 申请状态机（类型安全）。
///
/// 【真实流转与状态机的关系（2026-11 核实，权威=ApplicationStatusService）】
/// ① 归档即审核：系统无独立审批工作流——CompleteArchiveAsync 才是"审核"，
///    归档直接 Draft/Submitted → Approved（或不合格 → Stopped），不经过 Completed；
/// ② Completed 为历史保留态：全库无写入点（生产库存量 0 行），仅在读侧过滤集/转换器中兼容，
///    用于读取旧备份恢复的数据——禁止新增写入，勿当活跃状态使用；
/// ③ Submitted 仅由一事一议（SpecialApprovalFormService.CompleteReviewAsync）与 SubmitAsync 进入，
///    普通申请 Draft → 归档 → Approved；
/// ④ 状态机严格校验 Submit/Approve/Refuse；CompleteArchiveAsync 与 StopAsync(allowSubmittedOverride)
///    的"业务放行"是有意为之（见各方法注释），改动前先读 ApplicationStatusService。
///
/// 本类仅保留实际使用的 CanTransition / ValidateTransition / IsEditable；
/// 原 GetAvailableTransitions / GetAvailableTransitionDescriptions / IsFinalState / IsActiveState /
/// GetRecommendedNextStatus / GetStateDiagram（字符串已损坏）/ StateTransitionResult 类
/// 均于 2026-11 经全仓 grep 取证零调用方后删除。
/// </summary>
public static class ApplicationStateMachine
{
    private static readonly Dictionary<ApplicationStatus, HashSet<ApplicationStatus>> AllowedTransitions = new()
    {
        [ApplicationStatus.Draft] = new()
        {
            ApplicationStatus.Submitted,
            ApplicationStatus.Refused
        },
        [ApplicationStatus.Submitted] = new()
        {
            ApplicationStatus.Approved,
            ApplicationStatus.Refused
        },
        [ApplicationStatus.Approved] = new()
        {
            // Completed 仅为旧数据读取兼容保留（无写入点）；归档即审核见类注释②
            ApplicationStatus.Completed,
            ApplicationStatus.Stopped
        },
        [ApplicationStatus.Completed] = new()
        {
            ApplicationStatus.Stopped
        },
        [ApplicationStatus.Refused] = new HashSet<ApplicationStatus>(),
        // 恢复保障：已停保 → 在保（唯一出口，见 IApplicationStatusService.ResumeAsync）
        [ApplicationStatus.Stopped] = new HashSet<ApplicationStatus>
        {
            ApplicationStatus.Approved
        }
    };

    /// <summary>
    /// 判断是否可以从 from 状态转换到 to 状态
    /// </summary>
    public static bool CanTransition(ApplicationStatus from, ApplicationStatus to)
    {
        return AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);
    }

    /// <summary>
    /// 验证状态转换（返回带错误消息的 Result）
    /// </summary>
    public static Result ValidateTransition(ApplicationStatus from, ApplicationStatus to)
    {
        if (!CanTransition(from, to))
        {
            return Result.Failure(
                ErrorCodes.INVALID_TRANSITION,
                $"不允许从 {from.GetDescription()} 转换到 {to.GetDescription()}");
        }
        return Result.Success();
    }

    /// <summary>
    /// 判断是否可编辑（仅草稿可编辑）
    /// </summary>
    public static bool IsEditable(ApplicationStatus status) =>
        status == ApplicationStatus.Draft;
}
