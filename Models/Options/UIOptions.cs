using NewCosmos.Models.Exceptions;

namespace NewCosmos.Models.Options;

/// <summary>
/// UI配置选项
/// </summary>
public class UIOptions
{
    /// <summary>
    /// 背景图目录    /// </summary>
    public string BackgroundImagesDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 背景图格式    /// </summary>
    public string BackgroundImageFormat { get; set; } = string.Empty;

    /// <summary>
    /// 默认背景图    /// </summary>
    public string DefaultBackgroundImage { get; set; } = string.Empty;

    /// <summary>
    /// 是否使用随机背景
    /// </summary>
    public bool UseRandomBackground { get; set; }

    /// <summary>
    /// 是否记住用户选择的背景    /// </summary>
    public bool RememberBackgroundChoice { get; set; }

    /// <summary>
    /// 验证配置是否完整
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BackgroundImagesDirectory))
            throw new ConfigurationException("UIOptions.BackgroundImagesDirectory", "背景图目录未配置");

        if (string.IsNullOrWhiteSpace(BackgroundImageFormat))
            throw new ConfigurationException("UIOptions.BackgroundImageFormat", "背景图格式未配置");
    }
}