using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.Printing;

public interface IPrintExecuteService
{
    Task<Result<byte[]>> GeneratePreviewPdfAsync(
        long templateId,
        Dictionary<string, string> fields,
        List<Dictionary<string, string>> tableRows,
        CancellationToken ct = default);

    Task<Result<PrintRecord>> ExecutePrintAsync(
        long templateId,
        string templateName,
        string businessType,
        long? businessId,
        string batchNo,
        Dictionary<string, string> fields,
        List<Dictionary<string, string>> tableRows,
        string applicantName,
        string applicantIdCard,
        string printerName = null,
        int copies = 1,
        bool isDuplex = false,
        bool printToPrinter = true,
        CancellationToken ct = default);
}
