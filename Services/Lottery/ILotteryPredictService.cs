using NewCosmos.Constants;
using NewCosmos.Models.Lottery;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Lottery;

/// <summary>
/// 彩票预测服务接口
/// 负责调用预测算法生成号码
/// </summary>
public interface ILotteryPredictService
{
    /// <summary>
    /// 机选号码（随机生成，不受契合度门槛约束）
    /// </summary>
    Task<Result<List<PredictionResult>>> RandomPickAsync(LotteryType lotteryType, int count = 1, CancellationToken ct = default);

    /// <summary>
    /// 融合算法预测（原版机器学习 LSTM 0.6 + LotteryML 0.4，评分级融合）
    /// 内部生成 100 注候选，按位次投票选出 count 注；仅返回最终注。
    /// </summary>
    Task<Result<List<PredictionResult>>> PredictByFusionAsync(LotteryType lotteryType, int count = 1, CancellationToken ct = default);

    /// <summary>融合算法所需两套模型是否都已训练（LSTM .keras + LotteryML .joblib）</summary>
    bool IsFusionModelTrained(LotteryType lotteryType);

    /// <summary>训练融合算法所需的两套模型（LSTM + LotteryML）</summary>
    Task<Result<bool>> TrainFusionModelAsync(LotteryType lotteryType, CancellationToken ct = default);

    /// <summary>
    /// 保存预测记录到数据库
    /// </summary>
    Task<Result<int>> SavePredictionsAsync(List<PredictionResult> predictions, CancellationToken ct = default);

    /// <summary>
    /// 获取历史预测记录
    /// </summary>
    Task<Result<List<PredictionResult>>> GetPredictionHistoryAsync(LotteryType lotteryType, int pageNo = 1, int pageSize = 30, CancellationToken ct = default);

    /// <summary>
    /// 验证预测结果（开奖后对比）
    /// </summary>
    Task<Result<int>> VerifyPredictionsAsync(LotteryType lotteryType, string drawNumber, CancellationToken ct = default);
}
