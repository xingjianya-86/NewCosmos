using CommunityToolkit.Mvvm.ComponentModel;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.ViewModels;
using NewCosmos.ViewModels.ArchiveManagement;

namespace NewCosmos.ViewModels.Reprint;

/// <summary>
/// 统一补打中心·域补打模式：
/// ArchiveSet = 档案集（低收入/临时救助/普惠高龄，组合 ArchiveOutputViewModel 输出整套档案）；
/// AssetVerification = 资产核查（单模板直打 + 跨户批量）；
/// DynamicRecord = 动态管理档案（单模板 + 留痕原样补打/重新生成）。
/// </summary>
public enum ReprintDomainMode
{
    ArchiveSet,
    AssetVerification,
    DynamicRecord
}

/// <summary>
/// 补打中心·月份窗口策略：决定选中某年某月后，按哪个日期区间过滤该域记录。
/// 各域服务端按月查询均为自然月口径，本策略在客户端将其收敛到业务月份窗口。
/// </summary>
public enum ReprintMonthWindow
{
    /// <summary>自然月 [本月1日, 次月1日)</summary>
    NaturalMonth,

    /// <summary>B线业务线 [上月(结算日+1)日, 本月(结算日+1)日]（结算日由 app.ini BCycleSettleDay 配置）</summary>
    BusinessProcess,

    /// <summary>A线经济核查 [上月11日, 本月10日]</summary>
    EconomicReview,

    /// <summary>C线临时救助完整窗口 [入户调查起日, 验收日]</summary>
    TempReliefFull
}

/// <summary>
/// 统一列表项：五域记录的统一投影（跨域按人聚合后仍能定位回原记录）。
/// BusinessTime 为该域业务时间（档案=更新时间/临时救助=申请时间/高龄=申请时间/核查=创建时间/动态管理=最近变更时间）。
/// Status 保留原始状态码（下游 PrintNavigationData.Status 依赖英文码做模板分类，如 ArchiveOutput 的 "Stopped" 判断）；
/// 中文展示一律放 Extra。
/// </summary>
public sealed class ReprintArchiveItem : ObservableObject
{
    public ReprintArchiveItem(
        string DomainKey, long BusinessId, string Name, string IdCard,
        string No, string Status, DateTime BusinessTime, string Extra)
    {
        this.DomainKey = DomainKey;
        this.BusinessId = BusinessId;
        this.Name = Name;
        this.IdCard = IdCard;
        this.No = No;
        this.Status = Status;
        this.BusinessTime = BusinessTime;
        this.Extra = Extra;
    }

    public string DomainKey { get; }
    public long BusinessId { get; }
    public string Name { get; }
    public string IdCard { get; }
    public string No { get; }
    public string Status { get; }
    public DateTime BusinessTime { get; }
    public string Extra { get; }

    /// <summary>档案链类型（chain_type）：CategoryRebuild/HeadChange/HouseholdDeath/SingleRescue/ImportedArchive</summary>
    public string ChainType { get; set; } = string.Empty;

    /// <summary>档案版本标签：Stopped=历史档案；单人保=户内单人保；停旧建新接续=现状档案（接续）；其余=现状档案</summary>
    public string ArchiveVersionText =>
        string.Equals(Status, "Stopped", StringComparison.OrdinalIgnoreCase)
            ? "历史档案"
            : ChainType switch
            {
                ChainTypeConstants.SINGLE_RESCUE => "户内单人保",
                ChainTypeConstants.CATEGORY_REBUILD or ChainTypeConstants.HEAD_CHANGE or ChainTypeConstants.HOUSEHOLD_DEATH
                    => "现状档案（接续）",
                _ => "现状档案"
            };

    /// <summary>是否有档案状态（动态管理域 Status 为空，不展示版本标签）</summary>
    public bool HasArchiveVersion => !string.IsNullOrEmpty(Status);

    private bool _isSelected;
    /// <summary>选中高亮（VM 选择变化时维护，根除 CollectionView VSM 残留）</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>
/// 统一准备产物：选中记录后构建，供填充 PrintNavigationData 并进入 ArchiveOutput 流程。
/// </summary>
public sealed record ReprintArchivePayload(
    string DomainKey,
    long BusinessId,
    string Name,
    string IdCard,
    string Classification,
    string Status,
    Dictionary<string, string> FieldData,
    List<Dictionary<string, string>> TableData,
    List<Dictionary<string, string>>? SupporterTableData);

/// <summary>
/// 域补打策略：统一补打中心按 DomainKey 分派搜索/准备/留痕查询。
/// 逻辑自对应旧补打页平移（零重写）；新域接入 = 注册一个 Provider。
/// </summary>
public interface IReprintDomainProvider
{
    /// <summary>域键（与打印留痕 business_type 一致）</summary>
    string DomainKey { get; }

    /// <summary>域显示名（分类分支标题）</summary>
    string DisplayName { get; }

    /// <summary>补打模式</summary>
    ReprintDomainMode Mode { get; }

    /// <summary>该域按月筛选时使用的月份窗口策略</summary>
    ReprintMonthWindow MonthWindow { get; }

    /// <summary>
    /// 按关键词（姓名/身份证）搜索该域记录（全状态聚合；草稿等无补打意义的记录由各域自行排除）。
    /// </summary>
    Task<Result<List<ReprintArchiveItem>>> SearchByPersonAsync(string keyword, int limit = 20, CancellationToken ct = default);

    /// <summary>
    /// 按月查询该域记录（服务端整月口径，与名单业务时间一致；用于月份筛选覆盖"最近 N 条"缓存之外的记录）。
    /// </summary>
    Task<Result<List<ReprintArchiveItem>>> SearchByMonthAsync(int year, int month, int limit = 200, CancellationToken ct = default);

