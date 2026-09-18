using NewCosmos.Constants;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.StateMachine;

/// <summary>
/// 申请状态机（类型安全）
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
            ApplicationStatus.Completed,
            ApplicationStatus.Stopped
        },
        [ApplicationStatus.Completed] = new()
        {
            ApplicationStatus.Stopped
        },
        [ApplicationStatus.Refused] = new HashSet<ApplicationStatus>(),
        [ApplicationStatus.Stopped] = new HashSet<ApplicationStatus>()
    };

    /// <summary>
    /// 判断是否可以从 from 状态转换到 to 状态    /// </summary>
    public static bool CanTransition(ApplicationStatus from, ApplicationStatus to)
    {
        return AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);
    }

    /// <summary>
    /// 验证状态转换（返回 result ??   /// </summary>
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
    /// 获取当前状态可用的下一步状态列    /// </summary>
    public static IReadOnlyList<ApplicationStatus> GetAvailableTransitions(ApplicationStatus current)
    {
        return AllowedTransitions.TryGetValue(current, out var targets)
            ? targets.ToList()
            : Array.Empty<ApplicationStatus>();
    }

    /// <summary>
    /// 获取可用下一步状态的描述列表
    /// </summary>
    public static IReadOnlyList<string> GetAvailableTransitionDescriptions(ApplicationStatus current)
    {
        return GetAvailableTransitions(current)
            .Select(s => s.GetDescription())
            .ToList();
    }

    /// <summary>
    /// 判断是否为终    /// </summary>
    public static bool IsFinalState(ApplicationStatus status) =>
        status is ApplicationStatus.Refused or ApplicationStatus.Stopped;

    /// <summary>
    /// 判断是否为活动状态    /// </summary>
    public static bool IsActiveState(ApplicationStatus status) =>
        status is ApplicationStatus.Approved or ApplicationStatus.Completed;

    /// <summary>
    /// 判断是否可编    /// </summary>
    public static bool IsEditable(ApplicationStatus status) =>
        status == ApplicationStatus.Draft;

    /// <summary>
    /// 尝试获取下一个推荐状态    /// </summary>
    public static ApplicationStatus? GetRecommendedNextStatus(ApplicationStatus current)
    {
        var available = GetAvailableTransitions(current);
        return available.Count > 0 ? available[0] : null;
    }

    /// <summary>
    /// 获取状态机图示
    /// </summary>
    public static string GetStateDiagram()
    {
        return @"
状态流转图:

┌───────── Draft  (草稿)  ?└────┬────          ├─────────────────                                   ┌──────────              ?Refused                ?不予受理)              └──────────          ?┌───────────??Submitted (已提  ?└─────┬─────            ├──────────────────                                       ┌──────────                ?Approved                 (已审                 └────┬─────                                          ├───────────────                                                               ┌───────────                            ?Completed                             ?档案完成)                             └─────┬─────                                                                                               ┌──────────                           ?Stopped        └────────────────┼──────▶│ (已停                              └──────────                                              └──────────────────────                                                                                       ┌──────────                                        ?Refused                                          ?不予受理)                                        └──────────";
    }
}

/// <summary>
/// 状态转换结    /// </summary>
public class StateTransitionResult
{
    public bool IsSuccess { get; init; }
    public ApplicationStatus FromStatus { get; init; }
    public ApplicationStatus ToStatus { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public DateTime TransitionTime { get; init; }

    public static StateTransitionResult Success(ApplicationStatus from, ApplicationStatus to) => new()
    {
        IsSuccess = true,
        FromStatus = from,
        ToStatus = to,
        TransitionTime = DateTime.Now
    };

    public static StateTransitionResult Failure(ApplicationStatus from, ApplicationStatus to, string error) => new()
    {
        IsSuccess = false,
        FromStatus = from,
        ToStatus = to,
        ErrorMessage = error,
        TransitionTime = DateTime.Now
    };
}
