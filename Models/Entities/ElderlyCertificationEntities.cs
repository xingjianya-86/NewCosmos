using NewCosmos.Constants;

namespace NewCosmos.Models.Entities;

/// <summary>认证批次（nc_biz_elderly_cert_campaigns）</summary>
public class ElderlyCertCampaign
{
    public long Id { get; set; }
    public string CampaignNo { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Round { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string Status { get; set; } = ElderlyCertificationConstants.CampaignStatusDraft;
    public bool RequireEvidence { get; set; } = true;
    public int DeadlineStopOffset { get; set; } = 1;
    public DateTime? RosterGeneratedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? ClosedBy { get; set; }
    public string? Remark { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? DeletedAt { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? $"{Year}年度"
        : Name.Contains($"{Year}年度", StringComparison.Ordinal) ? Name : $"{Year}年度 {Name}";
    public string StatusName => ElderlyCertificationConstants.GetCampaignStatusName(Status);
    public string PeriodDisplay => $"{PeriodStart:yyyy-MM-dd} ~ {PeriodEnd:yyyy-MM-dd}";
    public string RoundDisplay => Round <= 1 ? "首轮" : $"第{Round}轮";
}

/// <summary>认证任务/名单快照（nc_biz_elderly_cert_tasks）</summary>
public class ElderlyCertTask
{
    public long Id { get; set; }
    public long CampaignId { get; set; }
    public string IdCard { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long? ApplicationId { get; set; }
    public string? ApplicationNo { get; set; }
    public string SourceKind { get; set; } = "Application";
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; }
    public int? AgeSnapshot { get; set; }
    public string? Category { get; set; }
    public string? IdentityFlag { get; set; }
    public decimal? IssueAmount { get; set; }
    public int? HukouCityId { get; set; }
    public int? HukouCountyId { get; set; }
    public int? HukouTownId { get; set; }
    public int? HukouVillageId { get; set; }
    public string? HukouVillage { get; set; }
    public string? HukouAddress { get; set; }
    public int? FamilyCityId { get; set; }
    public int? FamilyCountyId { get; set; }
    public int? FamilyTownId { get; set; }
    public int? FamilyVillageId { get; set; }
    public string? FamilyAddress { get; set; }
    public string? Phone { get; set; }
    public string? BankName { get; set; }
    public string? BankAccount { get; set; }
    public string? AgentName { get; set; }
    public string? AgentRelation { get; set; }
    public DateTime? SnapshotAt { get; set; }
    public string CertStatus { get; set; } = ElderlyCertificationConstants.CertStatusPending;
    public string? VerifyMethod { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? VerifiedBy { get; set; }
    public string? VerifierOrg { get; set; }
    public string? VerifierPhone { get; set; }
    public string? ResultNote { get; set; }
    public DateTime? DeathDate { get; set; }
    public int ExpectedEvidenceCount { get; set; }
    public int EvidenceCount { get; set; }
    public string EvidenceStatus { get; set; } = ElderlyCertificationConstants.EvidenceStatusNone;
    public string StopDecision { get; set; } = ElderlyCertificationConstants.StopDecisionNone;
    public string? StopReason { get; set; }
    public string? StopNote { get; set; }
    public DateTime? FollowUpDate { get; set; }
    public DateTime? DueStopDate { get; set; }
    public DateTime? ActualStopDate { get; set; }
    public long? StopApplicationId { get; set; }
    public DateTime? StoppedAt { get; set; }
    public string? SourceBatchNo { get; set; }
    public string? SourceFile { get; set; }
    public int? SourceRow { get; set; }
    public DateTime? SourceSubmittedAt { get; set; }
    public DateTime? LastImportedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public string CertStatusName => ElderlyCertificationConstants.GetCertStatusName(CertStatus);
    public string VerifyMethodName => ElderlyCertificationConstants.GetVerifyMethodName(VerifyMethod);
    public string EvidenceStatusName => ElderlyCertificationConstants.GetEvidenceStatusName(EvidenceStatus);
    public string StopDecisionName => ElderlyCertificationConstants.GetStopDecisionName(StopDecision);
    public string EvidenceDisplay => ExpectedEvidenceCount > 0
        ? $"{EvidenceCount}/{ExpectedEvidenceCount}"
        : EvidenceCount.ToString();
    public string VillageDisplay => string.IsNullOrWhiteSpace(HukouVillage)
        ? (HukouAddress ?? string.Empty)
        : HukouVillage;
    public string AgeDisplay => AgeSnapshot.HasValue ? $"{AgeSnapshot}岁" : string.Empty;
    public bool IsStopCandidate => ElderlyCertificationConstants.IsStopCandidate(CertStatus);
    public bool HasEvidenceIssue => EvidenceStatus is
        ElderlyCertificationConstants.EvidenceStatusMissing
        or ElderlyCertificationConstants.EvidenceStatusPartial
        or ElderlyCertificationConstants.EvidenceStatusAmbiguous;
}

/// <summary>认证凭证（nc_biz_elderly_cert_evidence）</summary>
public class ElderlyCertEvidence
{
    public long Id { get; set; }
    public long? TaskId { get; set; }
    public long? BatchId { get; set; }
    public int Seq { get; set; } = 1;
    public string? EvidenceType { get; set; } = ElderlyCertificationConstants.EvidenceTypeOther;
    public string? OriginalName { get; set; }
    public string? StoredName { get; set; }
    public string? DisplayName { get; set; }
    public string? SourceZip { get; set; }
    public string? SourceEntry { get; set; }
    public string MatchStatus { get; set; } = ElderlyCertificationConstants.EvidenceMatchUnmatched;
    public string? MatchMethod { get; set; }
    public byte[]? PhotoData { get; set; }
    public string ImageFormat { get; set; } = "jpg";
    public long FileSize { get; set; }
    public string? Sha256 { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? DeletedAt { get; set; }

    public string EvidenceTypeName => EvidenceType switch
    {
        ElderlyCertificationConstants.EvidenceTypeFace => "人脸认证",
        ElderlyCertificationConstants.EvidenceTypeOnsite => "上门核查",
        ElderlyCertificationConstants.EvidenceTypeIdCard => "身份证",
        _ => "其他"
    };

    public string Display => string.IsNullOrWhiteSpace(DisplayName) ? (OriginalName ?? string.Empty) : DisplayName!;
    public string FileSizeDisplay => FileSize > 1024 * 1024
        ? $"{FileSize / 1024.0 / 1024.0:F1} MB"
        : $"{FileSize / 1024.0:F0} KB";
}

/// <summary>导入批次（nc_biz_elderly_cert_import_batches）</summary>
public class ElderlyCertImportBatch
{
    public long Id { get; set; }
    public long CampaignId { get; set; }
    public string BatchNo { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? FileHash { get; set; }
    public long? FileSize { get; set; }
    public string? ZipName { get; set; }
    public string? ZipHash { get; set; }
    public long? ZipSize { get; set; }
    public int TotalRows { get; set; }
    public int MatchedRows { get; set; }
    public int UnknownRows { get; set; }
    public int DuplicateRows { get; set; }
    public int NameMismatchRows { get; set; }
    public int InvalidRows { get; set; }
    public int EvidenceTotal { get; set; }
    public int EvidenceMatched { get; set; }
    public int EvidenceMissing { get; set; }
    public int EvidenceUnmatched { get; set; }
    public int AppliedRows { get; set; }
    public int SkippedRows { get; set; }
    public int ErrorRows { get; set; }
    public string Status { get; set; } = ElderlyCertificationConstants.ImportBatchStatusPreviewed;
    public DateTime? AppliedAt { get; set; }
    public string? AppliedBy { get; set; }
    public string? Remark { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }

    public string StatusName => Status switch
    {
        ElderlyCertificationConstants.ImportBatchStatusPreviewed => "已预览",
        ElderlyCertificationConstants.ImportBatchStatusApplied => "已应用",
        ElderlyCertificationConstants.ImportBatchStatusPartiallyApplied => "部分应用",
        ElderlyCertificationConstants.ImportBatchStatusRevoked => "已撤销",
        ElderlyCertificationConstants.ImportBatchStatusFailed => "失败",
        _ => Status
    };
}

/// <summary>导入行暂存（nc_biz_elderly_cert_import_rows）</summary>
public class ElderlyCertImportRow
{
    public long Id { get; set; }
    public long BatchId { get; set; }
    public int RowNo { get; set; }
    public string? RawName { get; set; }
    public string? RawIdCard { get; set; }
    public string? RawPhone { get; set; }
    public string? RawVillage { get; set; }
    public string? RawMethod { get; set; }
    public string? RawResult { get; set; }
    public string? RawVerifiedAt { get; set; }
    public string? RawSubmittedAt { get; set; }
    public string? RawRemark { get; set; }
    public string? RawFileNames { get; set; }
    public string? RawJson { get; set; }
    public string? IdCard { get; set; }
    public string? Name { get; set; }
    public string? CertStatus { get; set; }
    public string? VerifyMethod { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public int ExpectedEvidenceCount { get; set; }
    public int MatchedEvidenceCount { get; set; }
    public string EvidenceStatus { get; set; } = ElderlyCertificationConstants.EvidenceStatusNone;
    public long? TaskId { get; set; }
    public string MatchStatus { get; set; } = ElderlyCertificationConstants.ImportRowUnknown;
    public string? ErrorMessage { get; set; }
    public DateTime? CreatedAt { get; set; }
}

/// <summary>批次统计</summary>
public class ElderlyCertStats
{
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Verified { get; set; }
    public int Refused { get; set; }
    public int Failed { get; set; }
    public int Deceased { get; set; }
    public int MovedOut { get; set; }
    public int NotLocated { get; set; }
    public int Exempt { get; set; }
    public int Stopped { get; set; }
    public int Invalid { get; set; }
    public int EvidenceIssue { get; set; }
    public int StopCandidate { get; set; }

    public int Conclusive => Verified + Refused + Failed + Deceased + MovedOut + NotLocated + Exempt + Stopped;
    public decimal CompletionRate => Total > 0 ? Math.Round(Conclusive * 100m / Total, 1) : 0m;
    public string CompletionRateText => $"{CompletionRate:F1}%";
}
