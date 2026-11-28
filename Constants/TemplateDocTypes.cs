namespace NewCosmos.Constants;

/// <summary>
/// 模板文档类型词表（doc_type 单一来源，方案B 二维精准过滤，2026-11-18）。
///
/// 维度口径：
///   项目类型 = nc_biz_templates.categories（businessType + classification 经 ArchiveCategoryResolver 映射，已有，不动）
///   文档类型 = nc_biz_templates.doc_type（本词表；与项目类型正交，模板上传时必选）
///
/// 规则（铁律）：
///   ① 代码定位模板一律 ITemplateService.GetByDocTypeAsync(docType)，禁止再用 Name.Contains / StartsWith 模糊匹配；
///   ② 改词表值须同步 docs/migrations/20261118_template_doc_type.sql 的回填与存量 UPDATE（按 id 定位）；
///   ③ 新增模板时在「模板管理」上传表单选类型，零代码即可正确进入档案输出清单。
///
/// 与 DocumentTemplateNames 的分工：
///   DocumentTemplateNames = 模板名常量（GetByNameAsync 等值精确取、白名单展示文案等值场景）；
///   本类 = 文档类型常量（GetByDocTypeAsync、适用性门槛、数据分派判定）。
/// 模板改名不影响本类；模板类型变更须改本类 + 迁移。
/// </summary>
public static class TemplateDocTypes
{
    // ── 档案骨架 ──
    /// <summary>档案封面（封面先行打印、牛皮纸确认的判定）</summary>
    public const string Cover = "封面";

    /// <summary>档案目录/临时救助目录（目录专属字段与行数据分派）</summary>
    public const string Directory = "目录";

    /// <summary>入户调查表三版（档案_入户调查表 / 特困版 / 临时救助版；文书模式补挂判定）</summary>
    public const string Survey = "入户调查";

    /// <summary>邻里访问调查表（异地协助专用文书）</summary>
    public const string NeighborVisit = "邻里访问";

    // ── 土地二选一（适用性规则相反，必须分值）──
    /// <summary>无土地证明：有地时不出</summary>
    public const string NoLandProof = "无土地证明";

    /// <summary>村级土地说明：无地时不出</summary>
    public const string VillageLandNote = "村级土地说明";

    // ── 一事一议三表（适用性同源：仅一事一议申请出；申报表供按钮直出须可区分）──
    public const string SpecialApprovalDeclaration = "一事一议申报表";
    public const string SpecialApprovalApplication = "一事一议申请表";
    public const string SpecialApprovalRecord = "一事一议备案表";

    /// <summary>一事一议三表集合（档案输出清单适用性门槛用）</summary>
    public static readonly string[] SpecialApprovalForms =
    {
        SpecialApprovalDeclaration, SpecialApprovalApplication, SpecialApprovalRecord
    };

    // ── 近亲属两表（适用性同源：有备案才出）──
    public const string NearRelative = "近亲属";

    // ── 变动文书（TemplateFilter/预勾选白名单引用）──
    public const string ChangeNotice = "变更告知书";
    public const string MemberChangeTable = "增员减员调整表";
    public const string GraceApproval = "渐退期审批表";
    public const string PeriodicReviewApproval = "定期复核审批表";

    // ── 专用直出 / 分派 ──
    public const string DestituteCancelFiling = "特困取消备案表";
    public const string DeathStatement = "死亡情况说明";
    public const string SupporterPledge = "赡养费承诺书";

    /// <summary>临时救助大额/小额审核审批表（手机推送打印选主单的判定，两表同值）</summary>
    public const string TempReliefAuditApproval = "临时救助审核审批表";

    /// <summary>临时救助信息公示（小额快救免公示，模板清单剔除）</summary>
    public const string TempReliefPublicity = "信息公示";

    /// <summary>临时救助验收报告（小额快救免验收，模板清单剔除）</summary>
    public const string TempReliefAcceptance = "验收报告";

    /// <summary>会议记录（月报_会议记录 / 月报_会议记录_一事一议，两表同值）</summary>
    public const string MeetingMinutes = "会议记录";

    // ── 其余存量模板的粗分类型（不参与门槛逻辑，仅为精准定位与管理；完整映射见迁移回填）──
    public const string MonthlyDetail = "月报明细";
    public const string MonthlySummary = "月报汇总表";
    public const string Application = "申请书";
    public const string Commitment = "承诺书";
    public const string AuditConfirm = "审核确认表";
    public const string Agreement = "供养协议";
    public const string Certificate = "证明文件";
    public const string Statement = "声明书";
    public const string Notice = "告知书";
    public const string Publicity = "公示文件";

    /// <summary>
    /// 上传表单「文档类型」下拉选项（= 迁移回填的全部 46 个 distinct 值，按组排序）。
    /// 新增类型先改本数组 + 加常量 + 写迁移，再给下拉。
    /// </summary>
    public static readonly string[] All =
    {
        // 档案骨架
        Cover, Directory, Survey, NeighborVisit,
        // 土地/一事一议/近亲属
        NoLandProof, VillageLandNote,
        SpecialApprovalDeclaration, SpecialApprovalApplication, SpecialApprovalRecord,
        NearRelative,
        // 变动文书
        ChangeNotice, MemberChangeTable, GraceApproval, PeriodicReviewApproval,
        // 专用直出
        DestituteCancelFiling, DeathStatement, SupporterPledge,
        TempReliefAuditApproval, MeetingMinutes,
        // 常用文档性质
        Application, Commitment, AuditConfirm, Agreement, Certificate, Statement, Notice, Publicity,
        // 粗分
        MonthlyDetail, MonthlySummary,
        // 存量其余（与迁移回填一致）
        "授权承诺书", "咨询台账", "一次性告知书", "汇总表",
        "高龄登记表", "高龄须知", "高龄取消备案表", "高龄调整备案表",
        "调查表", "分类施保调整表", "信息公示", "验收报告",
        "追缴办理单", "公示名单", "值班表", "动态管理记录",
        "照料服务协议", "协查函"
    };

    /// <summary>是否档案封面（打印确认/批量排序判定；原 DocumentTemplateNames.IsCover 按名子串，改按类型）</summary>
    public static bool IsCover(string? docType) =>
        !string.IsNullOrEmpty(docType) && docType == Cover;

    /// <summary>是否赡养费承诺书（按赡养人逐人生成/合并的判定）</summary>
    public static bool IsSupporterPledge(string? docType) =>
        !string.IsNullOrEmpty(docType) && docType == SupporterPledge;
}
