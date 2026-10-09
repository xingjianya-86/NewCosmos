using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Enums;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.Printing;
using NewCosmos.Services.Platform;
using NewCosmos.Services.Utilities;
using NewCosmos.ViewModels.ArchiveManagement;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Reprint.Providers;

namespace NewCosmos.ViewModels.Reprint;

/// <summary>
/// 统一补打中心 ViewModel（替代原低收入/临时救助/普惠高龄/资产核查/动态管理五个分散补打入口）：
/// Tab1 按人补打：左栏跨域按人搜索（身份证聚合）→ 分类分支（支持月份过滤）→
///   中栏文书清单/历史打印记录/打印设置 → 右栏 PDF 预览；
/// Tab2 批量打印：域 + 月份/日期范围 + 关键词 + 模板 → 逐户渲染打印 → 进度/失败汇总。
/// ArchiveSet 域组合 ArchiveOutputViewModel（模板集/预览/打印/一键打印复用其逻辑）。
/// </summary>
public partial class UnifiedReprintViewModel : ViewModelBase
{
    private readonly IReadOnlyList<IReprintDomainProvider> _providers;
    private readonly IPrintRecordService _printRecordService;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerService _logger;
    private readonly IPrinterService _printerService;
    private readonly ITemplateService _templateService;
    private readonly IBusinessTimelineService _timelineService;

    /// <summary>组合的档案输出 VM：ArchiveSet 域的模板加载/预览/打印/一键打印全部复用其逻辑</summary>
    public ArchiveOutputViewModel Output { get; }

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    #region 域与模式

    /// <summary>域 Provider（注册顺序 = 分支展示顺序）</summary>
    public IReadOnlyList<IReprintDomainProvider> Providers => _providers;

    private IReprintDomainProvider? _activeProvider;
    private IAssetVerificationReprintCapability? AssetCapability =>
        _activeProvider as IAssetVerificationReprintCapability;
    private IDynamicRecordReprintCapability? DynamicCapability =>
        _activeProvider as IDynamicRecordReprintCapability;

    #endregion

    public UnifiedReprintViewModel(
        IEnumerable<IReprintDomainProvider> providers,
        ArchiveOutputViewModel outputViewModel,
        IPrintRecordService printRecordService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        IPrinterService printerService,
        IBusinessTimelineService timelineService)
    {
        _providers = providers.ToList();
        DomainFilterOptions = new List<string> { "全部" }
            .Concat(_providers.Select(p => p.DisplayName)).ToList();
        _printRecordService = printRecordService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _printerService = printerService;
        _templateService = serviceProvider.GetRequiredService<ITemplateService>();
        _timelineService = timelineService;
        Output = outputViewModel;
        Title = "历史补打中心";

        Output.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ArchiveOutputViewModel.IsBusy))
                OnPropertyChanged(nameof(IsOverallBusy));
        };
    }

    protected override void OnBusyStateChanged() => OnPropertyChanged(nameof(IsOverallBusy));

    /// <summary>
    /// 带初始域进入（主页入口定向）：切回按人 Tab 并把批量域预置到该域
    /// （按人模式跨域聚合无需预设域；批量 Picker 预置方便直接批量）。
    /// </summary>
    public void PrepareForDomain(string domainKey)
    {
        IsBatchMode = false;
        var index = _providers.ToList().FindIndex(p => string.Equals(p.DomainKey, domainKey, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            BatchDomainIndex = index;
    }

    /// <summary>进入页面不预加载名单：由用户选择月份后触发（OnMonthFilterIndexChanged → RefreshPersonsAsync）；
    /// 若直接落在批量 Tab，则初始化批量域模板与名单。</summary>
    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        if (IsBatchMode)
            await EnsureBatchInitializedAsync();
    }

    /// <summary>
    /// 返回上一页：清理 PII 与预览临时文件，再走基类返回逻辑
    /// </summary>
    public override async Task GoBackAsync()
    {
        Output.Cleanup();
        await base.GoBackAsync();
    }

}
