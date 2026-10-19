using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.NearRelative;

/// <summary>
/// 近亲属备案服务接口
/// 无年月维度——全量名册；月报表输出忽略年月，任何月份均输出全部有效备案。
/// </summary>
public interface INearRelativeService
{
    /// <summary>
    /// 备案名册列表（列表页数据源；town 为空 = 全部乡镇）
    /// </summary>
    Task<Result<List<NearRelativeBrief>>> GetBriefsAsync(string? town = null, CancellationToken ct = default);

    /// <summary>
    /// 单条备案详情（工作人员 + 对象明细）
    /// </summary>
    Task<Result<NearRelativeEntry>> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// 保存单条备案（新增=插 staff+links；编辑=更新 staff + 重建 links）。
    /// 仅影响本条备案，不触碰其他任何人的数据；事务保护，失败全回滚。
    /// 编辑时校验版本（updated_at）：条已被他人修改则拒绝保存。
    /// </summary>
    Task<Result<long>> SaveAsync(NearRelativeEntry entry, CancellationToken ct = default);

    /// <summary>
    /// 删除单条备案（软删 staff + links）
    /// </summary>
    Task<Result> DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// 月报/全量取数：全部有效备案（town 空或"全部"=全量；否则按镇过滤）。忽略年月。
    /// </summary>
    Task<Result<List<NearRelativeEntry>>> GetAllForPrintAsync(string? town = null, CancellationToken ct = default);

    /// <summary>
    /// 档案出口：按申请ID检索其关联的备案（每对 staff+对象 一条）
    /// </summary>
    Task<Result<List<NearRelativePair>>> GetPairsByApplicationIdAsync(long applicationId, CancellationToken ct = default);

    // ── 档案联动（2026-10-17：输入身份证自动带出；编辑载入按档案刷新） ──

    /// <summary>
    /// 按工作人员身份证查已有有效备案（含对象明细）；未命中返回 null。
    /// 录入页输入身份证自动带出用（替代原"选择已有备案"下拉）。
    /// </summary>
    Task<Result<NearRelativeEntry?>> GetByStaffIdCardAsync(string idCard, CancellationToken ct = default);

    /// <summary>
    /// 按身份证检索经办（工作）人员工作信息，来源：系统用户表（启用用户，JOIN 机构/乡镇）+
    /// 档案照料护理人（nc_biz_caregivers 关联申请取乡镇 → nc_biz_destitute_persons.caregiver_id_card）。
    /// 合并规则：系统用户优先，照料人逐字段补空（申请档案 → 特困历史档案）；未命中任何来源返回 null。
    /// 返回来源标记（FoundInUsers/FoundInCaregivers/FoundInDestituteArchive）供界面提示。
    /// </summary>
    Task<Result<NearRelativeStaffLookup?>> GetStaffWorkInfoByIdCardAsync(string idCard, CancellationToken ct = default);

    /// <summary>
    /// 按救助对象身份证查本系统最新救助申请并映射为对象行（一一对应：自动回填 ApplicationId）。
    /// 取值顺序：在享（Approved/Completed）优先，其次最新一条（草稿/已停保也返回，来源提示含状态）。
    /// 未命中返回 null；命中但已被他人备案时 OccupiedByOtherStaff=true（保存前先提示）。
    /// </summary>
    Task<Result<NearRelativeArchiveFill?>> BuildLinkByIdCardAsync(string idCard, long? excludeStaffId = null, CancellationToken ct = default);

    /// <summary>
    /// 按申请ID映射对象行（工作流卡片入口 / 行重新读取用）。未命中返回 null。
    /// </summary>
    Task<Result<NearRelativeArchiveFill?>> BuildLinkByApplicationAsync(long applicationId, long? excludeStaffId = null, CancellationToken ct = default);

    /// <summary>
    /// 批量按申请ID刷新映射（编辑载入"按档案刷新"；= ANY 批查防 N+1）。返回命中的记录，未命中不包含。
    /// </summary>
    Task<Result<List<NearRelativeArchiveFill>>> RefreshLinksFromArchiveAsync(IReadOnlyCollection<long> applicationIds, long? excludeStaffId = null, CancellationToken ct = default);
}
