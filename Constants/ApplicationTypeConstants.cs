namespace NewCosmos.Constants;

/// <summary>
/// nc_biz_applications.application_type 业务类型常量（SQL 与 C# 统一引用，禁止内联字面量）。
/// <para>
/// LowIncome（含存量 NULL）走完整低收入人口认定流程，参与月报/首页统计/工作流；
/// RemoteAssistance（异地协助）只做基本信息 + 入户调查 + 邻里走访文书，
/// 仅本地留存，不进入任何正式统计口径，见 <see cref="ApplicationStatusCodes"/>。
/// </para>
/// </summary>
public static class ApplicationTypeConstants
{
    /// <summary>低收入人口认定（存量 NULL 也按此口径）。</summary>
    public const string LOW_INCOME = "LowIncome";

    /// <summary>异地协助（入户调查 + 邻里走访，仅本地留存）。</summary>
    public const string REMOTE_ASSISTANCE = "RemoteAssistance";
}
