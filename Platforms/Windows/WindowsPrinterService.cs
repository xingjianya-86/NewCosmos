using System.Management;
using System.Runtime.Versioning;
using NewCosmos.Services.Platform;

namespace NewCosmos.Platforms.Windows;

[SupportedOSPlatform("windows10.0.17763.0")]
public class WindowsPrinterService : IPrinterService
{
    public List<string> GetInstalledPrinters()
    {
        var printers = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer");
            using var collection = searcher.Get();
            foreach (var printer in collection)
            {
                var name = printer["Name"].ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    printers.Add(name);
                }
            }
        }
        catch (Exception ex)
        {
            // 枚举失败降级空列表（打印入口自会提示无打印机）；留痕供排障
            Serilog.Log.Warning(ex, "[WindowsPrinterService] 枚举已安装打印机失败（WMI），返回空列表");
        }
        return printers;
    }

    public string GetDefaultPrinter()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_Printer WHERE Default = true");
            using var collection = searcher.Get();
            foreach (var printer in collection)
            {
                return printer["Name"]?.ToString() ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            // 查询失败返回空串（调用方按"无默认打印机"降级处理）；留痕供排障
            Serilog.Log.Warning(ex, "[WindowsPrinterService] 查询默认打印机失败（WMI），返回空");
        }
        return string.Empty;
    }
}