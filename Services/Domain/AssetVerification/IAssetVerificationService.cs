using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.AssetVerification;

/// <summary>
/// 资产核查服务接口
/// </summary>
public interface IAssetVerificationService
{
    // 已删除（2026-11 死代码清理，均经全仓 grep 取证零调用方）：
    // GetByIdAsync / GetByArchiveIdAsync / CreateAsync / UpdateResultAsync / CompleteAsync / GetPendingAsync /
    // SearchPagedAsync(两重载，year/month 版还吞参) / GetMonthlyReviewAsync / GetMonthlyDetailAsync /
    // GetAllApplicantNamesAsync / ExportPendingListToExcelAsync / ExportMonthlyListToExcelAsync /
    // GetHistoryStatsAsync / GetPendingTasksAsync / GetPendingTasksByDateRangeAsync / GetCompletedPagedAsync /
    // GetPendingVerificationPagedAsync / GetCompletedCountAsync / GetPendingCountAsync / DeleteByHeadIdCardAsync
    // （前组所属 nc_biz_asset_verifications 表在生产库不存在；DeleteByHeadIdCardAsync 的覆盖语义
    //   已并入 SubmitQuickVerificationAsync(overwriteExisting) 同事务原子执行）。

    /// <summary>
    /// 获取核查任务列表（分页）
    /// </summary>
    Task<Result<PagedResult<AssetVerificationTask>>> GetTasksPagedAsync(int pageIndex, int pageSize, string status = null, string keyword = null, DateTime? dateFrom = null, DateTime? dateTo = null, CancellationToken ct = default);

    /// <summary>
    /// 按 ID 获取核查任务（统一补打页按记录 ID 直打用）
    /// </summary>
    Task<Result<AssetVerificationTask?>> GetTaskByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// 按户主身份证号查询同户所有成员
    /// </summary>
    Task<Result<List<AssetVerificationTask>>> GetFamilyMembersAsync(string headIdCard, CancellationToken ct = default);

    /// <summary>
    /// 按申请人身份证查询最新核查记录 ID（档案输出解析 check_id 用；无记录返回 null）
    /// </summary>
    Task<Result<long?>> GetLatestIdByIdCardAsync(string idCard, CancellationToken ct = default);

    /// <summary>
    /// 按 batch_id 查询同批所有成员
    /// </summary>
    Task<Result<List<AssetVerificationTask>>> GetFamilyMembersByBatchIdAsync(string batchId, CancellationToken ct = default);

    /// <summary>
    /// 按身份证号查询档案（nc_biz_applications）
    /// </summary>
    Task<Dictionary<string, string>?> GetApplicationFieldsByIdCardAsync(string idCard, CancellationToken ct = default);

    /// <summary>
    /// 检查户主当月是否已有核查记
/// </summary>
    Task<Result<bool>> CheckDuplicateAsync(string headIdCard, int year, int month, CancellationToken ct = default);

    /// <summary>提交快速核查。overwriteExisting=true 时同事务先软删该户主当月旧批次（覆盖提交，原子）。</summary>
    Task<Result<long>> SubmitQuickVerificationAsync(QuickAssetCheckSubmitRequest request, bool overwriteExisting = false, CancellationToken ct = default);

    /// <summary>
    /// 按日期范围获取统计数
/// </summary>
    Task<Result<MonthlyVerificationStats>> GetStatsByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken ct = default);

    /// <summary>
    /// 按日期范围搜索核查任务（分页
