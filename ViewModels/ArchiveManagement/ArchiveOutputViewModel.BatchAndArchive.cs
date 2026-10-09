using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.AssetVerification;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.Domain.Printing;
using NewCosmos.Services.Domain.Recovery;
using NewCosmos.Services.Core;
using NewCosmos.Services.Platform;
using NewCosmos.Services.Utilities;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;

namespace NewCosmos.ViewModels.ArchiveManagement;

public partial class ArchiveOutputViewModel
{
    // ========================
    //  批量（打印选中 / 全部保存）
    // ========================

    [RelayCommand]
    private Task PrintAllAsync() => PrintAllCoreAsync(interactive: true);

    /// <summary>
    /// 批量打印：直接以指定模板静默打印（不走命令、不依赖 SelectedPreviewTemplate 空校验，
    /// 避免统一补打中心批量时误报「请先选择一个模板」）。template 为空/无效时返回 false。
    /// </summary>
    public async Task<bool> PrintTemplateForBatchAsync(TemplateSelectItem? template)
    {
        if (template == null || template.TemplateId <= 0)
        {
            _logger.Warn("批量打印指定模板为空或无效，已跳过");
            return false;
        }
        SelectedPreviewTemplate = template;
        return await PrintCoreAsync(template, interactive: false);
    }

