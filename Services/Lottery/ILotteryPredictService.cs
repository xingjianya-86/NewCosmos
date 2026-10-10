using NewCosmos.Models.Lottery;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Lottery;

/// <summary>
/// 彩票预测服务接口
/// 负责调用预测算法生成号码
/// </summary>
public interface ILotteryPredictService
{
    /// <summary>机选号码（随机生成，供首页「快速机选」使用）</summary>
    Task<Result<List<PredictionResult>>> RandomPickAsync(LotteryType lotteryType, int count = 1, CancellationToken ct = default);

    /// <summary>
    /// 机器学习(LSTM)预测（内嵌原版 KittenCN/predict_Lottery_ticket，「基于tensorflow lstm模型的彩票预测」，
    /// 双色球/大乐透；窗口 = LotteryConstants.LSTM_WINDOW，预测期含负样本降权；
    /// 训练/预测数据源 = 开奖数据下载链路的 nc_lottery_draws）。
    /// </summary>
    Task<Result<List<PredictionResult>>> PredictByLstmAsync(LotteryType lotteryType, int count = 1, CancellationToken ct = default);

    /// <summary>LSTM 模型是否已训练（运行主目录 red+blue.keras）</summary>
    bool IsLstmModelTrained(LotteryType lotteryType);

    /// <summary>训练 LSTM 模型（数据源 = 开奖数据下载链路 nc_lottery_draws，窗口 LSTM_WINDOW）</summary>
    Task<Result<bool>> TrainLstmModelAsync(LotteryType lotteryType, CancellationToken ct = default);
}
