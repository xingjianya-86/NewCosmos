using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.Recovery;

/// <summary>
/// 后补追缴服务接口
/// </summary>
public interface IRecoveryService
{
    /// <summary>
    /// 搜索停止人员（低保、刚性支出、临时救助、高龄）
    /// </summary>
    /// <param name="keyword">搜索关键词（姓名或身份证）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>停止人员列表</returns>
    Task<Result<List<StoppedPersonDto>>> SearchStoppedPersonsAsync(
        string keyword, CancellationToken ct = default);

    /// <summary>
    /// 获取默认月保障额（根据来源类型和来源ID）
    /// </summary>
    /// <param name="sourceType">来源类型</param>
    /// <param name="sourceId">来源记录ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>月保障额</returns>
    Task<Result<decimal>> GetDefaultMonthlyAmountAsync(
        string sourceType, long sourceId, CancellationToken ct = default);

    /// <summary>
    /// 手工录入页按身份证查档（自动带出追缴分类 / 每月保障标准 / 停发原因）。
    /// 同一身份证命中多条时按「停发优先 → 分类（低保系 &gt; 临时救助 &gt; 高龄）→ 最新」只取一条。
    /// 未命中返回 Success(null)——不是失败（手工录入允许档案外人员），由界面提示转手工填写。
    /// </summary>
    /// <param name="idCard">身份证号（18 位）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>档案带出信息，未命中为 null</returns>
    Task<Result<RecoveryArchiveLookupDto?>> LookupArchiveByIdCardAsync(
        string idCard, CancellationToken ct = default);

    /// <summary>
    /// 保存追缴记录（搜索录入）
    /// </summary>
    /// <param name="dto">追缴记录DTO</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>追缴记录ID</returns>
    Task<Result<long>> SaveRecoveryRecordAsync(
        RecoveryRecordDto dto, CancellationToken ct = default);

    /// <summary>
    /// 保存追缴记录（手工录入）
    /// </summary>
    /// <param name="dto">手工追缴记录DTO</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>追缴记录ID</returns>
    Task<Result<long>> SaveManualRecoveryRecordAsync(
        ManualRecoveryRecordDto dto, CancellationToken ct = default);

    /// <summary>
    /// 获取追缴记录详情
    /// </summary>
    /// <param name="id">追缴记录ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>追缴记录</returns>
    Task<Result<RecoveryRecord>> GetRecoveryRecordByIdAsync(
        long id, CancellationToken ct = default);

    /// <summary>
    /// 查询追缴记录列表（支持按姓名/身份证搜索、按状态筛选）
    /// </summary>
    /// <param name="keyword">搜索关键词（姓名或身份证），空则查询全部</param>
    /// <param name="status">状态筛选，空则不筛选</param>
    /// <param name="limit">返回条数上限</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>追缴记录列表（按创建时间倒序）</returns>
    Task<Result<List<RecoveryRecord>>> GetRecoveryRecordsAsync(
        string? keyword = null, string? status = null, int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// 删除追缴记录（仅草稿状态可删，否则返回失败）
    /// </summary>
    /// <param name="id">追缴记录ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否成功</returns>
    Task<Result<bool>> DeleteRecoveryRecordAsync(
        long id, CancellationToken ct = default);

    /// <summary>
    /// 更新追缴记录（搜索录入，仅草稿状态可更新）
    /// </summary>
    /// <param name="id">追缴记录ID</param>
    /// <param name="dto">追缴记录DTO</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否成功</returns>
    Task<Result<bool>> UpdateRecoveryRecordAsync(
        long id, RecoveryRecordDto dto, CancellationToken ct = default);

    /// <summary>
    /// 更新追缴记录（手工录入，仅草稿状态可更新）
    /// </summary>
    /// <param name="id">追缴记录ID</param>
    /// <param name="dto">手工追缴记录DTO</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否成功</returns>
    Task<Result<bool>> UpdateManualRecoveryRecordAsync(
        long id, ManualRecoveryRecordDto dto, CancellationToken ct = default);

    /// <summary>
    /// 结案/出文书状态推进（Draft→Confirmed 快捷结案；Draft/Confirmed→Printed 归档写回）。
    /// 0 行命中视为幂等重入（已是目标状态）返回 true。
    /// </summary>
    /// <param name="id">追缴记录ID</param>
    /// <param name="status">新状态（Confirmed 结案 / Printed 出文书）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否成功</returns>
    Task<Result<bool>> UpdateRecoveryRecordStatusAsync(
        long id, string status, CancellationToken ct = default);

    /// <summary>
    /// 计算追缴月数
    /// </summary>
    /// <param name="startMonth">起始月 yyyy-MM</param>
    /// <param name="endMonth">终止月 yyyy-MM</param>
    /// <returns>追缴月数</returns>
    int CalculateRecoveryMonths(string startMonth, string endMonth);

    /// <summary>
    /// 计算应追缴金额
    /// </summary>
    /// <param name="monthlyAmount">月保障额</param>
    /// <param name="months">追缴月数</param>
    /// <returns>应追缴金额</returns>
    decimal CalculateRecoveryAmount(decimal monthlyAmount, int months);

    /// <summary>
    /// 查询追缴分段明细（自动计算依据，按段号升序）
    /// </summary>
    /// <param name="recoveryId">追缴记录ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>分段明细列表</returns>
    Task<Result<List<RecoverySegment>>> GetRecoverySegmentsAsync(
        long recoveryId, CancellationToken ct = default);

    /// <summary>
    /// 取分配追缴编号（格式 YYYYMM-NNN，按记录创建月）。
    /// 首次调用在事务内分配并落库（advisory 锁串行化同月并发）；此后返回库内编号——补打/多次打印同号。
    /// </summary>
    /// <param name="recoveryId">追缴记录ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>追缴编号，如 202609-001</returns>
    Task<Result<string>> GetOrAssignRecoveryCodeAsync(long recoveryId, CancellationToken ct = default);

    /// <summary>
    /// 获取停止人员统计信息
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>统计信息</returns>
    Task<Result<StoppedPersonStats>> GetStoppedPersonStatsAsync(CancellationToken ct = default);
}
