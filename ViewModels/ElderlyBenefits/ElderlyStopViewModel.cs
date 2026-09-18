using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.NavigationData;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ElderlyBenefits;

public partial class ElderlyStopViewModel : ViewModelBase
{
    private readonly IElderlyApplicationService _applicationService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IDialogService _dialogService = null!;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    private long _applicationId;

    [ObservableProperty]
    private string _applicantName = string.Empty;

    [ObservableProperty]
    private string _applicantIdCard = string.Empty;

    [ObservableProperty]
    private string _applicationNo = string.Empty;

    [ObservableProperty]
    private string _categoryName = string.Empty;

    [ObservableProperty]
    private string _issueStartMonth = string.Empty;

    [ObservableProperty]
    private decimal _issueAmount;

    [ObservableProperty]
    private string _gender = string.Empty;

    [ObservableProperty]
    private string _ageDisplay = string.Empty;

    [ObservableProperty]
    private DateTime _dueStopDate = DateTime.Today;

    /// <summary>停发原因选项（key, 中文显示名）— 取消备案表模板使用的五项</summary>
    public IReadOnlyList<KeyValuePair<string, string>> StopReasonOptions { get; } =
        ElderlyBenefitConstants.StopReasonPickerOptions
            .Select(k => new KeyValuePair<string, string>(k, ElderlyBenefitConstants.GetStopReasonName(k)))
            .ToArray();

    [ObservableProperty]
    private KeyValuePair<string, string>? _selectedStopReason;

    /// <summary>选中"其他"时是否显示原因说明输入框</summary>
    [ObservableProperty]
    private bool _isOtherReasonVisible;

    /// <summary>选中"去世"时是否显示死亡日期输入框</summary>
    [ObservableProperty]
    private bool _isDeathDateVisible;

    /// <summary>死亡日期（选中去世时必填）</summary>
    [ObservableProperty]
    private DateTime _deathDate = DateTime.Today;

    /// <summary>"其他"停发原因说明（选中其他时必填，原样入库）</summary>
    [ObservableProperty]
    private string _otherReasonText = string.Empty;

    partial void OnSelectedStopReasonChanged(KeyValuePair<string, string>? value)
    {
        IsOtherReasonVisible = value?.Key == ElderlyBenefitConstants.StopReasonOther;
        IsDeathDateVisible = value?.Key == ElderlyBenefitConstants.StopReasonDeceased;
        OnPropertyChanged(nameof(CanConfirm));
    }

    [ObservableProperty]
    private bool _isRecover;

    partial void OnIsRecoverChanged(bool value)
    {
        OnPropertyChanged(nameof(CanConfirm));
    }

    /// <summary>追缴起算年/月（yyyy-MM 由两项组合）</summary>
    public ObservableCollection<int> RecoverStartYearOptions { get; } = new();
    public ObservableCollection<int> RecoverStartMonthOptions { get; } = new(Enumerable.Range(1, 12));

    /// <summary>追缴止算年/月（yyyy-MM 由两项组合）</summary>
    public ObservableCollection<int> RecoverEndYearOptions { get; } = new();
    public ObservableCollection<int> RecoverEndMonthOptions { get; } = new(Enumerable.Range(1, 12));

    [ObservableProperty]
    private int _recoverStartYear;

    [ObservableProperty]
    private int _recoverStartMonth;

    [ObservableProperty]
    private int _recoverEndYear;

    [ObservableProperty]
    private int _recoverEndMonth;

    [ObservableProperty]
    private string _recoverAmountText = string.Empty;

    [ObservableProperty]
    private string _remark = string.Empty;

    public ElderlyStopViewModel(
        IElderlyApplicationService applicationService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        IDialogService dialogService)
    {
        _applicationService = applicationService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _dialogService = dialogService;

        var now = DateTime.Today;
        var startYear = ElderlyBenefitConstants.PolicyStartDate.Year;
        for (var y = startYear; y <= now.Year; y++)
        {
            RecoverStartYearOptions.Add(y);
            RecoverEndYearOptions.Add(y);
        }
        RecoverStartYear = now.Year;
        RecoverStartMonth = now.Month;
        RecoverEndYear = now.Year;
        RecoverEndMonth = now.Month;
    }

    public void LoadApplication(ElderlyApplication app)
    {
        _applicationId = app.Id;
        ApplicantName = app.Name;
        ApplicantIdCard = app.IdCard;
        ApplicationNo = app.ApplicationNo;
        CategoryName = ElderlyBenefitConstants.GetCategoryName(app.Category);
        IssueStartMonth = app.IssueStartMonth ?? string.Empty;
        IssueAmount = app.IssueAmount;
        Gender = app.Gender ?? string.Empty;
        AgeDisplay = app.BirthDate.HasValue
            ? $"{(DateTime.Today.Year - app.BirthDate.Value.Year)} 周岁"
            : string.Empty;
        DueStopDate = DateTime.Today;
        SelectedStopReason = null;
        OtherReasonText = string.Empty;
        IsDeathDateVisible = false;
        DeathDate = DateTime.Today;
        IsRecover = false;
        RecoverAmountText = string.Empty;
        Remark = string.Empty;
    }

