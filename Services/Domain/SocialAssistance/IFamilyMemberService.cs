using NewCosmos.Models.Entities;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 家庭成员服务接口
/// </summary>
public interface IFamilyMemberService
{
    /// <summary>
    /// 根据申请ID获取家庭成员列表
    /// </summary>
    Task<Result<List<FamilyMember>>> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 根据ID获取家庭成员
    /// </summary>
    Task<Result<FamilyMember>> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// 添加家庭成员
    /// </summary>
    Task<Result<long>> AddAsync(FamilyMemberCreateRequest request, CancellationToken ct = default);

    /// <summary>
    /// 更新家庭成员
    /// </summary>
    Task<Result> UpdateAsync(FamilyMemberUpdateRequest request, CancellationToken ct = default);

    /// <summary>
    /// 删除家庭成员
    /// </summary>
    Task<Result> DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// 删除申请的所有家庭成员（软删除），返回删除行数
    /// </summary>
    Task<Result<int>> DeleteByApplicationIdAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 查询申请下已登记死亡的成员身份证（历史数据未软删，展示/生成档案时须过滤）
    /// </summary>
    Task<Result<List<string>>> GetDeadIdCardsByApplicationIdAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 设置户主
    /// </summary>
    Task<Result> SetHouseholdHeadAsync(long applicationId, long memberId, CancellationToken ct = default);

    /// <summary>
    /// 检查是否可以删除成    /// </summary>
    Task<Result<bool>> CanDeleteMemberAsync(long applicationId, long memberId, CancellationToken ct = default);

    /// <summary>
    /// 获取家庭成员数量
    /// </summary>
    Task<Result<int>> GetCountByApplicationIdAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 批量更新收入
    /// </summary>
    Task<Result> BatchUpdateIncomeAsync(long applicationId, Dictionary<long, decimal> incomes, CancellationToken ct = default);
}
