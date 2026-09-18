using NewCosmos.Models.Options;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;

namespace NewCosmos.Services.Templates;

public interface ITemplateEngineFactory
{
    ITemplateEngine CreateEngine(string fileType);

    ITemplateEngine CreateFromTemplate(Models.Entities.Template template);

    Task<ITemplateEngine> CreateFromTemplateIdAsync(long templateId, CancellationToken ct = default);
}

public class TemplateEngineFactory : ITemplateEngineFactory
{
    private readonly ILoggerService _logger;
    private readonly ITemplateService _templateService;
    private readonly PerformanceOptions _perfOptions;
    private readonly StorageOptions _storageOptions;

    /// <summary>
    /// 模板字节缓存：templateId → (updated_at, file_type, 模板字节)。
    /// 以 updated_at 为失效依据（每次命中只查一行元数据，不再重复下载 BYTEA）。
    /// 原实现每次打印都下载整个模板 BLOB 并重新解析；赡养人模式在循环内逐人下载。
    /// </summary>
    private readonly global::System.Collections.Concurrent.ConcurrentDictionary<long, (DateTime? UpdatedAt, string FileType, byte[] Data)> _templateCache = new();

    public TemplateEngineFactory(ILoggerService logger, ITemplateService templateService, PerformanceOptions perfOptions, StorageOptions storageOptions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
        _perfOptions = perfOptions;
        _storageOptions = storageOptions ?? throw new ArgumentNullException(nameof(storageOptions));
    }

    public ITemplateEngine CreateEngine(string fileType)
    {
        _logger.Debug($"执行: {fileType}");

        return fileType.ToLowerInvariant() switch
        {
            "docx" or "word" => new WordEngine(_logger, _perfOptions, _storageOptions),
            "xlsx" or "excel" => new ExcelEngine(_logger, _perfOptions, _storageOptions),
            _ => throw new NotSupportedException($"不支持的模板类型: {fileType}")
        };
    }

    public ITemplateEngine CreateFromTemplate(Models.Entities.Template template)
    {
        if (template == null)
            throw new ArgumentNullException(nameof(template));

        if (template.FileData == null || template.FileData.Length == 0)
            throw new ArgumentException("模板文件内容为空", nameof(template));

        _logger.Debug($"执行: {template.FileType}");

        var engine = CreateEngine(template.FileType);
        engine.Load(template.FileData);
        return engine;
    }

    public async Task<ITemplateEngine> CreateFromTemplateIdAsync(long templateId, CancellationToken ct = default)
    {
        _logger.Debug($"执行: {templateId}");

        var template = await _templateService.GetByIdAsync(templateId, ct);
        if (template == null)
            throw new InvalidOperationException($"模板不存在: {templateId}");

        // 缓存命中且 updated_at 未变化 → 复用已下载的模板字节，省去 BYTEA 下载
        if (_templateCache.TryGetValue(templateId, out var cached)
            && cached.UpdatedAt == template.UpdatedAt
            && cached.FileType == template.FileType)
        {
            template.FileData = cached.Data;
        }
        else
        {
            template.FileData = await _templateService.GetFileDataAsync(templateId, ct);
            if (template.FileData is { Length: > 0 })
            {
                _templateCache[templateId] = (template.UpdatedAt, template.FileType, template.FileData);
            }
        }

        if (template.FileData == null || template.FileData.Length == 0)
            throw new InvalidOperationException($"模板文件内容为空: {templateId}");

        return CreateFromTemplate(template);
    }
}
