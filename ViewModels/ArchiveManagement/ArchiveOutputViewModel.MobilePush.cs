using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Domain.Printing;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ArchiveManagement;

/// <summary>
/// 档案输出 · 手机端动作（推送打印）。
/// 手机端无 Office/COM（WordEngine/ExcelEngine 仅 WINDOWS 编译），不能本地生成或直接打印，
/// 因此手机页只做「勾选模板 → 写推送打印队列」，由电脑端打印代理认领执行。
/// 模板清单、份数口径（BaseCopies×倍数）、目录/赡养/近亲属模板的行数据规则与桌面打印路径同源。
/// </summary>
public partial class ArchiveOutputViewModel
{
    /// <summary>全选 / 取消全选（只作用于可打印模板）</summary>
    [RelayCommand]
    private void ToggleSelectAll()
    {
        var applicable = Templates.Where(t => t.IsApplicable).ToList();
        if (applicable.Count == 0) return;

        var selectAll = applicable.Any(t => !t.IsSelected);
        foreach (var template in applicable)
            template.IsSelected = selectAll;
    }

    /// <summary>查看推送打印任务（手机端只看进度，实际打印在电脑端执行）</summary>
    [RelayCommand]
    private Task OpenPrintJobsAsync() => NavigateToPageAsync<Pages.Mobile.MobilePrintJobsPage>("打印任务");

    #region 同时打印已上传照片（逐张选版式）

    /// <summary>是否随档案一并打印申请人的已上传照片。</summary>
    [ObservableProperty]
    private bool _isPrintPhotosOn;

    [ObservableProperty]
    private ObservableCollection<PhotoPrintItem> _photoPrintItems = new();

    /// <summary>版式选项（整页/半页/证件原寸）。</summary>
    public ObservableCollection<PhotoLayoutOption> PhotoLayoutOptions { get; } =
        new(PhotoLayoutConstants.All.Select(a => new PhotoLayoutOption(a.Code, a.Name)));

    public bool HasNoPhotoForPrint => IsPrintPhotosOn && PhotoPrintItems.Count == 0;

    partial void OnIsPrintPhotosOnChanged(bool value)
    {
        OnPropertyChanged(nameof(HasNoPhotoForPrint));
        if (value)
            SafeFireAndForget(LoadPhotosForPrintAsync);
        else
            PhotoPrintItems.Clear();
    }

    /// <summary>按申请人身份证加载已归档照片（无身份证/无照片时列表为空，界面给提示）。</summary>
    private async Task LoadPhotosForPrintAsync()
    {
        try
        {
            PhotoPrintItems.Clear();

            var idCard = PrintNavigationData.FieldData?.GetValueOrDefault(FieldKeys.APPLICANT_ID_CARD);
            if (string.IsNullOrWhiteSpace(idCard))
            {
                OnPropertyChanged(nameof(HasNoPhotoForPrint));
                return;
            }

            var photoService = _serviceProvider.GetRequiredService<Services.Domain.ArchiveManagement.IArchivePhotoService>();
            var result = await photoService.GetByPersonAsync(idCard.Trim(), null, CancellationToken);
            if (result.IsSuccess && result.Value != null)
            {
                foreach (var photo in result.Value)
                {
                    var defaultCode = photo.PhotoCategory == PhotoCategoryConstants.ID_CARD
                        ? PhotoLayoutConstants.ID_CARD
                        : PhotoLayoutConstants.FULL_PAGE;

                    PhotoPrintItems.Add(new PhotoPrintItem
                    {
                        PhotoId = photo.Id,
                        CategoryCode = photo.PhotoCategory,
                        CategoryName = PhotoCategoryConstants.GetDisplayName(photo.PhotoCategory),
                        PhotoName = photo.PhotoName ?? string.Empty,
                        CreatedAtText = photo.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                        SelectedLayout = PhotoLayoutOptions.First(o => o.Code == defaultCode)
                    });
                }
            }

            OnPropertyChanged(nameof(HasNoPhotoForPrint));
        }
        catch (Exception ex)
        {
            _logger.Error($"加载档案照片失败: {ex.Message}");
        }
    }

