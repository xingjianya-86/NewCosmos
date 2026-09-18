using NewCosmos.Services.Core;
using NewCosmos.Services.Templates;

namespace NewCosmos.Services.Domain.ArchiveManagement;

public class DocumentGenerateResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public List<DocumentFileInfo> Files { get; set; } = new();
}

public class DocumentFileInfo
{
    public long TemplateId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSize { get; set; }
}

public interface IDocumentService
{
    Task<DocumentGenerateResult> GenerateDocumentsAsync(long applicationId, List<long> templateIds, string outputDirectory);
    Task<DocumentGenerateResult> GenerateSingleAsync(long templateId, object dataContext, string outputPath);
    Task<byte[]> RenderWithPagingAsync(long templateId, Dictionary<string, string> fields,
        List<string> tablePlaceholders, List<Dictionary<string, string>> rows, int pageSize, CancellationToken ct = default);
}

public class DocumentService : BaseService, IDocumentService
{
    private readonly ITemplateService _templateService;
    private readonly ITemplateEngineFactory _engineFactory;

    protected override string ServiceName => "DocumentService";

    public DocumentService(ITemplateService templateService, ITemplateEngineFactory engineFactory, ILoggerService logger)
        : base(logger)
    {
        _templateService = templateService;
        _engineFactory = engineFactory;
    }

    /// <summary>
    /// 功能未完成（2026-07 死代码清理时定性）：
    /// 旧实现的 fields 字典与 tableRows 始终为空，从未装配任何业务数据，
    /// 生成的是空白文档却返回 Success=true（静默失败）。
    /// 现改为诚实返回失败。当前该服务已无任何调用方：
    /// ArchiveOutputViewModel 的注入已移除，MauiProgram 的 DI 注册已删除。
    /// 如需启用此功能，必须先实现申请数据 → 模板占位符的装配逻辑（旧实现见版本历史）。
    /// </summary>
    public Task<DocumentGenerateResult> GenerateDocumentsAsync(long applicationId, List<long> templateIds, string outputDirectory)
    {
        LogError("GenerateDocumentsAsync 被调用，但数据装配逻辑尚未实现");
        return Task.FromResult(new DocumentGenerateResult
        {
            Success = false,
            ErrorMessage = "功能未完成：文档字段/表格数据装配逻辑尚未实现，拒绝生成空白文档"
        });
    }

    public Task<DocumentGenerateResult> GenerateSingleAsync(long templateId, object dataContext, string outputPath)
    {
        return GenerateDocumentsAsync(0, new List<long> { templateId }, Path.GetDirectoryName(outputPath)!);
    }

    public async Task<byte[]> RenderWithPagingAsync(
        long templateId,
        Dictionary<string, string> fields,
        List<string> tablePlaceholders,
        List<Dictionary<string, string>> rows,
        int pageSize,
        CancellationToken ct = default)
    {
        var template = await _templateService.GetByIdAsync(templateId);
        if (template == null)
            throw new InvalidOperationException($"模板不存在: {templateId}");

        var templateBin = await _templateService.GetFileDataAsync(templateId);
        if (templateBin == null || templateBin.Length == 0)
            throw new InvalidOperationException($"模板内容为空: {templateId}");

        using var engine = _engineFactory.CreateEngine(template.FileType);
        return await engine.RenderWithPagingAsync(templateBin, fields, tablePlaceholders, rows, pageSize, ct);
    }
}
