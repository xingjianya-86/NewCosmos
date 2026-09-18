namespace NewCosmos.Models.Enums;

/// <summary>
/// 分类类型枚举
/// </summary>
public enum ClassificationType
{
    #region 低保类
    /// <summary>农村低保</summary>
    RuralSubsistence,

    /// <summary>城市低保</summary>
    UrbanSubsistence,

    /// <summary>农村低保（单人保）</summary>
    RuralLowIncomeSingle,

    /// <summary>城市低保（单人保）</summary>
    UrbanLowIncomeSingle,

    #endregion

    #region 低收入类

    /// <summary>农村低收</summary>
    RuralLowIncome,

    /// <summary>城市低收</summary>
    UrbanLowIncome,

    #endregion

    #region 特困类
    /// <summary>农村特困集中供养</summary>
    RuralDestituteCentralized,

    /// <summary>城市特困集中供养</summary>
    UrbanDestituteCentralized,

    /// <summary>农村特困分散供养</summary>
    RuralDestituteScattered,

    /// <summary>城市特困分散供养</summary>
    UrbanDestituteScattered,

    #endregion

    #region 刚性支出类

    /// <summary>农村刚性支出困难家</summary>
    RuralRigidExpenditure,

    /// <summary>城市刚性支出困难家</summary>
    UrbanRigidExpenditure,

    #endregion

    #region 停保类
    /// <summary>农村收入超标</summary>
    RuralIncomeExceeded,

    /// <summary>城市收入超标</summary>
    UrbanIncomeExceeded,

    /// <summary>有劳动力不符</summary>
    IneligibleWithLabor,

    /// <summary>不符合条</summary>
    Ineligible,

    /// <summary>其他原因不符</summary>
    IneligibleOther,

    #endregion
}

/// <summary>
/// 分类类型扩展方法
/// </summary>
public static class ClassificationTypeExtensions
{
    private static readonly Dictionary<ClassificationType, string> Descriptions = new()
    {
        [ClassificationType.RuralSubsistence] = "最低生活保障对象",
        [ClassificationType.UrbanSubsistence] = "最低生活保障对象",
        [ClassificationType.RuralLowIncomeSingle] = "最低生活保障对象（单人）",
        [ClassificationType.UrbanLowIncomeSingle] = "最低生活保障对象（单人）",
        [ClassificationType.RuralLowIncome] = "最低生活保障边缘家庭",
        [ClassificationType.UrbanLowIncome] = "最低生活保障边缘家庭",
        [ClassificationType.RuralDestituteCentralized] = "特困人员（集中供养）",
        [ClassificationType.UrbanDestituteCentralized] = "特困人员（集中供养）",
        [ClassificationType.RuralDestituteScattered] = "特困人员（分散供养）",
        [ClassificationType.UrbanDestituteScattered] = "特困人员（分散供养）",
        [ClassificationType.RuralRigidExpenditure] = "刚性支出困难家庭",
        [ClassificationType.UrbanRigidExpenditure] = "刚性支出困难家庭",
        [ClassificationType.RuralIncomeExceeded] = "收入超标",
        [ClassificationType.UrbanIncomeExceeded] = "收入超标",
        [ClassificationType.IneligibleWithLabor] = "不符合认定条件（有劳动力）",
        [ClassificationType.Ineligible] = "不符合认定条件",
        [ClassificationType.IneligibleOther] = "不符合认定条件（其他）"
    };

    private static readonly HashSet<ClassificationType> SubsistenceTypes = new()
    {
        ClassificationType.RuralSubsistence,
        ClassificationType.UrbanSubsistence,
        ClassificationType.RuralLowIncomeSingle,
        ClassificationType.UrbanLowIncomeSingle
    };

    private static readonly HashSet<ClassificationType> LowIncomeTypes = new()
    {
        ClassificationType.RuralLowIncome,
        ClassificationType.UrbanLowIncome
    };

    private static readonly HashSet<ClassificationType> DestituteTypes = new()
    {
        ClassificationType.RuralDestituteCentralized,
        ClassificationType.UrbanDestituteCentralized,
        ClassificationType.RuralDestituteScattered,
        ClassificationType.UrbanDestituteScattered
    };

