using NewCosmos.Models.Exceptions;

namespace NewCosmos.Models.Options;

/// <summary>
/// 网络接入配置（ZeroTier）。
/// 支持两种模式：Public=公共服务器（ZeroTier Central）；Private=私有化服务器（自建控制器）。
/// </summary>
public class NetworkOptions
{
    /// <summary>接入模式：Public / Private</summary>
    public string Mode { get; set; } = ModePrivate;

    /// <summary>私有化控制器网络ID</summary>
    public string PrivateNetworkId { get; set; } = string.Empty;

    /// <summary>Moon（私有化加速根中继）ZeroTier 地址，可空</summary>
    public string MoonId { get; set; } = string.Empty;

    /// <summary>公共（ZeroTier Central）网络ID</summary>
    public string PublicNetworkId { get; set; } = string.Empty;

    /// <summary>私有化接入后数据库主机（ZeroTier IP）</summary>
    public string DbHost { get; set; } = "127.0.0.1";

    /// <summary>数据库端口</summary>
    public int DbPort { get; set; } = 5432;

    /// <summary>网页授权地址（自建控制器 UI）</summary>
    public string WebControllerUrl { get; set; } = string.Empty;

    public const string ModePublic = "Public";
    public const string ModePrivate = "Private";

    public bool IsPublic => string.Equals(Mode, ModePublic, StringComparison.OrdinalIgnoreCase);

    /// <summary>当前模式生效的网络ID</summary>
    public string EffectiveNetworkId =>
        IsPublic ? (PublicNetworkId ?? string.Empty) : (PrivateNetworkId ?? string.Empty);

    /// <summary>校验：选中模式必须有对应网络ID</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(EffectiveNetworkId))
            throw new ConfigurationException("NetworkOptions",
                IsPublic ? "公共服务器模式需填写 ZeroTier 网络ID" : "私有化服务器模式需配置网络ID");
        if (DbPort <= 0)
            throw new ConfigurationException("NetworkOptions.DbPort", "数据库端口无效");
    }
}
