namespace NewCosmos.Constants;

/// <summary>
/// 家庭成员变更原因（固定选项：增员/减员各自一组；供变更登记弹窗、变更记录明细与档案输出复用）
/// </summary>
public static class MemberChangeReasonConstants
{
    // ── 减员原因 ──
    public const string RemoveDeath = "Death";
    public const string RemoveHukouMoveOut = "HukouMove";
    public const string RemoveMarriageMoveOut = "MarriageMove";
    public const string RemovePrisonOrMilitary = "Prison";
    public const string RemoveOther = "Other";

    /// <summary>减员原因代码（顺序即弹窗展示顺序）</summary>
    public static readonly string[] RemoveCodes =
    {
        RemoveDeath, RemoveHukouMoveOut, RemoveMarriageMoveOut, RemovePrisonOrMilitary, RemoveOther
    };

    // ── 增员原因 ──
    public const string AddBirth = "Birth";
    public const string AddMarriageMoveIn = "MarriageMoveIn";
    public const string AddHukouMoveIn = "HukouMoveIn";
    public const string AddSharedLivingNew = "SharedLivingNew";
    public const string AddOther = "Other";

    /// <summary>增员原因代码（顺序即弹窗展示顺序）</summary>
    public static readonly string[] AddCodes =
    {
        AddBirth, AddMarriageMoveIn, AddHukouMoveIn, AddSharedLivingNew, AddOther
    };

    /// <summary>减员原因代码 → 名称</summary>
    public static string GetRemoveName(string? code) => code switch
    {
        RemoveDeath => "人员死亡",
        RemoveHukouMoveOut => "户籍迁出",
        RemoveMarriageMoveOut => "婚嫁迁出",
        RemovePrisonOrMilitary => "服刑/服兵役",
        RemoveOther => "其他原因",
        _ => code ?? string.Empty
    };

    /// <summary>增员原因代码 → 名称</summary>
    public static string GetAddName(string? code) => code switch
    {
        AddBirth => "出生",
        AddMarriageMoveIn => "婚嫁迁入",
        AddHukouMoveIn => "户籍迁入",
        AddSharedLivingNew => "共同生活新增",
        AddOther => "其他原因",
        _ => code ?? string.Empty
    };

    /// <summary>按方向取名称（isRemove=true 走减员映射）</summary>
    public static string GetName(bool isRemove, string? code) =>
        isRemove ? GetRemoveName(code) : GetAddName(code);

    /// <summary>是否为"人员死亡"减员原因（需联动死亡记录）</summary>
    public static bool IsDeath(string? code) =>
        string.Equals(code, RemoveDeath, StringComparison.OrdinalIgnoreCase);
}
