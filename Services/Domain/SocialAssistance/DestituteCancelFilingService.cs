using NewCosmos.Constants;
using NewCosmos.Models.NavigationData;
using NewCosmos.Services.Core;
using NewCosmos.Services.System;
using ApplicationEntity = NewCosmos.Models.Entities.Application;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 特困供养人员取消（停止）情况表 · 退出直出服务实现。
/// 直出自包含（TemplateFilter 强白名单 + OutputCategories 双挂「停止/唯一叶子」），
/// 不依赖 ArchiveCategoryResolver 停止分支，故可在任意特困停发入口复用（全场景）。
/// 字段装配与年保障金额计算复用 <see cref="DestitutePrintDataBuilder"/>（单点）。
/// </summary>
public class DestituteCancelFilingService : BaseService, IDestituteCancelFilingService
{
    protected override string ServiceName => "DestituteCancelFilingService";

    /// <summary>取消备案表模板分类（加载命中用）：唯一叶子 +「停止」位；模板侧 categories 与此一致。</summary>
    private static readonly string[] CancelFilingCategories = ["档案_特困取消备案表", "停止"];

    private readonly IApplicationService _applicationService;
    private readonly IStandardConfigService _standardConfigService;

    public DestituteCancelFilingService(
        IApplicationService applicationService,
        IStandardConfigService standardConfigService,
        ILoggerService logger) : base(logger)
    {
        _applicationService = applicationService;
        _standardConfigService = standardConfigService;
    }

    public async Task<bool> PrepareNavigationAsync(long applicationId, string? stopReasonKey, CancellationToken ct = default)
    {
        var appResult = await _applicationService.GetByIdAsync(applicationId, ct);
        if (!appResult.IsSuccess || appResult.Value is null)
        {
            LogWarn($"特困取消备案表：申请 {applicationId} 取数失败——{appResult.Message}，取消直出");
            return false;
        }
        var app = appResult.Value;

        var fields = await DestitutePrintDataBuilder.BuildSingleFieldsAsync(_standardConfigService, app, stopReasonKey, ct);
        var classification = app.ClassificationResult ?? string.Empty;

        PrintNavigationData.ClearDocumentMode();
        PrintNavigationData.BusinessType = "FamilyApplication";
        PrintNavigationData.BusinessId = applicationId;
        PrintNavigationData.Classification = classification;
        PrintNavigationData.FieldData = fields;
        PrintNavigationData.TableData = new();
        PrintNavigationData.SupporterTableData = null;
        // TemplateFilter 强白名单：仅出取消备案表、跳过变动文书事实过滤/补挂核查报告（退出时专用）
        PrintNavigationData.TemplateFilter = [DocumentTemplateNames.DestituteCancelFiling];
        PrintNavigationData.OutputCategories = CancelFilingCategories;

        LogInfo($"特困取消备案表：申请 {applicationId} 直出已就绪（分类={classification}，原因={stopReasonKey}）");
        return true;
    }
}
