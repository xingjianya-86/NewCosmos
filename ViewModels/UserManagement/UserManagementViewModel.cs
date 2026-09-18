using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;
using RoleEntity = NewCosmos.Models.Entities.UserManagement.Role;

namespace NewCosmos.ViewModels.UserManagement;

public partial class UserManagementViewModel : PagedSearchViewModelBase
{
    private readonly IUserService _userService = null!;
    private readonly IOrganizationService _organizationService = null!;
    private readonly IRoleService _roleService = null!;
    private readonly INewPermissionService _permissionService = null!;
    private readonly IDataScopeService _dataScopeService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly IDictionaryService _dictionaryService = null!;
    private readonly IServiceProvider _serviceProvider = null!;
    private int _currentUserId;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    #region Tab 状态管理
    [ObservableProperty]
    private string _selectedTab = string.Empty;

    public bool IsUsersTabSelected => SelectedTab == "Users";
    public bool IsRolesTabSelected => SelectedTab == "Roles";
    public bool IsPermissionsTabSelected => SelectedTab == "Permissions";

    [RelayCommand]
    private void SwitchTab(string tab)
    {
        SelectedTab = tab;
        OnPropertyChanged(nameof(IsUsersTabSelected));
        OnPropertyChanged(nameof(IsRolesTabSelected));
        OnPropertyChanged(nameof(IsPermissionsTabSelected));

        if (tab == "Roles") SafeFireAndForget(() => LoadRolesAsync());
        if (tab == "Permissions")
        {
            SafeFireAndForget(() => LoadRolesForPermissionFilterAsync());
            SafeFireAndForget(() => LoadPermissionsDataAsync());
        }
    }

    #endregion

    #region 用户管理

    [ObservableProperty]
    private ObservableCollection<User> _users = new();

    [ObservableProperty]
    private User _selectedUser = null!;

    [ObservableProperty]
    private string _selectedStatus = string.Empty;

    [ObservableProperty]
    private int? _selectedOrganizationId;

    [ObservableProperty]
    private RoleEntity? _selectedRoleFilter;

    [ObservableProperty]
    private bool _isUserEditorVisible;

    [ObservableProperty]
    private UserEditorViewModel _userEditorViewModel = null!;

    public List<string> StatusOptions { get; } = new() { "全部", "激活", "停用" };
    public ObservableCollection<RoleEntity> RoleFilterOptions { get; } = new();

    #endregion

    #region 角色管理

    [ObservableProperty]
    private ObservableCollection<RoleEntity> _roles = new();

    [ObservableProperty]
    private RoleEntity _selectedRole = null!;

    [ObservableProperty]
    private bool _isRoleEditorVisible;

    [ObservableProperty]
    private RoleEditorViewModel _roleEditorViewModel = null!;

    #endregion

    #region 权限配置

    [ObservableProperty]
    private ObservableCollection<PermissionGroupViewModel> _permissionGroups = new();

    [ObservableProperty]
    private RoleEntity? _selectedRoleForPermission;

    [ObservableProperty]
    private bool _isPermissionLoading;

    #endregion

    #region 权限控制属性
    [ObservableProperty]
    private bool _canCreateUser;

    [ObservableProperty]
    private bool _canEditUser;

    [ObservableProperty]
    private bool _canDeleteUser;

    [ObservableProperty]
    private bool _canResetPassword;

    [ObservableProperty]
    private bool _canToggleStatus;

    [ObservableProperty]
    private bool _canViewUser;

    [ObservableProperty]
    private bool _canViewRoles;

    [ObservableProperty]
    private bool _canManageRoles;

    [ObservableProperty]
    private bool _canDeleteRoles;

    public double CreateUserOpacity => CanCreateUser ? 1.0 : 0.5;
    public double EditUserOpacity => CanEditUser ? 1.0 : 0.5;
    public double DeleteUserOpacity => CanDeleteUser ? 1.0 : 0.5;
    public double ResetPasswordOpacity => CanResetPassword ? 1.0 : 0.5;
    public double ToggleStatusOpacity => CanToggleStatus ? 1.0 : 0.5;
    public double CreateRoleOpacity => CanManageRoles ? 1.0 : 0.5;
    public double EditRoleOpacity => CanManageRoles ? 1.0 : 0.5;
    public double DeleteRoleOpacity => CanDeleteRoles ? 1.0 : 0.5;

    #endregion

