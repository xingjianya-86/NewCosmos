using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.ElderlyBenefits;

/// <summary>
/// 高龄年度认证报表取数/拼装（桌面与 Web 共用，只读认证域表）。
/// 列定义严格按 API_CONTRACT_ELDERLY_CERTIFICATION_REPORT.md；SQL 只用 $1..$n，
/// 显式列出所需列并带 LIMIT（报表可全量，但不允许 SELECT *）。
/// </summary>
public class ElderlyCertificationReportService : BaseService
{
    /// <summary>单份报表最大行数（防止异常批次把内存/Excel 撑爆）。</summary>
    public const int ReportRowLimit = 100000;

    private static readonly string[] UnverifiedStatuses =
    {
        ElderlyCertificationConstants.CertStatusPending,
        ElderlyCertificationConstants.CertStatusRefused,
        ElderlyCertificationConstants.CertStatusFailed,
        ElderlyCertificationConstants.CertStatusNotLocated
    };

    private readonly IDatabaseService _db;

    protected override string ServiceName => "ElderlyCertificationReportService";

    public ElderlyCertificationReportService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    #region 列定义（与合同逐列一致，顺序即导出顺序）

    public static IReadOnlyList<ElderlyCertificationReportColumn> RosterColumns { get; } = new[]
    {
        new ElderlyCertificationReportColumn("Seq", "序号", 6, ElderlyCertificationReportColumnFormat.Integer),
        new ElderlyCertificationReportColumn("Name", "姓名", 12),
        new ElderlyCertificationReportColumn("IdCard", "身份证号", 22),
        new ElderlyCertificationReportColumn("Gender", "性别", 8),
        new ElderlyCertificationReportColumn("BirthDate", "出生日期", 13, ElderlyCertificationReportColumnFormat.Date),
        new ElderlyCertificationReportColumn("Age", "年龄", 8, ElderlyCertificationReportColumnFormat.Integer),
        new ElderlyCertificationReportColumn("Category", "享受类别", 30),
        new ElderlyCertificationReportColumn("IssueAmount", "月标准（元）", 14, ElderlyCertificationReportColumnFormat.Money),
        new ElderlyCertificationReportColumn("HukouAddress", "户籍地址", 34),
        new ElderlyCertificationReportColumn("FamilyAddress", "家庭住址", 34),
        new ElderlyCertificationReportColumn("Phone", "联系电话", 16),
        new ElderlyCertificationReportColumn("VerifyMethod", "认证方式", 12),
        new ElderlyCertificationReportColumn("VerifiedAt", "认证日期", 13, ElderlyCertificationReportColumnFormat.Date),
        new ElderlyCertificationReportColumn("CertStatus", "认证结果", 12),
        new ElderlyCertificationReportColumn("EvidenceStatus", "凭证状态", 12),
        new ElderlyCertificationReportColumn("StopDecision", "停发决定", 10),
        new ElderlyCertificationReportColumn("ResultNote", "备注", 24)
    };

    public static IReadOnlyList<ElderlyCertificationReportColumn> SummaryColumns { get; } = new[]
    {
        new ElderlyCertificationReportColumn("TownName", "乡镇", 16),
        new ElderlyCertificationReportColumn("VillageName", "村社区", 18),
        new ElderlyCertificationReportColumn("Total", "应认证人数", 11, ElderlyCertificationReportColumnFormat.Integer),
        new ElderlyCertificationReportColumn("Verified", "已通过", 10, ElderlyCertificationReportColumnFormat.Integer),
        new ElderlyCertificationReportColumn("Exempt", "免核查", 10, ElderlyCertificationReportColumnFormat.Integer),
        new ElderlyCertificationReportColumn("Pending", "待核查", 10, ElderlyCertificationReportColumnFormat.Integer),
        new ElderlyCertificationReportColumn("Refused", "拒绝核查", 10, ElderlyCertificationReportColumnFormat.Integer),
        new ElderlyCertificationReportColumn("Failed", "核查不通过", 12, ElderlyCertificationReportColumnFormat.Integer),
        new ElderlyCertificationReportColumn("Deceased", "已去世", 10, ElderlyCertificationReportColumnFormat.Integer),
        new ElderlyCertificationReportColumn("MovedOut", "已迁出", 10, ElderlyCertificationReportColumnFormat.Integer),
        new ElderlyCertificationReportColumn("NotLocated", "无法联系", 10, ElderlyCertificationReportColumnFormat.Integer),
        new ElderlyCertificationReportColumn("Stopped", "已停发", 10, ElderlyCertificationReportColumnFormat.Integer),
        new ElderlyCertificationReportColumn("EvidenceIssue", "凭证异常人数", 14, ElderlyCertificationReportColumnFormat.Integer),
        new ElderlyCertificationReportColumn("CertificationRate", "认证率", 10, ElderlyCertificationReportColumnFormat.Percent1)
    };

