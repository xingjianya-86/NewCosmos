using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.ViewModels.ArchiveManagement;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 工作流「已建档未提交」Tab 批量归档（2026-11 新增）。
///
/// 管线（每户四步，全部复用既有能力，无新服务）：
///  ① ArchiveProductionViewModel.BuildPrintDataAsync(appId) —— 字段装配唯一权威（§14 文书直出规范）；
///  ② 写 PrintNavigationData（先 ClearDocumentMode 防文书模式残留，与文书直出 ValidationAndSubmit 同口径）；
///  ③ ArchiveOutputViewModel.InitializeAsync() + PrintAllCoreAsync(interactive:false, printToPrinter:false)
///     —— 全部适用模板静默生成落盘（与统一补打中心批量逐户同一静默通道），满足「至少一个输出文件」归档前置；
///  ④ IApplicationService.CompleteArchiveAsync(appId, 操作人) —— 归档即审核（Draft→Approved + step6）。
///
/// 铁律：ArchiveProductionViewModel 的 BuildPrintDataAsync 使用实例内部字段状态（不可并发），
/// 批量期间用静态串行闸隔离；每户开始前 OutputFiles.Clear() 防上一户文件满足本户归档前置；
/// 批次 finally 必须 PrintNavigationData.Clear() 释放 PII（本页不导航输出页，无人代清）。
/// </summary>
public partial class ApplicationWorkflowViewModel
{
    /// <summary>BuildPrintDataAsync 串行闸（与 ApplicationFormViewModel._documentBuildGate 同因同构）</summary>
    private static readonly SemaphoreSlim _batchArchiveBuildGate = new(1, 1);

    #region 批量进度状态

    [ObservableProperty]
    private bool _isBatchArchiving;

    [ObservableProperty]
    private double _batchProgress;

    [ObservableProperty]
    private string _batchProgressText = string.Empty;

    #endregion

    #region 勾选

    /// <summary>全选当前页可勾选卡片</summary>
    [RelayCommand]
    private void SelectAllBatch()
    {
        foreach (var item in ArchiveBuiltItems.Where(i => i.IsBatchSelectable))
            item.IsSelected = true;
    }

    /// <summary>清空全部勾选</summary>
    [RelayCommand]
    private void ClearBatchSelection()
    {
        foreach (var item in ArchiveBuiltItems)
            item.IsSelected = false;
    }

    #endregion

