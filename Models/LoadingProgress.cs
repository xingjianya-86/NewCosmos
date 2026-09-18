namespace NewCosmos.Models;

/// <summary>
/// 加载进度模型
/// </summary>
public class LoadingProgress
{
    /// <summary>
    /// 当前步骤
    /// </summary>
    public int CurrentStep { get; set; }

    /// <summary>
    /// 总步骤数
    /// </summary>
    public int TotalSteps { get; set; }

    /// <summary>
    /// 当前步骤名称
    /// </summary>
    public string StepName { get; set; } = string.Empty;

    /// <summary>
    /// 进度百分比（0-100）
    /// </summary>
    public double Progress => TotalSteps > 0 ? (double)CurrentStep / TotalSteps * 100 : 0;

    /// <summary>
    /// 是否已完成
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// 是否有错误
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// 步骤信息（如 "3/6 步骤"）
    /// </summary>
    public string StepInfo => $"{CurrentStep}/{TotalSteps} 步骤";
}

/// <summary>
/// 加载步骤常量
/// </summary>
public static class LoadingSteps
{
    public const int Initialize = 1;
    public const int LoadApplication = 2;
    public const int LoadFamilyMembers = 3;
    public const int LoadSupporters = 4;
    public const int LoadCaregivers = 5;
    public const int LoadHouseholdSurvey = 6;
    public const int LoadEconomicDetails = 7;
    public const int LoadCapabilityAssessment = 8;
    public const int BuildFieldData = 9;
    public const int Total = 9;

    /// <summary>
    /// 获取步骤名称
    /// </summary>
    public static string GetStepName(int step) => step switch
    {
        Initialize => "正在初始化...",
        LoadApplication => "正在加载申请信息...",
        LoadFamilyMembers => "正在加载家庭成员...",
        LoadSupporters => "正在加载赡养人...",
        LoadCaregivers => "正在加载照料人...",
        LoadHouseholdSurvey => "正在加载入户调查...",
        LoadEconomicDetails => "正在加载经济明细...",
        LoadCapabilityAssessment => "正在加载能力鉴定...",
        BuildFieldData => "正在构建字段数据...",
        _ => "处理中..."
    };
}
