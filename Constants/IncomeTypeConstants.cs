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
    /// 收入类型中文显示名（用于 ActionSheet）
    /// </summary>
    public static class DisplayNames
    {
        public const string LABOR = "务工收入";
        public const string BUSINESS = "经营收入";
        public const string PROPERTY = "财产收入";
        public const string TRANSFER = "转移收入";
        public const string OTHER = "其他收入";
        public static readonly string[] All = { LABOR, BUSINESS, PROPERTY, TRANSFER, OTHER };
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
    /// 财产收入子类型
    /// </summary>
    public static class PropertySubType
    {
        public const string RENT = "租金";
        public const string INTEREST = "利息";
        public static readonly string[] All = { RENT, INTEREST };
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

    public static string GetDescription(string code) =>
        Helpers.DictDisplayHelper.GetIncomeTypeDisplay(code);
}
