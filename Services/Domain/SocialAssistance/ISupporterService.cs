using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.SocialAssistance;

public interface ISupporterService
{
    Task<Result<List<Supporter>>> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default);
    Task<Result> SaveAsync(long applicationId, List<Supporter> supporters, CancellationToken ct = default);
    Task<Result> DeleteByApplicationIdAsync(long applicationId, CancellationToken ct = default);
}

public interface ICaregiverService
{
    Task<Result<List<Caregiver>>> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default);
    Task<Result> SaveAsync(long applicationId, List<Caregiver> caregivers, CancellationToken ct = default);
    Task<Result> DeleteByApplicationIdAsync(long applicationId, CancellationToken ct = default);
}
