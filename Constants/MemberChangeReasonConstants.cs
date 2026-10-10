namespace NewCosmos.Constants;

/// <summary>
/// 家庭成员变更原因 —— 仅保留死亡语义哨兵与历史 code→名称兜底。
/// 增/减员原因选项的唯一数据源是字典（减员 ChangeReasons / 增员 MemberAddReasons，
/// 种子见 Resources\Seed\sys_dictionary\items\member_remove_reason.yaml / member_add_reason.yaml），
/// 弹窗选项、落库 ReasonCode 均取自字典，此处不再维护选项清单。
/// </summary>
public static class MemberChangeReasonConstants
{
    /// <summary>减员原因"人员死亡"的代码（死亡联动哨兵：字典项 item_key 必须保持同名）</summary>
    public const string RemoveDeath = "Death";

    /// <summary>减员原因代码 → 名称（仅死亡判定使用；未知 code 原样返回）</summary>
    public static string GetRemoveName(string? code) => code switch
    {
        RemoveDeath => "人员死亡",
        _ => code ?? string.Empty
    };

    /// <summary>是否为"人员死亡"减员原因（需联动死亡记录）</summary>
    public static bool IsDeath(string? code) =>
        string.Equals(code, RemoveDeath, StringComparison.OrdinalIgnoreCase);
}
