namespace NewCosmos.Models.Results;

public class PdfVerificationResult
{
    public bool IsValid { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ExpectedName { get; set; } = string.Empty;
    public bool NameFoundInFileName { get; set; }
    public string Message { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime VerifiedAt { get; set; } = DateTime.Now;
}

public class PdfVerificationRecord
{
    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ExpectedName { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public long VerifiedBy { get; set; }
    public string VerifiedByName { get; set; } = string.Empty;
    public DateTime VerifiedAt { get; set; }
    public string Notes { get; set; } = string.Empty;
}
