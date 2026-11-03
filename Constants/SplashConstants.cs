namespace NewCosmos.Constants;

/// <summary>
/// 启动动画参数（单一来源）：app.ini 解析边界、兜底超时、速度上下限共用。
/// 动画本体的配置键在 Resources\Splash\splash.js 的 DEFAULTS，此处只维护宿主侧边界。
/// </summary>
public static class SplashConstants
{
    /// <summary>速度倍率下限（与 splash.js 内部 clamp 一致）</summary>
    public const double MinSpeed = 0.35;

    /// <summary>速度倍率上限</summary>
    public const double MaxSpeed = 3.0;

    /// <summary>默认速度倍率（1 = 原速，整段约 16 秒）</summary>
    public const double DefaultSpeed = 1.0;

    /// <summary>兜底超时下限（秒）</summary>
    public const int MinTimeoutSeconds = 10;

    /// <summary>兜底超时上限（秒）</summary>
    public const int MaxTimeoutSeconds = 180;

    /// <summary>默认兜底超时（秒）：整段动画约 16 秒，留足慢速与音频时长余量</summary>
    public const int DefaultTimeoutSeconds = 40;
}
