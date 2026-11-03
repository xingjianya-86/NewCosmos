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
}