    public static IReadOnlyList<ElderlyCertificationReportColumn> UnverifiedColumns { get; } = new[]
    {
        new ElderlyCertificationReportColumn("Seq", "序号", 6, ElderlyCertificationReportColumnFormat.Integer),
        new ElderlyCertificationReportColumn("Name", "姓名", 12),
        new ElderlyCertificationReportColumn("IdCard", "身份证号", 22),
        new ElderlyCertificationReportColumn("Gender", "性别", 8),
        new ElderlyCertificationReportColumn("Age", "年龄", 8, ElderlyCertificationReportColumnFormat.Integer),
        new ElderlyCertificationReportColumn("TownName", "乡镇", 16),
        new ElderlyCertificationReportColumn("VillageName", "村社区", 18),
        new ElderlyCertificationReportColumn("Phone", "联系电话", 16),
        new ElderlyCertificationReportColumn("CertStatus", "认证状态", 12),
        new ElderlyCertificationReportColumn("EvidenceStatus", "凭证状态", 12),
        new ElderlyCertificationReportColumn("SuggestedStopReason", "建议停发原因", 24),
        new ElderlyCertificationReportColumn("DueStopDate", "应停日期", 13, ElderlyCertificationReportColumnFormat.Date),
        new ElderlyCertificationReportColumn("StopDecision", "停发决定", 10),
        new ElderlyCertificationReportColumn("FollowUpDate", "跟进日期", 13, ElderlyCertificationReportColumnFormat.Date),
        new ElderlyCertificationReportColumn("Note", "备注", 24)
    };

    #endregion

    /// <summary>
    /// 取一份认证报表。未知 formKey 显式失败；查询/映射异常不吞，转 Result 失败。
    /// </summary>
    public async Task<Result<ElderlyCertificationReport>> GetReportAsync(
        long campaignId,
        string formKey,
        CancellationToken ct = default)
    {
        if (!ElderlyCertificationReportFormKeys.IsValid(formKey))
            return Result.Failure<ElderlyCertificationReport>(
                ErrorCodes.VALIDATION_FAILED, $"未知的高龄认证报表类型：{formKey}");

        var campaignResult = await GetCampaignAsync(campaignId, ct);
        if (campaignResult.IsFailure)
            return Result.Failure<ElderlyCertificationReport>(
                campaignResult.ErrorCode!, campaignResult.Message!);

        var campaign = campaignResult.Value;
        if (campaign == null)
            return Result.Failure<ElderlyCertificationReport>(ErrorCodes.NOT_FOUND, "认证批次不存在");

        var title = $"{campaign.DisplayName} {ElderlyCertificationReportFormKeys.GetName(formKey)}";

        try
        {
            return formKey switch
            {
                ElderlyCertificationReportFormKeys.Roster => await BuildRosterAsync(campaign, title, ct),
                ElderlyCertificationReportFormKeys.Summary => await BuildSummaryAsync(campaign, title, ct),
                ElderlyCertificationReportFormKeys.Unverified => await BuildUnverifiedAsync(campaign, title, ct),
                _ => Result.Failure<ElderlyCertificationReport>(
                    ErrorCodes.VALIDATION_FAILED, $"未知的高龄认证报表类型：{formKey}")
            };
        }
        catch (Exception ex)
        {
            LogException(ex, $"GetReportAsync({formKey})");
            return Result.FromException<ElderlyCertificationReport>(ex);
        }
    }

    #region 花名册

