namespace NewCosmos.Constants;

/// <summary>
/// 银行流水核对常量（单一来源，禁止散落魔法值）。
/// 业务依据：《社会救助法》第十八条/第二十三条；黑龙江省家庭货币财产总额人均≤当地30个月城乡低保标准、
/// 名下无60马力以上农业机械。
/// </summary>
public static class BankFlowConstants
{
    /// <summary>货币财产阈值倍数：合计余额 &gt; 月低保标准 × 30 × 家庭人数 即超阈值（政策口径，勿改）</summary>
    public const int BalanceLimitMonths = 30;

    /// <summary>农业机械功率上限（马力）：名下无 60 马力以上农业机械</summary>
    public const decimal MachineryPowerLimit = 60m;

    /// <summary>阈值提示文案模板；{0}=标准值，{1}=人数，{2}=阈值金额</summary>
    public const string BalanceExceedFormat = "家庭货币财产合计 {0} 元，超过 30 个月低保标准（{1} 人 × 30 × 月标准 = {2} 元），仅提示，请人工核定";

    /// <summary>导入类型代码（须与 ImportTypeCodes.BANK_FLOW_VERIFY 一致）</summary>
    public const string ImportTypeCode = "BANK_FLOW_VERIFY";

    /// <summary>核对表文件名匹配模式（单文件导入过滤）</summary>
    public static readonly string[] FileNamePatterns = { "*核对表*.xls", "*核对表*.xlsx" };

    /// <summary>批次号格式：yyyy-MM（从文件名/标题解析年月，解析失败用导入当月）</summary>
    public const string BatchNoFormat = "yyyy-MM";

    /// <summary>日志中身份证脱敏前缀提示</summary>
    public const string LogServiceName = "BankFlowImportService";
}
