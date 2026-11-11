using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.DeathStatement;
using NewCosmos.Models.Entities;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Pages.ChangeManagement;
using NewCosmos.Pages.ElderlyBenefits;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.Printing;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.Platform;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;


namespace NewCosmos.ViewModels.ArchiveManagement;

/// <summary>
/// 模板 / 打印机 / 字段装配 / 预览 / 打印 / 保存 / 推送 / 三联。
/// 2026-11 死亡说明简化·阶段3a：从 DeathStatementViewModel.cs 纯移动拆出（零行为变更）。
/// </summary>
public partial class DeathStatementViewModel
{

    #region 模板与打印

    private Template? _statementTemplate;

    [ObservableProperty]
    private string _selectedTemplateName = string.Empty;

    [ObservableProperty]
    private bool _hasTemplate;

    [ObservableProperty]
    private string _noTemplateHint = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _printerNames = new();

    [ObservableProperty]
    private string? _selectedPrinter;

    [ObservableProperty]
    private int _copies = 1;

    partial void OnCopiesChanged(int value) => Copies = Math.Clamp(value, 1, 99);

    [RelayCommand]
    private void IncreaseCopies() => Copies = Math.Min(99, Copies + 1);

    [RelayCommand]
    private void DecreaseCopies() => Copies = Math.Max(1, Copies - 1);

    [ObservableProperty]
    private bool _isDuplex;

    [ObservableProperty]
    private string _pdfFilePath = string.Empty;

    [ObservableProperty]
    private bool _hasPdf;

    [ObservableProperty]
    private bool _isPreviewBusy;

    [ObservableProperty]
    private bool _isPrinting;

    /// <summary>打印/保存权限（PRINT_LOWINCOMEPROOF，手机端推送按钮门控）</summary>
    [ObservableProperty]
    private bool _canPrint;

    public bool IsOverallBusy => IsBusy || IsPreviewBusy || IsPrinting;

