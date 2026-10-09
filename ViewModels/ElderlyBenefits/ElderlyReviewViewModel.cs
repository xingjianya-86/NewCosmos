using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.NavigationData;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ElderlyBenefits;

/// <summary>
/// 高龄津贴类别复核办理 ViewModel：重评身份+年龄类别，与旧类别比较；
/// 变更则停旧增新并进入档案输出页打印（取消备案表 + 登记表 + 调整备案表）。
/// </summary>
public partial class ElderlyReviewViewModel : ViewModelBase
{
    private readonly IElderlyApplicationService _applicationService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IDialogService _dialogService = null!;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    private long? _reviewId;
    private long? _applicationId;
    private string? _idCard;
    private long? _historyId;
    private ElderlyReviewEvaluation? _evaluation;
    private long _oldAppId;
    private long _newAppId;

    /// <summary>最近一次导航参数（补全表单返回后 OnAppearing 重评用）</summary>
    private ElderlyReviewPageParameter? _parameter;

    /// <summary>补全返回标志：仅置位后的下一次 OnAppearing 触发重评（避免首次进入重复加载）</summary>
    private bool _reEvaluateOnAppear;

    #region 人员与评估展示

    [ObservableProperty] private string _applicantName = string.Empty;
    [ObservableProperty] private string _applicantIdCard = string.Empty;
    [ObservableProperty] private string _gender = string.Empty;
    [ObservableProperty] private string _ageDisplay = string.Empty;
    [ObservableProperty] private string _applicationNo = string.Empty;

    /// <summary>是否仅有导入名册（当前库未建档，复核时补建旧档）</summary>
    [ObservableProperty] private bool _isHistoryOnly;

    /// <summary>是否需要先补全历史档案（名册未建档，或导入来源档案缺必填项）——提示条与拦截共用</summary>
    [ObservableProperty] private bool _isCompletionNeeded;

    /// <summary>补全缺口提示（如"尚缺：户籍市、开户行"），空=已补全</summary>
    [ObservableProperty] private string _completionHint = string.Empty;

    [ObservableProperty] private string _oldCategoryName = string.Empty;
    [ObservableProperty] private string _oldAmountText = string.Empty;
    [ObservableProperty] private string _newCategoryName = string.Empty;
    [ObservableProperty] private string _newAmountText = string.Empty;

    [ObservableProperty] private bool _hasChange;
    [ObservableProperty] private string _changeTypeText = string.Empty;
    [ObservableProperty] private string _identityFlag = string.Empty;
    [ObservableProperty] private string _adjustReasonName = string.Empty;
    [ObservableProperty] private string _effectiveMonthText = string.Empty;
    [ObservableProperty] private string _issueStartMonthText = string.Empty;

    [ObservableProperty] private string _reviewOpinion = string.Empty;

    /// <summary>复核历史（同人）</summary>
    [ObservableProperty] private ObservableCollection<ElderlyReview> _historyRecords = new();

    #endregion

    public bool CanConfirm => _evaluation != null && !IsBusy;

    public ElderlyReviewViewModel(
        IElderlyApplicationService applicationService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        IDialogService dialogService)
    {
        _applicationService = applicationService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _dialogService = dialogService;
        Title = "高龄津贴复核";
    }

    /// <summary>导航参数入口：定位人员并评估</summary>
    public async Task LoadAsync(ElderlyReviewPageParameter parameter)
    {
        _parameter = parameter;
        _reviewId = parameter.ReviewId;
        _applicationId = parameter.ApplicationId;
        _idCard = parameter.IdCard;
        _historyId = parameter.HistoryId;

        await ExecuteAsync(async () =>
        {
            var result = _reviewId is > 0
                ? await _applicationService.EvaluateReviewByPendingAsync(_reviewId.Value, CancellationToken)
                : await _applicationService.EvaluateReviewAsync(_applicationId, _idCard, _historyId, CancellationToken);
            if (result.IsFailure || result.Value == null)
            {
                // 取消（页面已离开）不是错误，不弹窗
                if (result.ErrorCode != ErrorCodes.CANCELLED)
                    await _dialogService.DisplayAlertAsync("提示", result.Message ?? "复核评估失败", "确定");
                return result;
            }

            _evaluation = result.Value;
            _oldAppId = result.Value.ApplicationId ?? 0;
            _newAppId = 0;
            ApplyEvaluation(result.Value);
            await RefreshCompletionStateAsync(result.Value);

            var historyResult = await _applicationService.GetReviewsByPersonAsync(result.Value.IdCard, CancellationToken);
            if (historyResult.IsSuccess && historyResult.Value != null)
            {
                HistoryRecords.Clear();
                foreach (var item in historyResult.Value) HistoryRecords.Add(item);
            }
            return result;
        }, "正在重评类别...");
    }

