using NewCosmos.Models.Requests;
using NewCosmos.ViewModels.ArchiveManagement;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.Pages.ArchiveManagement;

/// <summary>
/// 档案照片查询（桌面端）：检索人员证件照、缩略图列表、勾选多张、预览、导出/另存为。
/// 支持从业务列表（高龄/工作流/月度审核等）带身份证号进入并自动检索。
/// </summary>
public partial class ArchivePhotoQueryPage : ContentPage, IParameterizedPage<ArchivePhotoQueryParameter>
{
    private readonly ArchivePhotoQueryViewModel _viewModel;

    public ArchivePhotoQueryPage(ArchivePhotoQueryViewModel viewModel)
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

    Task IParameterizedPage<ArchivePhotoQueryParameter>.SetParameterAsync(ArchivePhotoQueryParameter parameter)
    {
        _viewModel.ApplyParameter(parameter);
        return Task.CompletedTask;
    }
}
