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

    [ObservableProperty]
    private ArchivePhotoListItem? _selectedPhoto;

    [ObservableProperty]
    private ImageSource? _previewImage;

    [ObservableProperty]
    private string _previewInfo = "选择左侧照片查看预览";

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
    }

    /// <summary>业务页「照片」入口注入：预填关键词（身份证号），进页自动检索。</summary>
    public void ApplyParameter(ArchivePhotoQueryParameter parameter)
    {
        if (!string.IsNullOrWhiteSpace(parameter.Keyword))
            Keyword = parameter.Keyword.Trim();
        _autoSearchOnAppear = parameter.AutoSearch;
    }

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await RefreshPendingCountAsync();

        if (_autoSearchOnAppear)
        {
            _autoSearchOnAppear = false;
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
        PreviewImage = null;
        if (value == null)
        {
            PreviewInfo = "选择左侧照片查看预览";
            return;
        }

        var photo = value.Photo;
        PreviewInfo = $"{photo.PhotoCategoryName} · {DataMasker.MaskIdCard(photo.ApplicantIdCard)} · {photo.CreatedAt:yyyy-MM-dd HH:mm}";
        SafeFireAndForget(async () =>
        {
            var data = await _photoService.GetPhotoDataAsync(photo.Id, CancellationToken);
            if (data.IsFailure || data.Value == null || data.Value.Length == 0) return;

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

            PreviewImage = ImageSource.FromStream(() => new MemoryStream(displayBytes));
        });
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

            _logger.LogBusiness("档案照片导出", ("Count", exported), ("Root", root));

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

            _logger.LogBusiness("档案照片导出选中", ("Count", exported), ("Root", root));
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
                ("Count", exported), ("Failed", failed), ("Target", targetDir));

            var scope = checkedItems.Count > 0 ? $"勾选 {checkedItems.Count} 张" : "当前查询结果";
            await _dialogService.DisplayAlertAsync("保存完成",
                $"已保存 {exported} 张（{scope}）到：\n{targetDir}" +
                (failed > 0 ? $"\n失败 {failed} 张（详见日志）" : string.Empty),
                "确定");
            return Result.Success();
        }, "保存照片...");
    }

    /// <summary>导出单张到指定目录；同名文件自动追加 Id 防覆盖。返回是否成功。</summary>
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

            Directory.CreateDirectory(dir);

            var format = ImageTranscoder.DetectFormat(data.Value);
            if (string.IsNullOrEmpty(format))
                format = photo.ImageFormat;

            var filePath = Path.Combine(dir, BuildFileName(photo, format));
            if (File.Exists(filePath))
                filePath = Path.Combine(dir, BuildFileName(photo, format, forceIdSuffix: true));

            await File.WriteAllBytesAsync(filePath, data.Value, ct);
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
