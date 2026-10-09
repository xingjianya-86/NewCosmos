using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.AssetVerification;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.NearRelative;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.Domain.SpecialApproval;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.System;
using NewCosmos.Services.Utilities;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using ApplicationEntity = NewCosmos.Models.Entities.Application;

namespace NewCosmos.ViewModels.ArchiveManagement;

public partial class ArchiveProductionViewModel
{
    // ========================
    //  特困入户调查表专用字段构建方法
    // ========================

    /// <summary>
    /// 构建特困照料人情况
    /// </summary>
    private string BuildDestituteCaregiverSituation(ApplicationEntity app, List<Caregiver>? caregivers)
    {
        if (app.DestituteSupportType == "Centralized" || app.DestituteSupportType == ClassificationConstants.SupportMode.CENTRALIZED)
        {
            return "集中供养，由专业机构提供照料服务";
        }

        if (caregivers != null && caregivers.Count > 0)
        {
            var caregiverNames = string.Join("、", caregivers.Select(c => c.Name));
            return $"分散供养，由{caregiverNames}提供日常照料";
        }

        return "分散供养，由亲属提供日常照料";
    }

    /// <summary>
    /// 构建特困健康评估
    /// </summary>
    private string BuildDestituteHealthAssessment(ApplicationEntity app, List<FamilyMember>? members)
    {
        var sb = new System.Text.StringBuilder();

        // 户主健康状况
        sb.AppendLine($"户主自理能力: {BuildSelfCareAbility(app)}");
        sb.AppendLine($"健康状况: {app.HealthStatus ?? "未填写"}");

        if (!string.IsNullOrEmpty(app.DisabilityType))
        {
            sb.AppendLine($"残疾类型: {app.DisabilityType} {app.DisabilityLevel}");
        }

        if (!string.IsNullOrEmpty(app.DiseaseName))
        {
            sb.AppendLine($"疾病情况: {app.DiseaseName}");
        }

        // 家庭成员健康概况
        if (members != null && members.Count > 0)
        {
            var disabledCount = members.Count(m => m.IsDisabled);
            var diseasedCount = members.Count(m => !string.IsNullOrEmpty(m.DiseaseName));

            if (disabledCount > 0 || diseasedCount > 0)
            {
                sb.AppendLine($"家庭成员健康概况: 残疾{disabledCount}人, 患病{diseasedCount}人");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建特困居住条件
    /// </summary>
    private string BuildDestituteLivingCondition(ApplicationEntity app, EconomicDetailData? economicDetail)
    {
        var sb = new System.Text.StringBuilder();

        if (economicDetail?.FamilyProperties != null && economicDetail.FamilyProperties.Count > 0)
        {
            var property = economicDetail.FamilyProperties[0];
            sb.AppendLine($"住房类型: {property.HousingStructure ?? "未知"}");
            sb.AppendLine($"住房面积: {FormatDecimal(property.Area)}㎡");
            sb.AppendLine($"住房性质: {property.HousingNature ?? "未知"}");
        }
        else
        {
            sb.AppendLine("住房情况: 未填写");
        }

        sb.AppendLine($"家庭地址: {app.Address ?? "未填写"}");

        return sb.ToString();
    }

    /// <summary>
    /// 构建特困日常照料情况
    /// </summary>
    private string BuildDestituteDailyCare(ApplicationEntity app, List<Caregiver>? caregivers)
    {
        if (app.DestituteSupportType == "Centralized" || app.DestituteSupportType == ClassificationConstants.SupportMode.CENTRALIZED)
        {
            return "由供养机构提供日常照料，包括饮食起居、清洁卫生、医疗护理等服务";
        }

        if (caregivers != null && caregivers.Count > 0)
        {
            var caregiverInfo = string.Join("；", caregivers.Select(c =>
                $"{c.Name}（{(string.IsNullOrWhiteSpace(c.Relationship) ? "亲属" : DictDisplayHelper.GetFamilyRelationshipDisplay(c.Relationship))}）"));
            return $"由{caregiverInfo}提供日常照料";
        }

        return "由亲属提供日常照料";
    }

    /// <summary>
    /// 构建特困医疗情况
    /// </summary>
    private string BuildDestituteMedicalSituation(ApplicationEntity app)
    {
        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrEmpty(app.DiseaseName))
        {
            sb.AppendLine($"主要疾病: {app.DiseaseName}");
        }

        if (!string.IsNullOrEmpty(app.DisabilityType))
        {
            sb.AppendLine($"残疾情况: {app.DisabilityType} {app.DisabilityLevel}");
        }

        sb.AppendLine("医疗保障: 城乡居民基本医疗保险");

        return sb.ToString();
    }

    /// <summary>
    /// 构建特困家庭状况
    /// </summary>
    private string BuildDestituteFamilyStatus(ApplicationEntity app, List<FamilyMember>? members)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"家庭人口: {app.FamilySize}人");
        sb.AppendLine($"户主姓名: {app.ApplicantName}");
        var hukouType = ClassificationConstants.HukouType.IsHukouRural(app.HukouType) ? "农村" : "城市";
        sb.AppendLine($"户籍性质: {hukouType}");

        if (members != null && members.Count > 0)
        {
            var ages = members.Select(m => m.Age).ToList();
            var avgAge = ages.Average();
            sb.AppendLine($"家庭成员平均年龄: {avgAge:F0}岁");

            var elderlyCount = ages.Count(a => a >= 60);
            if (elderlyCount > 0)
            {
                sb.AppendLine($"60岁以上成员: {elderlyCount}人");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建特困财产状况
    /// </summary>
    private string BuildDestitutePropertySituation(ApplicationEntity app, EconomicDetailData? economicDetail)
    {
        var sb = new System.Text.StringBuilder();

        // 车辆
        if (economicDetail?.Vehicles != null && economicDetail.Vehicles.Count > 0)
        {
            var vehicles = string.Join("、", economicDetail.Vehicles.Select(v => $"{v.Brand} {v.Model}"));
            sb.AppendLine($"车辆: {vehicles}");
        }
        else
        {
            sb.AppendLine("车辆: 无");
        }

        // 金融资产
        if (economicDetail?.FinancialAssets != null && economicDetail.FinancialAssets.Count > 0)
        {
            var financial = economicDetail.FinancialAssets[0];
            sb.AppendLine($"银行存款: {FormatDecimal(financial.BankDepositAmount)}元");
        }
        else
        {
            sb.AppendLine("银行存款: 无");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建特困调查结论
    /// </summary>
    private string BuildDestituteSurveyConclusion(ApplicationEntity app)
    {
        var supportType = app.DestituteSupportType == "Centralized" || app.DestituteSupportType == ClassificationConstants.SupportMode.CENTRALIZED
            ? "集中供养"
            : "分散供养";

        return $"经入户调查，{app.ApplicantName}符合特困人员认定条件，建议纳入{supportType}保障范围。";
    }

    /// <summary>
    /// 构建特困供养机构信息
    /// </summary>
    private string BuildDestituteInstitutionInfo(ApplicationEntity app)
    {
        if (app.DestituteSupportType != "Centralized" && app.DestituteSupportType != ClassificationConstants.SupportMode.CENTRALIZED)
        {
            return "非集中供养";
        }

        if (app.SupportInstitutionId > 0)
        {
            return $"供养机构ID: {app.SupportInstitutionId}";
        }

        return "待指定供养机构";
    }

    /// <summary>
    /// 构建自理能力评估（三档：自理/半自理/不能自理）。优先使用能力鉴定表结论。
    /// </summary>
    private string BuildSelfCareAbility(ApplicationEntity app, CapabilityAssessment? capability = null)
    {
        if (capability != null && !string.IsNullOrEmpty(capability.SelfCareLevel))
        {
            return DictionaryConstants.CapabilityLevel.ToSelfCareDisplay(capability.SelfCareLevel);
        }

        // 兜底：按健康状况和残疾情况判断
        var healthStatus = app.HealthStatus ?? "";
        var disabilityType = app.DisabilityType ?? "";

        if (!string.IsNullOrEmpty(disabilityType))
        {
            if (disabilityType.Contains("一级") || disabilityType.Contains("重度"))
                return "不能自理";
            if (disabilityType.Contains("二级") || disabilityType.Contains("中度"))
                return "半自理";
            return "自理";
        }

        if (healthStatus.Contains("较差"))
            return "半自理";

        return "自理";
    }

    /// <summary>
    /// 构建残疾类别（两行：第一行类别、第二行等级）
    /// </summary>
    private string BuildDisabilityDisplay(ApplicationEntity app)
    {
        var typeKey = app.DisabilityType ?? "";
        var levelKey = app.DisabilityLevel ?? "";
        if (string.IsNullOrEmpty(typeKey) && string.IsNullOrEmpty(levelKey)) return "无";

        var type = _dictCacheService.GetValue(DictionaryTypeCodes.DisabilityTypes, typeKey);
        var level = _dictCacheService.GetValue(DictionaryTypeCodes.DisabilityLevels, levelKey);
        if (string.IsNullOrEmpty(type)) type = typeKey;
        if (string.IsNullOrEmpty(level)) level = levelKey;

        var lines = new List<string>();
        if (!string.IsNullOrEmpty(type)) lines.Add(type);
        if (!string.IsNullOrEmpty(level)) lines.Add(level);
        return string.Join("\n", lines);
    }

    /// <summary>
    /// 构建照料等级（随自理能力三档）
    /// </summary>
    private string BuildCareLevel(ApplicationEntity app, CapabilityAssessment? capability = null)
    {
        var selfCare = BuildSelfCareAbility(app, capability);
        if (selfCare == "不能自理")
            return "特级照料";
        if (selfCare == "半自理")
            return "一级照料";
        return "自理";
    }

    /// <summary>
    /// 构建人员类别（可组合：老年人/残疾人/未成年人）
    /// </summary>
    private string BuildPersonCategory(ApplicationEntity app)
    {
        var parts = new List<string>();
        var ageText = AddressResolver.ExtractAgeFromIdCard(app.ApplicantIdCard);
        int.TryParse(ageText, out var age);
        if (age >= 60) parts.Add("老年人");
        if (!string.IsNullOrEmpty(app.DisabilityType)) parts.Add("残疾人");
        if (age >= 0 && age < 18) parts.Add("未成年人");
        return parts.Count > 0 ? string.Join("、", parts) : "-";
    }

    /// <summary>
    /// 构建供养方式显示文本
    /// </summary>
    private string BuildDestituteSupportTypeDisplay(ApplicationEntity app)
    {
        if (app.DestituteSupportType == "Centralized" || app.DestituteSupportType == ClassificationConstants.SupportMode.CENTRALIZED)
            return "集中供养";
        if (app.DestituteSupportType == "Scattered" || app.DestituteSupportType == ClassificationConstants.SupportMode.SCATTERED)
            return "分散供养";
        return app.DestituteSupportType ?? "未确定";
    }

    /// <summary>
    /// 构建刚性支出扣减明细
    /// </summary>
    private string BuildRigidExpenditureDisplay(ApplicationEntity app, EconomicDetailData? economicDetail)
    {
        if (economicDetail?.RigidExpenditures == null || economicDetail.RigidExpenditures.Count == 0)
            return "-";

        var parts = new List<string>();
        foreach (var item in economicDetail.RigidExpenditures)
        {
            var typeName = RigidExpenditureConstants.GetDescription(item.ExpenditureType);
            parts.Add($"{typeName}{item.PersonDescription}{FormatDecimal(item.Amount * 12)}元{(!string.IsNullOrEmpty(item.Remark) ? $"（{item.Remark}）" : "")}");
        }
        return string.Join("；", parts) + "。";
    }

    /// <summary>
    /// 构建调查走访情况
    /// </summary>
    private string BuildSurveyVisitSituation(ApplicationEntity app)
    {
        return $"经入户调查，该家庭确属困难家庭，符合{(ClassificationConstants.ConvertFromCode(app.ClassificationResult ?? ""))}条件。";
    }

    /// <summary>
    /// 生成勾选框+标签："申请家庭（☑是 □否）xxx"
    /// </summary>
    private static string BuildCheckboxWithLabel(bool isChecked, string label)
    {
        var yes = isChecked ? "☑是" : "□是";
        var no = isChecked ? "□否" : "☑否";
        return $"申请家庭（{yes} {no}）{label}";
    }

    /// <summary>
    /// 生成简单勾选框："☑是 □否" 或 "□是 ☑否"
    /// </summary>
    private static string BuildYesNoCheckbox(bool value)
    {
        return value ? "☑是 □否" : "□是 ☑否";
    }

    /// <summary>
    /// 构建政府意见：根据入户调查结论输出
    /// </summary>
    private static string BuildGovernmentOpinion(HouseholdSurvey? survey)
    {
        if (survey == null) return "";
        var conclusion = survey.SurveyConclusion ?? "";
        if (conclusion == "属实") return "经入户调查，该家庭情况属实。";
        if (conclusion == "不属实" || conclusion == "部分属实")
        {
            var reason = survey.SurveyNotes ?? "";
            return string.IsNullOrEmpty(reason)
                ? $"经入户调查，{conclusion}。"
                : $"经入户调查，{conclusion}。{reason}";
        }
        return conclusion;
    }

    /// <summary>
    /// 入户调查日期：优先用实际入户调查日；
    /// 无调查记录（补录/未录入）时回退 B 线受理窗口起点（上月15日）
    /// </summary>
    private static string ResolveSurveyDate(TimelineResult tl, DateTime? actualSurvey)
    {
        var windowStart = tl.CycleStartDate.Date;
        var baseDate = actualSurvey.HasValue && actualSurvey.Value.Date != default
            ? actualSurvey.Value
            : windowStart;
        return baseDate.ToString("yyyy年M月d日");
    }

    /// <summary>
    /// 档案时间锚定日期：数据补全完成 → 首次审批 → 更新 → 创建。
    /// 用于按该档案自身业务时间取 B 线周期（档案编号/卷号/公示/审核/生效/调查日期），
    /// 避免档案补打时按“当天”重算导致编号/卷号漂移。
    /// </summary>
    private static DateTime ResolveArchiveAnchorDate(ApplicationEntity app)
    {
        if (app.DataCompletedAt is { } completed && completed != default) return completed;
        if (app.FirstApprovedAt is { } approved && approved != default) return approved;
        if (app.UpdatedAt != default) return app.UpdatedAt;
        return ResolveApplicationDate(app);
    }

    /// <summary>
    /// 申请日期（业务受理时间）解析（稳定口径）：
    /// 申请编号内嵌日期（SA+yyyyMMdd，创建时生成）→ 创建时间 → 首次审批 → 补全完成 → 更新 → 今天。
    /// 记录 created_at 为 NULL 时实体初值会被填成加载时刻（UtcNow），直接打印会导致每次补打都漂移，
    /// 故以申请编号日期优先。
    /// </summary>
    private static DateTime ResolveApplicationDate(ApplicationEntity app)
    {
        var no = app.ApplicationNo;
        if (!string.IsNullOrEmpty(no) && no.Length >= 10
            && no.StartsWith("SA", StringComparison.Ordinal)
            && DateTime.TryParseExact(no.Substring(2, 8), "yyyyMMdd", null,
                System.Globalization.DateTimeStyles.None, out var fromNo))
        {
            return fromNo;
        }
        if (app.CreatedAt != default) return app.CreatedAt;
        if (app.FirstApprovedAt is { } approved && approved != default) return approved;
        if (app.DataCompletedAt is { } completed && completed != default) return completed;
        if (app.UpdatedAt != default) return app.UpdatedAt;
        return DateTime.Today;
    }

    /// <summary>
    /// 将金额转换为中文大写（如 200.50 → "贰佰元伍角"）
    /// </summary>
    private static string ConvertToChineseAmount(decimal amount)
    {
        if (amount == 0) return "零元整";

        string[] cnDigits = { "零", "壹", "贰", "叁", "肆", "伍", "陆", "柒", "捌", "玖" };
        string[] cnIntUnits = { "", "拾", "佰", "仟" };
        string[] cnBigUnits = { "", "万", "亿", "兆" };

        var parts = amount.ToString("F2").Split('.');
        var intPart = long.Parse(parts[0]);
        var decPart = int.Parse(parts[1]);

        var result = "";

        if (intPart > 0)
        {
            var intStr = intPart.ToString();
            var len = intStr.Length;
            for (int i = 0; i < len; i++)
            {
                var n = intStr[i] - '0';
                var pos = len - i - 1;
                var bigUnit = pos / 4;
                var smallUnit = pos % 4;

                if (n == 0)
                {
                    if (result.Length > 0 && result[^1] != '零')
                        result += "零";
                }
                else
                {
                    result += cnDigits[n] + cnIntUnits[smallUnit];
                }

                if (smallUnit == 0 && bigUnit > 0 && result.Length > 0 && result[^1] != '零')
                    result += cnBigUnits[bigUnit];
            }
            result += "元";
        }

        var jiao = decPart / 10;
        var fen = decPart % 10;

        if (jiao > 0)
            result += cnDigits[jiao] + "角";
        if (fen > 0)
            result += cnDigits[fen] + "分";
        if (jiao == 0 && fen == 0)
            result += "整";

        return result;
    }

    /// <summary>
    /// 户主分类施保判定（权威口径：ClassificationService.CalculateClassifiedSubsidyAsync）
    /// 重病=主表重病标记或健康状态；重残=健康状态或残疾等级重度（一、二级任意类型；三级智力/精神）；高龄/未成年按身份证年龄
    /// </summary>
    private static (bool SevereIllness, bool SevereDisability, bool Elderly, bool Minor) EvaluateHeadFlags(ApplicationEntity app)
    {
        var headAge = IdCardValidator.ExtractAgeBasic(app.ApplicantIdCard ?? "") ?? 0;
        return (
            app.IsSevereDisease || DictionaryConstants.HealthStatus.HasSevereDisease(app.HealthStatus ?? ""),
            DictionaryConstants.HealthStatus.HasSevereDisability(app.HealthStatus ?? "")
                || ClassificationConstants.DisabilityLevel.IsSevereForAssistance(app.DisabilityLevel ?? "", app.DisabilityType),
            headAge >= AgeConstants.ELDERLY_THRESHOLD,
            headAge > 0 && headAge < AgeConstants.MINOR_THRESHOLD);
    }

    /// <summary>
    /// 成员分类施保判定（权威口径）：重病走 IsSevereDisease 标记 + 健康状态双通道；
    /// 重残走 IsSevereDisability + 健康状态 + 残疾等级（含三级智力/精神）
    /// </summary>
    private static (bool SevereIllness, bool SevereDisability, bool Elderly, bool Minor) EvaluateMemberFlags(FamilyMember m)
    {
        var age = m.Age ?? 0;
        return (
            m.IsSevereDisease || DictionaryConstants.HealthStatus.HasSevereDisease(m.HealthStatus ?? ""),
            m.IsSevereDisability
                || DictionaryConstants.HealthStatus.HasSevereDisability(m.HealthStatus ?? "")
                || ClassificationConstants.DisabilityLevel.IsSevereForAssistance(
                    m.DisabilityLevelKeyResolved, m.DisabilityType),
            age >= AgeConstants.ELDERLY_THRESHOLD,
            age > 0 && age < AgeConstants.MINOR_THRESHOLD);
    }

    private static bool IsClassifiedMember(FamilyMember m)
    {
        var flags = EvaluateMemberFlags(m);
        return flags.SevereIllness || flags.SevereDisability || flags.Elderly || flags.Minor;
    }

    /// <summary>
    /// 分类施保共计人数：判定集合 = 户主（主表）+ 共同生活成员（已排除赡养义务人/已死亡）。
    /// count 每人只计 1 次（与权威 count 语义一致）；分项可叠加展示（与权威 Types 叠加语义一致）。
    /// </summary>
    private static (string text, int count) BuildClassifiedCount(ApplicationEntity app, List<FamilyMember> members)
    {
        var head = EvaluateHeadFlags(app);
        bool headEligible = head.SevereIllness || head.SevereDisability || head.Elderly || head.Minor;
        var eligible = members.Where(IsClassifiedMember).ToList();
        int count = (headEligible ? 1 : 0) + eligible.Count;
        if (count == 0) return ("共计 0 人", 0);

        // 类型列表（去重，一人可同时符合多项；count 为去重后人数）
        var types = new List<string>();
        if (head.SevereIllness || eligible.Any(m => EvaluateMemberFlags(m).SevereIllness)) types.Add("重病");
        if (head.SevereDisability || eligible.Any(m => EvaluateMemberFlags(m).SevereDisability)) types.Add("重残");
        if (head.Elderly || eligible.Any(m => EvaluateMemberFlags(m).Elderly)) types.Add("60周岁以上老人");
        if (head.Minor || eligible.Any(m => EvaluateMemberFlags(m).Minor)) types.Add("18周岁以下未成年人");

        return ($"共计 {count} 人，其中 {string.Join("、", types)}", count);
    }

    /// <summary>
    /// 分类施保享受类型（复选框格式）：判定集合 = 户主 + 共同生活成员
    /// </summary>
    private static string BuildClassifiedSubsidyTypeDisplay(ApplicationEntity app, List<FamilyMember> members)
    {
        var head = EvaluateHeadFlags(app);
        bool hasSevereIllness = head.SevereIllness || members.Any(m => EvaluateMemberFlags(m).SevereIllness);
        bool hasSevereDisability = head.SevereDisability || members.Any(m => EvaluateMemberFlags(m).SevereDisability);
        bool hasElderly = head.Elderly || members.Any(m => EvaluateMemberFlags(m).Elderly);
        bool hasMinor = head.Minor || members.Any(m => EvaluateMemberFlags(m).Minor);

        return $"{(hasSevereIllness ? "☑" : "□")}重病 {(hasSevereDisability ? "☑" : "□")}重残 {(hasElderly ? "☑" : "□")}60周岁以上老人 {(hasMinor ? "☑" : "□")}18周岁以下未成年人";
    }

    /// <summary>
    /// 审核确认意见：户主 + 共同生活成员逐人列出符合的分类施保加发条件
    /// </summary>
    private static string BuildAuditOpinion(ApplicationEntity app, List<FamilyMember> members, OrganizationInfoDto orgInfo)
    {
        var lines = new List<string>();
        int seq = 0;

        var head = EvaluateHeadFlags(app);
        if (head.SevereIllness || head.SevereDisability || head.Elderly || head.Minor)
        {
            var headReasons = new List<string>();
            var headAge = IdCardValidator.ExtractAgeBasic(app.ApplicantIdCard ?? "") ?? 0;
            if (head.SevereIllness) headReasons.Add("重病");
            if (head.SevereDisability) headReasons.Add("重残");
            if (head.Elderly) headReasons.Add($"{headAge}周岁以上老人");
            if (head.Minor) headReasons.Add($"{headAge}周岁以下未成年人");
            var headReasonText = string.Join("、", headReasons);
            var headSuffix = headReasons.Count > 1 ? "（仅享受一项加发）" : "";
            lines.Add($"{++seq}. {app.ApplicantName}（户主）：{headReasonText}{headSuffix}；");
        }

        foreach (var m in members.Where(IsClassifiedMember))
        {
            var flags = EvaluateMemberFlags(m);
            var memberReasons = new List<string>();
            if (flags.SevereIllness) memberReasons.Add("重病");
            if (flags.SevereDisability) memberReasons.Add("重残");
            if (flags.Elderly) memberReasons.Add($"{m.Age}周岁以上老人");
            if (flags.Minor) memberReasons.Add($"{m.Age}周岁以下未成年人");

            var reasonText = string.Join("、", memberReasons);
            var suffix = memberReasons.Count > 1 ? "（仅享受一项加发）" : "";
            lines.Add($"{++seq}. {m.Name}：{reasonText}{suffix}；");
        }

        if (seq == 0) return "";
        var result = new List<string> { "经审核，以下家庭成员符合分类施保加发条件：" };
        result.AddRange(lines);
        return string.Join("\n", result);
    }

    /// <summary>
    /// 构建核实待遇文本
    /// </summary>
    private static string BuildLandStatusText(ApplicationEntity app)
    {
        var area = app.FamilyLandArea;
        if (area <= 0) return "";
        return $"{FormatDecimal(area)}亩";
    }

    /// <summary>
    /// 救助类别勾选框（选项名称采用标准法规长名称）
    /// </summary>
    private static string BuildClassificationCheckbox(string code)
    {
        var isSubsistence = ClassificationConstants.IsCodeSubsistence(code);
        var isLowIncome = ClassificationConstants.IsCodeLowIncome(code);
        var isDestitute = ClassificationConstants.IsCodeDestitute(code);
        var isRigid = ClassificationConstants.IsCodeRigidExpenditure(code);
        return $"{(isSubsistence ? "☑" : "□")}最低生活保障对象 {(isLowIncome ? "☑" : "□")}最低生活保障边缘家庭 {(isDestitute ? "☑" : "□")}特困人员 {(isRigid ? "☑" : "□")}刚性支出困难家庭";
    }
    private static string BuildLandStatusCheckbox(ApplicationEntity app)
    {
        var hasSelf = app.SelfFarmedLandArea > 0;
        var hasSublease = app.SubleasedLandArea > 0;
        var hasContract = app.ContractedLandArea > 0;
        if (!hasSelf && !hasSublease && !hasContract) return "";
        return $"{(hasSelf ? "☑" : "□")}自种 {(hasSublease ? "☑" : "□")}转包 {(hasContract ? "☑" : "□")}承包";
    }

    /// <summary>
    /// 土地亩数明细：自种X亩 转包X亩 承包X亩
    /// </summary>
    private static string BuildLandAreaDetail(ApplicationEntity app)
    {
        var parts = new List<string>();
        if (app.SelfFarmedLandArea > 0) parts.Add($"自种{FormatDecimal(app.SelfFarmedLandArea)}亩");
        if (app.SubleasedLandArea > 0) parts.Add($"转包{FormatDecimal(app.SubleasedLandArea)}亩");
        if (app.ContractedLandArea > 0) parts.Add($"承包{FormatDecimal(app.ContractedLandArea)}亩");
        return parts.Count > 0 ? string.Join(" ", parts) : "";
    }

    /// <summary>
    /// 村级土地说明：享受补贴类型与内容
    /// </summary>
    private static string BuildSubsidyTypeContent(ApplicationEntity app, Services.Domain.SocialAssistance.EconomicDetailData? economicDetail)
    {
        if (economicDetail?.Subsidies == null || economicDetail.Subsidies.Count == 0)
            return "无补贴";

        var sb = new System.Text.StringBuilder();
        foreach (var subsidy in economicDetail.Subsidies)
        {
            if (sb.Length > 0) sb.Append("；");
            sb.Append($"{subsidy.SubsidyType}{FormatDecimal(subsidy.Area)}亩×{FormatDecimal(subsidy.UnitPrice)}元/亩={FormatDecimal(subsidy.Amount)}元");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 村级土地说明：土地详细信息。
    /// 第一句直接使用主表折算面积（表单保存时已按家庭份额折算的现成结果，不再重复计算）；
    /// 第二句按台账全部登记人员口径实时汇总，分类说明死亡继承（点名继承人）/无土地权/外来土地人员，
    /// 分摊公式采用份额制（LandShareCalculator 权威算法，与申请表单 CalculateLandConfirmationArea 同源），
    /// 末值为乘积计算值，与第一句享有面积天然一致；不再读取主表 total_confirmed_land_area / confirmed_person_count 死列。
    /// </summary>
    private static string BuildLandDetailInfo(ApplicationEntity app, Services.Domain.SocialAssistance.EconomicDetailData? economicDetail, HashSet<string> familyNames)
    {
        var selfArea = app.SelfFarmedLandArea > 0 ? FormatDecimal(app.SelfFarmedLandArea) : "0";
        var subleaseArea = app.SubleasedLandArea > 0 ? FormatDecimal(app.SubleasedLandArea) : "0";
        var contractArea = app.ContractedLandArea > 0 ? FormatDecimal(app.ContractedLandArea) : "0";
        var totalArea = app.FamilyLandArea > 0 ? FormatDecimal(app.FamilyLandArea) : "0";

        var sb = new System.Text.StringBuilder();
        sb.Append($"该户享有土地{totalArea}亩，其中自种{selfArea}亩，转包{subleaseArea}亩，承包{contractArea}亩");

        // 土地台账人数 + 特殊状态说明 + 份额制分摊公式（口径与申请表单 CalculateLandConfirmationArea 一致）
        if (economicDetail?.LandConfirmationGroups is { Count: > 0 })
        {
            var groups = economicDetail.LandConfirmationGroups;
            // 登记总人数取全部台账人员（含死亡继承/无土地权等特殊状态，不静默过滤）
            var totalPersonCount = groups.Sum(g => g.Persons.Count);
            var totalConfirmedArea = (decimal)groups.Sum(g => g.TotalArea);

            // 特殊状态人员说明（仅非零项展示）：死亡继承逐人点名继承人
            var specialParts = new List<string>();
            foreach (var group in groups)
            {
                foreach (var p in group.Persons)
                {
                    if (p.LandStatus == LandStatusConstants.DECEASED_INHERITANCE)
                    {
                        specialParts.Add(string.IsNullOrWhiteSpace(p.LandInheritTo)
                            ? $"{p.Name}死亡（继承人未定）"
                            : $"{p.Name}死亡由{p.LandInheritTo}继承");
                    }
                }
            }
            var noRightCount = groups.Sum(g => g.Persons.Count(p => p.LandStatus == LandStatusConstants.NO_LAND_RIGHT));
            var externalCount = groups.Sum(g => g.Persons.Count(p => p.LandStatus == LandStatusConstants.EXTERNAL_LAND));
            if (noRightCount > 0) specialParts.Add($"无土地权{noRightCount}人");
            if (externalCount > 0) specialParts.Add($"外来土地{externalCount}人");

            // 份额制分摊：有效总份额为分母，本户命中份额为分子
            decimal totalShares = 0, familyShares = 0;
            int familyPersonCount = 0;
            foreach (var group in groups)
            {
                var gShares = (decimal)group.TotalShares;
                if (gShares <= 0) continue;

                totalShares += gShares;
                foreach (var kv in LandShareCalculator.ComputeEffectiveShares(group))
                {
                    if (!familyNames.Contains(kv.Key.Trim())) continue;
                    familyShares += kv.Value;
                    familyPersonCount++;
                }
            }

            if (totalPersonCount > 0 && totalShares > 0 && totalConfirmedArea > 0)
            {
                var ratio = familyShares / totalShares;
                var calcArea = Math.Round(totalConfirmedArea * ratio, 2);
                var specialText = specialParts.Count > 0 ? $"（{string.Join("，", specialParts)}）" : string.Empty;
                sb.Append($"。土地台账共登记{totalPersonCount}人{specialText}，参与分摊{totalShares:0.##}份，" +
                          $"本户家庭成员{familyPersonCount}人计{familyShares:0.##}份，占总份额{ratio * 100:0.#}%，" +
                          $"即总台账面积{FormatDecimal(totalConfirmedArea)}亩÷{totalShares:0.##}份×{familyShares:0.##}份≈{FormatDecimal(calcArea)}亩");
            }
        }

        return sb.ToString();
    }

    private static string BuildVerifiedBenefitText(ApplicationEntity app)
    {
        var familyTypeName = ClassificationConstants.ConvertToFamilyTypeName(app.ClassificationResult ?? "");
        return $"经调查核实，该家庭符合{familyTypeName}条件，补助执行时间：";
    }

    /// <summary>
    /// 构建资产核查家庭成员名单文本（部省协查函）
    /// 格式：户主姓名；成员姓名（关系）、成员姓名（关系）…
    /// </summary>
    private string BuildAssetCheckFamilyMemberInfo(AssetVerificationDetail detail)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(detail.ApplicantName))
            parts.Add($"户主{detail.ApplicantName}");

        if (detail.FamilyMembers != null)
        {
            foreach (var m in detail.FamilyMembers)
            {
                if (m.IsHead || m.IdCard == detail.ApplicantIdCard || m.Name == detail.ApplicantName)
                    continue;

                var rel = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, m.Relationship ?? "");
                var relDisplay = string.IsNullOrEmpty(rel) ? m.Relationship ?? "" : rel;
                parts.Add($"{m.Name}（{relDisplay}）");
            }
        }

        return parts.Count > 0 ? string.Join("；", parts) : "-";
    }

    /// <summary>
    /// 构建户主及家庭成员状况文本（模板10：最低生活保障申请书）
    /// 格式：户主（姓名、年龄、健康状况、住房情况）；成员（姓名、关系、年龄、健康状况、就业状况）
    /// </summary>
    private string BuildFamilyMemberInfoText(ApplicationEntity app, List<FamilyMember> members)
    {
        var parts = new List<string>();

        // 户主
        var headGender = AddressResolver.ExtractGenderFromIdCard(app.ApplicantIdCard);
        var headAge = AddressResolver.ExtractAgeFromIdCard(app.ApplicantIdCard);
        // 健康状况翻译
        var healthDisplay = string.IsNullOrEmpty(app.HealthStatus) ? "" : $"，{_dictCacheService.GetValue(DictionaryTypeCodes.HealthStatuses, app.HealthStatus)}";
        parts.Add($"户主{app.ApplicantName}，{headGender}，{headAge}岁{healthDisplay}");

        // 其他家庭成员
        if (members != null)
        {
            foreach (var m in members)
            {
                if (m.IsHouseholdHead || m.IdCard == app.ApplicantIdCard)
                    continue;

                var gender = AddressResolver.ExtractGenderFromIdCard(m.IdCard);
                var age = AddressResolver.ExtractAgeFromIdCard(m.IdCard);
                var rel = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, m.RelationshipToHead);
                var emp = string.IsNullOrEmpty(m.EmploymentStatus) ? "" : $"，{_dictCacheService.GetValue(DictionaryTypeCodes.EmploymentStatuses, m.EmploymentStatus)}";
                var memberHealth = string.IsNullOrEmpty(m.HealthStatus) ? "" : $"，{_dictCacheService.GetValue(DictionaryTypeCodes.HealthStatuses, m.HealthStatus)}";
                parts.Add($"{m.Name}（{rel}），{gender}，{age}岁{memberHealth}{emp}");
            }
        }

        return parts.Count > 0 ? string.Join("；", parts) : "-";
    }

    /// <summary>
    /// 构建经济收入状况文本（模板10：最低生活保障申请书）
    /// </summary>
    private string BuildIncomeSituationText(ApplicationEntity app, EconomicDetailData? economicDetail)
    {
        var parts = new List<string>();

        // 户籍类型
        var hukouType = app.HukouType?.ToLowerInvariant() switch
        {
            "rural" => "农村",
            "urban" => "城市",
            _ => app.HukouType ?? ""
        };

        void AddMonthly(string label, decimal value)
        {
            if (value != 0)
                parts.Add($"{label}{FormatDecimal(value * 12)}元/年");
        }

        void AddAnnual(string label, decimal value)
        {
            if (value != 0)
                parts.Add($"{label}{FormatDecimal(value)}元/年");
        }

        // 收入构成（年值口径：月值项×12、年值项原样，构成之和 = 家庭年收入 TotalAnnualIncome）
        AddMonthly("务工收入", app.WorkIncomeTotal);
        AddMonthly("经营收入", app.BusinessIncomeTotal);
        AddMonthly("财产性收入", app.PropertyIncomeTotal);
        AddMonthly("转移性收入", app.TransferIncomeTotal);
        AddAnnual("赡养收入", app.AlimonyIncome);          // 赡养存储为年值
        AddMonthly("其他收入", app.OtherIncomeTotal);

        // 土地和补贴本身是年值
        AddAnnual("土地收入", app.LandIncomeTotal);
        AddAnnual("农业补贴", app.SubsidyTotal);

        // 合计（年值为权威口径，月值 = 年值÷12 分解显示）
        var totalAnnual = app.TotalAnnualIncome;
        var totalMonthly = app.TotalFamilyIncome;
        var perCapitaAnnual = app.PerCapitaAnnualIncome;
        parts.Add($"家庭月收入{FormatDecimal(totalMonthly)}元，家庭年收入{FormatDecimal(totalAnnual)}元");
        parts.Add($"人均年收入{FormatDecimal(perCapitaAnnual)}元");
        AddMonthly("刚性支出", app.RigidExpenditure);

        var text = parts.Count > 0 ? string.Join("；", parts) : "-";

        // 前缀：户籍类型说明
        return $"本人为{hukouType}户口，{text}。";
    }

    // ========================
    //  模板8（邻里访问调查表）复合字段构建
    // ========================

    /// <summary>
    /// 构建收入来源详细说明（模板8：邻里访问调查表"主要收入来源"字段）
    /// 逐条列出具体来源与金额，如"养老金7.12元/月、务工收入30元/月、土地收入5748元/年"
    /// </summary>
    private string BuildIncomeSourceSummary(ApplicationEntity app, EconomicDetailData? economicDetail)
    {
        var items = new List<string>();

        if (economicDetail != null)
        {
            // 转移性收入：具体到收入类型（如"养老金"）
            if (economicDetail.TransferIncomes != null)
                foreach (var t in economicDetail.TransferIncomes)
                {
                    var type = string.IsNullOrEmpty(t.IncomeType) ? "转移性收入" : t.IncomeType;
                    items.Add($"{type}{FormatDecimal((t.MonthlyAmount ?? 0) * 12)}元/年");
                }

            // 务工收入
            if (economicDetail.LaborIncomes != null)
                foreach (var l in economicDetail.LaborIncomes)
                    items.Add($"务工收入{FormatDecimal((l.MonthlyIncome ?? 0) * 12)}元/年");

            // 经营收入
            if (economicDetail.BusinessIncomes != null)
                foreach (var b in economicDetail.BusinessIncomes)
                    items.Add($"经营收入{FormatDecimal((b.MonthlyIncome ?? 0) * 12)}元/年");

            // 种植/养殖收入（年收入）
            if (economicDetail.BreedingIncomes != null)
                foreach (var br in economicDetail.BreedingIncomes)
                    items.Add($"{br.BreedingType ?? "种植"}收入{FormatDecimal(br.AnnualIncome)}元/年");

            // 其他收入
            if (economicDetail.OtherIncomes != null)
                foreach (var o in economicDetail.OtherIncomes)
                {
                    var type = string.IsNullOrEmpty(o.IncomeType) ? "其他收入" : o.IncomeType;
                    items.Add($"{type}{FormatDecimal(o.Amount * 12)}元/年");
                }
        }

        // 赡养费（年值）、土地收入（年值）、农业补贴（年值）来自申请汇总字段
        if (app.AlimonyIncome != 0)
            items.Add($"赡养费{FormatDecimal(app.AlimonyIncome)}元/年");
        if (app.LandIncomeTotal != 0)
            items.Add($"土地收入{FormatDecimal(app.LandIncomeTotal)}元/年");
        if (app.SubsidyTotal != 0)
            items.Add($"农业补贴{FormatDecimal(app.SubsidyTotal)}元/年");

        return items.Count > 0 ? string.Join("、", items) : "-";
    }

    private string BuildBreedingIncomeDesc(EconomicDetailData? economicDetail)
    {
        if (economicDetail?.BreedingIncomes == null || economicDetail.BreedingIncomes.Count == 0)
            return "-";

        var items = economicDetail.BreedingIncomes.Select(b =>
            $"{b.BreedingType ?? "种植"}：数量{b.Quantity}，收入{FormatDecimal(b.AnnualIncome)}元");
        return $"有：{string.Join("；", items)}";
    }

    private string BuildBusinessIncomeDesc(EconomicDetailData? economicDetail)
    {
        if (economicDetail?.BusinessIncomes == null || economicDetail.BusinessIncomes.Count == 0)
            return "-";

        var items = economicDetail.BusinessIncomes.Select(b =>
        {
            var vendorType = b.VendorType ?? "";
            var companyName = b.CompanyName ?? "";
            var namePart = (!string.IsNullOrEmpty(companyName), !string.IsNullOrEmpty(vendorType)) switch
            {
                (true, true) => $"{vendorType}（{companyName}）",
                (true, false) => companyName,
                (false, true) => vendorType,
                _ => "经营收入"
            };
            return $"经营{namePart}，月收入{FormatDecimal(b.MonthlyIncome ?? 0)}元";
        });
        return $"有：{string.Join("；", items)}";
    }

    private string BuildLaborIncomeDesc(EconomicDetailData? economicDetail)
    {
        if (economicDetail?.LaborIncomes == null || economicDetail.LaborIncomes.Count == 0)
            return "-";

        var items = economicDetail.LaborIncomes.Select(l =>
        {
            var workUnit = l.WorkUnit ?? "";
            var incomeType = l.IncomeSubType ?? "";
            var desc = (!string.IsNullOrEmpty(workUnit), !string.IsNullOrEmpty(incomeType)) switch
            {
                (true, true) => $"在{workUnit}从事{incomeType}工作",
                (true, false) => $"在{workUnit}工作",
                (false, true) => $"从事{incomeType}工作",
                _ => "务工收入"
            };
            return $"{desc}，月收入{FormatDecimal(l.MonthlyIncome ?? 0)}元，工作{l.MonthsWorked ?? 0}个月/年";
        });
        return $"有：{string.Join("；", items)}";
    }

    private string BuildFarmEquipmentDesc(EconomicDetailData? economicDetail)
    {
        if (economicDetail?.BreedingIncomes == null || economicDetail.BreedingIncomes.Count == 0)
            return "";

        var items = economicDetail.BreedingIncomes.Select(b =>
            $"{b.BreedingType ?? "种植"}，数量{b.Quantity}，年收入{FormatDecimal(b.AnnualIncome)}元");
        return string.Join("；", items);
    }

    private string BuildVehicleDesc(EconomicDetailData? economicDetail)
    {
        if (economicDetail?.Vehicles == null || economicDetail.Vehicles.Count == 0)
            return "";

        var items = economicDetail.Vehicles.Select(v =>
            $"{v.Brand} {v.Model}（{v.LicensePlate}），估值{FormatDecimal(v.EstimatedValue)}元");
        return string.Join("；", items);
    }

    private string BuildHouseDesc(EconomicDetailData? economicDetail)
    {
        if (economicDetail?.FamilyProperties == null || economicDetail.FamilyProperties.Count == 0)
            return "";

        var items = economicDetail.FamilyProperties.Select(p =>
            $"{p.HousingStructure ?? "未知"}结构，面积{FormatDecimal(p.Area)}㎡，{p.HousingNature ?? "未知"}");
        return string.Join("；", items);
    }

    private string BuildGuaranteeTypeDisplay(string classificationResult)
    {
        if (string.IsNullOrEmpty(classificationResult)) return "-";

        if (classificationResult.Contains("Subsistence"))
            return "保障金";
        if (classificationResult.Contains("Destitute"))
            return "供养金";
        if (classificationResult.Contains("LowIncome") || classificationResult.Contains("LowIncomeSingle"))
            return "-";

        return "-";
    }

    /// <summary>
    /// 渐退期审批表「退出渐退期情况」分型拼句（仅本户存在渐退记录才产出；仍在渐退留空）。
    /// 日期统一用复核日 change_date；句式 A–D 见 docs/20260924_业务节点文书直出规范.md 同类约定。
    /// 取数失败时返回空串（不阻断整表预览），业务日志已由服务层记录。
    /// </summary>
    private async Task<string> BuildGraceExitSituationAsync(
        ApplicationEntity app,
        GracePeriodRecord grace)
    {
        // E：仍在渐退（记录有效且档案未停）→ 留空供后续手写
        if (grace.IsActive && app.Status != ApplicationStatusCodes.STOPPED)
            return "";

        try
        {
            var exitRes = await _changeService.GetLatestGraceExitChangeAsync(app.Id, CancellationToken);
            var exit = exitRes.IsSuccess ? exitRes.Value : null;

            // A：经济复核 + 触发停保 + 新分类属停保族
            if (exit != null
                && exit.ChangeReasonType == ChangeReasonTypeConstants.EconomicReview
                && exit.TriggeredStop == true
                && !string.IsNullOrEmpty(exit.NewClassification)
                && ClassificationConstants.IsCodeStop(exit.NewClassification))
            {
                var d = exit.ChangeDate?.ToString("yyyy年M月d日") ?? app.StopDate.ToString("yyyy年M月d日");
                return $"于{d}进行经济复核，经审核，家庭经济状况超过保障标准，依据相关规定，经批准，予以停止保障。";
            }

            // B：经济复核但未触发停保（换类/降档等退出渐退）
            if (exit != null && exit.ChangeReasonType == ChangeReasonTypeConstants.EconomicReview)
            {
                var d = exit.ChangeDate?.ToString("yyyy年M月d日") ?? app.StopDate.ToString("yyyy年M月d日");
                return $"于{d}进行经济复核，家庭经济状况发生变化，按规定退出渐退期。";
            }

            // C：户主死亡停保
            if ((exit != null && exit.ChangeReasonType == ChangeReasonTypeConstants.HeadDeceased)
                || app.StopReason == ChangeReasonTypeConstants.HeadDeceased)
            {
                var d = exit?.ChangeDate?.ToString("yyyy年M月d日")
                    ?? (app.StopDate != default ? app.StopDate.ToString("yyyy年M月d日") : "");
                if (string.IsNullOrEmpty(d)) return "";
                return $"于{d}原户主死亡，依据相关规定，经批准，予以停止保障。";
            }

            // D：其他停保（有 stop_reason）
            if (app.Status == ApplicationStatusCodes.STOPPED && !string.IsNullOrEmpty(app.StopReason))
            {
                var d = exit?.ChangeDate?.ToString("yyyy年M月d日")
                    ?? (app.StopDate != default ? app.StopDate.ToString("yyyy年M月d日") : "");
                if (string.IsNullOrEmpty(d)) return "";
                return $"于{d}因{app.StopReason}，经批准，予以停止保障。";
            }

            return "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成退出渐退期情况失败");
            return "";
        }
    }

    /// <summary>
    /// 渐退期审批表「变动情况说明」分句拼装（家庭人员/收入/享受类别/保障金/财产状况，分号连接、句号收尾）。
    /// 停旧建新链（OriginalApplicationId>0）：人口与家庭年收入取旧档对比，括号内注明原户主停保原因；
    /// 经济复核同档：人口无变化省略该分句，月人均收入取变更记录新旧值；
    /// 旧档读取/变更查询失败仅跳过对应分句（LogWarn），类别/保障金/财产照常输出——不吞整段。
    /// </summary>
    private async Task<string> BuildGraceChangeDetailAsync(ApplicationEntity app, GracePeriodRecord grace)
    {
        var parts = new List<string>();

        if (app.OriginalApplicationId > 0)
        {
            var oldRes = await _applicationService.GetByIdAsync(app.OriginalApplicationId, CancellationToken);
            var old = oldRes.IsSuccess ? oldRes.Value : null;
            if (old == null)
            {
                _logger.LogBusiness("变动情况说明-旧档读取失败降级", ("ApplicationId", app.Id.ToString()), ("OriginalApplicationId", app.OriginalApplicationId.ToString()));
            }
            else
            {
                // 总额/分项/人口对比旧值：Before 快照优先（经济复核链旧档行已被表单覆写为新值，不可信）；
                // 死亡/户主变更链旧档未被编辑，无快照时旧档行即真旧值。
                BeforeSnapshotOldValues? snap = null;
                var snapRes = await _changeService.GetLatestBeforeSnapshotAsync(app.OriginalApplicationId, CancellationToken);
                if (snapRes.IsFailure)
                {
                    _logger.LogBusiness("变动情况说明-快照旧值查询失败降级", ("ApplicationId", app.Id.ToString()), ("ErrorCode", snapRes.ErrorCode ?? ""));
                }
                else
                {
                    snap = snapRes.Value;
                }

                var oldFamilySize = snap?.OldFamilySize ?? old.FamilySize;
                if (oldFamilySize != app.FamilySize)
                {
                    var stopDay = old.StopDate != default ? old.StopDate.ToString("yyyy年M月d日") : "";
                    string cause;
                    if (old.StopReason == ChangeReasonTypeConstants.HeadDeceased)
                    {
                        cause = string.IsNullOrEmpty(stopDay) ? "原户主死亡" : $"原户主{old.ApplicantName}于{stopDay}死亡";
                    }
                    else if (old.StopReason.Contains("经济复核"))
                    {
                        // 复核链户主未变，旧档停止仅是停旧建新，不能写"停止保障"
                        cause = "经济复核后重新认定";
                    }
                    else
                    {
                        cause = string.IsNullOrEmpty(stopDay) ? "原户主停止保障" : $"原户主{old.ApplicantName}于{stopDay}停止保障";
                    }
                    parts.Add($"家庭人口由{oldFamilySize}人变为{app.FamilySize}人（{cause}）");
                }

                var incomePart = BuildAnnualIncomeComparisonPart(snap, old, app);
                if (!string.IsNullOrEmpty(incomePart))
                {
                    parts.Add(incomePart);
                }
            }
        }
        else
        {
            // 经济复核同档更新：旧人均收入仅存于变更记录
            var compRes = await _changeService.GetLatestIncomeComparisonAsync(app.Id, CancellationToken);
            if (compRes.IsFailure)
            {
                _logger.LogBusiness("变动情况说明-人均收入查询失败降级", ("ApplicationId", app.Id.ToString()), ("ErrorCode", compRes.ErrorCode ?? ""));
            }
            else if (compRes.Value is { } comp
                && comp.OldPerCapitaIncome.HasValue && comp.NewPerCapitaIncome.HasValue
                && comp.OldPerCapitaIncome.Value != comp.NewPerCapitaIncome.Value)
            {
                parts.Add($"月人均收入由{FormatDecimal(comp.OldPerCapitaIncome.Value)}元变为{FormatDecimal(comp.NewPerCapitaIncome.Value)}元");
            }
        }

        var oldClass = ClassificationConstants.ConvertFromCode(grace.OriginalClassification ?? "");
        var newClass = ClassificationConstants.ConvertFromCode(app.ClassificationResult ?? "");
        if (!string.IsNullOrEmpty(oldClass) && !string.IsNullOrEmpty(newClass))
        {
            parts.Add($"享受类别由{oldClass}调整为{newClass}");
        }

        if (grace.OriginalGuaranteeAmount is > 0)
        {
            var newAmt = grace.GraceGrantAmount ?? app.HouseholdMonthlyGuaranteeAmount;
            parts.Add($"月保障金由{FormatDecimal(grace.OriginalGuaranteeAmount.Value)}元调整为{FormatDecimal(newAmt)}元");
        }

        parts.Add("财产状况无变化");
        return string.Join("；", parts) + "。";
    }

    /// <summary>
    /// 拼「家庭年收入由X变为Y，其中…（收入分项对比）」分句；总额与分项均无变化时返回 null。
    /// 旧值来源：Before 快照 Components（复核链真旧值）优先，回退旧档行分项（死亡/户主变更链真旧值）。
    /// 口径统一为年值：务工/经营/财产性/转移性/其他/刚性支出为月值×12，土地收入/农业补贴/赡养费收入为年值原样；
    /// 复核链历史记录（旧档已被覆写、无快照）旧源与新档全等且总额相等 → 不输出，避免误导性"无变化"。
    /// </summary>
    private static string? BuildAnnualIncomeComparisonPart(BeforeSnapshotOldValues? snap, ApplicationEntity old, ApplicationEntity app)
    {
        var oldAnnual = snap?.OldTotalAnnualIncome ?? old.TotalAnnualIncome;
        var totalChanged = oldAnnual != app.TotalAnnualIncome;

        var oldItems = snap?.Components ?? new IncomeComponentValues
        {
            WorkIncomeTotal = old.WorkIncomeTotal,
            BusinessIncomeTotal = old.BusinessIncomeTotal,
            PropertyIncomeTotal = old.PropertyIncomeTotal,
            TransferIncomeTotal = old.TransferIncomeTotal,
            OtherIncomeTotal = old.OtherIncomeTotal,
            RigidExpenditure = old.RigidExpenditure,
            AlimonyIncome = old.AlimonyIncome,
            LandIncomeTotal = old.LandIncomeTotal,
            SubsidyTotal = old.SubsidyTotal
        };
        var newItems = new IncomeComponentValues
        {
            WorkIncomeTotal = app.WorkIncomeTotal,
            BusinessIncomeTotal = app.BusinessIncomeTotal,
            PropertyIncomeTotal = app.PropertyIncomeTotal,
            TransferIncomeTotal = app.TransferIncomeTotal,
            OtherIncomeTotal = app.OtherIncomeTotal,
            RigidExpenditure = app.RigidExpenditure,
            AlimonyIncome = app.AlimonyIncome,
            LandIncomeTotal = app.LandIncomeTotal,
            SubsidyTotal = app.SubsidyTotal
        };

        (string Label, decimal Old, decimal New)[] defs =
        {
            ("务工收入", oldItems.WorkIncomeTotal * 12, newItems.WorkIncomeTotal * 12),
            ("经营收入", oldItems.BusinessIncomeTotal * 12, newItems.BusinessIncomeTotal * 12),
            ("财产性收入", oldItems.PropertyIncomeTotal * 12, newItems.PropertyIncomeTotal * 12),
            ("转移性收入", oldItems.TransferIncomeTotal * 12, newItems.TransferIncomeTotal * 12),
            ("其他收入", oldItems.OtherIncomeTotal * 12, newItems.OtherIncomeTotal * 12),
            ("土地收入", oldItems.LandIncomeTotal, newItems.LandIncomeTotal),
            ("农业补贴", oldItems.SubsidyTotal, newItems.SubsidyTotal),
            ("赡养费收入", oldItems.AlimonyIncome, newItems.AlimonyIncome),
            ("刚性支出", oldItems.RigidExpenditure * 12, newItems.RigidExpenditure * 12),
        };

        var changed = new List<string>();
        var unchanged = new List<string>();
        foreach (var (label, oldValue, newValue) in defs)
        {
            if (oldValue != newValue)
            {
                changed.Add($"{label}由{FormatDecimal(oldValue)}元变为{FormatDecimal(newValue)}元");
            }
            else
            {
                unchanged.Add(label);
            }
        }

        if (!totalChanged && changed.Count == 0)
        {
            return null;
        }

        var detail = new List<string>();
        if (changed.Count > 0)
        {
            detail.Add(string.Join("，", changed));
            if (unchanged.Count > 0)
            {
                detail.Add($"其余分项（{string.Join("、", unchanged)}）无变化");
            }
        }

        if (totalChanged)
        {
            var head = $"家庭年收入由{FormatDecimal(oldAnnual)}元变为{FormatDecimal(app.TotalAnnualIncome)}元";
            if (detail.Count > 0)
            {
                return head + "，其中" + string.Join("，", detail);
            }
            return head + "，各收入分项无变化";
        }

        return "收入分项中" + string.Join("，", detail);
    }

    /// <summary>
    /// 生成收入超标停保的详细经济理由（H4：低保标准单点读 IStandardConfigService，
    /// 缺失/无效时抛 CONFIG_NOT_FOUND，禁止硬编码 632/832 回退值写入文书）。
    /// </summary>
    private async Task<string> BuildIncomeExceededReasonAsync(ApplicationEntity app, string? newClassification)
    {
        var sb = new System.Text.StringBuilder();
        var familySize = app.FamilySize;
        var totalIncome = app.TotalFamilyIncome;
        var perCapitaIncome = app.PerCapitaIncome;
        var rigidExpenditure = app.RigidExpenditure;
        var isRural = ClassificationConstants.HukouType.IsHukouRural(app.HukouType);

        sb.Append($"经核算，您家庭{familySize}口人，家庭月总收入{totalIncome:N2}元，");
        sb.Append($"人均月收入{perCapitaIncome:N2}元。");

        var stdType = isRural ? "RuralSubsistenceStandard" : "UrbanSubsistenceStandard";
        var stdHukou = isRural ? "Rural" : "Urban";
        var stdResult = await _standardConfigService.GetStandardValueAsync(stdType, stdHukou);
        if (!stdResult.IsSuccess || stdResult.Value <= 0)
        {
            _logger.Error($"停保告知书：低保标准配置缺失或无效（{stdType}/{stdHukou}），禁止使用硬编码回退值");
            return $"低保标准配置缺失或无效（{stdType}/{stdHukou}），请在「数据中心-标准配置管理」中维护有效标准后重新生成停保告知书。";
        }
        var standard = stdResult.Value;
        var threshold = standard * ClassificationConstants.LowIncomeMultiplier;

        sb.Append($"{(isRural ? "农村" : "城市")}低保标准为{standard:N0}元/月，");
        sb.Append($"低收入标准上限为{threshold:N0}元/月（{standard:N0}×1.5）。");
        sb.Append($"您家庭人均月收入{perCapitaIncome:N2}元已超过低收入标准上限{threshold:N2}元，");

        // 刚性支出分析
        if (rigidExpenditure > 0)
        {
            var perCapitaRigid = rigidExpenditure / familySize;
            var ratio = perCapitaIncome > 0 ? perCapitaRigid / perCapitaIncome : 0;
            sb.Append($"虽有刚性支出{rigidExpenditure:N2}元/月（占比{ratio:P0}），");

            if (ratio <= 0.5m)
                sb.Append("但占比未超过50%，不满足刚性支出困难家庭条件。");
            else if (ratio >= 0.6m)
                sb.Append("但占比已超过60%，不满足刚性支出困难家庭条件。");
            else if (perCapitaIncome >= standard * 2)
                sb.Append("但人均收入超过低保标准2倍，不满足刚性支出困难家庭条件。");
            else
                sb.Append("扣除刚性支出后人均收入不足低保标准，不满足刚性支出困难家庭条件。");
        }
        else
        {
            sb.Append("且无刚性支出，不满足刚性支出困难家庭条件。");
        }

        sb.Append("根据《牡丹江市低收入人口认定实施细则》（牡民规〔2024〕2号）相关规定，不符合低收入人口认定条件。");

        return sb.ToString();
    }
}
