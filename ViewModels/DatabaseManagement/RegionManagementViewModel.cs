using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Shared;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace NewCosmos.ViewModels.DatabaseManagement;

public partial class RegionManagementViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IRegionService _regionService = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly INewPermissionService _permissionService = null!;

    [ObservableProperty]
    private bool _canManageRegion;

    [ObservableProperty]
    private bool _isLoading;

    public ProgressViewModel Progress { get; } = new();

    public ObservableCollection<RegionCity> Cities { get; } = new();
    public ObservableCollection<RegionCounty> Counties { get; } = new();
    public ObservableCollection<RegionTown> Towns { get; } = new();
    public ObservableCollection<RegionVillage> Villages { get; } = new();

    [ObservableProperty]
    private RegionCity _selectedCity = null!;

    [ObservableProperty]
    private RegionCounty _selectedCounty = null!;

    [ObservableProperty]
    private RegionTown _selectedTown = null!;

    [ObservableProperty]
    private RegionVillage _selectedVillage = null!;

    public bool? IsCountyLevel => SelectedCity != null && SelectedCounty != null && SelectedTown == null;
    public bool? IsTownLevel => SelectedTown != null && SelectedVillage == null;
    public bool? IsVillageLevel => SelectedVillage != null;

    public string CityHeaderText => $"地级市（{Cities.Count}）";
    public string CountyHeaderText => SelectedCity != null ? $"县区（{Counties.Count}）" : "请选择地级市";
    public string TownHeaderText => SelectedCounty != null ? $"乡镇/街道（{Towns.Count}）" : "请选择县区";
    public string VillageHeaderText => SelectedTown != null ? $"村/社区（{Villages.Count}）" : "请选择乡镇";
    public string RegionHeaderText => SelectedCounty?.CountyName ?? SelectedCity?.CityName ?? "行政区划管理";

    public double ManageOpacity => CanManageRegion ? 1.0 : 0.5;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isAdding;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editCode = string.Empty;

    [ObservableProperty]
    private string _editCityName = string.Empty;

    [ObservableProperty]
    private string _editTownType = string.Empty;

    [ObservableProperty]
    private string _editVillageType = string.Empty;

    public RegionManagementViewModel(
        IServiceProvider serviceProvider,
        IRegionService regionService,
        IDialogService dialogService,
        ILoggerService logger,
        INewPermissionService permissionService)
    {
        _serviceProvider = serviceProvider;
        _regionService = regionService;
        _dialogService = dialogService;
        _logger = logger;
        _permissionService = permissionService;

        Title = "行政区划管理";
    }

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    public async Task InitializePermissionsAsync(int userId)
    {
        // [PERF-PROBE] 阶段0归因埋点：子页进入时的权限查询耗时
        var probe = Stopwatch.StartNew();
        CanManageRegion = await _permissionService.HasPermissionAsync(userId, PermissionCodes.REGION_MANAGE);
        probe.Stop();
        _logger.LogPerf("区划管理-权限初始化", probe.Elapsed.TotalMilliseconds, ("UserId", userId));
        OnPropertyChanged(nameof(ManageOpacity));
    }

    public override async Task OnAppearingAsync()
    {
        // [PERF-PROBE] 阶段0归因埋点：子页数据加载耗时
        var probe = Stopwatch.StartNew();
        await LoadCitiesAsync();
        probe.Stop();
        _logger.LogPerf("区划管理-OnAppearing", probe.Elapsed.TotalMilliseconds);
    }

    /// <summary>页面可见后异步执行权限检查 + 数据加载（C 组：不阻塞 PushAsync）</summary>
    public void StartLoadingInBackground() => SafeFireAndForget(async () =>
    {
        await InitializePermissionsAsync(App.CurrentUserId ?? 1);
        await OnAppearingAsync();
    }, nameof(StartLoadingInBackground));

    private async Task LoadCitiesAsync()
    {
        if (IsLoading) return;
        IsLoading = true;

        try
        {
            _logger.Info("加载地级市列表");
            var result = await _regionService.GetCitiesAsync(default);
            if (result.IsSuccess && result.Value is not null)
            {
                Cities.Clear();
                foreach (var city in result.Value)
                    Cities.Add(city);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败");
            await _dialogService.DisplayAlertAsync("错误", $"加载地级市列表失败: {ex.Message}", "确定");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadCountiesAsync()
    {
        if (IsLoading) return;
        IsLoading = true;

        try
        {
            _logger.Info($"执行操作");
            var result = await _regionService.GetCountiesAsync(default);
            if (result.IsSuccess && result.Value is not null)
            {
                Counties.Clear();
                foreach (var county in result.Value)
                    Counties.Add(county);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败");
            await _dialogService.DisplayAlertAsync("错误", $"加载县区列表失败: {ex.Message}", "确定");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedCityChanged(RegionCity value)
    {
        SelectedCounty = null;
        SelectedTown = null;
        SelectedVillage = null;
        Counties.Clear();
        Towns.Clear();
        Villages.Clear();
        NotifyLevelChanged();

        if (value != null)
        {
            SafeFireAndForget(() => LoadCountiesByCityAsync(value.Id));
        }
    }

    private async Task LoadCountiesByCityAsync(int cityId)
    {
        if (IsLoading) return;

        try
        {
            var result = await _regionService.GetCountiesByCityIdAsync(cityId);
            if (result.IsSuccess && result.Value is not null)
            {
                Counties.Clear();
                foreach (var county in result.Value)
                    Counties.Add(county);
            }
            OnPropertyChanged(nameof(CountyHeaderText));
            OnPropertyChanged(nameof(RegionHeaderText));
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败: {ex.Message}");
        }
    }

    partial void OnSelectedCountyChanged(RegionCounty value)
    {
        SelectedTown = null;
        SelectedVillage = null;
        Towns.Clear();
        Villages.Clear();
        NotifyLevelChanged();

        if (value != null)
        {
            SafeFireAndForget(() => LoadTownsAsync(value.Id));
        }
    }

    partial void OnSelectedTownChanged(RegionTown value)
    {
        SelectedVillage = null;
        Villages.Clear();
        NotifyLevelChanged();

        if (value != null)
        {
            SafeFireAndForget(() => LoadVillagesAsync(value.Id));
        }
    }

    partial void OnSelectedVillageChanged(RegionVillage value)
    {
        NotifyLevelChanged();
    }

    private void NotifyLevelChanged()
    {
        OnPropertyChanged(nameof(IsCountyLevel));
        OnPropertyChanged(nameof(IsTownLevel));
        OnPropertyChanged(nameof(IsVillageLevel));
        OnPropertyChanged(nameof(CityHeaderText));
        OnPropertyChanged(nameof(CountyHeaderText));
        OnPropertyChanged(nameof(TownHeaderText));
        OnPropertyChanged(nameof(VillageHeaderText));
        OnPropertyChanged(nameof(RegionHeaderText));
    }

    private async Task LoadTownsAsync(int countyId)
    {
        try
        {
            var result = await _regionService.GetTownsByCountyIdAsync(countyId);
            if (result.IsSuccess && result.Value is not null)
            {
                Towns.Clear();
                foreach (var town in result.Value)
                    Towns.Add(town);
            }
            OnPropertyChanged(nameof(TownHeaderText));
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败");
            await _dialogService.DisplayAlertAsync("错误", $"加载乡镇列表失败: {ex.Message}", "确定");
        }
    }

    private async Task LoadVillagesAsync(int townId)
    {
        try
        {
            var result = await _regionService.GetVillagesByTownIdAsync(townId);
            if (result.IsSuccess && result.Value is not null)
            {
                Villages.Clear();
                foreach (var village in result.Value)
                    Villages.Add(village);
            }
            OnPropertyChanged(nameof(VillageHeaderText));
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败");
            await _dialogService.DisplayAlertAsync("错误", $"加载村/社区列表失败: {ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private void StartEditCounty()
    {
        if (!CanManageRegion || SelectedCounty == null) return;

        IsAdding = false;
        IsEditing = true;
        EditName = SelectedCounty.CountyName;
        EditCode = SelectedCounty.CountyCode ?? string.Empty;
        EditCityName = SelectedCounty.CityName ?? string.Empty;
    }

    [RelayCommand]
    private async Task DeleteCountyAsync()
    {
        if (!CanManageRegion || SelectedCounty == null) return;

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认删除",
            $"确定要删除县区\"{SelectedCounty.CountyName}\" 吗？",
            "删除", "确定");

        if (!confirm) return;

        var result = await _regionService.DeleteCountyAsync(SelectedCounty.Id);
        if (result.IsSuccess)
        {
            if (SelectedCity != null)
                await LoadCountiesByCityAsync(SelectedCity.Id);
            _logger.Info($"执行操作");
        }
        else
        {
            await ShowFailureAsync(result, "删除");
        }
    }

    [RelayCommand]
    private async Task SaveCountyAsync()
    {
        if (!CanManageRegion || SelectedCounty == null) return;

        if (string.IsNullOrWhiteSpace(EditName))
        {
            await _dialogService.DisplayAlertAsync("提示", "县区名称不能为空", "确定");
            return;
        }

        var request = new RegionCountySaveRequest
        {
            Id = SelectedCounty.Id,
            CountyName = EditName,
            CountyCode = string.IsNullOrWhiteSpace(EditCode) ? null : EditCode,
            CityName = string.IsNullOrWhiteSpace(EditCityName) ? null : EditCityName,
            SortOrder = SelectedCounty.SortOrder,
            IsActive = true
        };

        var result = await _regionService.UpdateCountyAsync(request);
        if (result.IsSuccess)
        {
            CancelEdit();
            if (SelectedCity != null)
                await LoadCountiesByCityAsync(SelectedCity.Id);
            _logger.Info($"执行操作");
        }
        else
        {
            await ShowFailureAsync(result, "保存");
        }
    }

    [RelayCommand]
    private void StartAddTown()
    {
        if (!CanManageRegion || SelectedCounty == null) return;

        IsAdding = true;
        IsEditing = true;
        EditName = string.Empty;
        EditCode = string.Empty;
        EditTownType = string.Empty;
    }

    [RelayCommand]
    private void StartEditTown()
    {
        if (!CanManageRegion || SelectedTown == null) return;

        IsAdding = false;
        IsEditing = true;
        EditName = SelectedTown.TownName;
        EditCode = SelectedTown.TownCode ?? string.Empty;
        EditTownType = SelectedTown.TownType ?? string.Empty;
    }

    [RelayCommand]
    private async Task DeleteTownAsync()
    {
        if (!CanManageRegion || SelectedTown == null) return;

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认删除",
            $"确定要删除乡镇\"{SelectedTown.TownName}\" 吗？",
            "删除", "确定");

        if (!confirm) return;

        var result = await _regionService.DeleteTownAsync(SelectedTown.Id);
        if (result.IsSuccess)
        {
            await LoadTownsAsync(SelectedCounty!.Id);
            _logger.Info($"执行操作");
        }
        else
        {
            await ShowFailureAsync(result, "删除");
        }
    }

    [RelayCommand]
    private async Task SaveTownAsync()
    {
        if (!CanManageRegion || SelectedCounty == null) return;

        if (string.IsNullOrWhiteSpace(EditName))
        {
            await _dialogService.DisplayAlertAsync("提示", "乡镇名称不能为空", "确定");
            return;
        }

        var request = new RegionTownSaveRequest
        {
            Id = IsAdding ? 0 : SelectedTown!.Id,
            CountyId = SelectedCounty.Id,
            TownName = EditName,
            TownCode = string.IsNullOrWhiteSpace(EditCode) ? null : EditCode,
            TownType = string.IsNullOrWhiteSpace(EditTownType) ? null : EditTownType,
            SortOrder = 0,
            IsActive = true
        };

        Result result;
        if (IsAdding)
        {
            result = await _regionService.CreateTownAsync(request);
        }
        else
        {
            result = await _regionService.UpdateTownAsync(request);
        }

        if (result.IsSuccess)
        {
            CancelEdit();
            await LoadTownsAsync(SelectedCounty.Id);
            _logger.Info($"执行操作");
        }
        else
        {
            await ShowFailureAsync(result, "保存");
        }
    }

    [RelayCommand]
    private void StartAddVillage()
    {
        if (!CanManageRegion || SelectedTown == null) return;

        IsAdding = true;
        IsEditing = true;
        EditName = string.Empty;
        EditCode = string.Empty;
        EditVillageType = string.Empty;
    }

    [RelayCommand]
    private void StartEditVillage()
    {
        if (!CanManageRegion || SelectedVillage == null) return;

        IsAdding = false;
        IsEditing = true;
        EditName = SelectedVillage.VillageName;
        EditCode = SelectedVillage.VillageCode ?? string.Empty;
        EditVillageType = SelectedVillage.VillageType ?? string.Empty;
    }

    [RelayCommand]
    private async Task DeleteVillageAsync()
    {
        if (!CanManageRegion || SelectedVillage == null) return;

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认删除",
            $"确定要删除\"{SelectedVillage.VillageName}\" 吗？",
            "删除", "确定");

        if (!confirm) return;

        var result = await _regionService.DeleteVillageAsync(SelectedVillage.Id);
        if (result.IsSuccess)
        {
            await LoadVillagesAsync(SelectedTown!.Id);
            _logger.Info($"执行操作");
        }
        else
        {
            await ShowFailureAsync(result, "删除");
        }
    }

    [RelayCommand]
    private async Task SaveVillageAsync()
    {
        if (!CanManageRegion || SelectedTown == null) return;

        if (string.IsNullOrWhiteSpace(EditName))
        {
            await _dialogService.DisplayAlertAsync("提示", "名称不能为空", "确定");
            return;
        }

        var request = new RegionVillageSaveRequest
        {
            Id = IsAdding ? 0 : SelectedVillage!.Id,
            TownId = SelectedTown.Id,
            VillageName = EditName,
            VillageCode = string.IsNullOrWhiteSpace(EditCode) ? null : EditCode,
            VillageType = string.IsNullOrWhiteSpace(EditVillageType) ? null : EditVillageType,
            SortOrder = 0,
            IsActive = true
        };

        Result result;
        if (IsAdding)
        {
            result = await _regionService.CreateVillageAsync(request);
        }
        else
        {
            result = await _regionService.UpdateVillageAsync(request);
        }

        if (result.IsSuccess)
        {
            CancelEdit();
            await LoadVillagesAsync(SelectedTown.Id);
            _logger.Info($"执行操作");
        }
        else
        {
            await ShowFailureAsync(result, "保存");
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        IsAdding = false;
        EditName = string.Empty;
        EditCode = string.Empty;
        EditCityName = string.Empty;
        EditTownType = string.Empty;
        EditVillageType = string.Empty;
    }

    #region Schema同步与数据初始化

    [RelayCommand]
    private async Task SyncSchemaAsync()
    {
        if (!CanManageRegion)
        {
            await _dialogService.DisplayAlertAsync("提示", "您没有管理行政区划的权限", "确定");
            return;
        }

        var stepNames = new List<string>
        {
            "同步表结构：地级市表",
            "同步表结构：县区表",
            "同步表结构：乡镇表",
            "同步表结构：村/社区表"
        };

        Progress.InitializeSteps(stepNames);

        var progress = new Progress<ProgressContext>(Progress.UpdateProgress);

        try
        {
            var result = await _regionService.SyncSchemaAsync(progress, CancellationToken);

            if (result.IsSuccess)
            {
                Progress.MarkAllCompleted();
                await LoadCitiesAsync();
                _logger.Info("Schema同步完成");
            }
            else
            {
                var failedStep = Progress.ProgressSteps.FirstOrDefault(s => s.Status == ProgressStepStatus.InProgress);
                if (failedStep != null)
                {
                    Progress.MarkStepFailed(failedStep.StepNumber - 1);
                }
                await ShowFailureAsync(result, "同步");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败");
            await _dialogService.DisplayAlertAsync("错误", $"Schema同步失败: {ex.Message}", "确定");
            Progress.CanCloseProgress = true;
        }
    }

    [RelayCommand]
    private async Task InitializeFromSeedAsync()
    {
        if (!CanManageRegion)
        {
            await _dialogService.DisplayAlertAsync("提示", "您没有管理行政区划的权限", "确定");
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认初始化",
            "确定要从种子数据初始化行政区划数据吗？\n此操作将清空现有数据并导入黑龙江省全部县区126个、乡镇1703个、村/社区(10000+)",
            "确认初始化",
            "取消");

        if (!confirm) return;

        var stepNames = new List<string>
        {
            "确保表结构存在",
            "清空地级市表",
            "导入地级市数据",
            "清空县区表",
            "导入县区数据",
            "清空乡镇表",
            "导入乡镇数据",
            "清空村/社区表",
            "导入村/社区数据",
            "提交事务"
        };

        Progress.InitializeSteps(stepNames);

        var progress = new Progress<ProgressContext>(Progress.UpdateProgress);

        try
        {
            var result = await _regionService.InitializeFromSeedAsync(progress, CancellationToken);

            if (result.IsSuccess)
            {
                Progress.MarkAllCompleted();
                await LoadCitiesAsync();
                _logger.Info("行政区划数据初始化完成");
            }
            else
            {
                var failedStep = Progress.ProgressSteps.FirstOrDefault(s => s.Status == ProgressStepStatus.InProgress);
                if (failedStep != null)
                {
                    Progress.MarkStepFailed(failedStep.StepNumber - 1);
                }
                await ShowFailureAsync(result, "初始化");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败");
            await _dialogService.DisplayAlertAsync("错误", $"初始化失败: {ex.Message}", "确定");
            Progress.CanCloseProgress = true;
        }
    }

    #endregion
}