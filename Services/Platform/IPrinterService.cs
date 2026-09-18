using NewCosmos.Models.Results;

namespace NewCosmos.Services.Platform;

public interface IPrinterService
{
    List<string> GetInstalledPrinters();

    string GetDefaultPrinter();
}