    /// <summary>把选中照片以 JobKind=Photo 入队（逐张版式），由 PC 端打印代理执行。</summary>
    private async Task<int> EnqueueSelectedPhotosAsync(List<string> failures)
    {
        if (!IsPrintPhotosOn) return 0;

        var selected = PhotoPrintItems.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0) return 0;

        var photoService = _serviceProvider.GetRequiredService<Services.Domain.ArchiveManagement.IArchivePhotoService>();
        var queueService = _serviceProvider.GetRequiredService<IPrintQueueService>();
        var idCard = PrintNavigationData.FieldData?.GetValueOrDefault(FieldKeys.APPLICANT_ID_CARD) ?? string.Empty;
        var applicantName = PrintNavigationData.FieldData?.GetValueOrDefault(FieldKeys.APPLICANT_NAME) ?? string.Empty;

        var okCount = 0;
        foreach (var item in selected)
        {
            try
            {
                var dataResult = await photoService.GetPhotoDataAsync(item.PhotoId, CancellationToken);
                if (dataResult.IsFailure || dataResult.Value == null || dataResult.Value.Length == 0)
                {
                    failures.Add($"照片（{item.CategoryName}）：读取失败");
                    continue;
                }

                var request = new PrintJobRequest
                {
                    BusinessType = PrintNavigationData.BusinessType,
                    BusinessId = PrintNavigationData.BusinessId,
                    Classification = item.CategoryName,
                    ApplicantName = applicantName,
                    ApplicantIdCard = idCard,
                    Copies = 1,
                    IsDuplex = false,
                    JobKind = PrintJobConstants.JobKindPhoto,
                    PhotoData = dataResult.Value,
                    PhotoLayout = item.SelectedLayout?.Code ?? PhotoLayoutConstants.FULL_PAGE,
                    PhotoCategory = item.CategoryCode,
                    RequestedBy = App.CurrentUserId,
                    RequestedByName = App.CurrentUserName
                };

                var enqueue = await queueService.EnqueueAsync(request, CancellationToken);
                if (enqueue.IsSuccess) okCount++;
                else failures.Add($"照片（{item.CategoryName}）：{enqueue.Message}");
            }
            catch (Exception ex)
            {
                failures.Add($"照片（{item.CategoryName}）：{ex.Message}");
            }
        }

        if (okCount > 0)
        {
            _logger.LogBusiness("档案输出-照片随档打印入队",
                ("BusinessType", PrintNavigationData.BusinessType),
                ("PhotoCount", okCount));
        }

