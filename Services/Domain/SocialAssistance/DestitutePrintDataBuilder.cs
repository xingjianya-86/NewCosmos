using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Services.System;
using Globalization = global::System.Globalization;
using ApplicationEntity = NewCosmos.Models.Entities.Application;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 特困供养人员取消（停止）情况表字段构建器：把被停止的特困档案转换为模板 FieldData 字典。
/// 仿 <see cref="ElderlyBenefits.ElderlyPrintDataBuilder"/>（纯静态）：姓名/性别/年龄/身份证/住址复用通用键，
/// 保障类型复用 DESTITUTE_SUPPORT_TYPE，年保障金额/停发原因/停发时间/业务审批时间走 DESTITUTE_CANCEL_* 新键。
/// 年保障金额 = 特困供养标准(月)×12（§9 一次舍入）在本类单点计算，供退出直出服务与档案制作中心复用，避免双实现。
/// </summary>
public static class DestitutePrintDataBuilder
{
    /// <summary>
    /// 装配取消备案表字段（含年保障金额计算）：从 nc_config_standards 取特困供养标准（城乡×集中/分散）×12。
    /// 标准缺失/异常时年保障金额留空（不打印推测值），其余字段照常装配。供制作中心/退出直出共用。
    /// </summary>
    public static async Task<Dictionary<string, string>> BuildSingleFieldsAsync(
        IStandardConfigService standardConfig, ApplicationEntity app, string? stopReasonKey, CancellationToken ct = default)
    {
        var annual = await ComputeAnnualGuaranteeAmountAsync(standardConfig, app, ct);
        return BuildSingleFields(app, stopReasonKey, annual);
    }

    /// <summary>
    /// 构建取消备案表字段。annualGuaranteeAmount 为特困供养标准（月）×12 后一次舍入到分的年保障金额；
    /// 传 null（如标准配置缺失）时年保障金额留空，不打印推测值。
    /// </summary>
    public static Dictionary<string, string> BuildSingleFields(
        ApplicationEntity app, string? stopReasonKey, decimal? annualGuaranteeAmount)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FieldKeys.APPLICANT_NAME] = app.ApplicantName,
            [FieldKeys.APPLICANT_GENDER] = AddressResolver.ExtractGenderFromIdCard(app.ApplicantIdCard),
            [FieldKeys.APPLICANT_AGE] = AddressResolver.ExtractAgeFromIdCard(app.ApplicantIdCard),
            [FieldKeys.APPLICANT_ID_CARD] = app.ApplicantIdCard,
            [FieldKeys.FAMILY_ADDRESS] = AddressResolver.BuildAddressFromText(app.District, app.Town, app.Community, app.Address),
            [FieldKeys.DESTITUTE_SUPPORT_TYPE] = BuildDestituteSupportTypeDisplay(app),
            [FieldKeys.DESTITUTE_CANCEL_ANNUAL_AMOUNT] = annualGuaranteeAmount.HasValue ? FormatAmount(annualGuaranteeAmount.Value) : string.Empty,
            [FieldKeys.DESTITUTE_CANCEL_STOP_REASON] = DestituteStopReason.BuildCheckText(stopReasonKey),
            [FieldKeys.DESTITUTE_CANCEL_STOP_TIME] = FormatDate(app.StopDate),
            [FieldKeys.DESTITUTE_CANCEL_APPROVAL_DATE] = FormatDate(app.FirstApprovedAt)
        };
    }

    /// <summary>
    /// 年保障金额 = 特困供养标准（元/月）×12，一次舍入到分（§9 口径：不从已舍入月值反推）。
    /// 标准按「城乡 × 集中/分散」四象限从 nc_config_standards 取（Rural/UrbanDestituteStandard）。
    /// 标准缺失/异常返回 null（调用方留空），不抛异常中断出表。
    /// </summary>
    public static async Task<decimal?> ComputeAnnualGuaranteeAmountAsync(
        IStandardConfigService standardConfig, ApplicationEntity app, CancellationToken ct = default)
    {
        try
        {
            var isRural = IsDestituteRural(app);
            var hukou = isRural ? "Rural" : "Urban";
            var type = isRural ? "RuralDestituteStandard" : "UrbanDestituteStandard";
            var supportMode = app.DestituteSupportType; // Centralized / Scattered

            var std = await standardConfig.GetStandardValueAsync(type, hukou, supportMode, ct);
            if (!std.IsSuccess || std.Value <= 0)
                return null;
            return Math.Round(std.Value * 12m, 2);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 构建保障类型勾选文本（☑分散供养 □集中供养 / □分散供养 ☑集中供养），未确定时两框皆空。
    /// 单点实现：ArchiveProductionViewModel.DocumentFields.BuildDestituteSupportTypeDisplay 委托此处，避免双实现漂移。
    /// </summary>
    public static string BuildDestituteSupportTypeDisplay(ApplicationEntity app)
    {
        if (app.DestituteSupportType == "Centralized" || app.DestituteSupportType == ClassificationConstants.SupportMode.CENTRALIZED)
            return "□分散供养 ☑集中供养";
        if (app.DestituteSupportType == "Scattered" || app.DestituteSupportType == ClassificationConstants.SupportMode.SCATTERED)
            return "☑分散供养 □集中供养";
        return "□分散供养 □集中供养";
    }

    /// <summary>特困档案城乡判定：分类码（Rural/UrbanDestitute*）优先，无分类码时按户籍。</summary>
    private static bool IsDestituteRural(ApplicationEntity app)
    {
        var c = app.ClassificationResult ?? string.Empty;
        if (ClassificationConstants.IsCodeRural(c)) return true;
        if (ClassificationConstants.IsCodeUrban(c)) return false;
        return string.IsNullOrEmpty(app.HukouType) || ClassificationConstants.HukouType.IsHukouRural(app.HukouType);
    }

    private static string FormatDate(DateTime? dt) => dt?.ToString("yyyy年M月d日") ?? string.Empty;

    private static string FormatAmount(decimal amount) => $"{amount:F2}元";
}
