using NewCosmos.Models.NavigationData;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Recovery;

namespace NewCosmos.Pages.Recovery;

public partial class RecoveryFormPage : ContentPage, IParameterizedPage<RecoveryFormParameter>
{
    private readonly RecoveryFormViewModel _viewModel;

    public RecoveryFormPage(RecoveryFormViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>
    /// 导航参数注入：Record（编辑草稿）优先，否则 Person（搜索页选人新建）。
    /// 原 StoppedPersonDto / RecoveryRecord 双契约合一于此。
    /// </summary>
    Task IParameterizedPage<RecoveryFormParameter>.SetParameterAsync(RecoveryFormParameter parameter)
    {
        if (parameter == null) return Task.CompletedTask;

        if (parameter.Record != null)
            _viewModel.LoadRecord(parameter.Record);
        else if (parameter.Person != null)
            _viewModel.LoadPersonInfo(parameter.Person);

        return Task.CompletedTask;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}
