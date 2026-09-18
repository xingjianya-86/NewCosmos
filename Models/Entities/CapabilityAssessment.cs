namespace NewCosmos.Models.Entities;

/// <summary>
/// 能力鉴定实体（纯 POCO，对应 nc_biz_capability_assessments 表）
/// 六项考核指标：进食、穿衣、上下床、如厕、室内行走、洗澡
/// </summary>
public class CapabilityAssessment
{
    /// <summary>主键ID</summary>
    public long Id { get; set; }

    /// <summary>关联申请ID</summary>
    public long ApplicationId { get; set; }

    /// <summary>评估日期</summary>
    public DateTime AssessmentDate { get; set; } = DateTime.Today;

    /// <summary>评估人</summary>
    public string AssessorName { get; set; } = string.Empty;

    // 六项考核指标（0=不能完成, 1=能完成）

    /// <summary>进食能力</summary>
    public int Eating { get; set; } = 1;

    /// <summary>穿衣能力</summary>
    public int Dressing { get; set; } = 1;

    /// <summary>上下床能力</summary>
    public int GettingInOutOfBed { get; set; } = 1;

    /// <summary>如厕能力</summary>
    public int UsingToilet { get; set; } = 1;

    /// <summary>室内行走能力</summary>
    public int IndoorWalking { get; set; } = 1;

    /// <summary>洗澡能力</summary>
    public int Bathing { get; set; } = 1;

    // 自动计算字段

    /// <summary>能完成的项目数</summary>
    public int CompletedItems => Eating + Dressing + GettingInOutOfBed + UsingToilet + IndoorWalking + Bathing;

    /// <summary>自理能力等级</summary>
    public string SelfCareLevel => CompletedItems switch
    {
        6 => Constants.DictionaryConstants.CapabilityLevel.FULL_SELF_CARE,
        5 => Constants.DictionaryConstants.CapabilityLevel.MILD_DISABILITY,
        3 or 4 => Constants.DictionaryConstants.CapabilityLevel.MODERATE_DISABILITY,
        1 or 2 => Constants.DictionaryConstants.CapabilityLevel.SEVERE_DISABILITY,
        _ => Constants.DictionaryConstants.CapabilityLevel.COMPLETE_DISABILITY
    };

    /// <summary>照料等级（根据自理能力等级自动判定）</summary>
    public string CareLevel => Constants.DictionaryConstants.CareLevel.DetermineByCapability(SelfCareLevel);

    /// <summary>备注</summary>
    public string Remark { get; set; } = string.Empty;

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>更新时间</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>删除时间（软删除）</summary>
    public DateTime? DeletedAt { get; set; }
}
