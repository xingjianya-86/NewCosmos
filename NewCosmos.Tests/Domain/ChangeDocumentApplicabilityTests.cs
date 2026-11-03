using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Services.Domain.ChangeManagement;
using Xunit;

namespace NewCosmos.Tests.Domain;

/// <summary>
/// 变动文书适用性判定（<see cref="ChangeDocumentApplicabilityService"/> = 事实快照 + 分类→事实映射的单点口径，
/// 档案输出清单过滤与申请表预勾选共用）。
///
/// 锁点（2026-11 告知书缺陷回归）：
///  ① 《档案_变更告知书》模板全库唯一，"停保/不予认定/终止/待遇调整"是同一模板的不同**方向**设定，
///     方向由装配端 ArchiveProductionViewModel 决定，本类只管"清单里出不出这个模板"；
///  ② 三个放行事实（停保 / 归档终结拒保兜底 / 户主死亡整户停 / 待遇调整）与单人保排除；
///  ③ 死亡停保旧档（绝户）只保留告知书；
///  ④ 整档材料（未挂变动分类）不参与本过滤。
/// </summary>
public class ChangeDocumentApplicabilityTests
{
    /// <summary>判定纯逻辑，不触 DB / 渐退 / 变更服务依赖</summary>
    private static ChangeDocumentApplicabilityService Svc() => new(null!, null!, null!);

    private static ChangeDocumentFacts Facts(
        string classification,
        bool incomeStop = false,
        bool fundRegistration = false,
        bool deathStop = false,
        bool refusedStop = false,
        bool deathStoppedArchive = false,
        bool grantReduce = false)
        => new()
        {
            ApplicationId = 1,
            Classification = classification,
            IsIncomeStopChange = incomeStop,
            HasFundChangeRegistration = fundRegistration,
            IsHouseholdDeathStop = deathStop,
            IsArchiveRefusedStop = refusedStop,
            IsDeathStoppedArchive = deathStoppedArchive,
            HasGrantReduce = grantReduce,
        };

    // ─────────── NoticeApplicable：告知书放行口径 ───────────

    [Fact]
    public void 收入超标停保_告知书适用()
        => Assert.True(Facts("UrbanSubsistence", incomeStop: true).NoticeApplicable);

    [Fact]
    public void 保障金变更登记_待遇调整告知书适用()
        => Assert.True(Facts("UrbanSubsistence", fundRegistration: true).NoticeApplicable);

    /// <summary>
    /// A2（2026-11 拍板）：渐退封顶减发是告知书方向③的次级子场景（与登记互斥、装配端登记优先），
    /// 清单门同样放行；装配端取数与本事实同源（GetActiveAsync，原额 &gt; 现发 &gt; 0）。
    /// </summary>
    [Fact]
    public void 渐退封顶减发_告知书适用()
        => Assert.True(Facts("UrbanSubsistence", grantReduce: true).NoticeApplicable);

    /// <summary>单人保排除对封顶减发同样生效（2026-11 拍板：单人保不出告知书）</summary>
    [Fact]
    public void 渐退封顶减发_单人保_仍排除告知书()
        => Assert.False(Facts("UrbanLowIncomeSingle", grantReduce: true).NoticeApplicable);

    /// <summary>封顶减发与登记同时存在时仍放行（互斥由服务端保证，此处只锁 OR 语义）</summary>
    [Fact]
    public void 渐退封顶减发与登记并存_仍适用()
        => Assert.True(Facts("UrbanSubsistence", fundRegistration: true, grantReduce: true).NoticeApplicable);

    [Fact]
    public void 无任何变动事实_告知书不适用()
        => Assert.False(Facts("UrbanSubsistence").NoticeApplicable);

    [Fact]
    public void 单人保即使有停保事实_仍排除告知书()
        => Assert.False(Facts("UrbanLowIncomeSingle", incomeStop: true).NoticeApplicable);

    [Fact]
    public void 单人保即使有保障金登记_仍排除告知书()
        => Assert.False(Facts("RuralLowIncomeSingle", fundRegistration: true).NoticeApplicable);

    [Fact]
    public void 户主死亡整户停_不受单人保限制_告知书适用()
        => Assert.True(Facts("UrbanLowIncomeSingle", deathStop: true).NoticeApplicable);

