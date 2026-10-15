namespace NewCosmos.Models.Entities;

/// <summary>
/// 银行流水核对导入批次（纯 POCO，对应 nc_biz_bank_flow_imports）
/// </summary>
public class BankFlowImport
{
    public long Id { get; set; }
    public string BatchNo { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int ExceedCount { get; set; }
    public int InvalidIdCardCount { get; set; }

    /// <summary>本批判定阈值（30×月低保标准，元）</summary>
    public decimal ExceedThreshold { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public string CreatedAtDisplay => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}

/// <summary>
/// 银行流水核对每人汇总（纯 POCO，对应 nc_biz_bank_flow_records）
/// </summary>
public class BankFlowRecord
{
    public long Id { get; set; }
    public long ImportId { get; set; }
    public int? RowNo { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public bool IdCardValid { get; set; } = true;
    public string Relationship { get; set; } = string.Empty;
    public string FamilyAddress { get; set; } = string.Empty;

    /// <summary>各银行余额合计（截止查询日期，元）</summary>
    public decimal TotalBalance { get; set; }

    /// <summary>12个月内各银行最高流水发生额最大值（元）</summary>
    public decimal MaxFlowAmount { get; set; }

    public string Property1 { get; set; } = string.Empty;
    public string Property2 { get; set; } = string.Empty;
    public string Property3 { get; set; } = string.Empty;
    public decimal? MachineryPower { get; set; }
    public bool IsExceed { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>各银行明细摘要（查询时 string_agg 生成，如"农信社 12,000.00；邮储 0.00"）</summary>
    public string BankSummary { get; set; } = string.Empty;

    public string TotalBalanceDisplay => TotalBalance.ToString("N2");
    public string MaxFlowAmountDisplay => MaxFlowAmount.ToString("N2");

    /// <summary>超阈值标记（仅提示，不自动决策）</summary>
    public string ExceedDisplay => IsExceed ? "超30月标准" : "正常";

    /// <summary>身份证校验标记（存疑仍导入，不拒导）</summary>
    public string IdCardValidDisplay => IdCardValid ? "有效" : "存疑";

    /// <summary>身份证存疑（供列表徽标 IsVisible 绑定）</summary>
    public bool IsIdCardSuspect => !IdCardValid;

    /// <summary>农机功率展示（超过 60 马力红字提示用）</summary>
    public string MachineryPowerDisplay => MachineryPower.HasValue ? $"{MachineryPower.Value:F0} 马力" : "—";

    public bool IsMachineryExceed => MachineryPower.HasValue && MachineryPower.Value > Constants.BankFlowConstants.MachineryPowerLimit;
}

/// <summary>
/// 银行流水核对每银行明细（纯 POCO，对应 nc_biz_bank_flow_items；银行为数据行，增减银行不改表）
/// </summary>
public class BankFlowItem
{
    public long Id { get; set; }
    public long RecordId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public decimal MaxFlowAmount { get; set; }
    public DateTime CreatedAt { get; set; }

    public string BalanceDisplay => Balance.ToString("N2");
    public string MaxFlowAmountDisplay => MaxFlowAmount.ToString("N2");
}
