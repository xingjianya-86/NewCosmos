namespace NewCosmos.Models.Entities;

/// <summary>
/// 近亲属备案·工作人员实体（对应 nc_biz_near_relative_staffs 表）
/// </summary>
public class NearRelativeStaff
{
    public long Id { get; set; }

    /// <summary>工作人员姓名（近亲属姓名）</summary>
    public string StaffName { get; set; } = string.Empty;

    /// <summary>工作人员身份证号</summary>
    public string StaffIdCard { get; set; } = string.Empty;

    /// <summary>工作人员联系方式</summary>
    public string StaffPhone { get; set; } = string.Empty;

    /// <summary>工作单位</summary>
    public string WorkUnit { get; set; } = string.Empty;

    /// <summary>职务（职级）</summary>
    public string Position { get; set; } = string.Empty;

    /// <summary>所在镇/街道</summary>
    public string Town { get; set; } = string.Empty;

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime? DeletedAt { get; set; }
}

/// <summary>
/// 近亲属备案·救助对象明细实体（对应 nc_biz_near_relative_links 表）
/// </summary>
public class NearRelativeLink
{
    public long Id { get; set; }
    public long StaffId { get; set; }

    /// <summary>关联救助申请ID（可选，档案输出用）</summary>
    public long? ApplicationId { get; set; }

    /// <summary>救助对象与工作人员关系</summary>
    public string Relation { get; set; } = string.Empty;

    /// <summary>救助对象姓名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>救助对象身份证号</summary>
    public string IdCard { get; set; } = string.Empty;

    /// <summary>性别</summary>
    public string Gender { get; set; } = string.Empty;

    /// <summary>出生年月</summary>
    public string BirthDate { get; set; } = string.Empty;

    /// <summary>家庭住址</summary>
    public string FamilyAddress { get; set; } = string.Empty;

    /// <summary>户籍地址</summary>
    public string HukouAddress { get; set; } = string.Empty;

    /// <summary>居住地址</summary>
    public string ResidenceAddress { get; set; } = string.Empty;

    /// <summary>家庭人口/共同生活家庭成员人数</summary>
    public int? FamilySize { get; set; }

    /// <summary>社会救助种类/保障类别</summary>
    public string HelpType { get; set; } = string.Empty;

    /// <summary>月保障金</summary>
    public decimal? MonthAmount { get; set; }

    /// <summary>报账金额</summary>
    public decimal? ReportAmount { get; set; }

    /// <summary>救助起始时间</summary>
    public string StartTime { get; set; } = string.Empty;

    /// <summary>家庭关系</summary>
    public string FamilyRelation { get; set; } = string.Empty;

    /// <summary>申请原因</summary>
    public string ApplyReason { get; set; } = string.Empty;

    /// <summary>申请时间</summary>
    public string ApplyTime { get; set; } = string.Empty;

    /// <summary>家庭主要困难</summary>
    public string FamilyDifficulty { get; set; } = string.Empty;

    /// <summary>家庭经济情况调查情况</summary>
    public string EconomyInvestigate { get; set; } = string.Empty;

    /// <summary>乡镇（街道）意见</summary>
    public string TownOpinion { get; set; } = string.Empty;

    /// <summary>动态管理记录</summary>
    public string DynamicRecord { get; set; } = string.Empty;

    /// <summary>排序号</summary>
    public int SortOrder { get; set; }
}

/// <summary>
/// 近亲属备案完整视图（一条备案 = 工作人员 + 对象明细）
/// </summary>
public class NearRelativeEntry
{
    public NearRelativeStaff Staff { get; set; } = new();
    public List<NearRelativeLink> Links { get; set; } = new();
}

/// <summary>
/// 档案出口用：一名工作人员 + 一名关联救助对象（每对渲染一页）
/// </summary>
public class NearRelativePair
{
    public NearRelativeStaff Staff { get; set; } = new();
    public NearRelativeLink Link { get; set; } = new();
}

/// <summary>
/// 下拉选择项（选择已有备案用）
/// </summary>
public class NearRelativeBrief
{
    public long Id { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public string WorkUnit { get; set; } = string.Empty;
    public int LinkCount { get; set; }

    /// <summary>下拉显示文本</summary>
    public string Display => $"{StaffName}（{WorkUnit}）·{LinkCount}名对象";
}
