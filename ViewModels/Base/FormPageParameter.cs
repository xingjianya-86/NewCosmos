using NewCosmos.Models.Enums;

namespace NewCosmos.ViewModels.Base;

/// <summary>
/// 通用业务表单页导航参数（Create 无 id；Edit/View/Review/Completion 必带 id）。
/// </summary>
public sealed record FormPageParameter(FormOperationMode Mode, long? ApplicationId = null);

/// <summary>
/// 高龄津贴申请表单页导航参数：保存后跳转停发办理的可选联动；
/// Create 模式可选预填身份证/姓名（下月待办「待新增」快捷办理用，预填后自动判类）。
/// </summary>
public sealed record ElderlyFormPageParameter(
    FormOperationMode Mode,
    long? ApplicationId = null,
    bool NavigateToStopAfterSave = false,
    string? PrefillIdCard = null,
    string? PrefillName = null);

/// <summary>
/// 高龄类别复核页导航参数：队列记录 / 在享档案 / 名册记录 三选一定位人员。
/// </summary>
public sealed record ElderlyReviewPageParameter(
    long? ReviewId = null,
    long? ApplicationId = null,
    string? IdCard = null,
    long? HistoryId = null);

/// <summary>
/// 资产核查记录转救助申请的表单导航参数。
/// </summary>
public sealed record AssetCheckFormParameter(long AssetCheckId);

/// <summary>
/// 以申请档案为主键驱动的子表单页导航参数（能力鉴定 / 一事一议申报）。
/// </summary>
public sealed record ApplicationScopedParameter(long ApplicationId);

/// <summary>
/// 档案产出页初始化来源参数（三选一：资产核查 / 申请档案 / 经济复核归档）。
/// </summary>
public sealed record AssetCheckArchiveParameter(long VerificationId);

/// <summary>档案产出页初始化来源参数：申请档案。</summary>
public sealed record ApplicationArchiveParameter(long ApplicationId);

/// <summary>
/// 档案产出页初始化来源参数：复核/成员变更归档。
/// ApplicationId = 待输出的档案ID（复核流程为旧档案、成员变更为停旧建新后的新档案）；
/// OriginalApplicationId = 变更前旧档案ID（可空，停保告知书判定用）；
/// BusinessTitle = 展示标题（经济复核/成员变更），模板分类与打印权限内部仍走 EconomicReview。
/// </summary>
public sealed record ApplicationReviewArchiveParameter(
    long ApplicationId,
    long? OriginalApplicationId = null,
    string BusinessTitle = "经济复核");
