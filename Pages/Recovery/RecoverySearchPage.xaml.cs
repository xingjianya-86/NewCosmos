using NewCosmos.Models.Entities;
using NewCosmos.Models.NavigationData;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Recovery;

namespace NewCosmos.Pages.Recovery;

public partial class RecoverySearchPage : ContentPage, IParameterizedPage<RecoverySearchParameter>
{
    private readonly RecoverySearchViewModel _viewModel;

    public RecoverySearchPage(RecoverySearchViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>导航参数注入（死亡说明联动预填关键词 + 记录页签；Push 前调用）</summary>
    Task IParameterizedPage<RecoverySearchParameter>.SetParameterAsync(RecoverySearchParameter parameter)
    {
        _viewModel.ApplyNavigationParameter(parameter);
        return Task.CompletedTask;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // 统计 + 按页签/参数的初始列表加载（桌面与手机孪生页统一入口）
        await _viewModel.LoadInitialListAsync();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is StoppedPersonDto selectedPerson)
        {
            _viewModel.SelectedPerson = selectedPerson;
        }
    }
}
