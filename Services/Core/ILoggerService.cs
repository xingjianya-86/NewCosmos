namespace NewCosmos.Services.Core;

/// <summary>
/// 日志服务接口
/// </summary>
public interface ILoggerService
{
    #region 基础日志方法

    void Info(string message);
    void Debug(string message);
    void Warn(string message);
    void Error(string message);
    void LogError(Exception ex, string operation);

    #endregion

    #region 业务日志 (BIZ)

    void LogBusiness(string operation, params (string Key, object Value)[] context);
    void LogArchiveCreated(long archiveId, long familyId, string classification, int userId);
    void LogClassificationResult(long familyId, decimal annualIncome, int familySize, decimal perCapita, string classification, decimal standard);
    void LogGracePeriodSet(long archiveId, int months, DateTime startDate, DateTime endDate, string originalClassification, decimal originalAmount);

    // 新增档案流程
    void LogArchiveStart(long familyId, string applicantName, int userId);
    void LogArchiveFailed(long familyId, string errorCode, string reason, int userId);

    // 经济复核流程
    void LogEconomicReviewStart(long archiveId, string currentClassification, int userId);
    void LogEconomicReviewResult(long archiveId, string oldClassification, string newClassification, decimal oldPerCapita, decimal newPerCapita);
    void LogClassificationChanged(long archiveId, string oldClassification, string newClassification, int userId);
    void LogClassificationUnchanged(long archiveId, string classification, int userId);
    void LogEconomicReviewFailed(long archiveId, string errorCode, string reason, int userId);

    // 家庭成员死亡流程
    void LogMemberDeath(long archiveId, long memberId, string memberName, int userId);
    void LogFamilyRecalculated(long archiveId, int oldFamilySize, int newFamilySize, decimal oldPerCapita, decimal newPerCapita);
    void LogClassificationRejudged(long archiveId, string oldClassification, string newClassification);
    void LogArchiveStopped(long archiveId, string stopType, int userId);
    void LogMemberDeathFailed(long archiveId, long memberId, string errorCode, string reason);

    // 新增家庭成员流程
    void LogMemberAdded(long archiveId, long memberId, string memberName, string relation, int userId);
    void LogMemberAddFailed(long archiveId, string errorCode, string reason);

    // 停止档案流程
    void LogArchiveStopFailed(long archiveId, string errorCode, string reason, int userId);

    #endregion

    #region 安全日志 (SEC)

    void LogSecurity(string operation, params (string Key, object Value)[] context);
    void LogLoginSuccess(int userId, string username, string ip, string device);
    void LogLoginFailed(string username, string ip, string reason, int attempt);
    void LogUserLocked(int userId, string ip, int lockDuration, string reason);
    void LogPasswordChanged(int userId, string ip);
    void LogRoleChanged(int targetUserId, string oldRole, string newRole, int operatorId);
    void LogSensitiveOperation(string operation, int userId, string ip, string details);

    #endregion

    #region 性能日志 (PERF)

    string StartPerfTimer(string operation);
    void StopPerfTimer(string timerId, string operation);
    void LogSlowQuery(string sql, long duration, int rowCount);

    #endregion

    #region 导航日志 (NAV)

    /// <summary>
    /// 记录导航开始
    /// </summary>
    void LogNavigationStart(string from, string to);

    /// <summary>
    /// 记录导航成功
    /// </summary>
    void LogNavigationSuccess(string from, string to);

    /// <summary>
    /// 记录导航失败
    /// </summary>
    void LogNavigationFailed(string from, string to, string error);

    /// <summary>
    /// 记录 DI 解析
    /// </summary>
    void LogDiResolution(string serviceName, bool success, string error = null);

    /// <summary>
    /// 记录页面加载
    /// </summary>
    void LogPageLoad(string pageName, bool success, string error = null);

    #endregion
}
