using CommunityToolkit.Mvvm.ComponentModel;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 工资性收入实体（对应 nc_biz_labor_incomes 表）
/// 【月收入体系】AnnualIncome 存储的是月收入合计，非年收入
/// </summary>
public partial class LaborIncome : ObservableObject
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public long MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string MemberIdCard { get; set; } = string.Empty;
    public int MemberAge { get; set; }
    public string IncomeSubType { get; set; } = "工资";
    public string WorkUnit { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DeletedAt { get; set; }

    /// <summary>Picker 选择的人员（双向绑定，选择后自动同步 MemberName/MemberIdCard/MemberAge）</summary>
    [ObservableProperty]
    private FamilyMember? _selectedIncomeMember;

    partial void OnSelectedIncomeMemberChanged(FamilyMember? value)
    {
        if (value != null)
        {
            MemberName = value.Name;
            MemberIdCard = value.IdCard ?? "";
            MemberAge = value.Age ?? 0;
        }
    }

    /// <summary>月收入（元/月）</summary>
    [ObservableProperty]
    private decimal? _monthlyIncome;

    /// <summary>工作月数（用于计算年收入时参考，月收入体系下不影响计算）</summary>
    [ObservableProperty]
    private int? _monthsWorked;

    /// <summary>月收入合计（元/月），用于汇总到 Application 月收入字段</summary>
    [ObservableProperty]
    private decimal _annualIncome;

    partial void OnMonthlyIncomeChanged(decimal? value) => RecalculateAnnualIncome();
    partial void OnMonthsWorkedChanged(int? value) => RecalculateAnnualIncome();

    private void RecalculateAnnualIncome()
    {
        // 月收入体系：直接取月收入，不再乘以工作月数
        AnnualIncome = MonthlyIncome ?? 0;
    }
}

/// <summary>
/// 经营净收入实体（对应 nc_biz_business_incomes 表）
/// 【月收入体系】存储月收入，不再乘以12
/// </summary>
public partial class BusinessIncome : ObservableObject
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public long MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string MemberIdCard { get; set; } = string.Empty;
    public int MemberAge { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DeletedAt { get; set; }

    /// <summary>Picker 选择的人员</summary>
    [ObservableProperty]
    private FamilyMember? _selectedIncomeMember;

    partial void OnSelectedIncomeMemberChanged(FamilyMember? value)
    {
        if (value != null)
        {
            MemberName = value.Name;
            MemberIdCard = value.IdCard ?? "";
            MemberAge = value.Age ?? 0;
        }
    }

    [ObservableProperty]
    private string _companyName = string.Empty;

    [ObservableProperty]
    private string _vendorType = string.Empty;

    /// <summary>月收入（元/月）</summary>
    [ObservableProperty]
    private decimal? _monthlyIncome;

    /// <summary>月收入（元/月），直接返回月收入，不再乘以12</summary>
    public decimal? AnnualIncome => MonthlyIncome;
}

/// <summary>
/// 财产净收入实体（对应 nc_biz_property_incomes 表）
/// 【月收入体系】Amount 为月收入（元/月）
/// </summary>
public partial class PropertyIncome : ObservableObject
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public long MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string MemberIdCard { get; set; } = string.Empty;
    public int MemberAge { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DeletedAt { get; set; }

    /// <summary>Picker 选择的人员</summary>
    [ObservableProperty]
    private FamilyMember? _selectedIncomeMember;

    partial void OnSelectedIncomeMemberChanged(FamilyMember? value)
    {
        if (value != null)
        {
            MemberName = value.Name;
            MemberIdCard = value.IdCard ?? "";
            MemberAge = value.Age ?? 0;
        }
    }

    [ObservableProperty]
    private string _incomeType = string.Empty;

    [ObservableProperty]
    private string _propertyDescription = string.Empty;

    /// <summary>月收入（元/月）</summary>
    [ObservableProperty]
    private decimal _amount;
}

/// <summary>
/// 转移净收入实体（对应 nc_biz_transfer_incomes 表）
/// 【月收入体系】TotalAmount 存储的是月收入，非年收入
/// </summary>
public partial class TransferIncome : ObservableObject
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public long MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string MemberIdCard { get; set; } = string.Empty;
    public int MemberAge { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DeletedAt { get; set; }

    /// <summary>Picker 选择的人员</summary>
    [ObservableProperty]
    private FamilyMember? _selectedIncomeMember;

    partial void OnSelectedIncomeMemberChanged(FamilyMember? value)
    {
        if (value != null)
        {
            MemberName = value.Name;
            MemberIdCard = value.IdCard ?? "";
            MemberAge = value.Age ?? 0;
        }
    }

    [ObservableProperty]
    private string _incomeType = string.Empty;

    /// <summary>月收入（元/月）</summary>
    [ObservableProperty]
    private decimal? _monthlyAmount;

    /// <summary>发放次数（参考字段，月收入体系下不影响计算）</summary>
    [ObservableProperty]
    private int? _monthsOrTimes;

    /// <summary>月收入（元/月），直接取月收入，不再乘以次数</summary>
    [ObservableProperty]
    private decimal _totalAmount;

    partial void OnMonthlyAmountChanged(decimal? value) => RecalculateTotalAmount();
    partial void OnMonthsOrTimesChanged(int? value) => RecalculateTotalAmount();

    private void RecalculateTotalAmount()
    {
        // 月收入体系：直接取月收入，不再乘以次数
        TotalAmount = MonthlyAmount ?? 0;
    }
}

/// <summary>
/// 其他收入实体（对应 nc_biz_other_incomes 表）
/// 【月收入体系】Amount 为月收入（元/月）
/// </summary>
public partial class OtherIncome : ObservableObject
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DeletedAt { get; set; }

    [ObservableProperty]
    private string _incomeType = string.Empty;

    /// <summary>月收入（元/月）</summary>
    [ObservableProperty]
    private decimal _amount;
}

/// <summary>
/// 刚性支出实体（对应 nc_biz_rigid_expenditures 表）
/// 【月收入体系】Amount 为月支出（元/月）
/// </summary>
public partial class RigidExpenditure : ObservableObject
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public long MemberId { get; set; }
    public string PersonDescription { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;

    [ObservableProperty]
    private string _expenditureType = string.Empty;

    /// <summary>月支出（元/月）</summary>
    [ObservableProperty]
    private decimal _amount;
}