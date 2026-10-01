using CommunityToolkit.Mvvm.Input;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.TempRelief;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.ViewModels.TempRelief;

/// <summary>
/// 手机端临时救助表单 ViewModel：继承桌面 <see cref="TempReliefFormViewModel"/>，
/// 仅新增 <see cref="PickCandidateCommand"/>，供手机端候选申请人卡片（BindableLayout）点击选中后调用"使用该申请人"。
/// </summary>
public partial class MobileTempReliefFormViewModel : TempReliefFormViewModel
{
    public MobileTempReliefFormViewModel(
        ITempReliefService applicationService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        IDialogService dialogService)
        : base(applicationService, logger, serviceProvider, dialogService)
    {
    }

    /// <summary>选中候选申请人（手机端卡片点击）。</summary>
    [RelayCommand]
    private void PickCandidate(TempReliefCandidate? candidate)
    {
        if (candidate != null)
        {
            SelectedCandidate = candidate;
        }
    }
}
