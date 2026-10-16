using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 回访记录服务：低收入人口认定流程附属回访的补录与查询（nc_biz_visit_records）。
/// 录入侧按 application_id（申请详情内嵌回访区）；打印侧按 applicant_id_card 取最近一条（T3 台账）。
/// </summary>
public interface IVisitRecordService
{
    /// <summary>某申请的全部回访记录（回访历史，按回访时间倒序）</summary>
    Task<Result<List<VisitRecord>>> GetListByApplicationIdAsync(long applicationId, CancellationToken ct = default);

    /// <summary>某申请最近一条回访（状态条显示用；无记录返回 null）</summary>
    Task<Result<VisitRecord?>> GetLatestByApplicationIdAsync(long applicationId, CancellationToken ct = default);

    /// <summary>按身份证取最近一条回访（打印注入用；无记录返回 null）</summary>
    Task<Result<VisitRecord?>> GetLatestByIdCardAsync(string idCard, CancellationToken ct = default);

    /// <summary>补录一条回访（applicant_id_card 由申请表 INSERT…SELECT 同源带出；返回新记录 id）</summary>
    Task<Result<long>> AddAsync(VisitRecord record, string? createdBy, CancellationToken ct = default);
}
