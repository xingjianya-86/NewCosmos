using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ArchiveManagement;

/// <summary>
/// 待归属照片池：无身份证先存照片，拿到身份证并确认人员已建档后归属到档案照片。
/// </summary>
public partial class PendingPhotosViewModel : ViewModelBase
{
    private readonly IArchivePhotoPendingService _pendingService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    private string _prefillIdCard = string.Empty;
    private CancellationTokenSource? _thumbCts;

    public PendingPhotosViewModel(
        IArchivePhotoPendingService pendingService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider)
    {
        _pendingService = pendingService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        Title = "待归属照片";
    }

    [ObservableProperty]
    private ObservableCollection<PendingPhotoItem> _photos = new();

    [ObservableProperty]
    private string _keyword = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public void ApplyParameter(PendingPhotosParameter parameter)
    {
        if (!string.IsNullOrWhiteSpace(parameter.PrefillIdCard))
            _prefillIdCard = parameter.PrefillIdCard.Trim();
    }

    public override async Task OnAppearingAsync()
    {
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SearchAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await ExecuteAsync(async () =>
        {
            var result = await _pendingService.GetPendingPagedAsync(Keyword, null, 1, 200, CancellationToken);
            if (result.IsFailure || result.Value == null)
            {
                await _dialogService.DisplayAlertAsync("提示", result.Message ?? "加载待归属照片失败", "确定");
                return Result.Failure(ErrorCodes.DB_QUERY_ERROR, result.Message ?? "加载待归属照片失败");
            }

            _thumbCts?.Cancel();
            _thumbCts?.Dispose();
            _thumbCts = new CancellationTokenSource();

            Photos.Clear();
            foreach (var photo in result.Value.Items)
                Photos.Add(new PendingPhotoItem(photo));

            StatusText = $"待归属 {Photos.Count} 张";
            SafeFireAndForget(() => LoadThumbnailsAsync(_thumbCts.Token));
            return Result.Success();
        }, "加载待归属照片...");
    }

    private async Task LoadThumbnailsAsync(CancellationToken ct)
    {
        foreach (var item in Photos.ToList())
        {
            if (ct.IsCancellationRequested) return;
            var data = await _pendingService.GetPendingDataAsync(item.Id, ct);
            if (data.IsFailure || data.Value == null || data.Value.Length == 0) continue;

            var thumb = ImageCompressor.CreateThumbnail(data.Value, maxEdge: 160, jpeg: true);
            if (thumb == null) continue;
            var source = ImageSource.FromStream(() => new MemoryStream(thumb));
            MainThread.BeginInvokeOnMainThread(() => item.Thumbnail = source);
        }
    }

    [RelayCommand]
    private async Task AssignSelectedAsync()
    {
        var selected = Photos.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先勾选要归属的照片", "确定");
            return;
        }

        string? idCard;
        if (IdCardValidator.IsValid(_prefillIdCard))
        {
            var useCurrent = await _dialogService.DisplayAlertAsync("归属到人员",
                $"归属到当前查询的人员（{DataMasker.MaskIdCard(_prefillIdCard)}）吗？", "归属", "重新输入");
            idCard = useCurrent
                ? _prefillIdCard
                : await _dialogService.DisplayPromptAsync("归属到人员",
                    "请输入该人员的 18 位身份证号（须已在业务档案中）：", "归属", "取消", "身份证号");
        }
        else
        {
            idCard = await _dialogService.DisplayPromptAsync("归属到人员",
                "请输入该人员的 18 位身份证号（须已在业务档案中）：", "归属", "取消", "身份证号");
        }
        if (idCard == null) return;

        idCard = idCard.Trim();
        if (!IdCardValidator.IsValid(idCard))
        {
            await _dialogService.DisplayAlertAsync("提示", "身份证号格式不正确（需 18 位）", "确定");
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _pendingService.AssignPendingAsync(selected.Select(x => x.Id).ToList(), idCard, CancellationToken);
            if (result.IsFailure)
            {
                await _dialogService.DisplayAlertAsync("归属失败", result.Message ?? "归属失败", "确定");
                return result;
            }

            var summary = result.Value!;
            _logger.LogBusiness("待归属照片归属",
                ("IdCard", DataMasker.MaskIdCard(idCard)),
                ("Succeeded", summary.Succeeded),
                ("Failed", summary.Failed));

            if (summary.Failed > 0)
                await _dialogService.DisplayAlertAsync("部分完成",
                    $"成功 {summary.Succeeded} 张，失败 {summary.Failed} 张。\n{summary.FirstError}", "确定");
            else
                await _dialogService.ShowSnackBarAsync($"已归属 {summary.Succeeded} 张照片");

            await LoadAsync();
            return Result.Success();
        }, "归属照片...");
    }

    [RelayCommand]
    private async Task DeleteAsync(PendingPhotoItem? item)
    {
        if (item == null) return;
        var confirm = await _dialogService.DisplayAlertAsync("删除待归属照片",
            $"确定删除「{item.Title}」吗？此操作不可恢复。", "删除", "取消");
        if (!confirm) return;

        await ExecuteAsync(async () =>
        {
            var result = await _pendingService.DeletePendingAsync(item.Id, CancellationToken);
            if (result.IsFailure)
            {
                await _dialogService.DisplayAlertAsync("删除失败", result.Message ?? "删除失败", "确定");
                return result;
            }
            Photos.Remove(item);
            StatusText = $"待归属 {Photos.Count} 张";
            return Result.Success();
        }, "删除待归属照片...");
    }
}

/// <summary>待归属照片列表行（含缩略图与勾选状态）。</summary>
public partial class PendingPhotoItem : ObservableObject, IPhotoThumbnailTarget
{
    public PendingPhotoItem(PendingArchivePhoto photo) => Photo = photo;

    public PendingArchivePhoto Photo { get; }

    public long Id => Photo.Id;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private ImageSource? _thumbnail;

    public string Title => Photo.DisplayName;
    public string CategoryName => Photo.PhotoCategoryName;
    public string RecognizedName => string.IsNullOrWhiteSpace(Photo.RecognizedName) ? "未识别" : Photo.RecognizedName!;
    public string RecognizedIdCard => string.IsNullOrWhiteSpace(Photo.RecognizedIdCard) ? "未识别" : Photo.RecognizedIdCard!;
    public string SourceText => string.IsNullOrWhiteSpace(Photo.Source) ? "未标注" : Photo.Source!;
    public string CreatedAtText => Photo.CreatedAt.ToString("yyyy-MM-dd HH:mm");
    public string SizeText => Photo.FileSize > 0 ? $"{Photo.FileSize / 1024.0:F0} KB" : string.Empty;
}
