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
        catch
        {
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
        catch
        {
        }
        return string.Empty;
    }
}