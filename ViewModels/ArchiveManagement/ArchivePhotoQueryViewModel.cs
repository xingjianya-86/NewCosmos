using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.Printing;
using NewCosmos.Services.Platform;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Shared;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ArchiveManagement;

/// <summary>
/// 档案照片查询/导出（桌面端）：按身份证或备注关键词 + 类别检索。
/// 列表带缩略图（异步加载，WebP 统一转 JPEG 适配 WinUI）、支持勾选多张、预览单张；
/// 导出支持「导出全部 / 导出选中 / 另存为…（自选目录）」，文件名按 2026-11-04 命名规则
/// （姓名_身份证后4位_类别_编号），旧数据无名时回退原格式。
/// 另提供「历史照片转 WebP」批量维护入口（旧 JPEG 统一压缩为 WebP，单张 ≤500KB）。
/// 「推送打印」把勾选照片以 JobKind=Photo 写入打印队列，由本机 PC 打印代理认领执行
/// （与手机端照片打印同链路，版式逐批选择）。
/// </summary>
public partial class ArchivePhotoQueryViewModel : PagedSearchViewModelBase
{
    private readonly IArchivePhotoService _photoService;
    private readonly IArchivePhotoWebPMigrationService _webpMigrationService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IPrintQueueService _printQueueService;
    private readonly INewPermissionService _permissionService;
    private CancellationTokenSource? _convertCts;
    private CancellationTokenSource? _thumbCts;
    private bool _autoSearchOnAppear;

    /// <summary>从采集页/补传页返回时是否需要无条件重查（刚插入的照片要立即出现在列表）。</summary>
    private bool _returnRefreshNeeded;

    /// <summary>表单带入的申请 ID（「打开采集页」优先用，保持照片与当前申请的关联）。</summary>
    private long? _prefillApplicationId;

    /// <summary>表单带入的识别回填上下文（透传采集页，保住「拍照 → OCR → 回填表单」能力）。</summary>
    private IOcrAssignContext? _assignContext;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    /// <summary>批量转换进度（绑定 ProgressPopupView）。</summary>
    public ProgressViewModel Progress { get; } = new();

    [ObservableProperty]
    private ObservableCollection<ArchivePhotoListItem> _photos = new();

    [ObservableProperty]
    private string _keyword = string.Empty;

    /// <summary>类别代码，空 = 全部。</summary>
    [ObservableProperty]
    private string _selectedCategoryCode = string.Empty;

    public ObservableCollection<PhotoCategoryFilterOption> CategoryOptions { get; } = new();

    [ObservableProperty]
    private PhotoCategoryFilterOption? _selectedCategoryOption;

    /// <summary>导出格式选项：默认 WebP（库内原图、体积小）；JPG 供民政等不支持 WebP 的外部系统。</summary>
    public ObservableCollection<ExportFormatOption> ExportFormatOptions { get; } = new();

    [ObservableProperty]
    private ExportFormatOption? _selectedExportFormatOption;

    /// <summary>当前导出格式代码（webp / jpeg），三个导出入口与手机端「更多」菜单共用。</summary>
    public string ExportFormat => SelectedExportFormatOption?.Code ?? PhotoFormatConstants.WebP;

    partial void OnSelectedExportFormatOptionChanged(ExportFormatOption? value)
        => OnPropertyChanged(nameof(ExportFormat));

    [ObservableProperty]
    private ArchivePhotoListItem? _selectedPhoto;

    [ObservableProperty]
    private ImageSource? _previewImage;

    [ObservableProperty]
    private string _previewInfo = "选择左侧照片查看预览";

    /// <summary>手机端全屏预览是否打开（桌面端预览走右侧面板，此层始终隐藏）。</summary>
    [ObservableProperty]
    private bool _isPreviewOpen;

    /// <summary>手机端全屏预览大图加载中。</summary>
    [ObservableProperty]
    private bool _isPreviewLoading;

    /// <summary>是否已取到预览大图。</summary>
    public bool HasPreviewImage => PreviewImage is not null;

    /// <summary>预览空态（没取到图也没在加载：读取失败/照片已删）。</summary>
    public bool ShowPreviewEmpty => !HasPreviewImage && !IsPreviewLoading;