    public bool CanConfirm =>
        SelectedStopReason != null && (!IsRecover || RecoverEndYear >= RecoverStartYear);

    [RelayCommand]
    private async Task ConfirmStopAsync()
    {
        if (SelectedStopReason == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请选择停发原因", "确定");
            return;
        }

        // 落库值：非"其他"存 key，显示/打印映射中文；"其他"存说明文字
        string stopReasonValue;
        if (SelectedStopReason.Value.Key == ElderlyBenefitConstants.StopReasonOther)
        {
            if (string.IsNullOrWhiteSpace(OtherReasonText))
            {
                await _dialogService.DisplayAlertAsync("提示", "请填写“其他”停发原因说明", "确定");
                return;
            }
            stopReasonValue = OtherReasonText.Trim();
        }
        else
        {
            stopReasonValue = SelectedStopReason.Value.Key;
        }

        if (IsRecover)
        {
            if (RecoverStartYear < 1 || RecoverStartMonth < 1 || RecoverEndYear < 1 || RecoverEndMonth < 1)
            {
                await _dialogService.DisplayAlertAsync("提示", "请选择追缴起止月份", "确定");
                return;
            }
            if (RecoverEndYear < RecoverStartYear ||
                (RecoverEndYear == RecoverStartYear && RecoverEndMonth < RecoverStartMonth))
            {
                await _dialogService.DisplayAlertAsync("提示", "追缴止算月不能早于起算月", "确定");
                return;
            }
        }

        var recoverStartMonth = IsRecover ? $"{RecoverStartYear}-{RecoverStartMonth:D2}" : string.Empty;
        var recoverEndMonth = IsRecover ? $"{RecoverEndYear}-{RecoverEndMonth:D2}" : string.Empty;

        decimal recoverAmount = 0;
        if (IsRecover && !string.IsNullOrWhiteSpace(RecoverAmountText))
        {
            if (!decimal.TryParse(RecoverAmountText, out recoverAmount))
            {
                await _dialogService.DisplayAlertAsync("提示", "追缴金额格式不正确", "确定");
                return;
            }
        }

        var confirm = await _dialogService.DisplayAlertAsync("确认停发",
            $"确定要停发 [{ApplicationNo}] {ApplicantName} 的高龄补贴吗？", "停发", "取消");
        if (!confirm) return;

        await ExecuteAsync(async () =>
        {
            var deathDate = IsDeathDateVisible ? (DateTime?)DeathDate : null;

            var result = await _applicationService.StopAsync(
                _applicationId,
                stopReasonValue,
                App.CurrentUserName,
                DueStopDate,
                deathDate,
                IsRecover,
                recoverStartMonth,
                recoverEndMonth,
                recoverAmount,
                Remark?.Trim() ?? string.Empty,
                CancellationToken);

            if (result.IsSuccess)
            {
                _logger.LogBusiness("普惠高龄登记停发成功", ("ApplicationId", _applicationId));
                await _dialogService.ShowSnackBarAsync("停发成功");

                // 先关闭停发登记页，再推入档案输出页：
                // 导航栈由 …→停发办理列表→停发登记→档案输出 变为 …→停发办理列表→档案输出，
                // 打印完成后 GoBack 直接回到「停发办理」列表页。
                await CloseAsync();
                await NavigateToArchiveOutputAsync();
            }
            return result;
        }, "停发登记...");
    }

    /// <summary>
    /// 停发成功 → 跳转档案输出页（仿新增流程，分类 Stop 命中"普惠高龄/停止"模板）
    /// 数据补全模式下 SkipArchiveOutput=true 时直接返回，跳过档案输出。
    /// </summary>
    private async Task NavigateToArchiveOutputAsync()
    {
        var savedResult = await _applicationService.GetByIdAsync(_applicationId, CancellationToken);
        if (savedResult.IsSuccess && savedResult.Value != null)
        {
            var fields = ElderlyPrintDataBuilder.BuildSingleFields(savedResult.Value);
            PrintNavigationData.BusinessType = "ElderlyBenefits";
            PrintNavigationData.BusinessId = _applicationId;
            PrintNavigationData.Classification = "Stop";
            PrintNavigationData.FieldData = fields;
            PrintNavigationData.TableData = new();
            PrintNavigationData.SupporterTableData = null;

            await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveOutputPage>();
        }
        else
        {
            await CloseAsync();
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await CloseAsync();
    }

    private async Task CloseAsync()
    {
        if (Helpers.WindowNavigator.CurrentPage?.Navigation != null)
        {
            await Helpers.WindowNavigator.CurrentPage.Navigation.PopAsync();
            RestoreWindowTitleFromNavigation();
        }
    }
}