    private static readonly HashSet<ClassificationType> StopTypes = new()
    {
        ClassificationType.RuralIncomeExceeded,
        ClassificationType.UrbanIncomeExceeded,
        ClassificationType.IneligibleWithLabor,
        ClassificationType.Ineligible,
        ClassificationType.IneligibleOther
    };

    private static readonly HashSet<ClassificationType> RuralTypes = new()
    {
        ClassificationType.RuralSubsistence,
        ClassificationType.RuralLowIncome,
        ClassificationType.RuralDestituteCentralized,
        ClassificationType.RuralDestituteScattered,
        ClassificationType.RuralRigidExpenditure,
        ClassificationType.RuralIncomeExceeded,
        ClassificationType.RuralLowIncomeSingle
    };

    /// <summary>
    /// 获取分类描述
    /// </summary>
    public static string GetDescription(this ClassificationType type) =>
        Descriptions.TryGetValue(type, out var desc) ? desc : type.ToString();

    /// <summary>
    /// 是否为低保类    /// </summary>
    public static bool IsSubsistence(this ClassificationType type) =>
        SubsistenceTypes.Contains(type);

    /// <summary>
    /// 是否为低收入类型
    /// </summary>
    public static bool IsLowIncome(this ClassificationType type) =>
        LowIncomeTypes.Contains(type);

    /// <summary>
    /// 是否为特困类    /// </summary>
    public static bool IsDestitute(this ClassificationType type) =>
        DestituteTypes.Contains(type);

    /// <summary>
    /// 是否为停保类    /// </summary>
    public static bool IsStop(this ClassificationType type) =>
        StopTypes.Contains(type);

    /// <summary>
    /// 是否为农村类    /// </summary>
    public static bool IsRural(this ClassificationType type) =>
        RuralTypes.Contains(type);

    /// <summary>
    /// 是否为城市类    /// </summary>
    public static bool IsUrban(this ClassificationType type) =>
        !RuralTypes.Contains(type) && !StopTypes.Contains(type);

    /// <summary>
    /// 是否发放保障    /// </summary>
    public static bool HasSubsidy(this ClassificationType type) =>
        type.IsSubsistence() || type.IsDestitute();

    /// <summary>
    /// 从代码解    /// </summary>
    public static ClassificationType FromCode(string code) => code.Trim() switch
    {
        "RuralSubsistence" => ClassificationType.RuralSubsistence,
        "UrbanSubsistence" => ClassificationType.UrbanSubsistence,
        "RuralLowIncomeSingle" => ClassificationType.RuralLowIncomeSingle,
        "UrbanLowIncomeSingle" => ClassificationType.UrbanLowIncomeSingle,
        "RuralLowIncome" => ClassificationType.RuralLowIncome,
        "UrbanLowIncome" => ClassificationType.UrbanLowIncome,
        "RuralDestituteCentralized" => ClassificationType.RuralDestituteCentralized,
        "UrbanDestituteCentralized" => ClassificationType.UrbanDestituteCentralized,
        "RuralDestituteScattered" => ClassificationType.RuralDestituteScattered,
        "UrbanDestituteScattered" => ClassificationType.UrbanDestituteScattered,
        "RuralRigidExpenditure" => ClassificationType.RuralRigidExpenditure,
        "UrbanRigidExpenditure" => ClassificationType.UrbanRigidExpenditure,
        "RuralIncomeExceeded" => ClassificationType.RuralIncomeExceeded,
        "UrbanIncomeExceeded" => ClassificationType.UrbanIncomeExceeded,
        "IneligibleWithLabor" => ClassificationType.IneligibleWithLabor,
        "Ineligible" => ClassificationType.Ineligible,
        "IneligibleOther" => ClassificationType.IneligibleOther,
        _ => ClassificationType.Ineligible
    };

    /// <summary>
    /// 获取代码
    /// </summary>
    public static string GetCode(this ClassificationType type) => type.ToString();
}
