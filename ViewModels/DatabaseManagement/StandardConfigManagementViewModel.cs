using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Components;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Shared;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace NewCosmos.ViewModels.DatabaseManagement;

public partial class StandardConfigManagementViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IStandardConfigService _standardConfigService = null!;
    private readonly INewPermissionService _permissionService = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly ILoggerService _logger = null!;

    #region 分类列表

    [ObservableProperty]
    private ObservableCollection<StandardCategoryView> _categories = new();

    [ObservableProperty]
    private StandardCategoryView _selectedCategory = null!;

    #endregion

    #region 标准列表

    [ObservableProperty]
    private ObservableCollection<ConfigStandard> _configStandards = new();

    [ObservableProperty]
    private ConfigStandard _selectedStandard = null!;

    #endregion

    #region 编辑状态
    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isAddingStandard;

    [ObservableProperty]
    private string _editStandardName = string.Empty;

    [ObservableProperty]
    private decimal _editStandardValue;

    [ObservableProperty]
    private string _editUnit = string.Empty;

    [ObservableProperty]
    private string? _editHukouType = string.Empty;

    [ObservableProperty]
    private string? _editSupportMode = string.Empty;

    [ObservableProperty]
    private DateTime _editEffectiveStartDate;

    [ObservableProperty]
    private DateTime? _editEffectiveEndDate;

    [ObservableProperty]
    private string _editDescription = string.Empty;

    public List<string> HukouTypeOptions { get; } = new() { "Rural", "Urban" };
    public List<string> SupportModeOptions { get; } = new() { "Centralized", "Scattered" };

    #endregion

    #region 权限控制

    [ObservableProperty]
    private bool _canViewStandard;

    [ObservableProperty]
    private bool _canManageStandard;

    public double ManageOpacity => CanManageStandard ? 1.0 : 0.5;

    #endregion

    public ProgressViewModel Progress { get; } = new();

    #region SnackBar

    [ObservableProperty]
    private string _snackBarMessage = string.Empty;

    [ObservableProperty]
    private SnackBarType _snackBarType;

    [ObservableProperty]
    private bool _isSnackBarVisible;

    /// <summary>当前选中分类是否为「照料护理费」（控制计算方法公示横幅显隐）</summary>
    [ObservableProperty]
    private bool _isCareAllowanceCategory;

    /// <summary>照料护理费计算方法公示文案（公式 + 各档最低工资实时计算结果，随配置联动）</summary>
    [ObservableProperty]
    private string _careAllowanceFormulaText = string.Empty;

    #endregion

    public StandardConfigManagementViewModel(
        IServiceProvider serviceProvider,
        IStandardConfigService standardConfigService,
        INewPermissionService permissionService,
        IDialogService dialogService,
        ILoggerService logger)
    {
        _serviceProvider = serviceProvider;
        _standardConfigService = standardConfigService;
        _permissionService = permissionService;
        _dialogService = dialogService;
        _logger = logger;

        Title = "标准配置管理";

        InitializeCategories();
    }

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    private void InitializeCategories()
    {
        Categories.Clear();
        Categories.Add(new StandardCategoryView("收入标准", "RuralSubsistenceStandard", "UrbanSubsistenceStandard"));
        Categories.Add(new StandardCategoryView("分类补贴标准", "ClassifiedSubsidyStandard"));
        Categories.Add(new StandardCategoryView("特困供养标准", "RuralDestituteStandard", "UrbanDestituteStandard"));
        Categories.Add(new StandardCategoryView("渐退期配置", "GracePeriodMonths"));
        Categories.Add(new StandardCategoryView("系统配置", "MinWage", "RuralSingleRescue", "UrbanSingleRescue"));
        Categories.Add(new StandardCategoryView("最低工资档位", "MinWageLevel1", "MinWageLevel2", "MinWageLevel3"));
        Categories.Add(new StandardCategoryView("照料护理费", "CaregiverAllowanceFull", "CaregiverAllowancePartial", "CaregiverAllowanceNone"));
        Categories.Add(new StandardCategoryView("土地价格", "LandSelfFarmPrice", "LandSubleasePrice", "LandContractPrice"));
        Categories.Add(new StandardCategoryView("农业补贴价格", "LandFertilityPrice", "SoybeanPrice", "CornPrice", "RotationPrice", "SurfaceWaterRicePrice", "GroundWaterRicePrice"));
        Categories.Add(new StandardCategoryView("高龄津贴标准", "ELDERLY_SUBSIDY"));
        Categories.Add(new StandardCategoryView("临时救助小额定额", "TEMP_RELIEF_SMALL"));
    }

    public async Task InitializePermissionsAsync(int userId)
    {
        // [PERF-PROBE] 阶段0归因埋点：注意本页 code-behind 与 OnAppearingAsync 会各调一次，采样应成对出现
        var probe = Stopwatch.StartNew();
        CanViewStandard = await _permissionService.HasPermissionAsync(userId, PermissionCodes.STANDARD_VIEW);
        CanManageStandard = await _permissionService.HasPermissionAsync(userId, PermissionCodes.STANDARD_MANAGE);
        probe.Stop();
        _logger.LogPerf("标准配置-权限初始化", probe.Elapsed.TotalMilliseconds, ("UserId", userId));

        OnPropertyChanged(nameof(ManageOpacity));
    }

    public override async Task OnAppearingAsync()
    {
        // 权限由 code-behind → StartLoadingInBackground 统一执行一次（旧实现此处重复调用，权限查询跑两遍）
        // [PERF-PROBE] 阶段0归因埋点：数据加载耗时
        await MeasureAsync("标准列表加载", LoadStandardsAsync);
    }

    /// <summary>页面可见后异步执行权限检查 + 数据加载（C 组：不阻塞 PushAsync）</summary>
    public void StartLoadingInBackground() => SafeFireAndForget(async () =>
    {
        await InitializePermissionsAsync(App.CurrentUserId ?? 1);
        await OnAppearingAsync();
    }, nameof(StartLoadingInBackground));

    private async Task MeasureAsync(string part, Func<Task> load)
    {
        var probe = Stopwatch.StartNew();
        await load();
        probe.Stop();
        _logger.LogPerf("标准配置-OnAppearing", probe.Elapsed.TotalMilliseconds, ("Part", part));
    }

    partial void OnSelectedCategoryChanged(StandardCategoryView value)
    {
        if (value != null)
        {
            IsCareAllowanceCategory = value.DisplayName == "照料护理费";
            if (IsCareAllowanceCategory)
                SafeFireAndForget(() => RefreshCareAllowanceFormulaAsync(CancellationToken), nameof(RefreshCareAllowanceFormulaAsync));
            _ = LoadStandardsAsync();
        }
    }

    /// <summary>
    /// 刷新照料护理费计算方法公示文案：公式 + 三档最低工资的实时计算结果。
    /// 数值全部经 nc_config_standards（MinWageLevel1/2/3）与单点公式 WageStandardConstants.ComputeCareAllowance 取得，
    /// 改最低工资配置后重新进入本分类即刷新，无任何硬编码金额。
    /// </summary>
    private async Task RefreshCareAllowanceFormulaAsync(CancellationToken ct)
    {
        var level1 = await GetWageValueAsync(WageStandardConstants.Level1Type, ct);
        var level2 = await GetWageValueAsync(WageStandardConstants.Level2Type, ct);
        var level3 = await GetWageValueAsync(WageStandardConstants.Level3Type, ct);

        static string Triple(decimal wage) => wage <= 0
            ? "配置缺失"
            : $"{wage:N0} 元/月 → 全自理 {WageStandardConstants.ComputeCareAllowance(wage, WageStandardConstants.CareAllowanceDivisorFullSelfCare):N0}" +
              $" / 半护理 {WageStandardConstants.ComputeCareAllowance(wage, WageStandardConstants.CareAllowanceDivisorHalfCare):N0}" +
              $" / 全护理 {WageStandardConstants.ComputeCareAllowance(wage, WageStandardConstants.CareAllowanceDivisorFullCare):N0}";

        MainThread.BeginInvokeOnMainThread(() =>
        {
            CareAllowanceFormulaText =
                "计算方法：照料护理费 = 当地月最低工资 ÷ 10（全自理）、÷ 6（半护理）、÷ 4（全护理），四舍五入到元；" +
                $"依据《牡丹江市低收入人口认定实施细则》第四十五条，档位按申请所在区县解析（{WageStandardConstants.PolicyDocument}）。\n" +
                $"第一档：{Triple(level1)}；第二档：{Triple(level2)}；第三档（林口县适用）：{Triple(level3)}。\n" +
                "修改「系统配置 → 最低工资档位」后即时生效；下方三项固定标准仅作最低工资缺配置时的回退值。";
        });
    }

    private async Task<decimal> GetWageValueAsync(string standardType, CancellationToken ct)
    {
        try
        {
            var result = await _standardConfigService.GetStandardValueAsync(standardType, ct: ct);
            return result.IsSuccess ? result.Value : 0m;
        }
        catch
        {
            // 公示文案刷新失败不阻断页面（值显示"配置缺失"）
            return 0m;
        }
    }

    [RelayCommand]
    private async Task LoadStandardsAsync()
    {
        if (SelectedCategory == null) return;

        await ExecuteAsync(async ct =>
        {
            _logger.Info("加载标准列表");

            var result = await _standardConfigService.GetConfigStandardsAsync(null, ct);
            if (result.IsSuccess && result.Value is not null)
            {
                ConfigStandards.Clear();

                var filtered = result.Value.Where(s => SelectedCategory.StandardTypes.Contains(s.StandardType));

                foreach (var standard in filtered)
                {
                    ConfigStandards.Add(standard);
                }
            }

            _logger.Info($"加载完成: {ConfigStandards.Count} 条标准");
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
    private async Task SyncSchemaAsync()
    {
        if (!CanManageStandard)
        {
            await _dialogService.DisplayAlertAsync("提示", "您没有管理标准配置的权限", "确定");
            return;
        }

        var stepNames = new List<string>
        {
            "同步表结构 标准配置",
            "验证表结构"
        };

        Progress.InitializeSteps(stepNames);

        var progress = new Progress<ProgressContext>(Progress.UpdateProgress);

        try
        {
            var result = await _standardConfigService.SyncSchemaAsync(progress, CancellationToken);

            if (result.IsSuccess)
            {
                Progress.MarkAllCompleted();
                await LoadStandardsAsync();
                _logger.Info("同步完成");
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
            _logger.Error("同步失败");
            ErrorMessage = $"同步失败: {ex.Message}";
            Progress.CanCloseProgress = true;
        }
    }

    [RelayCommand]
    private async Task InitializeFromSeedAsync()
    {
        if (!CanManageStandard)
        {
            await _dialogService.DisplayAlertAsync("提示", "您没有管理标准配置的权限", "确定");
            return;
        }

        // 二次确认：服务内部会 TRUNCATE nc_config_standards 后重灌 YAML 种子，属破坏性操作
        var confirm = await _dialogService.DisplayAlertAsync(
            "确认初始化",
            "将从 YAML 种子重新导入标准配置：现有标准配置数据会被清空并覆盖，此操作不可撤销。是否继续？",
            "继续初始化",
            "取消");
        if (!confirm) return;

        var stepNames = new List<string>
        {
            "同步表结构",
            "加载YAML数据",
            "验证数据完整性",
            "清空并插入标准配置数据",
            "提交事务"
        };

        Progress.InitializeSteps(stepNames);

        var progress = new Progress<ProgressContext>(Progress.UpdateProgress);

        try
        {
            var result = await _standardConfigService.InitializeFromSeedAsync(progress, CancellationToken);

            if (result.IsSuccess)
            {
                Progress.MarkAllCompleted();
                await LoadStandardsAsync();
                _logger.Info("标准配置初始化完成");
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
            _logger.Error("初始化失败");
            ErrorMessage = $"初始化失败: {ex.Message}";
            Progress.CanCloseProgress = true;
        }
    }

    [RelayCommand]
    private void StartAddStandard()
    {
        if (!CanManageStandard || SelectedCategory == null) return;

        IsAddingStandard = true;
        IsEditing = true;
        EditStandardName = string.Empty;
        EditStandardValue = 0;
        EditUnit = "";
        EditHukouType = null;
        EditSupportMode = null;
        EditEffectiveStartDate = DateTime.Today;
        EditEffectiveEndDate = null;
        EditDescription = string.Empty;
    }

    [RelayCommand]
    private void StartEditStandard()
    {
        if (!CanManageStandard || SelectedStandard == null) return;

        IsAddingStandard = false;
        IsEditing = true;
        EditStandardName = SelectedStandard.StandardName;
        EditStandardValue = SelectedStandard.StandardValue;
        EditUnit = SelectedStandard.Unit;
        EditHukouType = SelectedStandard.HukouType;
        EditSupportMode = SelectedStandard.SupportMode;
        EditEffectiveStartDate = SelectedStandard.EffectiveStartDate;
        EditEffectiveEndDate = SelectedStandard.EffectiveEndDate;
        EditDescription = SelectedStandard.Description ?? string.Empty;
    }

    [RelayCommand]
    private async Task SaveStandardAsync()
    {
        if (!CanManageStandard || SelectedCategory == null) return;

        if (string.IsNullOrWhiteSpace(EditStandardName))
        {
            ErrorMessage = "标准名称不能为空";
            return;
        }

        await ExecuteAsync(async ct =>
        {
            var standard = new ConfigStandard
            {
                Id = IsAddingStandard ? 0 : SelectedStandard!.Id,
                StandardType = SelectedCategory.StandardTypes.FirstOrDefault() ?? string.Empty,
                StandardName = EditStandardName,
                StandardValue = EditStandardValue,
                Unit = EditUnit,
                HukouType = EditHukouType,
                SupportMode = EditSupportMode,
                EffectiveStartDate = EditEffectiveStartDate,
                EffectiveEndDate = EditEffectiveEndDate,
                Version = IsAddingStandard ? 1 : SelectedStandard!.Version,
                IsActive = true,
                Description = EditDescription
            };

            Result result;
            if (IsAddingStandard)
            {
                var createResult = await _standardConfigService.CreateConfigStandardAsync(standard, ct);
                result = createResult.IsFailure
                    ? Result.Failure(createResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, createResult.Message)
                    : Result.Success();
            }
            else
            {
                result = await _standardConfigService.UpdateConfigStandardAsync(standard, ct);
            }

            if (result.IsFailure)
            {
                ErrorMessage = result.Message;
                return;
            }

            CancelEdit();
            await LoadStandardsAsync();

            _logger.Info("保存标准配置成功");
        }, "确定");
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        IsAddingStandard = false;
        EditStandardName = string.Empty;
        EditStandardValue = 0;
        EditUnit = "";
        EditHukouType = null;
        EditSupportMode = null;
        EditEffectiveStartDate = DateTime.Today;
        EditEffectiveEndDate = null;
        EditDescription = string.Empty;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task DeleteStandardAsync()
    {
        if (!CanManageStandard || SelectedStandard == null) return;

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认删除",
            $"确定要删除标准 \"{SelectedStandard.StandardName}\" 吗？\n此操作将标记为已删除，可恢复",
            "删除",
            "取消");

        if (!confirm) return;

        await ExecuteAsync(async ct =>
        {
            var result = await _standardConfigService.DeleteConfigStandardAsync(SelectedStandard.Id, ct);
            if (result.IsFailure)
            {
                ErrorMessage = result.Message;
                return;
            }

            await LoadStandardsAsync();

            _logger.Info("删除标准配置成功");
        }, "确定");
    }
}

public class StandardCategoryView
{
    public string DisplayName { get; }
    public List<string> StandardTypes { get; }

    public StandardCategoryView(string displayName, params string[] standardTypes)
    {
        DisplayName = displayName;
        StandardTypes = standardTypes.ToList();
    }
}