using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.ChangeManagement;

namespace NewCosmos.Pages.ChangeManagement;

public partial class ChangePage : ContentPage, IParameterizedPage<long>
{
    private readonly ChangeViewModel _viewModel;

    public ChangePage(ChangeViewModel vm)
    {
        InitializeComponent();
        _viewModel = vm;
        BindingContext = _viewModel;
    }

    /// <summary>导航参数入口：携带档案ID直接进入（如列表页"变更"按钮）</summary>
    Task IParameterizedPage<long>.SetParameterAsync(long applicationId)
        => _viewModel.LoadApplicationInfoAsync(applicationId);

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.InitializeHeaderAsync();
    }
}
