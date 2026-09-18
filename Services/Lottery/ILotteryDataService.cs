using NewCosmos.Models.Lottery;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Lottery;

/// <summary>
/// 彩票数据服务接口
/// 负责开奖数据的获取、存储和查询
/// </summary>
public interface ILotteryDataService
{
    /// <summary>
    /// 增量同步开奖数据：抓取开奖日期 >= sinceDate 的记录并 upsert。
    /// sinceDate 为空表示首次全量抓取。
    /// </summary>
    /// <param name="lotteryType">彩种类型</param>
    /// <param name="sinceDate">增量起始开奖日期（含当天）；null=全量</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>本次真实新增的记录数量（已存在仅更新的不计入）</returns>
    Task<Result<int>> SyncHistoryDataAsync(LotteryType lotteryType, DateTime? sinceDate = null, CancellationToken ct = default);

    /// <summary>
    /// 获取指定彩种的开奖记录列表
    /// </summary>
    /// <param name="lotteryType">彩种类型</param>
    /// <param name="pageNo">页码（从1开始）</param>
    /// <param name="pageSize">每页数量</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>开奖记录列表</returns>
    Task<Result<List<LotteryDraw>>> GetDrawListAsync(LotteryType lotteryType, int pageNo = 1, int pageSize = 30, CancellationToken ct = default);

    /// <summary>
    /// 获取指定彩种的开奖记录总数
    /// </summary>
    /// <param name="lotteryType">彩种类型</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>记录总数</returns>
    Task<Result<int>> GetDrawCountAsync(LotteryType lotteryType, CancellationToken ct = default);

    /// <summary>
    /// 获取指定期号的开奖记录
    /// </summary>
    /// <param name="lotteryType">彩种类型</param>
    /// <param name="drawNumber">期号</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>开奖记录（不存在则返回null）</returns>
    Task<Result<LotteryDraw?>> GetDrawByNumberAsync(LotteryType lotteryType, string drawNumber, CancellationToken ct = default);

    /// <summary>
    /// 获取最新的开奖记录
    /// </summary>
    /// <param name="lotteryType">彩种类型</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>最新开奖记录</returns>
    Task<Result<LotteryDraw?>> GetLatestDrawAsync(LotteryType lotteryType, CancellationToken ct = default);

    /// <summary>
    /// 获取最近N期的开奖记录
    /// </summary>
    /// <param name="lotteryType">彩种类型</param>
    /// <param name="count">记录数量</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>开奖记录列表</returns>
    Task<Result<List<LotteryDraw>>> GetRecentDrawsAsync(LotteryType lotteryType, int count = 30, CancellationToken ct = default);

    /// <summary>
    /// 获取号码统计数据
    /// </summary>
    /// <param name="lotteryType">彩种类型</param>
    /// <param name="periodCount">统计期数范围（如：近100期）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>号码统计列表</returns>
    Task<Result<List<NumberStatistics>>> GetNumberStatisticsAsync(LotteryType lotteryType, int periodCount = 100, CancellationToken ct = default);

    /// <summary>
    /// 获取冷号列表（近期出现频率低的号码）
    /// </summary>
    /// <param name="lotteryType">彩种类型</param>
    /// <param name="count">返回数量</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>冷号列表</returns>
    Task<Result<List<NumberStatistics>>> GetColdNumbersAsync(LotteryType lotteryType, int count = 10, CancellationToken ct = default);

    /// <summary>
    /// 获取热号列表（近期出现频率高的号码）
    /// </summary>
    /// <param name="lotteryType">彩种类型</param>
    /// <param name="count">返回数量</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>热号列表</returns>
    Task<Result<List<NumberStatistics>>> GetHotNumbersAsync(LotteryType lotteryType, int count = 10, CancellationToken ct = default);
}
