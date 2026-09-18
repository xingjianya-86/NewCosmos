using NewCosmos.Models;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.SocialAssistance;

public interface IPersonSearchService
{
    Task<Result<List<PersonSearchResult>>> SearchByIdCardAsync(
        string idCard, CancellationToken ct = default);
}
