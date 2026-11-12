namespace NewCosmos.Models.Results;

/// <summary>
/// 修改申请日期结果：
/// <paramref name="ApplicationNo"/> = 新申请编号；
/// <paramref name="SurveyDate"/> = 同步调整后的入户调查日期
/// （改期会把入户日期自动钳进 [新申请日, min(会议日, 今天)] 窗口——批次锚点=入户日期优先，
/// 不同步则批次/提示不变；无调查记录、导入库建档档（调查日=纳入时间铁律）或日期未变时为 null/原值）。
/// </summary>
public sealed record ChangeApplicationDateResult(string ApplicationNo, DateTime? SurveyDate);