    private async Task<Result<ElderlyCertificationReport>> BuildRosterAsync(
        ElderlyCertCampaign campaign,
        string title,
        CancellationToken ct)
    {
        const string sql = @"
            SELECT tw.town_name AS town_name,
                   COALESCE(NULLIF(t.hukou_village, ''), v.village_name) AS village_name,
                   t.id_card, t.name, t.gender, t.birth_date, t.age_snapshot,
                   t.category, t.issue_amount, t.hukou_address, t.family_address, t.phone,
                   t.verify_method, t.verified_at, t.cert_status, t.evidence_status,
                   t.stop_decision, t.result_note
            FROM nc_biz_elderly_cert_tasks t
            LEFT JOIN nc_regions_towns tw ON tw.id = t.hukou_town_id
            LEFT JOIN nc_regions_villages v ON v.id = t.hukou_village_id
            WHERE t.campaign_id = $1 AND t.deleted_at IS NULL
            ORDER BY tw.town_name NULLS LAST, village_name NULLS LAST, t.name, t.id_card
            LIMIT $2";

        var query = await _db.QueryAsync<ElderlyCertificationRosterSourceRow>(
            sql, ct, campaign.Id, ReportRowLimit);
        if (query.IsFailure)
            return Result.Failure<ElderlyCertificationReport>(query.ErrorCode!, query.Message!);

        var source = query.Value ?? new List<ElderlyCertificationRosterSourceRow>();
        var rows = BuildRosterRows(source);
        return Result.Success(new ElderlyCertificationReport(
            ElderlyCertificationReportFormKeys.Roster, title, RosterColumns, rows));
    }

    /// <summary>
    /// 花名册源行 → 报表行。公开给 Web Server 在 scoped 查询后复用，避免两端重写列映射。
    /// </summary>
    public static List<ElderlyCertificationReportRow> BuildRosterRows(
        IReadOnlyList<ElderlyCertificationRosterSourceRow> source)
    {
        var rows = new List<ElderlyCertificationReportRow>(source.Count);
        var seq = 0;
        foreach (var item in source)
        {
            seq++;
            rows.Add(ElderlyCertificationReportRow.From(
                ("Seq", seq),
                ("Name", item.Name),
                ("IdCard", item.IdCard),
                ("Gender", FormatGender(item.Gender)),
                ("BirthDate", item.BirthDate),
                ("Age", item.AgeSnapshot),
                ("Category", ElderlyBenefitConstants.GetCategoryName(item.Category)),
                ("IssueAmount", item.IssueAmount),
                ("HukouAddress", item.HukouAddress),
                ("FamilyAddress", item.FamilyAddress),
                ("Phone", item.Phone),
                ("VerifyMethod", ElderlyCertificationConstants.GetVerifyMethodName(item.VerifyMethod)),
                ("VerifiedAt", item.VerifiedAt),
                ("CertStatus", ElderlyCertificationConstants.GetCertStatusName(item.CertStatus)),
                ("EvidenceStatus", ElderlyCertificationConstants.GetEvidenceStatusName(item.EvidenceStatus)),
                ("StopDecision", ElderlyCertificationConstants.GetStopDecisionName(item.StopDecision)),
                ("ResultNote", item.ResultNote)));
        }
        return rows;
    }

    #endregion

    #region 汇总表

    private async Task<Result<ElderlyCertificationReport>> BuildSummaryAsync(
        ElderlyCertCampaign campaign,
        string title,
        CancellationToken ct)
    {
        const string sql = @"
            SELECT COALESCE(tw.town_name, '') AS town_name,
                   COALESCE(NULLIF(t.hukou_village, ''), v.village_name, '') AS village_name,
                   COUNT(*)::int AS total,
                   COUNT(*) FILTER (WHERE t.cert_status = $2)::int AS verified,
                   COUNT(*) FILTER (WHERE t.cert_status = $3)::int AS exempt,
                   COUNT(*) FILTER (WHERE t.cert_status = $4)::int AS pending,
                   COUNT(*) FILTER (WHERE t.cert_status = $5)::int AS refused,
                   COUNT(*) FILTER (WHERE t.cert_status = $6)::int AS failed,
                   COUNT(*) FILTER (WHERE t.cert_status = $7)::int AS deceased,
                   COUNT(*) FILTER (WHERE t.cert_status = $8)::int AS moved_out,
                   COUNT(*) FILTER (WHERE t.cert_status = $9)::int AS not_located,
                   COUNT(*) FILTER (WHERE t.cert_status = $10)::int AS stopped,
                   COUNT(*) FILTER (WHERE t.evidence_status IN ($11, $12, $13))::int AS evidence_issue
            FROM nc_biz_elderly_cert_tasks t
            LEFT JOIN nc_regions_towns tw ON tw.id = t.hukou_town_id
            LEFT JOIN nc_regions_villages v ON v.id = t.hukou_village_id
            WHERE t.campaign_id = $1 AND t.deleted_at IS NULL
            GROUP BY town_name, village_name
            ORDER BY town_name, village_name
            LIMIT $14";

        var query = await _db.QueryAsync<ElderlyCertificationSummarySourceRow>(sql, ct,
            campaign.Id,
            ElderlyCertificationConstants.CertStatusVerified,
            ElderlyCertificationConstants.CertStatusExempt,
            ElderlyCertificationConstants.CertStatusPending,
            ElderlyCertificationConstants.CertStatusRefused,
            ElderlyCertificationConstants.CertStatusFailed,
            ElderlyCertificationConstants.CertStatusDeceased,
            ElderlyCertificationConstants.CertStatusMovedOut,
            ElderlyCertificationConstants.CertStatusNotLocated,
            ElderlyCertificationConstants.CertStatusStopped,
            ElderlyCertificationConstants.EvidenceStatusMissing,
            ElderlyCertificationConstants.EvidenceStatusPartial,
            ElderlyCertificationConstants.EvidenceStatusAmbiguous,
            ReportRowLimit);
        if (query.IsFailure)
            return Result.Failure<ElderlyCertificationReport>(query.ErrorCode!, query.Message!);

        var rows = BuildSummaryRows(query.Value ?? new List<ElderlyCertificationSummarySourceRow>());
        return Result.Success(new ElderlyCertificationReport(
            ElderlyCertificationReportFormKeys.Summary, title, SummaryColumns, rows));
    }

