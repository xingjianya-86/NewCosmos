using NewCosmos.Constants;
using NewCosmos.Models.Exceptions;

namespace NewCosmos.Models.Options;

/// <summary>
/// 应用配置选项
/// </summary>
public class AppOptions
{
    /// <summary>
    /// 应用版本
/// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// 应用名称
    /// </summary>
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>
    /// 窗口标题
    /// </summary>
    public string WindowTitle { get; set; } = string.Empty;

    /// <summary>
    /// B 线（业务线/月报）统计结算日：月报周期 = [上月(结算日+1)日, 本月(结算日+1)日)。
    /// 默认 15（周期 [上月16, 本月16)）；上级要求业务截止 20 号时配置为 20（周期 [上月21, 本月21)）。
    /// 启动时由 App 注入 BusinessCycleHelper。
    /// </summary>
    public int BCycleSettleDay { get; set; } = BusinessCycleConstants.DefaultSettleDay;

    /// <summary>
    /// 保障金变更登记阈值（元/月）：动态管理停旧建新后旧/新档同属低保群体时，
    /// 户月保障金差 ≥ 本值才登记"保障金变更"（进月报/文书），&lt; 本值只重建不登记。
    /// 启动时由 App 注入 FundChangeThresholdHelper；分类施保变动不适用本阈值。
    /// </summary>
    public decimal FundChangeThreshold { get; set; } = FundChangeThresholdConstants.DefaultFundChangeThreshold;

    /// <summary>
    /// 启动动画总开关（app.ini SplashEnabled，默认开）：关闭后直接进登录页，不播动画。
    /// </summary>
    public bool SplashEnabled { get; set; } = true;

    /// <summary>
    /// 启动动画音频开关（app.ini SplashSound，默认开）：真声台词 + 界面音效。
    /// 现场嫌吵或需要静音分发时可一键关闭。
    /// </summary>
    public bool SplashSound { get; set; } = true;

    /// <summary>
    /// 启动动画速度倍率（app.ini SplashSpeed，有效范围 0.35–3，默认 1 = 原速）。
    /// </summary>
    public double SplashSpeed { get; set; } = SplashConstants.DefaultSpeed;

    /// <summary>
    /// 启动动画是否要求人工确认（app.ini SplashRequireInteraction，默认关 = 自动连播）。
    /// 开启后第 1、2 步需点击按钮/回车才继续，整段时长由操作者决定。
    /// </summary>
    public bool SplashRequireInteraction { get; set; }

    /// <summary>
    /// 启动动画兜底超时（秒，有效范围 10–180）：脚本异常等情况下到点强制放行，
    /// 绝不让用户停留在启动画面。
    /// </summary>
    public int SplashTimeoutSeconds { get; set; } = SplashConstants.DefaultTimeoutSeconds;

    /// <summary>
    /// 安卓端系统栏形态（app.ini [Android] FullscreenMode，默认 Immersive）：
    /// - Immersive：沉浸式全屏，隐藏状态栏与导航栏（下滑临时呼出），内容顶到屏幕最上沿。
    /// - EdgeToEdge：状态栏透明、内容延伸到底下，但时钟/电量仍可见。
    /// 仅 Android 生效；Windows 无系统栏概念，忽略本项。
    /// </summary>
    public bool AndroidImmersiveFullscreen { get; set; } = true;

    /// <summary>
    /// 验证配置是否完整
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Version))
            throw new ConfigurationException("AppOptions.Version", "应用版本号未配置");

        if (string.IsNullOrWhiteSpace(ApplicationName))
            throw new ConfigurationException("AppOptions.ApplicationName", "应用名称未配置");

        if (string.IsNullOrWhiteSpace(WindowTitle))
            throw new ConfigurationException("AppOptions.WindowTitle", "窗口标题未配置");
    }
}
