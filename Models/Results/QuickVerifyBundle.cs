namespace NewCosmos.Models.Results;

/// <summary>
/// 快速核验聚合结果：一次身份证查询带回多组台账数据。
/// 【成败语义】每组独立暴露 IsLoaded/IsFailure/FailureReason——单组失败只在该组显示原因，
/// 绝不以空集冒充"无数据"（§7）；全部组失败时服务层返回整体 Failure（DB 级故障）。
/// 【只读】本结果仅供展示核验，不回写任何表单或台账。
/// </summary>
public class QuickVerifyBundle
{
    /// <summary>查询身份证（原样保存，展示时由 UI 决定是否脱敏）</summary>
    public string IdCard { get; set; } = string.Empty;

    /// <summary>匹配用姓名（输入值或人员库反查值）</summary>
    public string QueryName { get; set; } = string.Empty;

    /// <summary>姓名来源说明（"输入" / "人员库反查" / 空）</summary>
    public string NameSource { get; set; } = string.Empty;

    /// <summary>① 土地·外部承包台账（按姓名匹配，台账无身份证列）</summary>
    public QuickVerifyGroup<LandContractRow> ContractLand { get; } = new();

    /// <summary>② 土地·本系统历史申请确权记录（按身份证）</summary>
    public QuickVerifyGroup<LandHistoryRow> HistoryLand { get; } = new();

    /// <summary>③ 年度种植补贴（按身份证）</summary>
    public QuickVerifyGroup<SubsidyRow> Subsidies { get; } = new();

    /// <summary>④ 历史保障身份（低保/边缘/特困/申请表，按身份证）</summary>
    public QuickVerifyGroup<SecurityIdentityRow> SecurityIdentities { get; } = new();

    /// <summary>⑤ 资产类：车辆/房产/金融资产（历史申请经济明细，按身份证反查申请）</summary>
    public QuickVerifyGroup<AssetRow> Assets { get; } = new();

    /// <summary>⑤b 银行流水汇总（按身份证，最新批次）</summary>
    public QuickVerifyGroup<BankFlowRow> BankFlows { get; } = new();

    /// <summary>⑤c 家庭经济状况核对报告（按身份证）</summary>
    public QuickVerifyGroup<VerifyReportRow> AssetReports { get; } = new();

    /// <summary>⑥ 死亡记录（按身份证）</summary>
    public QuickVerifyGroup<DeathRow> Deaths { get; } = new();

    /// <summary>⑥b 临时救助（按身份证）</summary>
    public QuickVerifyGroup<TempReliefRow> TempReliefs { get; } = new();

    /// <summary>⑥c 高龄津贴（按身份证）</summary>
    public QuickVerifyGroup<ElderlyRow> ElderlyBenefits { get; } = new();

    /// <summary>全部组是否都失败（= 数据库级故障，服务层据此返回整体 Failure）</summary>
    public bool AllGroupsFailed =>
        ContractLand.IsFailure && HistoryLand.IsFailure && Subsidies.IsFailure &&
        SecurityIdentities.IsFailure && Assets.IsFailure && BankFlows.IsFailure &&
        AssetReports.IsFailure && Deaths.IsFailure && TempReliefs.IsFailure &&
        ElderlyBenefits.IsFailure;
}

/// <summary>核验单组结果</summary>
public class QuickVerifyGroup<T>
{
    /// <summary>是否已执行查询</summary>
    public bool IsLoaded { get; set; }

    /// <summary>查询是否失败（失败时 Items 为空且 FailureReason 有值）</summary>
    public bool IsFailure { get; set; }

    /// <summary>失败原因（用户可读，直接展示）</summary>
    public string FailureReason { get; set; } = string.Empty;

    /// <summary>匹配方式提示（如"按姓名匹配，存在同名风险"）</summary>
    public string MatchNote { get; set; } = string.Empty;

    public List<T> Items { get; set; } = new();

    public int Count => Items.Count;

    public bool HasItems => Items.Count > 0;
}

// ───────────────────────── 各组行模型 ─────────────────────────

/// <summary>土地·外部承包台账行（nc_biz_land_contract_contractor ⨝ _plot）</summary>
public class LandContractRow
{
    public string ContractorName { get; set; } = string.Empty;
    public string EmployerName { get; set; } = string.Empty;
    public int DataYear { get; set; }
    public string PlotName { get; set; } = string.Empty;
    public string PlotCode { get; set; } = string.Empty;
    public decimal ContractArea { get; set; }
    public decimal MeasuredArea { get; set; }
    public decimal LandArea { get; set; }
}

/// <summary>土地·历史申请确权记录行（nc_biz_land_confirmation_records）</summary>
public class LandHistoryRow
{
    public string MemberName { get; set; } = string.Empty;
    public string PlotCode { get; set; } = string.Empty;
    public string LandPlotInfo { get; set; } = string.Empty;
    public decimal LandArea { get; set; }
    public string LandUsage { get; set; } = string.Empty;
    public long ApplicationId { get; set; }
}

/// <summary>种植补贴行（nc_biz_annual_planting_subsidy 按分类汇总）</summary>
public class SubsidyRow
{
    public string Name { get; set; } = string.Empty;
    public string SubsidyType { get; set; } = string.Empty;
    public decimal Area { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>历史保障身份行（人员库/申请表命中）</summary>
public class SecurityIdentityRow
{
    public string SourceDisplay { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int FamilyMemberCount { get; set; }
}

/// <summary>资产行（车辆/房产/金融资产，Category 区分）</summary>
public class AssetRow
{
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

/// <summary>银行流水汇总行（最新批次）</summary>
public class BankFlowRow
{
    public string PersonName { get; set; } = string.Empty;
    public decimal TotalBalance { get; set; }
    public decimal MaxFlowAmount { get; set; }
    public bool IsExceed { get; set; }
    public string BatchNo { get; set; } = string.Empty;
    public string BankSummary { get; set; } = string.Empty;
}

/// <summary>核对报告行（nc_biz_asset_check_reports）</summary>
public class VerifyReportRow
{
    public DateTime? ReportDate { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int Version { get; set; }
    public bool IsValid { get; set; }
}

/// <summary>死亡记录行（nc_biz_death_records）</summary>
public class DeathRow
{
    public string MemberName { get; set; } = string.Empty;
    public string RelationshipToHead { get; set; } = string.Empty;
    public DateTime? DeathDate { get; set; }
    public string DeathReason { get; set; } = string.Empty;
}

/// <summary>临时救助行（nc_biz_temp_relief_applications）</summary>
public class TempReliefRow
{
    public string ApplicantName { get; set; } = string.Empty;
    public string ReliefType { get; set; } = string.Empty;
    public DateTime? ApplyDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal ConfirmAmount { get; set; }
}

/// <summary>高龄津贴行（nc_biz_elderly_applications）</summary>
public class ElderlyRow
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? ApplyDate { get; set; }
    public decimal IssueAmount { get; set; }
    public string Category { get; set; } = string.Empty;
}
