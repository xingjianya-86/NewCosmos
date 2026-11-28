using NewCosmos.Models.Entities;
using NewCosmos.Models.NavigationData;
using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.ElderlyBenefits;

public partial class ElderlyStopPage : ContentPage, IParameterizedPage<ElderlyApplication>, IParameterizedPage<ElderlyStopPrefill>
{
    private readonly ElderlyStopViewModel _viewModel;
    private readonly ILoggerService _logger;

    public ElderlyStopPage(ElderlyStopViewModel viewModel, ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("ElderlyStopPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("ElderlyStopPage", false, ex.Message);
            throw;
        }
    }

    /// <summary>导航参数入口：加载待停发登记（接口显式实现，仅经 NavigateToPageAsync 单一通道调用）</summary>
    Task IParameterizedPage<ElderlyApplication>.SetParameterAsync(ElderlyApplication app)
    {
        _viewModel.LoadApplication(app);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 联动预填入口：先加载档案。
    /// 认证停发预览携带认证任务ID时，额外预填建议停发原因/应停日期/死亡日期/备注；
    /// 其余入口沿用死亡联动预填（死亡日期/原因）。
    /// </summary>
    async Task IParameterizedPage<ElderlyStopPrefill>.SetParameterAsync(ElderlyStopPrefill prefill)
    {
        _viewModel.LoadApplication(prefill.Application);

        if (prefill.CertificationTaskId is { } certificationTaskId)
        {
            _viewModel.ApplyCertificationPrefill(
                certificationTaskId,
                prefill.PresetStopReason,
                prefill.PresetDueStopDate,
                prefill.DeathDate,
                prefill.PresetNote);
        }
        else
        {
            _viewModel.ApplyDeathPrefill(prefill.DeathDate, prefill.DeathReason);
        }

        await Task.CompletedTask;
    }
}
