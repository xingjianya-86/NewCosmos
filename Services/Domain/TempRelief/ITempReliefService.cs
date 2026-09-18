using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.TempRelief;

/// <summary>
/// 临时救助服务接口
/// </summary>
public interface ITempReliefService
{
    /// <summary>
    /// 检索申请人候选人（跨 5 个导入台账库 + 低保申请库；姓名/身份证模糊匹配）
    /// </summary>
    Task<Result<List<TempReliefCandidate>>> SearchCandidatesAsync(string keyword, string? excludeIdCard = null, CancellationToken ct = default);

    /// <summary>
    /// 获取候选人详情（含家庭成员快照），选中后用于表单回填
    /// </summary>
    Task<Result<TempReliefCandidate>> GetCandidateDetailAsync(string sourceTable, long sourceFamilyId, string? excludeIdCard = null, CancellationToken ct = default);

    /// <summary>
    /// 获取小额定额档位列表（启用中）
    /// </summary>
    Task<Result<List<Models.Entities.ConfigStandard>>> GetSmallAmountLevelsAsync(CancellationToken ct = default);

    Task<Result<TempReliefApplication>> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// 按"填报单位"名称查组织并取其联系电话（用于公示异议反馈电话；查不到返回空串）
    /// </summary>
    Task<Result<string>> GetReportUnitPhoneAsync(string? reportUnit, CancellationToken ct = default);

    /// <summary>
    /// 分页查询（关键字：姓名/身份证；状态：空=全部）
    /// </summary>
    Task<Result<PagedResult<TempReliefApplication>>> GetPagedAsync(string keyword, string status, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 分页查询已完结记录（已确认+已终止，供历史补打用）
    /// </summary>
    Task<Result<PagedResult<TempReliefApplication>>> GetArchivedPagedAsync(string? keyword, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 按月分页查询已完结记录（业务时间口径 COALESCE(apply_date, report_time, created_at)）
    /// </summary>
    Task<Result<PagedResult<TempReliefApplication>>> GetByMonthPagedAsync(int year, int month, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 创建申请（校验年度唯一：同一身份证当年仅一条）
    /// </summary>
    Task<Result<long>> CreateAsync(TempReliefApplication application, List<TempReliefMember> members, CancellationToken ct = default);

    /// <summary>
    /// 更新申请（草稿可编辑）
    /// </summary>
    Task<Result> UpdateAsync(TempReliefApplication application, List<TempReliefMember> members, CancellationToken ct = default);

    /// <summary>
    /// 确认生效（草稿→已确认）
    /// </summary>
    Task<Result> ConfirmAsync(long id, string operatorName, CancellationToken ct = default);

    /// <summary>
    /// 终止（已确认→已终止）
    /// </summary>
    Task<Result> StopAsync(long id, string reason, string operatorName, CancellationToken ct = default);

    /// <summary>
    /// 删除（软删除，草稿可删）
    /// </summary>
    Task<Result> DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// 获取家庭成员明细（按申请ID）
    /// </summary>
    Task<Result<List<TempReliefMember>>> GetMembersByApplicationIdAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 获取疾病明细（按申请ID，可多条）
    /// </summary>
    Task<Result<List<TempReliefDisease>>> GetDiseasesByApplicationIdAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 获取意外灾害明细（按申请ID，可多条）
    /// </summary>
    Task<Result<List<TempReliefAccident>>> GetAccidentsByApplicationIdAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 获取教育支出明细（按申请ID，可多条）
    /// </summary>
    Task<Result<List<TempReliefEducation>>> GetEducationsByApplicationIdAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 判断该身份证当年是否已存在申请（排除指定记录）
    /// </summary>
    Task<Result<bool>> CheckAnnualExistsAsync(string idCard, string applyYear, long? excludeId = null, CancellationToken ct = default);

    /// <summary>
    /// 按身份证检索已享受/在申的救助政策清单（遍历白名单来源表）
    /// </summary>
    Task<Result<List<string>>> GetPolicySnapshotAsync(string idCard, CancellationToken ct = default);

    /// <summary>
    /// 按身份证检索家庭成员健康状况汇总文本（源自低保管家庭成员明细；无数据返回空字符串）
    /// </summary>
    Task<Result<string>> GetFamilyMemberStatusAsync(string idCard, CancellationToken ct = default);
}
