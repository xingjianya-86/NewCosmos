using NewCosmos.Models.Entities;

namespace NewCosmos.Models.NavigationData;

/// <summary>
/// 跨流程联动预填参数：高龄停发登记。
/// 由死亡情况说明「联动卡片」携带死亡日期/时刻/原因预填停发表单，
/// 目标页仍走自身校验与确认（只读视图 + 预填跳转，不跨域写）。
/// 与既有 <see cref="ElderlyApplication"/> 直传参数并存（页面同时实现两个 IParameterizedPage 契约）。
/// </summary>
public sealed record ElderlyStopPrefill(
    ElderlyApplication Application,
    DateTime? DeathDate,
    string? DeathTime,
    string? DeathReason);

/// <summary>
/// 跨流程联动预填参数：户主死亡变更（停旧建新）。
/// ApplicationId 为旧档案ID；死亡日期/时刻/原因/证明编号预填表单，由经办确认后提交。
/// </summary>
public sealed record HouseholdDeathPrefill(
    long ApplicationId,
    DateTime? DeathDate,
    string? DeathTime,
    string? DeathReason,
    string? DeathCertificateNo);
