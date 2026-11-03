namespace NewCosmos.Constants;

/// <summary>
/// 业务节点文书直出：模板名常量（ITemplateService.GetByNameAsync 精确匹配键）。
/// 实机模板名以 nc_biz_templates.name 为准，不一致时改此处或改库中模板名。
/// 规范：docs/20260924_业务节点文书直出规范.md
/// </summary>
public static class DocumentTemplateNames
{
    /// <summary>人员增减变动表（增员减员调整表）</summary>
    public const string MemberChangeTable = "档案_增员减员调整表";

    /// <summary>渐退期审批表</summary>
    public const string GraceApproval = "档案_渐退期审批表";

    // 原 GrantReduce = "档案_保障金减少" 已删除（2026-11）：模板 2026-10-16 起不在库、
    // GR_* 字段填充块已删（三问全过）；减发场景由《档案_变更告知书》"待遇调整"方向承载，
    // 分类树/分类集/预勾选中的同名引用一并清理。

    /// <summary>变更告知书（与 ArchiveOutputViewModel 按名追加一致）</summary>
    public const string ChangeNotice = "档案_变更告知书";

    /// <summary>档案封面（停保卷宗白名单用）</summary>
    public const string Cover = "档案_封面";

    /// <summary>档案目录（停保卷宗白名单用）</summary>
    public const string Directory = "档案_目录";

    /// <summary>死亡情况说明（村内死亡申报出具；分类挂「证明文件」，模板名须与 nc_biz_templates.name 一致）</summary>
    public const string DeathStatement = "证明_死亡情况说明";

    /// <summary>特困供养人员取消（停止）情况表（特困退出/停发专用；退出直出经 TemplateFilter 强白名单命中，模板名须与 nc_biz_templates.name 一致）</summary>
    public const string DestituteCancelFiling = "特困供养人员取消备案表";

    /// <summary>清单展示名 → 模板名</summary>
    public static readonly (string Label, string TemplateName)[] SheetItems =
    {
        ("人员增减变动表", MemberChangeTable),
        ("渐退期审批表", GraceApproval),
        ("变更告知书", ChangeNotice),
        ("死亡情况说明", DeathStatement),
    };

    // ── 模板名判定单点（原 ArchiveOutputViewModel 两文件共 12 处散落 Contains("封面") 等魔串）──
    // 实机模板名带分类前缀（如 "档案_封面"），判定用子串匹配保持兼容；改名只改这里。

    /// <summary>是否档案封面模板（封面先行打印、失败中止的判定）</summary>
    public static bool IsCover(string? templateName) =>
        !string.IsNullOrEmpty(templateName) && templateName.Contains("封面");

    /// <summary>是否赡养费承诺书模板（按赡养人逐人生成/合并的判定）</summary>
    public static bool IsSupporterPledge(string? templateName) =>
        !string.IsNullOrEmpty(templateName) && templateName.Contains("赡养费承诺书");
}
