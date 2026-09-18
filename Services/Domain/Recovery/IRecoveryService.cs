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
    /// 更新追缴记录状态
    /// </summary>
    /// <param name="id">追缴记录ID</param>
    /// <param name="status">新状态</param>
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
    /// 生成追缴编码（格式 YYYYMM-NNN，按当月已有记录数流水递增）
    /// </summary>
    /// <param name="month">所属月份（取年月部分）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>追缴编码，如 202609-001</returns>
    Task<Result<string>> GenerateRecoveryCodeAsync(DateTime month, CancellationToken ct = default);

    /// <summary>
    /// 获取停止人员统计信息
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>统计信息</returns>
    Task<Result<StoppedPersonStats>> GetStoppedPersonStatsAsync(CancellationToken ct = default);
}
