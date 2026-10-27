using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Shared;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ArchiveManagement;

/// <summary>
/// 档案照片查询/导出（桌面端）：按身份证或备注关键词 + 类别检索，预览单张，批量导出到输出根。
/// 导出目录：{输出根}\档案照片\{身份证后6位}\{类别}_{时间}_{id}.{webp|jpg|png}
/// 另提供「历史照片转 WebP」批量维护入口（旧 JPEG 统一压缩为 WebP，单张 ≤500KB）。
/// </summary>
public partial class ArchivePhotoQueryViewModel : PagedSearchViewModelBase
{
    private readonly IArchivePhotoService _photoService;
    private readonly IArchivePhotoWebPMigrationService _webpMigrationService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;
    private CancellationTokenSource? _convertCts;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    /// <summary>批量转换进度（绑定 ProgressPopupView）。</summary>
    public ProgressViewModel Progress { get; } = new();

    [ObservableProperty]
    private ObservableCollection<ArchivePhoto> _photos = new();

    [ObservableProperty]
    private string _keyword = string.Empty;

    /// <summary>类别代码，空 = 全部。</summary>
    [ObservableProperty]
    private string _selectedCategoryCode = string.Empty;

    public ObservableCollection<PhotoCategoryFilterOption> CategoryOptions { get; } = new();

    [ObservableProperty]
    private PhotoCategoryFilterOption? _selectedCategoryOption;

    [ObservableProperty]
    private ArchivePhoto? _selectedPhoto;

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
        IServiceProvider serviceProvider)
    {
        _photoService = photoService;
        _webpMigrationService = webpMigrationService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;

        PageSize = 20;
        Title = "档案照片查询";

        CategoryOptions.Add(new PhotoCategoryFilterOption(string.Empty, "全部类别"));
        foreach (var (code, name) in PhotoCategoryConstants.All)
            CategoryOptions.Add(new PhotoCategoryFilterOption(code, name));
        _selectedCategoryOption = CategoryOptions[0];
    }

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await RefreshPendingCountAsync();
    }

    partial void OnSelectedCategoryOptionChanged(PhotoCategoryFilterOption? value)
    {
        if (value == null) return;
        SelectedCategoryCode = value.Code;
        PageIndex = 1;
        SafeFireAndForget(LoadDataAsync);
    }

    partial void OnSelectedPhotoChanged(ArchivePhoto? value)
    {
        PreviewImage = null;
        if (value == null)
        {
            PreviewInfo = "选择左侧照片查看预览";
            return;
        }

        PreviewInfo = $"{value.PhotoCategoryName} · {DataMasker.MaskIdCard(value.ApplicantIdCard)} · {value.CreatedAt:yyyy-MM-dd HH:mm}";
        SafeFireAndForget(async () =>
        {
            var data = await _photoService.GetPhotoDataAsync(value.Id, CancellationToken);
            if (data.IsFailure || data.Value == null || data.Value.Length == 0) return;

            var bytes = data.Value;
            var format = ImageTranscoder.DetectFormat(bytes);
            if (string.IsNullOrEmpty(format))
                format = value.ImageFormat;

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
                Photos.Add(photo);

            TotalCount = result.Value?.TotalCount ?? 0;
            SelectedPhoto = null;
            return Result.Success();
        }, "查询照片...");
    }

    [RelayCommand]
    private async Task ApplySearchAsync()
    {
        PageIndex = 1;
        await LoadDataAsync();
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

                    var data = await _photoService.GetPhotoDataAsync(photo.Id, CancellationToken);
                    if (data.IsFailure || data.Value == null || data.Value.Length == 0) continue;

                    var tail = photo.ApplicantIdCard.Length >= 6
                        ? photo.ApplicantIdCard[^6..]
                        : photo.ApplicantIdCard;
                    var dir = Path.Combine(root, tail);
                    Directory.CreateDirectory(dir);

                    var format = ImageTranscoder.DetectFormat(data.Value);
                    if (string.IsNullOrEmpty(format))
                        format = photo.ImageFormat;
                    var fileName = $"{photo.PhotoCategory}_{photo.CreatedAt:yyyyMMdd_HHmmss}_{photo.Id}{PhotoFormatConstants.GetExtension(format)}";
                    await File.WriteAllBytesAsync(Path.Combine(dir, fileName), data.Value, CancellationToken);
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

    /// <summary>导出当前选中单张照片。</summary>
    [RelayCommand]
    private async Task ExportSelectedAsync()
    {
        if (SelectedPhoto == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择一张照片", "确定");
            return;
        }

        var photo = SelectedPhoto;
        await ExecuteAsync(async () =>
        {
            var data = await _photoService.GetPhotoDataAsync(photo.Id, CancellationToken);
            if (data.IsFailure || data.Value == null || data.Value.Length == 0)
                return Result.Failure(data.ErrorCode ?? ErrorCodes.NOT_FOUND, data.Message ?? "照片读取失败");

            var tail = photo.ApplicantIdCard.Length >= 6 ? photo.ApplicantIdCard[^6..] : photo.ApplicantIdCard;
            var dir = Path.Combine(OutputPathHelper.OutputRoot, "档案照片", tail);
            Directory.CreateDirectory(dir);

            var format = ImageTranscoder.DetectFormat(data.Value);
            if (string.IsNullOrEmpty(format))
                format = photo.ImageFormat;
            var filePath = Path.Combine(dir,
                $"{photo.PhotoCategory}_{photo.CreatedAt:yyyyMMdd_HHmmss}_{photo.Id}{PhotoFormatConstants.GetExtension(format)}");
            await File.WriteAllBytesAsync(filePath, data.Value, CancellationToken);

            _logger.LogBusiness("档案照片导出单张", ("PhotoId", photo.Id));
            await _dialogService.DisplayAlertAsync("导出完成", $"已导出到：\n{filePath}", "确定");
            return Result.Success();
        }, "导出照片...");
    }
}

/// <summary>照片查询类别选项（空代码 = 全部）。</summary>
public sealed record PhotoCategoryFilterOption(string Code, string Name);
