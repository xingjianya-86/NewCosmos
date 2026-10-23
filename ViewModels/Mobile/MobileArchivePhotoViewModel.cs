using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.Printing;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.Mobile;

/// <summary>
/// 手机端·档案照片 ViewModel：按人（身份证）采集证件/核查照片，分类标定后存库，列表查阅，
/// 并可推送照片到 PC 端打印队列（批次三）。
/// 照片属公民身份敏感信息，仅存内网库；打印经 nc_biz_print_jobs 由 PC 打印代理执行。
/// </summary>
public partial class MobileArchivePhotoViewModel : ViewModelBase
{
    private readonly IArchivePhotoService _photoService;
    private readonly IPrintQueueService _printQueueService;
    private readonly INewPermissionService _permissionService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    [ObservableProperty] private string _applicantIdCard = string.Empty;
    [ObservableProperty] private string _applicantName = string.Empty;
    [ObservableProperty] private long? _applicationId;

    [ObservableProperty] private ObservableCollection<ArchivePhoto> _photos = new();
    [ObservableProperty] private bool _hasPhotos;

    /// <summary>选中的照片类别（存库时用 Code）。</summary>
    [ObservableProperty] private string _selectedCategoryCode = PhotoCategoryConstants.ID_CARD;

    /// <summary>类别选项（Code + 中文名），供 Picker 绑定。</summary>
    public ObservableCollection<PhotoCategoryOption> CategoryOptions { get; } = new();

    /// <summary>Picker 选中项（映射到 SelectedCategoryCode）。</summary>
    [ObservableProperty] private PhotoCategoryOption? _selectedCategoryOption;

    /// <summary>当前选中类别的中文名（头部展示）。</summary>
    public string SelectedCategoryName => PhotoCategoryConstants.GetDisplayName(SelectedCategoryCode);

    public MobileArchivePhotoViewModel(
        IArchivePhotoService photoService,
        IPrintQueueService printQueueService,
        INewPermissionService permissionService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider)
    {
        _photoService = photoService;
        _printQueueService = printQueueService;
        _permissionService = permissionService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        Title = "档案照片";

        foreach (var (code, name) in PhotoCategoryConstants.All)
            CategoryOptions.Add(new PhotoCategoryOption(code, name));

        // 默认选中身份证
        _selectedCategoryOption = CategoryOptions.FirstOrDefault(o => o.Code == SelectedCategoryCode);
    }

    partial void OnSelectedCategoryOptionChanged(PhotoCategoryOption? value)
    {
        if (value == null || value.Code == SelectedCategoryCode)
            return;
        SelectedCategoryCode = value.Code; // 触发类别切换 → 刷新列表
    }

    /// <summary>由页面 SetParameterAsync 注入导航参数（身份证/申请/姓名）。</summary>
    public void ApplyParameter(string applicantIdCard, long? applicationId, string? applicantName)
    {
        ApplicantIdCard = applicantIdCard ?? string.Empty;
        ApplicationId = applicationId;
        ApplicantName = applicantName ?? string.Empty;
    }

    #region 补传会话（历史档案批量补传：拍完一人点「下一位」连续采集）

    private IReadOnlyList<PhotoBackfillPerson>? _sessionQueue;
    private int _sessionIndex = -1;

    /// <summary>是否处于补传会话（≥2 人时才显示会话条）。</summary>
    [ObservableProperty]
    private bool _hasSession;

    /// <summary>会话进度文案（第 2/5 人 · 张三）。</summary>
    [ObservableProperty]
    private string _sessionText = string.Empty;

    /// <summary>是否已是会话最后一人（按钮显示「完成」）。</summary>
    [ObservableProperty]
    private bool _isLastInSession;

    public string SessionActionText => IsLastInSession ? "完成" : "下一位";

    partial void OnIsLastInSessionChanged(bool value) => OnPropertyChanged(nameof(SessionActionText));