    /// <summary>
    /// 从补全表单返回本页时重评（<see cref="_reEvaluateOnAppear"/> 置位才触发，避免首次进入重复加载）：
    /// 补建/补全后 IsHistoryOnly 归位、提示条消失、显示档案编号，确认复核才放行。
    /// </summary>
    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        if (!_reEvaluateOnAppear || _parameter == null) return;

        _reEvaluateOnAppear = false;
        await LoadAsync(_parameter);
    }

    /// <summary>
    /// 档案完整性状态：名册未建档 / 导入来源（ElderlySubsidyHistory、ElderlyReview）且缺必填项 → 需补全。
    /// 原生档案（source_type 为空）不提示也不拦截，避免存量老数据卡死；无需变更（HasChange=false）不要求补全（不产生档案与打印）。
    /// </summary>
    private async Task RefreshCompletionStateAsync(ElderlyReviewEvaluation eval)
    {
        IsCompletionNeeded = false;
        CompletionHint = string.Empty;

        if (!eval.HasChange) return;

        if (eval.IsHistoryOnly || eval.ApplicationId is not > 0)
        {
            IsCompletionNeeded = true;
            CompletionHint = "当前库未建档（数据仅在高龄导入名册中）";
            return;
        }

        var gaps = await _applicationService.GetReviewIncompleteFieldsAsync(eval.ApplicationId.Value, CancellationToken);
        if (gaps.IsFailure || gaps.Value == null)
        {
            // 检查失败不阻断复核页展示：确认复核时会再查一次并显式提示
            _logger.Warn($"复核-档案完整性检查失败: {gaps.ErrorCode} {gaps.Message}");
            return;
        }

        if (string.IsNullOrEmpty(gaps.Value.SourceType) || gaps.Value.MissingFields.Count == 0) return;
        IsCompletionNeeded = true;
        CompletionHint = $"尚缺：{string.Join("、", gaps.Value.MissingFields)}";
    }

    private void ApplyEvaluation(ElderlyReviewEvaluation eval)
    {
        ApplicantName = eval.Name;
        ApplicantIdCard = eval.IdCard;
        Gender = eval.Gender;
        AgeDisplay = $"{eval.Age} 周岁";
        ApplicationNo = eval.IsHistoryOnly ? "名册发放人员（未建档）" : eval.ApplicationNo;
        IsHistoryOnly = eval.IsHistoryOnly;

        OldCategoryName = eval.OldCategoryName;
        OldAmountText = $"{eval.OldMonthlyAmount:F2} 元/月";
        NewCategoryName = eval.NewCategoryName;
        NewAmountText = $"{eval.NewMonthlyAmount:F2} 元/月";

        HasChange = eval.HasChange;
        ChangeTypeText = eval.HasChange
            ? (eval.IsUpgrade ? "升档（标准提高）" : "降档（标准降低）")
            : "无需调整";
        IdentityFlag = eval.IdentityFlag;
        AdjustReasonName = eval.AdjustReasonCode switch
        {
            ElderlyBenefitConstants.AdjustReasonAge => "年龄档次变化",
            ElderlyBenefitConstants.AdjustReasonIdentity => "身份变动（低保/边缘/特困/低收入）",
            _ => "其他政策规定"
        };
        EffectiveMonthText = string.IsNullOrEmpty(eval.EffectiveMonth)
            ? "—"
            : FormatMonth(eval.EffectiveMonth);
        IssueStartMonthText = FormatMonth(eval.IssueStartMonth);
        OnPropertyChanged(nameof(CanConfirm));
    }

    /// <summary>
    /// 复核前置（硬拦截）：名册未建档 → 引导补建并补全；导入来源档案缺必填项 → 引导补全。
    /// 原生档案不拦（避免存量老数据卡死）；无需变更不拦（不产生档案与打印）。
    /// 返回 true = 已具备复核条件；false = 已导航去补全 / 用户取消 / 检查失败（勿继续复核）。
    /// </summary>
    private async Task<bool> EnsureReadyForReviewAsync()
    {
        if (_evaluation == null) return false;

        // ① 名册未建档：先补建（同事务标记名册行），再打开补全表单
        if (_evaluation.IsHistoryOnly || _evaluation.ApplicationId is not > 0)
        {
            var goBackfill = await _dialogService.DisplayAlertAsync("请先补全历史档案",
                $"[{ApplicantName}] 的数据仅在高龄导入名册中（当前库未建档）。\n" +
                "办理复核前需先补建档案并补全信息，补全保存后将自动返回本页继续复核。",
                "去补全", "取消");
            if (!goBackfill) return false;

            await OpenCompletionFormAsync();
            return false;
        }

        // ② 已建档：查必填项缺口（原生档案 SourceType 为空 → 不拦）
        var gaps = await _applicationService.GetReviewIncompleteFieldsAsync(_evaluation.ApplicationId.Value, CancellationToken);
        if (gaps.IsFailure || gaps.Value == null)
        {
            if (gaps.ErrorCode != ErrorCodes.CANCELLED)
                await _dialogService.DisplayAlertAsync("提示", gaps.Message ?? "档案完整性检查失败，无法继续复核", "确定");
            return false;
        }

        if (string.IsNullOrEmpty(gaps.Value.SourceType) || gaps.Value.MissingFields.Count == 0)
        {
            IsCompletionNeeded = false;
            CompletionHint = string.Empty;
            return true;
        }

        var goComplete = await _dialogService.DisplayAlertAsync("请先补全历史档案",
            $"[{ApplicantName}] 的档案来自高龄导入名册，尚缺：{string.Join("、", gaps.Value.MissingFields)}。\n" +
            "补全后才能办理复核（保证打印三表字段完整），补全保存后将自动返回本页。",
            "去补全", "取消");
        if (!goComplete) return false;

        await OpenCompletionFormAsync();
        return false;
    }

    /// <summary>补建（名册未建档时）并打开高龄申请表单补全；保存/返回本页时经 OnAppearing 重评。</summary>
    private async Task OpenCompletionFormAsync()
    {
        if (_evaluation == null) return;

        var appId = _evaluation.ApplicationId ?? 0;
        if (_evaluation.IsHistoryOnly || appId <= 0)
        {
            var ensure = await ExecuteAsync(
                () => _applicationService.EnsureReviewHistoryArchiveAsync(_evaluation!, App.CurrentUserName, CancellationToken),
                "补建档案中...");
            if (ensure.IsFailure || ensure.Value <= 0)
            {
                if (ensure.ErrorCode != ErrorCodes.CANCELLED)
                    await _dialogService.DisplayAlertAsync("建档失败", ensure.Message ?? "补建档案失败，请重试", "确定");
                return;
            }
            appId = ensure.Value;
        }

        _reEvaluateOnAppear = true;
        _logger.LogBusiness("高龄复核-打开历史档案补全",
            ("ApplicationId", appId),
            ("IdCard", DataMasker.MaskIdCard(_evaluation.IdCard)));

        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyApplicationFormPage, ElderlyFormPageParameter>(
            new ElderlyFormPageParameter(FormOperationMode.Edit, appId, ReturnToReview: true));
    }

    /// <summary>提示条按钮：补建/打开补全表单（与确认复核的拦截共用同一条路径）</summary>
    [RelayCommand]
    private async Task CompleteHistoryArchiveAsync()
    {
        if (_evaluation == null) return;
        await OpenCompletionFormAsync();
    }

    [RelayCommand]
    private async Task ConfirmReviewAsync()
    {
        if (_evaluation == null) return;

        // 硬拦截：导入来源档案未补全不得复核（先于确认弹窗，不补全不能继续）
        if (_evaluation.HasChange && !await EnsureReadyForReviewAsync()) return;

        var confirm = await _dialogService.DisplayAlertAsync("确认复核",
            _evaluation.HasChange
                ? $"确认将 {ApplicantName} 的享受类别由「{OldCategoryName}」调整为「{NewCategoryName}」？\n" +
                  $"旧档案将停发，新档案自 {IssueStartMonthText} 起按新标准计发（不补差）。"
                : $"确认 {ApplicantName} 经复核无需调整类别？",
            "确认", "取消");
        if (!confirm) return;

        await ExecuteAsync(async () =>
        {
            var result = await _applicationService.ConfirmReviewAsync(
                _reviewId, _applicationId, _idCard, _historyId,
                ReviewOpinion?.Trim() ?? string.Empty, App.CurrentUserName, CancellationToken);

            if (result.IsFailure)
            {
                await _dialogService.DisplayAlertAsync("复核失败", result.Message, "确定");
                return result;
            }

            if (!_evaluation.HasChange)
            {
                await _dialogService.ShowSnackBarAsync("复核完成：无需调整");
                await CloseAsync();
                return result;
            }

            _newAppId = result.Value;
            _logger.LogBusiness("高龄类别复核完成，进入档案输出",
                ("OldApplicationId", _oldAppId), ("NewApplicationId", _newAppId));

            await _dialogService.ShowSnackBarAsync("复核完成，已生成新档案");
            await CloseAsync();
            // [CT 豁免] 复核事务已 Commit：出档准备（取新旧档案构建打印字段）不可被取消，
            // 否则复核页消失/令牌作废会让用户误以为复核失败
            await NavigateToArchiveOutputAsync(CancellationToken.None);
            return result;
        }, "复核办理中...");
    }

    /// <summary>变更复核 → 档案输出页（停止 + 新增 + 变更 三表）</summary>
    private async Task NavigateToArchiveOutputAsync(CancellationToken ct)
    {
        var newAppResult = await _applicationService.GetByIdAsync(_newAppId, ct);
        if (!newAppResult.IsSuccess || newAppResult.Value == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "新档案已生成，但加载打印字段失败，请到档案管理补打", "确定");
            return;
        }
        var oldAppResult = _oldAppId > 0 ? await _applicationService.GetByIdAsync(_oldAppId, ct) : null;

        // 整档入口：先清文书模式上下文，防上一次「仅出文书」的静态残留被继承
        PrintNavigationData.ClearDocumentMode();
        PrintNavigationData.BusinessType = "ElderlyBenefits";
        PrintNavigationData.BusinessId = _newAppId;
        PrintNavigationData.Classification = "Review";
        PrintNavigationData.FieldData = BuildReviewPrintFields(newAppResult.Value, oldAppResult?.Value, _evaluation!);
        PrintNavigationData.TableData = new();
        PrintNavigationData.SupporterTableData = null;

        await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveOutputPage>();
    }

    /// <summary>合并三表字段：登记表用新档、取消备案表用旧档停发字段、调整备案表用调整专用字段</summary>
    private static Dictionary<string, string> BuildReviewPrintFields(
        ElderlyApplication newApp, ElderlyApplication? oldApp, ElderlyReviewEvaluation eval)
    {
        var f = ElderlyPrintDataBuilder.BuildSingleFields(newApp);

        // 取消备案表：受理日期与停发字段取复核/旧档
        var reviewDate = DateTime.Today.ToString("yyyy年M月d日");
        f[FieldKeys.ELDERLY_ACCEPT_DATE] = reviewDate;
        f[FieldKeys.ELDERLY_STOP_REASON] = ElderlyBenefitConstants.BuildStopReasonCheckText(ElderlyBenefitConstants.StopReasonOther);
        f[FieldKeys.ELDERLY_DUE_STOP_DATE] = FormatDate(oldApp?.DueStopDate ?? DateTime.Today);
        f[FieldKeys.ELDERLY_ACTUAL_STOP_DATE] = FormatDate(oldApp?.ActualStopDate ?? DateTime.Today);
        f[FieldKeys.ELDERLY_IS_RECOVER] = "否";
        f[FieldKeys.ELDERLY_RECOVER_RANGE] = string.Empty;
        f[FieldKeys.ELDERLY_RECOVER_AMOUNT] = "0.00元";
        f[FieldKeys.ELDERLY_REMARK] = $"类别调整，由{eval.OldCategoryName}调整为{eval.NewCategoryName}";

        // 调整备案表
        f[FieldKeys.ELDERLY_ADJUST_NAME] = newApp.Name;
        f[FieldKeys.ELDERLY_ADJUST_ID_CARD] = newApp.IdCard;
        f[FieldKeys.ELDERLY_ADJUST_HUKOU_ADDRESS] = newApp.HukouAddress ?? string.Empty;
        f[FieldKeys.ELDERLY_ADJUST_FAMILY_ADDRESS] = string.IsNullOrWhiteSpace(newApp.DetailAddress)
            ? (newApp.FamilyAddress ?? string.Empty)
            : $"{newApp.FamilyAddress}{newApp.DetailAddress}";
        f[FieldKeys.ELDERLY_ADJUST_REASON] = ElderlyBenefitConstants.BuildAdjustReasonCheckText(eval.AdjustReasonCode, eval.IdentityAdd);
        f[FieldKeys.ELDERLY_ADJUST_TIME] = $"{DateTime.Today.Year}年{DateTime.Today.Month}月";
        f[FieldKeys.ELDERLY_ADJUST_AMOUNT] = $"由{eval.OldMonthlyAmount:F2}元/月调整至{eval.NewMonthlyAmount:F2}元/月";
        f[FieldKeys.ELDERLY_ADJUST_PAYBACK_RANGE] = "—";
        f[FieldKeys.ELDERLY_ADJUST_PAYBACK_AMOUNT] = "—";
        f[FieldKeys.ELDERLY_ADJUST_ACCEPT_DATE] = reviewDate;

        return f;
    }

    [RelayCommand]
    private async Task CancelAsync() => await CloseAsync();

    private async Task CloseAsync()
    {
        if (Helpers.WindowNavigator.CurrentPage?.Navigation != null)
        {
            await Helpers.WindowNavigator.CurrentPage.Navigation.PopAsync();
            RestoreWindowTitleFromNavigation();
        }
    }

    private static string FormatDate(DateTime? dt) => dt?.ToString("yyyy年M月d日") ?? string.Empty;

    private static string FormatMonth(string month)
    {
        if (string.IsNullOrWhiteSpace(month)) return "—";
        if (DateTime.TryParseExact(month, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return $"{dt.Year}年{dt.Month}月";
        return month;
    }
}
