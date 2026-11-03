namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 特困供养人员取消（停止）情况表 · 退出直出服务：
/// 退出/停发成功后准备档案输出上下文（装配字段 + 写 PrintNavigationData），由 ViewModel 层导航到档案输出页。
/// </summary>
public interface IDestituteCancelFilingService
{
    /// <summary>
    /// 准备「特困取消备案表」直出上下文：取被停止的特困档案 → 算年保障金额 → 装配字段 → 写 PrintNavigationData。
    /// 成功返回 true（调用方随后导航到 ArchiveOutputPage）；取数失败返回 false（已记日志，不导航）。
    /// </summary>
    /// <param name="applicationId">被停止的特困档案 id</param>
    /// <param name="stopReasonKey">停发原因 key（见 <see cref="Constants.DestituteStopReason"/>）</param>
    Task<bool> PrepareNavigationAsync(long applicationId, string? stopReasonKey, CancellationToken ct = default);
}
