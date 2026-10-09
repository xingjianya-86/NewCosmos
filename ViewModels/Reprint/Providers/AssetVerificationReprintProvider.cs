using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.AssetVerification;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.Templates;
using NewCosmos.ViewModels.ArchiveManagement;

namespace NewCosmos.ViewModels.Reprint.Providers;

/// <summary>
/// 资产核查域补打策略（自 ReprintSelectionViewModel 平移）：
/// 核查记录搜索（成员→户主归位、家庭聚合）+ 单模板直打（预览/打印/跨户批量）。
/// 相比旧页补齐：打印留痕写入 nc_biz_print_records（BusinessType=AssetVerification）。
/// </summary>
public class AssetVerificationReprintProvider : IReprintDomainProvider, IAssetVerificationReprintCapability
{
    private readonly IAssetVerificationService _verificationService;
    private readonly ITemplateService _templateService;
    private readonly ITemplateEngineFactory _engineFactory;
    private readonly IOrganizationService _organizationService;
    private readonly IDictCacheService _dictCacheService;
    private readonly AddressResolver _addressResolver;
    private readonly TemplateFieldBuilder _fieldBuilder;
    private readonly Services.Domain.Printing.IPrintRecordService _printRecordService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerService _logger;

    public AssetVerificationReprintProvider(
        IAssetVerificationService verificationService,
        ITemplateService templateService,
        ITemplateEngineFactory engineFactory,
        IOrganizationService organizationService,
        IDictCacheService dictCacheService,
        AddressResolver addressResolver,
        TemplateFieldBuilder fieldBuilder,
        Services.Domain.Printing.IPrintRecordService printRecordService,
        IServiceProvider serviceProvider,
        ILoggerService logger)
    {
        _verificationService = verificationService;
        _templateService = templateService;
        _engineFactory = engineFactory;
        _organizationService = organizationService;
        _dictCacheService = dictCacheService;
        _addressResolver = addressResolver;
        _fieldBuilder = fieldBuilder;
        _printRecordService = printRecordService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public string DomainKey => "AssetVerification";
    public string DisplayName => "资产核查";
    public ReprintDomainMode Mode => ReprintDomainMode.AssetVerification;
    public ReprintMonthWindow MonthWindow => ReprintMonthWindow.EconomicReview;

    public async Task<Result<List<ReprintArchiveItem>>> SearchByPersonAsync(string keyword, int limit = 20, CancellationToken ct = default)
    {
        // keyword 为空 = 默认最近名单（核查记录按创建时间倒序）
        var kw = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
        var result = await _verificationService.GetTasksPagedAsync(1, limit, null, kw!, ct: ct);
        if (result.IsFailure)
            return Result.Failure<List<ReprintArchiveItem>>(result.ErrorCode!, result.Message ?? "核查记录搜索失败");

        return Result.Success(NormalizeToHeadEntries(result.Value?.Items ?? new List<AssetVerificationTask>()));
    }

    /// <summary>
    /// 成员→户主归位：核查表按人一条记录（relationship/is_head/head_id_card），
    /// 名单每户只出一条——取该户户主记录（is_head=true）；户主记录不在集内时取组内最新兜底。
    /// 补打数据构建（ResolveHeadRecordAsync/LoadFamilyMembersAsync）本就按户主记录聚合家庭，天然衔接。
    /// </summary>
    private List<ReprintArchiveItem> NormalizeToHeadEntries(IEnumerable<AssetVerificationTask> tasks)
    {
        var ordered = tasks.OrderByDescending(t => t.CreatedAt).ToList();
        return ordered
            .GroupBy(t => string.IsNullOrWhiteSpace(t.HeadIdCard) ? "self:" + t.ArchiveIdCard : t.HeadIdCard)
            .Select(g =>
            {
                var head = g.FirstOrDefault(t => t.IsHead) ?? g.First();
                return new ReprintArchiveItem(
                    DomainKey,
                    head.Id,
                    head.ArchiveName ?? "",
                    head.ArchiveIdCard ?? "",
                    head.BatchId ?? "",
                    head.Status ?? "",
                    head.CreatedAt,
                    $"{head.VerificationYear}年{head.VerificationMonth}月 · {head.StatusDisplay} · 该户{g.Count()}人核查");
            })
            .ToList();
    }

    public async Task<Result<List<ReprintArchiveItem>>> SearchByMonthAsync(int year, int month, int limit = 200, CancellationToken ct = default)
    {
        // 服务端按月查询（创建时间范围，与名单业务时间一致）
        var dateFrom = new DateTime(year, month, 1);
        var dateTo = dateFrom.AddMonths(1);
        var result = await _verificationService.GetTasksPagedAsync(1, limit, null, null, dateFrom, dateTo, ct);
        if (result.IsFailure)
            return Result.Failure<List<ReprintArchiveItem>>(result.ErrorCode!, result.Message ?? "核查记录按月查询失败");

        return Result.Success(NormalizeToHeadEntries(result.Value?.Items ?? new List<AssetVerificationTask>()));
    }

    public async Task<Result<ReprintArchivePayload>> PrepareAsync(long businessId, CancellationToken ct = default)
    {
        // AssetVerification 模式的打印走能力接口（按模板构建引擎），此处仅返回基础信息供中栏展示
        var headResult = await _verificationService.GetTasksPagedAsync(1, 1, null, null, ct: ct);
        return Result.Success(new ReprintArchivePayload(
            DomainKey, businessId, "", "", "AssetVerification", "",
            new Dictionary<string, string>(), new List<Dictionary<string, string>>(), null));
    }

    public async Task<List<TemplateSelectItem>> GetTemplatesAsync(CancellationToken ct = default)
    {
        var templates = await _templateService.GetByCategoriesAsync(new[] { "AssetVerification" });
        return templates.Select(t => new TemplateSelectItem
        {
            TemplateId = t.Id,
            Name = t.Name,
            FileType = t.FileType
        }).ToList();
    }

    public async Task<Result<string>> RenderPreviewAsync(long recordId, TemplateSelectItem template, CancellationToken ct = default)
    {
        var record = await LoadRecordAsync(recordId, ct);
        if (record == null)
            return Result.Failure<string>("NOT_FOUND", "核查记录不存在");

        try
        {
            var engineResult = await CreateFilledEngineForRecordAsync(record, template, ct);
            if (engineResult.IsFailure)
                return Result.Failure<string>(engineResult.ErrorCode, string.IsNullOrEmpty(engineResult.Message) ? "构建打印数据失败" : engineResult.Message);

            using var engine = engineResult.Value;

            // 用 ExportPdfAsync 返回内存字节，避免碰输出目录的文件
            var pdfBytes = await engine.ExportPdfAsync(ct);
            if (pdfBytes == null || pdfBytes.Length == 0)
                return Result.Failure<string>(ErrorCodes.DOCUMENT_GENERATION_FAILED, "PDF 生成失败");

            var storageOptions = _serviceProvider.GetRequiredService<StorageOptions>();
            var tempDir = storageOptions.GetTempPath("ReprintPreview");
            Directory.CreateDirectory(tempDir);
            var previewPath = Path.Combine(tempDir, $"reprint_preview_{record.ArchiveIdCard}_{template.TemplateId}.pdf");
            await File.WriteAllBytesAsync(previewPath, pdfBytes, ct);

            _logger.LogBusiness("补打预览生成成功", ("Template", template.Name), ("Record", record.ArchiveName));
            return Result.Success(previewPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "补打预览生成失败");
            return Result.FromException<string>(ex);
        }
    }

    public async Task<Result<string>> PrintSingleAsync(long recordId, TemplateSelectItem template, bool generatePdf, bool printToPrinter = true, CancellationToken ct = default)
    {
        var record = await LoadRecordAsync(recordId, ct);
        if (record == null)
            return Result.Failure<string>("NOT_FOUND", "核查记录不存在");

        try
        {
            var category = OutputPathHelper.GetCategoryDisplayName("AssetVerification");
            var ext = TemplateFileTypes.ToExtension(template.FileType);
            var sourceFilePath = OutputPathHelper.GetFilePath(category, record.ArchiveName, record.ArchiveIdCard, template.Name, ext);
            OutputPathHelper.EnsureDirectoryExists(category, record.ArchiveName, record.ArchiveIdCard);
            TryDeleteFile(sourceFilePath);

            var engineResult = await CreateFilledEngineForRecordAsync(record, template, ct);
            if (engineResult.IsFailure)
                return Result.Failure<string>(engineResult.ErrorCode, string.IsNullOrEmpty(engineResult.Message) ? "构建打印数据失败" : engineResult.Message);

            using var engine = engineResult.Value;

            await engine.SaveToFileAsync(sourceFilePath, ct);
            if (printToPrinter)
                await engine.PrintFromFileAsync(sourceFilePath, 1, ct);

            var pdfPath = "";
            if (generatePdf)
            {
                pdfPath = Path.ChangeExtension(sourceFilePath, ".pdf");
                await engine.ExportPdfFromFileAsync(sourceFilePath, pdfPath, ct);
            }

            await WritePrintRecordAsync(record, template, sourceFilePath, pdfPath, $"DMR核查{DateTime.Now:yyyyMMddHHmmssfff}-{record.Id}", ct);
            _logger.LogBusiness(printToPrinter ? "补打打印完成" : "补打生成完成(未打印)",
                ("Template", template.Name), ("File", sourceFilePath));
            return Result.Success(sourceFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "补打打印失败");
            return Result.FromException<string>(ex);
        }
    }

    public async Task<Result<(int SuccessCount, List<(string Name, string Error)> Failed)>> BatchPrintAsync(
        IReadOnlyList<long> recordIds,
        TemplateSelectItem template,
        bool generatePdf,
        Action<int, int, string>? progress,
        CancellationToken ct = default)
    {
        var failedItems = new List<(string Name, string Error)>();
        var successCount = 0;

        var category = OutputPathHelper.GetCategoryDisplayName("AssetVerification");
        var ext = TemplateFileTypes.ToExtension(template.FileType);

        for (var i = 0; i < recordIds.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var record = await LoadRecordAsync(recordIds[i], ct);
            if (record == null)
            {
                failedItems.Add(($"ID={recordIds[i]}", "核查记录不存在"));
                continue;
            }

            progress?.Invoke(i + 1, recordIds.Count, record.ArchiveName);
            try
            {
                var sourceFilePath = OutputPathHelper.GetFilePath(category, record.ArchiveName, record.ArchiveIdCard, template.Name, ext);
                OutputPathHelper.EnsureDirectoryExists(category, record.ArchiveName, record.ArchiveIdCard);
                TryDeleteFile(sourceFilePath);

                var engineResult = await CreateFilledEngineForRecordAsync(record, template, ct);
                if (engineResult.IsFailure)
                {
                    failedItems.Add((record.ArchiveName,
                        string.IsNullOrEmpty(engineResult.Message) ? "构建打印数据失败" : engineResult.Message));
                    continue;
                }

                using var engine = engineResult.Value;

                await engine.SaveToFileAsync(sourceFilePath, ct);
                await engine.PrintFromFileAsync(sourceFilePath, 1, ct);

                var pdfPath = "";
                if (generatePdf)
                {
                    pdfPath = Path.ChangeExtension(sourceFilePath, ".pdf");
                    await engine.ExportPdfFromFileAsync(sourceFilePath, pdfPath, ct);
                }

                await WritePrintRecordAsync(record, template, sourceFilePath, pdfPath, $"DMR核查{DateTime.Now:yyyyMMddHHmmssfff}-B{recordIds.Count}", ct);
                successCount++;
            }
            catch (Exception ex)
            {
                failedItems.Add((record.ArchiveName, ex.Message));
                _logger.LogError(ex, $"批量打印单条失败: {record.ArchiveName}");
            }
        }

        return Result.Success((successCount, failedItems));
    }

    // ========================
    //  私有辅助（自 ReprintSelectionViewModel 平移）
    // ========================

    private async Task<AssetVerificationTask?> LoadRecordAsync(long recordId, CancellationToken ct)
    {
        var result = await _verificationService.GetTaskByIdAsync(recordId, ct);
        return result.IsSuccess ? result.Value : null;
    }

    /// <summary>
    /// 构建填好的模板引擎。
    /// Failure = 字段构建/模板配置解析失败（配置损坏时必须显式失败，禁止降级为空配置——否则占位符残留缺字）；
    /// Success 恒返回非空引擎（调用方不再以 null 判失败）。
    /// </summary>
    private async Task<Result<ITemplateEngine>> CreateFilledEngineForRecordAsync(AssetVerificationTask record, TemplateSelectItem template, CancellationToken ct)
    {
        // 1. 查找户主
        var headRecord = await ResolveHeadRecordAsync(record, ct);

        // 2. 查找同户所有家庭成员
        var familyMembers = await LoadFamilyMembersAsync(record, headRecord, ct);

        // 3. 获取组织信息和操作员
        var operatorName = await GetCivilAssistantNameAsync();
        var orgInfo = await GetOrganizationInfoAsync();

        // 4. 加载 application 档案数据
        var appFields = await _verificationService.GetApplicationFieldsByIdCardAsync(headRecord.ArchiveIdCard, ct);

        // 5. 构建原始数据
        var rawData = new RawFieldData
        {
            RecordId = headRecord.Id,
            ApplicantName = headRecord.ArchiveName,
            ApplicantIdCard = headRecord.ArchiveIdCard,
            ApplicantIdType = headRecord.ApplicantIdType,
            ApplicationReason = headRecord.ApplicationReason,
            ApplicationDate = headRecord.ApplicationDate,
            ContactPhone = headRecord.ContactPhone,
            HeadIdCard = headRecord.ArchiveIdCard,
            Status = headRecord.Status,
            Community = headRecord.Community,
            DetailAddress = headRecord.FamilyAddress,
            OperatorName = operatorName,
            OperatorUnit = orgInfo.UnitName,
            District = orgInfo.District,
            Town = orgInfo.Town,
            Bureau = orgInfo.ParentName,
            BureauPhone = orgInfo.ParentPhone,
            UnitPhone = orgInfo.UnitPhone,
            FamilySize = familyMembers.Count,
            FamilyMembers = familyMembers.Select(m => new FamilyMemberData
            {
                Name = m.ArchiveName,
                IdCard = m.ArchiveIdCard,
                IdTypeDisplay = _dictCacheService.GetValue(DictionaryTypeCodes.IdTypes, m.ApplicantIdType ?? ""),
                Relationship = m.Relationship,
                Community = m.Community,
                FamilyAddress = m.FamilyAddress
            }).ToList(),
            ApplicationFields = appFields,
        };

        // 6. 统一构建字段（模板配置解析失败 → 显式 Failure，不降级）
        var fieldsResult = await _fieldBuilder.BuildFieldsAsync(template.TemplateId, rawData, ct);
        if (fieldsResult.IsFailure)
            return Result<ITemplateEngine>.Failure(
                fieldsResult.ErrorCode,
                string.IsNullOrEmpty(fieldsResult.Message) ? "构建打印数据失败" : fieldsResult.Message);
        var fields = fieldsResult.Value;

        // 7. 取模板配置（先于引擎创建，失败时不留下未释放的引擎实例）
        var configResult = await GetTemplateConfigAsync(template.TemplateId, ct);
        if (configResult.IsFailure)
            return Result<ITemplateEngine>.Failure(
                configResult.ErrorCode,
                string.IsNullOrEmpty(configResult.Message) ? "模板配置不存在或解析失败" : configResult.Message);
        var config = configResult.Value;

        // 8. 创建引擎并填充
        var engine = await _engineFactory.CreateFromTemplateIdAsync(template.TemplateId, ct);
        engine.IsDuplex = true;

        if (config != null)
        {
            var placeholderFields = MapFieldsToPlaceholders(fields, config.Fields, config.IndexShifts);
            engine.ReplaceFields(placeholderFields);

            if (config.Tables.Count > 0)
            {
                var tableRows = BuildTableRows(familyMembers);
                var tableConfig = config.Tables[0];
                var placeholderRows = MapRowsToPlaceholders(tableRows, tableConfig.Columns);
                engine.ReplaceTableByPlaceholder(tableConfig.StartMarker, tableConfig.EndMarker, placeholderRows);
            }
        }
        else
        {
            engine.ReplaceFields(fields);
        }

        return Result<ITemplateEngine>.Success(engine);
    }

    /// <summary>加载同户所有家庭成员（与快速核查 GetHistoryByIdCardAsync 逻辑一致）</summary>
    private async Task<List<AssetVerificationTask>> LoadFamilyMembersAsync(AssetVerificationTask record, AssetVerificationTask headRecord, CancellationToken ct)
    {
        try
        {
            // 优先用 batch_id 查（与快速核查一致）
            if (!string.IsNullOrWhiteSpace(record.BatchId))
            {
                var batchResult = await _verificationService.GetFamilyMembersByBatchIdAsync(record.BatchId, ct);
                if (batchResult.IsSuccess && batchResult.Value != null && batchResult.Value.Count > 0)
                    return batchResult.Value;
            }

            // 回退到 head_id_card
            var headIdCard = headRecord.ArchiveIdCard ?? record.HeadIdCard;
            if (!string.IsNullOrWhiteSpace(headIdCard))
            {
                var headResult = await _verificationService.GetFamilyMembersAsync(headIdCard, ct);
                if (headResult.IsSuccess && headResult.Value != null && headResult.Value.Count > 0)
                    return headResult.Value;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"加载家庭成员失败: {ex.Message}");
        }

        // 最后回退：只有当前记录
        return new List<AssetVerificationTask> { record };
    }

    private List<Dictionary<string, string>> BuildTableRows(List<AssetVerificationTask> familyMembers)
    {
        return familyMembers.Select(m =>
        {
            var isHead = m.IsHead;
            var relDisplay = isHead
                ? PickerConstants.Relationship.HeadDisplay
                : (_dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, m.Relationship ?? "") is var rel && !string.IsNullOrEmpty(rel) ? rel : m.Relationship ?? "-");
            var memberIdTypeDisplay = _dictCacheService.GetValue(DictionaryTypeCodes.IdTypes, m.ApplicantIdType ?? "");

            return new Dictionary<string, string>
            {
                [FieldKeys.FAMILY_MEMBER_NAME] = m.ArchiveName ?? "-",
                [FieldKeys.FAMILY_MEMBER_ID_CARD] = m.ArchiveIdCard ?? "-",
                [FieldKeys.FAMILY_MEMBER_RELATION] = relDisplay,
                [FieldKeys.FAMILY_MEMBER_CERT_TYPE] = memberIdTypeDisplay,
                [FieldKeys.FAMILY_MEMBER_ADDRESS] = _addressResolver.BuildAddress(m.Community, m.FamilyAddress)
            };
        }).ToList();
    }

