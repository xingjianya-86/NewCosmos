using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Components;
using NewCosmos.Constants;
using RegionCity = NewCosmos.Models.Entities.RegionCity;
using RegionCounty = NewCosmos.Models.Entities.RegionCounty;
using RegionTown = NewCosmos.Models.Entities.RegionTown;
using RegionVillage = NewCosmos.Models.Entities.RegionVillage;
using NewCosmos.Models.Results;
using NewCosmos.Pages.UserManagement;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.UserManagement;

public partial class OrganizationTreeViewModel : ViewModelBase
{
    private readonly IOrganizationService _organizationService = null!;
    private readonly IDictionaryService _dictionaryService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly INewPermissionService _permissionService = null!;
    private readonly IDataScopeService _dataScopeService = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly IServiceProvider _serviceProvider = null!;
    private int _currentUserId;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    [ObservableProperty]
    private ObservableCollection<OrganizationTreeNode> _organizationTree = new();

    [ObservableProperty]
    private ObservableCollection<OrganizationTreeNode> _filteredOrganizationTree = new();

    [ObservableProperty]
    private OrganizationTreeNode _selectedOrganization = null!;

    [ObservableProperty]
    private Organization _selectedOrganizationDetails = null!;

    [ObservableProperty]
    private bool _isOrganizationEditorVisible;

    [ObservableProperty]
    private OrganizationEditorViewModel _organizationEditorViewModel = null!;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _totalOrganizations;

    [ObservableProperty]
    private int _totalUsers;

    [ObservableProperty]
    private int _selectedOrganizationChildrenCount;

    [ObservableProperty]
    private int _specialCareInstitutionCount;

    [ObservableProperty]
    private bool _canCreateOrganization;

    [ObservableProperty]
    private bool _canEditOrganization;

    [ObservableProperty]
    private bool _canDeleteOrganization;

    public double CreateOrganizationOpacity => CanCreateOrganization ? 1.0 : 0.5;
    public double EditOrganizationOpacity => CanEditOrganization ? 1.0 : 0.5;
    public double DeleteOrganizationOpacity => CanDeleteOrganization ? 1.0 : 0.5;

    public bool? CanEditSelected => SelectedOrganization != null && CanEditOrganization;
    public bool? CanDeleteSelected => SelectedOrganization != null && CanDeleteOrganization;

    partial void OnSelectedOrganizationChanged(OrganizationTreeNode value)
    {
        OnPropertyChanged(nameof(CanEditSelected));
        OnPropertyChanged(nameof(CanDeleteSelected));
    }

