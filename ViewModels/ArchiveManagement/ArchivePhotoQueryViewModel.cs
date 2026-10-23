using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ArchiveManagement;

/// <summary>
/// 档案照片查询/导出（桌面端）：按身份证或备注关键词 + 类别检索，预览单张，批量导出到输出根。
/// 导出目录：{输出根}\档案照片\{身份证后6位}\{类别}_{时间}_{id}.jpg
/// </summary>
public partial class ArchivePhotoQueryViewModel : PagedSearchViewModelBase
{
    private readonly IArchivePhotoService _photoService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

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

    public ArchivePhotoQueryViewModel(
        IArchivePhotoService photoService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider)
    {
        _photoService = photoService;
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
            PreviewImage = ImageSource.FromStream(() => new MemoryStream(bytes));
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

                    var fileName = $"{photo.PhotoCategory}_{photo.CreatedAt:yyyyMMdd_HHmmss}_{photo.Id}.jpg";
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

            var filePath = Path.Combine(dir, $"{photo.PhotoCategory}_{photo.CreatedAt:yyyyMMdd_HHmmss}_{photo.Id}.jpg");
            await File.WriteAllBytesAsync(filePath, data.Value, CancellationToken);

            _logger.LogBusiness("档案照片导出单张", ("PhotoId", photo.Id));
            await _dialogService.DisplayAlertAsync("导出完成", $"已导出到：\n{filePath}", "确定");
            return Result.Success();
        }, "导出照片...");
    }
}

/// <summary>照片查询类别选项（空代码 = 全部）。</summary>
public sealed record PhotoCategoryFilterOption(string Code, string Name);
