namespace NewCosmos.Constants;

/// <summary>
/// 首页模块分组标题（业务模块直达区分组与侧边栏分组共用，文案单点维护）。
/// 分组内卡片归属见 <c>MainViewModel.RebuildDashboard</c> 的卡片目录。
/// </summary>
public static class ModuleCategoryConstants
{
    /// <summary>救助业务（低收入救助、高龄津贴、经济核对、档案、死亡情况说明、后补追缴）</summary>
    public const string BUSINESS = "救助业务";

    /// <summary>管理工具（数据中心、用户与权限、值班管理、历史补打中心等）</summary>
    public const string TOOLS = "管理工具";

    /// <summary>娱乐天地（彩票助手）</summary>
    public const string ENTERTAINMENT = "娱乐天地";

    /// <summary>快捷链接（外站直达）</summary>
    public const string LINKS = "快捷链接";
}
