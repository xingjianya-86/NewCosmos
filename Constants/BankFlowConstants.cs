namespace NewCosmos.Constants;

/// <summary>
/// 银行流水核对常量（单一来源，禁止散落魔法值）。
/// 业务依据：《社会救助法》第十八条/第二十三条；《牡丹江市低收入人口认定实施细则》（牡民规〔2024〕2号）
/// 第二十条(二) 低保人均 ≤ 30 个月、第二十五条(二) 边缘人均 ≤ 36 个月、
/// 第二十六条(二) 刚性支出困难家庭财产参照边缘（36 个月）；名下无 60 马力以上农业机械。
/// （省办法黑民规〔2024〕7号第二十/二十五条同款）
/// </summary>
public static class BankFlowConstants
{
    /// <summary>低保档货币财产月数（第二十条(二)：人均 ≤ 30 个月；未判定/特困等无类别信息时从严取此档）</summary>
    public const int BalanceLimitMonths = 30;

    /// <summary>边缘档货币财产月数（第二十五条(二)：人均 ≤ 36 个月；刚性支出困难家庭第二十六条(二)参照边缘同档）</summary>
    public const int BalanceLimitMonthsEdge = 36;

    /// <summary>农业机械功率上限（马力）：名下无 60 马力以上农业机械</summary>
    public const decimal MachineryPowerLimit = 60m;

    /// <summary>
    /// 按分类码取货币财产月数（**档位单点映射**，2026-11-18 功能缺口修复）：
    /// 低保/单人保（已入低保）→ 30；边缘/刚性支出（参照边缘）→ 36；
    /// 特困（无法定货币财产条件）、超标/拒保/未判定/null → 30 从严（提示宁严勿漏，不误导漏报）。
    /// 未知新码回落 30（保守），禁止回落 36（宽松方向会漏报）。
    /// </summary>
    public static int MonthsFor(string? classificationCode) => classificationCode switch
    {
        ClassificationConstants.RuralLowIncome
            or ClassificationConstants.UrbanLowIncome
            or ClassificationConstants.RuralRigidExpenditure
            or ClassificationConstants.UrbanRigidExpenditure
            => BalanceLimitMonthsEdge,

        _ => BalanceLimitMonths, // 低保/单人保/特困/未知/null 一律从严 30
    };

    /// <summary>阈值提示文案模板；{0}=合计余额，{1}=人数，{2}=阈值金额，{3}=货币财产月数（30/36，随类别分档）</summary>
    public const string BalanceExceedFormat = "家庭货币财产合计 {0} 元，超过 {3} 个月低保标准（{1} 人 × {3} × 月标准 = {2} 元），仅提示，请人工核定";

    /// <summary>导入类型代码（须与 ImportTypeCodes.BANK_FLOW_VERIFY 一致）</summary>
    public const string ImportTypeCode = "BANK_FLOW_VERIFY";

    /// <summary>核对表文件名匹配模式（单文件导入过滤）</summary>
    public static readonly string[] FileNamePatterns = { "*核对表*.xls", "*核对表*.xlsx" };

    /// <summary>批次号格式：yyyy-MM（从文件名/标题解析年月，解析失败用导入当月）</summary>
    public const string BatchNoFormat = "yyyy-MM";

    /// <summary>日志中身份证脱敏前缀提示</summary>
    public const string LogServiceName = "BankFlowImportService";
}
