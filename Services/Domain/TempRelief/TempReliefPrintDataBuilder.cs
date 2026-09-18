using System.Linq;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Domain.TempRelief;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Services.Domain.TempRelief;

/// <summary>
/// 临时救助打印数据组装（填充 PrintNavigationData.FieldData / TableData）
/// 字段键见 Constants/FieldKeys.cs 的 TEMP_* 常量；模板 config_json 将中文占位符映射到这些键。
/// </summary>
public static class TempReliefPrintDataBuilder
{
    /// <summary>授权承诺书家庭成员槽位数（_1.._6）</summary>
    private const int ArchiveMemberSlots = 6;

    /// <summary>
    /// 构建单值字段字典（主表 + 家庭成员索引槽位）
    /// </summary>
    /// <param name="app">临时救助申请实体</param>
    /// <param name="members">家庭成员明细</param>
    /// <param name="contactUnitPhone">公示异议反馈电话（当前登录用户所在单位联系电话）</param>
    /// <param name="acceptanceDate">验收日期（C线：公示结束次一个工作日；为空则回退公示结束日/当天）</param>
    /// <param name="investigationDate">入户调查日期（C线调查核实窗口首日；为空则不输出）</param>
    public static Dictionary<string, string> BuildSingleFields(
        TempReliefApplication app,
        List<TempReliefMember> members,
        string? contactUnitPhone = null,
        DateTime? acceptanceDate = null,
        DateTime? investigationDate = null)
    {
        var acceptance = acceptanceDate ?? app.PublicizeEndDate ?? DateTime.Today;

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FieldKeys.TEMP_APPLICATION_NO] = app.ApplicationNo,
            [FieldKeys.TEMP_RELIEF_TYPE_NAME] = TempReliefConstants.GetReliefTypeName(app.ReliefType),
            [FieldKeys.TEMP_REPORT_UNIT] = app.ReportUnit,
            [FieldKeys.TEMP_REPORT_TIME] = app.ReportTime?.ToString("yyyy年M月d日") ?? string.Empty,
            [FieldKeys.TEMP_APPLICANT_NAME] = app.ApplicantName,
            [FieldKeys.TEMP_APPLICANT_GENDER] = app.Gender,
            [FieldKeys.TEMP_APPLICANT_AGE] = app.Age?.ToString() ?? string.Empty,
            [FieldKeys.TEMP_APPLICANT_ID_CARD] = app.ApplicantIdCard,
            [FieldKeys.TEMP_APPLICANT_PHONE] = app.Phone,
            [FieldKeys.FAMILY_ADDRESS] = BuildLivingAddress(app),
            [FieldKeys.TEMP_FAMILY_ADDRESS] = BuildLivingAddress(app),
            [FieldKeys.TEMP_HUKOU_ADDRESS] = app.HukouAddress,
            [FieldKeys.TEMP_POLICY_ENJOYED] = app.PolicyEnjoyed,
            [FieldKeys.TEMP_POLICY_CHECKLIST] = BuildPolicyChecklist(app.PolicyEnjoyed),
            [FieldKeys.TEMP_FAMILY_MEMBER_STATUS] = BuildFamilyMemberStatus(app),
            [FieldKeys.TEMP_DIFFICULTY_REASON] = BuildDifficultyReason(app),
            [FieldKeys.TEMP_APPLY_STATEMENT] = app.DifficultyReason ?? string.Empty,
            [FieldKeys.TEMP_ACCEPTANCE_REASON] = BuildAcceptanceReason(app),
            [FieldKeys.TEMP_DIFFICULTY_SUMMARY] = BuildDifficultySummary(app),
            [FieldKeys.TEMP_AUDIT_REASON] = BuildAuditReason(app),
            [FieldKeys.TEMP_BENEFICIARY_NAME] = ResolveBeneficiaryName(app),
            [FieldKeys.TEMP_BENEFICIARY_ID_CARD] = FirstNonEmpty(app.BeneficiaryIdCard, app.ApplicantIdCard),
            [FieldKeys.TEMP_BENEFICIARY_GENDER] = FirstNonEmpty(app.BeneficiaryGender, app.Gender),
            [FieldKeys.TEMP_BENEFICIARY_AGE] = (app.BeneficiaryAge ?? app.Age)?.ToString() ?? string.Empty,
            [FieldKeys.TEMP_BENEFICIARY_RELATION] = app.BeneficiaryRelation ?? string.Empty,
            [FieldKeys.TEMP_DIFFICULTY_TYPE] = string.IsNullOrWhiteSpace(app.DifficultyType)
                ? TempReliefConstants.DifficultyTypeOther : app.DifficultyType,
            [FieldKeys.TEMP_DIFFICULTY_DETAILS] = BuildDifficultyDetails(app),
            [FieldKeys.TEMP_DISEASE_DIAGNOSIS] = JoinDiseaseFields(app.Diseases, d => d.DiseaseName),
            [FieldKeys.TEMP_DISEASE_CODE] = JoinDiseaseFields(app.Diseases, d => d.DiseaseCode),
            [FieldKeys.TEMP_DISEASE_SELF_PAID] = FormatDiseaseSelfPaid(app.Diseases),
            [FieldKeys.TEMP_DISEASE_HOSPITAL] = FormatDiseaseHospital(app.Diseases),
            [FieldKeys.TEMP_ACCIDENT_DETAILS] = BuildAccidentDetails(app.Accidents),
            [FieldKeys.TEMP_EDUCATION_DETAILS] = BuildEducationDetails(app.Educations),
            [FieldKeys.TEMP_APPLY_DATE] = app.ApplyDate?.ToString("yyyy年M月d日") ?? string.Empty,
            [FieldKeys.TEMP_FAMILY_SIZE] = app.FamilySize?.ToString() ?? string.Empty,
            [FieldKeys.TEMP_FAMILY_CATEGORY] = app.FamilyCategory,
            [FieldKeys.TEMP_VERIFY_RESULT] = "情况属实",
            [FieldKeys.TEMP_CONFIRM_AMOUNT] = app.ReliefType == TempReliefConstants.ReliefTypeSmall && app.ConfirmAmount.HasValue
                ? app.ConfirmAmount.Value.ToString("F2") : new string(' ', 12),
            [FieldKeys.TEMP_PARENT_UNIT] = app.ReliefType == TempReliefConstants.ReliefTypeLarge
                ? "县民政部门意见" : "镇政府意见",
            [FieldKeys.TEMP_FAMILY_CATEGORY_CHECKLIST] = BuildAuditFamilyCategoryChecklist(app),
            [FieldKeys.TEMP_ACCEPTANCE_DATE] = acceptance.ToString("yyyy年M月d日"),
            [FieldKeys.TEMP_ACCEPTANCE_CONCLUSION] = BuildAcceptanceConclusion(app, acceptance),
            [FieldKeys.TEMP_INVESTIGATION_DATE] = investigationDate?.ToString("yyyy年M月d日") ?? string.Empty,
            [FieldKeys.TEMP_TOWN_OPINION_DATE] = FormatCnDate(app.PublicizeEndDate),
            [FieldKeys.TEMP_PUBLICIZE_RANGE] = BuildPublicizeRange(app),
            [FieldKeys.TEMP_PUBLICIZE_START] = FormatCnDate(app.PublicizeStartDate),
            [FieldKeys.TEMP_PUBLICIZE_END] = FormatCnDate(app.PublicizeEndDate),
            [FieldKeys.TEMP_VILLAGE] = app.Village,
            [FieldKeys.TEMP_TOWN] = app.Town,
            [FieldKeys.TEMP_ACCEPTANCE_PERSON] = app.AcceptancePerson,
            [FieldKeys.TEMP_CONTACT_PHONE] = contactUnitPhone ?? string.Empty,

