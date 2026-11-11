namespace NewCosmos.Models.Entities;

/// <summary>
/// 入户调查日期批查投影（nc_biz_household_surveys 两列）。
/// 仅工作流列表"批次顺延提示"使用；整行读取仍走 HouseholdSurvey 实体，勿扩字段。
/// </summary>
public class SurveyDateRecord
{
    /// <summary>关联申请ID</summary>
    public long ApplicationId { get; set; }

    /// <summary>入户调查日期</summary>
    public DateTime SurveyDate { get; set; }
}