    partial void OnCanEditOrganizationChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditSelected));
        OnPropertyChanged(nameof(EditOrganizationOpacity));
    }

    partial void OnCanDeleteOrganizationChanged(bool value)
    {
        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(DeleteOrganizationOpacity));
    }

    partial void OnCanCreateOrganizationChanged(bool value)
    {
        OnPropertyChanged(nameof(CreateOrganizationOpacity));
    }

    private List<Organization> _allOrganizations = new();

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

        // 非阻塞隐藏：不延长调用命令的执行时间；版本号防止旧的隐藏任务关闭新消息
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
            // fire-and-forget：忽略异常，避免未观察的任务异常
        }
    }

    #endregion

    public OrganizationTreeViewModel(
        IOrganizationService organizationService,
        IDictionaryService dictionaryService,
        ILoggerService logger,
        INewPermissionService permissionService,
        IDataScopeService dataScopeService,
        IDialogService dialogService,
        IServiceProvider serviceProvider)
    {
        _organizationService = organizationService;
        _dictionaryService = dictionaryService;
        _logger = logger;
        _permissionService = permissionService;
        _dataScopeService = dataScopeService;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;
    }

    public async Task LoadPermissionsAsync(int userId)
    {
        _currentUserId = userId;
        try
        {
            CanCreateOrganization = await _permissionService.HasPermissionAsync(userId, PermissionCodes.ORG_CREATE);
            CanEditOrganization = await _permissionService.HasPermissionAsync(userId, PermissionCodes.ORG_EDIT);
            CanDeleteOrganization = await _permissionService.HasPermissionAsync(userId, PermissionCodes.ORG_DELETE);

            OnPropertyChanged(nameof(CreateOrganizationOpacity));
            OnPropertyChanged(nameof(EditOrganizationOpacity));
            OnPropertyChanged(nameof(DeleteOrganizationOpacity));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载权限失败");
        }
    }

    [RelayCommand]
    public async Task LoadTreeAsync()
    {
        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("加载组织机构树");

            var result = await _organizationService.GetTreeAsync(CancellationToken);

            if (result.IsSuccess)
            {
                OrganizationTree.Clear();
                foreach (var node in result.Value!)
                {
                    OrganizationTree.Add(node);
                }

                FlattenAndFilterTree();
                await LoadStatisticsAsync();

                _logger.LogBusiness("加载组织机构树完成");
            }
            else
            {
                _logger.Error($"加载组织树失败: {result.Message}");
            }
        }, "加载组织树");
    }

    private async Task LoadStatisticsAsync()
    {
        try
        {
            TotalOrganizations = FilteredOrganizationTree.Count;

            var allOrgsResult = await _organizationService.GetAllAsync(CancellationToken);
            if (allOrgsResult.IsSuccess)
            {
                _allOrganizations = allOrgsResult.Value!;
                SpecialCareInstitutionCount = _allOrganizations.Count(o => o.IsSpecialCareInstitution);
            }

            if (SelectedOrganization != null)
            {
                var childrenResult = await _organizationService.GetChildrenAsync(SelectedOrganization.Id, CancellationToken);
                if (childrenResult.IsSuccess)
                {
                    SelectedOrganizationChildrenCount = childrenResult.Value!.Count;
                }

                var userCountResult = await _organizationService.GetUserCountAsync(SelectedOrganization.Id, CancellationToken);
                if (userCountResult.IsSuccess)
                {
                    TotalUsers = userCountResult.Value;
                }
            }
            else
            {
                SelectedOrganizationChildrenCount = 0;
                TotalUsers = 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载统计信息失败");
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        FlattenAndFilterTree();
    }

    private void FlattenAndFilterTree()
    {
        FilteredOrganizationTree.Clear();

        var keyword = SearchText.Trim();
        var hasFilter = !string.IsNullOrWhiteSpace(keyword);

        foreach (var root in OrganizationTree)
        {
            FlattenNode(root, keyword, hasFilter);
        }
    }

    private void FlattenNode(OrganizationTreeNode node, string keyword, bool hasFilter)
    {
        var matchesFilter = !hasFilter ||
            node.Name.Contains(keyword!, StringComparison.OrdinalIgnoreCase);

        if (matchesFilter)
        {
            FilteredOrganizationTree.Add(node);
        }

        foreach (var child in node.Children)
        {
            if (!hasFilter || SubtreeContains(child, keyword!))
            {
                FlattenNode(child, keyword, hasFilter);
            }
        }
    }

    private static bool SubtreeContains(OrganizationTreeNode node, string keyword)
    {
        if (node.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var child in node.Children)
        {
            if (SubtreeContains(child, keyword))
                return true;
        }

        return false;
    }

    [RelayCommand]
    private async Task SelectOrganizationAsync(OrganizationTreeNode node)
    {
        SelectedOrganization = node;

        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness($"加载组织机构详情, OrganizationId={node.Id}");

            var result = await _organizationService.GetByIdAsync(node.Id, ct);

            if (result.IsSuccess)
            {
                SelectedOrganizationDetails = result.Value;
                await LoadStatisticsAsync();
            }
            else
            {
                _logger.Error($"获取组织详情失败: {result.Message}");
            }
        }, "加载组织详情");
    }

    [RelayCommand]
    private async Task CreateOrganizationAsync()
    {
        if (!CanCreateOrganization) return;

        // 组织树边界：新建组织挂在所选父组织下，父组织必须在可管理范围内；
        // 未选中节点时无法确定父级（ parentId 传 null 会挂到根，历史上此处直接解引用会 NRE），予以拦截
        var parentOrg = SelectedOrganization;
        var parentId = parentOrg?.Id;
        if (!parentId.HasValue)
        {
            await _dialogService.DisplayAlertAsync("请先选择组织", "请先在左侧组织树中选择上级组织，再新建下级机构。", "确定");
            return;
        }

        if (!await _dataScopeService.CanManageOrganizationAsync(_currentUserId, parentId.Value))
        {
            await _dialogService.DisplayAlertAsync("无权操作", "只能在本人可管理的组织范围内新建下级组织。", "确定");
            return;
        }

        _logger.LogBusiness("打开新建组织编辑器");

        var editor = _serviceProvider.GetRequiredService<OrganizationEditorViewModel>();
        editor.Initialize(null, parentId, async () =>
        {
            IsOrganizationEditorVisible = false;
            await LoadTreeAsync();
            await ShowSnackBarAsync("组织创建成功");
        });
        OrganizationEditorViewModel = editor;

        await editor.LoadInitialRegionAsync();
        IsOrganizationEditorVisible = true;
    }

    [RelayCommand]
    private async Task EditOrganizationAsync()
    {
        if (SelectedOrganization == null || !CanEditOrganization) return;

        // 组织树边界：仅可编辑可管理范围内的组织
        if (!await _dataScopeService.CanManageOrganizationAsync(_currentUserId, SelectedOrganization.Id))
        {
            await _dialogService.DisplayAlertAsync("无权操作", "只能编辑本人可管理组织范围内的机构。", "确定");
            return;
        }

        _logger.LogBusiness($"编辑组织机构, OrganizationId={SelectedOrganization.Id}");

        var editor = _serviceProvider.GetRequiredService<OrganizationEditorViewModel>();
        editor.Initialize(SelectedOrganizationDetails, null, async () =>
        {
            IsOrganizationEditorVisible = false;
            await LoadTreeAsync();
            await ShowSnackBarAsync("组织保存成功");
        });
        OrganizationEditorViewModel = editor;

        await editor.LoadInitialRegionAsync();
        IsOrganizationEditorVisible = true;
    }

    [RelayCommand]
    private async Task DeleteOrganizationAsync()
    {
        if (SelectedOrganization == null || !CanDeleteOrganization) return;

        // 组织树边界：仅可删除可管理范围内的组织
        if (!await _dataScopeService.CanManageOrganizationAsync(_currentUserId, SelectedOrganization.Id))
        {
            await _dialogService.DisplayAlertAsync("无权操作", "只能删除本人可管理组织范围内的机构。", "确定");
            return;
        }

        _logger.LogBusiness($"删除组织机构, OrganizationId={SelectedOrganization.Id}");

        var result = await _organizationService.DeleteAsync(SelectedOrganization.Id, CancellationToken);

        if (result.IsSuccess)
        {
            await LoadTreeAsync();
            SelectedOrganization = null;
            SelectedOrganizationDetails = null;
            _logger.LogBusiness("组织机构删除成功");
            await ShowSnackBarAsync("组织已删除", SnackBarType.Warning);
        }
        else
        {
            _logger.Error($"删除失败: {result.Message}");
            await _dialogService.DisplayAlertAsync("删除失败", result.Message ?? "未知错误", "确定");
        }
    }

    #region 页面导航

    [RelayCommand]
    private async Task NavigateToImportPageAsync()
    {
        try
        {
            await NavigateToPageAsync<OrganizationImportPage>();
        }
        catch (Exception ex)
        {
            _logger.Error($"打开导入页面失败: {ex.Message}");
            await _dialogService.DisplayAlertAsync("错误", $"打开导入页面失败: {ex.Message}", "确定");
        }
    }

    #endregion
}

