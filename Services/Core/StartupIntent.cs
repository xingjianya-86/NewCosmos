namespace NewCosmos.Services.Core;

/// <summary>
/// 本次启动的分流去向（启动动画片头底板就位后由动画侧经桥询问，宿主按会话恢复结果答复）。
/// 目的：五步验证序列 + 终幕在任何启动路径下只演一遍——
/// 会话有效接着自动演完；需要登录则片头收尾交接序列登录，在授权/账号密码步骤停下等待输入。
/// </summary>
public enum StartupIntent
{
    /// <summary>尚未定论（会话恢复仍在进行）：动画在底板上等待答复</summary>
    Pending = 0,

    /// <summary>会话有效：动画继续自动演完五步序列与终幕 → 进主页</summary>
    Full = 1,

    /// <summary>需要登录 / 进连接重试页：片头就此收尾放行，交由序列登录（或重试页）接手</summary>
    Skip = 2
}
