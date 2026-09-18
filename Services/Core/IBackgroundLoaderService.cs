namespace NewCosmos.Services.Core;

/// <summary>
/// 背景图加载服务接    /// </summary>
public interface IBackgroundLoaderService
{
    /// <summary>
    /// 获取所有背景图文件路径
    /// </summary>
    List<string> GetBackgroundImages();

    /// <summary>
    /// 获取默认背景图路径（用于fallback ??   /// </summary>
    string GetDefaultBackgroundImage();

    /// <summary>
    /// 获取用户上次选择的背景图（如果启用了记住功能    /// </summary>
    string? GetLastUserBackground();
    
    /// <summary>
    /// 保存用户选择的背景图
    /// </summary>
    void SaveUserBackground(string backgroundPath);

    /// <summary>
    /// 刷新背景图列表（重新扫描目录    /// </summary>
    void Refresh();
}