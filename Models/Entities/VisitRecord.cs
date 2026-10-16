namespace NewCosmos.Models.Entities;

/// <summary>
/// 回访记录实体（对应 nc_biz_visit_records 表）。
/// 低收入人口认定流程附属回访：认定通过后在申请详情补录（回访时间/经办/情况），
/// T3「档案_咨询登记台账」打印按 applicant_id_card 取最近一条填 VISIT_* 三键。
/// </summary>
public class VisitRecord
{
    /// <summary>主键ID</summary>
    public long Id { get; set; }

    /// <summary>关联申请ID（认定流程）</summary>
    public long ApplicationId { get; set; }

    /// <summary>申请人身份证号（打印侧按身份证取最近回访的桥接键，INSERT 时由申请表同源带出）</summary>
    public string ApplicantIdCard { get; set; } = string.Empty;

    /// <summary>回访时间（→ VISIT_DATE {回访时间}）</summary>
    public DateTime VisitAt { get; set; }

    /// <summary>回访经办人员（→ VISIT_OPERATOR {回访经办人员}）</summary>
    public string VisitBy { get; set; } = string.Empty;

    /// <summary>回访情况（→ VISIT_STATUS {回访情况}；枚举：已回访/电话回访/未联系上/无需回访）</summary>
    public string VisitResult { get; set; } = string.Empty;

    /// <summary>回访备注</summary>
    public string? VisitRemark { get; set; }

    /// <summary>补录人</summary>
    public string? CreatedBy { get; set; }

    /// <summary>创建时间</summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>更新时间</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>删除时间（软删除标记）</summary>
    public DateTime? DeletedAt { get; set; }
}
