using System.ComponentModel;
using NewCosmos.Models;
using NewCosmos.Models.Results;
using NewCosmos.Pages.SocialAssistance.FormSteps;
using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.SocialAssistance;
using NewCosmos.Models.Entities;

namespace NewCosmos.Pages.SocialAssistance;

public partial class ApplicationFormPage : ContentPage,
    IParameterizedPage<FormPageParameter>,
    IParameterizedPage<AssetCheckFormParameter>
{
    private readonly ApplicationFormViewModel _viewModel;
    private readonly ILoggerService _logger;

    /// <summary>
    /// 步骤视图懒加载缓存：Step1~Step5 各自的 ContentView 首次进入该步骤时才创建，
    /// 任一时刻 StepHost 中仅挂载当前步骤的可视树
    /// </summary>
    private readonly ContentView?[] _stepViews = new ContentView?[5];

    /// <summary>
    /// 当前已订阅 PropertyChanged（监听 CurrentStep）的 ViewModel，用于幂等订阅/退订
    /// </summary>
    private ApplicationFormViewModel? _stepSubscribedViewModel;

    public ApplicationFormPage(ApplicationFormViewModel viewModel, ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("ApplicationFormPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("ApplicationFormPage", false, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// BindingContext 设置或替换（ViewModel 更换）时：重新订阅步骤变化并刷新当前步骤视图
    /// </summary>
    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        SubscribeToStepChanges();
        ShowCurrentStepView();
    }

    /// <summary>
    /// 幂等订阅 ViewModel 的 PropertyChanged（监听 CurrentStep 变化以懒加载步骤视图）
    /// </summary>
    private void SubscribeToStepChanges()
    {
        var vm = BindingContext as ApplicationFormViewModel;
        if (ReferenceEquals(_stepSubscribedViewModel, vm))
            return;

        UnsubscribeFromStepChanges();

        if (vm != null)
        {
            vm.PropertyChanged += OnStepViewModelPropertyChanged;
            _stepSubscribedViewModel = vm;
        }
    }

    /// <summary>
    /// 退订 ViewModel 的 PropertyChanged
    /// </summary>
    private void UnsubscribeFromStepChanges()
    {
        if (_stepSubscribedViewModel != null)
        {
            _stepSubscribedViewModel.PropertyChanged -= OnStepViewModelPropertyChanged;
            _stepSubscribedViewModel = null;
        }
    }

    private void OnStepViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApplicationFormViewModel.CurrentStep))
        {
            ShowCurrentStepView();
        }
    }

    /// <summary>
    /// 将当前步骤对应的 ContentView 挂载到 StepHost（首次进入该步骤时才创建视图）
    /// </summary>
    private void ShowCurrentStepView()
    {
        if (StepHost == null)
            return;

        if (BindingContext is not ApplicationFormViewModel vm)
            return;

        var step = vm.CurrentStep;
        if (step < 1 || step > _stepViews.Length)
            return;

        var view = _stepViews[step - 1] ??= CreateStepView(step);
        view.BindingContext = BindingContext;

        if (StepHost.Children.Count == 1 && ReferenceEquals(StepHost.Children[0], view))
            return;

        StepHost.Children.Clear();
        StepHost.Children.Add(view);
    }

    private static ContentView CreateStepView(int step) => step switch
    {
        1 => new ApplicationFormStep1View(),
        2 => new ApplicationFormStep2View(),
        3 => new ApplicationFormStep3View(),
        4 => new ApplicationFormStep4View(),
        5 => new ApplicationFormStep5View(),
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "无效的表单步骤")
    };

    /// <summary>导航参数入口·常规建档（接口显式实现，仅经 NavigateToPageAsync 单一通道调用）</summary>
    Task IParameterizedPage<FormPageParameter>.SetParameterAsync(FormPageParameter parameter)
        => SetOperationModeCoreAsync(parameter.Mode, parameter.ApplicationId);

    /// <summary>设置操作模式核心逻辑（Create/Edit/View/Review/Completion）</summary>
    private async Task SetOperationModeCoreAsync(FormOperationMode mode, long? applicationId = null)
    {
        _logger.Debug($"[PAGE] 设置操作模式: {mode}");

        _viewModel.OperationMode = mode;

        if (mode == FormOperationMode.Create)
        {
            await _viewModel.InitializeAsync();
        }
        else if (applicationId.HasValue && applicationId.Value > 0)
        {
            await _viewModel.InitializeAsync();
            await _viewModel.LoadApplicationAsync(applicationId.Value);
        }
    }

    /// <summary>导航参数入口·资产核查转申请（接口显式实现，替代原 public LoadFromAssetCheckAsync）</summary>
    async Task IParameterizedPage<AssetCheckFormParameter>.SetParameterAsync(AssetCheckFormParameter parameter)
    {
        _logger.Debug($"[PAGE] 从资产核查加载数据: {parameter.AssetCheckId}");

        // 先初始化表单
        await _viewModel.InitializeAsync();

        // 加载资产核查数据
        await _viewModel.LoadFromAssetCheckAsync(parameter.AssetCheckId);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _logger.Debug("[PAGE] ApplicationFormPage OnAppearing");

        // 幂等重新订阅步骤变化（OnDisappearing 时退订），并确保当前步骤视图已挂载
        SubscribeToStepChanges();
        ShowCurrentStepView();

        try
        {
            if (_viewModel.OperationMode == FormOperationMode.Create && !_viewModel.IsInitialized)
            {
                _logger.Info("[PAGE] 开始初始化 ApplicationFormViewModel");
                await _viewModel.InitializeAsync();
                _logger.Info("[PAGE] ApplicationFormViewModel 初始化完成");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApplicationFormPage.OnAppearing 初始化失败");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        UnsubscribeFromStepChanges();
    }

    /// <summary>
    /// 家庭成员身份证号变化时自动更新性别、年龄和身体状况
    /// </summary>
    private void OnIdCardChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is Entry entry && entry.BindingContext is FamilyMember member)
        {
            _viewModel.OnIdCardChanged(member);
        }
    }

    /// <summary>
    /// 家庭成员身份证号变化时自动更新性别、年龄和身体状况
    /// </summary>
    private void OnFamilyMemberIdCardChanged(object? sender, TextChangedEventArgs e)
    {
        OnIdCardChanged(sender, e);
    }

    /// <summary>
    /// 照料人身份证号变化时自动更新性别和年龄
    /// </summary>
    private void OnCaregiverIdCardChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is Entry entry && entry.BindingContext is Caregiver caregiver)
        {
            _viewModel.OnCaregiverIdCardChanged(caregiver);
        }
    }

    /// <summary>
    /// 家庭成员残疾证号变化时自动解析残疾类型、等级并更新身体状况
    /// </summary>
    private void OnFamilyMemberDisabilityCardNoChanged(object? sender, TextChangedEventArgs e)
    {
        OnDisabilityCertificateChanged(sender, e);
    }

    /// <summary>
    /// 家庭成员残疾证号变化时自动解析残疾类型、等级并更新身体状况
    /// </summary>
    private void OnDisabilityCertificateChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is Entry entry && entry.BindingContext is FamilyMember member)
        {
            _viewModel.OnDisabilityCertificateChanged(member);
        }
    }

    /// <summary>
    /// 家庭成员疾病分类变化时更新身体状况
    /// </summary>
    private void OnFamilyMemberDiseaseCategoryChanged(object? sender, EventArgs e)
    {
        OnDiseaseCategoryChanged(sender, e);
    }

    /// <summary>
    /// 家庭成员疾病分类变化时更新身体状况
    /// </summary>
    private void OnDiseaseCategoryChanged(object? sender, EventArgs e)
    {
        if (sender is Picker picker && picker.BindingContext is FamilyMember member)
        {
            _viewModel.OnDiseaseCategoryChanged(member);
        }
    }

    /// <summary>
    /// 搜索结果选中变化时更新视觉反馈
    /// </summary>
    private void OnSearchResultSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _viewModel.OnSearchResultSelectionChanged(e.PreviousSelection, e.CurrentSelection);
    }
}