    /// <summary>按乡镇/村行生成汇总行，并在最后追加合计行（纯逻辑，便于单元测试）。</summary>
    public static List<ElderlyCertificationReportRow> BuildSummaryRows(
        IReadOnlyList<ElderlyCertificationSummarySourceRow> source)
    {
        var rows = new List<ElderlyCertificationReportRow>(source.Count + 1);
        foreach (var item in source)
        {
            rows.Add(BuildSummaryRow(
                item.TownName ?? string.Empty,
                item.VillageName ?? string.Empty,
                item.Total, item.Verified, item.Exempt, item.Pending, item.Refused, item.Failed,
                item.Deceased, item.MovedOut, item.NotLocated, item.Stopped, item.EvidenceIssue));
        }

        var total = source.Sum(r => r.Total);
        var verified = source.Sum(r => r.Verified);
        var exempt = source.Sum(r => r.Exempt);
        rows.Add(BuildSummaryRow(
            "合计",
            string.Empty,
            total, verified, exempt,
            source.Sum(r => r.Pending),
            source.Sum(r => r.Refused),
            source.Sum(r => r.Failed),
            source.Sum(r => r.Deceased),
            source.Sum(r => r.MovedOut),
            source.Sum(r => r.NotLocated),
            source.Sum(r => r.Stopped),
            source.Sum(r => r.EvidenceIssue)));
        return rows;
    }

    /// <summary>认证率 = (已通过 + 免核查) / 应认证 × 100，保留 1 位小数；总数为 0 时返回 0。</summary>
    public static decimal CalculateCertificationRate(int total, int verified, int exempt) =>
        total > 0 ? Math.Round((verified + exempt) * 100m / total, 1) : 0m;

    private static ElderlyCertificationReportRow BuildSummaryRow(
        string townName,
        string villageName,
        int total,
        int verified,
        int exempt,
        int pending,
        int refused,
        int failed,
        int deceased,
        int movedOut,
        int notLocated,
        int stopped,
        int evidenceIssue) =>
        ElderlyCertificationReportRow.From(
            ("TownName", townName),
            ("VillageName", villageName),
            ("Total", total),
            ("Verified", verified),
            ("Exempt", exempt),
            ("Pending", pending),
            ("Refused", refused),
            ("Failed", failed),
            ("Deceased", deceased),
            ("MovedOut", movedOut),
            ("NotLocated", notLocated),
            ("Stopped", stopped),
            ("EvidenceIssue", evidenceIssue),
            ("CertificationRate", CalculateCertificationRate(total, verified, exempt)));

    #endregion

    #region 未认证名单

