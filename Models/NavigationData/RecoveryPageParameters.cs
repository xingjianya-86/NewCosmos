using NewCosmos.Models.Entities;

namespace NewCosmos.Models.NavigationData;

/// <summary>
/// 后补追缴·搜索页导航参数（桌面/手机孪生页同契约）。
/// 缺省（Keyword 空、OpenHistoryMode=false）= 现行为：停止人员页签、无预填。
/// 死亡说明联动「去办理」传 Keyword=死者身份证 + OpenHistoryMode=true，
/// 落在「我的追缴记录」页签并自动过滤加载该人记录。
/// </summary>
public sealed record RecoverySearchParameter(
    string Keyword = "",
    bool OpenHistoryMode = false);

/// <summary>
/// 后补追缴·追缴表单页（搜索录入线）导航参数：
/// Person=新建（搜索页选中的停止人员）；Record=编辑草稿。二者二选一，Record 优先。
/// </summary>
public sealed record RecoveryFormParameter(
    StoppedPersonDto? Person = null,
    RecoveryRecord? Record = null);

/// <summary>
/// 后补追缴·手工录入页导航参数：Record=编辑草稿；null=新建（替代原无参跳转）。
/// </summary>
public sealed record RecoveryManualParameter(
    RecoveryRecord? Record = null);
