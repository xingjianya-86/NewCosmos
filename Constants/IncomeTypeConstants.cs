namespace NewCosmos.Constants;

public static class IncomeTypeConstants
{
    public static class Codes
    {
        public const string WAGE = "Wage";
        public const string BUSINESS = "Business";
        public const string PROPERTY = "Property";
        public const string TRANSFER = "Transfer";
        public const string ALIMONY = "Alimony";
        public const string OTHER = "Other";
    }

    /// <summary>
    /// 务工收入子类型
    /// </summary>
    public static class LaborSubType
    {
        public const string WAGE = "工资";
        public const string BONUS = "奖金";
        public const string ALLOWANCE = "津贴";
        public static readonly string[] All = { WAGE, BONUS, ALLOWANCE };
    }

    /// <summary>
    /// 经营收入子类型
    /// </summary>
    public static class BusinessSubType
    {
        public const string AGRICULTURE = "农业经营";
        public const string COMMERCE = "商业经营";
        public static readonly string[] All = { AGRICULTURE, COMMERCE };
    }

    /// <summary>
    /// 转移收入子类型
    /// </summary>
    public static class TransferSubType
    {
        public const string PENSION = "养老金";
        public const string LOW_INCOME_SUBSIDY = "低保金";
        public const string DISABILITY_SUBSIDY = "残疾人补贴";
        public const string OTHER_SUBSIDY = "其他补贴";
        public static readonly string[] All = { PENSION, LOW_INCOME_SUBSIDY, DISABILITY_SUBSIDY, OTHER_SUBSIDY };
    }

    /// <summary>土地收入默认单价（元/亩，后续从配置读取）</summary>
    public static class LandUnitPrice
    {
        public const decimal SELF_FARM = 700m;
        public const decimal SUBLEASE = 400m;
        public const decimal CONTRACT = 600m;
    }

    /// <summary>赡养费默认比例（默认月赡养费 = 低保标准 × 该比例）</summary>
    public const decimal DEFAULT_SUPPORT_FEE_RATIO = 0.20m;

    public static string GetDescription(string code) =>
        Helpers.DictDisplayHelper.GetIncomeTypeDisplay(code);
}
