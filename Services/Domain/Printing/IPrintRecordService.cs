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

    /// <summary>
    /// 按业务取最近打印留痕（含 pdf 实际大小与文件路径，档案留痕列表用）
    /// </summary>
    Task<Result<List<PrintRecord>>> GetRecentByBusinessAsync(string businessType, long businessId, int limit, CancellationToken ct = default);

    /// <summary>
    /// 取留痕 PDF 内容（pdf_data + pdf_path，原样补打用）；无记录 Value 为 null
    /// </summary>
    Task<Result<PrintRecord?>> GetPdfContentByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// 在本批次打印留痕上追加"工单办理完成"备注（完成工单留痕用）；返回追加行数，无留痕时 0 行也算成功
    /// </summary>
    Task<Result<int>> MarkBatchWorkOrderCompletedAsync(string batchNo, string remark, CancellationToken ct = default);
}