    partial void OnPreviewImageChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(HasPreviewImage));
        OnPropertyChanged(nameof(ShowPreviewEmpty));
    }

    partial void OnIsPreviewLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowPreviewEmpty));

    /// <summary>待转 WebP 的历史照片数。</summary>
    [ObservableProperty]
    private int _pendingWebPCount;

    /// <summary>是否正在批量转换。</summary>
    [ObservableProperty]
    private bool _isConverting;

    /// <summary>已勾选张数（多选导出用）。</summary>
    public int SelectedCount => Photos.Count(p => p.IsSelected);

    public string ExportSelectedText => SelectedCount > 0 ? $"导出选中（{SelectedCount}）" : "导出选中";

    public string ConvertWebPButtonText => PendingWebPCount > 0
        ? $"历史照片转 WebP（剩余 {PendingWebPCount} 张）"
        : "历史照片已全部为 WebP";

    /// <summary>转换按钮可用性：有待转记录且当前未在转换。</summary>
    public bool CanConvertWebP => !IsConverting && PendingWebPCount > 0;

    partial void OnPendingWebPCountChanged(int value)
    {
        OnPropertyChanged(nameof(ConvertWebPButtonText));
        OnPropertyChanged(nameof(CanConvertWebP));
    }

    partial void OnIsConvertingChanged(bool value)
        => OnPropertyChanged(nameof(CanConvertWebP));

    public ArchivePhotoQueryViewModel(
        IArchivePhotoService photoService,
        IArchivePhotoWebPMigrationService webpMigrationService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        IPrintQueueService printQueueService,
        INewPermissionService permissionService)
    {
        _photoService = photoService;
        _webpMigrationService = webpMigrationService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _printQueueService = printQueueService;
        _permissionService = permissionService;

        PageSize = 20;
        Title = "档案照片查询";

        CategoryOptions.Add(new PhotoCategoryFilterOption(string.Empty, "全部类别"));
        foreach (var (code, name) in PhotoCategoryConstants.All)
            CategoryOptions.Add(new PhotoCategoryFilterOption(code, name));
        _selectedCategoryOption = CategoryOptions[0];

        // 导出格式（默认 WebP；JPG 供民政等外部系统）——顺序即默认优先
        ExportFormatOptions.Add(new ExportFormatOption(PhotoFormatConstants.WebP, "WebP（原图）"));
        ExportFormatOptions.Add(new ExportFormatOption(PhotoFormatConstants.Jpeg, "JPG（外部系统兼容）"));
        _selectedExportFormatOption = ExportFormatOptions[0];
    }

    /// <summary>业务页/表单「照片」入口注入：预填关键词（身份证号）、申请 ID 与识别上下文，进页自动检索。</summary>
    public void ApplyParameter(ArchivePhotoQueryParameter parameter)
    {
        if (!string.IsNullOrWhiteSpace(parameter.Keyword))
            Keyword = parameter.Keyword.Trim();
        _autoSearchOnAppear = parameter.AutoSearch;
        _prefillApplicationId = parameter.ApplicationId;
        _assignContext = parameter.AssignContext;
    }

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await RefreshPendingCountAsync();

        // 业务页带身份证进入 → 自动检索；从采集页/补传页返回 → 无条件重查（新插入照片要立即可见）
        if (_autoSearchOnAppear || _returnRefreshNeeded)
        {
            _autoSearchOnAppear = false;
            _returnRefreshNeeded = false;
            PageIndex = 1;
            await LoadDataAsync();
        }
    }

    partial void OnSelectedCategoryOptionChanged(PhotoCategoryFilterOption? value)
    {
        if (value == null) return;
        SelectedCategoryCode = value.Code;
        PageIndex = 1;
        SafeFireAndForget(LoadDataAsync);
    }

    /// <summary>按当前关键词/类别检索（回车或「查询」按钮触发）。</summary>
    [RelayCommand]
    private async Task ApplySearchAsync()
    {
        PageIndex = 1;
        await LoadDataAsync();
    }

    partial void OnSelectedPhotoChanged(ArchivePhotoListItem? value)
    {
        // 桌面端：右侧预览面板；手机端：预览列已隐藏，选中即弹全屏预览层
        var phone = DeviceInfo.Idiom == DeviceIdiom.Phone;

        if (value == null)
        {
            PreviewImage = null;
            IsPreviewLoading = false;
            IsPreviewOpen = false;
            PreviewInfo = phone ? "点选列表中的照片放大查看" : "选择左侧照片查看预览";
            return;
        }

        var photo = value.Photo;
        PreviewInfo = $"{photo.PhotoCategoryName} · {DataMasker.MaskIdCard(photo.ApplicantIdCard)} · {photo.CreatedAt:yyyy-MM-dd HH:mm}";
        PreviewImage = null;

        if (phone)
        {
            IsPreviewOpen = true;
            IsPreviewLoading = true;
        }

        SafeFireAndForget(async () =>
        {
            try
            {
                var data = await _photoService.GetPhotoDataAsync(photo.Id, CancellationToken);
                if (data.IsFailure || data.Value == null || data.Value.Length == 0)
                {
                    PreviewInfo = "照片读取失败（可能已被删除）";
                    return;
                }

                var bytes = data.Value;
                var format = ImageTranscoder.DetectFormat(bytes);
                if (string.IsNullOrEmpty(format))
                    format = photo.ImageFormat;

                // WinUI 对 WebP 支持不一致：预览统一先转 JPEG（不改变库内格式）。
                var displayBytes = bytes;
                if (format == PhotoFormatConstants.WebP
                    && !ImageTranscoder.TryToJpeg(bytes, out displayBytes))
                {
                    PreviewInfo = "照片预览失败（WebP 转码失败）";
                    return;
                }

                var source = ImageSource.FromStream(() => new MemoryStream(displayBytes));
                await MainThread.InvokeOnMainThreadAsync(() => PreviewImage = source);
            }
            catch (OperationCanceledException)
            {
                // 离开页面/新操作顶掉：忽略
            }
            catch (Exception ex)
            {
                _logger.Warn($"照片预览加载失败 Id={photo.Id}: {ex.Message}");
                PreviewInfo = "照片预览失败";
            }
            finally
            {
                IsPreviewLoading = false;
            }
        });
    }

    /// <summary>关闭手机端全屏预览并清空选中（下次点同一行可再次打开）。</summary>
    [RelayCommand]
    private void ClosePreview()
    {
        IsPreviewOpen = false;
        IsPreviewLoading = false;
        PreviewImage = null;
        SelectedPhoto = null;
    }

    /// <summary>
    /// 手机端「更多」菜单：把桌面端摆在查询条上的次要功能收进 ActionSheet 分发
    /// （导出全部 / 另存为 / 清空勾选 / 转 WebP / 打开采集页 / 照片补传 / 导出格式）。
    /// </summary>
    [RelayCommand]
    private async Task MoreOptionsAsync()
    {
        var buttons = new List<string> { "导出全部", "另存为…", "清空勾选" };
        if (CanConvertWebP)
            buttons.Add($"历史照片转 WebP（剩余 {PendingWebPCount} 张）");
        buttons.Add("打开采集页");
        buttons.Add("照片补传");
        buttons.Add($"导出格式（当前：{PhotoFormatConstants.GetDisplayName(ExportFormat)}）");

        var choice = await _dialogService.DisplayActionSheetAsync("更多操作", "取消", null, buttons.ToArray());
        if (string.IsNullOrEmpty(choice) || choice == "取消") return;

        if (choice == "导出全部") await ExportAllAsync();
        else if (choice == "另存为…") await ExportToFolderAsync();
        else if (choice == "清空勾选") ClearSelection();
        else if (choice.StartsWith("历史照片转", StringComparison.Ordinal)) await ConvertLegacyPhotosAsync();
        else if (choice == "打开采集页") await OpenCapturePageAsync();
        else if (choice == "照片补传") await OpenBackfillPageAsync();
        else if (choice.StartsWith("导出格式", StringComparison.Ordinal)) await PickExportFormatAsync();
    }

    /// <summary>选导出格式（手机端在「更多」里；桌面端用查询条上的格式下拉）。</summary>
    private async Task PickExportFormatAsync()
    {
        var buttons = ExportFormatOptions.Select(o => o.Name).ToArray();
        var current = PhotoFormatConstants.GetDisplayName(ExportFormat);
        var choice = await _dialogService.DisplayActionSheetAsync(
            $"导出格式（当前 {current}）", "取消", null, buttons);
        if (string.IsNullOrEmpty(choice) || choice == "取消") return;

        foreach (var option in ExportFormatOptions)
        {
            if (option.Name == choice)
            {
                SelectedExportFormatOption = option;
                _logger.LogBusiness("档案照片-导出格式切换", ("Format", option.Code));
                return;
            }
        }
    }

    protected override async Task LoadDataAsync()
    {
        await ExecuteAsync(async () =>
        {
            var result = await _photoService.GetPhotosPagedAsync(
                Keyword, SelectedCategoryCode, PageIndex, PageSize, CancellationToken);

            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);

            Photos.Clear();
            foreach (var photo in result.Value?.Items ?? new List<ArchivePhoto>())
                Photos.Add(new ArchivePhotoListItem(photo, NotifySelectionChanged));

            TotalCount = result.Value?.TotalCount ?? 0;
            SelectedPhoto = null;
            NotifySelectionChanged();
            return Result.Success();
        }, "查询照片...");

        // 缩略图后台加载（不阻塞列表渲染）
        SafeFireAndForget(LoadThumbnailsAsync);
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(ExportSelectedText));
        OnPropertyChanged(nameof(PushPrintText));
    }

    /// <summary>为当前页照片异步生成缩略图（4 路并发，切页/重新查询时取消上一批）。</summary>
    private async Task LoadThumbnailsAsync()
    {
        _thumbCts?.Cancel();
        _thumbCts?.Dispose();
        var cts = new CancellationTokenSource();
        _thumbCts = cts;
        var token = cts.Token;

        var items = Photos.ToList();
        using var gate = new SemaphoreSlim(4);
        var tasks = items.Select(async item =>
        {
            await gate.WaitAsync(token);
            try
            {
                var data = await _photoService.GetPhotoDataAsync(item.Id, token);
                if (data.IsFailure || data.Value == null || data.Value.Length == 0) return;

                // 桌面端输出 JPEG（WinUI 对 WebP 支持不稳定）
                var thumb = ImageCompressor.CreateThumbnail(data.Value, maxEdge: 160, jpeg: true);
                if (thumb == null || token.IsCancellationRequested) return;

                MainThread.BeginInvokeOnMainThread(() =>
                    item.Thumbnail = ImageSource.FromStream(() => new MemoryStream(thumb)));
            }
            catch (OperationCanceledException)
            {
                // 切页/重新查询：忽略
            }
            catch (Exception ex)
            {
                _logger.Warn($"照片缩略图加载失败 Id={item.Id}: {ex.Message}");
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks);
    }

    /// <summary>刷新待转 WebP 的历史照片数（页面进入/转换完成后调用）。</summary>
    private async Task RefreshPendingCountAsync()
    {
        var result = await _webpMigrationService.CountPendingAsync(CancellationToken);
        if (result.IsSuccess)
            PendingWebPCount = result.Value;
    }

    /// <summary>
    /// 历史照片批量转 WebP：确认 → 进度 → 汇总；可取消、可重复执行（已完成/已转换的自动跳过）。
    /// </summary>
    [RelayCommand]
    private async Task ConvertLegacyPhotosAsync()
    {
        if (IsConverting) return;

        if (PendingWebPCount <= 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "没有需要转换的历史照片。", "确定");
            return;
        }

        var confirmed = await _dialogService.DisplayAlertAsync(
            "历史照片转 WebP",
            $"将把 {PendingWebPCount} 张旧照片压缩为 WebP（单张 ≤500KB）。\n\n" +
            "转换会更新 photo_data，请先备份数据库；转换过程可取消，已完成的会保留。是否继续？",
            "开始转换", "取消");
        if (!confirmed) return;

        IsConverting = true;
        _convertCts = new CancellationTokenSource();
        Progress.InitializeSteps(new List<string> { "历史照片转 WebP" });
        Progress.ProgressText = "准备转换...";

        try
        {
            var progress = new Progress<PhotoWebPMigrationProgress>(p =>
            {
                Progress.ProgressValue = p.Total > 0 ? (double)p.Processed / p.Total : 0;
                Progress.ProgressText =
                    $"已处理 {p.Processed}/{p.Total}，成功 {p.Converted}，跳过 {p.Skipped}，失败 {p.Failed}";
            });

            var result = await _webpMigrationService.ConvertAllAsync(progress, _convertCts.Token);
            if (result.IsFailure)
            {
                Progress.CanCloseProgress = true;
                await _dialogService.DisplayAlertAsync("转换失败", result.Message ?? "转换失败", "确定");
                return;
            }

            var summary = result.Value;
            Progress.ProgressValue = 1;
            Progress.ProgressText = "转换完成";
            Progress.MarkAllCompleted();

            var failedText = summary.Failed > 0
                ? $"\n失败 {summary.Failed} 张，已保留原格式，可修复后重试。"
                : string.Empty;
            var cancelledText = summary.Cancelled
                ? "\n已取消，可稍后继续。"
                : string.Empty;

            await _dialogService.DisplayAlertAsync(
                "转换完成",
                $"成功 {summary.Converted} 张，跳过 {summary.Skipped} 张，失败 {summary.Failed} 张。" +
                $"{failedText}{cancelledText}\n\n" +
                "全部完成后建议执行 VACUUM (ANALYZE) nc_biz_archive_photos。",
                "确定");
        }
        catch (OperationCanceledException)
        {
            Progress.CanCloseProgress = true;
            Progress.ProgressText = "已取消";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "历史照片转 WebP 失败");
            Progress.CanCloseProgress = true;
            await _dialogService.DisplayAlertAsync("转换失败", ex.Message, "确定");
        }
        finally
        {
            IsConverting = false;
            _convertCts?.Dispose();
            _convertCts = null;
            await RefreshPendingCountAsync();
            await LoadDataAsync();
        }
    }

    /// <summary>取消批量转换（已完成的记录保留，可稍后重跑）。</summary>
    [RelayCommand]
    private void CancelConvert() => _convertCts?.Cancel();

    #region 勾选

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in Photos) item.IsSelected = true;
        NotifySelectionChanged();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in Photos) item.IsSelected = false;
        NotifySelectionChanged();
    }

    #endregion

    #region 导出

    /// <summary>导出当前查询结果（最多 500 张）到输出根。</summary>
    [RelayCommand]
    private async Task ExportAllAsync()
    {
        await ExecuteAsync(async () =>
        {
            const int maxExport = 500;
            var root = Path.Combine(OutputPathHelper.OutputRoot, "档案照片");
            var exported = 0;
            var page = 1;

            while (exported < maxExport)
            {
                var pageResult = await _photoService.GetPhotosPagedAsync(
                    Keyword, SelectedCategoryCode, page, 100, CancellationToken);
                if (pageResult.IsFailure || pageResult.Value == null || pageResult.Value.Items.Count == 0)
                    break;

                foreach (var photo in pageResult.Value.Items)
                {
                    if (exported >= maxExport) break;
                    if (await ExportOneAsync(photo, Path.Combine(root, IdCardTail(photo)), CancellationToken))
                        exported++;
                }

                if (pageResult.Value.Items.Count < 100) break;
                page++;
            }

            _logger.LogBusiness("档案照片导出", ("Count", exported), ("Format", ExportFormat), ("Root", root));

            await _dialogService.DisplayAlertAsync("导出完成",
                exported == 0
                    ? "没有可导出的照片"
                    : $"已导出 {exported} 张照片到：\n{root}",
                "确定");
            return Result.Success();
        }, "导出照片...");
    }

    /// <summary>导出勾选的照片（多选）。</summary>
    [RelayCommand]
    private async Task ExportSelectedAsync()
    {
        var checkedItems = Photos.Where(p => p.IsSelected).ToList();
        if (checkedItems.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先勾选要导出的照片（行首复选框）", "确定");
            return;
        }

        var root = Path.Combine(OutputPathHelper.OutputRoot, "档案照片");
        await ExecuteAsync(async () =>
        {
            var exported = 0;
            foreach (var item in checkedItems)
            {
                if (await ExportOneAsync(item.Photo, Path.Combine(root, IdCardTail(item.Photo)), CancellationToken))
                    exported++;
            }

            _logger.LogBusiness("档案照片导出选中", ("Count", exported), ("Format", ExportFormat), ("Root", root));
            await _dialogService.DisplayAlertAsync("导出完成",
                $"已导出 {exported}/{checkedItems.Count} 张照片到：\n{root}", "确定");
            return Result.Success();
        }, "导出选中照片...");
    }

    /// <summary>另存为：选目录 → 导出勾选照片（未勾选时导出当前查询全部，最多 500 张）。</summary>
    [RelayCommand]
    private async Task ExportToFolderAsync()
    {
        var picker = _serviceProvider.GetService<IFolderPickerService>();
        if (picker == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "当前平台不支持选择文件夹", "确定");
            return;
        }

        var picked = await picker.PickFolderAsync("选择照片保存位置", OutputPathHelper.OutputRoot);
        if (picked.Status == FolderPickStatus.Canceled) return;
        if (picked.Status == FolderPickStatus.Failed || string.IsNullOrWhiteSpace(picked.Path))
        {
            await _dialogService.DisplayAlertAsync("保存失败", picked.Error ?? "无法访问所选文件夹", "确定");
            return;
        }

        var targetDir = picked.Path!;
        var checkedItems = Photos.Where(p => p.IsSelected).ToList();

        const int maxExport = 500;
        await ExecuteAsync(async () =>
        {
            var exported = 0;
            var failed = 0;

            if (checkedItems.Count > 0)
            {
                foreach (var item in checkedItems)
                {
                    if (await ExportOneAsync(item.Photo, targetDir, CancellationToken)) exported++;
                    else failed++;
                }
            }
            else
            {
                var page = 1;
                while (exported < maxExport)
                {
                    var pageResult = await _photoService.GetPhotosPagedAsync(
                        Keyword, SelectedCategoryCode, page, 100, CancellationToken);
                    if (pageResult.IsFailure || pageResult.Value == null || pageResult.Value.Items.Count == 0)
                        break;

                    foreach (var photo in pageResult.Value.Items)
                    {
                        if (exported >= maxExport) break;
                        if (await ExportOneAsync(photo, targetDir, CancellationToken)) exported++;
                        else failed++;
                    }

                    if (pageResult.Value.Items.Count < 100) break;
                    page++;
                }
            }

            _logger.LogBusiness("档案照片另存为",
                ("Count", exported), ("Failed", failed), ("Format", ExportFormat), ("Target", targetDir));

            var scope = checkedItems.Count > 0 ? $"勾选 {checkedItems.Count} 张" : "当前查询结果";
            await _dialogService.DisplayAlertAsync("保存完成",
                $"已保存 {exported} 张（{scope}）到：\n{targetDir}" +
                (failed > 0 ? $"\n失败 {failed} 张（详见日志）" : string.Empty),
                "确定");
            return Result.Success();
        }, "保存照片...");
    }

    /// <summary>导出单张到指定目录；同名文件自动追加 Id 防覆盖。返回是否成功。
    /// 导出格式按 <see cref="ExportFormat"/>（默认 WebP 原样；选 JPG 时转码，转码失败显式判失败，不静默回退）。</summary>
    private async Task<bool> ExportOneAsync(ArchivePhoto photo, string dir, CancellationToken ct)
    {
        try
        {
            var data = await _photoService.GetPhotoDataAsync(photo.Id, ct);
            if (data.IsFailure || data.Value == null || data.Value.Length == 0)
            {
                _logger.Warn($"照片导出失败（读取为空）Id={photo.Id}: {data.Message}");
                return false;
            }

            var bytes = data.Value;
            var format = ImageTranscoder.DetectFormat(bytes);
            if (string.IsNullOrEmpty(format))
                format = photo.ImageFormat;

            // 导出为 JPG：已是 JPEG 透传，其余（WebP/PNG）转 JPEG q90
            if (ExportFormat == PhotoFormatConstants.Jpeg && format != PhotoFormatConstants.Jpeg)
            {
                if (!ImageTranscoder.TryToJpeg(bytes, out var jpegBytes))
                {
                    _logger.Warn($"照片导出转 JPG 失败 Id={photo.Id}（原格式 {format}）");
                    return false;
                }

                bytes = jpegBytes;
                format = PhotoFormatConstants.Jpeg;
            }

            Directory.CreateDirectory(dir);

            var filePath = Path.Combine(dir, BuildFileName(photo, format));
            if (File.Exists(filePath))
                filePath = Path.Combine(dir, BuildFileName(photo, format, forceIdSuffix: true));

            await File.WriteAllBytesAsync(filePath, bytes, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warn($"照片导出失败 Id={photo.Id}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 导出文件名（2026-11-04 命名规则）：优先用 PhotoName（姓名_后4位_类别_编号），
    /// 旧数据无名时回退 {类别}_{时间}_{Id}；forceIdSuffix 用于同名防覆盖。
    /// </summary>
    private static string BuildFileName(ArchivePhoto photo, string format, bool forceIdSuffix = false)
    {
        var ext = PhotoFormatConstants.GetExtension(format);
        var baseName = PhotoNameBuilder.SanitizeFileName(photo.PhotoName);

        if (string.IsNullOrEmpty(baseName))
            baseName = $"{photo.PhotoCategory}_{photo.CreatedAt:yyyyMMdd_HHmmss}_{photo.Id}";
        else if (forceIdSuffix)
            baseName = $"{baseName}_{photo.Id}";

        return baseName + ext;
    }

    private static string IdCardTail(ArchivePhoto photo)
        => photo.ApplicantIdCard.Length >= 6 ? photo.ApplicantIdCard[^6..] : photo.ApplicantIdCard;

    #endregion

    #region 插入（PC 端：页内选图 / 拖拽 / 打开采集页 / 照片补传）

    /// <summary>插入来源（日志口径）。</summary>
    private const string InsertSourcePick = "选图";
    private const string InsertSourceDrop = "拖拽";

    /// <summary>
    /// 插入照片：先锁定"已存在被查询的人"（18 位身份证 + 低收入/高龄/临时救助档案中存在）
    /// → 选类别 → 本机多选图片 → 走与手机端采集同一保存入口入库 → 立即刷新。
    /// 归属人解析不出或档案不存在时显式拒绝，不静默放行。
    /// </summary>
    [RelayCommand]
    private async Task InsertPhotosAsync()
    {
        try
        {
            var owner = await ResolveOwnerForInsertAsync();
            if (owner == null) return;

            var category = await ResolveInsertCategoryAsync();
            if (category == null) return; // 用户取消选类别

            var picked = await MediaPicker.Default.PickPhotosAsync();
            if (picked == null) return;

            var files = new List<byte[]>();
            foreach (var file in picked)
            {
                try
                {
                    await using var stream = await file.OpenReadAsync();
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms);
                    if (ms.Length > 0) files.Add(ms.ToArray());
                }
                catch (Exception ex)
                {
                    _logger.Warn($"读取所选图片失败 {file.FileName}: {ex.Message}");
                }
            }

            await InsertBytesAsync(owner, category, files, InsertSourcePick);
        }
        catch (FeatureNotSupportedException)
        {
            await _dialogService.DisplayAlertAsync("提示", "当前平台不支持选图", "确定");
        }
        catch (PermissionException)
        {
            await _dialogService.DisplayAlertAsync("权限不足", "请在系统设置中允许相册访问权限", "确定");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "插入照片失败");
            await _dialogService.DisplayAlertAsync("错误", $"插入失败：{ex.Message}", "确定");
        }
    }

    /// <summary>
    /// 拖拽图片文件插入（Windows 拖放入口，由页面 code-behind 在 UI 线程回调）。
    /// 与「插入照片」共用同一套归属人校验、类别判定与入库逻辑。
    /// </summary>
    public async Task InsertFromDroppedPathsAsync(IReadOnlyList<string>? paths)
    {
        try
        {
            if (paths == null || paths.Count == 0)
            {
                await _dialogService.DisplayAlertAsync("提示",
                    "未检测到可插入的图片文件（支持 jpg / jpeg / png / webp / bmp / gif）", "确定");
                return;
            }

            var owner = await ResolveOwnerForInsertAsync();
            if (owner == null) return;

            var category = await ResolveInsertCategoryAsync();
            if (category == null) return;

            var files = new List<byte[]>(paths.Count);
            foreach (var path in paths)
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(path);
                    if (bytes.Length > 0) files.Add(bytes);
                }
                catch (Exception ex)
                {
                    _logger.Warn($"读取拖入图片失败 {Path.GetFileName(path)}: {ex.Message}");
                }
            }

            await InsertBytesAsync(owner, category, files, InsertSourceDrop);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "拖拽插入照片失败");
            await _dialogService.DisplayAlertAsync("错误", $"插入失败：{ex.Message}", "确定");
        }
    }

    /// <summary>
    /// 打开手机端采集页（复用 MobileArchivePhotoPage：拍照 / 相册多选 / 分类入库同一套逻辑），
    /// 归属人校验与「插入照片」同口径；表单带入的申请 ID 与识别上下文一并透传（保住 OCR 回填）；
    /// 返回本页时自动重查。
    /// </summary>
    [RelayCommand]
    private async Task OpenCapturePageAsync()
    {
        try
        {
            var owner = await ResolveOwnerForInsertAsync();
            if (owner == null) return;

            _returnRefreshNeeded = true;
            await NavigateToPageAsync<Pages.Mobile.MobileArchivePhotoPage, ArchivePhotoParameter>(
                new ArchivePhotoParameter(
                    owner.IdCard,
                    _prefillApplicationId ?? owner.ApplicationId,
                    owner.PersonName,
                    AssignContext: _assignContext));
        }
        catch (Exception ex)
        {
            _returnRefreshNeeded = false;
            _logger.LogError(ex, "打开档案照片采集页失败");
            await _dialogService.DisplayAlertAsync("错误", $"打开采集页失败：{ex.Message}", "确定");
        }
    }

    /// <summary>
    /// 打开照片补传页（复用 MobilePhotoBackfillPage：三域搜人 → 勾选 → 连续采集），返回本页时自动重查。
    /// </summary>
    [RelayCommand]
    private async Task OpenBackfillPageAsync()
    {
        try
        {
            _returnRefreshNeeded = true;
            await NavigateToPageAsync<Pages.Mobile.MobilePhotoBackfillPage>("照片补传");
        }
        catch (Exception ex)
        {
            _returnRefreshNeeded = false;
            _logger.LogError(ex, "打开照片补传页失败");
            await _dialogService.DisplayAlertAsync("错误", $"打开补传页失败：{ex.Message}", "确定");
        }
    }

    /// <summary>
    /// 锁定插入归属人：关键词为合法身份证用之，否则用"当前查询结果同属一人"（见 <see cref="PhotoOwnerResolver"/>）；
    /// 再查库确认该身份证确实存在于三类业务档案，取回姓名与代表业务 ID。任一步不通过都给出可执行的提示。
    /// </summary>
    private async Task<PhotoOwner?> ResolveOwnerForInsertAsync()
    {
        var idCard = PhotoOwnerResolver.Resolve(Keyword, Photos.Select(p => p.ApplicantIdCard));
        if (string.IsNullOrEmpty(idCard))
        {
            await _dialogService.DisplayAlertAsync("请先指定档案人员",
                "插入的照片必须挂到已存在的档案人员名下。\n\n" +
                "请先在查询框输入该人员的 18 位身份证号并点「查询」" +
                "（或让查询结果只显示这一个人），再执行插入。", "确定");
            return null;
        }

        var result = await _photoService.ResolveOwnerAsync(idCard, CancellationToken);
        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("无法插入", result.Message ?? "档案人员不存在", "确定");
            return null;
        }

        return result.Value;
    }

    /// <summary>
    /// 插入类别：查询条已选具体类别直接用；选的是「全部类别」则弹类别选择（与手机端采集同一套类别常量）。
    /// 用户取消返回 null。
    /// </summary>
    private async Task<string?> ResolveInsertCategoryAsync()
    {
        if (!string.IsNullOrEmpty(SelectedCategoryCode)) return SelectedCategoryCode;

        var buttons = PhotoCategoryConstants.All.Select(a => a.Name).ToArray();
        var choice = await _dialogService.DisplayActionSheetAsync("选择照片类别", "取消", null, buttons);
        if (string.IsNullOrEmpty(choice) || choice == "取消") return null;

        foreach (var (code, name) in PhotoCategoryConstants.All)
        {
            if (name == choice) return code;
        }

        return PhotoCategoryConstants.OTHER_CERT;
    }

    /// <summary>批量入库 → 记业务日志 → 刷新列表与待转计数 → 汇总提示（成功/失败张数 + 首个原因）。</summary>
    private async Task InsertBytesAsync(PhotoOwner owner, string category, List<byte[]> files, string source)
    {
        if (files.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "没有可插入的图片文件", "确定");
            return;
        }

        var result = await ExecuteAsync(async () =>
        {
            var insert = await _photoService.InsertPhotosAsync(owner, category, files, CancellationToken);
            if (insert.IsFailure)
                return Result.Failure<BatchPhotoInsertResult>(insert.ErrorCode!, insert.Message!);

            _logger.LogBusiness("档案照片插入",
                ("Source", source),
                ("Applicant", DataMasker.MaskIdCard(owner.IdCard)),
                ("Category", category),
                ("Total", insert.Value.Total),
                ("Succeeded", insert.Value.Succeeded),
                ("Failed", insert.Value.Failed));
            return Result.Success(insert.Value);
        }, files.Count > 1 ? $"插入 {files.Count} 张照片..." : "插入照片...");

        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("插入失败", result.Message ?? "插入失败", "确定");
            return;
        }

        var summary = result.Value;
        var catName = PhotoCategoryConstants.GetDisplayName(category);
        var message = summary.Failed == 0
            ? $"已插入 {summary.Succeeded} 张照片（{catName}）到 {owner.Summary}"
            : $"已插入 {summary.Succeeded} 张，失败 {summary.Failed} 张（{catName}）到 {owner.Summary}" +
              (string.IsNullOrWhiteSpace(summary.FirstError) ? string.Empty : $"\n原因：{summary.FirstError}");

        await LoadDataAsync();
        await RefreshPendingCountAsync();
        await _dialogService.DisplayAlertAsync(summary.HasSuccess ? "插入完成" : "插入失败", message, "确定");
    }

    #endregion

    #region 推送打印

    /// <summary>推送打印按钮文案（勾选时带张数，与「导出选中」同款反馈）。</summary>
    public string PushPrintText => SelectedCount > 0 ? $"推送打印（{SelectedCount}）" : "推送打印";

    /// <summary>
    /// 推送打印勾选照片：权限门控（复用 PRINT_FAMILYAPPLICATION，与手机端照片打印同口径、不新增权限种子）
    /// → 选版式（一次选择应用到本批；「按默认」逐张按类别取 证件原寸/整页）→ 逐张入队
    /// （JobKind=Photo，照片二进制内联在队列里）→ 本机 PC 打印代理认领后用 Word COM 排版打印。
    /// 打印机留空：代理用本机默认打印机。
    /// </summary>
    [RelayCommand]
    private async Task PushPrintSelectedAsync()
    {
        var checkedItems = Photos.Where(p => p.IsSelected).ToList();
        if (checkedItems.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先勾选要推送打印的照片（行首复选框）", "确定");
            return;
        }

        // 权限门控：复用「家庭收入档案打印」权限码（与手机端照片打印同源）
        var userId = App.CurrentUserId;
        if (userId.HasValue)
        {
            var allowed = await _permissionService.HasPermissionAsync(
                userId.Value, PermissionCodes.PRINT_FAMILYAPPLICATION, CancellationToken);
            if (!allowed)
            {
                await _dialogService.DisplayAlertAsync("权限不足",
                    "您没有打印权限（PRINT_FAMILYAPPLICATION），请联系管理员", "确定");
                return;
            }
        }

        // 选版式：按默认（逐张按类别）或强制统一版式；取消则中止
        var buttons = new[] { "按默认（证件原寸/整页）" }
            .Concat(PhotoLayoutConstants.All.Select(a => a.Name))
            .ToArray();
        var choice = await _dialogService.DisplayActionSheetAsync(
            "选择打印版式", "取消", null, buttons);
        if (choice == null || choice == "取消") return;

        var forcedLayout = choice switch
        {
            "整页" => (string?)PhotoLayoutConstants.FULL_PAGE,
            "半页" => PhotoLayoutConstants.HALF_PAGE,
            "证件原寸" => PhotoLayoutConstants.ID_CARD,
            _ => null // 按默认：证件类原寸、其余整页
        };

        await ExecuteAsync(async () =>
        {
            var okCount = 0;
            var failures = new List<string>();

            foreach (var item in checkedItems)
            {
                try
                {
                    var photo = item.Photo;
                    var dataResult = await _photoService.GetPhotoDataAsync(photo.Id, CancellationToken);
                    if (dataResult.IsFailure || dataResult.Value == null || dataResult.Value.Length == 0)
                    {
                        failures.Add($"{item.DisplayName}：读取照片失败");
                        continue;
                    }

                    var layout = forcedLayout
                        ?? (photo.PhotoCategory == PhotoCategoryConstants.ID_CARD
                            ? PhotoLayoutConstants.ID_CARD
                            : PhotoLayoutConstants.FULL_PAGE);

                    var request = new PrintJobRequest
                    {
                        BusinessType = PrintBusinessTypes.ArchivePhoto,
                        BusinessId = photo.ApplicationId,
                        Classification = photo.PhotoCategoryName,
                        ApplicantIdCard = photo.ApplicantIdCard,
                        Copies = 1,
                        IsDuplex = false,
                        JobKind = PrintJobConstants.JobKindPhoto,
                        PhotoData = dataResult.Value,
                        PhotoLayout = layout,
                        PhotoCategory = photo.PhotoCategory,
                        RequestedBy = App.CurrentUserId,
                        RequestedByName = App.CurrentUserName
                    };

                    var enqueue = await _printQueueService.EnqueueAsync(request, CancellationToken);
                    if (enqueue.IsSuccess) okCount++;
                    else failures.Add($"{item.DisplayName}：{enqueue.Message}");
                }
                catch (Exception ex)
                {
                    failures.Add($"{item.DisplayName}：{ex.Message}");
                }
            }

            if (okCount > 0)
            {
                _logger.LogBusiness("档案照片查询-推送打印入队",
                    ("Count", okCount), ("Failures", failures.Count));
            }

            if (failures.Count == 0)
            {
                await _dialogService.DisplayAlertAsync("已推送打印",
                    $"共 {okCount} 张照片已入队，电脑端打印代理将自动打印。", "确定");
                return Result.Success();
            }

            await _dialogService.DisplayAlertAsync(
                okCount > 0 ? "部分推送失败" : "推送失败",
                $"成功 {okCount} 张，失败 {failures.Count} 项：\n{string.Join("\n", failures.Take(5))}",
                "确定");
            return okCount > 0
                ? Result.Success()
                : Result.Failure(ErrorCodes.UNKNOWN_ERROR, "推送打印失败");
        }, "推送打印...");
    }

    #endregion
}

