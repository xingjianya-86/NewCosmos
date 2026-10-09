using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.Recovery;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.ViewModels.Recovery;

/// <summary>
/// 手工录入追缴信息页面ViewModel
/// </summary>
public partial class RecoveryManualViewModel : ViewModelBase
{
    private readonly IRecoveryService _recoveryService;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerService _logger;

    /// <summary>
    /// 人员姓名
    /// </summary>
    [ObservableProperty]
    private string _personName = string.Empty;

    /// <summary>
    /// 身份证号
    /// </summary>
    [ObservableProperty]
    private string _idCard = string.Empty;

    /// <summary>
    /// 追缴分类选中项（下拉选择，KeyValuePair 保证类型匹配）
    /// </summary>
    [ObservableProperty]
    private KeyValuePair<string, string>? _selectedSourceType;

    /// <summary>
    /// 编辑模式下的记录ID（新建为 null）
    /// </summary>
    [ObservableProperty]
    private long? _recordId;

    /// <summary>
    /// 追缴分类显示名称
    /// </summary>
    [ObservableProperty]
    private string _sourceTypeDisplay = string.Empty;

    /// <summary>
    /// 追缴分类列表
    /// </summary>
    [ObservableProperty]
    private List<KeyValuePair<string, string>> _sourceTypeList = new();

    /// <summary>
    /// 追缴理由
    /// </summary>
    [ObservableProperty]
    private string _recoveryReason = string.Empty;

    /// <summary>
    /// 月保障额
    /// </summary>
    [ObservableProperty]
    private decimal _monthlyAmount;

    /// <summary>
    /// 追缴起始月
    /// </summary>
    [ObservableProperty]
    private string _startMonth = string.Empty;

    /// <summary>
    /// 追缴终止月
    /// </summary>
    [ObservableProperty]
    private string _endMonth = string.Empty;

    /// <summary>
    /// 追缴月数（自动计算）
    /// </summary>
    [ObservableProperty]
    private int _recoveryMonths;

    /// <summary>
    /// 应追缴金额（自动计算）
    /// </summary>
    [ObservableProperty]
    private decimal _recoveryAmount;

    /// <summary>
    /// 已追缴金额
    /// </summary>
    [ObservableProperty]
    private decimal _recoveredAmount;

    /// <summary>
    /// 年份列表
    /// </summary>
    [ObservableProperty]
    private List<int> _yearList = new();

    /// <summary>
    /// 月份列表
    /// </summary>
    [ObservableProperty]
    private List<int> _monthList = new();

    /// <summary>
    /// 起始年份
    /// </summary>
    [ObservableProperty]
    private int _startYear;

    /// <summary>
    /// 起始月份
    /// </summary>
    [ObservableProperty]
    private int _startMonthValue;

    /// <summary>
    /// 终止年份
    /// </summary>
    [ObservableProperty]
    private int _endYear;

    /// <summary>
    /// 终止月份
    /// </summary>
    [ObservableProperty]
    private int _endMonthValue;

    public RecoveryManualViewModel(
        IServiceProvider serviceProvider,
        ILoggerService logger,
        IRecoveryService recoveryService,
        Services.Domain.Printing.IPrintJobFactory printJobFactory)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _recoveryService = recoveryService;
        _printJobFactory = printJobFactory;

        // 初始化追缴分类列表
        SourceTypeList = RecoveryConstants.GetAllSourceTypes();

        // 初始化年份和月份列表
        var now = DateTime.Today;
        YearList = Enumerable.Range(2020, now.Year - 2020 + 2).ToList();
        MonthList = Enumerable.Range(1, 12).ToList();

        // 设置默认值
        StartYear = now.Year;
        StartMonthValue = now.Month;
        EndYear = now.Year;
        EndMonthValue = now.Month;

