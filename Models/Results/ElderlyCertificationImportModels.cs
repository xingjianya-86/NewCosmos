using NewCosmos.Constants;

namespace NewCosmos.Models.Results;

/// <summary>ZIP 内一个图片条目的元数据（预览阶段不读图片内容）</summary>
public sealed class ElderlyCertZipEntryInfo
{
    public string EntryName { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long Length { get; init; }
    public string Extension { get; init; } = string.Empty;
    public string DisplaySize => Length > 1024 * 1024
        ? $"{Length / 1024.0 / 1024.0:F1} MB"
        : $"{Length / 1024.0:F0} KB";
}

/// <summary>Excel 一行导入预览</summary>
public sealed class ElderlyCertImportRowPreview
{
    public int RowNo { get; set; }
    public string? IdCard { get; set; }
    public string? Name { get; set; }
    public string? RawName { get; set; }
    public string? RawIdCard { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? BankName { get; set; }
    public string? BankAccount { get; set; }
    public string? AgentName { get; set; }
    public string? AgentRelation { get; set; }
    public string? AgentReceiveName { get; set; }
    public string? AgentReceiveRelation { get; set; }
    public string? AgentReceiveBankName { get; set; }
    public string? AgentReceiveBankAccount { get; set; }
    public string? Village { get; set; }
    public string? RawMethod { get; set; }
    public string? RawResult { get; set; }
    public string? Operator { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? DeathDate { get; set; }
    public string? Remark { get; set; }
    public string CertStatus { get; set; } = ElderlyCertificationConstants.CertStatusPending;
    public string VerifyMethod { get; set; } = ElderlyCertificationConstants.VerifyMethodOther;

    public List<string> FileNames { get; set; } = new();
    public List<ElderlyCertZipEntryInfo> MatchedEntries { get; set; } = new();
    public List<string> MissingFileNames { get; set; } = new();
    public List<string> AmbiguousFileNames { get; set; } = new();

    public int ExpectedEvidenceCount { get; set; }
    public int MatchedEvidenceCount { get; set; }
    public string EvidenceStatus { get; set; } = ElderlyCertificationConstants.EvidenceStatusNone;

    public long? TaskId { get; set; }
    public string? TaskName { get; set; }
    public string? TaskCertStatus { get; set; }
    public int TaskEvidenceCount { get; set; }

    /// <summary>Matched / Unknown / NameMismatch / Invalid / Conflict</summary>
    public string MatchStatus { get; set; } = ElderlyCertificationConstants.ImportRowUnknown;
    public string? ErrorMessage { get; set; }
    public bool IsDuplicate { get; set; }
    public bool IsNameMismatch { get; set; }

    public bool CanApply => MatchStatus is not (ElderlyCertificationConstants.ImportRowInvalid
        or ElderlyCertificationConstants.ImportRowUnknown
        or ElderlyCertificationConstants.ImportRowDuplicate);

    public string MatchStatusName => ElderlyCertificationConstants.GetImportRowMatchStatusName(MatchStatus);
    public string CertStatusName => ElderlyCertificationConstants.GetCertStatusName(CertStatus);
    public string VerifyMethodName => ElderlyCertificationConstants.GetVerifyMethodName(VerifyMethod);
    public string EvidenceStatusName => ElderlyCertificationConstants.GetEvidenceStatusName(EvidenceStatus);
    public string EvidenceDisplay => ExpectedEvidenceCount > 0
        ? $"{MatchedEvidenceCount}/{ExpectedEvidenceCount}"
        : MatchedEvidenceCount.ToString();
}

/// <summary>Excel+ZIP 导入预览结果</summary>
public sealed class ElderlyCertImportPreview
{
    public long CampaignId { get; set; }
    public string CampaignNo { get; set; } = string.Empty;
    public string CampaignName { get; set; } = string.Empty;
    public string BatchNo { get; set; } = string.Empty;
    public string ExcelPath { get; set; } = string.Empty;
    public string ZipPath { get; set; } = string.Empty;
    public string ExcelHash { get; set; } = string.Empty;
    public string ZipHash { get; set; } = string.Empty;
    public string ZipEncoding { get; set; } = "utf-8";
    public long ExcelSize { get; set; }
    public long ZipSize { get; set; }
    public DateTime PreviewedAt { get; set; } = DateTime.Now;

    public int TotalRows { get; set; }
    public int MatchedRows { get; set; }
    public int UnknownRows { get; set; }
    public int DuplicateRows { get; set; }
    public int NameMismatchRows { get; set; }
    public int InvalidRows { get; set; }
    public int ConflictRows { get; set; }

    public int EvidenceTotal { get; set; }
    public int EvidenceMatched { get; set; }
    public int EvidenceMissing { get; set; }
    public int EvidenceUnmatched { get; set; }

    public List<ElderlyCertImportRowPreview> Rows { get; set; } = new();
    public List<ElderlyCertZipEntryInfo> UnmatchedEntries { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();

    public bool HasBlockingErrors => Rows.Count == 0 || Errors.Count > 0;
    public int ApplicableRows => Rows.Count(r => r.CanApply);

    public string SummaryText =>
        $"共 {TotalRows} 行：匹配 {MatchedRows}，名单外 {UnknownRows}，重复 {DuplicateRows}，姓名不一致 {NameMismatchRows}，冲突 {ConflictRows}，无效 {InvalidRows}；" +
        $"凭证 {EvidenceMatched}/{EvidenceTotal}，缺失 {EvidenceMissing}，多余图片 {EvidenceUnmatched}";
}

/// <summary>导入应用选项</summary>
public sealed class ElderlyCertImportApplyOptions
{
    public long CampaignId { get; set; }
    public string ExcelPath { get; set; } = string.Empty;
    public string ZipPath { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;

    /// <summary>允许把已通过的人覆盖为不通过/去世等不利结果（默认 false，需人工确认）。</summary>
    public bool AllowAdverseOverwrite { get; set; }

    /// <summary>预览时的文件哈希，应用前校验文件未变化。</summary>
    public string ExcelHash { get; set; } = string.Empty;
    public string ZipHash { get; set; } = string.Empty;

    /// <summary>可选：只应用指定行号；为空表示全部可应用行。</summary>
    public HashSet<int>? RowNumbers { get; set; }
}

/// <summary>导入应用结果</summary>
public sealed class ElderlyCertImportApplyResult
{
    public long BatchId { get; set; }
    public string BatchNo { get; set; } = string.Empty;
    public int AppliedRows { get; set; }
    public int SkippedRows { get; set; }
    public int ErrorRows { get; set; }
    public int EvidenceInserted { get; set; }
    public int EvidenceSkipped { get; set; }
    public int EvidenceUnmatched { get; set; }

    /// <summary>导入联动生成的 Pending 信息变更单数。</summary>
    public int InfoChangesCreated { get; set; }

    /// <summary>导入值未变化/无法生成变更而跳过的任务数。</summary>
    public int InfoChangesSkipped { get; set; }

    /// <summary>生成信息变更失败的任务数。</summary>
    public int InfoChangeErrors { get; set; }

    public List<string> Errors { get; set; } = new();
    public bool IsPartial => SkippedRows > 0 || ErrorRows > 0 || InfoChangeErrors > 0;

    public string SummaryText =>
        $"应用 {AppliedRows} 行，跳过 {SkippedRows} 行，失败 {ErrorRows} 行；" +
        $"凭证入库 {EvidenceInserted} 张，重复跳过 {EvidenceSkipped} 张，未匹配 {EvidenceUnmatched} 张；" +
        $"信息变更生成 {InfoChangesCreated} 条，跳过 {InfoChangesSkipped} 条，失败 {InfoChangeErrors} 条";
}
