namespace NewCosmos.Constants;

/// <summary>
/// 申请工作流步骤（nc_biz_applications.current_step 列）的统一事实来源。
/// 步骤语义：1~4 为录入阶段，5=审批中草稿（五步全部完成待归档），6=已归档完结。
/// SQL 与 C# 一律引用本类，禁止内联 5/6 魔法数。
/// </summary>
public static class WorkflowSteps
{
    /// <summary>录入起点：新建/变更复制档案的初始录入步骤（1~4 为录入阶段）</summary>
    public const int ENTRY_START = 1;

    /// <summary>审批中草稿：五个录入步骤全部完成，等待月度审核归档</summary>
    public const int PENDING_ARCHIVE = 5;

    /// <summary>已归档：审核通过、系统自动归档完成（status 同步为 Approved）</summary>
    public const int ARCHIVED = 6;
}
