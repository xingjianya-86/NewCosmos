using System.ComponentModel;
using NewCosmos.Pages.Mobile.SocialAssistance.FormSteps;
using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·低收入人口认定申请表单（5 步）。
/// 复用桌面 <see cref="ApplicationFormViewModel"/> 与 FormSteps 步骤视图，
/// 仅重建手机壳（顶部信息 + 步骤条 + 步骤宿主 + 底部操作栏）。
/// 参数注入经 <see cref="IParameterizedPage{TParam}"/>，与桌面页保持同一契约。
/// </summary>
public partial class MobileApplicationFormPage : ContentPage,
    IParameterizedPage<FormPageParameter>,
    IParameterizedPage<AssetCheckFormParameter>
{
    private readonly ApplicationFormViewModel _viewModel;
    private readonly ILoggerService _logger;

    private readonly ContentView?[] _stepViews = new ContentView?[5];
    private ApplicationFormViewModel? _stepSubscribedViewModel;

    public MobileApplicationFormPage(ApplicationFormViewModel viewModel, ILoggerService logger)
    {
        _viewModel = viewModel;
        _logger = logger;

        InitializeComponent();
        BindingContext = _viewModel;
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        SubscribeToStepChanges();
        ShowCurrentStepView();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        SubscribeToStepChanges();
        ShowCurrentStepView();

        try
        {
            if (_viewModel.OperationMode == FormOperationMode.Create && !_viewModel.IsInitialized)
            {
                await _viewModel.InitializeAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MobileApplicationFormPage.OnAppearing 初始化失败");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        UnsubscribeFromStepChanges();
    }

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

    private void ShowCurrentStepView()
    {
        if (StepHost == null || BindingContext is not ApplicationFormViewModel vm)
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
        1 => new MobileApplicationFormStep1View(),
        2 => new MobileApplicationFormStep2View(),
        3 => new MobileApplicationFormStep3View(),
        4 => new MobileApplicationFormStep4View(),
        5 => new MobileApplicationFormStep5View(),
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "无效的表单步骤")
    };

    /// <summary>导航参数入口·常规建档（接口显式实现，与桌面页一致）。</summary>
    Task IParameterizedPage<FormPageParameter>.SetParameterAsync(FormPageParameter parameter)
        => SetOperationModeCoreAsync(parameter.Mode, parameter.ApplicationId);

    /// <summary>导航参数入口·资产核查转申请（接口显式实现，与桌面页一致）。</summary>
    async Task IParameterizedPage<AssetCheckFormParameter>.SetParameterAsync(AssetCheckFormParameter parameter)
    {
        await _viewModel.InitializeAsync();
        await _viewModel.LoadFromAssetCheckAsync(parameter.AssetCheckId);
    }

    private async Task SetOperationModeCoreAsync(FormOperationMode mode, long? applicationId = null)
    {
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
}
