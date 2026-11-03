using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Components;
using NewCosmos.Constants;
using RegionCity = NewCosmos.Models.Entities.RegionCity;
using RegionCounty = NewCosmos.Models.Entities.RegionCounty;
using RegionProvince = NewCosmos.Models.Entities.RegionProvince;
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

    /// <summary>
    /// 详情加载版本号：只有最新一次点击的结果才允许写回
    /// <see cref="SelectedOrganizationDetails"/>，防止慢请求覆盖新请求。
    /// </summary>
    private long _detailLoadVersion;
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

            // 一次取全量，树与统计共用同一份数据：
            // 原实现 GetTreeAsync 与 LoadStatisticsAsync 各查一遍带 4 个 JOIN 的全表，
            // 打开页面 = 2 次全量往返；改后 1 次。
            var allResult = await _organizationService.GetAllAsync(CancellationToken);

            if (allResult.IsFailure || allResult.Value == null)
            {
                _logger.Error($"加载组织树失败: {allResult.ErrorCode} {allResult.Message}");
                // 页面没绑定 ErrorMessage，必须走 SnackBar 才看得见
                await ShowSnackBarAsync(
                    UserFriendlyMessages.Get(allResult.ErrorCode ?? ErrorCodes.UNKNOWN_ERROR,
                                              allResult.Message ?? "组织数据加载失败"),
                    SnackBarType.Error);
                return;
            }

            _allOrganizations = allResult.Value;

            OrganizationTree.Clear();
            foreach (var node in _organizationService.BuildTree(_allOrganizations))
            {
                OrganizationTree.Add(node);
            }

            FlattenAndFilterTree();

            // 树节点是新对象，选中态对应的详情快照已过期：按当前选中项重取，
            // 否则保存后再次点"编辑"会拿到保存前的旧数据（含旧地址）。
            // 未选中时也要刷统计（两者只会走一条，避免重复 GetUserCount 往返）
            if (SelectedOrganization != null)
                await RefreshSelectedDetailAsync();
            else
                await LoadStatisticsAsync();

            _logger.LogBusiness("加载组织机构树完成", ("Count", OrganizationTree.Count));
        }, "加载组织树");
    }

    private async Task LoadStatisticsAsync()
    {
        try
        {
            // 口径：总数 = 全量机构数（原实现取 FilteredOrganizationTree.Count，
            // 搜索过滤后会被当成"总数"显示）
            TotalOrganizations = _allOrganizations.Count;
            SpecialCareInstitutionCount = _allOrganizations.Count(o => o.IsSpecialCareInstitution);

            if (SelectedOrganization != null)
            {
                // 子机构数从同一份全量数据算，省一次 GetChildrenAsync
                SelectedOrganizationChildrenCount =
                    _allOrganizations.Count(o => o.ParentId == SelectedOrganization.Id);

                var userCountResult = await _organizationService.GetUserCountAsync(SelectedOrganization.Id, CancellationToken);
                if (userCountResult.IsSuccess)
                {
                    TotalUsers = userCountResult.Value;
                }
                else
                {
                    // 失败不再静默归零/沿用旧值而无痕：留日志便于定位
                    _logger.Warn($"统计组织下用户数失败（保留上次值 {TotalUsers}）: "
                               + $"{userCountResult.ErrorCode} {userCountResult.Message}");
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

    /// <summary>
    /// 前序展开 + 过滤：命中（名称包含关键词）才进列表。
    /// 原实现每个节点还要先递归 <c>SubtreeContains</c> 判"子树是否命中"才决定要不要下探——
    /// 但该判定不改变结果（节点只在**自己**名称命中时才被加入，而命中的节点必然可达），
    /// 纯属让每次按键退化成 O(n²)，故删掉。
    /// </summary>
    private void FlattenNode(OrganizationTreeNode node, string keyword, bool hasFilter)
    {
        if (!hasFilter || node.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        {
            FilteredOrganizationTree.Add(node);
        }

        foreach (var child in node.Children)
        {
            FlattenNode(child, keyword, hasFilter);
        }
    }

    [RelayCommand]
    private async Task SelectOrganizationAsync(OrganizationTreeNode node)
    {
        SelectedOrganization = node;
        // 先清掉上一家的详情：详情面板与编辑器都不能拿着旧机构的数据
        SelectedOrganizationDetails = null!;
        await RefreshSelectedDetailAsync();
    }

    /// <summary>
    /// 按 <see cref="SelectedOrganization"/> 重新拉取详情。
    /// 不走 <c>ExecuteAsync</c>：它的 <c>IsBusy</c> 会在树/统计加载期间**静默 return**
    /// （既不加载也不报错），历史表现就是"点了节点但地址/详情不刷新"。
    /// 改用版本号裁决——只有最新一次请求允许写回。
    /// </summary>
    private async Task RefreshSelectedDetailAsync()
    {
        var node = SelectedOrganization;
        if (node == null)
        {
            SelectedOrganizationDetails = null!;
            return;
        }

        var version = Interlocked.Increment(ref _detailLoadVersion);
        _logger.LogBusiness($"加载组织机构详情, OrganizationId={node.Id}");

        try
        {
            var result = await _organizationService.GetByIdAsync(node.Id, CancellationToken);

            if (version != Volatile.Read(ref _detailLoadVersion))
            {
                _logger.Debug($"组织详情结果已过期，丢弃: OrganizationId={node.Id}");
                return;
            }

            // 无论详情成败，统计都按当前选中项刷新（统计只依赖 SelectedOrganization，不依赖详情）
            await LoadStatisticsAsync();

            if (result.IsSuccess && result.Value != null)
            {
                SelectedOrganizationDetails = result.Value;
            }
            else
            {
                SelectedOrganizationDetails = null!;
                _logger.Error($"获取组织详情失败: {result.ErrorCode} {result.Message}");
                await ShowSnackBarAsync(
                    UserFriendlyMessages.Get(ErrorCodes.ORGANIZATION_LOAD_FAILED, result.Message ?? string.Empty),
                    SnackBarType.Error);
            }
        }
        catch (OperationCanceledException)
        {
            // 页面离开/令牌取消属正常路径，静默
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"加载组织详情异常: OrganizationId={node.Id}");
            await ShowSnackBarAsync(
                UserFriendlyMessages.Get(ErrorCodes.ORGANIZATION_LOAD_FAILED, ex.Message),
                SnackBarType.Error);
        }
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

        // 重新按 ID 取最新数据：SelectedOrganizationDetails 是另一条异步链加载的快照，
        // 树重载/快速切换节点后可能已过期；为 null 时 Initialize(null,…) 会把 IsEditMode 置 false，
        // "编辑"实际变成"新建"，保存即产生重复机构
        var detail = await _organizationService.GetByIdAsync(SelectedOrganization.Id, CancellationToken);
        if (detail.IsFailure || detail.Value == null)
        {
            _logger.Error($"打开编辑器失败，组织详情加载不成功: {detail.ErrorCode} {detail.Message}");
            await _dialogService.DisplayAlertAsync("无法编辑",
                UserFriendlyMessages.Get(ErrorCodes.ORGANIZATION_LOAD_FAILED, detail.Message ?? string.Empty),
                "确定");
            return;
        }

        var org = detail.Value;
        SelectedOrganizationDetails = org;

        var editor = _serviceProvider.GetRequiredService<OrganizationEditorViewModel>();
        editor.Initialize(org, null, async () =>
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
            // 先清选中态再重载树：否则 LoadTreeAsync 会为已删除的节点白拉一次详情
            SelectedOrganization = null;
            SelectedOrganizationDetails = null!;
            await LoadTreeAsync();
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

    public ObservableCollection<string> ProvinceOptions { get; } = new();
    public ObservableCollection<string> CityOptions { get; } = new();
    public ObservableCollection<string> CountyOptions { get; } = new();
    public ObservableCollection<string> TownOptions { get; } = new();
    public ObservableCollection<string> VillageOptions { get; } = new();

    /// <summary>选中省（2026-10-26 真级联：选省 → 筛市；回显期由 <c>_isRestoringRegion</c> 抑制级联）</summary>
    [ObservableProperty]
    private string _selectedProvinceName = string.Empty;

    [ObservableProperty]
    private string _selectedCityName = string.Empty;

    [ObservableProperty]
    private string _selectedCountyName = string.Empty;

    [ObservableProperty]
    private string _selectedTownName = string.Empty;

    [ObservableProperty]
    private string _selectedVillageName = string.Empty;

    /// <summary>
    /// 区划数据加载失败（DB/快照异常）。为 true 时对应 Picker 不可信，
    /// 保存会被拦截——否则四个 ID 会全部写成 NULL，把库中已有区划清空。
    /// </summary>
    [ObservableProperty]
    private bool _regionLoadFailed;

    [ObservableProperty]
    private string _regionLoadMessage = string.Empty;

    private bool _isRestoringRegion;
    private List<DictItemView> _allInstitutionTypes = new();
    private Dictionary<string, string> _institutionCategoryKeyMap = new();

    private Dictionary<string, RegionCity> _cityMap = new();
    private Dictionary<string, RegionCounty> _countyMap = new();
    private Dictionary<string, RegionTown> _townMap = new();
    private Dictionary<string, RegionVillage> _villageMap = new();
    private Dictionary<string, RegionProvince> _provinceMap = new();

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

    /// <summary>
    /// 选省 → 重载市（按省筛）并清掉市及以下选择。
    /// 回显期由 <see cref="_isRestoringRegion"/> 抑制：回显按全量市定位，只有用户主动切省才筛。
    /// </summary>
    partial void OnSelectedProvinceNameChanged(string value)
    {
        // 回显期 _isRestoringRegion=true：回显按全量市定位，只有用户主动切省才筛
        if (_isRestoringRegion) return;
        if (value is null) return;   // Picker 控件复位不级联（同 OnSelectedCityNameChanged）

        SelectedCityName = string.Empty;
        SelectedCountyName = string.Empty;
        SelectedTownName = string.Empty;
        SelectedVillageName = string.Empty;
        CityOptions.Clear();
        CountyOptions.Clear();
        TownOptions.Clear();
        VillageOptions.Clear();

        if (!string.IsNullOrEmpty(value) && value != "（请选择）" && _provinceMap.TryGetValue(value, out var province))
            SafeFireAndForget(() => LoadCityOptionsAsync(province.Id));
        else
            SafeFireAndForget(() => LoadCityOptionsAsync(null));
    }

    /// <summary>加载省级行政区（省表未建/未跑迁移时只记日志，不置 RegionLoadFailed——不得因省缺失阻断市/县/镇/村既有流程）</summary>
    private async Task LoadProvinceOptionsAsync()
    {
        try
        {
            var result = await _regionService.GetProvincesAsync(CancellationToken);

            ProvinceOptions.Clear();
            _provinceMap.Clear();
            ProvinceOptions.Add("（请选择）");

            if (result.IsSuccess && result.Value != null)
            {
                foreach (var province in result.Value)
                {
                    _provinceMap[province.ProvinceName] = province;
                    ProvinceOptions.Add(province.ProvinceName);
                }
            }
            else
            {
                _logger.Warn($"加载省级列表失败（不影响市县镇村）: {result.ErrorCode} {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载省级列表异常（不影响市县镇村）");
        }
    }

    partial void OnSelectedCityNameChanged(string value)
    {
        if (_isRestoringRegion) return;
        // Picker 在 ItemsSource 被 Clear/重建时会把 SelectedItem 推回 null（控件复位，不是用户操作），
        // 此时级联清空会让刚回显好的县/镇/村整体丢失。用户主动清空走的是 "（请选择）" 占位项。
        if (value is null) return;

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
        if (value is null) return;   // 同 OnSelectedCityNameChanged：控件复位不级联

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
        if (value is null) return;   // 同 OnSelectedCityNameChanged：控件复位不级联

        SelectedVillageName = string.Empty;
        VillageOptions.Clear();

        if (!string.IsNullOrEmpty(value) && value != "（请选择）" && _townMap.TryGetValue(value, out var town))
        {
            SafeFireAndForget(() => LoadVillageOptionsAsync(town.Id));
        }
    }

    /// <summary>
    /// 记录一级区划加载失败：原文写日志 + 标记 <see cref="RegionLoadFailed"/>。
    /// 原实现只写了 <c>if (result.IsSuccess …)</c> 而没有 else 分支——失败时空列表、零日志，
    /// 既查不出原因，也会让保存把库中已有区划写成 NULL。
    /// </summary>
    private void NoteRegionFailure(string level, string? errorCode, string? message)
    {
        _logger.Warn($"加载{level}列表失败: {errorCode} {message}");
        RegionLoadFailed = true;
        RegionLoadMessage = $"{level}数据加载失败，所在地区可能无法回显或保存";
    }

    private async Task LoadCityOptionsAsync(int? provinceId = null)
    {
        try
        {
            IsLoadingOptions = true;
            // 选省后按省筛市；未选省（含回显期）仍取全部市，保证既有回显/级联不受影响
            var result = (provinceId.HasValue && provinceId.Value > 0)
                ? await _regionService.GetCitiesByProvinceIdAsync(provinceId.Value, CancellationToken)
                : await _regionService.GetCitiesAsync(CancellationToken);

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
                if (result.Value.Count == 0)
                    _logger.Warn("地级市列表为空（区划表可能尚未初始化）");
            }
            else
            {
                NoteRegionFailure("地级市", result.ErrorCode, result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载城市列表失败");
            RegionLoadFailed = true;
            RegionLoadMessage = "地级市数据加载异常，所在地区可能无法回显或保存";
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
            else
            {
                NoteRegionFailure("县/区", result.ErrorCode, result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载区县列表失败");
            RegionLoadFailed = true;
            RegionLoadMessage = "县/区数据加载异常，所在地区可能无法回显或保存";
        }
        finally
        {
            IsLoadingOptions = false;
        }
    }

    /// <summary>装入全量县/区（回显兜底：市一级缺失或县不属该市时用）</summary>
    private async Task LoadAllCountyOptionsAsync()
    {
        try
        {
            IsLoadingOptions = true;
            var result = await _regionService.GetCountiesAsync(CancellationToken);

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
                _logger.Warn($"县/区选项回退为全量列表（市一级未命中），共 {result.Value.Count} 条");
            }
            else
            {
                NoteRegionFailure("县/区", result.ErrorCode, result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载全量区县列表失败");
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
            else
            {
                NoteRegionFailure("乡镇/街道", result.ErrorCode, result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载乡镇列表失败");
            RegionLoadFailed = true;
            RegionLoadMessage = "乡镇/街道数据加载异常，所在地区可能无法回显或保存";
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
            else
            {
                NoteRegionFailure("村/社区", result.ErrorCode, result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载村社区列表失败");
            RegionLoadFailed = true;
            RegionLoadMessage = "村/社区数据加载异常，所在地区可能无法回显或保存";
        }
    }

    /// <summary>
    /// 回显已保存的四级区划。
    /// 逐级独立：任何一级缺失/解析不到都不影响下一级（原实现是"市没命中就整段跳过"，
    /// city_id 为空时 county/town/village 全部丢弃）；全程 try/finally 保护级联开关，
    /// 并留下可定位的日志（原实现零日志，回显失败无从排查）。
    /// </summary>
    private async Task RestoreRegionSelectionAsync()
    {
        if (_originalOrganization == null) return;

        var cityId = _originalOrganization.CityId;
        var countyId = _originalOrganization.CountyId;
        var townId = _originalOrganization.TownId;
        var villageId = _originalOrganization.VillageId;

        if (!cityId.HasValue && !countyId.HasValue && !townId.HasValue && !villageId.HasValue)
        {
            _logger.Debug("组织未配置区划，跳过所在地区回显");
            return;
        }

        _isRestoringRegion = true;
        try
        {
            // 1. 地级市
            if (cityId.HasValue)
            {
                var cityName = FindName(_cityMap, cityId.Value, c => c.Id);
                if (cityName != null)
                {
                    SelectedCityName = cityName;

                    // 省回显：按市反查所属省。回显期 _isRestoringRegion=true，OnSelectedProvinceNameChanged
                    // 会直接返回（不重载市）——回显走全量市，只有用户主动切省才筛。
                    if (_cityMap.TryGetValue(cityName, out var city) && city.ProvinceId is > 0)
                    {
                        var provinceName = _provinceMap.Values.FirstOrDefault(p => p.Id == city.ProvinceId)?.ProvinceName;
                        if (!string.IsNullOrEmpty(provinceName))
                            SelectedProvinceName = provinceName;
                    }

                    await LoadCountyOptionsByCityAsync(cityId.Value);
                }
                else
                {
                    _logger.Warn($"区划回显: city_id={cityId.Value} 不在当前市列表中（市选项 {CityOptions.Count} 条）");
                }
            }

            // 2. 县/区（不依赖市是否命中；市一级没装出来时按县 ID 反查所属市）
            if (countyId.HasValue)
            {
                var countyName = FindName(_countyMap, countyId.Value, c => c.Id)
                                  ?? await WidenCountyOptionsAsync(countyId.Value);
                if (countyName != null)
                {
                    SelectedCountyName = countyName;
                    await LoadTownOptionsAsync(countyId.Value);
                }
                else
                {
                    _logger.Warn($"区划回显: county_id={countyId.Value} 未找到，乡镇/村/社区无法回显");
                }
            }

            // 3. 乡镇/街道（有 town_id 即可按镇 ID 反查 county_id 直接装，不依赖县名解析）
            if (townId.HasValue)
            {
                var townName = FindName(_townMap, townId.Value, t => t.Id)
                                ?? await WidenTownOptionsAsync(townId.Value);
                if (townName != null)
                {
                    SelectedTownName = townName;
                    await LoadVillageOptionsAsync(townId.Value);
                }
                else
                {
                    _logger.Warn($"区划回显: town_id={townId.Value} 未找到，村/社区无法回显");
                }
            }

            // 4. 村/社区
            if (villageId.HasValue)
            {
                var villageName = FindName(_villageMap, villageId.Value, v => v.Id)
                                  ?? await WidenVillageOptionsAsync(villageId.Value);
                if (villageName != null)
                    SelectedVillageName = villageName;
                else
                    _logger.Warn($"区划回显: village_id={villageId.Value} 未找到");
            }

            _logger.Info($"区划回显完成: {SelectedCityName} > {SelectedCountyName} > {SelectedTownName} > {SelectedVillageName}"
                       + $"（选项 市{CityOptions.Count} 县{CountyOptions.Count} 镇{TownOptions.Count} 村{VillageOptions.Count}）");
        }
        finally
        {
            _isRestoringRegion = false;
        }
    }

    /// <summary>按 ID 在"名称→实体"字典里反查显示名（同名行政区会覆盖字典项，ID 才是唯一可靠键）</summary>
    private static string? FindName<T>(Dictionary<string, T> map, int id, Func<T, int> idOf)
    {
        foreach (var kv in map)
            if (idOf(kv.Value) == id)
                return kv.Key;
        return null;
    }

    /// <summary>县选项缺该条时的兜底：按县 ID 反查所属市 → 重装县级选项，成功返回县名</summary>
    private async Task<string?> WidenCountyOptionsAsync(int countyId)
    {
        var county = await _regionService.GetCountyByIdAsync(countyId, CancellationToken);
        if (county.IsFailure || county.Value == null || string.IsNullOrEmpty(county.Value.CityName))
            return null;

        var city = await _regionService.GetCityByNameAsync(county.Value.CityName, CancellationToken);
        if (city.IsSuccess && city.Value != null && SelectedCityName != city.Value.CityName)
        {
            SelectedCityName = city.Value.CityName;
            await LoadCountyOptionsByCityAsync(city.Value.Id);
        }
        else if (_countyMap.Count <= 1)
        {
            await LoadAllCountyOptionsAsync();
        }

        return FindName(_countyMap, countyId, c => c.Id);
    }

    /// <summary>乡镇选项缺该条时的兜底：按镇 ID 反查 county_id 重装选项，成功返回镇名</summary>
    private async Task<string?> WidenTownOptionsAsync(int townId)
    {
        var town = await _regionService.GetTownByIdAsync(townId, CancellationToken);
        if (town.IsFailure || town.Value == null) return null;

        await LoadTownOptionsAsync(town.Value.CountyId);
        return FindName(_townMap, townId, t => t.Id);
    }

    /// <summary>村/社区选项缺该条时的兜底：按村 ID 反查 town_id 重装选项，成功返回村名</summary>
    private async Task<string?> WidenVillageOptionsAsync(int villageId)
    {
        var village = await _regionService.GetVillageByIdAsync(villageId, CancellationToken);
        if (village.IsFailure || village.Value == null) return null;

        await LoadVillageOptionsAsync(village.Value.TownId);
        return FindName(_villageMap, villageId, v => v.Id);
    }

    public async Task LoadInitialRegionAsync()
    {
        await LoadProvinceOptionsAsync();
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

            var cityId = GetCityId();
            var countyId = GetCountyId();
            var townId = GetTownId();
            var villageId = GetVillageId();

            // 区划保护：不通过就放弃本次保存（详见 GuardRegionOverwriteAsync）
            if (await GuardRegionOverwriteAsync(cityId, countyId, townId, villageId))
                return;

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
                ProvinceId = GetProvinceId(cityId),
                CityId = cityId,
                CountyId = countyId,
                TownId = townId,
                VillageId = villageId,
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

    /// <summary>
    /// 省ID（2026-10-26 真级联省）：**市是权威**——市已归属省就用市的省（用户可能没动省 Picker）；
    /// 市未归属但选了省 → 用选中省；都没有 → null（组织未配省，文书 {所在省} 输出空并进数据提醒）。
    /// </summary>
    private int? GetProvinceId(int? cityId)
    {
        if (cityId is > 0 && _cityMap.Values.FirstOrDefault(c => c.Id == cityId.Value) is { } city && city.ProvinceId is > 0)
            return city.ProvinceId;

        if (!string.IsNullOrEmpty(SelectedProvinceName) && SelectedProvinceName != "（请选择）"
            && _provinceMap.TryGetValue(SelectedProvinceName, out var province))
            return province.Id;

        return null;
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

    /// <summary>
    /// 保存前的区划保护：原实现直接把四个 ID 写进 UPDATE，只要编辑器打开时某一级选项没加载出来，
    /// 点一次保存就会把库中已有的 city_id/county_id/town_id/village_id 覆盖成 NULL（地址被"保存"掉）。
    /// 这里区分两种"变成 null"：
    ///   · 该级选项只剩"（请选择）" = 列表根本没装出来 → 拦截，不写库；
    ///   · 选项正常、用户主动选了"（请选择）" → 二次确认后才允许清空。
    /// 返回 true = 已提示，放弃本次保存。
    /// </summary>
    private async Task<bool> GuardRegionOverwriteAsync(int? cityId, int? countyId, int? townId, int? villageId)
    {
        if (_originalOrganization == null) return false;   // 新建不存在"覆盖已有区划"

        var notLoaded = new List<string>();
        var cleared = new List<string>();

        void Check(string level, int? original, int? current, int optionCount)
        {
            if (!original.HasValue || current.HasValue) return;
            if (optionCount <= 1) notLoaded.Add(level);
            else cleared.Add(level);
        }

        Check("地级市", _originalOrganization.CityId, cityId, CityOptions.Count);
        Check("县/区", _originalOrganization.CountyId, countyId, CountyOptions.Count);
        Check("乡镇/街道", _originalOrganization.TownId, townId, TownOptions.Count);
        Check("村/社区", _originalOrganization.VillageId, villageId, VillageOptions.Count);

        if (notLoaded.Count > 0)
        {
            var detail = string.Join("、", notLoaded);
            _logger.Warn($"保存组织被拦截：{detail} 选项未加载，继续保存会清空库中已有区划 "
                       + $"(OrgId={_originalOrganization.Id})");
            await _dialogService.DisplayAlertAsync(
                "无法保存",
                $"所在地区数据未加载完成（{detail}），继续保存会清空该机构已有的地区信息。请稍后重试。",
                "确定");
            return true;
        }

        if (cleared.Count > 0)
        {
            var detail = string.Join("、", cleared);
            var confirmed = await _dialogService.DisplayAlertAsync(
                "确认清空地区",
                $"将清空该机构的{detail}信息，确定继续？", "确定", "取消");
            if (!confirmed)
            {
                _logger.Info($"用户取消清空区划的保存: {detail}, OrgId={_originalOrganization.Id}");
                return true;
            }
        }

        return false;
    }

    #endregion
}