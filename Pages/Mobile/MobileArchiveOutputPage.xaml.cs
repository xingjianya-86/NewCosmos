using NewCosmos.Models.NavigationData;
using NewCosmos.ViewModels.ArchiveManagement;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·档案输出（推送打印工作台）。
/// 手机端无 Office/COM：本页不生成文书、不调打印机，只把勾选的模板推送进打印队列，
/// 由电脑端打印代理（PrintAgentService）认领后生成并打印。
/// 业务逻辑与模板清单全部复用 <see cref="ArchiveOutputViewModel"/>（与桌面页同一类型）。
/// </summary>
public partial class MobileArchiveOutputPage : ContentPage
{
    private readonly ArchiveOutputViewModel _viewModel;

    public MobileArchiveOutputPage(ArchiveOutputViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();

        // 页面已不在导航栈中（真正离开输出流程，而非 push 子页）时清空打印上下文：
        // FieldData/TableData 含公民 PII，不能驻留到进程退出。
        var stack = Helpers.WindowNavigator.CurrentNavigation?.NavigationStack;
        if (stack == null || !stack.Contains(this))
        {
            PrintNavigationData.Clear();
        }
    }
}
