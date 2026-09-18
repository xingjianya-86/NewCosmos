using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.Printing;

public interface IPrintRecordService
{
    Task<Result<PrintRecord>> SaveAsync(PrintRecord record, CancellationToken ct = default);

    Task<Result<List<PrintRecord>>> GetByBusinessAsync(string businessType, long businessId, CancellationToken ct = default);

    Task<Result<List<PrintRecord>>> GetByBatchNoAsync(string batchNo, CancellationToken ct = default);

    Task<Result<PrintRecord>> GetByIdAsync(long id, CancellationToken ct = default);

    Task<Result<List<PrintRecord>>> GetByTemplateAsync(long templateId, CancellationToken ct = default);
}
