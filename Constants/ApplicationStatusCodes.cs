namespace NewCosmos.Constants;

/// <summary>
/// 业务状态字符串代码的统一事实来源（数据库 status 列存储值）。
/// SQL 与 C# 比较一律引用本类常量，禁止在调用点内联 'Draft'/'Approved'/'0' 等字面量。
/// </summary>
public static class ApplicationStatusCodes
{
    // ── 主申请表 nc_biz_applications.status ──
    public const string DRAFT = "Draft";
    public const string SUBMITTED = "Submitted";
    public const string APPROVED = "Approved";
    public const string COMPLETED = "Completed";
    public const string REFUSED = "Refused";
    public const string STOPPED = "Stopped";

    // ── 异地协助专用状态（application_type=RemoteAssistance） ──
    /// <summary>异地协助办理中（等价草稿语义，但不进普通草稿 Tab，兼容未升级客户端）</summary>
    public const string REMOTE_ASSISTANCE_DRAFT = "RemoteAssistanceDraft";
    /// <summary>异地协助已办结（终态，只读，永不进入月报/在保/停保口径）</summary>
    public const string REMOTE_ASSISTANCE_COMPLETED = "RemoteAssistanceCompleted";

    // ── 别名/通用状态（跨表共用） ──
    /// <summary>通用待处理状态（资产核对/流程节点等）</summary>
    public const string PENDING = "Pending";
    /// <summary>通用已确认状态（临时救助/高龄津贴等）</summary>
    public const string CONFIRMED = "Confirmed";
    /// <summary>通用活跃/生效中状态（打印记录/组织等）</summary>
    public const string ACTIVE = "Active";
    /// <summary>通用驳回状态（一事一议等）</summary>
    public const string REJECTED = "Rejected";

    /// <summary>在享口径：已审批在保</summary>
    public const string ACTIVE_CONDITION = APPROVED;
}
