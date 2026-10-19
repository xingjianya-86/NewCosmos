using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.Recovery;

/// <summary>
/// 追缴历史数据取数器：把「台账/申请现值 + 保障金变更记录 + 渐退期原标准」组装成
/// 「逐月保障标准时间轴」（<see cref="RecoveryMonthlyStandardPoint"/>），
/// 供 <see cref="RecoveryAmountCalculator"/> 做分段自动计算。
///
/// 契约：**数据缺失就返回空（或不足的）时间轴，绝不构造/猜测金额**——
/// 由上层判定"算不出 → 转人工填写"。
/// </summary>
public interface IRecoveryHistoryProvider
{
    /// <summary>
    /// 获取来源记录的逐月保障标准时间轴（按生效月升序、同月后者覆盖前者）。
    /// </summary>
    /// <param name="sourceType">来源类型（RecoveryConstants.SOURCE_TYPE_*）</param>
    /// <param name="sourceId">来源记录ID（低保/刚性支出为 nc_biz_applications.id，临时救助/高龄为各自申请表ID）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>时间轴（可为空列表：表示无任何历史标准可用）</returns>
    Task<Result<List<RecoveryMonthlyStandardPoint>>> GetMonthlyStandardsAsync(
        string sourceType, long sourceId, CancellationToken ct = default);
}
