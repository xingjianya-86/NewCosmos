using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Pages.SocialAssistance;

public partial class NearRelativeEntryPage : ContentPage, IParameterizedPage<NearRelativeEntryParameter>
{
    private readonly NearRelativeEntryViewModel _viewModel;

    public NearRelativeEntryPage(NearRelativeEntryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>导航参数（列表页编辑=StaffId；工作流卡片=户主上下文）由基类导航在 OnAppearing 前注入。</summary>
    Task IParameterizedPage<NearRelativeEntryParameter>.SetParameterAsync(NearRelativeEntryParameter parameter)
        => _viewModel.SetNavigationParameter(parameter);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }

    // ── 身份证自动带出 ──
    // WinUI3 绑定默认失焦才提交源，故取 e.NewTextValue 主动写回 ViewModel（同 ApplicationFormStep2View 处理）。

    /// <summary>工作人员身份证变化：满18位自动带出工作信息/载入已有备案</summary>
    private void OnStaffIdCardChanged(object? sender, TextChangedEventArgs e)
        => _viewModel.OnStaffIdCardTextChanged(e.NewTextValue ?? string.Empty);

    /// <summary>救助对象身份证变化：满18位自动从档案带出（一一对应回填关联申请ID）</summary>
    private void OnLinkIdCardChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is Entry entry && entry.BindingContext is NearRelativeLinkRow row)
            _viewModel.OnLinkIdCardTextChanged(row, e.NewTextValue ?? string.Empty);
    }
}