    /// <summary>如果选中的是家庭成员，通过 head_id_card 查找户主记录</summary>
    private async Task<AssetVerificationTask> ResolveHeadRecordAsync(AssetVerificationTask record, CancellationToken ct)
    {
        // 如果没有 head_id_card 或者就是本人，直接返回
        if (string.IsNullOrWhiteSpace(record.HeadIdCard)
            || record.HeadIdCard == record.ArchiveIdCard)
            return record;

        try
        {
            // 用 head_id_card 搜索户主
            var result = await _verificationService.GetTasksPagedAsync(
                1, 1, null, record.HeadIdCard, ct: ct);

            if (result.IsSuccess && result.Value?.Items != null && result.Value.Items.Count > 0)
            {
                var head = result.Value.Items[0];
                _logger.LogBusiness("补打查找户主",
                    ("Member", record.ArchiveName ?? ""),
                    ("Head", head.ArchiveName ?? ""));
                return head;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"查找户主失败: {ex.Message}");
        }

        return record;
    }

    private static Dictionary<string, string> MapFieldsToPlaceholders(
        Dictionary<string, string> fields,
        List<TemplateFieldMapping> fieldMappings,
        IReadOnlyList<IndexShiftRule>? indexShifts = null)
    {
        fields = TemplateFieldResolver.Project(fields, indexShifts);
        var result = new Dictionary<string, string>();
        foreach (var mapping in fieldMappings)
        {
            if (fields.TryGetValue(mapping.FieldKey, out var value))
                result[mapping.Placeholder] = TemplateFieldMapping.ApplyFormat(value, mapping.Format);
            else if (!string.IsNullOrEmpty(mapping.DefaultValue))
                result[mapping.Placeholder] = mapping.DefaultValue;
        }
        return result;
    }

