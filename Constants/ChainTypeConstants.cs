namespace NewCosmos.Constants;

/// <summary>
/// 档案链类型（nc_biz_applications.chain_type）：记录 original_application_id 的由来，
/// 用于区分「停旧建新（旧档案应停）」与「户内单人保（旧档案继续有效）」等链路，避免一致性检查误判。
/// </summary>
public static class ChainTypeConstants
{
    /// <summary>经济复核停旧建新（分类变更/转入其他类别），旧档案应停止</summary>
    public const string CATEGORY_REBUILD = "CategoryRebuild";

    /// <summary>户主变更停旧建新，旧档案应停止</summary>
    public const string HEAD_CHANGE = "HeadChange";

    /// <summary>户主死亡停旧建新，旧档案应停止</summary>
    public const string HOUSEHOLD_DEATH = "HouseholdDeath";

    /// <summary>家庭成员变更停旧建新（增员/减员后重新认定），旧档案应停止</summary>
    public const string MEMBER_CHANGE = "MemberChange";

    /// <summary>户内单人保新建档案：旧户档案继续有效（不停止）</summary>
    public const string SINGLE_RESCUE = "SingleRescue";

    /// <summary>导入库建档（链根）</summary>
    public const string IMPORTED_ARCHIVE = "ImportedArchive";

    /// <summary>旧档案应停止的链类型集合（单人保/导入建档除外）</summary>
    public static readonly string[] StopOldArchiveChainTypes =
    {
        CATEGORY_REBUILD, HEAD_CHANGE, HOUSEHOLD_DEATH, MEMBER_CHANGE
    };
}
