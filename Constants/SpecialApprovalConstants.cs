using NewCosmos.Models.Entities;

namespace NewCosmos.Constants;

/// <summary>
/// 一事一议申报常量
/// </summary>
public static class SpecialApprovalConstants
{
    #region 申报表状态

    /// <summary>草稿</summary>
    public const string StatusDraft = "Draft";

    /// <summary>已提交申报</summary>
    public const string StatusSubmitted = "Submitted";

    /// <summary>会议审议通过</summary>
    public const string StatusApproved = "Approved";

    /// <summary>会议审议驳回</summary>
    public const string StatusRejected = "Rejected";

    // ── 黑民规〔2026〕10 号（2026-11-25） ──
    /// <summary>已返回结论（县级线下评议结论已登记，待备案）</summary>
    public const string StatusReturned = "Returned";

    /// <summary>不通过（终态：禁止提交申请）</summary>
    public const string StatusNotPassed = "NotPassed";

    /// <summary>已备案（终态）</summary>
    public const string StatusFiled = "Filed";

    /// <summary>退回补正（乡镇可修改后重新上报）</summary>
    public const string StatusReturnedForCorrection = "ReturnedForCorrection";

    /// <summary>历史：不予受理（只读兼容）</summary>
    public const string StatusNotApplicable = "NotApplicable";

    private static readonly Dictionary<string, string> StatusDescriptions = new()
    {
        [StatusDraft] = "草稿",
        [StatusSubmitted] = "已上报（待县级评议）",
        [StatusApproved] = "历史：审议通过（未备案）",
        [StatusRejected] = "历史：审议驳回",
        [StatusReturned] = "已返回结论（待备案）",
        [StatusNotPassed] = "不通过（禁止提交）",
        [StatusFiled] = "已备案",
        [StatusReturnedForCorrection] = "退回补正",
        [StatusNotApplicable] = "历史：不予受理"
    };

    /// <summary>
    /// 申报表状态描述
    /// </summary>
    public static string GetStatusDescription(string? status)
    {
        if (string.IsNullOrEmpty(status)) return string.Empty;
        return StatusDescriptions.TryGetValue(status, out var desc) ? desc : status;
    }

    #endregion

    #region 适用情形 / 负面清单（黑民规〔2026〕10 号）

    /// <summary>适用情形 8 类（code 稳定，名称按正式文原文）。</summary>
    public static readonly (string Code, string Name)[] CaseTypes =
    {
        ("PolicyUnclear", "现行救助政策无明确规定，实际操作中难以判定或不给予救助将造成基本生活困难的疑难事项"),
        ("DataExceedsNoEvidence", "家庭经济状况调查数据超出现有政策标准，但佐证材料缺失，实际生活水平明显低于当地正常水平"),
        ("MemberRelationUnclear", "家庭成员关系与赡养、抚养、扶养义务人难以界定"),
        ("SevereDiseaseDisability", "重病、重残等特殊困难需提高救助标准"),
        ("TempReliefLimitOrAbnormal", "临时救助达到限额或申请材料存在异常"),
        ("MultiDepartment", "需多部门综合施救"),
        ("RegionalCommonIssue", "区域内出现多起同类共性疑难救助问题"),
        ("CityDesignated", "市级民政部门确定的其他情形")
    };

    /// <summary>不适用情形（负面清单 6 项，命中任一项不能提交）。</summary>
    public static readonly (string Code, string Name)[] NegativeItems =
    {
        ("IncomeClearlyHigh", "收入明显高于当地平均水平且无重大致困情形"),
        ("ConcealOrBlacklist", "故意隐瞒事实、提供虚假材料、转移家庭财产，或被列入社会救助领域信用黑名单"),
        ("AbandonRights", "故意放弃赡养费、抚养费、扶养费或继承权"),
        ("IllegalHardship", "因违法违纪行为导致基本生活困难"),
        ("RepeatSameCause", "同一家庭、同一致困事由年度内多次重复申请且无新增重大困难"),
        ("OtherLaw", "法律法规规定的其他不适用情形")
    };

    /// <summary>适用情形名称（精确匹配；未知 code 返回 null）。</summary>
    public static string? GetCaseTypeName(string? code)
        => string.IsNullOrWhiteSpace(code)
            ? null
            : CaseTypes.FirstOrDefault(x => x.Code == code.Trim()).Name;

    /// <summary>负面清单名称（精确匹配；未知 code 返回 null）。</summary>
    public static string? GetNegativeItemName(string? code)
        => string.IsNullOrWhiteSpace(code)
            ? null
            : NegativeItems.FirstOrDefault(x => x.Code == code.Trim()).Name;