    private async Task<Result<ElderlyCertificationReport>> BuildUnverifiedAsync(
        ElderlyCertCampaign campaign,
        string title,
        CancellationToken ct)
    {
        const string sql = @"
            SELECT tw.town_name AS town_name,
                   COALESCE(NULLIF(t.hukou_village, ''), v.village_name) AS village_name,
                   t.id_card, t.name, t.gender, t.birth_date, t.age_snapshot, t.phone,
                   t.cert_status, t.evidence_status, t.due_stop_date, t.death_date,
                   t.stop_decision, t.follow_up_date,
                   COALESCE(NULLIF(t.stop_note, ''), t.result_note) AS stop_note,
                   t.result_note
            FROM nc_biz_elderly_cert_tasks t
            LEFT JOIN nc_regions_towns tw ON tw.id = t.hukou_town_id
            LEFT JOIN nc_regions_villages v ON v.id = t.hukou_village_id
            WHERE t.campaign_id = $1 AND t.deleted_at IS NULL
              AND t.cert_status = ANY($2)
              AND t.cert_status <> $3
              AND t.stop_decision <> $4
            ORDER BY tw.town_name NULLS LAST, village_name NULLS LAST, t.name, t.id_card
            LIMIT $5";

        var query = await _db.QueryAsync<ElderlyCertificationUnverifiedSourceRow>(sql, ct,
            campaign.Id,
            UnverifiedStatuses,
            ElderlyCertificationConstants.CertStatusStopped,
            ElderlyCertificationConstants.StopDecisionApproved,
            ReportRowLimit);
        if (query.IsFailure)
            return Result.Failure<ElderlyCertificationReport>(query.ErrorCode!, query.Message!);

        var source = query.Value ?? new List<ElderlyCertificationUnverifiedSourceRow>();
        var rows = BuildUnverifiedRows(source, campaign);
        return Result.Success(new ElderlyCertificationReport(
            ElderlyCertificationReportFormKeys.Unverified, title, UnverifiedColumns, rows));
    }

    /// <summary>
    /// 未认证源行 → 报表行。公开给 Web Server 在 scoped 查询后复用。
    /// </summary>
    public static List<ElderlyCertificationReportRow> BuildUnverifiedRows(
        IReadOnlyList<ElderlyCertificationUnverifiedSourceRow> source,
        ElderlyCertCampaign campaign)
    {
        var rows = new List<ElderlyCertificationReportRow>(source.Count);
        var seq = 0;
        foreach (var item in source)
        {
            seq++;
            var reasonKey = ElderlyCertificationStopPreviewHelper.GetSuggestedStopReason(item.CertStatus);
            var dueStopDate = item.DueStopDate ?? ElderlyCertificationStopPreviewHelper.ComputeSuggestedDueStopDate(
                item.CertStatus, item.DeathDate, campaign.PeriodEnd, campaign.DeadlineStopOffset);

            rows.Add(ElderlyCertificationReportRow.From(
                ("Seq", seq),
                ("Name", item.Name),
                ("IdCard", item.IdCard),
                ("Gender", FormatGender(item.Gender)),
                ("Age", item.AgeSnapshot),
                ("TownName", item.TownName),
                ("VillageName", item.VillageName),
                ("Phone", item.Phone),
                ("CertStatus", ElderlyCertificationConstants.GetCertStatusName(item.CertStatus)),
                ("EvidenceStatus", ElderlyCertificationConstants.GetEvidenceStatusName(item.EvidenceStatus)),
                ("SuggestedStopReason", string.IsNullOrEmpty(reasonKey)
                    ? string.Empty
                    : ElderlyBenefitConstants.GetStopReasonName(reasonKey)),
                ("DueStopDate", dueStopDate),
                ("StopDecision", ElderlyCertificationConstants.GetStopDecisionName(item.StopDecision)),
                ("FollowUpDate", item.FollowUpDate),
                ("Note", item.StopNote ?? item.ResultNote)));
        }
        return rows;
    }

    #endregion

    #region 内部查询/显示格式

    private async Task<Result<ElderlyCertCampaign?>> GetCampaignAsync(long campaignId, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, campaign_no, year, round, name, period_start, period_end, valid_until,
                   status, require_evidence, deadline_stop_offset, roster_generated_at,
                   closed_at, closed_by, remark, created_at, updated_at, created_by, updated_by, deleted_at
            FROM nc_biz_elderly_cert_campaigns
            WHERE id = $1 AND deleted_at IS NULL
            LIMIT 1";

        var result = await _db.QueryAsync<ElderlyCertCampaign>(sql, ct, campaignId);
        if (result.IsFailure)
            return Result.Failure<ElderlyCertCampaign?>(result.ErrorCode!, result.Message!);

        return Result.Success<ElderlyCertCampaign?>(result.Value?.FirstOrDefault());
    }

    private static string FormatGender(string? gender) => gender switch
    {
        "1" or "M" or "m" or "Male" => "男",
        "2" or "F" or "f" or "Female" => "女",
        _ => gender ?? string.Empty
    };

    #endregion
}
