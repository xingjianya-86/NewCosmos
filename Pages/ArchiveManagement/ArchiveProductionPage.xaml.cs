using NewCosmos.ViewModels.ArchiveManagement;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.Pages.ArchiveManagement;

public partial class ArchiveProductionPage : ContentPage,
    IParameterizedPage<AssetCheckArchiveParameter>,
    IParameterizedPage<ApplicationArchiveParameter>,
    IParameterizedPage<ApplicationReviewArchiveParameter>
{
    private readonly ArchiveProductionViewModel _viewModel;

    public ArchiveProductionPage(ArchiveProductionViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_viewModel.HasBusinessData)
        {
            await _viewModel.InitializeAsync();
        }
    }

    /// <summary>导航参数入口：从资产核查记录初始化（接口显式实现）</summary>
    Task IParameterizedPage<AssetCheckArchiveParameter>.SetParameterAsync(AssetCheckArchiveParameter parameter)
        => _viewModel.InitializeFromAssetCheckAsync(parameter.VerificationId);

    /// <summary>导航参数入口：从申请记录初始化（接口显式实现）</summary>
    Task IParameterizedPage<ApplicationArchiveParameter>.SetParameterAsync(ApplicationArchiveParameter parameter)
        => _viewModel.InitializeFromApplicationAsync(parameter.ApplicationId);

    /// <summary>导航参数入口：复核/成员变更归档（接口显式实现，由复核/成员变更保存完成后调用）</summary>
    Task IParameterizedPage<ApplicationReviewArchiveParameter>.SetParameterAsync(ApplicationReviewArchiveParameter parameter)
        => _viewModel.InitializeFromApplicationReviewAsync(parameter.ApplicationId, parameter.BusinessTitle);
}