        // 设置默认起始月
        StartMonth = $"{now.Year:D4}-{now.Month:D2}";
        EndMonth = $"{now.Year:D4}-{now.Month:D2}";
    }

    private readonly Services.Domain.Printing.IPrintJobFactory _printJobFactory = null!;

    /// <summary>
    /// 起始年份变化
    /// </summary>
    partial void OnStartYearChanged(int value)
    {
        UpdateStartMonth();
    }

    /// <summary>
    /// 起始月份变化
    /// </summary>
    partial void OnStartMonthValueChanged(int value)
    {
        UpdateStartMonth();
    }

    /// <summary>
    /// 终止年份变化
    /// </summary>
    partial void OnEndYearChanged(int value)
    {
        UpdateEndMonth();
    }

    /// <summary>
    /// 终止月份变化
    /// </summary>
    partial void OnEndMonthValueChanged(int value)
    {
        UpdateEndMonth();
    }

    /// <summary>
    /// 人员姓名变化（刷新保存命令可用状态）
    /// </summary>
    partial void OnPersonNameChanged(string value)
    {
        NotifyAllCommandsCanExecuteChanged();
    }

    /// <summary>
    /// 月保障额变化
    /// </summary>
    partial void OnMonthlyAmountChanged(decimal value)
    {
        CalculateRecovery();
        NotifyAllCommandsCanExecuteChanged();
    }

    /// <summary>
    /// 追缴分类变化
    /// </summary>
    partial void OnSelectedSourceTypeChanged(KeyValuePair<string, string>? value)
    {
        SourceTypeDisplay = value.HasValue
            ? RecoveryConstants.GetSourceTypeDisplayName(value.Value.Key)
            : string.Empty;
        NotifyAllCommandsCanExecuteChanged();
    }

    private void UpdateStartMonth()
    {
        StartMonth = $"{StartYear:D4}-{StartMonthValue:D2}";
        CalculateRecovery();
    }

    private void UpdateEndMonth()
    {
        EndMonth = $"{EndYear:D4}-{EndMonthValue:D2}";
        CalculateRecovery();
    }

    private void CalculateRecovery()
    {
        RecoveryMonths = _recoveryService.CalculateRecoveryMonths(StartMonth, EndMonth);
        RecoveryAmount = _recoveryService.CalculateRecoveryAmount(MonthlyAmount, RecoveryMonths);
    }

    /// <summary>
    /// 回填草稿数据用于编辑
    /// </summary>
    public void LoadRecord(RecoveryRecord record)
    {
        if (record == null) return;

        RecordId = record.Id;
        PersonName = record.PersonName;
        IdCard = record.IdCard ?? string.Empty;

        if (record.SourceType != null)
        {
            SelectedSourceType = SourceTypeList.FirstOrDefault(kv => kv.Key == record.SourceType);
        }

        RecoveryReason = record.RecoveryReason ?? string.Empty;
        MonthlyAmount = record.MonthlyAmount;
        RecoveredAmount = record.RecoveredAmount;

        if (DateTime.TryParseExact(record.StartMonth ?? string.Empty, "yyyy-MM",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var start))
        {
            StartYear = start.Year;
            StartMonthValue = start.Month;
        }

        if (DateTime.TryParseExact(record.EndMonth ?? string.Empty, "yyyy-MM",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var end))
        {
            EndYear = end.Year;
            EndMonthValue = end.Month;
        }

        CalculateRecovery();
    }

    /// <summary>
    /// 保存草稿命令（宽松校验，必填项不全也可保存）
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveDraft))]
    private async Task SaveDraftAsync()
    {
        var result = await SaveDraftInternalAsync();
        if (result.IsSuccess)
        {
            await GoBackAsync();
        }
    }

    private bool CanSaveDraft() => !IsBusy;

    /// <summary>
    /// 保存并打印命令：保存后跳转档案输出页生成《追缴资金办理单》
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAndPrintAsync()
    {
        var result = await SaveInternalAsync();
        if (result.IsSuccess)
        {
            await NavigateToPrintAsync(result.Value);
        }
        else
        {
            await ShowFailureAsync(result, "保存并打印");
        }
    }

    private bool CanSave() => !IsBusy
        && !string.IsNullOrWhiteSpace(PersonName)
        && SelectedSourceType.HasValue
        && MonthlyAmount > 0;

    /// <summary>
    /// 保存并推送打印：保存后写入推送打印队列（PC 端打印代理生成《追缴资金办理单》）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAndPushPrintAsync()
    {
        var result = await SaveInternalAsync();
        if (result.IsFailure)
        {
            await ShowFailureAsync(result, "保存并推送打印");
            return;
        }

        var fields = await BuildPrintFieldsAsync();
        var request = new PrintJobRequest
        {
            BusinessType = "Recovery",
            BusinessId = result.Value,
            Classification = RecoveryConstants.INPUT_MODE_MANUAL,
            ApplicantName = PersonName,
            ApplicantIdCard = IdCard,
            Fields = fields,
            TableRows = new()
        };

        var push = await _printJobFactory.EnqueueAsync(request);
        if (push.IsFailure)
        {
            ErrorMessage = push.Message ?? "推送打印失败";
            return;
        }

        var dialog = ServiceProvider.GetRequiredService<IDialogService>();
        await dialog.DisplayAlertAsync("已推送打印",
            $"打印任务 {push.Value?.JobNo} 已推送，将在电脑端自动打印。", "确定");
        await GoBackAsync();
    }

    /// <summary>
    /// 保存草稿（宽松校验：字段缺失时保存为空值，不阻断）
    /// </summary>
    private async Task<Result<long>> SaveDraftInternalAsync()
    {
        try
        {
            IsBusy = true;
            LoadingMessage = "保存草稿中...";

            var dto = new ManualRecoveryRecordDto
            {
                PersonName = PersonName,
                IdCard = IdCard,
                SourceType = SelectedSourceType.HasValue ? SelectedSourceType.Value.Key : string.Empty,
                RecoveryReason = RecoveryReason,
                MonthlyAmount = MonthlyAmount,
                StartMonth = StartMonth,
                EndMonth = EndMonth,
                RecoveredAmount = RecoveredAmount
            };

            Result<long> result;
            if (RecordId.HasValue)
            {
                var upd = await _recoveryService.UpdateManualRecoveryRecordAsync(RecordId.Value, dto);
                result = upd.IsSuccess
                    ? Result.Success(RecordId.Value)
                    : Result.Failure<long>(upd.ErrorCode!, upd.Message ?? "保存失败");
            }
            else
            {
                result = await _recoveryService.SaveManualRecoveryRecordAsync(dto);
            }

            if (result.IsFailure)
            {
                ErrorMessage = result.Message ?? "保存失败";
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "保存手工追缴草稿异常");
            ErrorMessage = "保存异常，请稍后重试";
            return Result.FromException<long>(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 校验并保存追缴记录，返回记录ID
    /// </summary>
    private async Task<Result<long>> SaveInternalAsync()
    {
        if (string.IsNullOrWhiteSpace(PersonName))
        {
            ErrorMessage = "请输入追缴人员姓名";
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "请输入追缴人员姓名");
        }

        if (!SelectedSourceType.HasValue)
        {
            ErrorMessage = "请选择追缴分类";
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "请选择追缴分类");
        }

        if (MonthlyAmount <= 0)
        {
            ErrorMessage = "月保障额必须大于0";
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "月保障额必须大于0");
        }

        if (RecoveryMonths <= 0)
        {
            ErrorMessage = "追缴月数必须大于0";
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "追缴月数必须大于0");
        }

        if (RecoveryAmount <= 0)
        {
            ErrorMessage = "应追缴金额必须大于0";
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "应追缴金额必须大于0");
        }

        try
        {
            IsBusy = true;
            LoadingMessage = "保存追缴记录中...";

            var dto = new ManualRecoveryRecordDto
            {
                PersonName = PersonName,
                IdCard = IdCard,
                SourceType = SelectedSourceType.Value.Key,
                RecoveryReason = RecoveryReason,
                MonthlyAmount = MonthlyAmount,
                StartMonth = StartMonth,
                EndMonth = EndMonth,
                RecoveredAmount = RecoveredAmount
            };

            Result<long> result;
            if (RecordId.HasValue)
            {
                var upd = await _recoveryService.UpdateManualRecoveryRecordAsync(RecordId.Value, dto);
                result = upd.IsSuccess
                    ? Result.Success(RecordId.Value)
                    : Result.Failure<long>(upd.ErrorCode!, upd.Message ?? "保存失败");
            }
            else
            {
                result = await _recoveryService.SaveManualRecoveryRecordAsync(dto);
            }

            if (result.IsFailure)
            {
                ErrorMessage = result.Message ?? "保存失败";
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "保存手工追缴记录异常");
            ErrorMessage = "保存异常，请稍后重试";
            return Result.FromException<long>(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 保存成功后跳转档案输出页（模板：档案_追缴资金办理单）
    /// </summary>
    private async Task NavigateToPrintAsync(long recordId)
    {
        // 整档入口：先清文书模式上下文，防上一次「仅出文书」的静态残留被继承
        PrintNavigationData.ClearDocumentMode();
        PrintNavigationData.BusinessType = "Recovery";
        PrintNavigationData.BusinessId = recordId;
        PrintNavigationData.Classification = RecoveryConstants.INPUT_MODE_MANUAL;
        PrintNavigationData.FieldData = await BuildPrintFieldsAsync();
        PrintNavigationData.TableData = new();
        PrintNavigationData.SupporterTableData = null;
        PrintNavigationData.NearRelativePairs = null;

        await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveOutputPage>();
    }

    /// <summary>
    /// 填报单位默认值：当前登录用户所属组织名称（单位全称，取不到时回退登录用户名）
    /// </summary>
    private async Task<string> GetReportUnitAsync()
    {
        var orgId = App.CurrentUserOrganizationId;
        if (!orgId.HasValue)
            return string.IsNullOrWhiteSpace(App.CurrentUserName) ? string.Empty : App.CurrentUserName;

        try
        {
            var orgService = ServiceProvider.GetRequiredService<NewCosmos.Services.Domain.UserManagement.IOrganizationService>();
            var result = await orgService.GetByIdAsync(orgId.Value, CancellationToken);
            if (result.IsSuccess && result.Value != null)
            {
                var org = result.Value;
                if (!string.IsNullOrWhiteSpace(org.Name)) return org.Name;
                if (!string.IsNullOrWhiteSpace(org.TownName)) return org.TownName;
                if (!string.IsNullOrWhiteSpace(org.CountyName)) return org.CountyName;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"获取填报单位默认值失败: {ex.Message}");
        }

        return string.IsNullOrWhiteSpace(App.CurrentUserName) ? string.Empty : App.CurrentUserName;
    }

    /// <summary>
    /// 构建《追缴资金办理单》打印字段（key 与模板中文占位符对应）
    /// </summary>
    private async Task<Dictionary<string, string>> BuildPrintFieldsAsync()
    {
        var now = DateTime.Now;
        var reportUnit = await GetReportUnitAsync();
        var codeResult = await _recoveryService.GenerateRecoveryCodeAsync(now);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FieldKeys.RECOVERY_UNIT] = reportUnit,
            [FieldKeys.RECOVERY_CODE] = codeResult.IsSuccess ? codeResult.Value : string.Empty,
            [FieldKeys.RECOVERY_SEQ] = "1",
            [FieldKeys.RECOVERY_PERSON_NAME] = PersonName,
            [FieldKeys.RECOVERY_ID_CARD] = IdCard,
            [FieldKeys.RECOVERY_SOURCE_TYPE] = SourceTypeDisplay,
            [FieldKeys.RECOVERY_AMOUNT] = RecoveryAmount.ToString("N2"),
            [FieldKeys.RECOVERED_AMOUNT] = RecoveredAmount.ToString("N2"),
            [FieldKeys.RECOVERY_REPORT_DATE] = now.ToString("yyyy年MM月dd日"),
            [FieldKeys.RECOVERY_RANGE] = $"{StartMonth} 至 {EndMonth}"
        };
    }
}
