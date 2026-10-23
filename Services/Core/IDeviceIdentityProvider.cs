namespace NewCosmos.Services.Core;

/// <summary>
/// 设备身份（安装级，不采集硬件指纹）：用于会话的 device_id 比对与在线列表展示。
/// </summary>
public interface IDeviceIdentityProvider
{
    /// <summary>安装级稳定标识（Preferences 持久化，重装应用会变化）</summary>
    string DeviceId { get; }

    /// <summary>端类型：desktop / android（SessionConstants.DeviceKind*）</summary>
    string DeviceKind { get; }

    /// <summary>设备显示名：计算机名 / 手机厂商型号</summary>
    string DeviceName { get; }
}
