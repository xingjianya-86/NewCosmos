using NewCosmos.Models.Entities;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.SocialAssistance;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

/// <summary>
/// 申请服务接口
/// </summary>
public interface IApplicationService
{
    /// <summary>
    /// 根据ID获取申请
    /// </summary>
    Task<Result<ApplicationEntity>> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// 根据身份证号获取申请列表
    /// </summary>
    Task<Result<List<ApplicationEntity>>> GetByIdCardAsync(string idCard, CancellationToken ct = default);

    /// <summary>
    /// 分页查询申请列表
    /// </summary>
    Task<Result<PagedResult<ApplicationEntity>>> GetPagedAsync(int pageIndex, int pageSize, string status = null, string keyword = null, CancellationToken ct = default);

    /// <summary>
    /// 按月分页查询档案（updated_at 范围口径，与统一补打中心名单业务时间一致）
    /// </summary>
    Task<Result<PagedResult<ApplicationEntity>>> GetByMonthPagedAsync(int year, int month, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 搜索申请（分页）
    /// </summary>
    Task<Result<PagedResult<ApplicationEntity>>> SearchPagedAsync(string? keyword, string? status, string? role, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 创建申请
    /// </summary>
    Task<Result<long>> CreateAsync(ApplicationCreateRequest request, CancellationToken ct = default);

    /// <summary>
    /// 创建申请（简化版    /// </summary>
    Task<Result<long>> CreateAsync(ApplicationEntity application, CancellationToken ct = default);

    /// <summary>
    /// 更新申请
    /// </summary>
    /// <param name="request">更新请求</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="allowNonEditable">是否允许更新非草稿状态档案（补全模式对已建档的 Approved 档案使用）</param>
    Task<Result> UpdateAsync(ApplicationUpdateRequest request, CancellationToken ct = default, bool allowNonEditable = false);

    /// <summary>
    /// 更新申请（简化版
    /// </summary>
    /// <param name="application">申请实体</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="allowNonEditable">是否允许更新非草稿状态档案（补全模式对已建档的 Approved 档案使用）</param>
    Task<Result> UpdateAsync(ApplicationEntity application, CancellationToken ct = default, bool allowNonEditable = false);

    /// <summary>
    /// 标记导入库建档档案的数据补全完成（置 data_completed_at=NOW()）
    /// </summary>
    Task<Result> MarkDataCompletedAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// 删除申请（草稿状态）
    /// </summary>
    Task<Result> DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// 提交申请
    /// </summary>
    Task<Result> SubmitAsync(long id, string submittedBy, CancellationToken ct = default);

    /// <summary>
    /// 审批申请
    /// </summary>
    Task<Result> ApproveAsync(ApplicationApproveRequest request, CancellationToken ct = default);

    /// <summary>
    /// 更新经济信息
    /// </summary>
    Task<Result> UpdateEconomicInfoAsync(EconomicInfoUpdateRequest request, CancellationToken ct = default);

    /// <summary>
    /// 获取下一个申请编号（按当天日期前缀）
    /// </summary>
    Task<Result<string>> GetNextApplicationNoAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取下一个申请编号（按指定日期前缀，导入库建档按纳入月份编号）
    /// </summary>
    Task<Result<string>> GetNextApplicationNoAsync(DateTime date, CancellationToken ct = default);

/// <summary>
    /// 检查身份证号是否存    /// </summary>
    Task<Result<bool>> CheckIdCardExistsAsync(string idCard, long? excludeId = null, CancellationToken ct = default);

    /// <summary>
    /// 获取已完成档案建设但未提交的申请（分页，支持关键词搜索）
    /// CurrentStep 已完成且 status='Draft'
    /// </summary>
    Task<Result<PagedResult<ApplicationEntity>>> GetArchiveBuiltNotSubmittedPagedAsync(string keyword, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 获取已完成档案建设但未提交的申请总数
    /// </summary>
    Task<Result<int>> GetArchiveBuiltNotSubmittedCountAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取草稿申请（分页，支持关键词搜索）
    /// status='Draft' AND current_step < 5
    /// </summary>
    Task<Result<PagedResult<ApplicationEntity>>> GetDraftPagedAsync(string keyword, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 获取草稿申请总数
    /// </summary>
    Task<Result<int>> GetDraftCountAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取已完结档案（分页，支持关键词搜索）
    /// current_step = 6
    /// </summary>
    Task<Result<PagedResult<ApplicationEntity>>> GetArchivedPagedAsync(string keyword, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 获取已完结档案总数
    /// </summary>
    Task<Result<int>> GetArchivedCountAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取已停保档案（分页，支持关键词搜索）
    /// status = 'Stopped'
    /// </summary>
    Task<Result<PagedResult<ApplicationEntity>>> GetStoppedPagedAsync(string keyword, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 获取已停保档案总数
    /// </summary>
    Task<Result<int>> GetStoppedCountAsync(CancellationToken ct = default);

    /// <summary>
    /// 更新申请的当前步骤
    /// </summary>
    Task<Result> UpdateCurrentStepAsync(long applicationId, int step, CancellationToken ct = default);

    /// <summary>
    /// 完成归档：current_step 推进到 6 并将 status 置为 Approved（已审批/在保）。
    /// 系统无独立审批工作流，归档即视为审批通过；已 Approved/Completed 的档案仅推进步骤，不改状态。
    /// </summary>
    Task<Result> CompleteArchiveAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 为单人保成员创建草稿申请
    /// </summary>
    Task<Result<long>> CreateSingleRescueDraftAsync(long sourceApplicationId, FamilyMember member, CancellationToken ct = default);
}
