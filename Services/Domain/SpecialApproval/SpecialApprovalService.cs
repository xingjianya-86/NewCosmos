using SpecialApprovalEntity = NewCosmos.Models.Entities.SpecialApproval;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.SpecialApproval;

public interface ISpecialApprovalService
{
    Task<SpecialApprovalEntity?> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default);
    Task<SpecialApprovalEntity> CreateAsync(SpecialApprovalEntity approval, CancellationToken ct = default);
}

public class SpecialApprovalService : BaseService, ISpecialApprovalService
{
    protected override string ServiceName => "SpecialApprovalService";
    private readonly IDatabaseService _db;

    public SpecialApprovalService(IDatabaseService db, ILoggerService logger) : base(logger) { _db = db; }

    public async Task<SpecialApprovalEntity?> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        var sql = "SELECT * FROM nc_biz_special_approvals WHERE application_id = $1 AND deleted_at IS null";
        var result = await _db.QuerySingleAsync<SpecialApprovalEntity>(sql, ct, applicationId);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<SpecialApprovalEntity> CreateAsync(SpecialApprovalEntity approval, CancellationToken ct = default)
    {
        var sql = @"INSERT INTO nc_biz_special_approvals (application_id, meeting_date, meeting_location, meeting_topic, meeting_summary, meeting_members, special_reason, special_circumstances, override_classification, approver_name, approver_title, approver_organization, created_by)
                     VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13) RETURNING id";

        var result = await _db.ExecuteScalarAsync(sql, ct,
            approval.ApplicationId, approval.MeetingDate, approval.MeetingLocation,
            approval.MeetingTopic, approval.MeetingSummary, approval.MeetingMembers,
            approval.SpecialReason, approval.SpecialCircumstances,
            approval.OverrideClassification, approval.ApproverName,
            approval.ApproverTitle, approval.ApproverOrganization, approval.CreatedBy);

        if (result.IsSuccess) approval.Id = result.Value;
        return approval;
    }
}