    protected override void OnBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsOverallBusy));
        OnPropertyChanged(nameof(CanArchive)); // 完结归档按钮随忙碌态禁用
    }

    #endregion

    #region 文书输出（预览 / 打印 / 保存 / 推送）

    private bool EnsureCanProceed()
    {
        if (!ScopeReady)
        {
            _ = ShowTipAsync(ScopeError);
            return false;
        }

        // 文书输出门禁：台账先行，文书后出（2026-10-29 设计确认）
        if (!IsDeclared)
        {
            _ = ShowTipAsync("请先提交申报，申报成功后方可输出文书");
            return false;
        }
        if (!HasSelected && !IsManualMode)
        {
            _ = ShowTipAsync("请先选择死亡人员，或点「手工录入」");
            return false;
        }
        if (IsManualMode && string.IsNullOrWhiteSpace(Name))
        {
            _ = ShowTipAsync("请先填写死者姓名与身份证");
            return false;
        }
        if (_statementTemplate == null)
        {
            _ = ShowTipAsync(NoTemplateHint);
            return false;
        }
        return true;
    }

    /// <summary>
    /// 输出前置：基础校验 + 手工录入模式下自动汇总一次上下文
    /// （保证文书带身份清单与数据提醒，而不是空模板）。
    /// </summary>
    private async Task<bool> PrepareOutputAsync()
    {
        if (!EnsureCanProceed()) return false;

        if (IsManualMode && _context == null)
        {
            await BuildManualContextAsync();
            if (_context == null)
            {
                await ShowTipAsync("请先填写死者姓名与身份证号，并点「汇总身份与提醒」");
                return false;
            }
        }

        return _context != null || IsManualMode;
    }

    /// <summary>
    /// 文书字段单点装配（2026-11 简化·3b：逻辑已下沉 <see cref="DeathStatementFieldAssembler"/>，
    /// 此处只采集表单值组装 DTO；「表单值优先、context 兜底」等行为语义见装配器类注释）。
    /// pushUnit：三联各份「推送单位」不同，由调用方按联次注入；null → 取 <see cref="CurrentPushUnit"/>（预览默认联次）。
    /// </summary>
    private Dictionary<string, string> BuildStatementFields(string? pushUnit = null) =>
        DeathStatementFieldAssembler.BuildStatementFields(BuildFieldInput(pushUnit));

    /// <summary>采集表单当前值 → 装配 DTO（BuildStatementFields / BuildManualContext 共用）</summary>
    private DeathStatementFieldInput BuildFieldInput(string? pushUnit = null) => new()
    {
        Name = Name,
        IdCard = IdCard,
        DeathDateTime = DeathDateTime,
        DeathReason = DeathReason,
        DeathCertNo = DeathCertNo,
        DeathPlace = DeathPlace,
        Relation = Relation,
        DeclaredRelation = DeclaredRelation,
        DeclaredVillage = DeclaredVillage,
        DeathGender = DeathGender,
        DeathEthnicity = DeathEthnicity,
        ReporterName = ReporterName,
        ReporterRelation = ReporterRelation,
        ReporterPhone = ReporterPhone,
        SituationDesc = SituationDesc,
        RemainsArrangement = RemainsArrangement,
        BusinessAgent = BusinessAgent,
        BusinessMatter = BusinessMatter,
        DeclaredByName = DeclaredByName,
        DeclaredByIdCard = DeclaredByIdCard,
        DeclaredByPhone = DeclaredByPhone,
        DeclaredByType = DeclaredByType,
        DeclaredAtText = DeclaredAtText,
        CertType = SelectedCertType,
        CertIssueOrg = CertIssueOrg,
        DispositionBranch = SelectedDisposition,
        CremationDate = CremationDate,
        CremationPlace = CremationPlace,
        CremationCertNo = CremationCertNo,
        BurialDate = BurialDate,
        BurialPlace = BurialPlace,
        FamilySituation = FamilySituation,
        DisputeStatus = SelectedDispute,
        DisputeDesc = DisputeDesc,
        Context = _context,
        Scope = _scope,
        PushUnit = pushUnit,
        DefaultPushUnit = CurrentPushUnit(),
        OperatorName = string.IsNullOrEmpty(App.CurrentUserFullName) ? App.CurrentUserName : App.CurrentUserFullName
    };

    /// <summary>
    /// 手工录入未提交时的上下文（仅表单值，身份/提醒/联动走服务聚合）。
    /// 未选择死亡时刻时回退当前时刻——仅影响文书预览聚合，落库必经 DeclareAsync 的真实必填校验。
    /// </summary>
    private DeathStatementContext BuildManualContext() =>
        DeathStatementFieldAssembler.BuildManualContext(BuildFieldInput());

    /// <summary>手工录入模式下按表单重新聚合（不写库）</summary>
    [RelayCommand]
    private async Task BuildManualContextAsync()
    {
        if (!EnsureScopeReady()) return;
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(IdCard))
        {
            await ShowTipAsync("请先填写死者姓名与身份证号");
            return;
        }

        await LoadContextAsync(new DeathSearchItem
        {
            Name = Name.Trim(),
            IdCard = IdCard.Trim(),
            // 未选择死亡时刻时回退当前时刻——仅影响文书预览聚合，落库必经 DeclareAsync 的真实必填校验
            DeathDateText = (DeathDateTime ?? DateTime.Now).ToString("yyyy-MM-dd"),
            DeathTimeText = (DeathDateTime ?? DateTime.Now).ToString(@"hh\:mm"),
            DeathReason = DeathReason ?? string.Empty,
            DeathCertNo = DeathCertNo ?? string.Empty,
            Relation = Relation ?? string.Empty,
            DeclaredByName = DeclaredByName ?? string.Empty,
            DeclaredByType = DeclaredByType ?? string.Empty,
            DeclaredAtText = DeclaredAtText,
            HasDeclaration = IsDeclared
        });
        // 死亡地点不在 DeathSearchItem 快照里，聚合完成后由表单回填（文书 DEATH_PLACE 用）
        if (_context != null)
            _context.DeathPlace = DeathPlace ?? string.Empty;
        IsManualMode = true;
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (!await PrepareOutputAsync()) return;

        IsPreviewBusy = true;
        try
        {
            var fields = BuildStatementFields();
            var result = await _printExecuteService.GeneratePreviewPdfAsync(
                _statementTemplate!.Id, fields, null, CancellationToken);
            if (result.IsFailure)
            {
                await ShowTipAsync($"文书生成失败: {result.Message}");
                return;
            }

            var tempPath = Path.Combine(OutputPathHelper.GetTempDirectory(),
                $"nc_death_stmt_{DateTime.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.pdf");
            await File.WriteAllBytesAsync(tempPath, result.Value, CancellationToken);
            PdfFilePath = tempPath;
            HasPdf = true;

            _logger.LogBusiness("死亡情况说明-预览",
                ("TemplateId", _statementTemplate!.Id.ToString()),
                ("Name", DataMasker.MaskName(Name ?? string.Empty)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "死亡情况说明-预览失败");
            await ShowTipAsync($"预览失败: {ex.Message}");
        }
        finally
        {
            IsPreviewBusy = false;
        }
    }

    [RelayCommand]
    private Task PrintAsync() => ExecuteStatementActionAsync(printToPrinter: true);

    [RelayCommand]
    private Task SaveAsync() => ExecuteStatementActionAsync(printToPrinter: false);

    /// <summary>预览默认展示的联次推送单位（优先镇政府联，其次留存、卫生院）</summary>
    private string CurrentPushUnit()
    {
        if (PrintGov && !string.IsNullOrWhiteSpace(PushUnitGov)) return PushUnitGov.Trim();
        if (PrintRetain && !string.IsNullOrWhiteSpace(PushUnitRetain)) return PushUnitRetain.Trim();
        if (PrintClinic && !string.IsNullOrWhiteSpace(PushUnitClinic)) return PushUnitClinic.Trim();
        return PushUnitGov?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// 三联出具：一份村里留存、一份交镇政府相关部门、一份给镇中心卫生院（用于申报申请死亡证明）。
    /// 三份「推送单位」不同 → <b>逐份生成，不能用份数复制</b>；每份独立批次号，
    /// 落盘文件由 OutputPathHelper 时间戳区分（同目录三个文件）。
    /// </summary>
    [RelayCommand]
    private async Task PrintTripleAsync()
    {
        if (!await PrepareOutputAsync()) return;

        var units = new List<(string Label, string Unit)>();
        if (PrintRetain && !string.IsNullOrWhiteSpace(PushUnitRetain)) units.Add(("村留存", PushUnitRetain.Trim()));
        if (PrintGov && !string.IsNullOrWhiteSpace(PushUnitGov)) units.Add(("镇政府", PushUnitGov.Trim()));
        if (PrintClinic && !string.IsNullOrWhiteSpace(PushUnitClinic)) units.Add(("卫生院", PushUnitClinic.Trim()));

        if (units.Count == 0)
        {
            await ShowTipAsync("请至少勾选一联（村留存 / 镇政府 / 卫生院）");
            return;
        }

        // 记住三联设置：同镇后续文书不必重填
        Preferences.Default.Set(PrefPushRetain, PushUnitRetain ?? string.Empty);
        Preferences.Default.Set(PrefPushGov, PushUnitGov ?? string.Empty);
        Preferences.Default.Set(PrefPushClinic, PushUnitClinic ?? string.Empty);

        // 只勾一联时等同单份打印
        if (units.Count == 1)
        {
            await ExecuteStatementActionAsync(printToPrinter: true, pushUnit: units[0].Unit);
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync("三联出具",
            $"将输出 {units.Count} 份《死亡情况核实说明》，推送单位分别为：\n"
            + string.Join("\n", units.Select(u => $"  · {u.Label}：{u.Unit}"))
            + "\n\n确认打印？", "打印", "取消");
        if (!confirm) return;

        IsPrinting = true;
        var ok = 0;
        try
        {
            foreach (var (label, unit) in units)
            {
                var result = await PrintOneAsync(Guid.NewGuid().ToString("N").ToUpper(), unit, printToPrinter: true, CancellationToken);
                if (result.IsSuccess)
                {
                    ok++;
                    _logger.LogBusiness("死亡情况说明-三联出件", ("Label", label), ("PushUnit", unit));
                }
                else
                {
                    await ShowTipAsync($"「{label}」出件失败: {result.Message}");
                }
            }
        }
        finally
        {
            IsPrinting = false;
        }

        await ShowTipAsync($"三联出具完成：{ok}/{units.Count} 份（各自独立批次号，可在打印留痕按模板名查询）");
    }

    private async Task ExecuteStatementActionAsync(bool printToPrinter, string? pushUnit = null)
    {
        if (!await PrepareOutputAsync()) return;

        var action = printToPrinter ? "打印" : "保存";
        IsPrinting = true;
        try
        {
            var result = await PrintOneAsync(Guid.NewGuid().ToString("N").ToUpper(), pushUnit, printToPrinter, CancellationToken);
            if (result.IsFailure)
            {
                await ShowTipAsync($"{action}失败: {result.Message}");
                return;
            }

            await ShowTipAsync(string.IsNullOrWhiteSpace(pushUnit)
                ? $"死亡情况说明{action}完成（{Name}）"
                : $"死亡情况说明{action}完成（{Name}），推送单位：{pushUnit}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"死亡情况说明{action}失败");
            await ShowTipAsync($"{action}失败: {ex.Message}");
        }
        finally
        {
            IsPrinting = false;
        }
    }

    /// <summary>单份出件：fields 按联次注入推送单位（调用方负责 IsPrinting 与前置校验）</summary>
    private async Task<Result> PrintOneAsync(string batchNo, string? pushUnit, bool printToPrinter, CancellationToken ct)
    {
        var fields = BuildStatementFields(pushUnit);
        return await _printExecuteService.ExecutePrintAsync(
            _statementTemplate!.Id,
            _statementTemplate.Name,
            StatementBusinessType,
            _context?.DeclarationId ?? _context?.DeathRecordId,
            batchNo,
            fields,
            null,
            Name ?? string.Empty,
            IdCard ?? string.Empty,
            printToPrinter ? SelectedPrinter ?? string.Empty : null!,
            Math.Max(1, Copies),
            IsDuplex,
            printToPrinter: printToPrinter,
            ct);
    }

    /// <summary>手机端推送打印（由 PC 打印代理执行）</summary>
    [RelayCommand]
    private async Task PushPrintAsync()
    {
        if (!await PrepareOutputAsync()) return;

        if (!CanPrint)
        {
            await ShowTipAsync("您没有打印证明的权限（PRINT_LOWINCOMEPROOF），请联系管理员");
            return;
        }

        await ExecuteAsync(async ct =>
        {
            var fields = BuildStatementFields();
            var request = new PrintJobRequest
            {
                BusinessType = StatementBusinessType,
                BusinessId = _context?.DeclarationId ?? _context?.DeathRecordId,
                Classification = StatementBusinessType,
                TemplateId = _statementTemplate!.Id,
                TemplateName = _statementTemplate.Name,
                ApplicantName = Name ?? string.Empty,
                ApplicantIdCard = IdCard ?? string.Empty,
                PrinterName = null,
                Copies = Math.Max(1, Copies),
                IsDuplex = IsDuplex,
                Fields = fields,
                TableRows = new()
            };

            var result = await _printJobFactory.EnqueueAsync(request, ct);
            if (result.IsFailure)
            {
                await ShowTipAsync($"推送打印失败: {result.Message}");
                return;
            }

            _logger.LogBusiness("死亡情况说明-推送打印",
                ("TemplateId", _statementTemplate!.Id.ToString()),
                ("JobNo", result.Value?.JobNo ?? string.Empty));
            await ShowTipAsync($"已推送打印（任务号 {result.Value?.JobNo}），将在电脑端打印。");
        }, "推送打印中…");
    }

    /// <summary>
    /// 手机端三联推送：逐份入队，由电脑端打印代理逐份执行（手机端不生成本地 PDF）。
    /// 三联「推送单位」不同 → 逐份 Enqueue，各自独立任务号。
    /// </summary>
    [RelayCommand]
    private async Task PushPrintTripleAsync()
    {
        if (!await PrepareOutputAsync()) return;

        if (!CanPrint)
        {
            await ShowTipAsync("您没有打印证明的权限（PRINT_LOWINCOMEPROOF），请联系管理员");
            return;
        }

        var units = new List<(string Label, string Unit)>();
        if (PrintRetain && !string.IsNullOrWhiteSpace(PushUnitRetain)) units.Add(("村留存", PushUnitRetain.Trim()));
        if (PrintGov && !string.IsNullOrWhiteSpace(PushUnitGov)) units.Add(("镇政府", PushUnitGov.Trim()));
        if (PrintClinic && !string.IsNullOrWhiteSpace(PushUnitClinic)) units.Add(("卫生院", PushUnitClinic.Trim()));

        if (units.Count == 0)
        {
            await ShowTipAsync("请至少勾选一联（村留存 / 镇政府 / 卫生院）");
            return;
        }

        Preferences.Default.Set(PrefPushRetain, PushUnitRetain ?? string.Empty);
        Preferences.Default.Set(PrefPushGov, PushUnitGov ?? string.Empty);
        Preferences.Default.Set(PrefPushClinic, PushUnitClinic ?? string.Empty);

        await ExecuteAsync(async ct =>
        {
            foreach (var (label, unit) in units)
            {
                var fields = BuildStatementFields(unit);
                var request = new PrintJobRequest
                {
                    BusinessType = StatementBusinessType,
                    BusinessId = _context?.DeclarationId ?? _context?.DeathRecordId,
                    Classification = StatementBusinessType,
                    TemplateId = _statementTemplate!.Id,
                    TemplateName = _statementTemplate.Name,
                    ApplicantName = Name ?? string.Empty,
                    ApplicantIdCard = IdCard ?? string.Empty,
                    PrinterName = null,
                    Copies = Math.Max(1, Copies),
                    IsDuplex = IsDuplex,
                    Fields = fields,
                    TableRows = new()
                };

                var result = await _printJobFactory.EnqueueAsync(request, ct);
                if (result.IsFailure)
                {
                    await ShowTipAsync($"「{label}」推送失败: {result.Message}");
                    return;
                }

                _logger.LogBusiness("死亡情况说明-三联推送",
                    ("Label", label), ("PushUnit", unit), ("JobNo", result.Value?.JobNo ?? string.Empty));
            }

            await ShowTipAsync($"三联已推送 {units.Count} 份，将在电脑端逐份打印。");
        }, "三联推送中…");
    }

    #endregion
}