    public UserManagementViewModel(
        IUserService userService,
        IOrganizationService organizationService,
        IRoleService roleService,
        INewPermissionService permissionService,
        IDataScopeService dataScopeService,
        ILoggerService logger,
        IDialogService dialogService,
        IDictionaryService dictionaryService,
        IServiceProvider serviceProvider,
        Services.Domain.Reporting.IStatisticsService statisticsService)
    {
        _userService = userService;
        _organizationService = organizationService;
        _roleService = roleService;
        _permissionService = permissionService;
        _dataScopeService = dataScopeService;
        _logger = logger;
        _dialogService = dialogService;
        _dictionaryService = dictionaryService;
        _serviceProvider = serviceProvider;
        _statisticsService = statisticsService;
    }

    private readonly Services.Domain.Reporting.IStatisticsService _statisticsService = null!;

    public async Task InitializePermissionsAsync(int currentUserId)
    {
        _currentUserId = currentUserId;

        var requiredCodes = new[]
        {
            PermissionCodes.USER_VIEW, PermissionCodes.USER_CREATE, PermissionCodes.USER_EDIT,
            PermissionCodes.USER_DELETE, PermissionCodes.USER_RESET_PASSWORD, PermissionCodes.USER_MANAGE_STATUS,
            PermissionCodes.ROLE_VIEW, PermissionCodes.ROLE_MANAGE, PermissionCodes.ROLE_DELETE
        };
        var results = await _permissionService.CheckPermissionsAsync(_currentUserId, requiredCodes);

        CanViewUser = results.GetValueOrDefault(PermissionCodes.USER_VIEW);
        CanCreateUser = results.GetValueOrDefault(PermissionCodes.USER_CREATE);
        CanEditUser = results.GetValueOrDefault(PermissionCodes.USER_EDIT);
        CanDeleteUser = results.GetValueOrDefault(PermissionCodes.USER_DELETE);
        CanResetPassword = results.GetValueOrDefault(PermissionCodes.USER_RESET_PASSWORD);
        CanToggleStatus = results.GetValueOrDefault(PermissionCodes.USER_MANAGE_STATUS);
        CanViewRoles = results.GetValueOrDefault(PermissionCodes.ROLE_VIEW);
        CanManageRoles = results.GetValueOrDefault(PermissionCodes.ROLE_MANAGE);
        CanDeleteRoles = results.GetValueOrDefault(PermissionCodes.ROLE_DELETE);

        OnPropertyChanged(nameof(CreateUserOpacity));
        OnPropertyChanged(nameof(EditUserOpacity));
        OnPropertyChanged(nameof(DeleteUserOpacity));
        OnPropertyChanged(nameof(ResetPasswordOpacity));
        OnPropertyChanged(nameof(ToggleStatusOpacity));
        OnPropertyChanged(nameof(CreateRoleOpacity));
        OnPropertyChanged(nameof(EditRoleOpacity));
        OnPropertyChanged(nameof(DeleteRoleOpacity));

        await LoadRoleFilterOptionsAsync();
        
        SelectedStatus = "全部";
        SelectedRoleFilter = RoleFilterOptions.FirstOrDefault();

        SelectedTab = "Users";
        OnPropertyChanged(nameof(IsUsersTabSelected));
        OnPropertyChanged(nameof(IsRolesTabSelected));
        OnPropertyChanged(nameof(IsPermissionsTabSelected));
    }

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await LoadDataAsync();
        _ = LoadHeaderStatsAsync();
    }

    #region 页头统计栏

    /// <summary>统计加载失败占位</summary>
    private const string StatNA = "—";

    /// <summary>活跃用户数</summary>
    [ObservableProperty]
    private string _activeUsersText = StatNA;

    /// <summary>角色总数</summary>
    [ObservableProperty]
    private string _roleCountText = StatNA;

    private async Task LoadHeaderStatsAsync()
    {
        try
        {
            var statsTask = _statisticsService.GetActiveUsersCountAsync();
            var rolesTask = _roleService.GetAllRolesAsync();
            await Task.WhenAll(statsTask, rolesTask);

            ActiveUsersText = statsTask.Result.IsSuccess && statsTask.Result.Value > 0
                ? statsTask.Result.Value.ToString("N0")
                : (statsTask.Result.IsSuccess ? "0" : StatNA);
            RoleCountText = rolesTask.Result?.Count.ToString("N0") ?? StatNA;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "用户管理页头统计加载失败");
        }
    }

    #endregion

    private async Task LoadRoleFilterOptionsAsync()
    {
        try
        {
            var roles = await _roleService.GetAllRolesAsync();
            RoleFilterOptions.Clear();
            RoleFilterOptions.Add(new RoleEntity { Id = 0, Name = "全部", Code = "全部" });
            foreach (var role in roles)
            {
                RoleFilterOptions.Add(role);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "角色筛选选项加载失败");
        }
    }

    #region 用户管理方法

    protected override Task LoadDataAsync()
    {
        _logger.LogBusiness("开始加载用户列表");

        bool? isActive = SelectedStatus switch
        {
            "激活" => true,
            "停用" => false,
            _ => null
        };
        var roleCode = SelectedRoleFilter?.Code == "全部" ? null : SelectedRoleFilter?.Code;

        return LoadPageAsync<User>(
            async ct =>
            {
                // 组织树边界过滤：数据范围为 ALL（超管/区县）不过滤；其余仅见可管理组织内的用户
                var scope = await _dataScopeService.GetEffectiveScopeAsync(_currentUserId, ct);
                IReadOnlyCollection<int>? manageableOrgIds = null;
                if (scope.Scope != DataScopeConstants.ALL)
                    manageableOrgIds = await _dataScopeService.GetManageableOrganizationIdsAsync(_currentUserId, ct);

                var result = await _userService.SearchPagedAsync(SearchText, roleCode, isActive, manageableOrgIds, PageIndex, PageSize, ct);
                return result;
            },
            Users,
            async page =>
            {
                try
                {
                    var manageableOrgIds = await _dataScopeService.GetManageableOrganizationIdsAsync(_currentUserId);
                    foreach (var user in page.Items)
                        user.CanBeManaged = user.OrganizationId.HasValue && manageableOrgIds.Contains(user.OrganizationId.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "数据权限标注失败");
                }
                _logger.LogBusiness($"用户列表加载完成，共 {page.TotalCount} 条");
            },
            "正在加载数据...");
    }

    [RelayCommand(CanExecute = nameof(CanCreateUser))]
    private async Task CreateUserAsync()
    {
        if (!CanCreateUser) return;

        _logger.LogBusiness("开始创建用户");

        UserEditorViewModel = new UserEditorViewModel(_userService,
            _organizationService,
            _roleService,
            _dictionaryService,
            _permissionService,
            _dataScopeService,
            _currentUserId,
            _logger,
            _dialogService,
            _serviceProvider,
            null,
            async () =>
            {
                IsUserEditorVisible = false;
                await LoadDataAsync();
            });

        await UserEditorViewModel.InitializeAsync();
        IsUserEditorVisible = true;
    }

    [RelayCommand]
    private async Task EditUserAsync(User user)
    {
        if (user == null || !CanEditUser || !user.CanBeManaged) return;

        try
        {
            _logger.LogBusiness("编辑用户", ("UserId", user.Id));

        UserEditorViewModel = new UserEditorViewModel(_userService,
                _organizationService,
                _roleService,
                _dictionaryService,
                _permissionService,
                _dataScopeService,
                _currentUserId,
                _logger,
                _dialogService,
                _serviceProvider,
                user,
                async () =>
                {
                    IsUserEditorVisible = false;
                    await LoadDataAsync();
                });

            await UserEditorViewModel.InitializeAsync();
            IsUserEditorVisible = true;
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败");
            await _dialogService.DisplayAlertAsync("错误", $"打开编辑器失败: {ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private async Task DeleteUserAsync(User user)
    {
        if (user == null || !CanDeleteUser || !user.CanBeManaged) return;

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认删除",
            $"确定要删除用户「{user.Username}」吗？此操作不可撤销。",
            "确定",
            "取消");

        if (!confirm) return;

        _logger.LogBusiness("删除用户", ("UserId", user.Id));

        var result = await _userService.DeleteAsync(user.Id, CancellationToken);

        if (result.IsSuccess)
        {
            Users.Remove(user);
            _logger.LogBusiness("用户删除成功", ("UserId", user.Id));
        }
        else
        {
            _logger.Error($"删除用户失败: {result.Message}");
            await ShowFailureAsync(result, "删除用户", "删除失败");
        }
    }

    [RelayCommand]
    private async Task ToggleUserStatusAsync(User user)
    {
        if (user == null || !CanToggleStatus || !user.CanBeManaged) return;

        var actionText = user.IsActive ? "停用" : "启用";
        var confirm = await _dialogService.DisplayAlertAsync(
            "确认操作",
            $"确定要{actionText}用户「{user.Username}」吗？",
            "确定",
            "取消");

        if (!confirm)
            return;

        _logger.LogBusiness("切换用户状态", ("UserId", user.Id));

        var result = user.IsActive
            ? await _userService.DisableAsync(user.Id, CancellationToken)
            : await _userService.EnableAsync(user.Id, CancellationToken);

        if (result.IsSuccess)
        {
            await LoadDataAsync();
            _logger.LogBusiness("用户状态切换成功", ("UserId", user.Id));
        }
        else
        {
            _logger.Error($"切换用户状态失败: {result.Message}");
            await ShowFailureAsync(result, "切换用户状态", "保存失败");
        }
    }

    [RelayCommand]
    private void ResetFilters()
    {
        SearchText = string.Empty;
        SelectedStatus = "全部";
        SelectedRoleFilter = RoleFilterOptions.FirstOrDefault();
        PageIndex = 1;
        TotalCount = 0;
        SafeFireAndForget(() => LoadDataAsync());
    }

    [RelayCommand]
    private async Task ResetPasswordAsync(User user)
    {
        if (user == null || !CanResetPassword || !user.CanBeManaged) return;

        // 第一次输入密码
        var newPassword = await _dialogService.DisplayPromptAsync(
            "重置密码",
            $"请输入用户「{user.Username}」的新密码：",
            "下一步",
            "取消",
            "请输入新密码（6-20位）",
            maxLength: 20,
            keyboard: Keyboard.Text);

        if (string.IsNullOrWhiteSpace(newPassword))
            return;

        if (newPassword.Length < 6)
        {
            await _dialogService.DisplayAlertAsync("提示", "密码长度不能少于6位", "确定");
            return;
        }

        // 第二次确认密码
        var confirmPassword = await _dialogService.DisplayPromptAsync(
            "确认密码",
            "请再次输入新密码以确认：",
            "确定",
            "取消",
            "请再次输入新密码",
            maxLength: 20,
            keyboard: Keyboard.Text);

        if (string.IsNullOrWhiteSpace(confirmPassword))
            return;

        // 验证两次密码是否一致
        if (newPassword != confirmPassword)
        {
            await _dialogService.DisplayAlertAsync("错误", "两次输入的密码不一致，请重新操作", "确定");
            return;
        }

        _logger.LogBusiness("重置用户密码", ("UserId", user.Id));

        var result = await _userService.ResetPasswordAsync(user.Id, newPassword, "System", CancellationToken);

        if (result.IsSuccess)
        {
            await _dialogService.DisplayAlertAsync("成功", $"用户「{user.Username}」的密码已重置", "确定");
            _logger.LogBusiness("用户密码重置成功", ("UserId", user.Id));
        }
        else
        {
            await ShowFailureAsync(result, "密码重置");
        }
    }

    #endregion

    #region 角色管理方法

    private async Task LoadRolesAsync()
    {
        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("开始加载角色列表");

            var roles = await _roleService.GetAllRolesAsync(ct);
            Roles.Clear();
            foreach (var role in roles)
            {
                Roles.Add(role);
            }

            _logger.LogBusiness($"角色列表加载完成，共 {Roles.Count} 条");
        }, "确定");
    }

    [RelayCommand]
    private Task CreateRoleAsync()
    {
        if (!CanManageRoles) return Task.CompletedTask;

        _logger.LogBusiness("开始创建角色");

        RoleEditorViewModel = new RoleEditorViewModel(_roleService,
            _logger,
            _dialogService,
            _serviceProvider,
            null,
            async () =>
            {
                IsRoleEditorVisible = false;
                await LoadRolesAsync();
            });

        IsRoleEditorVisible = true;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task EditRoleAsync(RoleEntity role)
    {
        if (!CanManageRoles || role == null) return Task.CompletedTask;

        _logger.LogBusiness("编辑角色", ("RoleId", role.Id));

        RoleEditorViewModel = new RoleEditorViewModel(_roleService,
            _logger,
            _dialogService,
            _serviceProvider,
            role,
            async () =>
            {
                IsRoleEditorVisible = false;
                await LoadRolesAsync();
            });

        IsRoleEditorVisible = true;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task DeleteRoleAsync(RoleEntity role)
    {
        if (!CanDeleteRoles || role == null) return;

        var confirm = await _dialogService.DisplayAlertAsync("确认删除", $"确定要删除角色「{role.Name}」吗？", "确定", "取消");
        if (confirm != true) return;

        _logger.LogBusiness("删除角色", ("RoleId", role.Id));

        var result = await _roleService.DeleteRoleAsync(role.Id, CancellationToken);
        if (result.IsSuccess)
        {
            Roles.Remove(role);
            _logger.LogBusiness("角色删除成功", ("RoleId", role.Id));
        }
        else
        {
            await _dialogService.DisplayAlertAsync("删除失败", result.Message, "确定");
        }
    }

    #endregion

    #region 权限配置方法

    private async Task LoadRolesForPermissionFilterAsync()
    {
        try
        {
            var roles = await _roleService.GetAllRolesAsync();
            if (SelectedRoleForPermission == null)
            {
                SelectedRoleForPermission = roles.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载角色列表失败");
        }
    }

    private async Task LoadPermissionsDataAsync()
    {
        if (SelectedRoleForPermission == null)
        {
            PermissionGroups.Clear();
            return;
        }

        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("加载权限配置", ("RoleId", SelectedRoleForPermission.Id));

            IsPermissionLoading = true;

            try
            {
                var rolePermissionsResult = await _roleService.GetRolePermissionsAsync(SelectedRoleForPermission.Id, ct);
                var rolePermissions = rolePermissionsResult.IsSuccess
                    ? new HashSet<string>(rolePermissionsResult.Value, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>();

                var allPermissions = await _permissionService.GetAllPermissionDefinitionsAsync(ct);
                var groupedPermissions = allPermissions
                    .GroupBy(p => p.Module)
                    .OrderBy(g => g.Min(p => p.SortOrder));

                PermissionGroups.Clear();

                foreach (var group in groupedPermissions)
                {
                    var groupVm = new PermissionGroupViewModel
                    {
                        GroupName = group.Key,
                        Permissions = new ObservableCollection<PermissionItemViewModel>()
                    };

                    foreach (var permission in group.OrderBy(p => p.SortOrder))
                    {
                        groupVm.Permissions.Add(new PermissionItemViewModel
                        {
                            Code = permission.Code,
                            Name = permission.Name,
                            IsChecked = rolePermissions.Contains(permission.Code)
                        });
                    }

                    PermissionGroups.Add(groupVm);
                }

                _logger.LogBusiness("权限配置加载完成");
            }
            finally
            {
                IsPermissionLoading = false;
            }
        }, "确定");
    }

    [RelayCommand]
    private async Task SaveRolePermissionsAsync()
    {
        if (SelectedRoleForPermission == null) return;

        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("保存权限配置", ("RoleId", SelectedRoleForPermission.Id));

            var selectedPermissions = PermissionGroups
                .SelectMany(g => g.Permissions)
                .Where(p => p.IsChecked)
                .Select(p => p.Code)
                .ToList();

            var result = await _roleService.SyncRolePermissionsAsync(SelectedRoleForPermission.Id, selectedPermissions, ct);

            if (result.IsSuccess)
            {
                _logger.LogBusiness("权限配置保存成功", ("RoleId", SelectedRoleForPermission.Id), ("PermissionCount", selectedPermissions.Count));
                await _dialogService.DisplayAlertAsync("成功", "权限配置已保存", "确定");
            }
            else
            {
                _logger.Error($"操作失败");
                await _dialogService.DisplayAlertAsync("保存失败", result.Message, "确定");
            }
        }, "确定");
    }

    partial void OnSelectedRoleForPermissionChanged(RoleEntity? value)
    {
        if (SelectedTab == "Permissions" && value != null)
        {
            SafeFireAndForget(() => LoadPermissionsDataAsync());
        }
    }

    #endregion
}

