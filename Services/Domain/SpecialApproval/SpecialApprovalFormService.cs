using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.Templates;

using ApplicationEntity = NewCosmos.Models.Entities.Application;
using SpecialApprovalEntity = NewCosmos.Models.Entities.SpecialApproval;

namespace NewCosmos.Services.Domain.SpecialApproval;

public interface ISpecialApprovalFormService
{
    Task<SpecialApprovalForm?> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default);

    Task<Result> SaveDraftAsync(SpecialApprovalFormRequest request, CancellationToken ct = default);

    Task<Result> SubmitAsync(SpecialApprovalFormRequest request, CancellationToken ct = default);

    Task<Result> CompleteReviewAsync(SpecialApprovalReviewRequest request, CancellationToken ct = default);

    Task<Result<Archive>> GenerateDocumentAsync(long applicationId, CancellationToken ct = default);
}

public class SpecialApprovalFormService : BaseService, ISpecialApprovalFormService
{
    protected override string ServiceName => "SpecialApprovalFormService";
    private readonly IDatabaseService _db;
    private readonly IApplicationService _applicationService;
    private readonly IArchiveService _archiveService;
    private readonly ITemplateService _templateService;
    private readonly ITemplateEngineFactory _templateEngineFactory;
    private readonly IOrganizationService _organizationService;

    public SpecialApprovalFormService(
        IDatabaseService db,
        ILoggerService logger,
        IApplicationService applicationService,
        IArchiveService archiveService,
        ITemplateService templateService,
        ITemplateEngineFactory templateEngineFactory,
        IOrganizationService organizationService)
        : base(logger)
    {
        _db = db;
        _applicationService = applicationService;
        _archiveService = archiveService;
        _templateService = templateService;
        _templateEngineFactory = templateEngineFactory;
        _organizationService = organizationService;
    }

    // 原 TemplateNameKeyword（"一事一议申报表" 名称关键词）已删：模板定位改按
    // TemplateDocTypes.SpecialApprovalDeclaration 文档类型（方案B，2026-11-18）。

