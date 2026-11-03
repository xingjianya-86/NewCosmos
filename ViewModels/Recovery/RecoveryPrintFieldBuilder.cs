using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.Recovery;
using NewCosmos.Services.Domain.UserManagement;

namespace NewCosmos.ViewModels.Recovery;

/// <summary>
/// 追缴《追缴资金办理单》打印字段单点装配（2026-11 合并）：
/// RecoveryFormViewModel / RecoveryManualViewModel / RecoverySearchViewModel 此前各持一份
/// 逐行相同的 BuildPrintFieldsAsync + GetReportUnitAsync（约 60% 重复），任何字段改动需改三处。
/// 唯一差异点（来源类型显示文本）由参数传入。
/// </summary>
internal static class RecoveryPrintFieldBuilder
{
    /// <summary>
    /// 填报单位默认值：当前登录用户所属组织名称（单位全称，取不到时回退登录用户名）。
    /// </summary>
    internal static async Task<string> GetReportUnitAsync(IServiceProvider serviceProvider)
    {
        var orgId = App.CurrentUserOrganizationId;
        if (!orgId.HasValue)
            return string.IsNullOrWhiteSpace(App.CurrentUserName) ? string.Empty : App.CurrentUserName;

        try
        {
            var orgService = serviceProvider.GetRequiredService<IOrganizationService>();
            var result = await orgService.GetByIdAsync(orgId.Value, CancellationToken.None);
            if (result.IsSuccess && result.Value != null)
            {
                var org = result.Value;
                if (!string.IsNullOrWhiteSpace(org.Name)) return org.Name;
                if (!string.IsNullOrWhiteSpace(org.TownName)) return org.TownName;
                if (!string.IsNullOrWhiteSpace(org.CountyName)) return org.CountyName;
            }
        }
        catch (Exception ex)
        {
            serviceProvider.GetService<ILoggerService>()?.Warn($"获取填报单位默认值失败: {ex.Message}");
        }

        return string.IsNullOrWhiteSpace(App.CurrentUserName) ? string.Empty : App.CurrentUserName;
    }

    /// <summary>
    /// 构建《追缴资金办理单》打印字段（key 与模板中文占位符对应）。
    /// </summary>
    /// <param name="serviceProvider">DI（填报单位与编号分配）</param>
    /// <param name="recoveryService">追缴服务（编号取库内已分配值，补打同号）</param>
    /// <param name="recordId">追缴记录 ID（首次打印时补分配编号并回写）</param>
    /// <param name="personName">追缴人员姓名</param>
    /// <param name="idCard">身份证号</param>
    /// <param name="sourceTypeDisplay">来源类型显示文本（三入口唯一差异点）</param>
    /// <param name="recoveryAmount">应追缴总额</param>
    /// <param name="recoveredAmount">已追缴金额</param>
    /// <param name="startMonth">起算月（yyyy-MM）</param>
    /// <param name="endMonth">止算月（yyyy-MM）</param>
    internal static async Task<Dictionary<string, string>> BuildFieldsAsync(
        IServiceProvider serviceProvider,
        IRecoveryService recoveryService,
        long recordId,
        string personName,
        string? idCard,
        string sourceTypeDisplay,
        decimal recoveryAmount,
        decimal recoveredAmount,
        string? startMonth,
        string? endMonth)
    {
        var now = DateTime.Now;
        var reportUnit = await GetReportUnitAsync(serviceProvider);
        // 编号取库内已分配值（保存时落库）——补打/多次打印同号；存量记录首次打印时补分配回写
        var codeResult = await recoveryService.GetOrAssignRecoveryCodeAsync(recordId);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FieldKeys.RECOVERY_UNIT] = reportUnit,
            [FieldKeys.RECOVERY_CODE] = codeResult.IsSuccess ? codeResult.Value : string.Empty,
            [FieldKeys.RECOVERY_SEQ] = "1",
            [FieldKeys.RECOVERY_PERSON_NAME] = personName,
            [FieldKeys.RECOVERY_ID_CARD] = idCard ?? string.Empty,
            [FieldKeys.RECOVERY_SOURCE_TYPE] = sourceTypeDisplay,
            [FieldKeys.RECOVERY_AMOUNT] = recoveryAmount.ToString("N2"),
            [FieldKeys.RECOVERED_AMOUNT] = recoveredAmount.ToString("N2"),
            [FieldKeys.RECOVERY_REPORT_DATE] = now.ToString("yyyy年MM月dd日"),
            [FieldKeys.RECOVERY_RANGE] = $"{startMonth} 至 {endMonth}"
        };
    }
}