public partial class UserEditorViewModel : ViewModelBase
{
    private readonly IUserService _userService = null!;
    private readonly IRoleService _roleService = null!;
    private readonly IOrganizationService _organizationService = null!;
    private readonly IDictionaryService _dictionaryService = null!;
    private readonly INewPermissionService _permissionService = null!;
    private readonly IDataScopeService _dataScopeService = null!;
    private readonly int _currentUserId;
    private readonly ILoggerService _logger = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly Func<Task> _onSaved = null!;
    private readonly User _originalUser = null!;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _identityCard = string.Empty;

    [ObservableProperty]
    private string _selectedPosition = string.Empty;

    [ObservableProperty]
    private Organization? _selectedOrganization;

    [ObservableProperty]
    private ObservableCollection<Organization> _availableOrganizations = new();

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private ObservableCollection<RoleEntity> _availableRoles = new();

    [ObservableProperty]
    private ObservableCollection<UserRoleItem> _userRoles = new();

    [ObservableProperty]
    private RoleEntity? _selectedRoleToAdd;

    public ObservableCollection<string> PositionOptions { get; } = new();

    public bool IsEditMode => _originalUser != null;

    public UserEditorViewModel(
        IUserService userService,
        IOrganizationService organizationService,
        IRoleService roleService,
        IDictionaryService dictionaryService,
        INewPermissionService permissionService,
        IDataScopeService dataScopeService,
        int currentUserId,
        ILoggerService logger,
        IDialogService dialogService,
        IServiceProvider serviceProvider,
        User user,
        Func<Task> onSaved)
    {
        _userService = userService;
        _organizationService = organizationService;
        _roleService = roleService;
        _dictionaryService = dictionaryService;
        _permissionService = permissionService;
        _dataScopeService = dataScopeService;
        _currentUserId = currentUserId;
        _logger = logger;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;
        _onSaved = onSaved;
        _originalUser = user;

        if (user != null)
        {
            Username = user.Username;
            DisplayName = user.FullName ?? string.Empty;
            Phone = user.Phone ?? string.Empty;
            IdentityCard = user.IdentityCard ?? string.Empty;
            IsActive = user.IsActive;
        }
    }

