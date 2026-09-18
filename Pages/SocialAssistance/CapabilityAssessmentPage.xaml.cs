using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Pages.SocialAssistance;

public partial class CapabilityAssessmentPage : ContentPage, IParameterizedPage<ApplicationScopedParameter>
{
    private readonly CapabilityAssessmentViewModel _viewModel;

    public CapabilityAssessmentPage(CapabilityAssessmentViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>导航参数入口：加载能力鉴定数据（接口显式实现，仅经 NavigateToPageAsync 单一通道调用）</summary>
    Task IParameterizedPage<ApplicationScopedParameter>.SetParameterAsync(ApplicationScopedParameter parameter)
        => _viewModel.LoadAsync(parameter.ApplicationId);
}
