using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.SocialAssistance;

public class SubsidyImportRecord
{
    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string SubsidyType { get; set; } = string.Empty;
    public decimal Area { get; set; }
    public decimal Amount { get; set; }
}

public interface ISubsidyDataService
{
    Task<Result<List<SubsidyImportRecord>>> GetSubsidiesByIdCardsAsync(
        List<string> idCards, CancellationToken ct = default);
}
