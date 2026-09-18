using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.ElderlyBenefits;

/// <summary>
/// 普惠高龄补贴申请服务接口
/// </summary>
public interface IElderlyApplicationService
{
    /// <summary>
    /// 按身份证评估：判类 + 补发分段计算（受理时身份证联动展示用）
    /// </summary>
    Task<Result<ElderlyEvaluateResult>> EvaluateAsync(string idCard, DateTime applyDate, CancellationToken ct = default);

    /// <summary>
    /// 获取补贴标准金额（按类别代码）
    /// </summary>
    Task<Result<decimal>> GetMonthlyAmountAsync(string category, CancellationToken ct = default);

    Task<Result<ElderlyApplication>> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// 查询登记补发分段明细（按登记ID）
    /// </summary>
    Task<Result<List<ElderlyPaybackSegment>>> GetSegmentsAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 分页查询（关键字：姓名/身份证；状态：空=全部）
    /// </summary>
    Task<Result<PagedResult<ElderlyApplication>>> GetPagedAsync(string keyword, string status, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 按月份查询记录（月报表用）。
    /// 新增明细：受理月份 = 指定月且状态已确认；停止明细：实际停发月份 = 指定月。
    /// excludeImported: 排除从导入库补全建档的记录（source_type IS NULL）。
    /// categoryCode: 享受类别代码（CAT1-CAT4），null/空 = 不按分类筛选。
    /// </summary>
    Task<Result<List<ElderlyApplication>>> GetByMonthAsync(int year, int month, bool isStop, string? categoryCode = null, bool excludeImported = false, CancellationToken ct = default);

    /// <summary>
    /// 按指定记录ID列表查询（明细表多选打印用）
    /// </summary>
    Task<Result<List<ElderlyApplication>>> GetByIdsAsync(List<long> ids, CancellationToken ct = default);

    /// <summary>
    /// 创建登记（含补发分段明细）
    /// </summary>
    Task<Result<long>> CreateAsync(ElderlyApplication application, List<ElderlyPaybackSegment> segments, CancellationToken ct = default);

    /// <summary>
    /// 更新登记（含补发分段明细）
    /// </summary>
    Task<Result> UpdateAsync(ElderlyApplication application, List<ElderlyPaybackSegment> segments, CancellationToken ct = default);

    /// <summary>
    /// 确认生效（草稿→已确认）
    /// </summary>
    Task<Result> ConfirmAsync(long id, string operatorName, CancellationToken ct = default);

    /// <summary>
    /// 停发（含追缴信息）
    /// </summary>
    Task<Result> StopAsync(long id, string reason, string operatorName, DateTime? dueStopDate,
        DateTime? deathDate, bool isRecover,
        string recoverStartMonth, string recoverEndMonth, decimal recoverAmount, string remark,
        CancellationToken ct = default);

    /// <summary>
    /// 删除（软删除）
    /// </summary>
    Task<Result> DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// 判断身份证号是否已登记（排除指定记录）
    /// </summary>
    Task<Result<bool>> CheckIdCardExistsAsync(string idCard, long? excludeId = null, CancellationToken ct = default);

    /// <summary>
    /// 搜索导入库记录（按姓名/身份证，从 nc_biz_elderly_subsidy_history 查询）
    /// </summary>
    Task<Result<List<ElderlyImportedSearchItem>>> SearchImportedLibraryAsync(string keyword, CancellationToken ct = default);

    /// <summary>
    /// 按身份证精确查询导入库记录（取 data_year 最新一条），未命中返回 null。
    /// 高龄申请表单输入身份证后自动带出人员信息用。
    /// </summary>
    Task<Result<ElderlyImportedSearchItem?>> GetImportedByIdCardAsync(string idCard, CancellationToken ct = default);

    /// <summary>
    /// 需停旧增新：自动生成高龄申请草稿（预填身份证/姓名并自动判类补发）。
    /// 仅对"已有草稿"复用，已在享/已停发记录不阻塞新建草稿（停旧增新场景）。
    /// </summary>
    Task<Result<long>> CreateAutoDraftAsync(string idCard, string name, string? createdBy, CancellationToken ct = default);

    /// <summary>
    /// 从导入库记录建档迁移至当前库（幂等：已建档直接返回现有档案ID）
    /// </summary>
    Task<Result<long>> MigrateFromHistoryAsync(long historyId, string createdBy, CancellationToken ct = default);

    /// <summary>
    /// 数据补全并写入数据库后删除对应导入库记录（nc_biz_elderly_subsidy_history）。
    /// </summary>
    Task<Result> DeleteHistoryAsync(long historyId, CancellationToken ct = default);

    /// <summary>
    /// 归档联动：为已完结档案（排除单人保）中年满80周岁的户主/共同生活成员自动创建普惠高龄草稿。
    /// 仅对"无任何未删除登记记录"的人员建档（查重口径与 CheckIdCardExistsAsync 一致）；
    /// 已有在享（Confirmed）或历史名册在享的人员属"先停发后新增"场景，由经办人工办理，不自动建。
    /// </summary>
    Task<Result<int>> CreateDraftsForArchivedApplicationAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 首页提醒统计：已完结档案（排除单人保）中年满80周岁的户主/共同生活成员，
    /// 按普惠高龄待遇状态分流——待新增 / 待停旧增新（正式表 Confirmed 在享或历史名册有记录）。
    /// </summary>
    Task<Result<ElderlyPendingCounts>> GetPendingElderlyCountsAsync(CancellationToken ct = default);

    /// <summary>
    /// 下月待办明细：与 <see cref="GetPendingElderlyCountsAsync"/> 同口径的待办人员名单，
    /// 含姓名/身份证/年龄/来源档案/状态标注，并按 New（待新增）/ Transfer（需停旧增新）分流，
    /// 附带跳转办理所需的记录ID（正式表在享ID、历史名册ID）。
    /// </summary>
    Task<Result<List<ElderlyPendingItem>>> GetPendingElderlyListAsync(CancellationToken ct = default);

    /// <summary>
    /// 按身份证批量读取历史档案发放金额（nc_biz_elderly_subsidy_history.subsidy_amount，取最新 data_year）。
    /// 返回 id_card → 发放金额 映射；停止明细表"实际发放"列使用。
    /// </summary>
    Task<Result<Dictionary<string, decimal>>> GetHistorySubsidyAmountsAsync(IEnumerable<string> idCards, CancellationToken ct = default);

    #region 类别复核（身份 + 年龄重评，停旧增新）

    /// <summary>
    /// 低收入家庭变化联动：对申请人/共同生活成员中，已建高龄档案或在册发放的人员，
    /// 用当前身份重评类别，与旧类别不同则写入"待复核"队列（按身份证去重）。
    /// 失败不阻断上游业务。
    /// </summary>
    Task<Result<int>> TriggerReviewsForHouseholdAsync(long applicationId, string triggerSource, CancellationToken ct = default);

    /// <summary>
    /// 按身份证清单写入"待复核"队列（供其他进程调用；与 Household 版共用实现）。
    /// </summary>
    Task<Result<int>> TriggerReviewsForIdCardsAsync(IEnumerable<string> idCards, string triggerSource, long? triggerRef, string? triggerReason, CancellationToken ct = default);

    /// <summary>
    /// 重评类别：按在享档案 / 名册记录 / 身份证定位人员，用当前身份比对 + 年龄档次得出新类别，
    /// 与旧类别比较后返回变更结果（不落库）。
    /// </summary>
    Task<Result<ElderlyReviewEvaluation>> EvaluateReviewAsync(long? applicationId, string? idCard, long? historyId, CancellationToken ct = default);

    /// <summary>待复核队列（status=Pending）</summary>
    Task<Result<List<ElderlyReview>>> GetPendingReviewsAsync(CancellationToken ct = default);

    /// <summary>复核记录分页查询（关键字：姓名/身份证；状态：空=全部）</summary>
    Task<Result<PagedResult<ElderlyReview>>> GetReviewsPagedAsync(string keyword, string status, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 按待复核记录评估：来源为 Age90Monthly 时采用记录中已算好的旧→新类别/金额（月报自动入队），
    /// 其余来源按当前数据重评。
    /// </summary>
    Task<Result<ElderlyReviewEvaluation>> EvaluateReviewByPendingAsync(long reviewId, CancellationToken ct = default);

    /// <summary>按身份证查询复核历史（倒序）</summary>
    Task<Result<List<ElderlyReview>>> GetReviewsByPersonAsync(string idCard, CancellationToken ct = default);

    /// <summary>
    /// 确认复核：NoChange 仅落复核记录；Changed 同事务内补建旧档（仅名册人员）→ 停旧档（stop_reason=REVIEW）
    /// → 新建 Confirmed 新档（次月起按新档计发，不补差）→ 复核记录置 Completed。
    /// 返回新档案 ID（NoChange 时返回旧档案 ID 或 0）。
    /// </summary>
    Task<Result<long>> ConfirmReviewAsync(long? reviewId, long? applicationId, string? idCard, long? historyId,
        string reviewOpinion, string operatorName, CancellationToken ct = default);

    #endregion

    #region 月报：满90周岁调整备案表

    /// <summary>
    /// 满90周岁名单：所选自然月满90周岁的人员（在享 Confirmed CAT1/CAT2 + 名册 Active 未建档）。
    /// 旧类别取 89 岁档，新类别 CAT3；供《普惠高龄津贴调整备案表》逐人一页打印。
    /// </summary>
    Task<Result<List<ElderlyAge90Row>>> GetAge90AdjustRowsAsync(int year, int month, CancellationToken ct = default);

    /// <summary>
    /// 将所选月满90周岁名单写入「待复核」队列（trigger_source=Age90Monthly，按身份证去重）。
    /// 队列行携带已算好的旧→新类别/金额与应生效月，供复核办理按记录采信。
    /// </summary>
    Task<Result<int>> EnqueueAge90ReviewsAsync(int year, int month, CancellationToken ct = default);

    #endregion
}
