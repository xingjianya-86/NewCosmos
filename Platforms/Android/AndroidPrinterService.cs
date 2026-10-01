using NewCosmos.Services.Platform;

namespace NewCosmos.Services.Platform;

/// <summary>
/// Android 打印服务 stub - 数据录入无需打印
/// </summary>
public class AndroidPrinterService : IPrinterService
{
    public List<string> GetInstalledPrinters()
    {
        // Android 打印由系统 PrintManager 管理，此处仅返回空列表
        return [];
    }

    public string GetDefaultPrinter()
    {
        return string.Empty;
    }
}
