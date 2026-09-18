using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 入户调查服务接口
/// </summary>
public interface IHouseholdSurveyService
{
    /// <summary>
    /// 根据申请ID获取入户调查
    /// </summary>
    Task<Result<HouseholdSurvey?>> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 保存入户调查（存在则更新，不存在则创建）
    /// </summary>
    Task<Result<long>> SaveAsync(long applicationId, HouseholdSurvey survey, CancellationToken ct = default);

    /// <summary>
    /// 删除入户调查
    /// </summary>
    Task<Result> DeleteByApplicationIdAsync(long applicationId, CancellationToken ct = default);
}
