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

    private static readonly Dictionary<string, string> StatusDescriptions = new()
    {
        [StatusDraft] = "草稿",
        [StatusSubmitted] = "已提交",
        [StatusApproved] = "审议通过",
        [StatusRejected] = "审议驳回"
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
