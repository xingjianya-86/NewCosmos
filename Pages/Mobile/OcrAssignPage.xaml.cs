using NewCosmos.Models.Requests;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Mobile;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 识别结果分配页：展示识别记录卡片（字段可编辑 + 目标下拉），确认后经来源表单的
/// <see cref="Services.Platform.IOcrAssignContext"/> 逐条回填。跨平台（桌面走照片采集入口同样可达）。
/// </summary>
public partial class OcrAssignPage : ContentPage, IParameterizedPage<OcrAssignParameter>
{
    private readonly OcrAssignViewModel _viewModel;

    public OcrAssignPage(OcrAssignViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public Task SetParameterAsync(OcrAssignParameter parameter)
    {
        _viewModel.Initialize(parameter);
        return Task.CompletedTask;
    }

    /// <summary>系统返回/手势返回：放行默认 pop 前先放行完成回调（否则来源页按钮永久灰死）</summary>
    protected override bool OnBackButtonPressed()
    {
        _viewModel.NotifyAbandoned();
        return false;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // 兜底：任何未经「确认回填」的离页也必须恰好放行一次完成回调。
        // 延迟一拍判断：退后台 / 原生弹窗时本页仍在导航栈中，不会误触发。
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300), () =>
        {
            if (!Navigation.NavigationStack.Contains(this))
                _viewModel.NotifyAbandoned();
        });
    }
}
