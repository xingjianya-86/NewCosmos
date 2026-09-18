using System.IO;
using System.Collections.Concurrent;
using NewCosmos.Models.Options;
using Serilog;
using Serilog.Events;
using NewCosmos.Services.Core;

namespace NewCosmos.Services.Core;

/// <summary>
/// Serilog 结构化日志服务
/// 支持 APP/BIZ/SEC/ERR/PERF 五类日志
/// 自动脱敏敏感数据
/// </summary>
public class LoggerService : ILoggerService, IDisposable
{
    /// <summary>分类事件的属性名：BIZ/SEC/PERF 三类通过它路由到各自的日志文件</summary>
    private const string CategoryProperty = "Category";

    /// <summary>单个日志文件上限 64MB，超限滚动——防止异常循环一天写出数 GB 单文件</summary>
    private const long FileSizeLimit = 64L * 1024 * 1024;

    private readonly ILogger _logger;
    private readonly ILogger _bizLogger;
    private readonly ILogger _secLogger;
    private readonly ILogger _perfLogger;
    private readonly ConcurrentDictionary<string, DateTime> _perfTimers = new();
    private readonly PerformanceOptions _perfOptions;
    private bool _disposed;

    public LoggerService(StorageOptions storageOptions, AppOptions appOptions, PerformanceOptions perfOptions)
    {
        var logPath = storageOptions.GetLogPath();
        _perfOptions = perfOptions;

        // 确保日志目录存在
        Directory.CreateDirectory(logPath);

        // 设计要点（性能）：
        // 1) 全部文件 sink 经 WriteTo.Async 包装——旧实现是调用线程同步写盘，
        //    数据库层/ViewModel 的每条日志都在 UI 线程上做文件 I/O；
        // 2) 按 Category 属性分流——旧实现只按级别区分，一条 Info 会同时写入
        //    app_/biz_/fallback 三个文件（Warning 写 5 个，Error 写 6 个）；
        // 3) 最低级别 Information——Debug 级诊断需要时把下面一行改回 Debug；
        // 4) 移除 fallback sink（与 app_ 完全重复的第 3 份拷贝）。
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", appOptions.ApplicationName)
            // app_: 通用日志（不含已分类事件，避免重复落盘）
            .WriteTo.Logger(lc => lc
                .Filter.ByExcluding(e => e.Properties.ContainsKey(CategoryProperty))
                .WriteTo.Async(a => a.File(
                    path: Path.Combine(logPath, "app_.log"),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    fileSizeLimitBytes: FileSizeLimit,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 30)))
            // biz_: 仅业务事件（任何级别）
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => HasCategory(e, "BIZ"))
                .WriteTo.Async(a => a.File(
                    path: Path.Combine(logPath, "biz_.log"),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [BIZ] {Message:lj}{NewLine}",
                    fileSizeLimitBytes: FileSizeLimit,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 180)))
            // sec_: 仅安全事件（任何级别——旧实现要求 Warning 起步，导致密码修改等 Info 级安全事件从未落入 sec_ 日志）
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => HasCategory(e, "SEC"))
                .WriteTo.Async(a => a.File(
                    path: Path.Combine(logPath, "sec_.log"),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [SEC] {Message:lj}{NewLine}",
                    fileSizeLimitBytes: FileSizeLimit,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 365)))
            // err_: 全部错误（无论类别，错误值得冗余一份）
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => e.Level >= LogEventLevel.Error)
                .WriteTo.Async(a => a.File(
                    path: Path.Combine(logPath, "err_.log"),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [ERR] {Message:lj}{NewLine}{Exception}",
                    fileSizeLimitBytes: FileSizeLimit,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 90)))
            // perf_: 仅性能事件
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => HasCategory(e, "PERF"))
                .WriteTo.Async(a => a.File(
                    path: Path.Combine(logPath, "perf_.log"),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [PERF] {Message:lj}{NewLine}",
                    fileSizeLimitBytes: FileSizeLimit,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 30)))
            .CreateLogger();

        _logger = Log.Logger;
        _bizLogger = _logger.ForContext(CategoryProperty, "BIZ");
        _secLogger = _logger.ForContext(CategoryProperty, "SEC");
        _perfLogger = _logger.ForContext(CategoryProperty, "PERF");
    }

    private static bool HasCategory(LogEvent e, string category)
        => e.Properties.TryGetValue(CategoryProperty, out var v)
           && v is ScalarValue { Value: string s }
           && s == category;

    #region 基础日志方法

    public void Info(string message) => _logger.Information(message);
    public void Debug(string message) => _logger.Debug(message);
    public void Warn(string message) => _logger.Warning(message);
    public void Error(string message) => _logger.Error(message);
    public void LogError(Exception ex, string operation) => _logger.Error(ex, "操作失败: {Operation}", operation);

    #endregion

    #region 业务日志 (BIZ)

    public void LogBusiness(string operation, params (string Key, object Value)[] context)
    {
        var sanitizedContext = context.Select(c => (c.Key, DataMasker.Sanitize(c.Value)));
        _bizLogger.Information("业务操作: {Operation} | {@Context}", operation, sanitizedContext.ToDictionary(x => x.Key, x => x.Item2));
    }

    public void LogArchiveCreated(long archiveId, long familyId, string classification, int userId)
    {
        _bizLogger.Information("新增档案完成 | ArchiveId={ArchiveId} FamilyId={FamilyId} Classification={Classification} UserId={UserId}", archiveId, familyId, classification, userId);
    }

    public void LogClassificationResult(long familyId, decimal annualIncome, int familySize, decimal perCapita, string classification, decimal standard)
    {
        _bizLogger.Information("分类判定完成 | FamilyId={FamilyId} AnnualIncome={AnnualIncome} FamilySize={FamilySize} PerCapita={PerCapita} Classification={Classification} Standard={Standard}", familyId, annualIncome, familySize, perCapita, classification, standard);
    }

    public void LogGracePeriodSet(long archiveId, int months, DateTime startDate, DateTime endDate, string originalClassification, decimal originalAmount)
    {
        _bizLogger.Information("渐退期已设置 | ArchiveId={ArchiveId} Months={Months} StartDate={StartDate} EndDate={EndDate} OriginalClassification={OriginalClassification} OriginalAmount={OriginalAmount}", archiveId, months, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"), originalClassification, originalAmount);
    }

    public void LogArchiveStart(long familyId, string applicantName, int userId)
    {
        var maskedName = DataMasker.MaskName(applicantName);
        _bizLogger.Information("开始创建档案 | FamilyId={FamilyId} ApplicantName={ApplicantName} UserId={UserId}", familyId, maskedName, userId);
    }

    public void LogArchiveFailed(long familyId, string errorCode, string reason, int userId)
    {
        _bizLogger.Error("新增档案失败 | FamilyId={FamilyId} ErrorCode={ErrorCode} Reason={Reason} UserId={UserId}", familyId, errorCode, reason, userId);
    }

    public void LogEconomicReviewStart(long archiveId, string currentClassification, int userId)
    {
        _bizLogger.Information("开始经济复核 | ArchiveId={ArchiveId} CurrentClassification={CurrentClassification} UserId={UserId}", archiveId, currentClassification, userId);
    }

    public void LogEconomicReviewResult(long archiveId, string oldClassification, string newClassification, decimal oldPerCapita, decimal newPerCapita)
    {
        _bizLogger.Information("经济复核判定完成 | ArchiveId={ArchiveId} OldClassification={OldClassification} NewClassification={NewClassification} OldPerCapita={OldPerCapita} NewPerCapita={NewPerCapita}", archiveId, oldClassification, newClassification, oldPerCapita, newPerCapita);
    }

    public void LogClassificationChanged(long archiveId, string oldClassification, string newClassification, int userId)
    {
        _bizLogger.Information("档案分类变更 | ArchiveId={ArchiveId} OldClassification={OldClassification} NewClassification={NewClassification} UserId={UserId}", archiveId, oldClassification, newClassification, userId);
    }

    public void LogClassificationUnchanged(long archiveId, string classification, int userId)
    {
        _bizLogger.Information("经济复核完成（分类未变）| ArchiveId={ArchiveId} Classification={Classification} UserId={UserId}", archiveId, classification, userId);
    }

    public void LogEconomicReviewFailed(long archiveId, string errorCode, string reason, int userId)
    {
        _bizLogger.Error("经济复核失败 | ArchiveId={ArchiveId} ErrorCode={ErrorCode} Reason={Reason} UserId={UserId}", archiveId, errorCode, reason, userId);
    }

    public void LogMemberDeath(long archiveId, long memberId, string memberName, int userId)
    {
        var maskedName = DataMasker.MaskName(memberName);
        _bizLogger.Information("家庭成员死亡登记 | ArchiveId={ArchiveId} MemberId={MemberId} MemberName={MemberName} UserId={UserId}", archiveId, memberId, maskedName, userId);
    }

    public void LogFamilyRecalculated(long archiveId, int oldFamilySize, int newFamilySize, decimal oldPerCapita, decimal newPerCapita)
    {
        _bizLogger.Information("成员死亡重算完成 | ArchiveId={ArchiveId} OldFamilySize={OldFamilySize} NewFamilySize={NewFamilySize} OldPerCapita={OldPerCapita} NewPerCapita={NewPerCapita}", archiveId, oldFamilySize, newFamilySize, oldPerCapita, newPerCapita);
    }

    public void LogClassificationRejudged(long archiveId, string oldClassification, string newClassification)
    {
        _bizLogger.Information("分类重判完成 | ArchiveId={ArchiveId} OldClassification={OldClassification} NewClassification={NewClassification}", archiveId, oldClassification, newClassification);
    }

    public void LogArchiveStopped(long archiveId, string stopType, int userId)
    {
        _bizLogger.Information("档案停止 | ArchiveId={ArchiveId} StopType={StopType} UserId={UserId}", archiveId, stopType, userId);
    }

    public void LogMemberDeathFailed(long archiveId, long memberId, string errorCode, string reason)
    {
        _bizLogger.Error("成员死亡处理失败 | ArchiveId={ArchiveId} MemberId={MemberId} ErrorCode={ErrorCode} Reason={Reason}", archiveId, memberId, errorCode, reason);
    }

    public void LogMemberAdded(long archiveId, long memberId, string memberName, string relation, int userId)
    {
        var maskedName = DataMasker.MaskName(memberName);
        _bizLogger.Information("新增家庭成员 | ArchiveId={ArchiveId} MemberId={MemberId} MemberName={MemberName} Relation={Relation} UserId={UserId}", archiveId, memberId, maskedName, relation, userId);
    }

    public void LogMemberAddFailed(long archiveId, string errorCode, string reason)
    {
        _bizLogger.Error("新增成员处理失败 | ArchiveId={ArchiveId} ErrorCode={ErrorCode} Reason={Reason}", archiveId, errorCode, reason);
    }

    public void LogArchiveStopFailed(long archiveId, string errorCode, string reason, int userId)
    {
        _bizLogger.Error("档案停止失败 | ArchiveId={ArchiveId} ErrorCode={ErrorCode} Reason={Reason} UserId={UserId}", archiveId, errorCode, reason, userId);
    }

    #endregion

    #region 安全日志 (SEC)

    public void LogSecurity(string operation, params (string Key, object Value)[] context)
    {
        var sanitizedContext = context.Select(c => (c.Key, Value: DataMasker.Sanitize(c.Value))).ToDictionary(x => x.Key, x => x.Value);
        _secLogger.Warning("安全事件: {Operation} | {@Context}", operation, sanitizedContext);
    }

    public void LogLoginSuccess(int userId, string username, string ip, string device)
    {
        var maskedUsername = DataMasker.MaskName(username);
        _secLogger.Warning("用户登录成功 | UserId={UserId} Username={Username} IP={IP} Device={Device}", userId, maskedUsername, ip, device);
    }

    public void LogLoginFailed(string username, string ip, string reason, int attempt)
    {
        var maskedUsername = DataMasker.MaskName(username);
        _secLogger.Warning("用户登录失败 | Username={Username} IP={IP} Reason={Reason} Attempt={Attempt}", maskedUsername, ip, reason, attempt);
    }

    public void LogUserLocked(int userId, string ip, int lockDuration, string reason)
    {
        _secLogger.Warning("用户账户锁定 | UserId={UserId} IP={IP} LockDuration={LockDuration} Reason={Reason}", userId, ip, lockDuration, reason);
    }

    public void LogPasswordChanged(int userId, string ip)
    {
        _secLogger.Information("密码已修改 | UserId={UserId} IP={IP}", userId, ip);
    }

    public void LogRoleChanged(int targetUserId, string oldRole, string newRole, int operatorId)
    {
        _secLogger.Information("用户角色变更 | TargetUserId={TargetUserId} OldRole={OldRole} NewRole={NewRole} OperatorId={OperatorId}", targetUserId, oldRole, newRole, operatorId);
    }

    public void LogSensitiveOperation(string operation, int userId, string ip, string details)
    {
        _secLogger.Information("敏感操作记录 | Operation={Operation} UserId={UserId} IP={IP} Details={Details}", operation, userId, ip, details);
    }

    #endregion

    #region 性能日志 (PERF)

    public string StartPerfTimer(string operation)
    {
        var timerId = $"{operation}_{Guid.NewGuid():N}";
        _perfTimers[timerId] = DateTime.Now;
        return timerId;
    }

    public void StopPerfTimer(string timerId, string operation)
    {
        if (_perfTimers.TryRemove(timerId, out var startTime))
        {
            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
            if (elapsed > _perfOptions.SlowOperationThresholdMs)
            {
                _perfLogger.Warning("慢操作告警| Operation={Operation} Duration={Duration}ms", operation, elapsed.ToString("F2"));
            }
        }
    }

    public void LogSlowQuery(string sql, long duration, int rowCount)
    {
        var truncatedSql = sql.Length > 100 ? sql[..100] + "..." : sql;
        _perfLogger.Warning("慢查询告| SQL={SQL} Duration={Duration}ms RowCount={RowCount}", truncatedSql, duration, rowCount);
    }

    #endregion

    #region 导航日志 (NAV)

    public void LogNavigationStart(string from, string to)
    {
        _logger.Information("[NAV] 导航开始: {From} -> {To}", from, to);
    }

    public void LogNavigationSuccess(string from, string to)
    {
        _logger.Information("[NAV] 导航成功: {From} -> {To}", from, to);
    }

    public void LogNavigationFailed(string from, string to, string error)
    {
        _logger.Error("[NAV] 导航失败: {From} -> {To}, 错误: {Error}", from, to, error);
    }

    public void LogDiResolution(string serviceName, bool success, string error = null)
    {
        if (success)
            _logger.Debug("[DI] 解析成功: {ServiceName}", serviceName);
        else
            _logger.Error("[DI] 解析失败: {ServiceName}, 错误: {Error}", serviceName, error);
    }

    public void LogPageLoad(string pageName, bool success, string error = null)
    {
        if (success)
            _logger.Information("[PAGE] 加载成功: {PageName}", pageName);
        else
            _logger.Error("[PAGE] 加载失败: {PageName}, 错误: {Error}", pageName, error);
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            Log.CloseAndFlush();
            _disposed = true;
        }
    }
}
