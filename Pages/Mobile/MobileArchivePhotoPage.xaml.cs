using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Mobile;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·档案照片：按人（身份证）采集证件/核查照片，分类标定后存库，列表查阅。
/// 参数经 <see cref="IParameterizedPage{TParam}"/> 注入（身份证/申请ID/姓名）。
/// </summary>
public partial class MobileArchivePhotoPage : ContentPage, IParameterizedPage<ArchivePhotoParameter>
{
    private readonly MobileArchivePhotoViewModel _viewModel;

    public MobileArchivePhotoPage(MobileArchivePhotoViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }

    Task IParameterizedPage<ArchivePhotoParameter>.SetParameterAsync(ArchivePhotoParameter parameter)
    {
        _viewModel.ApplyParameter(parameter.ApplicantIdCard, parameter.ApplicationId, parameter.ApplicantName);
        return Task.CompletedTask;
    }
}
