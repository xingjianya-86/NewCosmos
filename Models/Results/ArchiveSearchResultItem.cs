namespace NewCosmos.Models.Results;

/// <summary>
/// 档案搜索来源（当前库 + 5 个历史导入库）
/// </summary>
public enum ArchiveSearchSource
{
    /// <summary>当前库（nc_biz_applications）</summary>
    Current = 0,

    /// <summary>农村低保导入库</summary>
    RuralSubsistence = 1,

    /// <summary>城市低保导入库</summary>
    UrbanSubsistence = 2,

    /// <summary>低收入边缘导入库</summary>
    LowIncomeEdge = 3,

    /// <summary>特困供养导入库</summary>
    Destitute = 4,

    /// <summary>刚性支出导入库</summary>
    RigidExpenditure = 5
}

/// <summary>
/// 变更页"选择档案"搜索结果项：统一承载当前库档案与导入库家庭记录
/// </summary>
public class ArchiveSearchResultItem
{
    /// <summary>来源</summary>
    public ArchiveSearchSource Source { get; set; }

    /// <summary>当前库档案ID（Source=Current 时有效）</summary>
    public long ApplicationId { get; set; }

    /// <summary>导入库家庭表名（Source≠Current 时有效，来自服务内表白名单）</summary>
    public string SourceTable { get; set; } = string.Empty;

    /// <summary>导入库家庭行ID（Source≠Current 时有效）</summary>
    public long SourceId { get; set; }

    /// <summary>户主/申请人姓名</summary>
    public string ApplicantName { get; set; } = string.Empty;

    /// <summary>户主/申请人身份证号</summary>
    public string ApplicantIdCard { get; set; } = string.Empty;

    /// <summary>家庭住址</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>家庭人口数（导入库无该列时为 0）</summary>
    public int FamilySize { get; set; }

    /// <summary>保障金额合计（导入库无该列时为 0）</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>人均收入（导入库无该列时为 0；农村低保/城市低保为年人均，低收入/刚性库为空）</summary>
    public decimal PerCapitaIncome { get; set; }

    /// <summary>户口类型原始值（农村低保按域硬编码'农村'，特困库为中文，低收入/刚性库可能为空）</summary>
    public string HukouType { get; set; } = string.Empty;

    /// <summary>按成员命中时的说明（如：命中成员：李四（配偶））</summary>
    public string MatchedPersonInfo { get; set; } = string.Empty;

    /// <summary>是否有成员命中说明</summary>
    public bool HasMatchedPersonInfo => !string.IsNullOrEmpty(MatchedPersonInfo);

    /// <summary>是否来自当前库</summary>
    public bool IsFromCurrentLibrary => Source == ArchiveSearchSource.Current;

    /// <summary>是否来自导入库</summary>
    public bool IsFromImportedLibrary => Source != ArchiveSearchSource.Current;

    /// <summary>按来源推导的分类认定等级代码（导入库记录有效，用于"沿用历史等级"选项）</summary>
    public string DerivedClassificationCode { get; set; } = string.Empty;

    /// <summary>推导等级的显示名</summary>
    public string DerivedClassificationName { get; set; } = string.Empty;

    /// <summary>来源显示名</summary>
    public string SourceDisplay => Source switch
    {
        ArchiveSearchSource.Current => "当前库",
        ArchiveSearchSource.RuralSubsistence => "最低生活保障",
        ArchiveSearchSource.UrbanSubsistence => "最低生活保障",
        ArchiveSearchSource.LowIncomeEdge => "最低生活保障边缘",
        ArchiveSearchSource.Destitute => "特困供养",
        ArchiveSearchSource.RigidExpenditure => "刚性支出",
        _ => "未知来源"
    };
}
