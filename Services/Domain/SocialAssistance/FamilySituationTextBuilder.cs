using System.Text;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// "家庭情况说明"生成上下文：字段口径与 ApplicationFormViewModel 保持一致。
/// 年值基准：月项×12 + 赡养年值 + 土地年值 + 补贴年值 − 刚性×12 = 家庭年收入合计（TotalAnnualIncome）。
/// </summary>
public class FamilySituationContext
{
    public string ApplicantName { get; set; } = string.Empty;
    public string HukouType { get; set; } = string.Empty;
    public int FamilySize { get; set; }
    public List<(string Name, string HealthStatus)> SickMembers { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
    public decimal WorkIncomeTotal { get; set; }
    public decimal BusinessIncomeTotal { get; set; }
    public decimal PropertyIncomeTotal { get; set; }
    public decimal TransferIncomeTotal { get; set; }
    public decimal OtherIncomeTotal { get; set; }
    public decimal AlimonyIncome { get; set; }
    public decimal TotalFamilyIncome { get; set; }
    public decimal PerCapitaIncome { get; set; }
    public decimal TotalAnnualIncome { get; set; }
    public decimal PerCapitaAnnualIncome { get; set; }
    public decimal RigidExpenditure { get; set; }
    public decimal FamilyLandArea { get; set; }
    public decimal LandIncomeTotal { get; set; }
    public decimal SubsidyTotal { get; set; }
    public int PropertyCount { get; set; }
    public int VehicleCount { get; set; }
    public int MachineryCount { get; set; }
    public List<(string PersonType, int Count, decimal AnnualFee)> SupporterGroups { get; set; } = new();
}

/// <summary>
/// 家庭情况说明文本生成器（申请表单与月报会议记录共用，避免两套口径漂移）。
/// 补贴（年度）单独列示，保证"年收入合计 = 列示明细之和"。
/// </summary>
public static class FamilySituationTextBuilder
{
    public static string Build(FamilySituationContext c)
    {
        if (c == null) return string.Empty;
        var sb = new StringBuilder();
        sb.Append($"申请人{c.ApplicantName}，{c.HukouType}，家庭共{c.FamilySize}人。");

        if (c.SickMembers.Count > 0)
        {
            var details = string.Join("、", c.SickMembers.Select(m => $"{m.Name}{m.HealthStatus}"));
            sb.Append($"家庭成员中{details}，");
        }

        sb.Append($"因{c.Reason}，导致家庭经济收入明显下降，基本生活出现严重困难。");

        // 收入来源（月收入分类标注，某分类为0则省略）
        var incomeSources = new List<string>();
        if (c.WorkIncomeTotal > 0) incomeSources.Add($"务工收入{c.WorkIncomeTotal:F0}元/月");
        if (c.BusinessIncomeTotal > 0) incomeSources.Add($"经营收入{c.BusinessIncomeTotal:F0}元/月");
        if (c.PropertyIncomeTotal > 0) incomeSources.Add($"财产收入{c.PropertyIncomeTotal:F0}元/月");
        if (c.TransferIncomeTotal > 0) incomeSources.Add($"转移收入{c.TransferIncomeTotal:F0}元/月");
        if (c.OtherIncomeTotal > 0) incomeSources.Add($"其他收入{c.OtherIncomeTotal:F0}元/月");
        if (incomeSources.Count > 0)
            sb.Append($"家庭月收入来源包括：{string.Join("、", incomeSources)}。");

        // 家庭收入（年值为权威口径，月值=年÷12 分解显示）
        if (c.TotalFamilyIncome > 0)
        {
            var annual = c.TotalAnnualIncome;
            if (annual <= 0) annual = Math.Round(c.TotalFamilyIncome * 12, 2);
            sb.Append($"家庭年收入合计{annual:F0}元（月收入{c.TotalFamilyIncome:F0}元×12个月），" +
                      $"人均月收入{c.PerCapitaIncome:F0}元，人均年收入{c.PerCapitaAnnualIncome:F0}元");
            if (c.RigidExpenditure > 0)
                sb.Append($"，月刚性支出{c.RigidExpenditure:F0}元，年刚性支出{Math.Round(c.RigidExpenditure * 12, 2):F0}元");
            sb.Append("。");
        }

        // 土地（年值）
        if (c.FamilyLandArea > 0)
        {
            sb.Append($"家庭承包土地{c.FamilyLandArea:F2}亩，年土地收入{c.LandIncomeTotal:F0}元。");
        }

        // 资产
        var assetDescriptions = new List<string>();
        if (c.PropertyCount > 0) assetDescriptions.Add($"{c.PropertyCount}处房产");
        if (c.VehicleCount > 0) assetDescriptions.Add($"{c.VehicleCount}辆车辆");
        if (c.MachineryCount > 0) assetDescriptions.Add($"{c.MachineryCount}台农机具");
        if (assetDescriptions.Count > 0)
            sb.Append($"家庭拥有{string.Join("、", assetDescriptions)}。");

        // 赡养/抚养/扶养：按类型分组，中文数字，年给付
        foreach (var (type, count, fee) in c.SupporterGroups)
        {
            var personLabel = type switch
            {
                "抚养" => "抚养人",
                "扶养" => "扶养人",
                _ => "赡养人"
            };
            var feeLabel = type switch
            {
                "抚养" => "抚养费",
                "扶养" => "扶养费",
                _ => "赡养费"
            };
            sb.Append($"有{personLabel}共{ToChineseNumber(count)}名，年给付{feeLabel}{fee:F0}元。");
        }

        // 补贴（年值，与土地/赡养费同为年度口径；0 则省略，保证合计=明细和）
        if (c.SubsidyTotal > 0)
            sb.Append($"另有补贴收入{c.SubsidyTotal:F0}元。");

        return sb.ToString();
    }

    /// <summary>阿拉伯数字转中文数字（用于"两名/三名"等表述）</summary>
    private static string ToChineseNumber(int n)
    {
        string[] chinese = { "零", "一", "两", "三", "四", "五", "六", "七", "八", "九", "十" };
        if (n >= 1 && n <= 10) return chinese[n];
        if (n >= 11 && n <= 20) return $"十{chinese[n - 10]}";
        if (n >= 21 && n <= 99)
        {
            var tens = n / 10;
            var ones = n % 10;
            return tens switch
            {
                2 => $"二十{chinese[ones]}",
                3 => $"三十{chinese[ones]}",
                4 => $"四十{chinese[ones]}",
                5 => $"五十{chinese[ones]}",
                6 => $"六十{chinese[ones]}",
                7 => $"七十{chinese[ones]}",
                8 => $"八十{chinese[ones]}",
                9 => $"九十{chinese[ones]}",
                _ => n.ToString()
            };
        }
        return n.ToString();
    }
}