    private static List<Dictionary<string, string>> MapRowsToPlaceholders(
        List<Dictionary<string, string>> rows,
        List<TemplateFieldMapping> columnMappings)
    {
        var result = new List<Dictionary<string, string>>();
        foreach (var row in rows)
        {
            var mappedRow = new Dictionary<string, string>();
            foreach (var col in columnMappings)
            {
                if (row.TryGetValue(col.FieldKey, out var value))
                    mappedRow[col.Placeholder] = TemplateFieldMapping.ApplyFormat(value, col.Format);
                else if (!string.IsNullOrEmpty(col.DefaultValue))
                    mappedRow[col.Placeholder] = col.DefaultValue;
            }
            result.Add(mappedRow);
        }
        return result;
    }

    private sealed record OrganizationInfo(string District, string Town, string UnitName, string ParentName, string ParentPhone, string UnitPhone);

    private async Task<OrganizationInfo> GetOrganizationInfoAsync()
    {
        var empty = new OrganizationInfo("-", "-", "-", "-", "-", "-");
        try
        {
            var orgId = App.CurrentUserOrganizationId;
            if (!orgId.HasValue)
                return empty;

            var orgResult = await _organizationService.GetByIdAsync(orgId.Value);
            if (orgResult.IsFailure || orgResult.Value == null)
                return empty;

            var org = orgResult.Value;
            var district = org.CountyName ?? "-";
            var town = org.TownName ?? "-";
            var unitName = org.Name ?? "-";
            var unitPhone = org.Phone ?? "-";
            var parentName = org.ParentName ?? "-";
            var parentPhone = "-";

            if (org.ParentId.HasValue)
            {
                var parentResult = await _organizationService.GetByIdAsync(org.ParentId.Value);
                if (parentResult.IsSuccess && parentResult.Value != null)
                    parentPhone = parentResult.Value.Phone ?? "-";
            }

            return new OrganizationInfo(district, town, unitName, parentName, parentPhone, unitPhone);
        }
        catch
        {
            return empty;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>
    /// Success(null) = 模板未配置 ConfigJson（走裸字段替换，属正常形态）；
    /// Failure = ConfigJson 解析失败（配置损坏），必须显式失败——降级会打印出占位符残留/缺字。
    /// </summary>
    private async Task<Result<TemplateConfig?>> GetTemplateConfigAsync(long templateId, CancellationToken ct)
    {
        try
        {
            var templateResult = await _templateService.GetByIdAsync(templateId, ct);
            if (templateResult.IsFailure)
                return Result.Failure<TemplateConfig?>(
                    templateResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    $"读取模板失败: TemplateId={templateId}, {templateResult.Message}");
            var template = templateResult.Value;
            if (template == null || string.IsNullOrEmpty(template.ConfigJson))
                return Result.Success<TemplateConfig?>(null);

            return Result.Success<TemplateConfig?>(TemplateConfig.FromJson(template.ConfigJson));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "补打-模板配置解析失败 TemplateId=" + templateId);
            return Result.Failure<TemplateConfig?>(
                ErrorCodes.FILE_FORMAT_ERROR,
                $"模板配置解析失败: TemplateId={templateId}");
        }
    }

    private async Task<string> GetCivilAssistantNameAsync()
    {
        try
        {
            var userId = App.CurrentUserId;
            if (userId.HasValue)
            {
                var userService = _serviceProvider.GetRequiredService<IUserService>();
                var user = await userService.GetByIdAsync(userId.Value);
                return user?.Value?.FullName ?? "未配";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "补打-获取操作员姓名失败");
        }
        return "未配";
    }

    /// <summary>打印留痕写入（旧补打页缺失，此处补齐；失败仅告警不阻断）</summary>
    private async Task WritePrintRecordAsync(AssetVerificationTask record, TemplateSelectItem template,
        string sourceFilePath, string pdfPath, string batchNo, CancellationToken ct)
    {
        try
        {
            var saveResult = await _printRecordService.SaveAsync(new PrintRecord
            {
                BatchNo = batchNo,
                BusinessType = DomainKey,
                BusinessId = record.Id,
                TemplateId = template.TemplateId,
                TemplateName = template.Name,
                PdfData = [],
                PdfSize = 0,
                SourceData = [],
                SourceType = "",
                FilePath = sourceFilePath,
                PdfPath = pdfPath,
                PrinterName = "",
                Copies = 1,
                OperatorId = App.CurrentUserId,
                OperatorName = App.CurrentUserFullName ?? "",
                Status = "Completed",
                Remark = "统一补打中心·资产核查直打"
            }, ct);
            if (saveResult.IsFailure)
                _logger.Warn($"资产核查补打留痕写入失败: {record.ArchiveName}, {saveResult.Message}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"资产核查补打留痕写入异常: {record.ArchiveName}, {ex.Message}");
        }
    }
}
