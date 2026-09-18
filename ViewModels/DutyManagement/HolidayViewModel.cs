using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Components;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.DutyManagement;

/// <summary>
/// 节假日维护 ViewModel：年度节假日列表、API 下载更新、手工增删
/// </summary>
public partial class HolidayViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IHolidayManageService _holidayManageService = null!;
    private readonly INewPermissionService _permissionService = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly ILoggerService _logger = null!;

    public HolidayViewModel(
        IServiceProvider serviceProvider,
        IHolidayManageService holidayManageService,
        INewPermissionService permissionService,
        IDialogService dialogService,
        ILoggerService logger)
    {
        _serviceProvider = serviceProvider;
        _holidayManageService = holidayManageService;
        _permissionService = permissionService;
        _dialogService = dialogService;
        _logger = logger;

        Title = "节假日维护";
    }

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    #region 权限

    [ObservableProperty]
    private bool _canViewDuty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManageOpacity))]
    private bool _canManageDuty;

    public double ManageOpacity => CanManageDuty ? 1.0 : 0.5;

    public async Task InitializePermissionsAsync(int userId)
    {
        CanViewDuty = await _permissionService.HasPermissionAsync(userId, PermissionCodes.DUTY_VIEW);
        CanManageDuty = await _permissionService.HasPermissionAsync(userId, PermissionCodes.DUTY_MANAGE);
    }

    #endregion

    #region 数据

    /// <summary>年份选项（当前年 ± 2）</summary>
    public List<int> YearOptions { get; } = Enumerable
        .Range(DateTime.Now.Year - 2, 5)
        .ToList();

    [ObservableProperty]
    private int _selectedYear = DateTime.Now.Year;

    partial void OnSelectedYearChanged(int value)
    {
        if (_loading) return;
        _ = LoadHolidaysAsync();
    }

    public ObservableCollection<SysHoliday> Holidays { get; } = new();

    /// <summary>节假日类型选项（手工新增用）</summary>
    public List<string> HolidayTypeOptions { get; } = new()
    {
        DutyConstants.HolidayRowTypes.HOLIDAY,
        DutyConstants.HolidayRowTypes.MAKEUP,
    };

    /// <summary>节假日类型显示名映射</summary>
    public string TypeName(string code) => DutyConstants.HolidayRowTypes.DisplayName(code);

    /// <summary>来源显示名映射</summary>
    public string SourceName(string code) => DutyConstants.Sources.DisplayName(code);

    /// <summary>加载守卫（防止年份初始化时重复加载）</summary>
    private bool _loading;

    #endregion

    #region 新增编辑区（内联）

    [ObservableProperty]
    private bool _isAdding;

    [ObservableProperty]
    private DateTime _newDate = DateTime.Today;

    [ObservableProperty]
    private string _newType = DutyConstants.HolidayRowTypes.HOLIDAY;

    [ObservableProperty]
    private string _newName = string.Empty;

    #endregion

    #region SnackBar

    [ObservableProperty]
    private string _snackBarMessage = string.Empty;

    [ObservableProperty]
    private SnackBarType _snackBarType;

    [ObservableProperty]
    private bool _isSnackBarVisible;

    private int _snackBarVersion;

    private Task ShowSnackBarAsync(string message, SnackBarType type = SnackBarType.Success)
    {
        SnackBarMessage = message;
        SnackBarType = type;
        IsSnackBarVisible = true;

        var version = ++_snackBarVersion;
        _ = HideSnackBarAfterDelayAsync(version);
        return Task.CompletedTask;
    }

    private async Task HideSnackBarAfterDelayAsync(int version)
    {
        try
        {
            await Task.Delay(3000);
            if (version == _snackBarVersion)
            {
                IsSnackBarVisible = false;
            }
        }
        catch
        {
            // fire-and-forget：忽略异常
        }
    }

    #endregion

    #region 命令

    [RelayCommand]
    private async Task LoadHolidaysAsync()
    {
        await ExecuteAsync(async ct =>
        {
            _loading = true;
            try
            {
                var result = await _holidayManageService.GetHolidaysAsync(SelectedYear, ct);
                if (result.IsFailure)
                {
                    await ShowFailureAsync(result, "加载节假日");
                    return;
                }

                Holidays.Clear();
                foreach (var holiday in result.Value ?? new List<SysHoliday>())
                {
                    Holidays.Add(holiday);
                }
            }
            finally
            {
                _loading = false;
            }
        }, "正在加载节假日...");
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (!CanManageDuty)
        {
            await _dialogService.DisplayAlertAsync("提示", "您没有管理节假日数据的权限", "确定");
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync(
            "下载节假日数据",
            $"确定要从节假日接口下载 {SelectedYear} 年数据吗？\n该年份的接口来源数据将被覆盖（手工维护的记录保留）。",
            "下载", "取消");
        if (!confirm) return;

        var downloaded = false;
        await ExecuteAsync(async ct =>
        {
            var result = await _holidayManageService.DownloadYearAsync(SelectedYear, ct);
            if (result.IsSuccess && result.Value != null)
            {
                downloaded = true;
                await ShowSnackBarAsync(
                    $"下载完成：法定节假日 {result.Value.HolidayCount} 天，调休补班 {result.Value.MakeupCount} 天");
            }
            else
            {
                await ShowFailureAsync(result, "下载节假日数据");
            }
        }, $"正在下载 {SelectedYear} 年节假日数据...");

        if (downloaded)
        {
            await LoadHolidaysAsync();
        }
    }

    [RelayCommand]
    private void StartAdd()
    {
        if (!CanManageDuty) return;
        IsAdding = true;
        NewDate = new DateTime(SelectedYear, 1, 1);
        NewType = DutyConstants.HolidayRowTypes.HOLIDAY;
        NewName = string.Empty;
    }

    [RelayCommand]
    private void CancelAdd() => IsAdding = false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!CanManageDuty || !IsAdding) return;

        if (string.IsNullOrWhiteSpace(NewName))
        {
            await _dialogService.DisplayAlertAsync("提示", "节日名称不能为空", "确定");
            return;
        }

        var saved = false;
        await ExecuteAsync(async ct =>
        {
            var result = await _holidayManageService.UpsertHolidayAsync(
                NewDate, NewType, NewName.Trim(), DutyConstants.Sources.MANUAL, ct);
            saved = result.IsSuccess;
            if (result.IsSuccess)
            {
                IsAdding = false;
                await ShowSnackBarAsync("节假日记录已保存");
            }
            else
            {
                await ShowFailureAsync(result, "保存节假日");
            }
        }, "正在保存...");

        if (saved)
        {
            // 数据变更后同步内存快照（失败仅提示，不影响落库结果）
            var refresh = await _holidayManageService.RefreshCacheAsync(CancellationToken);
            if (refresh.IsFailure)
            {
                _logger.Warn($"节假日快照刷新失败: {refresh.Message}");
            }
            await LoadHolidaysAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(SysHoliday? holiday)
    {
        if (!CanManageDuty || holiday == null) return;

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认删除",
            $"确定要删除 {holiday.DateDisplay}（{holiday.Name}）的节假日记录吗？",
            "删除", "取消");
        if (!confirm) return;

        var deleted = false;
        await ExecuteAsync(async ct =>
        {
            var result = await _holidayManageService.DeleteHolidayAsync(holiday.Id, ct);
            deleted = result.IsSuccess;
            if (!result.IsSuccess)
            {
                await ShowFailureAsync(result, "删除节假日");
            }
        }, "正在删除...");

        if (deleted)
        {
            var refresh = await _holidayManageService.RefreshCacheAsync(CancellationToken);
            if (refresh.IsFailure)
            {
                _logger.Warn($"节假日快照刷新失败: {refresh.Message}");
            }
            await ShowSnackBarAsync("节假日记录已删除");
            await LoadHolidaysAsync();
        }
    }

    #endregion

    #region 生命周期

    public override async Task OnAppearingAsync()
    {
        await LoadHolidaysAsync();
    }

    #endregion
}