    public async Task<SpecialApprovalForm?> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        var sql = "SELECT " + WideTableColumns.SpecialApprovalFormsColumns + " FROM nc_biz_special_approval_forms WHERE application_id = $1 AND deleted_at IS NULL LIMIT 1";
        var result = await _db.QuerySingleAsync<SpecialApprovalForm>(sql, ct, applicationId);
        if (result.IsFailure)
        {
            // 查询失败不能当成"未保存"：SaveDraftAsync 会据此走 INSERT，产生一申请多份申报表。
            LogError($"查询一事一议申报表失败: {result.ErrorCode} {result.Message}");
            throw new BusinessException(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"查询一事一议申报表失败: {result.Message}");
        }
        return result.Value;
    }

    /// <summary>
    /// 保存草稿：存在则更新，不存在则新建（一个申请一份申报表）
    /// </summary>
    public async Task<Result> SaveDraftAsync(SpecialApprovalFormRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));
        if (string.IsNullOrWhiteSpace(request.SpecialMatters))
            return Result.Failure(ErrorCodes.REQUIRED_FIELD_MISSING, "请填写一事一议说明情况");

        var existing = await GetByApplicationIdAsync(request.ApplicationId, ct);
        // 已提交但未审议仍可修改内容（与 ViewModel IsEditable「草稿或已提交但未审议」同一契约）。
        // 原实现 Submitted 即拒绝：提交后新填内容无处可存、审议时被静默丢弃（用户实测「数据库可能未保存」）。
        // 已审议（Approved/Rejected）为终态，禁止修改。
        if (existing != null && existing.Id > 0
            && existing.Status is SpecialApprovalConstants.StatusApproved or SpecialApprovalConstants.StatusRejected)
            return Result.Failure(ErrorCodes.SPECIAL_APPROVAL_ALREADY_SUBMITTED, "申报表已完成会议审议，无法修改");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        if (existing != null && existing.Id > 0)
        {
            var updateSql = @"UPDATE nc_biz_special_approval_forms SET
                basic_situation = $1, special_matters = $2, template_key = $3,
                report_unit = $4, handler_name = $5, form_date = $6,
                audit_result = $7, audit_date = $8, updated_at = NOW()
                WHERE id = $9 AND deleted_at IS NULL";
            var updateResult = await _db.ExecuteNonQueryAsync(updateSql, ct,
                request.BasicSituation, request.SpecialMatters, request.TemplateKey,
                request.ReportUnit, request.HandlerName, request.FormDate,
                request.AuditResult, request.AuditDate, existing.Id);
            if (updateResult.IsFailure)
                return Result.Failure(updateResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, updateResult.Message);
        }
        else
        {
            var formNo = await GenerateFormNoAsync(request.ApplicationId, ct);
            var insertSql = @"INSERT INTO nc_biz_special_approval_forms
                (application_id, form_no, basic_situation, special_matters, template_key,
                 report_unit, handler_name, form_date, audit_result, audit_date,
                 status, created_by, created_at, updated_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,NOW(),NOW())";
            var insertResult = await _db.ExecuteNonQueryAsync(insertSql, ct,
                request.ApplicationId, formNo, request.BasicSituation, request.SpecialMatters, request.TemplateKey,
                request.ReportUnit, request.HandlerName, request.FormDate,
                request.AuditResult, request.AuditDate,
                SpecialApprovalConstants.StatusDraft, request.CreatedBy);
            if (insertResult.IsFailure)
                return Result.Failure(insertResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, insertResult.Message);
        }

        await tx.CommitAsync(ct);
        LogInfo($"申报表草稿已保存: ApplicationId={request.ApplicationId}");
        return Result.Success();
    }

    /// <summary>
    /// 提交申报（Draft→Submitted）
    /// </summary>
    public async Task<Result> SubmitAsync(SpecialApprovalFormRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));
        if (string.IsNullOrWhiteSpace(request.SpecialMatters))
            return Result.Failure(ErrorCodes.REQUIRED_FIELD_MISSING, "请填写一事一议说明情况");

        var existing = await GetByApplicationIdAsync(request.ApplicationId, ct);
        if (existing == null || existing.Id <= 0)
            return Result.Failure(ErrorCodes.SPECIAL_APPROVAL_FORM_NOT_FOUND, "请先保存申报表草稿");

        if (existing.Status != SpecialApprovalConstants.StatusDraft)
            return Result.Failure(ErrorCodes.SPECIAL_APPROVAL_ALREADY_SUBMITTED, "申报表已提交，无法重复提交");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        var sql = @"UPDATE nc_biz_special_approval_forms SET
            basic_situation = $1, special_matters = $2, template_key = $3,
            report_unit = $4, handler_name = $5, form_date = $6,
            audit_result = $7, audit_date = $8,
            status = $10, updated_at = NOW()
            WHERE id = $9 AND deleted_at IS NULL AND status = $11";
        var result = await _db.ExecuteNonQueryAsync(sql, ct,
            request.BasicSituation, request.SpecialMatters, request.TemplateKey,
            request.ReportUnit, request.HandlerName, request.FormDate,
            request.AuditResult, request.AuditDate, existing.Id,
            ApplicationStatusCodes.SUBMITTED, ApplicationStatusCodes.DRAFT);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message);

        await tx.CommitAsync(ct);
        LogInfo($"申报表已提交: ApplicationId={request.ApplicationId}, FormNo={existing.FormNo}");
        return Result.Success();
    }

    /// <summary>
    /// 会议审议闭环：写 nc_biz_special_approvals（最终会议审议结果）→ 更新申报表状态 → 回写申请。
    /// 通过：申报表 Approved，申请按指定分类施保（状态机 Draft→Submitted→Approved，IsSpecialApproval=true）。
    /// 驳回：申报表 Rejected，申请转 Refused（Draft 或 Submitted 均可）。
    /// </summary>
    public async Task<Result> CompleteReviewAsync(SpecialApprovalReviewRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));
        if (request.ApplicationId <= 0)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "申请ID无效");

        var form = await GetByApplicationIdAsync(request.ApplicationId, ct);
        if (form == null || form.Id <= 0)
            return Result.Failure(ErrorCodes.SPECIAL_APPROVAL_FORM_NOT_FOUND, "未找到一事一议申报表");

        if (form.Status is SpecialApprovalConstants.StatusApproved or SpecialApprovalConstants.StatusRejected)
            return Result.Failure(ErrorCodes.SPECIAL_APPROVAL_REVIEW_DONE, "该申报表已完成会议审议");

        if (request.Approved && !SpecialApprovalConstants.IsValidOverrideClassification(request.OverrideClassification))
            return Result.Failure(ErrorCodes.INVALID_CLASSIFICATION, "请选择有效的指定救助分类");

        var appResult = await _applicationService.GetByIdAsync(request.ApplicationId, ct);
        if (appResult.IsFailure || appResult.Value == null)
            return Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "未找到申请记录");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        // 1. 写会议审议结果（nc_biz_special_approvals）
        var approval = new SpecialApprovalEntity
        {
            ApplicationId = request.ApplicationId,
            MeetingDate = request.MeetingDate,
            MeetingMembers = request.MeetingMembers,
            SpecialReason = request.SpecialReason,
            SpecialCircumstances = request.SpecialCircumstances,
            OverrideClassification = request.OverrideClassification,
            ApproverName = request.ApproverName,
            ApproverTitle = request.ApproverTitle,
            ApproverOrganization = request.ApproverOrganization,
            CreatedBy = request.CreatedBy
        };
        // 会议地点/议题/纪要三栏 2026-11-17 移除（模板承载地点、议题纪要无读取方，全链路死字段）
        var insertApprovalSql = @"INSERT INTO nc_biz_special_approvals
            (application_id, meeting_date, meeting_members,
             special_reason, special_circumstances, override_classification,
             approver_name, approver_title, approver_organization, created_by, created_at, updated_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,NOW(),NOW())
            RETURNING id";
        var insertResult = await _db.ExecuteScalarAsync(insertApprovalSql, ct,
            request.ApplicationId, request.MeetingDate, request.MeetingMembers,
            request.SpecialReason, request.SpecialCircumstances,
            request.OverrideClassification, request.ApproverName, request.ApproverTitle,
            request.ApproverOrganization, request.CreatedBy);
        if (insertResult.IsFailure)
            return Result.Failure(insertResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, insertResult.Message);
        var specialApprovalId = insertResult.Value;

        // 2. 更新申报表状态
        var formStatus = request.Approved
            ? SpecialApprovalConstants.StatusApproved
            : SpecialApprovalConstants.StatusRejected;
        // 申报内容随审议一并落库（提交后内容仍可编辑，见 SaveDraftAsync 注释）：
        // 原实现只写会议字段，提交后新填的基本情况/说明在审议锁定时被静默丢弃。
        // SpecialReason 即 VM 的一事一议说明情况（与 SpecialCircumstances 同源）。
        var updateFormSql = @"UPDATE nc_biz_special_approval_forms SET
            status = $1, audit_result = $2, audit_date = $3,
            basic_situation = $4, special_matters = $5, template_key = $6,
            updated_at = NOW()
            WHERE id = $7 AND deleted_at IS NULL";
        var updateFormResult = await _db.ExecuteNonQueryAsync(updateFormSql, ct,
            formStatus, request.Approved ? "同意纳入" : "不同意纳入",
            request.MeetingDate == default ? (DateTime?)null : request.MeetingDate,
            request.BasicSituation, request.SpecialReason, request.TemplateKey, form.Id);
        if (updateFormResult.IsFailure)
            return Result.Failure(updateFormResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, updateFormResult.Message);

        // 3. 回写申请（2026-11-17 起：只写分类与标记，不迁移申请状态）
        if (request.Approved)
        {
            // 审议通过 = 确定指定救助分类与一事一议标记；申请保持原状态/步骤（正常为 Draft+step5
            // 「已建档未提交」），交回档案申报程序继续：档案制作出文书 → 完成归档（归档即审核
            // CompleteArchiveAsync）→ Approved+step6。
            // 原实现 Submit→Approve 直落终态会整段跳过档案制作管线；更早版本 Approved+step<6
            // 还曾让档案落在五个工作流 Tab 之外不可见（韩应成 id=111 实测，两种修法均已废弃）。
            var updateAppSql = @"UPDATE nc_biz_applications SET
                classification_result = $1, is_eligible = TRUE,
                is_special_approval = TRUE, special_approval_id = $2,
                updated_at = NOW()
                WHERE id = $3 AND deleted_at IS NULL";
            var updateAppResult = await _db.ExecuteNonQueryAsync(updateAppSql, ct,
                request.OverrideClassification, specialApprovalId, request.ApplicationId);
            if (updateAppResult.IsFailure)
                return Result.Failure(updateAppResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, updateAppResult.Message);
        }
        else
        {
            // 驳回：申请转 Refused
            var refuseResult = await _applicationService.ApproveAsync(new ApplicationApproveRequest
            {
                ApplicationId = request.ApplicationId,
                Approved = false,
                ApprovalComment = "一事一议审议驳回",
                ApprovedBy = request.CreatedBy
            }, ct);
            if (refuseResult.IsFailure)
                return Result.Failure(refuseResult.ErrorCode ?? ErrorCodes.OPERATION_FAILED, refuseResult.Message);
        }

        await tx.CommitAsync(ct);
        LogInfo($"一事一议审议完成: ApplicationId={request.ApplicationId}, Approved={request.Approved}, SpecialApprovalId={specialApprovalId}");
        return Result.Success();
    }

    /// <summary>
    /// 生成申报表文档：加载模板 → 组装字段 → 渲染 → 输出 xlsx/pdf → 归档到申请档案（幂等）
    /// </summary>
    public async Task<Result<Archive>> GenerateDocumentAsync(long applicationId, CancellationToken ct = default)
    {
        var form = await GetByApplicationIdAsync(applicationId, ct);
        if (form == null || form.Id <= 0)
            return Result.Failure<Archive>(ErrorCodes.SPECIAL_APPROVAL_FORM_NOT_FOUND, "未找到一事一议申报表");

        var appResult = await _applicationService.GetByIdAsync(applicationId, ct);
        if (appResult.IsFailure || appResult.Value == null)
            return Result.Failure<Archive>(ErrorCodes.APPLICATION_NOT_FOUND, "未找到申请记录");
        var app = appResult.Value;

        try
        {
            var template = await FindSpecialApprovalTemplateAsync(ct);
            if (template == null)
                return Result.Failure<Archive>(ErrorCodes.SPECIAL_APPROVAL_TEMPLATE_NOT_FOUND, "未找到一事一议申报表模板");

            using var engine = await _templateEngineFactory.CreateFromTemplateIdAsync(template.Id, ct);

            var fields = await BuildFieldDataAsync(app, form, ct);
            engine.ReplaceFields(fields);

            // 目录段用真实姓名（与打印/补打口径一致）：脱敏名含 '*' 是 Windows 非法文件名字符，
            // 会直接触发 ERROR_INVALID_NAME「文件名、目录名或卷标语法不正确」（…\韩**_111 实测）。
            // DataMasker 仅用于日志脱敏（§10），禁止进文件路径。
            var dir = OutputPathHelper.EnsureDirectoryExists(
                OutputPathHelper.GetCategoryDisplayName(PrintBusinessTypes.FamilyApplication),
                app.ApplicantName, app.Id.ToString(), DateTime.Today);

            var fileType = TemplateFileTypes.Normalize(template.FileType);
            var fileName = $"档案_乡镇社会救助一事一议申报表_{DateTime.Now:yyyyMMddHHmmssfff}.{fileType}";
            var outputPath = Path.Combine(dir, fileName);
            await engine.SaveToFileAsync(outputPath, ct);

            var pdfPath = OutputPathHelper.GetSiblingPath(outputPath, ".pdf");
            var pdfBytes = await engine.ExportPdfFromFileAsync(outputPath, pdfPath, ct);

            var archiveResult = await _archiveService.CreateOrAppendFilesAsync(
                applicationId,
                "SpecialApproval",
                app.ClassificationResult ?? "",
                App.CurrentUserFullName,
                new List<ArchiveFile>
                {
                    new()
                    {
                        TemplateId = template.Id.ToString(),
                        FileName = fileName,
                        FileType = fileType,
                        FileSize = new FileInfo(outputPath).Length,
                        OutputPath = outputPath
                    },
                    new()
                    {
                        TemplateId = template.Id.ToString(),
                        FileName = Path.GetFileName(pdfPath),
                        FileType = "pdf",
                        FileSize = pdfBytes?.Length ?? 0,
                        OutputPath = pdfPath
                    }
                }, ct);

            if (archiveResult.IsFailure)
                return Result.Failure<Archive>(archiveResult.ErrorCode ?? ErrorCodes.DOCUMENT_GENERATION_FAILED, archiveResult.Message);

            LogInfo($"申报表文档已生成并归档: ApplicationId={applicationId}, Path={outputPath}");
            return Result.Success(archiveResult.Value!);
        }
        catch (Exception ex)
        {
            LogException(ex, "生成一事一议申报表文档");
            return Result.FromException<Archive>(ex);
        }
    }

    /// <summary>
    /// 查找申报表模板（按文档类型精准定位，方案B；原按名称关键词在"新增"分类中子串匹配）。
    /// 找不到返回 null → 调用方 GenerateDocumentAsync 显式报 SPECIAL_APPROVAL_TEMPLATE_NOT_FOUND
    ///（原 `?? templates.FirstOrDefault()` 兜底会拿任意模板渲染，已随模糊过滤一并废除）。
    /// </summary>
    private async Task<Template?> FindSpecialApprovalTemplateAsync(CancellationToken ct = default)
    {
        var result = await _templateService.GetByDocTypeAsync(TemplateDocTypes.SpecialApprovalDeclaration, ct);
        if (result.IsFailure)
        {
            LogWarn($"按文档类型查申报表模板失败: {result.Message}");
            return null;
        }
        return result.Value.FirstOrDefault();
    }

    /// <summary>
    /// 组装申报表模板字段（占位符 → 值）
    /// </summary>
    private async Task<Dictionary<string, string>> BuildFieldDataAsync(ApplicationEntity app, SpecialApprovalForm form, CancellationToken ct)
    {
        // 地址统一走文本级唯一入口（县+镇+村+门牌，已含则跳过；原 Town+Community+Address 裸拼会重复）
        var fullAddress = AddressResolver.BuildAddressFromText(app.District, app.Town, app.Community, app.Address);
        var reportUnit = string.IsNullOrWhiteSpace(form.ReportUnit)
            ? await GetOperatorUnitNameAsync()
            : form.ReportUnit;
        var confirmTime = form.AuditDate?.ToString("yyyy年M月d日") ?? DateTime.Today.ToString("yyyy年M月d日");
        var auditDate = form.AuditDate?.ToString("yyyy年M月d日") ?? DateTime.Today.ToString("yyyy年M月d日");

        return new Dictionary<string, string>
        {
            ["{户主姓名}"] = app.ApplicantName,
            ["{户主身份证号}"] = app.ApplicantIdCard,
            ["{户主联系方式}"] = app.ApplicantPhone,
            ["{户主家庭人口}"] = $"{app.FamilySize}人",
            ["{户主家庭住址}"] = fullAddress,
            ["{户主享受类别}"] = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? ""),
            ["{申请救助家庭基本情况}"] = form.BasicSituation,
            ["{申请救助家庭需要一事一议说明的情况}"] = form.SpecialMatters,
            ["{审核结果}"] = form.AuditResult,
            ["{审核日期}"] = auditDate,
            ["{填报单位}"] = reportUnit,
            ["{审核确认时间}"] = confirmTime
        };
    }

    /// <summary>
    /// 获取当前用户机构名称（填报单位）
    /// </summary>
    private async Task<string> GetOperatorUnitNameAsync()
    {
        var orgId = App.CurrentUserOrganizationId;
        if (!orgId.HasValue) return string.Empty;
        try
        {
            var result = await _organizationService.GetByIdAsync(orgId.Value);
            if (result.IsFailure || result.Value == null)
            {
                LogWarn($"获取填报单位失败，文书该字段将为空: {result.ErrorCode} {result.Message}");
                return string.Empty;
            }
            return result.Value.Name ?? string.Empty;
        }
        catch (Exception ex)
        {
            LogWarn($"获取填报单位名称异常，文书该字段将为空: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 生成申报表编号
    /// </summary>
    private async Task<string> GenerateFormNoAsync(long applicationId, CancellationToken ct = default)
    {
        var appResult = await _applicationService.GetByIdAsync(applicationId, ct);
        if (appResult.IsFailure || appResult.Value == null)
            LogWarn($"生成申报表编号：读取申请号失败，编号将退化为随机串: ApplicationId={applicationId}, {appResult.ErrorCode} {appResult.Message}");
        var appNo = appResult.HasValue ? appResult.Value.ApplicationNo : string.Empty;
        return SpecialApprovalConstants.BuildFormNo(appNo, Random.Shared.Next(1, 9999));
    }
}
