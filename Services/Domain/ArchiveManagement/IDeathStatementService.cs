using NewCosmos.Models.DeathStatement;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 死亡情况说明（村内死亡申报）领域服务。
///
/// 职责：
///   ① 本村范围解析（唯一口径，未绑定村社区显式失败）；
///   ② 申报台账查询/提交/核实/作废；
///   ③ 死者在库身份八源聚合（仅本村命中，跨村只计数；第⑧源=5 个历史导入台账）；
///   ④ 数据信息提醒规则（界面与文书共用同一份规则）；
///   ⑤ 跨流程联动卡片（只读视图 + 预填跳转，不跨域写）。
///
/// 设计文档：docs/20261026_死亡情况说明与申报台账规范.md
/// </summary>
public interface IDeathStatementService
{
    /// <summary>解析当前登录用户的本村范围。Failure=账号未绑定村社区或组织查询失败（禁止静默放开全库）。</summary>
    Task<Result<VillageScope>> ResolveScopeAsync(CancellationToken ct = default);

    /// <summary>
    /// 搜索死亡申报（主源：申报台账，按身份证去重取最新有效）+ 补充候选
    /// （本村档案死亡登记但尚无有效申报，标「未申报」）。严格按本村 + LIMIT。
    /// </summary>
    Task<Result<List<DeathSearchItem>>> SearchAsync(
        VillageScope scope, string? keyword, DateTime fromDate, DateTime toDate,
        bool showAll, CancellationToken ct = default);

    /// <summary>构建文书上下文：死者信息 + 身份八源 + 数据提醒 + 联动卡片。</summary>
    Task<Result<DeathStatementContext>> BuildContextAsync(
        VillageScope scope, DeathSearchItem item, CancellationToken ct = default);

    /// <summary>提交死亡申报（死亡确切时刻必填；同身份证窗口期内重复申报由调用方确认后放行）。</summary>
    Task<Result<DeathDeclareResult>> DeclareAsync(DeathDeclareRequest request, CancellationToken ct = default);

    /// <summary>
    /// 同身份证在死亡日期前后窗口期（±3 天）内的有效申报数。
    /// 提交前调用，&gt;0 时页面弹"重复申报"确认。
    /// </summary>
    Task<Result<int>> CountRecentValidAsync(string idCard, DateTime deathDate, CancellationToken ct = default);

    /// <summary>核实申报（Pending → Verified）。</summary>
    Task<Result> VerifyAsync(long declarationId, CancellationToken ct = default);

    /// <summary>作废申报（软状态 Revoked，不物理删除）。</summary>
    Task<Result> RevokeAsync(long declarationId, string reason, CancellationToken ct = default);

    /// <summary>
    /// 完结归档（Pending/Verified → Archived）：申报流程全部办结结案，
    /// 退出首页联动提醒横幅与待联动列表；不可对已作废申报归档（先撤销作废不支持，需重新申报）。
    /// </summary>
    Task<Result> ArchiveAsync(long declarationId, CancellationToken ct = default);

    #region 死亡联动待办（nc_biz_death_linkage_tasks，2026-10-31 方案2）

    /// <summary>
    /// 按申报ID幂等补齐该申报的死亡联动待办（DeclareAsync 成功后调用，失败不阻断申报——
    /// 任何列表/计数读取路径会先兜底补齐）。返回本次新生成的任务数。
    /// </summary>
    Task<Result<int>> EnsureLinkageTasksForDeclarationAsync(long declarationId, CancellationToken ct = default);

    /// <summary>
    /// 本范围死亡联动待办（先按范围幂等补齐，再按目标域实时状态判定办结并自愈推进 Completed，
    /// 仅返回未办任务；申报作废/归档的行经 JOIN 台账状态自动退出）。onlyDomains 非空时只取指定域。
    /// </summary>
    Task<Result<List<DeathLinkageTaskItem>>> GetLinkageTasksAsync(
        VillageScope scope, string[]? onlyDomains = null, CancellationToken ct = default);

    /// <summary>本范围未办死亡联动待办数（首页横幅计数；失败 → Success(0)，调用方不显示横幅）。</summary>
    Task<Result<int>> GetLinkageTaskCountAsync(VillageScope scope, CancellationToken ct = default);

    #endregion
}