            // ── 通用档案键：档案_授权承诺书_P1（即通用_授权委托书）复用临时救助打印流 ──
            [FieldKeys.APPLICANT_NAME] = app.ApplicantName,
            [FieldKeys.APPLICANT_ID_CARD] = app.ApplicantIdCard,
            [FieldKeys.APPLICATION_REASON] = string.IsNullOrWhiteSpace(app.DifficultyType)
                ? TempReliefConstants.DifficultyTypeOther : app.DifficultyType,
            [FieldKeys.APPLICATION_DATE] = app.ApplyDate?.ToString("yyyy-MM-dd") ?? string.Empty,

            // ── 通用承诺书键：通用_申请人承诺书(174) / 通用_经办人承诺书(178) 复用临时救助打印流 ──
            [FieldKeys.HUKOU_ADDRESS] = app.HukouAddress,
            [FieldKeys.APPLICANT_COUNT] = app.FamilySize?.ToString() ?? string.Empty,
            [FieldKeys.OPERATOR_UNIT] = app.ReportUnit,
            [FieldKeys.OPERATOR_NAME] = app.AcceptancePerson,

            [FieldKeys.AGENT_NAME] = string.Empty,
            [FieldKeys.AGENT_CERT_TYPE] = string.Empty,
            [FieldKeys.AGENT_ID_CARD] = string.Empty,
            [FieldKeys.AGENT_RELATION] = string.Empty,

