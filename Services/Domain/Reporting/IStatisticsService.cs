using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.Reporting;

public interface IStatisticsService
{
    Task<Result<int>> GetPendingApprovalsCountAsync(CancellationToken ct = default);
    Task<Result<int>> GetMonthlyArchivesCountAsync(int year, int month, CancellationToken ct = default);
    Task<Result<int>> GetAssetVerificationsCountAsync(CancellationToken ct = default);
    Task<Result<int>> GetActiveUsersCountAsync(CancellationToken ct = default);
    Task<Result<DashboardStatistics>> GetDashboardStatisticsAsync(CancellationToken ct = default);

    /// <summary>低收入人口救助帮扶模块统计（在享/本月新增/本月退出）</summary>
    Task<Result<SocialAssistanceModuleStats>> GetSocialAssistanceStatsAsync(CancellationToken ct = default);

    /// <summary>高龄津贴发放模块统计（在享领取人数/本月新增登记）</summary>
    Task<Result<ElderlyModuleStats>> GetElderlyStatsAsync(CancellationToken ct = default);

    /// <summary>家庭经济状况核对模块统计（待传报告/本月完成/本月新增/全年累计）</summary>
    Task<Result<AssetVerificationModuleStats>> GetAssetVerificationStatsAsync(CancellationToken ct = default);

    /// <summary>高龄津贴停发统计（本月/年累计停发人次）</summary>
    Task<Result<ElderlyStopStats>> GetElderlyStopStatsAsync(CancellationToken ct = default);

    /// <summary>临时救助统计（草稿/已确认累计/年确认人次/年确认金额）</summary>
    Task<Result<TempReliefStats>> GetTempReliefStatsAsync(CancellationToken ct = default);

    /// <summary>保障对象变更统计（本月/年累计变更笔数）</summary>
    Task<Result<ChangeStats>> GetChangeStatsAsync(CancellationToken ct = default);

    /// <summary>救助档案统计（归档总数）</summary>
    Task<Result<ArchiveStats>> GetArchiveStatsAsync(CancellationToken ct = default);
}

/// <summary>
/// 首页数据概览统计（依托 B 线业务时间轴）
/// </summary>
public class DashboardStatistics
{
    /// <summary>本月总新增：status='Approved' 且 first_approved_at∈B线周期（月报"新增救助明细"口径）</summary>
    public int MonthlyNewAdditions { get; set; }

    /// <summary>本月总退出：status='Stopped' 且 stop_date∈B线周期（月报"停保汇总"口径）</summary>
    public int MonthlyExits { get; set; }

    /// <summary>新申请资产核查：status='0'（已申请未出授权报告）且 application_date∈B线周期</summary>
    public int NewAssetChecks { get; set; }

    /// <summary>本月新增高龄老人：status∈(Confirmed,Stopped) 且 apply_date∈当月自然月（普惠高龄月报"新增明细"口径）</summary>
    public int MonthlyNewElderly { get; set; }
}

/// <summary>
/// 低收入人口救助帮扶模块统计（口径与 GetDashboardStatisticsAsync 保持一致，依托 B 线业务时间轴）
/// </summary>
public class SocialAssistanceModuleStats
{
    /// <summary>在享保障对象数（status='Approved'）</summary>
    public int ActiveCount { get; set; }

    /// <summary>本月总新增（status='Approved' 且 first_approved_at∈B线周期）</summary>
    public int MonthlyNewAdditions { get; set; }

    /// <summary>本月总退出（status='Stopped' 且 stop_date∈B线周期）</summary>
    public int MonthlyExits { get; set; }
}

/// <summary>
/// 高龄津贴发放模块统计
/// </summary>
public class ElderlyModuleStats
{
    /// <summary>在享领取人数（当前库 Confirmed 且未死亡 + 导入库尚未建档且未登记死亡）</summary>
    public int ActiveCount { get; set; }

    /// <summary>本月新增登记数（status∈(Confirmed,Stopped) 且 apply_date∈当月自然月）</summary>
    public int MonthlyNew { get; set; }
}

/// <summary>
/// 家庭经济状况核对模块统计
/// </summary>
public class AssetVerificationModuleStats
{
    /// <summary>待上传报告数（status='0'，已申请未出授权报告，不限时间）</summary>
    public int PendingReportCount { get; set; }

    /// <summary>本月已完成核对数（status='1' 且 updated_at∈当月自然月）</summary>
    public int MonthlyCompleted { get; set; }

    /// <summary>本月新增申请数（application_date∈当月自然月）</summary>
    public int MonthlyNew { get; set; }

    /// <summary>全年累计申请数（application_date∈当年）</summary>
    public int YearTotal { get; set; }
}

/// <summary>
/// 高龄津贴停发统计
/// </summary>
public class ElderlyStopStats
{
    /// <summary>本月停发人次（status='Stopped' 且 stopped_at∈当月自然月）</summary>
    public int MonthlyStopped { get; set; }

    /// <summary>年累计停发人次（stopped_at∈当年）</summary>
    public int YearStopped { get; set; }
}

/// <summary>
/// 临时救助统计
/// </summary>
public class TempReliefStats
{
    /// <summary>草稿数（status='Draft'）</summary>
    public int DraftCount { get; set; }

    /// <summary>已确认累计数（全部状态为已确认的申请）</summary>
    public int ConfirmedTotal { get; set; }

    /// <summary>本年确认人次（confirmed_at∈当年且 status='Confirmed'）</summary>
    public int YearConfirmed { get; set; }

    /// <summary>本年确认金额合计（元）</summary>
    public decimal YearConfirmedAmount { get; set; }
}

/// <summary>
/// 保障对象变更统计
/// </summary>
public class ChangeStats
{
    /// <summary>本月变更笔数（change_date∈当月自然月）</summary>
    public int MonthlyChanges { get; set; }

    /// <summary>年累计变更笔数（change_date∈当年）</summary>
    public int YearChanges { get; set; }
}

/// <summary>
/// 救助档案统计
/// </summary>
public class ArchiveStats
{
    /// <summary>归档总数（nc_biz_archives，未删除）</summary>
    public int TotalArchives { get; set; }
}