    /// <summary>选中记录后构建打印数据（ArchiveSet 模式供 Output 输出；DynamicRecord 模式返回字段供重新生成）</summary>
    Task<Result<ReprintArchivePayload>> PrepareAsync(long businessId, CancellationToken ct = default);
}

/// <summary>资产核查域扩展能力（单模板直打/跨户批量）</summary>
public interface IAssetVerificationReprintCapability
{
    /// <summary>核查类模板列表（categories=["AssetVerification"]）</summary>
    Task<List<TemplateSelectItem>> GetTemplatesAsync(CancellationToken ct = default);

    /// <summary>渲染预览 PDF，返回临时文件路径</summary>
    Task<Result<string>> RenderPreviewAsync(long recordId, TemplateSelectItem template, CancellationToken ct = default);

    /// <summary>单户直打（保存文件→送打印机；generatePdf 时另存 PDF）</summary>
    Task<Result<string>> PrintSingleAsync(long recordId, TemplateSelectItem template, bool generatePdf, CancellationToken ct = default);

    /// <summary>跨户批量直打（进度回调：当前/总数/户名），返回 (成功数, 失败明细)</summary>
    Task<Result<(int SuccessCount, List<(string Name, string Error)> Failed)>> BatchPrintAsync(
        IReadOnlyList<long> recordIds,
        TemplateSelectItem template,
        bool generatePdf,
        Action<int, int, string>? progress,
        CancellationToken ct = default);
}

/// <summary>动态管理域扩展能力（留痕原样补打/重新生成/按月批量导出）</summary>
public interface IDynamicRecordReprintCapability
{
    /// <summary>留痕行（含原样补打所需的 pdf 可用性）</summary>
    Task<Result<List<DynamicPrintHistoryItem>>> GetPrintHistoryAsync(long applicationId, CancellationToken ct = default);

    /// <summary>原样补打：导出留痕 PDF 到临时文件，返回文件路径（无 pdf 数据且文件缺失 → 失败）</summary>
    Task<Result<string>> ReprintOriginalAsync(long printRecordId, CancellationToken ct = default);

    /// <summary>重新生成：装配导出并写新留痕，返回文件路径。
    /// normalizeToLatest=true 归一到现役档案；false 按所选档案版本（如历史旧档案）原样生成。</summary>
    Task<Result<string>> RegenerateAsync(long applicationId, bool normalizeToLatest = true, CancellationToken ct = default);

    /// <summary>按月变更人群（档案链归一后，批量预览列表用）</summary>
    Task<Result<List<ReprintArchiveItem>>> SearchMonthlyChangedAsync(int year, int month, CancellationToken ct = default);

    /// <summary>按B线周期区间(含端点)变更人群批量导出（逐户渲染合并为一份 PDF + 留痕），返回文件路径</summary>
    Task<Result<string>> BatchExportRangeAsync(DateTime fromInclusive, DateTime toInclusive, CancellationToken ct = default);

    /// <summary>按指定打印机打印单户动态管理记录（生成源文件+PDF并留痕），返回成功数与失败明细</summary>
    Task<Result<(int SuccessCount, List<(string Name, string Error)> Failed)>> PrintAsync(
        long applicationId, string printerName, int copies, CancellationToken ct = default);
}

/// <summary>动态管理留痕展示项（PrintRecordDisplayItem 的动态管理扩展版，携带补打所需信息）</summary>
public sealed record DynamicPrintHistoryItem(
    long Id,
    string TemplateName,
    string DisplayTime,
    string OperatorName,
    string Status,
    string Remark,
    bool HasPdfData,
    string PdfPath);

/// <summary>跨域按人聚合结果：一个人 + 其命中域分支</summary>
public sealed class ReprintPerson : ObservableObject
{
    public ReprintPerson(string IdCard, string Name, List<ReprintArchiveItem> Records)
    {
        this.IdCard = IdCard;
        this.Name = Name;
        this.Records = Records;
    }

    public string IdCard { get; }
    public string Name { get; }
    public List<ReprintArchiveItem> Records { get; }

    /// <summary>命中域键去重列表（保持搜索顺序）</summary>
    public List<string> DomainKeys => Records.Select(r => r.DomainKey).Distinct().ToList();

    /// <summary>最近业务时间</summary>
    public DateTime LatestTime => Records.Count == 0 ? DateTime.MinValue : Records.Max(r => r.BusinessTime);

    private string _domainSummary = string.Empty;
    /// <summary>命中域显示名明细（VM 聚合时按 Provider DisplayName 生成，如"低收入人口·资产核查"）</summary>
    public string DomainSummary
    {
        get => _domainSummary;
        set => SetProperty(ref _domainSummary, value);
    }

    private bool _isSelected;
    /// <summary>选中高亮（VM 选择变化时维护）</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>分类分支（左栏点人名后出现的该名下类别）</summary>
public sealed class ReprintBranch : ObservableObject
{
    public ReprintBranch(string DomainKey, string DisplayName, ReprintDomainMode Mode, List<ReprintArchiveItem> Records)
    {
        this.DomainKey = DomainKey;
        this.DisplayName = DisplayName;
        this.Mode = Mode;
        this.Records = Records;
    }

    public string DomainKey { get; }
    public string DisplayName { get; }
    public ReprintDomainMode Mode { get; }
    public List<ReprintArchiveItem> Records { get; }

    public int Count => Records.Count;
    public DateTime LatestTime => Records.Count == 0 ? DateTime.MinValue : Records.Max(r => r.BusinessTime);

    private bool _isSelected;
    /// <summary>选中高亮（VM 选择变化时维护）</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