    /// <summary>
    /// 归档终结拒保：首次申请即被拒的独立新档只置 Stopped、**不写变更记录**，
    /// IsIncomeStopChange 恒 false——清单门必须靠 IsArchiveRefusedStop 兜底，
    /// 否则此类档案在「变动文书」清单里被过滤漏打（装配端方向②有同口径兜底，app95 同型）。
    /// </summary>
    [Fact]
    public void 归档终结拒保_已停保且分类为停保码_无变更记录也适用()
        => Assert.True(Facts("UrbanIncomeExceeded", refusedStop: true).NoticeApplicable);

    [Fact]
    public void 未标记归档终结拒保_仅有停保码分类_不适用()
        => Assert.False(Facts("UrbanIncomeExceeded").NoticeApplicable);

    // ─────────── IsTemplateApplicable：清单过滤 ───────────

    [Fact]
    public void 变动分类叶子存在_与模板名常量同名()
        => Assert.Contains(DocumentTemplateNames.ChangeNotice, ArchiveCategoryResolver.DocumentOperationCategories);

    [Fact]
    public void 渐退审批表_无渐退事实_不适用()
        => Assert.False(Svc().IsTemplateApplicable(
            new[] { DocumentTemplateNames.GraceApproval }, Facts("UrbanSubsistence")));

    [Fact]
    public void 渐退审批表_有渐退事实_适用()
    {
        var facts = new ChangeDocumentFacts
        {
            ApplicationId = 1,
            Classification = "UrbanSubsistence",
            HasActiveGracePeriod = true,
        };
        Assert.True(Svc().IsTemplateApplicable(new[] { DocumentTemplateNames.GraceApproval }, facts));
    }

    [Fact]
    public void 整档材料只挂档案分类码_不参与变动过滤_恒适用()
        => Assert.True(Svc().IsTemplateApplicable(new[] { "UrbanSubsistence" }, Facts("UrbanSubsistence")));

    [Fact]
    public void 模板分类为null_恒适用()
        => Assert.True(Svc().IsTemplateApplicable(null, Facts("UrbanSubsistence")));

    [Fact]
    public void 模板空分类数组_恒适用()
        => Assert.True(Svc().IsTemplateApplicable(System.Array.Empty<string>(), Facts("UrbanSubsistence")));

    /// <summary>模板 241 挂上变动叶子后，由 NoticeApplicable 承担同一过滤（与追加门口径同源）</summary>
    [Fact]
    public void 告知书挂变动叶子_事实成立_适用()
        => Assert.True(Svc().IsTemplateApplicable(
            new[] { DocumentTemplateNames.ChangeNotice, "UrbanIncomeExceeded", "新增" },
            Facts("UrbanIncomeExceeded", incomeStop: true)));

    [Fact]
    public void 告知书挂变动叶子_事实不成立_被过滤()
        => Assert.False(Svc().IsTemplateApplicable(
            new[] { DocumentTemplateNames.ChangeNotice, "新增" },
            Facts("UrbanSubsistence")));

    [Fact]
    public void 死亡停保旧档_非告知书模板一律不出()
        => Assert.False(Svc().IsTemplateApplicable(
            new[] { ChangeCategoriesPeriodicReview },
            Facts("UrbanSubsistence", deathStop: true, deathStoppedArchive: true)));

    [Fact]
    public void 死亡停保旧档_告知书事实成立_放行()
        => Assert.True(Svc().IsTemplateApplicable(
            new[] { DocumentTemplateNames.ChangeNotice },
            Facts("UrbanSubsistence", deathStop: true, deathStoppedArchive: true)));

    [Fact]
    public void 正常档案_定期复核审批表有复核记录_适用()
    {
        var facts = new ChangeDocumentFacts
        {
            ApplicationId = 1,
            Classification = "UrbanSubsistence",
            HasEconomicReviewRecord = true,
        };
        Assert.True(Svc().IsTemplateApplicable(new[] { "档案_定期复核审批表" }, facts));
    }

    /// <summary>分类树叶子名（TemplateCategoryProvider 变更组）与 DocumentOperationCategories 保持一致</summary>
    private const string ChangeCategoriesPeriodicReview = "档案_定期复核审批表";
}