    /// <summary>
    /// 提交前校验：情形必选且必须是 8 类之一；负面清单命中任一项即阻断。
    /// 返回 null 表示通过，否则返回用户可读错误。
    /// </summary>
    public static string? ValidateCaseSelection(string? caseType, IEnumerable<string>? negativeItems)
    {
        var code = (caseType ?? string.Empty).Trim();
        if (code.Length == 0)
            return "请选择适用的「一事一议」情形";
        if (GetCaseTypeName(code) == null)
            return $"无效的适用情形代码：{code}";

        var hits = (negativeItems ?? Enumerable.Empty<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Select(x => GetNegativeItemName(x) ?? x)
            .ToList();
        if (hits.Count > 0)
            return "命中不适用情形，不能提交一事一议：" + string.Join("；", hits);

        return null;
    }

    /// <summary>是否不通过（终态，禁止申请提交）。</summary>
    public static bool IsNotPassed(string? status) => status == StatusNotPassed;

    /// <summary>是否可（重新）提交申报：草稿 / 退回补正。</summary>
    public static bool CanSubmit(string? status)
        => status is StatusDraft or StatusReturnedForCorrection;

    /// <summary>是否可登记评议结论：已上报。</summary>
    public static bool CanRecordConclusion(string? status) => status == StatusSubmitted;

    /// <summary>是否可登记备案：已返回结论（含历史 Approved）。</summary>
    public static bool CanMarkFiled(string? status)
        => status is StatusReturned or StatusApproved;

    /// <summary>是否仍会阻塞申请提交（草稿/已上报/退回补正）。</summary>
    public static bool BlocksApplicationSubmit(string? status)
        => status is StatusDraft or StatusSubmitted or StatusReturnedForCorrection;

    #endregion

    #region 内容编辑 / 负面清单

    /// <summary>申报内容是否可编辑：草稿 / 已上报待结论 / 退回补正（结论登记后锁定）。</summary>
    public static bool IsContentEditable(string? status)
        => status is StatusDraft or StatusSubmitted or StatusReturnedForCorrection;

    /// <summary>负面清单 code 列表 → 落库文本（逗号分隔、去空去重）。</summary>
    public static string JoinNegativeItems(IEnumerable<string>? items)
        => string.Join(",", (items ?? Enumerable.Empty<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Distinct());

    /// <summary>落库文本 → 负面清单 code 列表。</summary>
    public static string[] SplitNegativeItems(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    #endregion

    #region 指定分类选项

    /// <summary>
    /// 会议审议可指定的救助分类（一事一议通过后按此分类施保）。
    /// 仅允许指定合规的施保分类（最低生活保障/最低生活保障边缘家庭/特困人员/刚性支出困难家庭），禁止指定"不符合"类。
    /// 城乡同名的仅保留一项（Code 用 Rural 代表，保存时按申请人户籍经
    /// <see cref="ClassificationConstants.NormalizeCodeByHukou"/> 补全为对应 Rural/Urban 代码）。
    /// </summary>
    public static readonly (string Code, string Name)[] OverrideClassificationOptions =
    {
        (ClassificationConstants.RuralSubsistence, ClassificationConstants.ConvertFromCode(ClassificationConstants.RuralSubsistence)),
        (ClassificationConstants.RuralLowIncome, ClassificationConstants.ConvertFromCode(ClassificationConstants.RuralLowIncome)),
        (ClassificationConstants.RuralLowIncomeSingle, ClassificationConstants.ConvertFromCode(ClassificationConstants.RuralLowIncomeSingle)),
        (ClassificationConstants.RuralRigidExpenditure, ClassificationConstants.ConvertFromCode(ClassificationConstants.RuralRigidExpenditure)),
        (ClassificationConstants.RuralDestituteScattered, ClassificationConstants.ConvertFromCode(ClassificationConstants.RuralDestituteScattered)),
        (ClassificationConstants.RuralDestituteCentralized, ClassificationConstants.ConvertFromCode(ClassificationConstants.RuralDestituteCentralized))
    };

    /// <summary>
    /// 校验指定分类是否合法（必须是施保分类，不能是"不符合"类）
    /// </summary>
    public static bool IsValidOverrideClassification(string? code)
    {
        if (string.IsNullOrEmpty(code)) return false;
        if (ClassificationConstants.IsCodeStop(code)) return false;
        if (ClassificationConstants.IsCodeRural(code)) return true;
        return ClassificationConstants.IsCodeUrban(code);
    }

    #endregion

    #region 审核结果选项

    /// <summary>
    /// 乡镇民政办初核意见选项（模板文案："经初核，拟（{审核结果}）纳入'一事一议'决策范围。"）
    /// </summary>
    public static readonly string[] AuditResultOptions =
    {
        "同意纳入",
        "不同意纳入"
    };

    #endregion

    #region 申报表编号

    /// <summary>
    /// 生成申报表编号：SA{yyyyMMdd}-SP{seq:D4}
    /// </summary>
    public static string BuildFormNo(string applicationNo, int seq)
    {
        return $"SA{DateTime.Now:yyyyMMdd}-SP{seq:D4}";
    }

    #endregion
}