    public async Task InitializeAsync()
    {
        await LoadPositionOptionsAsync();
        await LoadOrganizationsAsync();

        if (_originalUser != null)
        {
            SelectedPosition = _originalUser.Position ?? string.Empty;
        }

        await LoadRolesAsync();
    }

    private async Task LoadPositionOptionsAsync()
    {
        try
        {
            PositionOptions.Clear();

            var result = await _dictionaryService.GetItemsByCategoryAsync(
                DictionaryTypeCodes.Positions);

            if (result.IsSuccess && result.Value != null)
            {
                foreach (var item in result.Value.Where(i => i.IsActive).OrderBy(i => i.SortOrder))
                {
                    PositionOptions.Add(item.ItemKey);
                }
            }

            if (_originalUser != null
                && !string.IsNullOrWhiteSpace(_originalUser.Position)
                && !PositionOptions.Contains(_originalUser.Position))
            {
                PositionOptions.Add(_originalUser.Position);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "失败");
        }
    }

    private async Task LoadOrganizationsAsync()
    {
        try
        {
            var result = await _organizationService.GetAllAsync();
            if (result.IsSuccess)
            {
                // 组织树边界：仅可在可管理组织子树内分配用户的所属组织；
                // 编辑模式下保留用户当前组织（保证不在范围内的既有取值可见，避免误改）
                List<int> manageableOrgIds;
                try
                {
                    manageableOrgIds = await _dataScopeService.GetManageableOrganizationIdsAsync(_currentUserId);
                }
                catch (Exception ex)
                {
                    _logger.Warn($"可管理组织解析失败，回退全量: {ex.Message}");
                    manageableOrgIds = result.Value.Select(o => o.Id).ToList();
                }

                AvailableOrganizations.Clear();
                foreach (var org in result.Value)
                {
                    if (manageableOrgIds.Contains(org.Id) || org.Id == _originalUser?.OrganizationId)
                        AvailableOrganizations.Add(org);
                }

                if (_originalUser?.OrganizationId > 0)
                    SelectedOrganization = AvailableOrganizations.FirstOrDefault(o => o.Id == _originalUser.OrganizationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载组织列表失败");
        }
    }

    private async Task LoadRolesAsync()
    {
        try
        {
            var allRoles = await _roleService.GetAllRolesAsync();
            AvailableRoles.Clear();
            foreach (var role in allRoles)
            {
                AvailableRoles.Add(role);
            }

            SelectedRoleToAdd = AvailableRoles.FirstOrDefault();

            if (_originalUser != null)
            {
                var userRoles = await _roleService.GetUserRolesAsync(_originalUser.Id);
                UserRoles.Clear();
                foreach (var role in userRoles)
                {
                    UserRoles.Add(new UserRoleItem { RoleId = role.Id, RoleName = role.Name, RoleCode = role.Code });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "失败");
            await _dialogService.DisplayAlertAsync("错误", $"角色数据加载失败: {ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private async Task AddRoleAsync()
    {
        if (SelectedRoleToAdd == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择要添加的角色", "确定");
            return;
        }

        if (UserRoles.Any(r => r.RoleId == SelectedRoleToAdd.Id))
        {
            await _dialogService.DisplayAlertAsync("提示", "该用户已拥有此角色", "确定");
            return;
        }

        UserRoles.Add(new UserRoleItem { RoleId = SelectedRoleToAdd.Id, RoleName = SelectedRoleToAdd.Name, RoleCode = SelectedRoleToAdd.Code });
    }

    public void AddRole(RoleEntity role)
    {
        if (UserRoles.Any(r => r.RoleId == role.Id)) return;
        UserRoles.Add(new UserRoleItem { RoleId = role.Id, RoleName = role.Name, RoleCode = role.Code });
    }

    [RelayCommand]
    private void RemoveRole(UserRoleItem roleItem)
    {
        if (roleItem == null) return;
        UserRoles.Remove(roleItem);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("开始保存用户");

            if (_originalUser == null)
            {
                if (string.IsNullOrWhiteSpace(Password))
                {
                    await _dialogService.DisplayAlertAsync("错误", "请输入密码", "确定");
                    return;
                }

                var createRequest = new UserCreateRequest
                {
                    Username = Username,
                    Password = Password,
                    FullName = DisplayName,
                    Phone = Phone,
                    IdentityCard = IdentityCard,
                    Position = SelectedPosition,
                    OrganizationId = SelectedOrganization?.Id,
                    CreatedBy = "System"
                };

                var result = await _userService.CreateAsync(createRequest, ct);

                if (result.IsSuccess)
                {
                    var userId = result.Value;
                    var rolesAssigned = 0;
                    var errors = new List<string>();

                    foreach (var roleItem in UserRoles)
                    {
                        var assignResult = await _userService.AssignRoleAsync(userId, roleItem.RoleId, "System", ct);
                        if (assignResult.IsSuccess)
                        {
                            rolesAssigned++;
                        }
                        else
                        {
                            errors.Add($"分配角色失败");
                            _logger.Error($"AssignRoleAsync 失败: UserId={userId}, Message={assignResult.Message}");
                        }
                    }

                    if (rolesAssigned > 0)
                    {
                        await _permissionService.InvalidateUserCacheAsync(userId);
                    }

                    if (errors.Count > 0)
                    {
                        await _dialogService.DisplayAlertAsync("部分操作失败", string.Join("\n", errors), "确定");
                    }

                    await _onSaved();
                    _logger.LogBusiness("用户创建成功", ("UserId", userId), ("RolesAssigned", rolesAssigned));
                }
                else
                {
                    _logger.Error($"操作失败");
                    await _dialogService.DisplayAlertAsync("创建失败", result.Message, "确定");
                }
            }
            else
            {
                var updateRequest = new UserUpdateRequest
                {
                    UserId = _originalUser.Id,
                    FullName = DisplayName,
                    Phone = Phone,
                    IdentityCard = IdentityCard,
                    Position = SelectedPosition,
                    OrganizationId = SelectedOrganization?.Id,
                    IsActive = IsActive,
                    UpdatedBy = "System"
                };

                var result = await _userService.UpdateAsync(updateRequest, ct);

                if (result.IsSuccess)
                {
                    var currentRoles = await _roleService.GetUserRolesAsync(_originalUser.Id, ct);
                    var currentRoleIds = currentRoles.Select(r => r.Id).ToHashSet();
                    var selectedRoleIds = UserRoles.Select(r => r.RoleId).ToHashSet();

                    if (selectedRoleIds.Count == 0 && currentRoleIds.Count > 0)
                    {
                        var confirm = await _dialogService.DisplayAlertAsync(
                            "确认", "当前未选择任何角色，保存后将清空用户的所有角色权限。\n\n是否继续？", "确认清空", "取消");
                        if (!confirm)
                        {
                            _logger.LogBusiness("用户更新取消（拒绝清空角色）", ("UserId", _originalUser.Id));
                            return;
                        }
                    }

                    var rolesToRemove = currentRoleIds.Except(selectedRoleIds).ToList();
                    var rolesToAdd = selectedRoleIds.Except(currentRoleIds).ToList();

                    var rolesRemoved = 0;
                    var rolesAdded = 0;
                    var errors = new List<string>();

                    foreach (var roleId in rolesToRemove)
                    {
                        var removeResult = await _userService.RemoveRoleAsync(_originalUser.Id, roleId, ct);
                        if (removeResult.IsSuccess)
                        {
                            rolesRemoved++;
                        }
                        else
                        {
                            errors.Add($"移除角色失败");
                            _logger.Error($"RemoveRoleAsync 失败: UserId={_originalUser.Id}");
                        }
                    }

                    foreach (var roleId in rolesToAdd)
                    {
                        var assignResult = await _userService.AssignRoleAsync(_originalUser.Id, roleId, "System", ct);
                        if (assignResult.IsSuccess)
                        {
                            rolesAdded++;
                        }
                        else
                        {
                            errors.Add($"分配角色失败");
                            _logger.Error($"AssignRoleAsync 失败: UserId={_originalUser.Id}");
                        }
                    }

                    if (rolesRemoved > 0 || rolesAdded > 0)
                    {
                        await _permissionService.InvalidateUserCacheAsync(_originalUser.Id);
                        _logger.Info($"用户角色权限已同步");
                    }

                    if (errors.Count > 0)
                    {
                        await _dialogService.DisplayAlertAsync("部分操作失败", string.Join("\n", errors), "确定");
                    }

                    await _onSaved();
                    _logger.LogBusiness("用户更新成功", 
                        ("UserId", _originalUser.Id),
                        ("RolesRemoved", rolesRemoved),
                        ("RolesAdded", rolesAdded));
                }
                else
                {
                    _logger.Error($"操作失败");
                    await _dialogService.DisplayAlertAsync("更新失败", result.Message, "确定");
                }
            }
        }, "确定");
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await _onSaved();
    }
}

public partial class RoleEditorViewModel : ViewModelBase
{
    private readonly IRoleService _roleService;
    private readonly ILoggerService _logger;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly Func<Task> _onSaved;
    private readonly RoleEntity _originalRole;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    [ObservableProperty]
    private string _roleName = string.Empty;

    [ObservableProperty]
    private string _roleCode = string.Empty;

    [ObservableProperty]
    private string _roleDescription = string.Empty;

    [ObservableProperty]
    private int _roleLevel;

    /// <summary>数据范围（角色可访问的业务数据边界）</summary>
    [ObservableProperty]
    private string _roleDataScope = DataScopeConstants.SELF;

    /// <summary>数据范围可选项</summary>
    public IReadOnlyList<string> DataScopeOptions { get; } = DataScopeConstants.OrderedScopes;

    /// <summary>数据范围中文名（Picker 显示用）</summary>
    public string DataScopeDisplay => DataScopeConstants.GetDisplayName(RoleDataScope);

    public bool IsEditMode => _originalRole != null;

    public RoleEditorViewModel(
        IRoleService roleService,
        ILoggerService logger,
        IDialogService dialogService,
        IServiceProvider serviceProvider,
        RoleEntity role,
        Func<Task> onSaved)
    {
        _roleService = roleService;
        _logger = logger;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;
        _onSaved = onSaved;
        _originalRole = role;

        if (role != null)
        {
            RoleName = role.Name;
            RoleCode = role.Code;
            RoleDescription = role.Description ?? string.Empty;
            RoleLevel = role.Level;
            RoleDataScope = string.IsNullOrWhiteSpace(role.DataScope) ? DataScopeConstants.SELF : role.DataScope;
        }
    }

    partial void OnRoleDataScopeChanged(string value) => OnPropertyChanged(nameof(DataScopeDisplay));

    [RelayCommand]
    private async Task SaveAsync()
    {
        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("开始保存角色");

            if (string.IsNullOrWhiteSpace(RoleName))
            {
                await _dialogService.DisplayAlertAsync("错误", "角色名称不能为空", "确定");
                return;
            }

            if (_originalRole == null)
            {
                if (string.IsNullOrWhiteSpace(RoleCode))
                {
                    await _dialogService.DisplayAlertAsync("错误", "角色代码不能为空", "确定");
                    return;
                }

                var createRequest = new RoleCreateRequest
                {
                    Name = RoleName,
                    Code = RoleCode.ToUpperInvariant(),
                    Description = RoleDescription,
                    Level = RoleLevel,
                    DataScope = RoleDataScope,
                    CreatedBy = "System"
                };

                var result = await _roleService.CreateRoleAsync(createRequest, ct);

                if (result.IsSuccess)
                {
                    await _onSaved();
                    _logger.LogBusiness("角色创建成功", ("RoleId", result.Value));
                }
                else
                {
                    await _dialogService.DisplayAlertAsync("创建失败", result.Message, "确定");
                }
            }
            else
            {
                var updateRequest = new RoleUpdateRequest
                {
                    RoleId = _originalRole.Id,
                    Name = RoleName,
                    Description = RoleDescription,
                    Level = RoleLevel,
                    DataScope = RoleDataScope,
                    UpdatedBy = "System"
                };

                var result = await _roleService.UpdateRoleAsync(updateRequest, ct);

                if (result.IsSuccess)
                {
                    await _onSaved();
                    _logger.LogBusiness("角色更新成功", ("RoleId", _originalRole.Id));
                }
                else
                {
                    await _dialogService.DisplayAlertAsync("更新失败", result.Message, "确定");
                }
            }
        }, "确定");
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await _onSaved();
    }
}

public class UserRoleItem
{
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
}

public class PermissionGroupViewModel
{
    public string GroupName { get; set; } = string.Empty;
    public ObservableCollection<PermissionItemViewModel> Permissions { get; set; } = new();
}

public class PermissionItemViewModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsChecked { get; set; }
}