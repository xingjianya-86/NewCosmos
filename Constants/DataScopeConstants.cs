namespace NewCosmos.Constants;

/// <summary>
/// 数据范围类型常量：决定用户可见/可管理的业务数据边界。
/// 解析规则：以用户所属组织（nc_sys_users.organization_id）为基准，
/// 业务数据按创建人（created_by）归属组织进行过滤；多角色时取范围最大值。
/// </summary>
public static class DataScopeConstants
{
    /// <summary>仅本人数据（created_by = 当前用户）</summary>
    public const string SELF = "SELF";

    /// <summary>本组织数据（created_by ∈ 本组织用户集合）</summary>
    public const string ORG = "ORG";

    /// <summary>本组织及下级组织数据（created_by ∈ 组织子树用户集合）</summary>
    public const string ORG_AND_CHILDREN = "ORG_AND_CHILDREN";

    /// <summary>全部数据（不加过滤条件）</summary>
    public const string ALL = "ALL";

    /// <summary>全部合法数据范围（低 → 高）</summary>
    public static readonly IReadOnlyList<string> OrderedScopes = new[] { SELF, ORG, ORG_AND_CHILDREN, ALL };

    /// <summary>范围层级：数值越大范围越大（非法值返回 0）</summary>
    public static int GetLevel(string scope) => scope switch
    {
        SELF => 1,
        ORG => 2,
        ORG_AND_CHILDREN => 3,
        ALL => 4,
        _ => 0,
    };

    /// <summary>返回两者中范围更大的一个（多角色合并规则）</summary>
    public static string Max(string a, string b) => GetLevel(a) >= GetLevel(b) ? a : b;

    /// <summary>中文名（角色编辑器/权限页展示用）</summary>
    public static string GetDisplayName(string scope) => scope switch
    {
        SELF => "仅本人",
        ORG => "本组织",
        ORG_AND_CHILDREN => "本组织及下级",
        ALL => "全部数据",
        _ => scope,
    };
}
