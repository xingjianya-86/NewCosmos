namespace NewCosmos.ViewModels.Base;

/// <summary>
/// 页面参数化入口契约：导航后由基类模板方法统一注入业务参数。
/// 页面以接口显式实现收窄可见性——不再暴露 public Set* 可变入口，
/// 参数传递只能经 <see cref="ViewModelBase.NavigateToPageAsync{TPage, TParam}"/> 单一通道。
/// </summary>
public interface IParameterizedPage<in TParam>
{
    /// <summary>接收导航参数并初始化页面（由导航辅助在 Push 前调用）</summary>
    Task SetParameterAsync(TParam parameter);
}
