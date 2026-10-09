using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Components;
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

public partial class DictionaryManagementViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IDictionaryService _dictionaryService = null!;
    private readonly INewPermissionService _permissionService = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IDictCacheService _dictCacheService = null!;

    [ObservableProperty]
    private DictCategoryView _selectedCategory = null!;

    [ObservableProperty]
    private DictItemView _selectedItem = null!;

    public ObservableCollection<DictCategoryView> Categories { get; } = new();
    public ObservableCollection<DictItemView> Items { get; } = new();

    [ObservableProperty]
    private bool _canViewDictionary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManageOpacity))]
    private bool _canManageDictionary;

    [ObservableProperty]
    private string _snackBarMessage = string.Empty;

    [ObservableProperty]
    private SnackBarType _snackBarType;

    [ObservableProperty]
    private bool _isSnackBarVisible;

    [ObservableProperty]
    private bool _isAddingItem;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editItemKey = string.Empty;

    [ObservableProperty]
    private string _editItemValue = string.Empty;

    [ObservableProperty]
    private int _editItemSortOrder;

    [ObservableProperty]
    private string _editItemDescription = string.Empty;

    public double ManageOpacity => CanManageDictionary ? 1.0 : 0.5;

    public ProgressViewModel Progress { get; } = new();

    #region Schema同步与数据初始化

    [RelayCommand]
    private async Task SyncSchemaAsync()
    {
        if (!CanManageDictionary)
        {
            await _dialogService.DisplayAlertAsync("提示", "您没有管理数据字典的权限", "确定");
            return;
        }

        var stepNames = new List<string>
        {
            "同步表结构 字典分类",
            "同步表结构 字典分类更新",
            "同步表结构 字典项表",
            "同步表结构 字典项更新",
            "重建分类视图",
            "重建字典项视图"
        };

        Progress.InitializeSteps(stepNames);

        var progress = new Progress<ProgressContext>(Progress.UpdateProgress);

        try
        {
            var result = await _dictionaryService.SyncSchemaAsync(progress, CancellationToken);

            if (result.IsSuccess)
            {
                Progress.MarkAllCompleted();
                await LoadCategoriesAsync();
                _logger.Info("字典同步完成");
            }
            else
            {
                var failedStep = Progress.ProgressSteps.FirstOrDefault(s => s.Status == ProgressStepStatus.InProgress);
                if (failedStep != null)
                {
                    Progress.MarkStepFailed(failedStep.StepNumber - 1);
                }
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("操作失败");
            ErrorMessage = $"同步失败: {ex.Message}";
            Progress.CanCloseProgress = true;
        }
    }

    [RelayCommand]
    private async Task InitializeFromSeedAsync()
    {
        if (!CanManageDictionary)
        {
            await _dialogService.DisplayAlertAsync("提示", "您没有管理数据字典的权限", "确定");
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认初始化",
            "确定要从种子数据初始化字典数据吗？\n此操作将导入系统预置的字典分类和字典项数据",
            "确认初始化",
            "取消");

        if (!confirm) return;

        var stepNames = new List<string>
        {
            "加载分类数据",
            "加载字典项数据",
            "验证数据完整性",
            "确保表结构存在",
            "合并字典分类数据",
            "合并字典项数据"
        };

        Progress.InitializeSteps(stepNames);

        var progress = new Progress<ProgressContext>(Progress.UpdateProgress);

        try
        {
            var result = await _dictionaryService.InitializeFromSeedAsync(progress, CancellationToken);

            if (result.IsSuccess)
            {
                Progress.MarkAllCompleted();
                await LoadCategoriesAsync();
                _logger.Info("字典数据初始化完成");
            }
            else
            {
                var failedStep = Progress.ProgressSteps.FirstOrDefault(s => s.Status == ProgressStepStatus.InProgress);
                if (failedStep != null)
                {
                    Progress.MarkStepFailed(failedStep.StepNumber - 1);
                }
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("操作失败");
            ErrorMessage = $"初始化失败: {ex.Message}";
            Progress.CanCloseProgress = true;
        }
    }

    #endregion

    public DictionaryManagementViewModel(
        IServiceProvider serviceProvider,
        IDictionaryService dictionaryService,
        INewPermissionService permissionService,
        IDialogService dialogService,
        ILoggerService logger,
        IDictCacheService dictCacheService)
    {
        _serviceProvider = serviceProvider;
        _dictionaryService = dictionaryService;
        _permissionService = permissionService;
        _dialogService = dialogService;
        _logger = logger;
        _dictCacheService = dictCacheService;

        Title = "数据字典管理";
    }

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    public async Task InitializePermissionsAsync(int userId)
    {
        // [PERF-PROBE] 阶段0归因埋点：子页进入时的权限查询耗时
        var probe = Stopwatch.StartNew();
        CanViewDictionary = await _permissionService.HasPermissionAsync(userId, PermissionCodes.DICTIONARY_VIEW);
        CanManageDictionary = await _permissionService.HasPermissionAsync(userId, PermissionCodes.DICTIONARY_MANAGE);
        probe.Stop();
        _logger.LogPerf("字典管理-权限初始化", probe.Elapsed.TotalMilliseconds, ("UserId", userId));

        OnPropertyChanged(nameof(ManageOpacity));
    }

    public override async Task OnAppearingAsync()
    {
        // [PERF-PROBE] 阶段0归因埋点：子页数据加载耗时
        var probe = Stopwatch.StartNew();
        await LoadCategoriesAsync();
        probe.Stop();
        _logger.LogPerf("字典管理-OnAppearing", probe.Elapsed.TotalMilliseconds);
    }

    /// <summary>页面可见后异步执行权限检查 + 数据加载（C 组：不阻塞 PushAsync）</summary>
    public void StartLoadingInBackground() => SafeFireAndForget(async () =>
    {
        await InitializePermissionsAsync(App.CurrentUserId ?? 1);
        await OnAppearingAsync();
    }, nameof(StartLoadingInBackground));

    [RelayCommand]
    private async Task LoadCategoriesAsync()
    {
        await ExecuteAsync(async ct =>
        {
            _logger.Info("正在加载字典分类");

            var result = await _dictionaryService.GetCategoriesAsync(ct);
            if (result.IsSuccess && result.Value is not null)
            {
                Categories.Clear();
                foreach (var category in result.Value)
                {
                    Categories.Add(category);
                }

                if (Categories.Count > 0 && SelectedCategory == null)
                {
                    SelectedCategory = Categories[0];
                }
            }

            _logger.Info($"加载完成: {Categories.Count} 个分类");
        }, "确定");
    }

    partial void OnSelectedCategoryChanged(DictCategoryView value)
    {
        if (value != null)
        {
            SafeFireAndForget(() => LoadItemsForCategoryAsync());
        }
    }

    [RelayCommand]
    private async Task LoadItemsForCategoryAsync()
    {
        if (SelectedCategory == null) return;

        await ExecuteAsync(async ct =>
        {
            _logger.Info($"正在加载字典项: {SelectedCategory.Category}");

            var result = await _dictionaryService.GetItemsByCategoryAsync(SelectedCategory.Category, ct);
            if (result.IsSuccess && result.Value is not null)
            {
                Items.Clear();
                foreach (var item in result.Value)
                {
                    Items.Add(item);
                }
            }

            _logger.Info($"加载完成: {Items.Count} 个字典项");
        }, "确定");
    }

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

    [RelayCommand]
    private void StartAddItem()
    {
        if (!CanManageDictionary || SelectedCategory == null) return;

        IsAddingItem = true;
        IsEditing = true;
        EditItemKey = string.Empty;
        EditItemValue = string.Empty;
        EditItemSortOrder = Items.Count;
        EditItemDescription = string.Empty;
    }

    [RelayCommand]
    private void StartEditItem()
    {
        if (!CanManageDictionary || SelectedItem == null) return;

        IsAddingItem = false;
        IsEditing = true;
        EditItemKey = SelectedItem.ItemKey;
        EditItemValue = SelectedItem.ItemValue ?? string.Empty;
        EditItemSortOrder = SelectedItem.SortOrder;
        EditItemDescription = SelectedItem.Description ?? string.Empty;
    }

    [RelayCommand]
    private async Task SaveItemAsync()
    {
        if (!CanManageDictionary || SelectedCategory == null) return;

        if (string.IsNullOrWhiteSpace(EditItemKey))
        {
            ErrorMessage = "字典项键不能为空";
            return;
        }

        await ExecuteAsync(async ct =>
        {
            var item = new DictItemUpdate
            {
                Id = IsAddingItem ? 0 : SelectedItem!.Id,
                Category = SelectedCategory.Category,
                ItemKey = EditItemKey,
                ItemValue = EditItemValue,
                SortOrder = EditItemSortOrder,
                IsActive = true,
                Description = EditItemDescription,
                Source = "user"
            };

            if (IsAddingItem)
            {
                var maxId = Items.Count > 0 ? Items.Max(i => i.Id) : SelectedCategory.Id * 1000;
                item.Id = maxId + 1;
                var result = await _dictionaryService.CreateItemAsync(item, ct);
                if (result.IsFailure)
                {
                    ErrorMessage = result.Message;
                    return;
                }
            }
            else
            {
                var result = await _dictionaryService.UpdateItemAsync(item, ct);
                if (result.IsFailure)
                {
                    ErrorMessage = result.Message;
                    return;
                }
            }

            CancelEdit();
            await LoadItemsForCategoryAsync();

            _ = _dictCacheService.RefreshAsync();

            _logger.Info($"字典项保存成功: {EditItemKey}");
        }, "确定");
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        IsAddingItem = false;
        EditItemKey = string.Empty;
        EditItemValue = string.Empty;
        EditItemSortOrder = 0;
        EditItemDescription = string.Empty;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task DeleteItemAsync()
    {
        if (!CanManageDictionary || SelectedItem == null) return;

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认删除",
            $"确定要删除字典项 \"{SelectedItem.ItemKey}\" 吗？",
            "删除",
            "取消");

        if (!confirm) return;

        await ExecuteAsync(async ct =>
        {
            var result = await _dictionaryService.DeleteItemAsync(SelectedItem.Id, ct);
            if (result.IsFailure)
            {
                ErrorMessage = result.Message;
                return;
            }

            await LoadItemsForCategoryAsync();

            _ = _dictCacheService.RefreshAsync();

            _logger.Info($"字典项删除成功: {SelectedItem.ItemKey}");
        }, "确定");
    }
}