public partial class OrganizationEditorViewModel : ViewModelBase
{
    private readonly IOrganizationService _organizationService;
    private readonly IDictionaryService _dictionaryService;
    private readonly IRegionService _regionService;
    private readonly ILoggerService _logger;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private Func<Task>? _onSaved;
    private Organization? _originalOrganization;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _abbreviation = string.Empty;

    [ObservableProperty]
    private string _principal = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string _postalCode = string.Empty;

    [ObservableProperty]
    private string _institutionNature = string.Empty;

    [ObservableProperty]
    private string _institutionCategory = string.Empty;

    [ObservableProperty]
    private string _institutionTypeValue = string.Empty;

    [ObservableProperty]
    private bool _isSpecialCareInstitution;

    [ObservableProperty]
    private int _parentId;

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private bool _isLoadingOptions;

    [ObservableProperty]
    private Organization _selectedParentOrganization = null!;

    public string EditorTitle => _originalOrganization == null ? "新建组织" : "编辑组织";

    public ObservableCollection<Organization> ParentOrganizationOptions { get; } = new();

    public ObservableCollection<string> InstitutionCategoryOptions { get; } = new();

    public ObservableCollection<string> InstitutionTypeOptions { get; } = new();

    public ObservableCollection<string> CityOptions { get; } = new();
    public ObservableCollection<string> CountyOptions { get; } = new();
    public ObservableCollection<string> TownOptions { get; } = new();
    public ObservableCollection<string> VillageOptions { get; } = new();

