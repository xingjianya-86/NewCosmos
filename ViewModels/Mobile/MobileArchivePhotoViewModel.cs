using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
            FileResult? result = camera
                ? await MediaPicker.Default.CapturePhotoAsync()
                : (await MediaPicker.Default.PickPhotosAsync())?.FirstOrDefault();

            if (result == null)
                return; // 用户取消

            byte[] raw;
            await using (var stream = await result.OpenReadAsync())
            using (var ms = new MemoryStream())
            {
                await stream.CopyToAsync(ms);
                raw = ms.ToArray();
            }

            if (raw.Length == 0)
            {
                await _dialogService.DisplayAlertAsync("提示", "照片读取为空，请重试", "确定");
                return;
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

            await ExecuteAsync(async () =>
            {
                var save = await _photoService.SavePhotoAsync(photo, CancellationToken);
                if (save.IsFailure)
                    return Result.Failure(save.ErrorCode!, save.Message!);

                _logger.LogBusiness("采集档案照片",
                    ("ApplicationId", ApplicationId?.ToString() ?? ""),
                    ("Category", SelectedCategoryCode),
                    ("PhotoId", save.Value.ToString()),
                    ("Size", compressed.Length.ToString()));
                return Result.Success();
            }, "保存照片...");

            await LoadPhotosAsync();
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
