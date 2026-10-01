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

    /// <summary>保障金减少（渐退超限封顶减发）</summary>
    public const string GrantReduce = "档案_保障金减少";

    /// <summary>变更告知书（与 ArchiveOutputViewModel 按名追加一致）</summary>
    public const string ChangeNotice = "档案_变更告知书";

    /// <summary>清单展示名 → 模板名</summary>
    public static readonly (string Label, string TemplateName)[] SheetItems =
    {
        ("人员增减变动表", MemberChangeTable),
        ("渐退期审批表", GraceApproval),
        ("保障金减少", GrantReduce),
        ("变更告知书", ChangeNotice),
    };
}
