using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 能力鉴定服务接口
/// </summary>
public interface ICapabilityAssessmentService
{
    /// <summary>
    /// 根据申请ID获取能力鉴定
    /// </summary>
    Task<Result<CapabilityAssessment?>> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 保存能力鉴定
    /// </summary>
    Task<Result<long>> SaveAsync(CapabilityAssessment assessment, CancellationToken ct = default);

    /// <summary>
    /// 删除能力鉴定
    /// </summary>
    Task<Result> DeleteAsync(long id, CancellationToken ct = default);
}
