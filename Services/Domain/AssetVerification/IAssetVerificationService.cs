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
    /// <summary>
    /// 根据档案ID获取资产核查记录
    /// </summary>
    Task<Result<AssetVerification>> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// 根据档案ID获取资产核查列表
    /// </summary>
    Task<Result<List<AssetVerification>>> GetByArchiveIdAsync(long archiveId, CancellationToken ct = default);

    /// <summary>
    /// 创建资产核查记录
    /// </summary>
    Task<Result<long>> CreateAsync(AssetVerificationCreateRequest request, CancellationToken ct = default);

    /// <summary>
    /// 更新资产核查结果
    /// </summary>
    Task<Result> UpdateResultAsync(long id, string verificationResult, decimal assetValue, string remarks, string updatedBy, CancellationToken ct = default);

    /// <summary>
    /// 完成核查
    /// </summary>
    Task<Result> CompleteAsync(long id, string completedBy, CancellationToken ct = default);

    /// <summary>
    /// 获取待核查列    /// </summary>
    Task<Result<PagedResult<AssetVerification>>> GetPendingAsync(int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 搜索核查任务（分页）
    /// </summary>
    Task<Result<PagedResult<AssetVerificationTask>>> SearchPagedAsync(string keyword, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 搜索核查任务（分页，带筛选）
    /// </summary>
    Task<Result<PagedResult<AssetVerificationTask>>> SearchPagedAsync(int year, int month, string keyword, string status, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 获取月度审核列表
    /// </summary>
    Task<Result<List<AssetVerification>>> GetMonthlyReviewAsync(int year, int month, CancellationToken ct = default);

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
    /// 按 batch_id 查询同批所有成员
    /// </summary>
    Task<Result<List<AssetVerificationTask>>> GetFamilyMembersByBatchIdAsync(string batchId, CancellationToken ct = default);

    /// <summary>
    /// 按身份证号查询档案（nc_biz_applications）
    /// </summary>
    Task<Dictionary<string, string>?> GetApplicationFieldsByIdCardAsync(string idCard, CancellationToken ct = default);

    /// <summary>
    /// 检查户主当月是否已有核查记    /// </summary>
    Task<Result<bool>> CheckDuplicateAsync(string headIdCard, int year, int month, CancellationToken ct = default);

    /// <summary>
    /// 按户主身份证号删除当月核查记录（覆盖提交用）
    /// </summary>
    Task<Result> DeleteByHeadIdCardAsync(string headIdCard, int year, int month, CancellationToken ct = default);

    /// <summary>
    /// 提交快速核查申请（批量创建核查记录    /// </summary>
    Task<Result<long>> SubmitQuickVerificationAsync(QuickAssetCheckSubmitRequest request, CancellationToken ct = default);

    /// <summary>
    /// 按日期范围获取统计数    /// </summary>
    Task<Result<MonthlyVerificationStats>> GetStatsByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken ct = default);

    /// <summary>
    /// 按日期范围搜索核查任务（分页    /// </summary>
    Task<Result<PagedResult<AssetVerificationTask>>> SearchByDateRangePagedAsync(
        DateTime startDate, DateTime endDate,
        string? keyword, string? status,
        int pageIndex, int pageSize,
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
    /// 获取月度明细列表（用于打印）
    /// </summary>
    Task<Result<List<AssetVerificationTask>>> GetMonthlyDetailAsync(int year, int month, CancellationToken ct = default);

    /// <summary>
    /// 按姓名或身份证号搜索核查记录
    /// </summary>
    Task<Result<List<AssetVerificationTask>>> SearchByNameOrIdCardAsync(string keyword, CancellationToken ct = default);

    /// <summary>
    /// 获取所有申请人姓名（用于批量匹配）
    /// </summary>
    Task<Result<List<string>>> GetAllApplicantNamesAsync(CancellationToken ct = default);

    /// <summary>
    /// 根据ID获取核查详情（用于预览）
    /// </summary>
    Task<Result<AssetVerificationDetail>> GetDetailByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// 导出未完成人员列表到Excel
    /// </summary>
    Task<Result<byte[]>> ExportPendingListToExcelAsync(int year, int? month, CancellationToken ct = default);

    /// <summary>
    /// 导出月度资产核查列表到Excel
    /// </summary>
    /// <param name="year">年份</param>
    /// <param name="month">月份</param>
    /// <param name="statusFilter>状态过 "0"=尚未完成, "1"=已完成、有报告", null=全部</param>
    Task<Result<byte[]>> ExportMonthlyListToExcelAsync(int year, int month, string statusFilter, CancellationToken ct = default);

    /// <summary>
    /// 获取历史统计数据（按年月分组    /// </summary>
    Task<Result<List<MonthlyVerificationStats>>> GetHistoryStatsAsync(int? year, CancellationToken ct = default);

    /// <summary>
    /// 获取待处理任务列表（不分页）
    /// </summary>
    Task<Result<List<AssetVerificationTask>>> GetPendingTasksAsync(int year, int? month, CancellationToken ct = default);

    /// <summary>
    /// 按日期范围获取待处理任务列表（不分页    /// </summary>
    Task<Result<List<AssetVerificationTask>>> GetPendingTasksByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken ct = default);

    /// <summary>
    /// 获取已完成资产核查记录（分页，支持关键词搜索）
    /// </summary>
    Task<Result<PagedResult<AssetVerificationTask>>> GetCompletedPagedAsync(string keyword, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 获取待处理资产核查记录（分页，支持关键词搜索）
    /// </summary>
    Task<Result<PagedResult<AssetVerificationTask>>> GetPendingVerificationPagedAsync(string keyword, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 获取已完成资产核查记录总数
    /// </summary>
    Task<Result<int>> GetCompletedCountAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取待处理资产核查记录总数
    /// </summary>
    Task<Result<int>> GetPendingCountAsync(CancellationToken ct = default);

    /// <summary>
    /// 更新核查状态（按户：同 head_id_card 的整户记录一并更新）
    /// '0'=已提交 '1'=有报告未建档 '2'=已建档 '3'=不予认定
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
        "0" => "已提交",
        "1" => "有报告",
        "2" => "已完成",
        "3" => "已拒绝",
        ApplicationStatusCodes.PENDING => "已提交",
        ApplicationStatusCodes.COMPLETED => "有报告",
        "Error" => "异常",
        _ => Status
    };

    /// <summary>是否可执行"不予认定"（仅"有报告未建档"状态）</summary>
    public bool CanReject => Status == "1";

    public string DisplayInfo => $"{ArchiveName ?? ""} ({ArchiveIdCard ?? ""}) {Relationship ?? ""}";
}

/// <summary>
/// 资产核查实体
/// </summary>
public class AssetVerification
{
    public long Id { get; set; }
    public long ArchiveId { get; set; }
    public string ArchiveName { get; set; } = string.Empty;
    public string ArchiveIdCard { get; set; } = string.Empty;
    public int VerificationYear { get; set; }
    public int VerificationMonth { get; set; }
    public string VerificationType { get; set; } = "Quick";
    public string Status { get; set; } = ApplicationStatusCodes.PENDING;
    public decimal TotalAssetValue { get; set; }
    public decimal HousingAssetValue { get; set; }
    public decimal VehicleAssetValue { get; set; }
    public decimal FinancialAssetValue { get; set; }
    public decimal BusinessAssetValue { get; set; }
    public string VerificationResult { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
    public string CompletedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// 资产核查创建请求
/// </summary>
public class AssetVerificationCreateRequest
{
    public long ArchiveId { get; set; }
    public int VerificationYear { get; set; }
    public int VerificationMonth { get; set; }
    public string VerificationType { get; set; } = "Quick";
    public string CreatedBy { get; set; } = string.Empty;
}
