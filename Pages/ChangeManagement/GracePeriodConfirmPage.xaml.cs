using NewCosmos.ViewModels.ChangeManagement;

namespace NewCosmos.Pages.ChangeManagement;

public partial class GracePeriodConfirmPage : ContentPage
{
    private readonly GracePeriodConfirmViewModel _viewModel;

    public GracePeriodConfirmPage(GracePeriodConfirmViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>弹窗结果：确认 => Confirmed=true；取消 => Confirmed=false</summary>
    public Task<GracePeriodConfirmResult> Result => _viewModel.Result;

    /// <summary>按 Step5 判定快照初始化</summary>
    public void Initialize(GracePeriodConfirmParameter parameter)
        => _viewModel.Initialize(parameter);
}
