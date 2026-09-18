using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Pages.SocialAssistance;

public partial class SpecialApprovalFormPage : ContentPage, IParameterizedPage<ApplicationScopedParameter>
{
    private readonly SpecialApprovalFormViewModel _viewModel;

    public SpecialApprovalFormPage(SpecialApprovalFormViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>导航参数入口：加载一事一议申报表数据（接口显式实现，仅经 NavigateToPageAsync 单一通道调用）</summary>
    Task IParameterizedPage<ApplicationScopedParameter>.SetParameterAsync(ApplicationScopedParameter parameter)
        => _viewModel.LoadAsync(parameter.ApplicationId);
}
