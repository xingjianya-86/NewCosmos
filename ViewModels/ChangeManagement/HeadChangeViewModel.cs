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
                OperatorName = "System"
            }, CancellationToken);

            if (changeResult.IsSuccess)
            {
                await _dialogService.DisplayAlertAsync("成功", "户主变更已完成", "确定");

                // 出口三选一：整套档案 / 仅出文书 / 稍后
                var choice = await _dialogService.DisplayActionSheetAsync(
                    "户主变更完成", "取消", null, "整套档案", "仅出文书", "稍后再说");
                if (choice == "仅出文书")
                {
                    try
                    {
                        // 户主变更停旧建新：新档 ID 在 ChangeResult（若有）否则用当前档
                        var newAppId = changeResult.Value?.NewApplicationId is long na && na > 0
                            ? na
                            : ApplicationId;
                        var originalId = changeResult.Value?.NewApplicationId is long na3 && na3 > 0
                            ? ApplicationId
                            : (long?)null;

                        // 预置输出文书上下文 → 进档案制作页 → 用户点「档案输出」
                        PrintNavigationData.OutputCategories = Helpers.ArchiveCategoryResolver.DocumentOperationCategories;
                        PrintNavigationData.OperationOverride = "户主变更";
                        PrintNavigationData.PrefilterTemplateNames = new[]
                        {
                            Constants.DocumentTemplateNames.MemberChangeTable,
                            Constants.DocumentTemplateNames.ChangeNotice
                        };
                        PrintNavigationData.TemplateFilter = null;

                        await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveProductionPage, ApplicationReviewArchiveParameter>(
                            new ApplicationReviewArchiveParameter(newAppId, originalId, "户主变更"));
                    }
                    catch (Exception navEx)
                    {
                        _logger.LogError(navEx, "导航到户主变更文书输出失败");
                        await _dialogService.DisplayAlertAsync("错误", $"打开文书输出失败: {navEx.Message}", "确定");
                    }
                }
                else if (choice == "整套档案")
                {
                    PrintNavigationData.OutputCategories = null;
                    PrintNavigationData.OperationOverride = null;
                    PrintNavigationData.PrefilterTemplateNames = null;
                    PrintNavigationData.TemplateFilter = null;
                    var newAppId = changeResult.Value?.NewApplicationId is long na2 && na2 > 0
                        ? na2
                        : ApplicationId;
                    await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveProductionPage, ApplicationReviewArchiveParameter>(
                        new ApplicationReviewArchiveParameter(newAppId, ApplicationId, "户主变更"));
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