namespace NewCosmos.ViewModels.Main;

/// <summary>
/// 首页模块分组（分组标题 + 组内卡片）。
/// 标题取自 <c>Constants\ModuleCategoryConstants</c>；空组不进入 MainViewModel.DashboardGroups。
/// </summary>
public class ModuleGroup
{
    public string Title { get; init; } = string.Empty;

    public IReadOnlyList<ModuleCardItem> Cards { get; init; } = Array.Empty<ModuleCardItem>();
}
