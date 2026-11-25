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
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

using ApplicationEntity = NewCosmos.Models.Entities.Application;
using SpecialApprovalEntity = NewCosmos.Models.Entities.SpecialApproval;

namespace NewCosmos.Services.Domain.SpecialApproval;

/// <summary>一事一议负面结论告知书类型。</summary>
public enum SpecialApprovalNoticeKind
{
    /// <summary>不予受理（不符合适用情形，3 个工作日内书面告知）</summary>
    NotAccepted,
    /// <summary>不予救助（评议不通过）</summary>
    Rejected
}

public interface ISpecialApprovalFormService
{
    Task<SpecialApprovalForm?> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default);

    Task<Result> SaveDraftAsync(SpecialApprovalFormRequest request, CancellationToken ct = default);

    Task<Result> SubmitAsync(SpecialApprovalFormRequest request, CancellationToken ct = default);

    Task<Result> CompleteReviewAsync(SpecialApprovalReviewRequest request, CancellationToken ct = default);

    /// <summary>登记备案完成（已返回结论 / 历史 Approved → Filed）。</summary>
    Task<Result> MarkFiledAsync(long applicationId, string? receiptFile, string operatorName, CancellationToken ct = default);

    /// <summary>退回补正（已上报 → 退回补正），乡镇可修改后重新提交。</summary>
    Task<Result> ReturnForCorrectionAsync(long applicationId, string? reason, string operatorName, CancellationToken ct = default);

    /// <summary>生成负面结论告知书 PDF（不予受理 / 不予救助）并归档。</summary>
    Task<Result<Archive>> GenerateNoticeAsync(long applicationId, SpecialApprovalNoticeKind kind,
        string reason, CancellationToken ct = default);

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
        // 已上报但未登记结论仍可修改内容；结论登记后（Returned/NotPassed/Filed/历史 Approved/Rejected）锁定。
        if (existing != null && existing.Id > 0
            && !SpecialApprovalConstants.IsContentEditable(existing.Status))
            return Result.Failure(ErrorCodes.SPECIAL_APPROVAL_ALREADY_SUBMITTED, "申报表已登记结论或已备案，无法修改");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        if (existing != null && existing.Id > 0)
        {
            var updateSql = @"UPDATE nc_biz_special_approval_forms SET
                basic_situation = $1, special_matters = $2, template_key = $3,
                report_unit = $4, handler_name = $5, form_date = $6,
                audit_result = $7, audit_date = $8,
                case_type = $9, negative_items = $10, filing_receipt = $11,
                updated_at = NOW()
                WHERE id = $12 AND deleted_at IS NULL";
            var updateResult = await _db.ExecuteNonQueryAsync(updateSql, ct,
                request.BasicSituation, request.SpecialMatters, request.TemplateKey,
                request.ReportUnit, request.HandlerName, request.FormDate,
                request.AuditResult, request.AuditDate,
                request.CaseType, SpecialApprovalConstants.JoinNegativeItems(request.NegativeItems),
                request.FilingReceipt,
                existing.Id);
            if (updateResult.IsFailure)
                return Result.Failure(updateResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, updateResult.Message);
        }
        else
        {
            var formNo = await GenerateFormNoAsync(request.ApplicationId, ct);
            var insertSql = @"INSERT INTO nc_biz_special_approval_forms
                (application_id, form_no, basic_situation, special_matters, template_key,
                 report_unit, handler_name, form_date, audit_result, audit_date,
                 status, created_by, case_type, negative_items, filing_receipt, created_at, updated_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,NOW(),NOW())";
            var insertResult = await _db.ExecuteNonQueryAsync(insertSql, ct,
                request.ApplicationId, formNo, request.BasicSituation, request.SpecialMatters, request.TemplateKey,
                request.ReportUnit, request.HandlerName, request.FormDate,
                request.AuditResult, request.AuditDate,
                SpecialApprovalConstants.StatusDraft, request.CreatedBy,
                request.CaseType, SpecialApprovalConstants.JoinNegativeItems(request.NegativeItems),
                request.FilingReceipt);
            if (insertResult.IsFailure)
                return Result.Failure(insertResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, insertResult.Message);
        }

        await tx.CommitAsync(ct);
        LogInfo($"申报表草稿已保存: ApplicationId={request.ApplicationId}");
        return Result.Success();
    }

    /// <summary>
    /// 提交申报（草稿/退回补正 → 已上报），写 case_type、submitted_at。
    /// </summary>
    public async Task<Result> SubmitAsync(SpecialApprovalFormRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));
        if (string.IsNullOrWhiteSpace(request.SpecialMatters))
            return Result.Failure(ErrorCodes.REQUIRED_FIELD_MISSING, "请填写一事一议说明情况");

        var caseError = SpecialApprovalConstants.ValidateCaseSelection(request.CaseType, request.NegativeItems);
        if (caseError != null)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, caseError);

        var existing = await GetByApplicationIdAsync(request.ApplicationId, ct);
        if (existing == null || existing.Id <= 0)
            return Result.Failure(ErrorCodes.SPECIAL_APPROVAL_FORM_NOT_FOUND, "请先保存申报表草稿");

        if (!SpecialApprovalConstants.CanSubmit(existing.Status))
            return Result.Failure(ErrorCodes.SPECIAL_APPROVAL_ALREADY_SUBMITTED, "仅草稿或退回补正状态可提交");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        var sql = @"UPDATE nc_biz_special_approval_forms SET
            basic_situation = $1, special_matters = $2, template_key = $3,
            report_unit = $4, handler_name = $5, form_date = $6,
            audit_result = $7, audit_date = $8,
            status = $9, submitted_at = CURRENT_DATE,
            case_type = $10, negative_items = $11,
            updated_at = NOW()
            WHERE id = $12 AND deleted_at IS NULL AND status = ANY($13)";
        var result = await _db.ExecuteNonQueryAsync(sql, ct,
            request.BasicSituation, request.SpecialMatters, request.TemplateKey,
            request.ReportUnit, request.HandlerName, request.FormDate,
            request.AuditResult, request.AuditDate,
            SpecialApprovalConstants.StatusSubmitted,
            request.CaseType, SpecialApprovalConstants.JoinNegativeItems(request.NegativeItems),
            existing.Id,
            (object)new[]
            {
                SpecialApprovalConstants.StatusDraft,
                SpecialApprovalConstants.StatusReturnedForCorrection
            });
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message);
        if (result.Value == 0)
            return Result.Failure(ErrorCodes.INVALID_TRANSITION, "仅草稿或退回补正状态可提交");

        await tx.CommitAsync(ct);
        LogInfo($"申报表已提交: ApplicationId={request.ApplicationId}, FormNo={existing.FormNo}, CaseType={request.CaseType}");
        return Result.Success();
    }

    /// <summary>
    /// 登记评议结论：写 nc_biz_special_approvals（评议结果）→ 更新申报表状态 → 通过时回写申请分类/标记。
    /// 通过：申报表 Returned（待备案），申请按指定分类施保，IsSpecialApproval=true。
    /// 不通过：申报表 NotPassed（终态），申请状态不变；由申请提交门槛禁止再提交。
    /// </summary>
    public async Task<Result> CompleteReviewAsync(SpecialApprovalReviewRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));
        if (request.ApplicationId <= 0)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "申请ID无效");

        var form = await GetByApplicationIdAsync(request.ApplicationId, ct);
        if (form == null || form.Id <= 0)
            return Result.Failure(ErrorCodes.SPECIAL_APPROVAL_FORM_NOT_FOUND, "未找到一事一议申报表");

        if (!SpecialApprovalConstants.CanRecordConclusion(form.Status))
            return Result.Failure(ErrorCodes.SPECIAL_APPROVAL_REVIEW_DONE,
                $"当前状态（{SpecialApprovalConstants.GetStatusDescription(form.Status)}）不可登记结论，仅已上报可登记");

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

        // 2. 更新申报表状态：通过 → Returned（待备案）；不通过 → NotPassed（终态，禁止申请提交）
        var formStatus = request.Approved
            ? SpecialApprovalConstants.StatusReturned
            : SpecialApprovalConstants.StatusNotPassed;
        // 申报内容随审议一并落库（提交后内容仍可编辑，见 SaveDraftAsync 注释）：
        // 原实现只写会议字段，提交后新填的基本情况/说明在审议锁定时被静默丢弃。
        // SpecialReason 即 VM 的一事一议说明情况（与 SpecialCircumstances 同源）。
        var updateFormSql = @"UPDATE nc_biz_special_approval_forms SET
            status = $1, audit_result = $2, audit_date = $3,
            basic_situation = $4, special_matters = $5, template_key = $6,
            is_long_term_publicized = $8,
            updated_at = NOW()
            WHERE id = $7 AND deleted_at IS NULL";
        var updateFormResult = await _db.ExecuteNonQueryAsync(updateFormSql, ct,
            formStatus, request.Approved ? "同意纳入" : "不同意纳入",
            request.MeetingDate == default ? (DateTime?)null : request.MeetingDate,
            request.BasicSituation, request.SpecialReason, request.TemplateKey, form.Id,
            request.Approved);
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
            // 不通过：只登记申报表结论，不把申请置 Refused；
            // 申请保持原状态，由 ApplicationService.SubmitAsync 的“一事一议不通过”门槛禁止提交。
            LogInfo($"一事一议结论为不通过（申请保持原状态）: ApplicationId={request.ApplicationId}");
        }

        await tx.CommitAsync(ct);
        LogInfo($"一事一议审议完成: ApplicationId={request.ApplicationId}, Approved={request.Approved}, SpecialApprovalId={specialApprovalId}");
        return Result.Success();
    }

    /// <summary>
    /// 登记备案完成（已返回结论 / 历史 Approved → Filed），写 filed_at。
    /// </summary>
    public async Task<Result> MarkFiledAsync(long applicationId, string? receiptFile,
        string operatorName, CancellationToken ct = default)
    {
        var form = await GetByApplicationIdAsync(applicationId, ct);
        if (form == null || form.Id <= 0)
            return Result.Failure(ErrorCodes.SPECIAL_APPROVAL_FORM_NOT_FOUND, "未找到一事一议申报表");
        if (!SpecialApprovalConstants.CanMarkFiled(form.Status))
            return Result.Failure(ErrorCodes.INVALID_TRANSITION,
                $"当前状态（{SpecialApprovalConstants.GetStatusDescription(form.Status)}）不可登记备案");

        var sql = @"UPDATE nc_biz_special_approval_forms SET
                    status = $1, filed_at = CURRENT_DATE, filing_receipt = $2,
                    is_long_term_publicized = TRUE, updated_at = NOW()
                    WHERE id = $3 AND deleted_at IS NULL AND status = ANY($4)";
        var result = await _db.ExecuteNonQueryAsync(sql, ct,
            SpecialApprovalConstants.StatusFiled, receiptFile, form.Id,
            (object)new[] { SpecialApprovalConstants.StatusReturned, SpecialApprovalConstants.StatusApproved });
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message);
        if (result.Value == 0)
            return Result.Failure(ErrorCodes.INVALID_TRANSITION, "仅已返回结论状态可登记备案");

        LogInfo($"一事一议备案完成: ApplicationId={applicationId}, Operator={operatorName ?? "System"}, Receipt={receiptFile ?? string.Empty}");
        return Result.Success();
    }

    /// <summary>
    /// 退回补正（已上报 → 退回补正），乡镇可修改后重新提交。
    /// </summary>
    public async Task<Result> ReturnForCorrectionAsync(long applicationId, string? reason,
        string operatorName, CancellationToken ct = default)
    {
        var form = await GetByApplicationIdAsync(applicationId, ct);
        if (form == null || form.Id <= 0)
            return Result.Failure(ErrorCodes.SPECIAL_APPROVAL_FORM_NOT_FOUND, "未找到一事一议申报表");
        if (form.Status != SpecialApprovalConstants.StatusSubmitted)
            return Result.Failure(ErrorCodes.INVALID_TRANSITION, "仅已上报状态可退回补正");

        var sql = @"UPDATE nc_biz_special_approval_forms SET
                    status = $1, updated_at = NOW()
                    WHERE id = $2 AND deleted_at IS NULL AND status = $3";
        var result = await _db.ExecuteNonQueryAsync(sql, ct,
            SpecialApprovalConstants.StatusReturnedForCorrection, form.Id, SpecialApprovalConstants.StatusSubmitted);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message);
        if (result.Value == 0)
            return Result.Failure(ErrorCodes.INVALID_TRANSITION, "仅已上报状态可退回补正");

        LogInfo($"一事一议退回补正: ApplicationId={applicationId}, Operator={operatorName ?? "System"}, Reason={reason ?? string.Empty}");
        return Result.Success();
    }

    /// <summary>
    /// 生成负面结论告知书 PDF（不予受理 / 不予救助）并归档。
    /// 不依赖 nc_biz_templates（现有告知书模板无“理由/不予受理”占位符），直接排版 PDF。
    /// </summary>
    public async Task<Result<Archive>> GenerateNoticeAsync(long applicationId, SpecialApprovalNoticeKind kind,
        string reason, CancellationToken ct = default)
    {
        var form = await GetByApplicationIdAsync(applicationId, ct);
        if (form == null || form.Id <= 0)
            return Result.Failure<Archive>(ErrorCodes.SPECIAL_APPROVAL_FORM_NOT_FOUND, "未找到一事一议申报表");

        var appResult = await _applicationService.GetByIdAsync(applicationId, ct);
        if (appResult.IsFailure || appResult.Value == null)
            return Result.Failure<Archive>(ErrorCodes.APPLICATION_NOT_FOUND, "未找到申请记录");
        var app = appResult.Value;

        if (!ChinesePdfFontResolver.EnsureRegistered(out var fontError))
            return Result.Failure<Archive>(ErrorCodes.DOCUMENT_GENERATION_FAILED, fontError ?? "中文字体不可用");

        try
        {
            var title = kind == SpecialApprovalNoticeKind.NotAccepted
                ? "社会救助“一事一议”不予受理告知书"
                : "社会救助“一事一议”不予救助告知书";
            var lines = BuildNoticeLines(app, form, kind, reason);

            var dir = OutputPathHelper.EnsureDirectoryExists(
                OutputPathHelper.GetCategoryDisplayName(PrintBusinessTypes.FamilyApplication),
                app.ApplicantName, app.Id.ToString(), DateTime.Today);
            var fileName = $"一事一议_{title.Replace("社会救助“一事一议”", "", StringComparison.Ordinal)}_{DateTime.Now:yyyyMMddHHmmssfff}.pdf";
            var outputPath = Path.Combine(dir, fileName);

            using (var doc = new PdfDocument())
            {
                var page = doc.AddPage();
                page.Size = PageSize.A4;
                using var gfx = XGraphics.FromPdfPage(page);
                var titleFont = new XFont(ChinesePdfFontResolver.FaceName, 18, XFontStyleEx.Bold);
                var bodyFont = new XFont(ChinesePdfFontResolver.FaceName, 12, XFontStyleEx.Regular);
                var smallFont = new XFont(ChinesePdfFontResolver.FaceName, 10, XFontStyleEx.Regular);

                var y = 80.0;
                gfx.DrawString(title, titleFont, XBrushes.Black,
                    new XRect(0, y, page.Width.Point, 30), XStringFormats.TopCenter);
                y += 56;

                foreach (var line in lines)
                {
                    if (string.IsNullOrEmpty(line))
                    {
                        y += 12;
                        continue;
                    }
                    foreach (var wrapped in WrapNoticeText(line, 30))
                    {
                        if (y > page.Height.Point - 80) break;
                        gfx.DrawString(wrapped, bodyFont, XBrushes.Black,
                            new XRect(60, y, page.Width.Point - 120, 20), XStringFormats.TopLeft);
                        y += 24;
                    }
                }

                y = page.Height.Point - 80;
                gfx.DrawString($"告知单位：{form.ReportUnit}", smallFont, XBrushes.Black,
                    new XRect(60, y, page.Width.Point - 120, 16), XStringFormats.TopLeft);
                gfx.DrawString($"告知日期：{DateTime.Today:yyyy年M月d日}", smallFont, XBrushes.Black,
                    new XRect(60, y + 20, page.Width.Point - 120, 16), XStringFormats.TopLeft);
                if (kind == SpecialApprovalNoticeKind.NotAccepted)
                    gfx.DrawString("（本告知书应在作出不予受理决定后 3 个工作日内送达）", smallFont, XBrushes.Black,
                        new XRect(60, y + 42, page.Width.Point - 120, 16), XStringFormats.TopLeft);

                doc.Save(outputPath);
            }

            var archiveResult = await _archiveService.CreateOrAppendFilesAsync(
                applicationId,
                "SpecialApproval",
                app.ClassificationResult ?? "",
                App.CurrentUserFullName,
                new List<ArchiveFile>
                {
                    new()
                    {
                        TemplateId = "notice",
                        FileName = fileName,
                        FileType = "pdf",
                        FileSize = new FileInfo(outputPath).Length,
                        OutputPath = outputPath
                    }
                }, ct);
            if (archiveResult.IsFailure)
                return Result.Failure<Archive>(archiveResult.ErrorCode ?? ErrorCodes.DOCUMENT_GENERATION_FAILED,
                    archiveResult.Message);

            LogInfo($"一事一议告知书已生成并归档: ApplicationId={applicationId}, Kind={kind}, Path={outputPath}");
            return Result.Success(archiveResult.Value!);
        }
        catch (Exception ex)
        {
            LogException(ex, "生成一事一议告知书");
            return Result.FromException<Archive>(ex);
        }
    }

    private static List<string> BuildNoticeLines(ApplicationEntity app, SpecialApprovalForm form,
        SpecialApprovalNoticeKind kind, string reason)
    {
        var address = AddressResolver.BuildAddressFromText(app.District, app.Town, app.Community, app.Address);
        var caseName = SpecialApprovalConstants.GetCaseTypeName(form.CaseType) ?? "（未选择）";
        var lines = new List<string>
        {
            $"申请人：{app.ApplicantName}（身份证号：{app.ApplicantIdCard}）",
            $"家庭住址：{address}",
            string.Empty,
            kind == SpecialApprovalNoticeKind.NotAccepted
                ? $"您提出的社会救助“一事一议”申请（适用情形：{caseName}），经审查不符合受理条件，决定不予受理。"
                : "您提出的社会救助“一事一议”申请，经评议，结论为不通过，决定不予救助。",
            string.IsNullOrWhiteSpace(reason) ? string.Empty : $"理由：{reason.Trim()}",
            string.Empty,
            "如对本决定不服，可自收到本告知书之日起 60 日内依法申请行政复议，或 6 个月内依法提起行政诉讼。"
        };
        return lines;
    }

    private static IEnumerable<string> WrapNoticeText(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            yield return text;
            yield break;
        }
        for (var i = 0; i < text.Length; i += maxChars)
            yield return text.Substring(i, Math.Min(maxChars, text.Length - i));
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
            ["{申请意见}"] = form.AuditResult,
            ["{一事一议事项基本情况}"] = form.BasicSituation,
            ["{疑难事项}"] = form.SpecialMatters,
            ["{审批时间}"] = auditDate,
            ["{所在单位}"] = reportUnit,
            ["{享受分类}"] = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? ""),
            ["{事项名称}"] = form.BasicSituation,
            ["{经办人}"] = form.HandlerName,
            ["{纪检监察部门备案}"] = string.Empty,
            ["{市级民政部门备案}"] = string.Empty,
            ["{附件清单}"] = "申请表；会议纪要；审核确认表",
            ["{适用情形}"] = SpecialApprovalConstants.GetCaseTypeName(form.CaseType) ?? string.Empty,
            ["{不适用情形}"] = string.Join("；", SpecialApprovalConstants
                .SplitNegativeItems(form.NegativeItems)
                .Select(x => SpecialApprovalConstants.GetNegativeItemName(x) ?? x)),
            ["{上报日期}"] = form.SubmittedAt?.ToString("yyyy年M月d日") ?? string.Empty,
            ["{备案日期}"] = form.FiledAt?.ToString("yyyy年M月d日") ?? string.Empty,
            ["{评议结论}"] = SpecialApprovalConstants.GetStatusDescription(form.Status),
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