/// </summary>
    Task<Result<PagedResult<AssetVerificationTask>>> SearchByDateRangePagedAsync(
        DateTime startDate, DateTime endDate,
        string? keyword, string? status,
        int pageIndex, int pageSize,
        CancellationToken ct = default,
        bool onlyHead = false);

    /// <summary>
    /// 按日期范围计数（工作流页徽章专用，onlyHead 口径与 SearchByDateRangePagedAsync 一致）。
    /// 原实现用 pageSize=1 的页查询拿 TotalCount——白跑一条 DISTINCT ON CTE 页查询（2026-11 收口）。
    /// </summary>
    Task<Result<long>> CountByDateRangeAsync(
        DateTime startDate, DateTime endDate,
        string? status,
        CancellationToken ct = default,
        bool onlyHead = false);

    /// <summary>
    /// 按户主身份证集合批查同户全部核查记录（含各状态成员行，用于"有报告未建档"导出时展开家庭成员）
    /// </summary>
    Task<Result<List<AssetVerificationTask>>> GetByHeadIdsAsync(
        IReadOnlyCollection<string> headIdCards, CancellationToken ct = default);

    /// <summary>
    /// 根据身份证号查询历史记录（用于快速核查自动填充）
    /// </summary>
    Task<AssetCheckHistoryResult?> GetHistoryByIdCardAsync(string idCard, CancellationToken ct = default);

    /// <summary>
    /// 按姓名或身份证号搜索核查记录
    /// </summary>
    Task<Result<List<AssetVerificationTask>>> SearchByNameOrIdCardAsync(string keyword, CancellationToken ct = default);

    /// <summary>
    /// 根据ID获取核查详情（用于预览）
    /// </summary>
    Task<Result<AssetVerificationDetail>> GetDetailByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// 更新核查状态（按户：同 head_id_card 的整户记录一并更新）
    /// '0'=已提交 '1'=有报告未建档 '2'=已建档 '3'=不予认定（终态互不覆盖）
    /// </summary>
    Task<Result> UpdateCheckStatusAsync(long checkId, string status, CancellationToken ct = default);
}

/// <summary>
/// 资产核查任务（用于列表展示）
/// </summary>
public class AssetVerificationTask
{
    public long Id { get; set; }
    public long ArchiveId { get; set; }
    public string ArchiveName { get; set; } = string.Empty;
    public string ArchiveIdCard { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public bool IsHead { get; set; }
    public string HeadIdCard { get; set; } = string.Empty;
    public string FamilyAddress { get; set; } = string.Empty;
    public string Community { get; set; } = string.Empty;
    public string ApplicantIdType { get; set; } = string.Empty;
    public string ApplicationReason { get; set; } = string.Empty;
    public DateTime ApplicationDate { get; set; }
    public string ContactPhone { get; set; } = string.Empty;
    public int VerificationYear { get; set; }
    public int VerificationMonth { get; set; }
    public string VerificationType { get; set; } = "Quick";
    public string Status { get; set; } = ApplicationStatusCodes.PENDING;
    public decimal TotalAssetValue { get; set; }
    public string VerificationResult { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime CompletedAt { get; set; }

    public string StatusDisplay => Status switch
    {
        AssetCheckStatusConstants.SUBMITTED => AssetCheckStatusConstants.GetDescription(Status),
        AssetCheckStatusConstants.VERIFIED => AssetCheckStatusConstants.GetDescription(Status),
        AssetCheckStatusConstants.INCLUDED => AssetCheckStatusConstants.GetDescription(Status),
        AssetCheckStatusConstants.REFUSED => AssetCheckStatusConstants.GetDescription(Status),
        ApplicationStatusCodes.PENDING => "已提交",
        ApplicationStatusCodes.COMPLETED => "有报告未建档",
        "Error" => "异常",
        _ => Status
    };

    /// <summary>是否可执行"不予认定"（仅"有报告未建档"状态）</summary>
    public bool CanReject => Status == "1";

    public string DisplayInfo => $"{ArchiveName ?? ""} ({ArchiveIdCard ?? ""}) {Relationship ?? ""}";
}

// 已删除（2026-11）：AssetVerification 实体与 AssetVerificationCreateRequest——
// 仅被 nc_biz_asset_verifications 遗留死方法组引用（生产库无该表），随死方法一并清理。
