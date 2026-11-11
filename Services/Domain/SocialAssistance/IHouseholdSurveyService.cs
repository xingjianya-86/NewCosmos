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
    /// 批量取多份申请的最新入户调查日期（工作流列表"批次顺延提示"用，= ANY 一次查询防 N+1）。
    /// 同一申请存在多条未删记录时按 id DESC 取最新一条（与 GetByApplicationIdAsync 同口径）；
    /// 无调查记录的申请不出现在字典中。
    /// </summary>
    Task<Result<IReadOnlyDictionary<long, DateTime>>> GetLatestSurveyDateMapByApplicationIdsAsync(
        IReadOnlyCollection<long> applicationIds, CancellationToken ct = default);

    /// <summary>
    /// 保存入户调查（存在则更新，不存在则创建）
    /// </summary>
    Task<Result<long>> SaveAsync(long applicationId, HouseholdSurvey survey, CancellationToken ct = default);

    /// <summary>
    /// 删除入户调查
    /// </summary>
    Task<Result> DeleteByApplicationIdAsync(long applicationId, CancellationToken ct = default);
}
