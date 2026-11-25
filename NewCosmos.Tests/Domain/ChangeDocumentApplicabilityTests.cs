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
        bool grantReduce = false,
        bool verifyStop = false)
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
            IsVerifyStop = verifyStop,
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
    /// 核验停止（动态管理「拒绝配合核验工作」）：分类不变、无 CategoryStop 记录，
    /// 清单门靠 IsVerifyStop 放行；与死亡同为客观停保事件，不受单人保排除限制。
    /// </summary>
    [Fact]
    public void 核验停止_告知书适用()
        => Assert.True(Facts("UrbanSubsistence", verifyStop: true).NoticeApplicable);

    [Fact]
    public void 核验停止_单人保_仍适用()
        => Assert.True(Facts("UrbanLowIncomeSingle", verifyStop: true).NoticeApplicable);

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

    // ─────────── CountsAsEconomicReview：经济复核类判定（清单门 hasEconomicReview 单点） ───────────

    /// <summary>经济复核主记录（FundChange）计为复核类</summary>
    [Fact]
    public void 经济复核主记录_计为复核类()
        => Assert.True(ChangeDocumentApplicabilityService.CountsAsEconomicReview(
            DictionaryConstants.ChangeType.FUND_CHANGE, ChangeReasonTypeConstants.EconomicReview));

    /// <summary>跨类 CategoryAdd 计为复核类</summary>
    [Fact]
    public void 跨类新增记录_计为复核类()
        => Assert.True(ChangeDocumentApplicabilityService.CountsAsEconomicReview(
            DictionaryConstants.ChangeType.CATEGORY_ADD, ChangeReasonTypeConstants.CrossCategoryTransfer));

    /// <summary>经济复核/成员变更链补写的 CategoryStop 计为复核类（既有口径不变）</summary>
    [Fact]
    public void 经济复核停保记录_计为复核类()
        => Assert.True(ChangeDocumentApplicabilityService.CountsAsEconomicReview(
            DictionaryConstants.ChangeType.CATEGORY_STOP, ChangeReasonTypeConstants.EconomicReview));

    /// <summary>
    /// 归档终结拒保补写的 CategoryStop **不计**复核类：该档从未经过复核行为，
    /// 计入会让《档案_定期复核审批表》/「经济复核」叶子对拒保档误开放（丁玉花 app113 同型收口）。
    /// </summary>
    [Fact]
    public void 归档终结拒保停保记录_不计为复核类()
        => Assert.False(ChangeDocumentApplicabilityService.CountsAsEconomicReview(
            DictionaryConstants.ChangeType.CATEGORY_STOP, ChangeReasonTypeConstants.ArchiveRefusalStop));

    /// <summary>死亡/户主变更记录不属经济复核类（既有口径不变）</summary>
    [Fact]
    public void 户主死亡记录_不计为复核类()
        => Assert.False(ChangeDocumentApplicabilityService.CountsAsEconomicReview(
            DictionaryConstants.ChangeType.HOUSEHOLD_DEATH, ChangeReasonTypeConstants.HeadDeceased));

    /// <summary>空类型不计为复核类</summary>
    [Fact]
    public void 空变更类型_不计为复核类()
        => Assert.False(ChangeDocumentApplicabilityService.CountsAsEconomicReview(null, null));

    // ─────────── NoticeStopDirection：告知书方向②（停止/不予认定）判定 ───────────

    /// <summary>
    /// 丁玉花 app113 锁点：归档终结拒保 + 链上有在保前分类（户主死亡停旧建新，前分类=农村低保）
    /// → 原分类取前分类 → 在保 → 方向「停止」（曾享受、因收入超标被拒，不属首拒）。
    /// </summary>
    [Fact]
    public void 归档拒保_链上前分类在保_原分类取前分类()
        => Assert.Equal("RuralLowIncome",
            NoticeStopDirection.ResolveArchiveRefusedOriginal("RuralLowIncome", "RuralIncomeExceeded"));

    /// <summary>app110 锁点：无链上前分类（真·首次申请即被拒）→ 原分类=档案自身停保码 → 方向「不予认定」</summary>
    [Fact]
    public void 归档拒保_无链上前分类_原分类取自身停保码()
        => Assert.Equal("RuralIncomeExceeded",
            NoticeStopDirection.ResolveArchiveRefusedOriginal(null, "RuralIncomeExceeded"));

    /// <summary>前分类本身是停保码（链上曾停保）→ 不采纳，原分类=档案自身停保码（仍出「不予认定」）</summary>
    [Fact]
    public void 归档拒保_前分类为停保码_不采纳()
        => Assert.Equal("UrbanIncomeExceeded",
            NoticeStopDirection.ResolveArchiveRefusedOriginal("RuralIncomeExceeded", "UrbanIncomeExceeded"));

    /// <summary>前分类为空串视为无</summary>
    [Fact]
    public void 归档拒保_前分类空串_原分类取自身停保码()
        => Assert.Equal("RuralIncomeExceeded",
            NoticeStopDirection.ResolveArchiveRefusedOriginal("", "RuralIncomeExceeded"));

    /// <summary>原分类在保（低保等有效分类）→ 方向「停止」</summary>
    [Fact]
    public void 原分类在保_方向为停止()
        => Assert.True(NoticeStopDirection.WasInScope("RuralLowIncome"));

    /// <summary>原分类为停保码 → 方向「不予认定」</summary>
    [Fact]
    public void 原分类停保码_方向为不予认定()
        => Assert.False(NoticeStopDirection.WasInScope("RuralIncomeExceeded"));

    /// <summary>
    /// 原分类缺失 → 不属停保码 → 按在保处理（与既有 `!IsCodeStop` 口径逐字一致）。
    /// 能走到方向②分支时链上必有 CategoryStop 停保事件（或兜底已置档案自身停保码），
    /// 停保事件本身即"曾在保"的证据 → 方向「停止」。
    /// </summary>
    [Fact]
    public void 原分类缺失_按在保处理_方向为停止()
        => Assert.True(NoticeStopDirection.WasInScope(null));
}
