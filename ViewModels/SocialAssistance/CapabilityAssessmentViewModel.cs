using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 能力鉴定 ViewModel
/// </summary>
public partial class CapabilityAssessmentViewModel : ViewModelBase
{
    private readonly ICapabilityAssessmentService _assessmentService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;
    private long _applicationId;
    private long _assessmentId;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    #region 评估指标

    [ObservableProperty]
    private bool _eating = true;

    [ObservableProperty]
    private bool _dressing = true;

    [ObservableProperty]
    private bool _gettingInOutOfBed = true;

    [ObservableProperty]
    private bool _usingToilet = true;

    [ObservableProperty]
    private bool _indoorWalking = true;

    [ObservableProperty]
    private bool _bathing = true;

    #endregion

    #region 评估信息

    [ObservableProperty]
    private DateTime _assessmentDate = DateTime.Today;

    [ObservableProperty]
    private string _assessorName = string.Empty;

    [ObservableProperty]
    private string _remark = string.Empty;

    #endregion

    #region 计算结果

    /// <summary>
    /// 能完成的项目数
    /// </summary>
    public int CompletedItems => (Eating ? 1 : 0) + (Dressing ? 1 : 0) + (GettingInOutOfBed ? 1 : 0) +
                                 (UsingToilet ? 1 : 0) + (IndoorWalking ? 1 : 0) + (Bathing ? 1 : 0);

    /// <summary>
    /// 自理能力等级
    /// </summary>
    public string SelfCareLevel => DictionaryConstants.CapabilityLevel.DetermineLevel(CompletedItems);

    /// <summary>
    /// 照料等级
    /// </summary>
    public string CareLevelDisplay => DictionaryConstants.CareLevel.DetermineByCapability(SelfCareLevel);

    /// <summary>
    /// 等级颜色（用于UI显示）
    /// </summary>
    public string LevelColor => CompletedItems switch
    {
        6 => "#10B981",  // 绿色 - 全自理
        5 => "#3B82F6",  // 蓝色 - 轻度失能
        3 or 4 => "#F59E0B",  // 黄色 - 中度失能
        1 or 2 => "#EF4444",  // 红色 - 重度失能
        _ => "#7C3AED"   // 紫色 - 完全失能
    };

    #endregion

    public CapabilityAssessmentViewModel(
        ICapabilityAssessmentService assessmentService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider)
    {
        _assessmentService = assessmentService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        Title = "能力鉴定";
    }

    /// <summary>
    /// 加载能力鉴定数据
    /// </summary>
    public async Task LoadAsync(long applicationId)
    {
        _applicationId = applicationId;

        try
        {
            var result = await _assessmentService.GetByApplicationIdAsync(applicationId);
            if (result.IsSuccess && result.Value != null)
            {
                var assessment = result.Value;
                _assessmentId = assessment.Id;
                AssessmentDate = assessment.AssessmentDate;
                AssessorName = assessment.AssessorName;
                Remark = assessment.Remark;

                Eating = assessment.Eating == 1;
                Dressing = assessment.Dressing == 1;
                GettingInOutOfBed = assessment.GettingInOutOfBed == 1;
                UsingToilet = assessment.UsingToilet == 1;
                IndoorWalking = assessment.IndoorWalking == 1;
                Bathing = assessment.Bathing == 1;

                RefreshCalculations();
            }
            else
            {
                // 无既有记录：默认鉴定人为当前登录用户
                if (string.IsNullOrWhiteSpace(AssessorName))
                    AssessorName = string.IsNullOrEmpty(App.CurrentUserName) ? string.Empty : App.CurrentUserName;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"加载能力鉴定失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 刷新计算结果
    /// </summary>
    private void RefreshCalculations()
    {
        OnPropertyChanged(nameof(CompletedItems));
        OnPropertyChanged(nameof(SelfCareLevel));
        OnPropertyChanged(nameof(CareLevelDisplay));
        OnPropertyChanged(nameof(LevelColor));
    }

    /// <summary>
    /// 保存能力鉴定
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            IsBusy = true;

            var assessment = new CapabilityAssessment
            {
                Id = _assessmentId,
                ApplicationId = _applicationId,
                AssessmentDate = AssessmentDate,
                AssessorName = AssessorName,
                Eating = Eating ? 1 : 0,
                Dressing = Dressing ? 1 : 0,
                GettingInOutOfBed = GettingInOutOfBed ? 1 : 0,
                UsingToilet = UsingToilet ? 1 : 0,
                IndoorWalking = IndoorWalking ? 1 : 0,
                Bathing = Bathing ? 1 : 0,
                Remark = Remark
            };

            var result = await _assessmentService.SaveAsync(assessment);
            if (result.IsSuccess)
            {
                _assessmentId = result.Value;
                _logger.LogBusiness("能力鉴定保存成功",
                    ("ApplicationId", _applicationId),
                    ("AssessmentId", _assessmentId),
                    ("SelfCareLevel", SelfCareLevel));

                await _dialogService.DisplayAlertAsync("成功", "能力鉴定已保存", "确定");

                // 保存成功后返回上级页面
                await GoBackAsync();
            }
            else
            {
                await ShowFailureAsync(result, "保存");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"保存能力鉴定失败: {ex.Message}");
            await _dialogService.DisplayAlertAsync("错误", $"保存失败: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // 返回上一页：使用基类 GoBackCommand（含栈守卫与窗口标题恢复）
}
