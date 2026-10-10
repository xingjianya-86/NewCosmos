namespace NewCosmos.Constants;

/// <summary>
/// 彩票模块常量（LSTM 算法参数、进程超时）
/// </summary>
public static class LotteryConstants
{
    /// <summary>唯一预测算法展示名（购彩记录、导出文案统一用它）</summary>
    public const string LSTM_ALGORITHM_NAME = "机器学习(LSTM)";

    /// <summary>Python 预测进程超时（秒）</summary>
    public const int PREDICT_TIMEOUT_SECONDS = 300;

    /// <summary>Python 训练进程超时（秒）。窗口 73 的全量历史训练为分钟～十分钟级，放宽到 2 小时</summary>
    public const int TRAIN_TIMEOUT_SECONDS = 7200;

    /// <summary>
    /// LSTM 回看窗口（训练/预测同窗；样本数 = 历史期数 − 窗口）。
    /// 73 = 全库 2073 期下训练样本恰 2000（SSQ），DLT 全量入训样本更多；改参后需重训。
    /// 与 Scripts\Lottery\lstm_entry.py 的 DEFAULT_WINDOW 同步。
    /// </summary>
    public const int LSTM_WINDOW = 73;

    /// <summary>内嵌原版工程目录名（KittenCN/predict_Lottery_ticket，commit 6cf60bb7，GPL-3.0）</summary>
    public const string LSTM_PROJECT_DIR = "predict_Lottery_ticket";
}
