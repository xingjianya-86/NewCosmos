using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 异地协助专用命令（仅基本信息 + 入户调查 + 邻里走访文书）。
/// 不走分类认定/归档，办结后终态只读。
/// </summary>
public partial class ApplicationFormViewModel
{
    /// <summary>
    /// 异地协助办结：保存后将状态置为 RemoteAssistanceCompleted（终态只读）。
    /// </summary>
    [RelayCommand]
    private async Task CompleteRemoteAssistanceAsync()
    {
        if (IsViewMode) return;
        if (!IsRemoteAssistance) return;
        if (_applicationId <= 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请先保存记录后再办结", "确定");
            return;
        }

        var proceed = await _serviceProvider.GetRequiredService<IDialogService>().DisplayAlertAsync(
            "确认办结",
            "确定办结这条异地协助吗？办结后不可再编辑。",
            "确认办结", "取消");
        if (!proceed) return;

        await ExecuteAsync(async ct =>
        {
            // 先保存当前表单内容
            await SaveApplicationInternalAsync(ApplicationStatusCodes.REMOTE_ASSISTANCE_DRAFT, ct);

            // 再办结
            var appService = _serviceProvider.GetRequiredService<NewCosmos.Services.Domain.SocialAssistance.IApplicationService>();
            var operatorName = string.IsNullOrEmpty(App.CurrentUserName) ? "System" : App.CurrentUserName;
            var result = await appService.CompleteRemoteAssistanceAsync(_applicationId, operatorName, ct);
            result.ThrowIfFailure("异地协助办结");

            ApplicationStatus = ApplicationStatusCodes.REMOTE_ASSISTANCE_COMPLETED;
            _originalStatus = ApplicationStatusCodes.REMOTE_ASSISTANCE_COMPLETED;
            OperationMode = FormOperationMode.View;
            await ShowSnackBarAsync("异地协助已办结");
        }, "办结中...");
    }

    /// <summary>
    /// 异地协助出文书：设置模板文档类型过滤（入户调查表 + 邻里访问调查表），进入档案制作页。
    /// </summary>
    [RelayCommand]
    private async Task OpenRemoteDocumentsAsync()
    {
        if (_applicationId <= 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请先保存记录后再出文书", "确定");
            return;
        }

        // 模板文档类型过滤：只出入户调查表和邻里访问调查表
        PrintNavigationData.ClearDocumentMode();
        PrintNavigationData.TemplateDocTypeFilter = new[]
        {
            TemplateDocTypes.Survey,
            TemplateDocTypes.NeighborVisit
        };
        PrintNavigationData.IsRemoteAssistance = true;

        await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveProductionPage, ApplicationArchiveParameter>(
            new ApplicationArchiveParameter(_applicationId));
    }
}
