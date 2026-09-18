using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ElderlyBenefits;

/// <summary>
/// 普惠高龄下月待办列表 ViewModel（双 Tab：待新增 / 需停旧增新）
/// 列出具体待办老人名单（姓名/身份证/年龄/来源/状态标注），支持快捷办理。
/// </summary>
public partial class ElderlyPendingListViewModel : ViewModelBase
{
    private readonly IElderlyApplicationService _applicationService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IDialogService _dialogService = null!;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    /// <summary>待新增列表（正式表无 Confirmed 且历史名册无记录）</summary>
    [ObservableProperty]
    private ObservableCollection<ElderlyPendingItem> _pendingNewItems = new();

    /// <summary>需停旧增新列表（正式表 Confirmed 在享或历史名册有记录）</summary>
    [ObservableProperty]
    private ObservableCollection<ElderlyPendingItem> _pendingTransferItems = new();

    [ObservableProperty]
    private int _selectedTabIndex = 0;

    public bool IsTab0Selected => SelectedTabIndex == 0;
    public bool IsTab1Selected => SelectedTabIndex == 1;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTab0Selected));
        OnPropertyChanged(nameof(IsTab1Selected));
    }

    /// <summary>待新增计数文本</summary>
    [ObservableProperty]
    private string _pendingNewCountText = "0";

    /// <summary>需停旧增新计数文本</summary>
    [ObservableProperty]
    private string _pendingTransferCountText = "0";

    public ElderlyPendingListViewModel(
        IElderlyApplicationService applicationService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        IDialogService dialogService)
    {
        _applicationService = applicationService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _dialogService = dialogService;
        Title = "高龄津贴下月待办";
    }

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await LoadPendingAsync();
    }

    [RelayCommand]
    private void SwitchTab(string? tabIndex)
    {
        if (int.TryParse(tabIndex, out var idx))
        {
            SelectedTabIndex = idx;
        }
    }

    /// <summary>
    /// 加载下月待办明细并分流到两个 Tab
    /// </summary>
    private async Task LoadPendingAsync()
    {
        await ExecuteAsync(async () =>
        {
            var result = await _applicationService.GetPendingElderlyListAsync(CancellationToken);
            if (result.IsFailure)
                return result;

            PendingNewItems.Clear();
            PendingTransferItems.Clear();

            foreach (var item in result.Value ?? new List<ElderlyPendingItem>())
            {
                if (item.IsTransfer)
                    PendingTransferItems.Add(item);
                else
                    PendingNewItems.Add(item);
            }

            PendingNewCountText = PendingNewItems.Count.ToString("N0");
            PendingTransferCountText = PendingTransferItems.Count.ToString("N0");

            _logger.LogBusiness("加载高龄下月待办",
                ("PendingNew", PendingNewItems.Count),
                ("PendingTransfer", PendingTransferItems.Count));
            return Result.Success();
        }, "加载待办...");
    }

    /// <summary>
    /// 待新增：打开高龄申请表单（Create 模式，预填身份证/姓名，自动判类）
    /// </summary>
    [RelayCommand]
    private async Task HandleNewAsync(ElderlyPendingItem? item)
    {
        if (item == null) return;

        _logger.LogBusiness("高龄待办-待新增办理",
            ("Name", DataMasker.MaskName(item.Name)),
            ("IdCard", DataMasker.MaskIdCard(item.IdCard)));

        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyApplicationFormPage, ElderlyFormPageParameter>(
            new ElderlyFormPageParameter(FormOperationMode.Create,
                PrefillIdCard: item.IdCard,
                PrefillName: item.Name));
    }

    /// <summary>
    /// 需停旧增新：自动生成高龄申请草稿（增新），旧记录在享则跳转停发页停旧；
    /// 旧记录已停发/无在享则仅生成草稿。
    /// </summary>
    [RelayCommand]
    private async Task HandleTransferAsync(ElderlyPendingItem? item)
    {
        if (item == null) return;

        await ExecuteAsync(async () =>
        {
            _logger.LogBusiness("高龄待办-需停旧增新办理",
                ("Name", DataMasker.MaskName(item.Name)),
                ("IdCard", DataMasker.MaskIdCard(item.IdCard)),
                ("ElderlyApplicationId", item.ElderlyApplicationId),
                ("HistoryId", item.HistoryId));

            // 自动生成草稿（增新；已有草稿则复用，不会重复）
            var draftResult = await _applicationService.CreateAutoDraftAsync(
                item.IdCard, item.Name, App.CurrentUserName, CancellationToken);
            if (draftResult.IsFailure)
            {
                await _dialogService.DisplayAlertAsync("提示", $"生成草稿失败：{draftResult.Message}", "确定");
                return Result.Success();
            }
            var draftId = draftResult.Value;
            _logger.LogBusiness("高龄待办-需停旧增新草稿已生成",
                ("DraftId", draftId),
                ("IdCard", DataMasker.MaskIdCard(item.IdCard)));

            // 旧记录在享 → 跳转停发页停旧
            if (item.ElderlyApplicationId > 0)
            {
                var appResult = await _applicationService.GetByIdAsync(item.ElderlyApplicationId, CancellationToken);
                if (appResult.IsFailure || appResult.Value == null)
                {
                    await _dialogService.DisplayAlertAsync("提示",
                        $"[{item.Name}] 在享登记加载失败，草稿已生成，请刷新后重试。", "确定");
                    return Result.Success();
                }

                await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyStopPage, ElderlyApplication>(appResult.Value);
                return Result.Success();
            }

            // 旧记录已停发/无在享：仅生成草稿（增新）
            await _dialogService.DisplayAlertAsync("提示",
                $"[{item.Name}] 已自动生成高龄申请草稿，请前往「高龄津贴申请登记」草稿页补全信息后确认。", "确定");
            return Result.Success();
        }, "生成草稿...");
    }

    /// <summary>
    /// 刷新待办列表（处理后返回页面时自动重新加载）
    /// </summary>
    [RelayCommand]
    private Task RefreshAsync() => LoadPendingAsync();

    // 返回上一页：使用基类 GoBackCommand（含栈守卫与窗口标题恢复）
}