/// <summary>
/// 照片清单行（桌面查询页）：包一层实体，带勾选状态与异步缩略图。
/// 实体本身不加 UI 字段，避免污染数据映射。
/// </summary>
public partial class ArchivePhotoListItem : ObservableObject
{
    private readonly Action? _onSelectionChanged;

    public ArchivePhotoListItem(ArchivePhoto photo, Action? onSelectionChanged = null)
    {
        Photo = photo;
        _onSelectionChanged = onSelectionChanged;
    }

    public ArchivePhoto Photo { get; }

    public long Id => Photo.Id;
    public string ApplicantIdCard => Photo.ApplicantIdCard;
    public string CategoryName => Photo.PhotoCategoryName;
    public long FileSize => Photo.FileSize;
    public DateTime CreatedAt => Photo.CreatedAt;

    /// <summary>显示名：PhotoName 优先，旧数据回退「照片 #Id」。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Photo.PhotoName)
        ? $"照片 #{Photo.Id}"
        : Photo.PhotoName!;

    /// <summary>多选勾选状态（导出选中用）。</summary>
    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged?.Invoke();

    /// <summary>缩略图（异步加载；null = 未加载/失败，显示占位）。</summary>
    [ObservableProperty]
    private ImageSource? _thumbnail;

    public bool HasThumbnail => Thumbnail is not null;

    partial void OnThumbnailChanged(ImageSource? value) => OnPropertyChanged(nameof(HasThumbnail));
}

/// <summary>照片查询类别选项（空代码 = 全部）。</summary>
public sealed record PhotoCategoryFilterOption(string Code, string Name);

/// <summary>导出格式选项（webp 默认 / jpeg 供民政等外部系统）。</summary>
public sealed record ExportFormatOption(string Code, string Name);
