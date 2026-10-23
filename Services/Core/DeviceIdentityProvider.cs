using NewCosmos.Constants;

namespace NewCosmos.Services.Core;

/// <summary>
/// 设备身份实现：首次使用生成 GUID 存 Preferences，之后复用。
/// 桌面取计算机名，Android 取厂商 + 型号；不读取 IMEI/序列号等硬件标识。
/// </summary>
public sealed class DeviceIdentityProvider : IDeviceIdentityProvider
{
    private const string DeviceIdKey = "newcosmos.device_id";
    private readonly Lazy<string> _deviceId;
    private readonly Lazy<string> _deviceName;

    public DeviceIdentityProvider()
    {
        _deviceId = new Lazy<string>(LoadOrCreateDeviceId, LazyThreadSafetyMode.ExecutionAndPublication);
        _deviceName = new Lazy<string>(LoadDeviceName, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string DeviceId => _deviceId.Value;

    public string DeviceKind => DeviceInfo.Platform == DevicePlatform.Android
        ? SessionConstants.DeviceKindAndroid
        : SessionConstants.DeviceKindDesktop;

    public string DeviceName => _deviceName.Value;

    private static string LoadOrCreateDeviceId()
    {
        try
        {
            var existing = Preferences.Default.Get(DeviceIdKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(existing))
                return existing;

            var created = Guid.NewGuid().ToString("N");
            Preferences.Default.Set(DeviceIdKey, created);
            return created;
        }
        catch
        {
            // Preferences 不可用时退化为进程内临时标识：本次会话仍可登录，
            // 重启后校验会因 device_id 不一致要求重新登录（安全侧兜底）。
            return Guid.NewGuid().ToString("N");
        }
    }

    private static string LoadDeviceName()
    {
        string name;
        if (DeviceInfo.Platform == DevicePlatform.Android)
        {
            name = $"{DeviceInfo.Manufacturer} {DeviceInfo.Model}".Trim();
        }
        else
        {
            try { name = Environment.MachineName; }
            catch { name = "Windows"; }
        }

        if (string.IsNullOrWhiteSpace(name))
            name = DeviceInfo.Platform == DevicePlatform.Android ? "Android" : "Windows";

        return name.Length > 100 ? name[..100] : name;
    }
}
