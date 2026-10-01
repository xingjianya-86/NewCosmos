namespace NewCosmos.Constants;

/// <summary>
/// 默认值常量 - 使用字典 ItemKey（英文编码）作为标识符
/// </summary>
public static class DefaultValuesConstants
{
    // ===== ItemKey 标识符（稳定不因语言变化） =====
    
    /// <summary>性别默认值 ItemKey</summary>
    public const string GENDER_KEY = "Male";
    
    /// <summary>民族默认值 ItemKey</summary>
    public const string ETHNICITY_KEY = "Han";
    
    /// <summary>婚姻状况默认值 ItemKey</summary>
    public const string MARITAL_STATUS_KEY = "Single";
    
    /// <summary>文化程度默认值 ItemKey</summary>
    public const string EDUCATION_LEVEL_KEY = "Primary";
    
    /// <summary>政治面貌默认值 ItemKey</summary>
    public const string POLITICAL_STATUS_KEY = "Masses";
    
    /// <summary>健康状况默认值 ItemKey</summary>
    public const string HEALTH_STATUS_KEY = "Healthy";
    
    /// <summary>户籍类型默认值 ItemKey</summary>
    public const string HUKOU_TYPE_KEY = "Rural";
    
    /// <summary>申请原因默认值 ItemKey</summary>
    public const string APPLICATION_REASON_KEY = "LowIncome";
    
    /// <summary>疾病类别默认值 ItemKey（疾病类别的 ItemKey 本身是中文）</summary>
    public const string DISEASE_CATEGORY_KEY = "无任何疾病";

    /// <summary>默认省份（家庭住址/户籍省份初值）</summary>
    public const string HOME_PROVINCE = "黑龙江省";
    
    // ===== 旧常量（标记为过时，保留兼容） =====
    
    [Obsolete("使用 ETHNICITY_KEY 替代")]
    public const string ETHNICITY = "汉族";
    
    [Obsolete("使用 MARITAL_STATUS_KEY 替代")]
    public const string MARITAL_STATUS = "未婚";
    
    [Obsolete("使用 EDUCATION_LEVEL_KEY 替代")]
    public const string EDUCATION_LEVEL = "小学";
    
    [Obsolete("使用 POLITICAL_STATUS_KEY 替代")]
    public const string POLITICAL_STATUS = "群众";
    
    [Obsolete("使用 APPLICATION_REASON_KEY 替代")]
    public const string APPLICATION_REASON = "收入低";
    
    [Obsolete("使用 HEALTH_STATUS_KEY 替代")]
    public const string HEALTH_STATUS = "健康或良好";
    
    [Obsolete("使用 DISEASE_CATEGORY_KEY 替代")]
    public const string DISEASE_CATEGORY = "无任何疾病";
    
    [Obsolete("使用 HUKOU_TYPE_KEY 替代")]
    public const string HUKOU_TYPE = "农村户口";
    
    public const string PENSION_INCOME_TYPE = "养老金";
}