    [ObservableProperty]
    private string _selectedCityName = string.Empty;

    [ObservableProperty]
    private string _selectedCountyName = string.Empty;

    [ObservableProperty]
    private string _selectedTownName = string.Empty;

    [ObservableProperty]
    private string _selectedVillageName = string.Empty;

    private bool _isRestoringRegion;
    private List<DictItemView> _allInstitutionTypes = new();
    private Dictionary<string, string> _institutionCategoryKeyMap = new();

    private Dictionary<string, RegionCity> _cityMap = new();
    private Dictionary<string, RegionCounty> _countyMap = new();
    private Dictionary<string, RegionTown> _townMap = new();
    private Dictionary<string, RegionVillage> _villageMap = new();

    public OrganizationEditorViewModel(
        IOrganizationService organizationService,
        IDictionaryService dictionaryService,
        IRegionService regionService,
        ILoggerService logger,
        IDialogService dialogService,
        IServiceProvider serviceProvider)
    {
        _organizationService = organizationService;
        _dictionaryService = dictionaryService;
        _regionService = regionService;
        _logger = logger;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;
    }

    /// <summary>从 DI 解析后、绑定前调用：传入本次编辑目标、上级组织与保存回调（new 的运行期参数改由此承载）</summary>
    public void Initialize(Organization? organization, int? parentId, Func<Task> onSaved)
    {
        _originalOrganization = organization;
        _onSaved = onSaved;

        IsEditMode = organization != null;

        SafeFireAndForget(() => LoadParentOptionsAsync(parentId));
        SafeFireAndForget(() => LoadDictionaryOptionsAsync());

        if (organization != null)
        {
            Name = organization.Name;
            Abbreviation = organization.Abbreviation ?? string.Empty;
            Principal = organization.Principal ?? string.Empty;
            Phone = organization.Phone ?? string.Empty;
            Address = organization.Address ?? string.Empty;
            PostalCode = organization.PostalCode ?? string.Empty;
            InstitutionNature = organization.InstitutionNature ?? string.Empty;
            IsSpecialCareInstitution = organization.IsSpecialCareInstitution;
            ParentId = organization.ParentId ?? 0;
        }

        OnPropertyChanged(nameof(EditorTitle));
    }

