namespace NewCosmos.Models.Enums;

/// <summary>
/// 申请状态枚举（类型安全    /// </summary>
public enum ApplicationStatus
{
    /// <summary>草稿</summary>
    Draft,

    /// <summary>已提</summary>
    Submitted,

    /// <summary>已审</summary>
    Approved,

    /// <summary>档案完成</summary>
    Completed,

    /// <summary>不予受理</summary>
    Refused,

    /// <summary>已停</summary>
    Stopped
}

/// <summary>
/// 申请状态扩展方    /// </summary>
public static class ApplicationStatusExtensions
{
    private static readonly Dictionary<ApplicationStatus, string> Descriptions = new()
    {
        [ApplicationStatus.Draft] = "草稿",
        [ApplicationStatus.Submitted] = "已提交",
        [ApplicationStatus.Approved] = "已审批",
        [ApplicationStatus.Completed] = "档案制作完成",
        [ApplicationStatus.Refused] = "不予受理",
        [ApplicationStatus.Stopped] = "已停保"
    };

    private static readonly Dictionary<ApplicationStatus, string> Codes = new()
    {
        [ApplicationStatus.Draft] = "Draft",
        [ApplicationStatus.Submitted] = "Submitted",
        [ApplicationStatus.Approved] = "Approved",
        [ApplicationStatus.Completed] = "Completed",
        [ApplicationStatus.Refused] = "Refused",
        [ApplicationStatus.Stopped] = "Stopped"
    };

    /// <summary>
    /// 获取状态描    /// </summary>
    public static string GetDescription(this ApplicationStatus status) =>
        Descriptions.TryGetValue(status, out var desc) ? desc : status.ToString();

    /// <summary>
    /// 获取状态代    /// </summary>
    public static string GetCode(this ApplicationStatus status) =>
        Codes.TryGetValue(status, out var code) ? code : status.ToString();

    /// <summary>
    /// 从代码解析状    /// </summary>
    public static ApplicationStatus FromCode(string code) => code.Trim() switch
    {
        "Draft" => ApplicationStatus.Draft,
        "Submitted" => ApplicationStatus.Submitted,
        "Approved" => ApplicationStatus.Approved,
        "Completed" => ApplicationStatus.Completed,
        "Refused" => ApplicationStatus.Refused,
        "Stopped" => ApplicationStatus.Stopped,
        _ => ApplicationStatus.Draft
    };

    /// <summary>
    /// 是否为可编辑状    /// </summary>
    public static bool IsEditable(this ApplicationStatus status) =>
        status == ApplicationStatus.Draft;

    /// <summary>
    /// 是否为终    /// </summary>
    public static bool IsFinal(this ApplicationStatus status) =>
        status is ApplicationStatus.Completed or ApplicationStatus.Refused or ApplicationStatus.Stopped;

    /// <summary>
    /// 是否为活动状    /// </summary>
    public static bool IsActive(this ApplicationStatus status) =>
        status is ApplicationStatus.Approved or ApplicationStatus.Completed;
}
