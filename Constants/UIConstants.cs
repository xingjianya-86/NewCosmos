namespace NewCosmos.Constants;

/// <summary>
/// UI 常量（标签、占位符等）
/// </summary>
public static class UIConstants
{
    // 收入标签
    public const string LABOR_TAG = "务工";
    public const string BUSINESS_TAG = "经营";
    public const string PROPERTY_TAG = "财产";
    public const string TRANSFER_TAG = "转移";
    public const string OTHER_TAG = "其他";

    // 单位
    public const string YUAN = "元";
    public const string YUAN_PER_MONTH = "元/月";
    public const string MONTH = "月";

    // 占位符
    public const string WORK_UNIT_PLACEHOLDER = "工作单位";
    public const string COMPANY_NAME_PLACEHOLDER = "经营单位名称";
    public const string PROPERTY_DESC_PLACEHOLDER = "财产描述";
    public const string MONTHLY_AMOUNT_PLACEHOLDER = "月金额";
    public const string INCOME_TYPE_PLACEHOLDER = "收入类型";

    // ScrollView 内 CollectionView 固定高度（有内容时取最大高度，避免删除项后回弹到顶部）
    public const double SUBSIDY_LIST_MAX_HEIGHT = 560;
    public const double RIGID_EXPENDITURE_LIST_MAX_HEIGHT = 600;
    public const double SUPPORTER_LIST_MAX_HEIGHT = 780;
    public const double LIST_MIN_HEIGHT = 40;
}