    private async Task LoadDictionaryOptionsAsync()
    {
        try
        {
            IsLoadingOptions = true;

            var categoryResult = await _dictionaryService.GetItemsByCategoryAsync(
                DictionaryTypeCodes.InstitutionCategory, CancellationToken);
            if (categoryResult.IsSuccess)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    InstitutionCategoryOptions.Clear();
                    _institutionCategoryKeyMap.Clear();
                    foreach (var item in categoryResult.Value!)
                    {
                        InstitutionCategoryOptions.Add((item.ItemValue ?? item.ItemKey)!);
                        if (item.ItemValue != null && item.ItemKey != null)
                        {
                            _institutionCategoryKeyMap[item.ItemValue] = item.ItemKey;
                        }
                    }
                });
            }

            var typeResult = await _dictionaryService.GetItemsByCategoryAsync(
                DictionaryTypeCodes.InstitutionType, CancellationToken);
            if (typeResult.IsSuccess)
            {
                _allInstitutionTypes = typeResult.Value!;
            }

            if (_originalOrganization != null && !string.IsNullOrEmpty(_originalOrganization.InstitutionType))
            {
                var existingType = _allInstitutionTypes
                    .FirstOrDefault(t => t.ItemValue == _originalOrganization.InstitutionType
                                      || t.ItemKey == _originalOrganization.InstitutionType);
                if (existingType != null)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        var categoryDisplay = _institutionCategoryKeyMap
                            .FirstOrDefault(kv => kv.Value == existingType.Description)
                            .Key;
                        InstitutionCategory = categoryDisplay ?? existingType.Description ?? string.Empty;
                        InstitutionTypeValue = existingType.ItemValue ?? existingType.ItemKey;
                    });
                }
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载字典选项失败");
        }
        finally
        {
            IsLoadingOptions = false;
        }
    }

    partial void OnInstitutionCategoryChanged(string value)
    {
        FilterTypesByCategory();
    }

    private void FilterTypesByCategory()
    {
        InstitutionTypeOptions.Clear();

        if (string.IsNullOrEmpty(InstitutionCategory)) return;

        var categoryKey = _institutionCategoryKeyMap.TryGetValue(InstitutionCategory, out var key)
            ? key
            : InstitutionCategory;

        var matched = _allInstitutionTypes
            .Where(t => t.Description == categoryKey)
            .ToList();

        foreach (var item in matched)
        {
            InstitutionTypeOptions.Add(item.ItemValue ?? item.ItemKey);
        }
    }

    #region 地区级联

    partial void OnSelectedCityNameChanged(string value)
    {
        if (_isRestoringRegion) return;

        SelectedCountyName = string.Empty;
        SelectedTownName = string.Empty;
        SelectedVillageName = string.Empty;
        CountyOptions.Clear();
        TownOptions.Clear();
        VillageOptions.Clear();

        if (!string.IsNullOrEmpty(value) && value != "（请选择）" && _cityMap.TryGetValue(value, out var city))
        {
            SafeFireAndForget(() => LoadCountyOptionsByCityAsync(city.Id));
        }
    }

    partial void OnSelectedCountyNameChanged(string value)
    {
        if (_isRestoringRegion) return;

        SelectedTownName = string.Empty;
        SelectedVillageName = string.Empty;
        TownOptions.Clear();
        VillageOptions.Clear();

        if (!string.IsNullOrEmpty(value) && value != "（请选择）" && _countyMap.TryGetValue(value, out var county))
        {
            SafeFireAndForget(() => LoadTownOptionsAsync(county.Id));
        }
    }

    partial void OnSelectedTownNameChanged(string value)
    {
        if (_isRestoringRegion) return;

        SelectedVillageName = string.Empty;
        VillageOptions.Clear();

        if (!string.IsNullOrEmpty(value) && value != "（请选择）" && _townMap.TryGetValue(value, out var town))
        {
            SafeFireAndForget(() => LoadVillageOptionsAsync(town.Id));
        }
    }

    private async Task LoadCityOptionsAsync()
    {
        try
        {
            IsLoadingOptions = true;
            var result = await _regionService.GetCitiesAsync(CancellationToken);

            CityOptions.Clear();
            _cityMap.Clear();
            CityOptions.Add("（请选择）");
            if (result.IsSuccess && result.Value != null)
            {
                foreach (var city in result.Value)
                {
                    _cityMap[city.CityName] = city;
                    CityOptions.Add(city.CityName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载城市列表失败");
        }
        finally
        {
            IsLoadingOptions = false;
        }
    }

    private async Task LoadCountyOptionsByCityAsync(int cityId)
    {
        try
        {
            IsLoadingOptions = true;
            var result = await _regionService.GetCountiesByCityIdAsync(cityId, CancellationToken);

            CountyOptions.Clear();
            _countyMap.Clear();
            CountyOptions.Add("（请选择）");
            if (result.IsSuccess && result.Value != null)
            {
                foreach (var county in result.Value)
                {
                    _countyMap[county.CountyName] = county;
                    CountyOptions.Add(county.CountyName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载区县列表失败");
        }
        finally
        {
            IsLoadingOptions = false;
        }
    }

    private async Task LoadTownOptionsAsync(int countyId)
    {
        try
        {
            var result = await _regionService.GetTownsByCountyIdAsync(countyId, CancellationToken);

            TownOptions.Clear();
            _townMap.Clear();
            TownOptions.Add("（请选择）");
            if (result.IsSuccess && result.Value != null)
            {
                foreach (var town in result.Value)
                {
                    _townMap[town.TownName] = town;
                    TownOptions.Add(town.TownName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载乡镇列表失败");
        }
    }

    private async Task LoadVillageOptionsAsync(int townId)
    {
        try
        {
            var result = await _regionService.GetVillagesByTownIdAsync(townId, CancellationToken);

            VillageOptions.Clear();
            _villageMap.Clear();
            VillageOptions.Add("（请选择）");
            if (result.IsSuccess && result.Value != null)
            {
                foreach (var village in result.Value)
                {
                    _villageMap[village.VillageName] = village;
                    VillageOptions.Add(village.VillageName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载村社区列表失败");
        }
    }

    private async Task RestoreRegionSelectionAsync()
    {
        if (_originalOrganization == null) return;

        var cityId = _originalOrganization.CityId;
        var countyId = _originalOrganization.CountyId;
        var townId = _originalOrganization.TownId;
        var villageId = _originalOrganization.VillageId;

        _isRestoringRegion = true;

        if (cityId.HasValue)
        {
            var cityEntry = _cityMap.FirstOrDefault(kv => kv.Value.Id == cityId.Value);
            if (!string.IsNullOrEmpty(cityEntry.Key))
            {
                SelectedCityName = cityEntry.Key;
                await LoadCountyOptionsByCityAsync(cityId.Value);
            }
        }

        if (countyId.HasValue)
        {
            var countyEntry = _countyMap.FirstOrDefault(kv => kv.Value.Id == countyId.Value);
            if (!string.IsNullOrEmpty(countyEntry.Key))
            {
                SelectedCountyName = countyEntry.Key;
                if (townId.HasValue)
                {
                    await LoadTownOptionsAsync(countyId.Value);
                }
            }
        }

        if (townId.HasValue)
        {
            var townEntry = _townMap.FirstOrDefault(kv => kv.Value.Id == townId.Value);
            if (!string.IsNullOrEmpty(townEntry.Key))
            {
                SelectedTownName = townEntry.Key;
                if (villageId.HasValue)
                {
                    await LoadVillageOptionsAsync(townId.Value);
                }
            }
        }

        if (villageId.HasValue)
        {
            var villageEntry = _villageMap.FirstOrDefault(kv => kv.Value.Id == villageId.Value);
            if (!string.IsNullOrEmpty(villageEntry.Key))
            {
                SelectedVillageName = villageEntry.Key;
            }
        }

        _isRestoringRegion = false;
    }

    public async Task LoadInitialRegionAsync()
    {
        await LoadCityOptionsAsync();
        await RestoreRegionSelectionAsync();
    }

    #endregion

    private async Task LoadParentOptionsAsync(int? initialParentId)
    {
        try
        {
            IsLoadingOptions = true;
            var result = await _organizationService.GetAllAsync(CancellationToken);

            if (result.IsSuccess)
            {
                ParentOrganizationOptions.Clear();

                var emptyItem = new Organization { Id = 0, Name = "（无上级组织）" };
                ParentOrganizationOptions.Add(emptyItem);

                foreach (var org in result.Value!)
                {
                    if (_originalOrganization != null && org.Id == _originalOrganization.Id)
                        continue;
                    ParentOrganizationOptions.Add(org);
                }

                var targetId = initialParentId ?? _originalOrganization?.ParentId ?? 0;
                var selected = ParentOrganizationOptions.FirstOrDefault(o => o.Id == targetId);
                if (selected != null)
                {
                    SelectedParentOrganization = selected;
                }
                else
                {
                    SelectedParentOrganization = emptyItem;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载上级组织列表失败");
        }
        finally
        {
            IsLoadingOptions = false;
        }
    }

    partial void OnSelectedParentOrganizationChanged(Organization value)
    {
        if (value != null)
        {
            ParentId = value.Id;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            await _dialogService.DisplayAlertAsync("验证失败", "组织名称不能为空", "确定");
            return;
        }

        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("保存组织机构");

            var request = new OrganizationSaveRequest
            {
                Id = _originalOrganization?.Id,
                Name = Name,
                Abbreviation = Abbreviation,
                Principal = Principal,
                Phone = Phone,
                Address = Address,
                PostalCode = PostalCode,
                InstitutionNature = InstitutionNature,
                InstitutionType = InstitutionTypeValue,
                InstitutionCategory = InstitutionCategory,
                IsSpecialCareInstitution = IsSpecialCareInstitution,
                ParentId = ParentId > 0 ? ParentId : null,
                CityId = GetCityId(),
                CountyId = GetCountyId(),
                TownId = GetTownId(),
                VillageId = GetVillageId(),
                SavedBy = "System"
            };

            Result result;
            if (_originalOrganization == null)
            {
                var createResult = await _organizationService.CreateAsync(request, ct);
                result = createResult.IsSuccess
                    ? Result.Success()
                    : Result.Failure(createResult.ErrorCode!, createResult.Message!);
            }
            else
            {
                result = await _organizationService.UpdateAsync(_originalOrganization.Id, request, ct);
            }

            if (result.IsSuccess)
            {
                if (_onSaved != null) await _onSaved();
                _logger.LogBusiness("组织机构保存成功");
            }
            else
            {
                _logger.Error($"保存组织机构失败: {result.Message}");
                await _dialogService.DisplayAlertAsync("保存失败", result.Message ?? "未知错误", "确定");
            }
        }, "确定");
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (_onSaved != null) await _onSaved();
    }

    #region 辅助方法

    private int? GetCityId()
    {
        if (string.IsNullOrEmpty(SelectedCityName) || SelectedCityName == "（请选择）")
            return null;
        return _cityMap.TryGetValue(SelectedCityName, out var city) ? city.Id : null;
    }

    private int? GetCountyId()
    {
        if (string.IsNullOrEmpty(SelectedCountyName) || SelectedCountyName == "（请选择）")
            return null;
        return _countyMap.TryGetValue(SelectedCountyName, out var county) ? county.Id : null;
    }

    private int? GetTownId()
    {
        if (string.IsNullOrEmpty(SelectedTownName) || SelectedTownName == "（请选择）")
            return null;
        return _townMap.TryGetValue(SelectedTownName, out var town) ? town.Id : null;
    }

    private int? GetVillageId()
    {
        if (string.IsNullOrEmpty(SelectedVillageName) || SelectedVillageName == "（请选择）")
            return null;
        return _villageMap.TryGetValue(SelectedVillageName, out var village) ? village.Id : null;
    }

    #endregion
}