    /// <summary>
    /// 一键打印/全部保存核心（可编程调用）：处理全部勾选模板（封面先行）。
    /// interactive=true 保持原有交互确认（牛皮纸提示/核查报告跳过/结果弹窗）；
    /// interactive=false 供统一补打页批量模式逐户静默调用（封面直接打印、失败仅记录不弹窗）。
    /// printToPrinter=false 为「全部保存」：同一批模板全部仅生成落盘、不调打印机。
    /// </summary>
    public async Task PrintAllCoreAsync(bool interactive = true, bool printToPrinter = true)
    {
        var action = printToPrinter ? "打印" : "保存";
        var selectedTemplates = Templates.Where(t => t.IsSelected && t.IsApplicable).ToList();
        if (selectedTemplates.Count == 0)
        {
            // 批量静默模式：模板集为空时自动选全部适用模板，避免逐户弹「请至少选择一个模板」；
            // 确实无可用模板则记录并跳过（不弹窗）。
            var applicable = Templates.Where(t => t.IsApplicable).ToList();
            if (!interactive)
            {
                if (applicable.Count == 0)
                {
                    _logger.Warn("批量打印：该档案未匹配到可用模板，已跳过");
                    return;
                }
                foreach (var t in applicable)
                    t.IsSelected = true;
                selectedTemplates = applicable;
                _logger.Warn($"批量打印：未显式选择模板，已自动选全部适用模板 {applicable.Count} 个");
            }
            else
            {
                await _dialogService.DisplayAlertAsync("提示", "请至少选择一个模板", "确定");
                return;
            }
        }

        var failedItems = new List<(string Name, string Message)>();
        var successCount = 0;

        // 资产核查档：检查核查报告是否已上传（使用 check_id 解析逻辑）
        if (PrintNavigationData.BusinessType == "AssetVerification")
        {
            var checkId = await ResolveCheckIdAsync(
                PrintNavigationData.BusinessType, PrintNavigationData.BusinessId, PrintNavigationData.FieldData, CancellationToken);
            if (checkId.HasValue)
            {
                var reportItem = Templates.FirstOrDefault(t => t.IsVerificationReport && t.CheckId == checkId.Value);
                if (reportItem != null && !reportItem.IsSelected)
                {
                    // 核查报告未勾选：提示用户（批量静默模式直接跳过）
                    var skipReport = interactive
                        ? await _dialogService.DisplayAlertAsync("提示",
                            $"核查报告未勾选，是否跳过核查报告{action}？", "跳过", $"取消{action}")
                        : true;
                    if (!skipReport) return;
                }
            }
        }

        // 封面单独队列：必须先打印封面，只有封面成功后才能打印后续页面
        var coverTemplate = selectedTemplates.FirstOrDefault(t => t.Name.Contains("封面"));
        var restTemplates = selectedTemplates.Where(t => !t.Name.Contains("封面")).ToList();

        try
        {
            IsGenerating = true;
            Progress = 0;
            ProgressText = $"准备{action}...";

            if (coverTemplate != null)
            {
                // 封面牛皮纸确认只在真打印时弹出（保存不调打印机，无需插纸）
                var confirmed = !interactive || !printToPrinter || await _dialogService.DisplayAlertAsync(
                    "打印确认",
                    "请确认已插入牛皮纸，是否开始打印封面？",
                    "继续",
                    "取消");
                if (!confirmed)
                {
                    Progress = 1;
                    StatusText = "已取消打印";
                    return;
                }

                CancellationToken.ThrowIfCancellationRequested();
                ProgressText = $"执行{action}: {coverTemplate.Name}";
                var coverResult = await PrintSingleTemplateAsync(coverTemplate, failedItems, printToPrinter);
                if (coverResult)
                {
                    successCount++;
                    Progress = (double)1 / Math.Max(selectedTemplates.Count, 1);
                }
                else
                {
                    // 封面打印失败：中止整个批量，后续页面不打印
                    Progress = 1;
                    StatusText = $"批量{action}中止: 封面{action}失败 ({failedItems[^1].Message})";
                    _logger.LogBusiness($"批量{action}中止: 封面失败",
                        ("BatchNo", _batchNo), ("Message", failedItems[^1].Message));
                    if (interactive)
                    {
                        await _dialogService.DisplayAlertAsync(
                            $"{action}失败",
                            $"封面{action}失败：{failedItems[^1].Message}\n已中止批量{action}，请处理后再试。",
                            "确定");
                    }
                    IsGenerating = false;
                    return;
                }
            }

            var totalSteps = restTemplates.Count;
            var currentStep = 0;

            foreach (var template in restTemplates)
            {
                CancellationToken.ThrowIfCancellationRequested();

                currentStep++;
                Progress = (double)currentStep / totalSteps;
                ProgressText = $"执行{action}: {template.Name}";

                // 核查报告走浏览器打印路径
                if (template.IsVerificationReport)
                {
                    var reportPrinted = await PrintVerificationReportAsync(template, failedItems, printToPrinter);
                    if (reportPrinted) successCount++;
                    continue;
                }

                if (await PrintSingleTemplateAsync(template, failedItems, printToPrinter))
                    successCount++;
            }

            Progress = 1;
            StatusText = $"批量{action}完成: 成功 {successCount} 个" + (failedItems.Count > 0 ? $", 失败 {failedItems.Count} 个" : "");
            _logger.LogBusiness($"批量{action}完成", ("BatchNo", _batchNo), ("Success", successCount), ("Failed", failedItems.Count));

            if (!interactive)
            {
                // 批量静默模式：结果仅体现在状态文本，由调用方汇总展示
                ProgressText = failedItems.Count == 0
                    ? $"批量{action}完成，共 {successCount} 个模板"
                    : $"批量{action}: 成功 {successCount} 个 失败 {failedItems.Count} 个";
                return;
            }

            if (failedItems.Count == 0)
            {
                ProgressText = $"批量{action}完成，共 {successCount} 个模板";
            }
            else if (successCount > 0)
            {
                ProgressText = $"批量{action}: 成功 {successCount} 个 失败 {failedItems.Count} 个";
                var failedList = failedItems.Select(f => $"{f.Name}: {f.Message}").Take(3);
                var moreCount = failedItems.Count > 3 ? failedItems.Count - 3 : 0;
                var message = $"成功: {successCount} 个\n失败: {failedItems.Count} 个\n\n失败详情:\n{string.Join("\n", failedList)}";
                if (moreCount > 0) message += $"\n...还有 {moreCount} 个失败";
                await _dialogService.DisplayAlertAsync($"{action}结果", message, "确定");
            }
            else
            {
                ProgressText = $"批量{action}失败: {failedItems.Count} 个";
                var failedList = failedItems.Select(f => $"{f.Name}: {f.Message}").Take(5);
                var moreCount = failedItems.Count > 5 ? failedItems.Count - 5 : 0;
                var message = $"全部{action}失败 ({failedItems.Count} 个)\n\n失败详情:\n{string.Join("\n", failedList)}";
                if (moreCount > 0) message += $"\n...还有 {moreCount} 个失败";
                await _dialogService.DisplayAlertAsync($"{action}失败", message, "确定");
            }
        }
        catch (OperationCanceledException)
        {
            ProgressText = $"{action}已取消";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "失败");
            await _dialogService.DisplayAlertAsync("错误", $"批量{action}失败: {ex.Message}", "确定");
        }
        finally
        {
            IsGenerating = false;
        }
    }

    /// <summary>
    /// 执行单个模板打印（含字段/行数据构建，成功时收集输出文件）
    /// </summary>
    private async Task<bool> PrintSingleTemplateAsync(TemplateSelectItem template, List<(string Name, string Message)> failedItems, bool printToPrinter = true)
    {
        var action = printToPrinter ? "打印" : "保存";
        var fields = new Dictionary<string, string>(PrintNavigationData.FieldData!, StringComparer.Ordinal);
        var tableRows = PrintNavigationData.TableData ?? new();

        // 目录模板：使用目录专属字段和行数据
        if (template.IsDirectoryTemplate)
        {
            var tl = _timelineService.GetTimelineForDate(await ResolveArchiveAnchorAsync(), TimelineType.BusinessProcess);
            fields = await BuildDirectoryFieldsAsync(fields);
            tableRows = BuildDirectoryRows(tl.MeetingDate);
        }
        // 赡养费承诺书：使用赡养人数据
        else if (template.Name.Contains("赡养费承诺书"))
        {
            tableRows = PrintNavigationData.SupporterTableData ?? new();
        }
        // 近亲属备案两表：使用备案对数据（每对一页，copiesBySupporter 模式）
        else if (template.Name.Contains("近亲属"))
        {
            tableRows = PrintNavigationData.NearRelativePairs ?? new();
        }

        var applicantName = fields.TryGetValue("APPLICANT_NAME", out var name) ? name : "未知";
        var applicantIdCard = fields.TryGetValue("APPLICANT_ID_CARD", out var idCard) ? idCard : "000000000000000000";
        applicantIdCard = applicantIdCard.Length > 6 ? applicantIdCard : "000000000000000000";

        var actualCopies = template.BaseCopies * Copies;
        _logger.Info($"批量{action}: 模板={template.Name}");

        var result = await _printExecuteService.ExecutePrintAsync(
            template.TemplateId,
            template.Name,
            PrintNavigationData.BusinessType,
            PrintNavigationData.BusinessId,
            _batchNo,
            fields,
            tableRows,
            applicantName,
            applicantIdCard,
            printToPrinter ? SelectedPrinter : null!,
            actualCopies,
            IsDuplex,
            printToPrinter: printToPrinter,
            CancellationToken);

        if (result.IsSuccess)
        {
            AddOutputFiles(template.TemplateId, template.Name, result.Value.FilePath, result.Value.PdfPath);
            return true;
        }
        else
        {
            failedItems.Add((template.Name, result.Message ?? "未知错误"));
            _logger.Warn($"批量{action}失败");
            return false;
        }
    }

    // ========================
    //  输出文件管理
    // ========================

    [RelayCommand]
    private async Task PreviewFileAsync(OutputFileItem file)
    {
        if (file == null || string.IsNullOrEmpty(file.FilePath))
        {
            await _dialogService.DisplayAlertAsync("提示", "文件路径无效", "确定");
            return;
        }

        if (!file.IsPdf)
        {
            await _dialogService.DisplayAlertAsync("提示", "仅 PDF 文件可在页面内预览", "确定");
            return;
        }

        try
        {
            PdfFilePath = file.FilePath;
            HasPdf = true;
            StatusText = $"预览: {file.FileName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "失败");
            await _dialogService.DisplayAlertAsync("错误", $"预览失败: {ex.Message}", "确定");
        }
    }

    /// <summary>行内「打印」：该文件对应模板重新生成并调打印机</summary>
    [RelayCommand]
    private Task PrintFileAsync(OutputFileItem file) => ExecuteFileActionAsync(file, printToPrinter: true);

    /// <summary>
    /// 行内动作公共链（打印/保存共用）：按模板重新生成该文件；
    /// printToPrinter=false 即四件套里的「保存」——生成落盘到输出根、不调打印机。
    /// </summary>
    private async Task ExecuteFileActionAsync(OutputFileItem? file, bool printToPrinter)
    {
        var action = printToPrinter ? "打印" : "保存";
        if (file == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请选择要操作的文件", "确定");
            return;
        }

        // 无模板产物（核查报告等）：文件已在输出根，无需重新生成
        if (file.TemplateId <= 0)
        {
            if (printToPrinter)
            {
                if (WebViewPrintPdfFunc != null)
                    await WebViewPrintPdfFunc(file.FilePath);
                else
                    ShellOpenPdf(file.FilePath);
                StatusText = $"已发送打印: {file.FileName}";
            }
            else
            {
                StatusText = $"{file.FileName} 已在输出目录";
            }
            return;
        }

        if (PrintNavigationData.FieldData == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "没有可用的打印数据", "确定");
            return;
        }

        // 封面模板打印前确认：需先插入牛皮纸（保存不调打印机，跳过）
        if (printToPrinter && file.TemplateName.Contains("封面"))
        {
            var confirmed = await _dialogService.DisplayAlertAsync(
                "打印确认",
                "请确认已插入牛皮纸，是否继续打印封面？",
                "继续",
                "取消");
            if (!confirmed) return;
        }

        try
        {
            IsBusy = true;
            StatusText = $"正在{action}: {file.FileName}";

            var fields = new Dictionary<string, string>(PrintNavigationData.FieldData, StringComparer.Ordinal);
            var tableRows = PrintNavigationData.TableData ?? new();

            // 目录模板：使用目录专属字段和行数据
            if (file.TemplateName.Contains("目录"))
            {
                var tl = _timelineService.GetTimelineForDate(await ResolveArchiveAnchorAsync(), TimelineType.BusinessProcess);
                fields = await BuildDirectoryFieldsAsync(fields);
                tableRows = BuildDirectoryRows(tl.MeetingDate);
            }
            // 赡养费承诺书：使用赡养人数据
            else if (file.TemplateName.Contains("赡养费承诺书"))
            {
                tableRows = PrintNavigationData.SupporterTableData ?? new();
            }
            // 近亲属备案两表：使用备案对数据（每对一页，copiesBySupporter 模式）
            else if (file.TemplateName.Contains("近亲属"))
            {
                tableRows = PrintNavigationData.NearRelativePairs ?? new();
            }

            var applicantName = fields.TryGetValue("APPLICANT_NAME", out var name) ? name : "未知";
            var applicantIdCard = fields.TryGetValue("APPLICANT_ID_CARD", out var idCard) ? idCard : "000000000000000000";
            applicantIdCard = applicantIdCard.Length > 6 ? applicantIdCard : "000000000000000000";

            var actualCopies = file.BaseCopies * Copies;

            var result = await _printExecuteService.ExecutePrintAsync(
                file.TemplateId,
                file.TemplateName,
                PrintNavigationData.BusinessType,
                PrintNavigationData.BusinessId,
                _batchNo,
                fields,
                tableRows,
                applicantName,
                applicantIdCard,
                printToPrinter ? SelectedPrinter : null!,
                actualCopies,
                IsDuplex,
                printToPrinter: printToPrinter,
                CancellationToken);

            if (result.IsSuccess)
            {
                _logger.LogBusiness($"单文件{action}完成",
                    ("Template", file.TemplateName),
                    ("BatchNo", _batchNo),
                    ("Copies", actualCopies));
                StatusText = $"{action}完成";
                AddOutputFiles(file.TemplateId, file.TemplateName, result.Value.FilePath, result.Value.PdfPath);
            }
            else
            {
                StatusText = $"{action}失败: {result.Message}";
                await _dialogService.DisplayAlertAsync("错误", $"{action}失败: {result.Message}", "确定");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "失败");
            StatusText = $"{action}异常: {ex.Message}";
            await _dialogService.DisplayAlertAsync("错误", $"{action}异常: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ========================
    //  完成归档 / 返回
    // ========================

    /// <summary>
    /// 完成工单：打印→保存→完成 流的「完成」动作。
    /// 仅记录办理完成留痕（本批打印追痕 remark + 业务日志），不改档案状态、不导航；
    /// 文档/过滤模式下替代被隐藏的「完成归档」。
    /// </summary>
    [RelayCommand]
    private async Task CompleteWorkOrderAsync()
    {
        if (IsWorkOrderCompleted) return;

        if (OutputFiles.Count == 0 || !OutputFiles.Any(f => f.IsCompleted))
        {
            await _dialogService.DisplayAlertAsync("提示", "请至少生成一个输出文件", "确定");
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认", "确认完成该文书办理工单？\n仅记录办理完成，不影响档案状态。", "确认", "取消");
        if (!confirm) return;

        try
        {
            if (!string.IsNullOrEmpty(_batchNo))
            {
                var remark = $"工单办理完成 {DateTime.Now:yyyy-MM-dd HH:mm}";
                var markResult = await _printRecordService.MarkBatchWorkOrderCompletedAsync(
                    _batchNo, remark,
                    CancellationToken.None); // [CT 豁免] 归档批次状态回写属收尾落库，中断会文件已出而状态未改
                if (markResult.IsFailure)
                {
                    await _dialogService.DisplayAlertAsync("错误", $"工单完成留痕失败: {markResult.Message}", "确定");
                    return;
                }
            }

            _logger.LogBusiness("文书工单完成",
                ("BusinessType", PrintNavigationData.BusinessType),
                ("BusinessId", PrintNavigationData.BusinessId?.ToString() ?? ""),
                ("BatchNo", _batchNo),
                ("FileCount", OutputFiles.Count));

            IsWorkOrderCompleted = true;
            StatusText = "工单已完成";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "完成工单失败");
            await _dialogService.DisplayAlertAsync("错误", $"完成工单失败: {ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private async Task CompleteAsync()
    {
        // 仅出文书/文书直出文档模式：禁止完成归档（会把渐退草稿 Draft 强行置 Approved/step6）
        if (IsTemplateFilterMode || IsDocumentMode)
        {
            await _dialogService.DisplayAlertAsync("提示", "仅出文书模式不支持完成归档", "确定");
            return;
        }

        if (OutputFiles.Count == 0 || !OutputFiles.Any(f => f.IsCompleted))
        {
            await _dialogService.DisplayAlertAsync("提示", "请至少生成一个输出文件", "确定");
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync("确认", "确认完成归档操作？", "确认", "取消");
        if (!confirm) return;

        // 归档完成：current_step 推进到 6（已完结）并将 status 置 Approved（已审批/在保）
        // 系统无独立审批工作流，归档即视为审批通过（跳过状态机 Draft→Approved 校验）
        if (PrintNavigationData.BusinessId.HasValue)
        {
            try
            {
                if (PrintNavigationData.BusinessType == "Recovery")
                {
                    var recoveryService = _serviceProvider.GetRequiredService<IRecoveryService>();
                    var recoveryResult = await recoveryService.UpdateRecoveryRecordStatusAsync(
                        PrintNavigationData.BusinessId.Value, RecoveryConstants.STATUS_PRINTED,
                        CancellationToken.None); // [CT 豁免] 追缴状态回写属收尾落库，不可中断
                    if (recoveryResult.IsSuccess)
                        _logger.LogBusiness("追缴记录归档完成，状态置已打印", ("RecoveryId", PrintNavigationData.BusinessId.Value));
                    else
                        _logger.Error($"追缴记录归档完成但更新状态失败: {recoveryResult.Message}");
                }
                else
                {
                    var appService = _serviceProvider.GetRequiredService<IApplicationService>();
                    var stepResult = await appService.CompleteArchiveAsync(PrintNavigationData.BusinessId.Value,
                        CancellationToken.None); // [CT 豁免] 归档完成回写属收尾落库，不可中断
                    if (stepResult.IsSuccess)
                        _logger.LogBusiness("归档完成，current_step=6 且已置已审批", ("ApplicationId", PrintNavigationData.BusinessId.Value));
                    else
                        _logger.Error($"归档完成但更新状态失败: {stepResult.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"归档完成更新状态异常: {ex.Message}");
            }
        }

        _logger.LogBusiness("完成归档操作", ("BatchNo", _batchNo), ("FileCount", OutputFiles.Count));
        Cleanup();
        var navigation = Helpers.WindowNavigator.CurrentNavigation;
        if (navigation != null)
        {
            await navigation.PopToRootAsync();
            // PopToRoot 直接回到主首页，必须恢复窗口标题（历史 BUG：标题残留档案输出页）
            RestoreWindowTitleFromNavigation();
        }
    }

    /// <summary>
    /// 清理预览临时文件、PdfFilePath 与静态打印数据（组合场景：历史补打页返回时调用，
    /// 不再走 ArchiveOutputPage 的 OnDisappearing 清理）
    /// </summary>
    public void Cleanup()
    {
        TryDeleteFile(_currentTempPreviewPath);
        _currentTempPreviewPath = string.Empty;
        PdfFilePath = string.Empty;
        HasPdf = false;
        _currentPdfData = null;
        PrintNavigationData.Clear();
    }

    /// <summary>返回上一页：先清理预览临时文件与打印数据，再走基类返回逻辑</summary>
    public override async Task GoBackAsync()
    {
        try
        {
            Cleanup();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "归档输出返回前清理失败");
        }
        await base.GoBackAsync();
    }

    private void TryDeleteFile(string filePath)
    {
        _fileService.DeleteFile(filePath);
    }

    /// <summary>
    /// 使用系统默认 PDF 应用（首选 verb=print）打印核查报告
    /// </summary>
    private void ShellPrintPdf(string pdfPath)
    {
        try
        {
            var psi = new ProcessStartInfo(pdfPath)
            {
                UseShellExecute = true,
                Verb = "print"
            };
            using var process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"PDF ShellPrint 失败: {pdfPath}");
            throw;
        }
    }

    /// <summary>
    /// 使用系统默认 PDF 应用（verb=open）预览核查报告
    /// </summary>
    private void ShellOpenPdf(string pdfPath)
    {
        try
        {
            var psi = new ProcessStartInfo(pdfPath)
            {
                UseShellExecute = true,
                Verb = "open"
            };
            using var process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"PDF ShellOpen 失败: {pdfPath}");
            throw;
        }
    }

    /// <summary>
    /// 生成核查报告（PDF 落输出根/核查报告/；printToPrinter=true 时再经浏览器/WebView2 执行打印）
    /// </summary>
    private async Task<bool> PrintVerificationReportAsync(TemplateSelectItem reportItem, List<(string Name, string Message)> failedItems, bool printToPrinter = true)
    {
        try
        {
            var reportResult = await _pdfVerificationService.GetReportDataByCheckIdAsync(
                reportItem.CheckId, CancellationToken);
            if (reportResult.IsFailure || reportResult.Value is not { Length: > 0 })
            {
                failedItems.Add(("核查报告", $"获取报告数据失败: {reportResult.Message}"));
                return false;
            }

            // 报告是正式产物，与四件套口径一致：落输出根（不再进会被清理的 temp）
            var reportDir = Path.Combine(OutputPathHelper.OutputRoot, "核查报告");
            Directory.CreateDirectory(reportDir);
            var reportName = $"核查报告_{DateTime.Now:yyyyMMddHHmmssfff}.pdf";
            var reportPath = Path.Combine(reportDir, reportName);
            await _fileService.WriteAllBytesAsync(reportPath, reportResult.Value, CancellationToken);

            AddOutputFiles(0, "核查报告", null, reportPath);

            if (printToPrinter)
            {
                // 通过 WebView2 打印（复用 PdfJs 渲染 + 系统打印对话框，不依赖默认 PDF 关联）
                if (WebViewPrintPdfFunc != null)
                {
                    await WebViewPrintPdfFunc(reportPath);
                }
                else
                {
                    // 回退：仅打开预览（无打印）
                    ShellOpenPdf(reportPath);
                }
                _logger.LogBusiness("核查报告已发送打印",
                    ("CheckId", reportItem.CheckId.ToString()),
                    ("BatchNo", _batchNo));
            }
            else
            {
                _logger.LogBusiness("核查报告已生成(未打印)",
                    ("CheckId", reportItem.CheckId.ToString()),
                    ("BatchNo", _batchNo));
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "核查报告打印失败");
            failedItems.Add(("核查报告", ex.Message));
            return false;
        }
    }

    /// <summary>
    /// 预览核查报告（通过浏览器打开）
    /// </summary>
    [RelayCommand]
    private async Task PreviewReportAsync(TemplateSelectItem? reportItem)
    {
        if (reportItem == null || !reportItem.IsVerificationReport) return;

        try
        {
            IsBusy = true;
            var reportResult = await _pdfVerificationService.GetReportDataByCheckIdAsync(
                reportItem.CheckId, CancellationToken);
            if (reportResult.IsFailure || reportResult.Value is not { Length: > 0 })
            {
                await _dialogService.DisplayAlertAsync("提示", $"获取报告数据失败: {reportResult.Message}", "确定");
                return;
            }

            var reportPath = Path.Combine(OutputPathHelper.GetTempDirectory(), $"check_report_preview_{Guid.NewGuid():N}.pdf");
            await _fileService.WriteAllBytesAsync(reportPath, reportResult.Value, CancellationToken);

            // 通过浏览器打开预览
            ShellOpenPdf(reportPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "核查报告预览失败");
            await _dialogService.DisplayAlertAsync("错误", $"核查报告预览失败: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>按业务类型解析档案模板分类（统一走 ArchiveCategoryResolver，补打中心同源）</summary>
    private string[] GetCategoriesByBusinessType(string businessType, string? operationOverride = null)
        => ArchiveCategoryResolver.GetRecordCategories(
            businessType, PrintNavigationData.Classification, operationOverride);

    // ========================
    //  保存（四件套：仅生成落盘，不打印）
    // ========================

    /// <summary>行内「保存」：按模板重新生成该文件到输出根，不调打印机</summary>
    [RelayCommand]
    private Task SaveFileAsync(OutputFileItem file) => ExecuteFileActionAsync(file, printToPrinter: false);

    /// <summary>「保存选中」：勾选模板全部仅生成落盘（不打印）</summary>
    [RelayCommand]
    private async Task SaveSelectedAsync()
        => await PrintAllCoreAsync(interactive: true, printToPrinter: false);

    /// <summary>
    /// 「全部保存」：全部适用模板仅生成落盘（不打印）。
    /// 输出已固定到 config\document_output.yaml 的 output.base_directory，不再弹目录选择。
    /// </summary>
    [RelayCommand]
    private async Task SaveAllFilesAsync()
    {
        var applicable = Templates.Where(t => t.IsApplicable).ToList();
        if (applicable.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "没有可用模板", "确定");
            return;
        }

        foreach (var t in applicable)
            t.IsSelected = true;

        await PrintAllCoreAsync(interactive: true, printToPrinter: false);
        TryOpenFolder(OutputPathHelper.OutputRoot);
    }

    // ========================
    //  目录模板数据构建
    // ========================

    private async Task<Dictionary<string, string>> BuildDirectoryFieldsAsync(Dictionary<string, string> originalFields)
    {
        // 临时救助档案：目录头部字段走 TEMP_* 专用口径（下方低保硬编码会取错值）
        if (PrintNavigationData.BusinessType == TempReliefConstants.BusinessType)
            return await BuildTempReliefDirectoryFieldsAsync(originalFields);

        var fields = new Dictionary<string, string>(originalFields, StringComparer.Ordinal);

        // 申请年份：从 APPLICATION_DATE 提取，无则取当前年
        if (!fields.TryGetValue(FieldKeys.APPLICATION_DATE, out var appDate) || string.IsNullOrEmpty(appDate))
            appDate = DateTime.Now.ToString("yyyy-MM-dd");
        var year = appDate.Length >= 4 ? appDate[..4] : DateTime.Now.Year.ToString();
        fields[FieldKeys.APPLICATION_YEAR] = year;

        // 业务终结时间：当天
        fields[FieldKeys.BUSINESS_END_DATE] = DateTime.Now.ToString("yyyy年MM月dd日");

        // 案卷题名：姓名+社会救助档案
        var applicantName = fields.TryGetValue(FieldKeys.APPLICANT_NAME, out var n) ? n : "未知";
        fields[FieldKeys.ARCHIVE_TITLE] = $"{applicantName}社会救助档案";

        // 保管期限：留空
        fields[FieldKeys.RETENTION_PERIOD] = "";

        // 社会救助类型：当前分类全称
        var classification = fields.TryGetValue(FieldKeys.CLASSIFICATION_RESULT, out var cls) ? cls : "";
        var fullName = ClassificationConstants.ConvertToFullName(classification);
        fields[FieldKeys.SOCIAL_ASSISTANCE_TYPE] = fullName;

        // 档案卷号：B线结算日次月1日，格式 yyyy-M-d
        var volumeDate = await CalculateBLineVolumeDateAsync();
        fields[FieldKeys.ARCHIVE_VOLUME_NO] = volumeDate;
        fields[FieldKeys.ARCHIVE_VOLUME] = volumeDate;  // 模板配置兼容

        // 版权信息：根据户主（申请人）身份证出生月份取对应电信号，1-12 月对应第 1-12 号，无效/越界取第 13 号（昔涟）兜底
        fields.TryGetValue(FieldKeys.APPLICANT_ID_CARD, out var idCardForCopyright);
        fields[FieldKeys.COPYRIGHT_INFO] = CopyrightHelper.BuildCopyrightInfo(idCardForCopyright);

        return fields;
    }

    /// <summary>
    /// 临时救助目录头部字段：复用档案_目录同名占位符，值取自 TEMP_* 打印数据
    /// </summary>
    private async Task<Dictionary<string, string>> BuildTempReliefDirectoryFieldsAsync(Dictionary<string, string> originalFields)
    {
        var fields = new Dictionary<string, string>(originalFields, StringComparer.Ordinal);

        var applicantName = fields.TryGetValue(FieldKeys.TEMP_APPLICANT_NAME, out var n) && !string.IsNullOrWhiteSpace(n) ? n : "未知";
        fields.TryGetValue(FieldKeys.TEMP_APPLY_DATE, out var applyDate);

        // 申请年份：从申请日期提取，无则取当前年
        fields[FieldKeys.APPLICATION_YEAR] = !string.IsNullOrEmpty(applyDate) && applyDate.Length >= 4
            ? applyDate[..4] : DateTime.Now.Year.ToString();

        // 业务终结时间：当天
        fields[FieldKeys.BUSINESS_END_DATE] = DateTime.Now.ToString("yyyy年MM月dd日");

        // 案卷题名：姓名+临时救助档案
        fields[FieldKeys.ARCHIVE_TITLE] = $"{applicantName}临时救助档案";

        // 保管期限：留空；社会救助类型固定"临时救助"
        fields[FieldKeys.RETENTION_PERIOD] = "";
        fields[FieldKeys.SOCIAL_ASSISTANCE_TYPE] = "临时救助";

        // 档案卷号：沿用 B 线结算日次月 1 日算法
        var volumeDate = await CalculateBLineVolumeDateAsync();
        fields[FieldKeys.ARCHIVE_VOLUME_NO] = volumeDate;
        fields[FieldKeys.ARCHIVE_VOLUME] = volumeDate;

        // 组卷单位：填报单位
        if (!fields.TryGetValue(FieldKeys.OPERATOR_UNIT, out var opUnit) || string.IsNullOrWhiteSpace(opUnit))
            fields[FieldKeys.OPERATOR_UNIT] = fields.TryGetValue(FieldKeys.TEMP_REPORT_UNIT, out var ru) ? ru : string.Empty;

        // 版权信息：按申请人身份证出生月映射
        fields.TryGetValue(FieldKeys.TEMP_APPLICANT_ID_CARD, out var idCard);
        fields[FieldKeys.COPYRIGHT_INFO] = CopyrightHelper.BuildCopyrightInfo(idCard);

        return fields;
    }

    private List<Dictionary<string, string>> BuildDirectoryRows(DateTime meetingDate)
    {
        // 临时救助档案：固定 7 条佐证材料行
        if (PrintNavigationData.BusinessType == TempReliefConstants.BusinessType)
            return BuildTempReliefDirectoryRows();

        var rows = new List<Dictionary<string, string>>();

        // 从 FieldData 获取基础信息
        var fieldData = PrintNavigationData.FieldData ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var applicantName = fieldData.TryGetValue(FieldKeys.APPLICANT_NAME, out var name) ? name : "未知";
        var appDateRaw = fieldData.TryGetValue(FieldKeys.APPLICATION_DATE, out var d) ? d : DateTime.Now.ToString("yyyy-MM-dd");
        var appDate = NormalizeDirectoryDate(appDateRaw);

        // 页号累计（首文档从第1页起）
        var currentPage = 1;

        // 静态预填条目（无模板，仅在目录中列出），责任人 = 申请人姓名
        var staticItems = new (string Title, int Copies)[]
        {
            ("户口簿（必要）", 1),
            ("身份证（必要）", 1),
            ("赡养、抚养、扶养义务人相关材料", 1),
            ("残疾人证或其他证明残疾人的材料", 1),
            ("住院病案或相关诊断材料", 1),
            ("结婚证或离婚证、离婚协议", 1),
            ("在校就读相关佐证材料", 1),
            ("申请前12个月内家庭刚性支出材料", 1),
            ("其他相关材料", 1),
        };
        foreach (var (title, copies) in staticItems)
        {
            var startPage = currentPage;
            var endPage = currentPage + copies - 1;
            rows.Add(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldKeys.DIRECTORY_PERSON] = applicantName,
                [FieldKeys.DIRECTORY_TITLE] = title,
                [FieldKeys.DIRECTORY_DATE] = appDate,
                [FieldKeys.DIRECTORY_PAGE] = $"{startPage}-{endPage}"
            });
            currentPage = endPage + 1;
        }

        // 特殊条目（无模板，仅在目录中列出），责任人 = 民政办，日期 = B线会议时间
        var specialItems = new (string Title, int Copies)[]
        {
            ("会议记录", 1),
            ("资产核查报告", 1),
        };
        foreach (var (title, copies) in specialItems)
        {
            var startPage = currentPage;
            var endPage = currentPage + copies - 1;
            rows.Add(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldKeys.DIRECTORY_PERSON] = "民政办",
                [FieldKeys.DIRECTORY_TITLE] = title,
                [FieldKeys.DIRECTORY_DATE] = NormalizeDirectoryDate(meetingDate.ToString("yyyy-MM-dd")),
                [FieldKeys.DIRECTORY_PAGE] = $"{startPage}-{endPage}"
            });
            currentPage = endPage + 1;
        }

        // 取选中的非目录模板，按 SortOrder+Name 排序
        var sortedTemplates = Templates
            .Where(t => t.IsSelected && t.IsApplicable && !t.IsDirectoryTemplate)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .ToList();

        foreach (var template in sortedTemplates)
        {
            // 题名过滤：去除"档案_"或"通用_"前缀，去除"封面"和"目录"
            var title = template.Name;
            if (title.StartsWith("档案_"))
                title = title["档案_".Length..];
            else if (title.StartsWith("通用_"))
                title = title["通用_".Length..];
            title = title.Replace("封面", "").Replace("目录", "");
            if (string.IsNullOrWhiteSpace(title))
                continue;  // 过滤结果为空则跳过（封面和目录本身不应出现在目录行中）

            var startPage = currentPage;
            var endPage = currentPage + template.BaseCopies - 1;

            // 目录日期：依据模板类型取对应日期字段值，缺失时回退申请日期
            var directoryDate = appDate;
            if (!string.IsNullOrEmpty(template.DirectoryDateField)
                && fieldData.TryGetValue(template.DirectoryDateField, out var templateDate)
                && !string.IsNullOrWhiteSpace(templateDate))
            {
                directoryDate = NormalizeDirectoryDate(templateDate);
            }

            rows.Add(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldKeys.DIRECTORY_PERSON] = "民政办",
                [FieldKeys.DIRECTORY_TITLE] = title,
                [FieldKeys.DIRECTORY_DATE] = directoryDate,
                [FieldKeys.DIRECTORY_PAGE] = $"{startPage}-{endPage}"
            });

            currentPage = endPage + 1;
        }

        return rows;
    }

    /// <summary>
    /// 临时救助目录行：7 条固定佐证材料（必要材料 + 佐证 + 公示照片），
    /// 责任人=申请人，日期=申请日期，页号逐条顺延（每条 1 页）
    /// </summary>
    private List<Dictionary<string, string>> BuildTempReliefDirectoryRows()
    {
        var fieldData = PrintNavigationData.FieldData ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var applicantName = fieldData.TryGetValue(FieldKeys.TEMP_APPLICANT_NAME, out var n) && !string.IsNullOrWhiteSpace(n)
            ? n : "未知";
        fieldData.TryGetValue(FieldKeys.TEMP_APPLY_DATE, out var appDateRaw);
        var appDate = NormalizeDirectoryDate(string.IsNullOrWhiteSpace(appDateRaw)
            ? DateTime.Now.ToString("yyyy-MM-dd")
            : appDateRaw);

        var rows = new List<Dictionary<string, string>>();
        var staticItems = new[]
        {
            "身份证（必要）",
            "户口本（必要）",
            "受残疾人证或其他证明残疾人的材料",
            "住院病历或相关诊断材料",
            "在校就读相关佐证材料",
            "因灾造成家庭损失相关佐证材料",
            "公示照片"
        };

        var currentPage = 1;
        foreach (var title in staticItems)
        {
            var startPage = currentPage;
            var endPage = currentPage;
            rows.Add(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldKeys.DIRECTORY_PERSON] = applicantName,
                [FieldKeys.DIRECTORY_TITLE] = title,
                [FieldKeys.DIRECTORY_DATE] = appDate,
                [FieldKeys.DIRECTORY_PAGE] = $"{startPage}-{endPage}"
            });
            currentPage = endPage + 1;
        }

        return rows;
    }

    /// <summary>
    /// 统一目录中的日期格式为 yyyy-MM-dd
    /// 兼容 yyyy-MM-dd / yyyy/M/d / yyyy年M月d日 / yyyy年MM月dd日 / 含时间 等格式；无法解析时保留原值
    /// </summary>
    private static string NormalizeDirectoryDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        var formats = new[]
        {
            "yyyy-MM-dd", "yyyy/M/d", "yyyy/M/dd", "yyyy/MM/d", "yyyy/MM/dd",
            "yyyy年M月d日", "yyyy年MM月d日", "yyyy年M月dd日", "yyyy年MM月dd日",
            "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"
        };
        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString("yyyy-MM-dd");
        }
        return value;
    }

    /// <summary>
    /// B线结算日的次月1日，格式 yyyy-M-d（用于档案卷号）。
    /// 按该档案自身业务日期锚定周期（补全完成→首次审批→更新→创建），保证档案补打时卷号稳定、不随当天漂移。
    /// </summary>
    private async Task<string> CalculateBLineVolumeDateAsync()
    {
        var anchor = await ResolveArchiveAnchorAsync();
        var tl = _timelineService.GetTimelineForDate(anchor, TimelineType.BusinessProcess);
        return new DateTime(tl.CycleEndDate.Year, tl.CycleEndDate.Month, 1).AddMonths(1).ToString("yyyy-M-d");
    }

    /// <summary>目录卷号锚定日期：申请类档案按该申请业务日期；其它域回退当天。</summary>
    private async Task<DateTime> ResolveArchiveAnchorAsync()
    {
        if (PrintNavigationData.BusinessType is "FamilyApplication" or "EconomicReview"
            && PrintNavigationData.BusinessId is > 0)
        {
            try
            {
                var appService = _serviceProvider.GetRequiredService<IApplicationService>();
                var result = await appService.GetByIdAsync(PrintNavigationData.BusinessId.Value, CancellationToken);
                var app = result.IsSuccess ? result.Value : null;
                if (app != null)
                {
                    if (app.DataCompletedAt is { } completed && completed != default) return completed;
                    if (app.FirstApprovedAt is { } approved && approved != default) return approved;
                    if (app.UpdatedAt != default) return app.UpdatedAt;
                    if (app.CreatedAt != default) return app.CreatedAt;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"目录卷号锚定日期解析失败，回退当天: {ex.Message}");
            }
        }
        return DateTime.Today;
    }
}