    /// <summary>
    /// 批量归档：勾选卡片 → 逐户生成全套文书（保存落盘）→ 完成归档。
    /// 失败户跳过归档并入汇总（文书已生成但状态未推进的户由单户归档页既有路径兜底重试）。
    /// </summary>
    [RelayCommand]
    private async Task BatchArchiveAsync()
    {
        if (IsBatchArchiving) return;

        var selected = ArchiveBuiltItems
            .Where(i => i.IsBatchSelectable && i.IsSelected && i.Source is ApplicationEntity)
            .Select(i => (Item: i, App: (ApplicationEntity)i.Source))
            .ToList();

        if (selected.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先勾选要归档的档案卡片", "确定");
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync("确认批量归档",
            $"将逐户生成全套文书并完成归档，共 {selected.Count} 户。\n文书保存到输出目录后档案状态推进为「已完结」，是否继续？",
            "确认", "取消");
        if (!confirm) return;

        var operatorName = string.IsNullOrEmpty(App.CurrentUserName) ? "System" : App.CurrentUserName;
        var successCount = 0;
        var failedItems = new List<(string Name, string Error)>();

        // 每批次独立实例：状态互不污染（Transient 亦不复用跨页旧实例）
        var production = _serviceProvider.GetRequiredService<ArchiveProductionViewModel>();
        var output = _serviceProvider.GetRequiredService<ArchiveOutputViewModel>();

        IsBatchArchiving = true;
        BatchProgress = 0;
        IsBusy = true;
        try
        {
            for (var i = 0; i < selected.Count; i++)
            {
                CancellationToken.ThrowIfCancellationRequested();
                var (item, app) = selected[i];
                BatchProgress = (double)i / selected.Count;
                BatchProgressText = $"正在归档 ({i + 1}/{selected.Count}): {app.ApplicantName}";

                try
                {
                    // ① 字段装配（串行闸：实例内部字段状态不可并发）
                    ApplicationPrintData? printData;
                    await _batchArchiveBuildGate.WaitAsync(CancellationToken);
                    try
                    {
                        printData = await production.BuildPrintDataAsync(app.Id);
                    }
                    finally
                    {
                        _batchArchiveBuildGate.Release();
                    }

                    if (printData == null)
                    {
                        failedItems.Add((app.ApplicantName, "档案数据装配失败，数据可能不完整"));
                        continue;
                    }

                    // ② 输出上下文（先清文书模式四字段，防上一户/其他入口残留）
                    PrintNavigationData.ClearDocumentMode();
                    PrintNavigationData.BusinessType = string.IsNullOrEmpty(printData.BusinessType)
                        ? "FamilyApplication"
                        : printData.BusinessType;
                    PrintNavigationData.BusinessId = printData.BusinessId ?? app.Id;
                    PrintNavigationData.Classification = printData.Classification;
                    PrintNavigationData.Status = app.Status ?? string.Empty;
                    PrintNavigationData.FieldData = new Dictionary<string, string>(printData.FieldData, StringComparer.Ordinal);
                    PrintNavigationData.TableData = printData.TableData?.ToList() ?? new();
                    PrintNavigationData.SupporterTableData = printData.SupporterTableData;
                    PrintNavigationData.NearRelativePairs = printData.NearRelativePairs;

                    // ③ 静默生成全部适用模板（保存落盘，不调打印机）
                    // ⚠️ 必须先清上一户输出：OutputFiles 跨户累积会让本户在 0 文件时误过归档前置
                    output.OutputFiles.Clear();
                    await output.InitializeAsync();
                    await output.PrintAllCoreAsync(interactive: false, printToPrinter: false);

                    if (!output.OutputFiles.Any(f => f.IsCompleted))
                    {
                        failedItems.Add((app.ApplicantName, "未生成任何输出文件（无适用模板或全部失败），未归档"));
                        continue;
                    }

                    // ④ 归档即审核（与单户「完成归档」同服务同语义）
                    var archiveResult = await _applicationService.CompleteArchiveAsync(app.Id, operatorName, CancellationToken);
                    if (archiveResult.IsSuccess)
                    {
                        successCount++;
                        item.IsSelected = false;
                        _logger.LogBusiness("批量归档完成",
                            ("ApplicationId", app.Id),
                            ("ApplicationNo", app.ApplicationNo));
                    }
                    else
                    {
                        failedItems.Add((app.ApplicantName, $"文书已生成，但归档状态更新失败: {archiveResult.Message}"));
                        _logger.Error($"批量归档状态更新失败: ApplicationId={app.Id}, {archiveResult.ErrorCode} {archiveResult.Message}");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error($"批量归档单户异常: ApplicationId={app.Id}, {ex.Message}");
                    failedItems.Add((app.ApplicantName, ex.Message));
                }
                finally
                {
                    BatchProgress = (double)(i + 1) / selected.Count;
                }
            }
        }
        finally
        {
            // PII 释放：本流程不导航输出页，PrintNavigationData 无人代清（类注释契约）
            PrintNavigationData.Clear();
            IsBatchArchiving = false;
            BatchProgressText = string.Empty;
            IsBusy = false;
        }

        // 刷新徽章与当前 Tab 列表（成功户应从「已建档」消失）
        await LoadAllCountsAsync();
        await LoadDataAsync();

        var summary = $"批量归档完成：成功 {successCount} 户";
        if (failedItems.Count > 0)
        {
            summary += $"，失败 {failedItems.Count} 户\n\n" +
                       string.Join("\n", failedItems.Take(8).Select(f => $"{f.Name}：{f.Error}")) +
                       (failedItems.Count > 8 ? $"\n...等共 {failedItems.Count} 户" : "");
            _logger.Warn($"批量归档完成（含失败）: Success={successCount}, Failed={failedItems.Count}");
        }
        else
        {
            _logger.LogBusiness("批量归档全部成功", ("Count", successCount));
        }

        await _dialogService.DisplayAlertAsync("批量归档", summary, "确定");
    }
}
