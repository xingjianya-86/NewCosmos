using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace NewCosmos.Services.Core;

/// <summary>
/// 背景图片管理- 随机选择登录背景    /// </summary>
public static class BackgroundManager
{
    private static readonly string BACKGROUNDS_FOLDER = "Resources/Backgrounds";
    private static readonly string BUILD_BACKGROUNDS_FOLDER = "backgrounds";
    private static readonly string[] SUPPORTED_EXTENSIONS = { ".webp", ".png", ".jpg", ".jpeg", ".gif" };
    private static readonly ThreadLocal<Random> RANDOM_INSTANCE = new(() => new Random());

    /// <summary>
    /// 获取随机背景图片路径
    /// </summary>
    public static string? GetRandomBackground()
    {
        try
        {
            var backgroundImages = GetAllBackgroundImages();
            return backgroundImages.Count == 0 ? null : backgroundImages[RANDOM_INSTANCE.Value!.Next(backgroundImages.Count)];
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[BackgroundManager] 获取背景图失败");
            return null;
        }
    }

    /// <summary>
    /// 获取所有背景图片列    /// </summary>
    private static List<string> GetAllBackgroundImages()
    {
        var backgroundImages = new List<string>();
        var appDirectory = AppContext.BaseDirectory;

        if (string.IsNullOrEmpty(appDirectory)) return backgroundImages;

        // 开发环境路径
        var devBackgroundsPath = Path.Combine(appDirectory, BACKGROUNDS_FOLDER);
        AddImagesFromDirectory(devBackgroundsPath, backgroundImages);

        // 发布环境路径
        var buildBackgroundsPath = Path.Combine(appDirectory, BUILD_BACKGROUNDS_FOLDER);
        AddImagesFromDirectory(buildBackgroundsPath, backgroundImages);

        return backgroundImages;
    }

    /// <summary>
    /// 从目录添加图片
    /// </summary>
    private static void AddImagesFromDirectory(string directoryPath, List<string> backgroundImages)
    {
        if (!Directory.Exists(directoryPath)) return;

        var files = Directory.GetFiles(directoryPath);
        foreach (var file in files)
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (Array.IndexOf(SUPPORTED_EXTENSIONS, extension) >= 0)
            {
                if (!backgroundImages.Contains(file))
                {
                    backgroundImages.Add(file);
                }
            }
        }
    }
}
