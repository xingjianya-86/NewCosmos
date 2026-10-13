using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ChangeManagement;

    /// <summary>
    /// 户主变更ViewModel
    /// </summary>
public partial class HeadChangeViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IApplicationService _applicationService = null!;
    private readonly IFamilyMemberService _familyMemberService = null!;
    private readonly IChangeService _changeService = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly ILoggerService _logger = null!;

    #region 当前户主信息

    [ObservableProperty]
    private long _applicationId;

    [ObservableProperty]
    private string _currentHeadName = string.Empty;

    [ObservableProperty]
    private string _currentHeadIdCard = string.Empty;

    [ObservableProperty]
    private string _currentClassification = string.Empty;

    [ObservableProperty]
    private string _currentClassificationName = string.Empty;

    [ObservableProperty]
    private decimal _currentAmount;

    [ObservableProperty]
    private int _familySize;

    #endregion

    #region 可选新户主

    [ObservableProperty]
    private ObservableCollection<FamilyMember> _eligibleMembers = new();

    [ObservableProperty]
    private FamilyMember _selectedNewHead = null!;

    #endregion

    #region 变更原因

    [ObservableProperty]
    private string _changeReason = string.Empty;

    #endregion

    public HeadChangeViewModel(
        IServiceProvider serviceProvider,
        IApplicationService applicationService,
        IFamilyMemberService familyMemberService,
        IChangeService changeService,
        IDialogService dialogService,
        ILoggerService logger)
    {
        _serviceProvider = serviceProvider;
        _applicationService = applicationService;
        _familyMemberService = familyMemberService;
        _changeService = changeService;
        _dialogService = dialogService;
        _logger = logger;
        Title = "户主变更";
    }

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    /// <summary>
    /// 加载数据
    /// </summary>
    public async Task LoadDataAsync(long applicationId)
    {
        ApplicationId = applicationId;
        IsBusy = true;

        try
        {
            _logger.LogBusiness("加载户主变更数据", ("ApplicationId", applicationId));

            // 加载申请信息
            var appResult = await _applicationService.GetByIdAsync(applicationId, CancellationToken);
            if (appResult.IsSuccess && appResult.Value != null)
            {
                var app = appResult.Value;
                CurrentHeadName = app.ApplicantName;
                CurrentHeadIdCard = app.ApplicantIdCard;
                CurrentClassification = app.ClassificationResult ?? string.Empty;
                CurrentClassificationName = ClassificationConstants.ConvertFromCode(CurrentClassification);
                CurrentAmount = app.TotalGuaranteeAmount;
                FamilySize = app.FamilySize;
            }

            // 加载可选新户主（排除原户主）
            var membersResult = await _familyMemberService.GetByApplicationIdAsync(applicationId, CancellationToken);
            if (membersResult.IsSuccess)
            {
                EligibleMembers.Clear();
                foreach (var member in membersResult.Value.Where(m => !m.IsHouseholdHead))
                {
                    EligibleMembers.Add(member);
                }
            }

            _logger.LogBusiness("户主变更数据加载完成");
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 确认变更
    /// </summary>
    [RelayCommand]
    private async Task ConfirmChangeAsync()
    {
        // 验证
        if (SelectedNewHead == null)
        {
            await _dialogService.DisplayAlertAsync("验证失败", "请选择新户主", "确定");
            return;
        }

        if (string.IsNullOrWhiteSpace(ChangeReason))
        {
            await _dialogService.DisplayAlertAsync("验证失败", "请填写变更原因", "确定");
            return;
        }

        // 确认对话框
        bool confirm = await _dialogService.DisplayAlertAsync(
            "确认变更",
            $"确认将户主从 {CurrentHeadName} 变更为 {SelectedNewHead.Name}",
            "确认",
            "取消");

        if (!confirm) return;

        _logger.LogBusiness("执行户主变更", ("OldHead", CurrentHeadName), ("NewHead", SelectedNewHead.Name));

        IsBusy = true;

        try
        {
            // 执行户主变更
            var changeResult = await _changeService.ExecuteHeadChangeAsync(new HeadChangeContext
            {
                ApplicationId = ApplicationId,
                OldHeadMemberId = 0, // TODO: 获取原户主成员ID
                NewHeadMemberId = SelectedNewHead.Id,
                OldHeadName = CurrentHeadName,
                NewHeadName = SelectedNewHead.Name,
                ChangeReason = ChangeReason,
                // 审计记真实操作人（原硬编码 "System"，致 nc_biz_change_records 操作人不可追溯；
                // 与 ApplicationFormViewModel/UserManagementViewModel 同型，一次修全）
                OperatorName = string.IsNullOrEmpty(App.CurrentUserName) ? "System" : App.CurrentUserName
            }, CancellationToken);

            if (changeResult.IsSuccess)
            {
                await _dialogService.DisplayAlertAsync("成功", "户主变更已完成", "确定");

                // 成功后直接进档案产出页（整档模式）。原「整套档案 / 仅出文书 / 稍后再说」三选一已合并为单一出口：
                //  · 户主变更停旧建新的新档必须归档才算收尾——旧档已 Stopped，新档不归档则本户无在保档案；
                //  ·「仅出文书」会设 OutputCategories/OperationOverride/PrefilterTemplateNames，
                //    触发 ArchiveOutputViewModel 的 IsDocumentMode=true →「完成归档」按钮被隐藏、
                //    CompleteAsync 入口直接 return，新档永远无法归档（该绑定本是为渐退草稿设的护栏，
                //    户主变更复用同一上下文时被误伤）；
                //  ·「稍后再说」则新档留在列表里无人认领。
                // 三字段清空 → IsDocumentMode=false，归档能力天然可用；退路为用户直接返回、不点归档。
                PrintNavigationData.OutputCategories = null;
                PrintNavigationData.OperationOverride = null;
                PrintNavigationData.PrefilterTemplateNames = null;
                PrintNavigationData.TemplateFilter = null;

                // 未停旧建新（NewApplicationId 为 0/null）时新旧档同为当前档，此时不该把当前档
                // 自己当作"变更前旧档案"传入（会让停保告知书判定拿到自指的原始档）→ 置 null。
                var newAppId = changeResult.Value?.NewApplicationId is long na && na > 0
                    ? na
                    : ApplicationId;
                var originalId = changeResult.Value?.NewApplicationId is long na2 && na2 > 0
                    ? ApplicationId
                    : (long?)null;

                try
                {
                    await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveProductionPage, ApplicationReviewArchiveParameter>(
                        new ApplicationReviewArchiveParameter(newAppId, originalId, "户主变更"));
                }
                catch (Exception navEx)
                {
                    _logger.LogError(navEx, "导航到户主变更文书输出失败");
                    await _dialogService.DisplayAlertAsync("错误", $"打开文书输出失败: {navEx.Message}", "确定");
                }
            }
            else
            {
                await _dialogService.DisplayAlertAsync("错误", $"变更失败: {changeResult.Message}", "确定");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败: {ex.Message}");
            await _dialogService.DisplayAlertAsync("错误", $"变更失败: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }
}