    /// <summary>注入会话队列（补传页传入；其他入口传 null 表示单页模式）。</summary>
    public void ApplySession(IReadOnlyList<PhotoBackfillPerson>? queue, int index)
    {
        _sessionQueue = queue;
        _sessionIndex = index;
        UpdateSessionState();
    }

    private void UpdateSessionState()
    {
        HasSession = _sessionQueue is { Count: > 1 } && _sessionIndex >= 0 && _sessionIndex < _sessionQueue.Count;
        if (!HasSession)
        {
            SessionText = string.Empty;
            IsLastInSession = false;
            return;
        }

        var person = _sessionQueue![_sessionIndex];
        SessionText = $"第 {_sessionIndex + 1}/{_sessionQueue.Count} 人 · {person.Name}";
        IsLastInSession = _sessionIndex >= _sessionQueue.Count - 1;
    }

    /// <summary>下一位（最后一位则退出采集，回到补传清单）。</summary>
    [RelayCommand]
    private async Task NextPersonAsync()
    {
        if (!HasSession || _sessionQueue == null) return;

        if (_sessionIndex >= _sessionQueue.Count - 1)
        {
            var navigation = Helpers.WindowNavigator.CurrentPage?.Navigation;
            if (navigation != null)
                await navigation.PopAsync();
            return;
        }

        _sessionIndex++;
        var next = _sessionQueue[_sessionIndex];
        // 保留当前照片类别，方便同一批档案按同一类别连续采集
        ApplyParameter(next.IdCard, next.ApplicationId, next.Name);
        UpdateSessionState();
        await LoadPhotosAsync();
    }

