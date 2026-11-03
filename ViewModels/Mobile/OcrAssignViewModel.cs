using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Platform;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.ViewModels.Mobile;

/// <summary>
/// 识别结果分配页 ViewModel：每条识别记录一张卡片（字段可编辑 + 目标选择 + 警告徽标），
/// 确认后经 <see cref="IOcrAssignContext"/> 逐条回填来源表单（只填空字段）。
/// 交互定稿（2026-11-03）：名单外人物记录默认「新增赡养人」；冲突（同目标多条）弹「覆盖/改选」。
/// </summary>
public partial class OcrAssignViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;

    private IOcrAssignContext? _context;
    private Action<int>? _onCompleted;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    /// <summary>识别记录卡片（字段可编辑）</summary>
    public ObservableCollection<OcrRecognizedRecord> Records { get; } = new();

    /// <summary>摘要行（如「3 条记录 · 已预匹配 2 条」）</summary>
    [ObservableProperty] private string _summaryText = string.Empty;

    /// <summary>是否存在校验码警告（任一记录）</summary>
    [ObservableProperty] private bool _hasChecksumWarning;

    public OcrAssignViewModel(
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider)
    {
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        Title = "识别结果分配";
    }

    /// <summary>初始化（页面 SetParameterAsync 调用）</summary>
    public void Initialize(OcrAssignParameter parameter)
    {
        _context = parameter.Context;
        _onCompleted = parameter.OnCompleted;

        Records.Clear();
        var memberRows = _context.GetMemberRows();
        var suggested = 0;

        foreach (var result in parameter.Results)
        {
            var record = new OcrRecognizedRecord(result);

            // 目标选项固化到记录（含"不使用"）；无内容记录目标锁定不使用
            var options = new List<OcrAssignmentTarget>(_context.GetTargets(result.Category));
            options.Add(new OcrAssignmentTarget(OcrAssignmentPlanner.TargetKeys.Ignore, "不使用"));
            foreach (var opt in options)
                record.TargetOptions.Add(opt);

            if (!record.HasContent)
            {
                record.SelectedTarget = options[^1]; // 不使用
                Records.Add(record);
                continue;
            }

            // 身份证校验码核验（识别值；编辑后以最终值为准，Apply 时由表单再验）
            if (result.Category == PhotoCategoryConstants.ID_CARD)
            {
                var idCard = OcrAssignmentPlanner.GetTrimmed(result.Fields, OcrFieldKeys.IdCard);
                record.IdCardChecksumInvalid = idCard.Length > 0 && !IdCardValidator.IsValid(idCard);
            }

            // 预匹配：人物类记录走名单；表单类给固定目标；其余默认不使用
            var key = OcrAssignmentPlanner.SuggestTarget(result.Fields, memberRows)
                      ?? FixedTargetFor(result.Category);
            if (key is not null)
            {
                record.SelectedTarget = options.FirstOrDefault(o => o.Key == key) ?? options[^1];
                if (record.SelectedTargetKey != OcrAssignmentPlanner.TargetKeys.Ignore) suggested++;
            }
            else
            {
                record.SelectedTarget = options[^1];
            }

            Records.Add(record);
        }

        HasChecksumWarning = Records.Any(r => r.IdCardChecksumInvalid);
        var categoryName = Records.Count > 0
            ? PhotoCategoryConstants.GetDisplayName(Records[0].Category)
            : string.Empty;
        SummaryText = $"{Records.Count} 条「{categoryName}」记录 · 已预匹配 {suggested} 条，可逐条调整去向";

        _logger.LogBusiness("打开识别结果分配",
            ("Category", categoryName),
            ("RecordCount", Records.Count.ToString()),
            ("Suggested", suggested.ToString()));
    }

    /// <summary>表单类记录的固定目标（无人物名单参与）；返回 null = 默认不使用</summary>
    private static string? FixedTargetFor(string category) => category switch
    {
        PhotoCategoryConstants.BANK_CARD => OcrAssignmentPlanner.TargetKeys.FormBank,
        PhotoCategoryConstants.HOUSEHOLD_REGISTER => OcrAssignmentPlanner.TargetKeys.FormHukou,
        PhotoCategoryConstants.MEDICAL_RECORD => OcrAssignmentPlanner.TargetKeys.FormReason,
        _ => null // INCOME_PROOF（§9 金额不落库）/ 公示 / 其他：默认不使用
    };

    /// <summary>全部标记不使用（跳过回填）</summary>
    [RelayCommand]
    private async Task IgnoreAllAsync()
    {
        foreach (var r in Records)
            r.SelectedTarget = r.TargetOptions.FirstOrDefault(o => o.Key == OcrAssignmentPlanner.TargetKeys.Ignore);
        await ShowSnackBarAsync("已全部标记为不使用", Components.SnackBarType.Info);
    }

    /// <summary>确认回填：冲突检测 → 逐条应用 → 回传条数</summary>
    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (_context is null || Records.Count == 0)
        {
            await CompleteAsync(0);
            return;
        }

        // 冲突检测：同目标多条 → 弹「覆盖/改选」
        var conflicts = OcrAssignmentPlanner.FindConflicts(Records.Select(r => r.SelectedTargetKey));
        if (conflicts.Count > 0)
        {
            var first = conflicts[0];
            var overwrite = await _dialogService.DisplayAlertAsync("目标冲突",
                $"多条记录都选择了同一个去向（{TargetDisplayName(first)}）。\n" +
                "[覆盖] 同去向保留最后一条，其余改为不使用；[改选] 返回逐条调整。",
                "覆盖", "改选");
            if (!overwrite)
                return;

            // 覆盖语义：同目标保留最后一条，先前的改为不使用
            foreach (var key in conflicts)
            {
                var same = Records.Where(r => r.SelectedTargetKey == key).ToList();
                for (var i = 0; i < same.Count - 1; i++)
                {
                    same[i].SelectedTarget = same[i].TargetOptions
                        .FirstOrDefault(o => o.Key == OcrAssignmentPlanner.TargetKeys.Ignore);
                }
            }
        }

        var applied = 0;
        foreach (var record in Records)
        {
            if (record.SelectedTarget is null || record.SelectedTargetKey == OcrAssignmentPlanner.TargetKeys.Ignore)
                continue;

            try
            {
                if (await _context.ApplyAsync(record, record.SelectedTarget))
                    applied++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"识别结果回填失败: {record.Category}");
            }
        }

        _logger.LogBusiness("识别结果回填完成",
            ("RecordCount", Records.Count.ToString()),
            ("Applied", applied.ToString()));

        await CompleteAsync(applied);
    }

    private string TargetDisplayName(string key)
        => Records.SelectMany(r => r.TargetOptions).FirstOrDefault(t => t.Key == key)?.DisplayName ?? key;

    /// <summary>完成：先弹出分配页自身，再回调来源页（顺序反了会让来源页的 Pop 弹错页面）</summary>
    private async Task CompleteAsync(int applied)
    {
        var callback = _onCompleted;
        _onCompleted = null;
        await GoBackAsync();
        callback?.Invoke(applied);
    }
}
