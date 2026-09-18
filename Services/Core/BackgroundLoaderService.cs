using NewCosmos.Models.Options;

namespace NewCosmos.Services.Core;

/// <summary>
/// 背景图加载服务实现
/// 动态扫描Resources/Images/backgrounds目录
/// </summary>
public class BackgroundLoaderService : BaseService, IBackgroundLoaderService
{
    protected override string ServiceName => "BackgroundLoaderService";

    private readonly IConfigService _configService;
    private readonly PreferencesOptions _preferencesOptions;
    private List<string> _cachedBackgrounds = new();

    public BackgroundLoaderService(IConfigService configService, ILoggerService logger)
        : base(logger)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _preferencesOptions = configService.GetPreferencesOptions();
        
        LoadBackgrounds();
    }

    private void LoadBackgrounds()
    {
        try
        {
            var uiOptions = _configService.GetUIOptions();

            var backgroundsPath = Path.Combine(AppContext.BaseDirectory, uiOptions.BackgroundImagesDirectory);

            if (!Directory.Exists(backgroundsPath))
            {
                Logger.Warn($"背景图目录不存在: {backgroundsPath}");
                return;
            }

            // 构建新列表后整体替换（原实现向旧列表追加，每次 Refresh 都会累积重复项）
            var files = Directory.GetFiles(backgroundsPath, $"*{uiOptions.BackgroundImageFormat}");
            var loaded = new List<string>(files.Length);
            foreach (var file in files)
            {
                loaded.Add(file);
            }
            _cachedBackgrounds = loaded;

            Logger.Info($"背景图加载完成，共 {_cachedBackgrounds.Count} 张");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "加载背景图失败");
        }
    }

    public List<string> GetBackgroundImages()
    {
        if (_cachedBackgrounds.Count == 0)
        {
            Refresh();
        }
        
        return _cachedBackgrounds;
    }

    public string GetDefaultBackgroundImage()
    {
        var uiOptions = _configService.GetUIOptions();
        
        if (_cachedBackgrounds.Count == 0)
        {
            Refresh();
        }
        
        if (_cachedBackgrounds.Count == 0)
        {
            Logger.Warn("背景图缓存为空，返回空背景");
            return string.Empty;
        }
        
        var defaultBg = _cachedBackgrounds.FirstOrDefault(b => b.Contains(uiOptions.DefaultBackgroundImage));
        
        return defaultBg ?? _cachedBackgrounds.First();
    }

    public string? GetLastUserBackground()
    {
        var uiOptions = _configService.GetUIOptions();
        
        if (!uiOptions.RememberBackgroundChoice)
            return null;
        
        var lastBg = Preferences.Get(_preferencesOptions.LastBackgroundKey, string.Empty);
        
        if (string.IsNullOrEmpty(lastBg))
            return null;
        
        if (_cachedBackgrounds.Contains(lastBg))
            return lastBg;
        
        return null;
    }

    public void SaveUserBackground(string backgroundPath)
    {
        var uiOptions = _configService.GetUIOptions();
        
        if (!uiOptions.RememberBackgroundChoice)
            return;
        
        Preferences.Set(_preferencesOptions.LastBackgroundKey, backgroundPath);
        Logger.Info($"已保存用户背景图选择: {backgroundPath}");
    }

    public void Refresh()
    {
        LoadBackgrounds();
    }
}
