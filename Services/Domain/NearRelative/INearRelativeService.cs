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
    /// 下拉列表（选择已有备案；town 为空 = 全部乡镇）
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
}