            // ── 档案封面专用字段 ──
            [FieldKeys.COVER_CLASSIFICATION] = $"临时救助（{TempReliefConstants.GetReliefTypeName(app.ReliefType)}）",
            [FieldKeys.ARCHIVE_NUMBER] = app.ApplicationNo,
            [FieldKeys.COPYRIGHT_INFO] = CopyrightHelper.BuildCopyrightInfo(app.ApplicantIdCard)
        };

        // 家庭成员索引槽位（审批表 5 行）
        if (members != null)
        {
            for (var i = 0; i < TempReliefConstants.MaxMemberRows; i++)
            {
                var idx = i + 1;
                var member = i < members.Count ? members[i] : null;
                fields[FieldKeys.TEMP_MEMBER_NAME + "_" + idx] = member?.MemberName ?? string.Empty;
                fields[FieldKeys.TEMP_MEMBER_GENDER + "_" + idx] = member?.Gender ?? string.Empty;
                fields[FieldKeys.TEMP_MEMBER_RELATION + "_" + idx] = member?.Relation ?? string.Empty;
                fields[FieldKeys.TEMP_MEMBER_ID_CARD + "_" + idx] = member?.IdCard ?? string.Empty;
                fields[FieldKeys.TEMP_MEMBER_WORK_UNIT + "_" + idx] = member?.WorkUnit ?? string.Empty;
                fields[FieldKeys.TEMP_MEMBER_ANNUAL_INCOME + "_" + idx] = member?.AnnualIncome?.ToString("F2") ?? string.Empty;
            }
        }

        // 授权承诺书家庭成员槽位（1..6）：户主（申请人）恒为索引 1（与 FamilyFieldBuilder 约定一致），
        // Members 依次占槽位 2..6（表单成员不含户主，最多 5 人）；证件类型固定居民身份证。
        // 不依赖 Members 是否为空：无成员数据时仍输出户主一行
        var archiveMembers = new List<(string? Name, string Gender, string Relation, string IdCard)>(ArchiveMemberSlots)
        {
            (app.ApplicantName, app.Gender, TempReliefConstants.RelationHead, app.ApplicantIdCard)
        };
        if (members != null)
        {
            foreach (var m in members.Take(ArchiveMemberSlots - 1))
                archiveMembers.Add((m.MemberName, m.Gender, m.Relation, m.IdCard));
        }
        while (archiveMembers.Count < ArchiveMemberSlots)
            archiveMembers.Add((null, string.Empty, string.Empty, string.Empty));

        for (var i = 0; i < ArchiveMemberSlots; i++)
        {
            var idx = i + 1;
            var slot = archiveMembers[i];
            fields[FieldKeys.FAMILY_MEMBER_NAME + "_" + idx] = slot.Name ?? string.Empty;
            fields[FieldKeys.FAMILY_MEMBER_CERT_TYPE + "_" + idx] = slot.Name == null ? string.Empty : "居民身份证";
            fields[FieldKeys.FAMILY_MEMBER_ID_CARD + "_" + idx] = slot.IdCard ?? string.Empty;
            fields[FieldKeys.FAMILY_MEMBER_RELATION + "_" + idx] = slot.Relation ?? string.Empty;
        }

        return fields;
    }

    private static string FormatCnDate(DateTime? dt) => dt?.ToString("yyyy年M月d日") ?? string.Empty;

    /// <summary>
    /// 解析打印用日期：验收日（= 公示结束的次一个工作日）与入户调查日期（= C线调查核实窗口首日）。
    /// 月份以记录公示开始日（无则公示结束日/申请日/当天）为锚；调查核实期按家庭类别区分特殊5/普通10个工作日。
    /// 打印调用方统一走此方法，避免各自重抄。
    /// </summary>
    public static async Task<(DateTime Acceptance, DateTime Investigation)> ResolveScheduleAsync(
        IBusinessTimelineService timelineService, TempReliefApplication app)
    {
        var acceptance = timelineService.GetTempReliefAcceptanceDate(app.PublicizeEndDate);

        var simplified = TempReliefConstants.IsSimplifiedProcedure(app.FamilyCategory, app.SourceTable);
        var anchor = app.PublicizeStartDate ?? app.PublicizeEndDate ?? app.ApplyDate ?? DateTime.Today;
        var cLine = await timelineService.CalculateTempReliefAsync(anchor.Year, anchor.Month, simplified);

        return (acceptance, cLine.InvestigationStartDate);
    }

    /// <summary>
    /// 验收结论整句：{因病|因学|因灾|因突发困难}，于{公示起}至{公示止}公示，{验收日}验收通过。
    /// 公示日期缺失一侧时只输出已有侧；两侧皆空时省略"于…公示"分句。
    /// </summary>
    private static string BuildAcceptanceConclusion(TempReliefApplication app, DateTime acceptanceDate)
    {
        var reasonWord = (app.DifficultyType ?? string.Empty).Trim() switch
        {
            TempReliefConstants.DifficultyTypeDisease => "因病",
            TempReliefConstants.DifficultyTypeEducation => "因学",
            TempReliefConstants.DifficultyTypeAccident => "因灾",
            _ => "因突发困难"
        };

        var start = FormatCnDate(app.PublicizeStartDate);
        var end = FormatCnDate(app.PublicizeEndDate);
        var rangeText = (string.IsNullOrEmpty(start), string.IsNullOrEmpty(end)) switch
        {
            (true, true) => string.Empty,
            (true, false) => end,
            (false, true) => start,
            _ => $"{start}至{end}"
        };

        var acceptanceText = acceptanceDate.ToString("yyyy年M月d日");
        return string.IsNullOrEmpty(rangeText)
            ? $"{reasonWord}，{acceptanceText}验收通过。"
            : $"{reasonWord}，于{rangeText}公示，{acceptanceText}验收通过。";
    }

    /// <summary>
    /// 审核审批表{户主家庭类别}勾选清单：9 项固定清单，命中项 □→☑；
    /// 命中规则见 TempReliefConstants.ResolveAuditChecklistCategory（指定字段固定映射）。
    /// 排版 5+4 两行，换行符由 ExcelEngine 自动启用单元格自动换行。
    /// </summary>
    private static string BuildAuditFamilyCategoryChecklist(TempReliefApplication app)
    {
        var hit = TempReliefConstants.ResolveAuditChecklistCategory(app.FamilyCategory, app.SourceTable, app.HukouType);
        var options = TempReliefConstants.AuditChecklistOptions;
        var line1 = new List<string>(5);
        var line2 = new List<string>(4);
        for (var i = 0; i < options.Length; i++)
        {
            var item = $"{(options[i] == hit ? "☑" : "□")}{options[i]}";
            (i < 5 ? line1 : line2).Add(item);
        }
        return string.Join("  ", line1) + Environment.NewLine + string.Join("  ", line2);
    }

    /// <summary>
    /// 公示日期区间（完整中文格式，如 2026年8月10日至2026年8月12日；单边时仅输出已有日期）
    /// </summary>
    private static string BuildPublicizeRange(TempReliefApplication app)
    {
        var start = FormatCnDate(app.PublicizeStartDate);
        var end = FormatCnDate(app.PublicizeEndDate);
        if (string.IsNullOrEmpty(start) && string.IsNullOrEmpty(end)) return string.Empty;
        if (string.IsNullOrEmpty(end)) return start;
        if (string.IsNullOrEmpty(start)) return end;
        return $"{start} 至 {end}";
    }

    /// <summary>
    /// 家庭成员状态详细文本：成员身体状况 + 申请人所报困难明细（疾病/意外灾害/教育支出），空段落省略
    /// </summary>
    private static string BuildFamilyMemberStatus(TempReliefApplication app)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(app.FamilyMemberStatus))
            parts.Add($"家庭成员身体状况：{app.FamilyMemberStatus}");

        var diseaseText = BuildDiseaseBrief(app.Diseases);
        if (!string.IsNullOrWhiteSpace(diseaseText)) parts.Add($"申请人患病情况：{diseaseText}");

        var accidentText = BuildAccidentBrief(app.Accidents);
        if (!string.IsNullOrWhiteSpace(accidentText)) parts.Add($"意外灾害：{accidentText}");

        var educationText = BuildEducationBrief(app.Educations);
        if (!string.IsNullOrWhiteSpace(educationText)) parts.Add($"教育支出：{educationText}");

        return string.Join("；", parts);
    }

    /// <summary>
    /// 申请救助原因/困难情况：原救助原因 + 家庭成员状态详细内容（审批表/公示/验收报告共用）
    /// </summary>
    private static string BuildDifficultyReason(TempReliefApplication app)
    {
        var memberStatus = BuildFamilyMemberStatus(app);
        if (string.IsNullOrWhiteSpace(app.DifficultyReason)) return memberStatus;
        if (string.IsNullOrWhiteSpace(memberStatus)) return app.DifficultyReason;
        return $"{app.DifficultyReason}\n{memberStatus}";
    }

    private static string BuildDiseaseBrief(List<TempReliefDisease>? diseases)
    {
        if (diseases == null || diseases.Count == 0) return string.Empty;
        var idx = 1;
        var parts = new List<string>();
        foreach (var d in diseases)
        {
            var owner = string.IsNullOrWhiteSpace(d.MemberName) ? "" : d.MemberName.Trim() + " ";
            var seg = $"【疾病{idx}】{owner}{d.DiseaseName}".Trim();
            if (!string.IsNullOrWhiteSpace(d.DiseaseCode)) seg += $"（编码 {d.DiseaseCode}）";
            parts.Add(seg);
            idx++;
        }
        return string.Join("、", parts);
    }

    private static string BuildAccidentBrief(List<TempReliefAccident>? accidents)
    {
        if (accidents == null || accidents.Count == 0) return string.Empty;
        var idx = 1;
        var parts = new List<string>();
        foreach (var a in accidents)
        {
            var owner = string.IsNullOrWhiteSpace(a.MemberName) ? "" : a.MemberName.Trim() + " ";
            var seg = $"【意外灾害{idx}】{owner}{a.AccidentType}".Trim();
            if (!string.IsNullOrWhiteSpace(a.InjurySituation)) seg += $"：{a.InjurySituation}";
            parts.Add(seg);
            idx++;
        }
        return string.Join("、", parts);
    }

    private static string BuildEducationBrief(List<TempReliefEducation>? educations)
    {
        if (educations == null || educations.Count == 0) return string.Empty;
        var idx = 1;
        var parts = new List<string>();
        foreach (var e in educations)
        {
            var seg = $"【教育支出{idx}】{e.StudentName}（{e.EducationStage}）".Trim();
            parts.Add(seg);
            idx++;
        }
        return string.Join("、", parts);
    }

    private static string JoinDiseaseFields(List<TempReliefDisease>? diseases, Func<TempReliefDisease, string> selector)
    {
        if (diseases == null || diseases.Count == 0) return string.Empty;
        return string.Join("；", diseases.Select(selector).Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    /// <summary>疾病自付费用：多条时求和汇总</summary>
    private static string FormatDiseaseSelfPaid(List<TempReliefDisease>? diseases)
    {
        if (diseases == null || diseases.Count == 0) return string.Empty;
        var total = DistinctDiseasesByExpense(diseases)
            .Where(d => d.SelfPaid.HasValue)
            .Sum(d => d.SelfPaid!.Value);
        return total > 0 ? total.ToString("F2") : string.Empty;
    }

    /// <summary>疾病所在医院：单条取值，多条去重拼接</summary>
    private static string FormatDiseaseHospital(List<TempReliefDisease>? diseases)
    {
        if (diseases == null || diseases.Count == 0) return string.Empty;
        var hospitals = diseases
            .Select(d => d.Hospital ?? string.Empty)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return hospitals.Count switch
        {
            0 => string.Empty,
            1 => hospitals[0],
            _ => string.Join("/", hospitals)
        };
    }

    private static string BuildDifficultyDetails(TempReliefApplication app)
    {
        var parts = new List<string>();
        if (app.Diseases != null && app.Diseases.Count > 0)
        {
            var idx = 1;
            foreach (var d in app.Diseases)
            {
                var owner = string.IsNullOrWhiteSpace(d.MemberName) ? "" : d.MemberName.Trim() + " ";
                var seg = $"【疾病{idx}】{owner}{d.DiseaseName}".Trim();
                if (!string.IsNullOrWhiteSpace(d.DiseaseCode)) seg += $"（编码 {d.DiseaseCode}）";
                parts.Add(seg);
                idx++;
            }
        }

        if (app.Accidents != null && app.Accidents.Count > 0)
        {
            var idx = 1;
            foreach (var a in app.Accidents)
            {
                var owner = string.IsNullOrWhiteSpace(a.MemberName) ? "" : a.MemberName.Trim() + " ";
                var seg = $"【意外灾害{idx}】{owner}{a.AccidentType}".Trim();
                if (a.HappenDate != default) seg += $"（{a.HappenDate:yyyy-MM-dd}）";
                if (!string.IsNullOrWhiteSpace(a.HappenPlace)) seg += $"；地点：{a.HappenPlace}";
                if (!string.IsNullOrWhiteSpace(a.InjurySituation)) seg += $"；伤亡：{a.InjurySituation}";
                if (a.PropertyLoss.HasValue) seg += $"；财产损失：{a.PropertyLoss.Value:F2}元";
                parts.Add(seg);
                idx++;
            }
        }

        if (app.Educations != null && app.Educations.Count > 0)
        {
            var idx = 1;
            foreach (var e in app.Educations)
            {
                var seg = $"【教育支出{idx}】{e.StudentName}（{e.EducationStage}）".Trim();
                if (!string.IsNullOrWhiteSpace(e.SchoolName)) seg += $"；学校：{e.SchoolName}";
                if (e.TuitionFee.HasValue) seg += $"；学费：{e.TuitionFee.Value:F2}元";
                parts.Add(seg);
                idx++;
            }
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string BuildAccidentDetails(List<TempReliefAccident>? accidents)
    {
        if (accidents == null || accidents.Count == 0) return string.Empty;
        var parts = new List<string>();
        var idx = 1;
        foreach (var a in accidents)
        {
            var seg = $"{idx}. {a.AccidentType}".Trim();
            if (a.HappenDate != default) seg += $"（{a.HappenDate:yyyy-MM-dd}）";
            if (!string.IsNullOrWhiteSpace(a.HappenPlace)) seg += $" {a.HappenPlace}";
            if (!string.IsNullOrWhiteSpace(a.InjurySituation)) seg += $"，伤亡：{a.InjurySituation}";
            if (a.PropertyLoss.HasValue) seg += $"，财产损失：{a.PropertyLoss.Value:F2}元";
            parts.Add(seg);
            idx++;
        }
        return string.Join(Environment.NewLine, parts);
    }

    private static string BuildEducationDetails(List<TempReliefEducation>? educations)
    {
        if (educations == null || educations.Count == 0) return string.Empty;
        var parts = new List<string>();
        var idx = 1;
        foreach (var e in educations)
        {
            var seg = $"{idx}. {e.StudentName}（{e.EducationStage}）".Trim();
            if (!string.IsNullOrWhiteSpace(e.SchoolName)) seg += $" {e.SchoolName}";
            if (!string.IsNullOrWhiteSpace(e.SchoolDurationDisplay)) seg += $" {e.SchoolDurationDisplay}";
            if (e.TuitionFee.HasValue) seg += $"，学费：{e.TuitionFee.Value:F2}元";
            parts.Add(seg);
            idx++;
        }
        return string.Join(Environment.NewLine, parts);
    }

    /// <summary>现居住地：镇+村+详细地址（门牌号），与档案封面格式一致</summary>
    private static string BuildLivingAddress(TempReliefApplication app)
    {
        return string.Join(" ", new[] { app.FamilyTown, app.Village, app.FamilyDetail }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    /// <summary>
    /// 享受政策清单（□/√ 勾选格式）：以全量家庭类别为清单，对 PolicyEnjoyed 做模糊匹配
    /// 输出示例：☑最低生活保障家庭  □特困人员  □最低生活保障边缘家庭 ...
    /// </summary>
    private static string BuildPolicyChecklist(string? policyEnjoyed)
    {
        var parts = new List<string>();
        foreach (var option in TempReliefConstants.FamilyCategoryOptions)
        {
            var checked_ = !string.IsNullOrWhiteSpace(policyEnjoyed) && policyEnjoyed.Contains(option);
            parts.Add($"{(checked_ ? "☑" : "□")}{option}");
        }
        return string.Join("  ", parts);
    }

    /// <summary>临时救助原因（验收报告专用）：存库叙述 + 费用明细汇总</summary>
    private static string BuildAcceptanceReason(TempReliefApplication app)
    {
        var reason = app.DifficultyReason ?? string.Empty;

        var costParts = new List<string>();
        if (app.Diseases != null && app.Diseases.Count > 0)
        {
            var distinctDiseases = DistinctDiseasesByExpense(app.Diseases).ToList();
            var totalMedical = distinctDiseases.Where(d => d.MedicalTotal.HasValue).Sum(d => d.MedicalTotal!.Value);
            var totalInsurance = distinctDiseases.Where(d => d.InsurancePaid.HasValue).Sum(d => d.InsurancePaid!.Value);
            var totalSelfPaid = distinctDiseases.Where(d => d.SelfPaid.HasValue).Sum(d => d.SelfPaid!.Value);

            if (totalMedical > 0) costParts.Add($"本次医疗费用总额{totalMedical:F2}元");
            if (totalInsurance > 0) costParts.Add($"经基本医疗保险及大病保险等报销{totalInsurance:F2}元");
            if (totalSelfPaid > 0) costParts.Add($"个人自付{totalSelfPaid:F2}元");
        }

        if (costParts.Count == 0) return reason;
        var costSummary = string.Join("，", costParts);
        return string.IsNullOrWhiteSpace(reason) ? costSummary : $"{reason}\n{costSummary}";
    }

    /// <summary>
    /// 困难情况摘要（概括句，不含疾病名称/编码/费用明细）：用于入户调查表{申请救助情况说明}和信息公示{困难情况}
    /// </summary>
    private static string BuildDifficultySummary(TempReliefApplication app)
    {
        var type = (app.DifficultyType ?? string.Empty).Trim();
        return type switch
        {
            "疾病" => "申请人家庭成员因患疾病，医疗费用支出较高，造成家庭基本生活暂时陷入困境，特申请临时救助。",
            "意外灾害" => "申请人家庭成员因遭遇意外灾害，造成家庭基本生活暂时陷入困境，特申请临时救助。",
            "教育支出" => "申请人家庭成员因教育支出较大，造成家庭基本生活暂时陷入困境，特申请临时救助。",
            _ => "申请人家庭因突发困难导致基本生活暂时陷入困境，特申请临时救助。"
        };
    }

    /// <summary>
    /// 审核审批表{申请救助原因}精简句：本户家庭类别 + 人口 + 困难概括 + 成员姓名 + 原因词。
    /// 不含户籍分类与疾病/费用明细（详细内容见入户调查表等其它表格）。
    /// </summary>
    private static string BuildAuditReason(TempReliefApplication app)
    {
        var category = string.IsNullOrWhiteSpace(app.FamilyCategory) ? "困难群众" : app.FamilyCategory.Trim();
        var sizeText = app.FamilySize is > 0 ? $"{app.FamilySize}口人" : string.Empty;
        var member = ResolveAuditReasonMemberName(app);
        var reasonWord = (app.DifficultyType ?? string.Empty).Trim() switch
        {
            TempReliefConstants.DifficultyTypeEducation => "因学",
            TempReliefConstants.DifficultyTypeDisease => "因病",
            TempReliefConstants.DifficultyTypeAccident => "因灾",
            _ => "因突发困难"
        };
        return $"本户{category}{sizeText}，家庭困难，家庭成员{member}{reasonWord}申请临时救助。";
    }

    /// <summary>
    /// 精简句成员姓名：按困难类型收集全部明细成员（教育=学生、疾病/意外=患病受灾成员），
    /// 按出现顺序去重后以「、」连接；明细成员名为空视为申请人本人补申请人姓名；
    /// 无任何明细成员时回退户主/申请人。
    /// </summary>
    private static string ResolveAuditReasonMemberName(TempReliefApplication app)
    {
        var type = (app.DifficultyType ?? string.Empty).Trim();
        IEnumerable<string?>? detailNames = type switch
        {
            TempReliefConstants.DifficultyTypeEducation => app.Educations?.Select(e => e.StudentName),
            TempReliefConstants.DifficultyTypeDisease => app.Diseases?.Select(d => d.MemberName),
            TempReliefConstants.DifficultyTypeAccident => app.Accidents?.Select(a => a.MemberName),
            _ => null
        };

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (detailNames != null)
        {
            foreach (var raw in detailNames)
            {
                // 明细成员名为空时视为申请人本人
                var name = string.IsNullOrWhiteSpace(raw) ? app.ApplicantName : raw.Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (seen.Add(name)) names.Add(name);
            }
        }

        return names.Count > 0 ? string.Join("、", names) : app.ApplicantName;
    }

    /// <summary>救助对象姓名：未指定时兜底回退申请人（户主），保证旧数据打印不空</summary>
    private static string ResolveBeneficiaryName(TempReliefApplication app)
        => FirstNonEmpty(app.BeneficiaryName, app.ApplicantName);

    /// <summary>取第一个非空字符串（全空返回空串）</summary>
    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    /// <summary>
    /// 疾病明细按"同一次医疗事件"去重（单人多病例口径）：
    /// 同一医院 + 同一治疗起止日期 + 同一费用三元组（医疗总额/报销/自付）视为一次住院，
    /// 该次住院下的多条诊断只计一份费用，避免支出总额/验收报告/月报自付合计重复累计；
    /// 不同住院（医院或治疗起止不同）即便金额相同也分别计入。
    /// 唯一事实来源：打印 builder 与月报自付合计共用本方法，禁止各自重抄。
    /// </summary>
    public static IEnumerable<TempReliefDisease> DistinctDiseasesByExpense(List<TempReliefDisease>? diseases)
    {
        if (diseases == null || diseases.Count == 0)
            return Enumerable.Empty<TempReliefDisease>();

        return diseases
            .GroupBy(d => (d.Hospital, d.TreatStartDate, d.TreatEndDate, d.MedicalTotal, d.InsurancePaid, d.SelfPaid))
            .Select(g => g.First());
    }
}