        return okCount;
    }

    #endregion

    /// <summary>返回首页（推送完成后离场）：PopToRoot + 恢复窗口标题，与桌面「完成工单」同口径</summary>
    [RelayCommand]
    private async Task GoHomeAsync()
    {
        var navigation = Helpers.WindowNavigator.CurrentNavigation;
        if (navigation == null) return;

        await navigation.PopToRootAsync();
        RestoreWindowTitleFromNavigation();
    }

    /// <summary>
    /// 推送打印：按勾选模板逐个入队（一个模板一个任务），电脑端打印代理轮询执行。
    /// 核查报告依赖电脑端 PDF 服务生成，不能入队（列表内已标注）。
    /// </summary>
    [RelayCommand]
    private async Task PushPrintAsync()
    {
        if (PrintNavigationData.FieldData == null || PrintNavigationData.FieldData.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "没有可推送的打印数据", "确定");
            return;
        }

        var selected = Templates.Where(t => t.IsSelected && t.IsApplicable).ToList();
        var photoSelected = IsPrintPhotosOn && PhotoPrintItems.Any(p => p.IsSelected);

        if (selected.Count == 0 && !photoSelected)
        {
            await _dialogService.DisplayAlertAsync("提示",
                "请勾选要推送打印的模板；或在「同时打印已上传照片」里勾选照片", "确定");
            return;
        }

        var pushable = selected.Where(t => !t.IsVerificationReport).ToList();
        var reportCount = selected.Count - pushable.Count;
        if (pushable.Count == 0 && !photoSelected)
        {
            await _dialogService.DisplayAlertAsync("提示", "核查报告需在电脑端生成，暂不支持推送打印", "确定");
            return;
        }

        await ExecuteAsync(async () =>
        {
            var jobFactory = _serviceProvider.GetRequiredService<IPrintJobFactory>();
            var successCount = 0;
            var failures = new List<string>();

            foreach (var template in pushable)
            {
                try
                {
                    var (fields, tableRows) = await BuildPushPayloadAsync(template);

                    var request = new PrintJobRequest
                    {
                        BusinessType = PrintNavigationData.BusinessType,
                        BusinessId = PrintNavigationData.BusinessId,
                        Classification = PrintNavigationData.Classification,
                        TemplateId = template.TemplateId,
                        TemplateName = template.Name,
                        ApplicantName = fields.GetValueOrDefault(FieldKeys.APPLICANT_NAME),
                        ApplicantIdCard = fields.GetValueOrDefault(FieldKeys.APPLICANT_ID_CARD),
                        // 打印机留空：由电脑端打印代理使用本机默认打印机（手机上的打印机列表与本机无关）
                        PrinterName = null,
                        Copies = Math.Clamp(template.BaseCopies * Copies, 1, 99),
                        IsDuplex = IsDuplex,
                        Fields = fields,
                        TableRows = tableRows
                    };

                    var result = await jobFactory.EnqueueAsync(request, CancellationToken);
                    if (result.IsSuccess) successCount++;
                    else failures.Add($"{template.Name}：{result.Message}");
                }
                catch (Exception ex)
                {
                    failures.Add($"{template.Name}：{ex.Message}");
                }
            }

            // 已上传照片随档打印（每张可单独选版式）
            var photoOk = await EnqueueSelectedPhotosAsync(failures);

            if (successCount > 0 || photoOk > 0)
            {
                _logger.LogBusiness("档案推送打印入队",
                    ("BusinessType", PrintNavigationData.BusinessType),
                    ("BusinessId", PrintNavigationData.BusinessId?.ToString() ?? string.Empty),
                    ("JobCount", successCount),
                    ("PhotoCount", photoOk));
                StatusText = photoOk > 0
                    ? $"已推送 {successCount} 个模板任务、{photoOk} 张照片"
                    : $"已推送 {successCount} 个打印任务";
            }

            var photoHint = photoOk > 0 ? $"，照片 {photoOk} 张" : string.Empty;
            var reportHint = reportCount > 0
                ? $"\n（{reportCount} 个核查报告需在电脑端生成，未推送）"
                : string.Empty;

            if (failures.Count == 0)
            {
                await _dialogService.DisplayAlertAsync("已推送打印",
                    $"共 {successCount} 个模板任务{photoHint}已入队，电脑端登录后自动打印。{reportHint}", "确定");
                return Result.Success();
            }

            await _dialogService.DisplayAlertAsync("部分推送失败",
                $"成功 {successCount} 个模板任务{photoHint}，失败 {failures.Count} 项：\n{string.Join("\n", failures.Take(5))}{reportHint}", "确定");
            return Result.Failure(ErrorCodes.UNKNOWN_ERROR, "部分模板推送打印失败");
        }, "推送打印...");
    }

    /// <summary>
    /// 构建推送任务的字段与表格行：与桌面打印路径（PrintCoreAsync）同口径——
    /// 目录模板换目录字段/会议行，赡养费承诺书用赡养人行，近亲属两表用备案对行。
    /// </summary>
    private async Task<(Dictionary<string, string> Fields, List<Dictionary<string, string>> Rows)> BuildPushPayloadAsync(
        TemplateSelectItem template)
    {
        var fields = new Dictionary<string, string>(PrintNavigationData.FieldData, StringComparer.Ordinal);
        var rows = PrintNavigationData.TableData ?? new List<Dictionary<string, string>>();

        if (template.IsDirectoryTemplate)
        {
            var timeline = _timelineService.GetTimelineForApplicationDate(
                await ResolveApplicationDateForTimelineAsync(), TimelineType.BusinessProcess);
            fields = await BuildDirectoryFieldsAsync(fields);
            rows = BuildDirectoryRows(timeline.MeetingDate);
        }
        else if (template.Name.Contains("赡养费承诺书"))
        {
            rows = PrintNavigationData.SupporterTableData ?? new List<Dictionary<string, string>>();
        }
        else if (template.Name.Contains("近亲属"))
        {
            rows = PrintNavigationData.NearRelativePairs ?? new List<Dictionary<string, string>>();
        }

        return (fields, rows);
    }
}
