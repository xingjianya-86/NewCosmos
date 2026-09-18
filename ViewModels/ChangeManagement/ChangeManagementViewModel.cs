using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.ViewModels.ChangeManagement;

public partial class ChangeManagementViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerService _logger;
    private readonly IChangeService _changeService;
    private readonly IDialogService _dialogService;

    public ChangeManagementViewModel(
        IServiceProvider serviceProvider,
        ILoggerService logger,
        IChangeService changeService,
        IDialogService dialogService)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _changeService = changeService;
        _dialogService = dialogService;
        Title = "保障对象动态管理";
    }

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    [ObservableProperty]
    private long _applicationId;

    [ObservableProperty]
    private string _currentClassification = string.Empty;

    [ObservableProperty]
    private decimal _currentAmount;

    [ObservableProperty]
    private string _selectedChangeType = "EconomicChange";

    [ObservableProperty]
    private string _changeReason = string.Empty;

    [ObservableProperty]
    private bool _showEconomicForm;

    [ObservableProperty]
    private bool _showMemberForm;

    [ObservableProperty]
    private bool _showHeadOnlyForm;

    [ObservableProperty]
    private bool _showHeadMemberForm;

    [ObservableProperty]
    private string _beforeJson = string.Empty;

    [ObservableProperty]
    private string _afterJson = string.Empty;

    partial void OnSelectedChangeTypeChanged(string value)
    {
        ShowEconomicForm = value == "EconomicChange";
        ShowMemberForm = value == "MemberChange";
        ShowHeadOnlyForm = value == "HeadOnlyChange";
        ShowHeadMemberForm = value == "HeadWithMemberChange";
    }

    [RelayCommand]
    private async Task SubmitChangeAsync()
    {
        var context = new ChangeContext
        {
            ApplicationId = ApplicationId,
            ChangeType = SelectedChangeType,
            ChangeReason = ChangeReason,
            ChangeDate = System.DateTime.Today
        };

        var result = await _changeService.CreateChangeAsync(context, BeforeJson, AfterJson);
        if (result.IsFailure)
        {
            await ShowFailureAsync(result, "记录变更");
            return;
        }

        await _dialogService.DisplayAlertAsync("成功", "变更记录已保存", "确定");
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await Task.CompletedTask;
    }
}
