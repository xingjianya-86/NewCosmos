namespace NewCosmos.Constants;

/// <summary>
/// 近亲属备案业务常量
/// 业务背景：社会救助工作人员近亲属享受社会救助的备案制度（防优亲厚友）。
/// 一份备案 = 1 名工作人员 + 其 N 名救助对象（links 1..N，不设行数上限）。
/// 无年月维度——全量名册，月报表输出忽略年月，任何月份均输出全部有效备案。
/// </summary>
public static class NearRelativeConstants
{
    /// <summary>月报表表单键：近亲属备案（工作人员批量）</summary>
    public const string FormKeyStaffBatch = "近亲属_工作人员批量备案信息";

    /// <summary>月报表表单键：近亲属备案汇总</summary>
    public const string FormKeySummary = "近亲属_工作人员批量汇总表";

    /// <summary>月报表单键列表（勾选列表展示顺序）</summary>
    public static readonly (string Key, string Label)[] MonthlyFormOptions =
    {
        (FormKeyStaffBatch, "近亲属备案（工作人员批量）"),
        (FormKeySummary, "近亲属备案汇总"),
    };

    /// <summary>批量备案信息：每页对象行数（模板固定 5 行，超出自动续页）</summary>
    public const int StaffPageRowLimit = 5;

    /// <summary>汇总表：每页组数（模板固定 9 组，超出自动续页）</summary>
    public const int SummaryPageRowLimit = 9;

    /// <summary>权限码：近亲属备案管理</summary>
    public const string PermissionManage = "NEAR_RELATIVE_MANAGE";

    /// <summary>打印模板名：月报-批量备案信息</summary>
    public const string TemplateStaffBatch = "月报_近亲属_工作人员批量备案信息";

    /// <summary>打印模板名：月报-汇总表</summary>
    public const string TemplateSummary = "月报_近亲属_工作人员批量汇总表";

    /// <summary>打印模板名：档案-工作人员关联信息表</summary>
    public const string TemplateLinkInfo = "档案_近亲属_工作人员关联信息表";

    /// <summary>打印模板名：档案-救助对象信息表</summary>
    public const string TemplateObjectInfo = "档案_近亲属_救助对象信息表";
}