    #endregion

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await LoadPhotosAsync();
    }

    partial void OnSelectedCategoryCodeChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedCategoryName));
        // 切类别即刷新列表（按类别过滤）
        SafeFireAndForget(LoadPhotosAsync);
    }

    private async Task LoadPhotosAsync()
    {
        if (string.IsNullOrWhiteSpace(ApplicantIdCard))
            return;

        await ExecuteAsync(async () =>
        {
            var result = await _photoService.GetByPersonAsync(
                ApplicantIdCard, SelectedCategoryCode, CancellationToken);

            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);

            Photos.Clear();
            foreach (var p in result.Value ?? new List<ArchivePhoto>())
                Photos.Add(p);
            HasPhotos = Photos.Count > 0;
            return Result.Success();
        }, "加载照片...");
    }

    /// <summary>拍照采集。</summary>
    [RelayCommand]
    private async Task TakePhotoAsync()
    {
        await CaptureAsync(camera: true);
    }

    /// <summary>从相册选择。</summary>
    [RelayCommand]
    private async Task PickPhotoAsync()
    {
        await CaptureAsync(camera: false);
    }

    private async Task CaptureAsync(bool camera)
    {
        if (string.IsNullOrWhiteSpace(ApplicantIdCard))
        {
            await _dialogService.DisplayAlertAsync("提示", "缺少身份证号关联，无法存档", "确定");
            return;
        }

        try
        {
            // 拍照：单张；从相册选择：支持一次多选，批量归入当前类别
            var files = new List<FileResult>();
            if (camera)
            {
                var shot = await MediaPicker.Default.CapturePhotoAsync();
                if (shot != null) files.Add(shot);
            }
            else
            {
                var picked = await MediaPicker.Default.PickPhotosAsync();
                if (picked != null) files.AddRange(picked);
            }

            if (files.Count == 0)
                return; // 用户取消

            var savedCount = 0;
            var failedCount = 0;

            await ExecuteAsync(async () =>
            {
                foreach (var file in files)
                {
                    try
                    {
                        byte[] raw;
                        await using (var stream = await file.OpenReadAsync())
                        using (var ms = new MemoryStream())
                        {
                            await stream.CopyToAsync(ms);
                            raw = ms.ToArray();
                        }

                        if (raw.Length == 0)
                        {
                            failedCount++;
                            continue;
                        }

                        // 压缩后存库
                        var compressed = Helpers.ImageCompressor.Compress(raw);

                        var photo = new ArchivePhoto
                        {
                            PhotoData = compressed,
                            ApplicantIdCard = ApplicantIdCard.Trim(),
                            ApplicationId = ApplicationId,
                            PhotoCategory = SelectedCategoryCode,
                            PhotoName = null,
                            CreatedBy = App.CurrentUserId.HasValue ? (int?)App.CurrentUserId.Value : null,
                            CreatedByName = App.CurrentUserName
                        };

                        var save = await _photoService.SavePhotoAsync(photo, CancellationToken);
                        if (save.IsFailure)
                        {
                            failedCount++;
                            _logger.Warn($"照片保存失败: {file.FileName} {save.Message}");
                            continue;
                        }

                        savedCount++;
                        _logger.LogBusiness("采集档案照片",
                            ("ApplicationId", ApplicationId?.ToString() ?? ""),
                            ("Category", SelectedCategoryCode),
                            ("PhotoId", save.Value.ToString()),
                            ("Size", compressed.Length.ToString()));
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        _logger.Warn($"照片处理失败: {file.FileName} {ex.Message}");
                    }
                }

                return Result.Success();
            }, files.Count > 1 ? $"上传 {files.Count} 张照片..." : "保存照片...");

            await LoadPhotosAsync();

            if (savedCount == 0 && failedCount == 0)
                return; // 全部取消

            var message = failedCount == 0
                ? $"已保存 {savedCount} 张照片（{SelectedCategoryName}）"
                : $"已保存 {savedCount} 张，失败 {failedCount} 张（{SelectedCategoryName}）";
            await ShowTipAsync(message);
        }
        catch (FeatureNotSupportedException)
        {
            await _dialogService.DisplayAlertAsync("提示", "该设备不支持此功能", "确定");
        }
        catch (PermissionException)
        {
            await _dialogService.DisplayAlertAsync("权限不足", "请在系统设置中允许相机/相册访问权限", "确定");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "采集照片失败");
            await _dialogService.DisplayAlertAsync("错误", $"采集失败：{ex.Message}", "确定");
        }
    }

    /// <summary>
    /// 导出到系统相册（Android MediaStore / Windows 图片目录）：便于外发打印或留档。
    /// </summary>
    [RelayCommand]
    private async Task ExportPhotoAsync(ArchivePhoto? photo)
    {
        if (photo == null) return;

        var album = _serviceProvider.GetRequiredService<Services.Platform.IAlbumExportService>();

        var result = await ExecuteAsync(async () =>
        {
            var data = await _photoService.GetPhotoDataAsync(photo.Id, CancellationToken);
            if (data.IsFailure || data.Value == null || data.Value.Length == 0)
                return Result.Failure(data.ErrorCode ?? ErrorCodes.NOT_FOUND, data.Message ?? "照片读取失败");

            var fileName = $"NewCosmos_{photo.ApplicantIdCard}_{photo.PhotoCategory}_{DateTime.Now:yyyyMMddHHmmss}_{photo.Id}.jpg";
            var saved = await album.SaveToAlbumAsync(fileName, data.Value, CancellationToken);
            if (saved.IsFailure)
                return Result.Failure(saved.ErrorCode!, saved.Message!);

            _logger.LogBusiness("档案照片导出到相册",
                ("PhotoId", photo.Id),
                ("Category", photo.PhotoCategory),
                ("Target", saved.Value ?? string.Empty));
            return Result.Success();
        }, "导出到相册...");

        if (result.IsSuccess)
            await ShowTipAsync("已导出到相册");
    }

    /// <summary>删除照片（长按/按钮）。</summary>
    [RelayCommand]
    private async Task DeletePhotoAsync(ArchivePhoto? photo)
    {
        if (photo == null) return;

        var ok = await _dialogService.DisplayAlertAsync(
            "删除照片",
            $"确定删除这张「{PhotoCategoryConstants.GetDisplayName(photo.PhotoCategory)}」照片吗？删除后不可恢复。",
            "删除", "取消");
        if (!ok) return;

        await ExecuteAsync(async () =>
        {
            var result = await _photoService.DeletePhotoAsync(photo.Id, CancellationToken);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);

            _logger.LogBusiness("删除档案照片", ("PhotoId", photo.Id.ToString()));
            return Result.Success();
        }, "删除照片...");

        await LoadPhotosAsync();
    }

    /// <summary>
    /// 推送照片到 PC 打印队列（批次三）：选版式 → 取回照片二进制 → 入队（Pending）。
    /// PC 打印代理轮询后用 Word COM 排版直接打印。权限口径复用「家庭收入档案打印」（不新增权限种子）。
    /// </summary>
    [RelayCommand]
    private async Task PrintPhotoAsync(ArchivePhoto? photo)
    {
        if (photo == null) return;

        // 权限门控（复用 PRINT_FAMILYAPPLICATION，与动态管理记录打印同源）
        var userId = App.CurrentUserId;
        if (userId.HasValue)
        {
            var allowed = await _permissionService.HasPermissionAsync(
                userId.Value, PermissionCodes.PRINT_FAMILYAPPLICATION, CancellationToken);
            if (!allowed)
            {
                await ShowTipAsync("您没有打印权限（PRINT_FAMILYAPPLICATION），请联系管理员");
                return;
            }
        }

        // 选版式：证件类默认「证件原寸」，其余默认「整页」
        var defaultLayout = photo.PhotoCategory == PhotoCategoryConstants.ID_CARD
            ? PhotoLayoutConstants.ID_CARD
            : PhotoLayoutConstants.FULL_PAGE;
        var buttons = PhotoLayoutConstants.All
            .Select(a => PhotoLayoutConstants.GetDisplayName(a.Code)).ToArray();
        var choice = await _dialogService.DisplayActionSheetAsync(
            "选择打印版式", "取消", null, buttons);

        var layout = MapLayoutChoice(choice) ?? defaultLayout;
        if (choice == null || choice == "取消")
            return; // 用户取消选版式

        await ExecuteAsync(async () =>
        {
            var dataResult = await _photoService.GetPhotoDataAsync(photo.Id, CancellationToken);
            if (dataResult.IsFailure)
                return Result.Failure(dataResult.ErrorCode!, dataResult.Message!);

            var request = new PrintJobRequest
            {
                BusinessType = "ArchivePhoto",
                BusinessId = ApplicationId,
                Classification = PhotoCategoryConstants.GetDisplayName(photo.PhotoCategory),
                ApplicantName = ApplicantName,
                ApplicantIdCard = ApplicantIdCard,
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
            if (enqueue.IsFailure)
                return Result.Failure(enqueue.ErrorCode!, enqueue.Message!);

            _logger.LogBusiness("推送照片打印",
                ("PhotoId", photo.Id.ToString()),
                ("Layout", layout),
                ("JobNo", enqueue.Value?.JobNo ?? ""),
                ("Applicant", DataMasker.MaskName(ApplicantName)));
            return Result.Success();
        }, "推送到打印队列...");

        await ShowTipAsync("已推送到打印队列，将在 PC 端打印");
    }

    /// <summary>ActionSheet 选项名 → 版式代码。</summary>
    private static string? MapLayoutChoice(string? choice)
        => choice switch
        {
            "整页" => PhotoLayoutConstants.FULL_PAGE,
            "半页" => PhotoLayoutConstants.HALF_PAGE,
            "证件原寸" => PhotoLayoutConstants.ID_CARD,
            _ => null
        };

    private async Task ShowTipAsync(string message)
    {
        await _dialogService.DisplayAlertAsync("提示", message, "确定");
    }
}

/// <summary>照片类别选项（供 Picker 绑定：Code + 中文名）。</summary>
public sealed class PhotoCategoryOption
{
    public string Code { get; }
    public string Name { get; }

    public PhotoCategoryOption(string code, string name)
    {
        Code = code;
        Name = name;
    }
}
