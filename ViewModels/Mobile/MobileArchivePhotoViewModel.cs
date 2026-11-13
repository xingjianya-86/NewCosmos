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
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

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
    private readonly IOcrRecognizeClient _ocrClient;

    /// <summary>识别回填上下文（表单传入；null = 只存档不识别）</summary>
    private IOcrAssignContext? _assignContext;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    [ObservableProperty] private string _applicantIdCard = string.Empty;
    [ObservableProperty] private string _applicantName = string.Empty;
    [ObservableProperty] private long? _applicationId;

    [ObservableProperty] private ObservableCollection<MobileArchivePhotoItem> _photos = new();
    [ObservableProperty] private bool _hasPhotos;

    /// <summary>是否显示底部「批量扫描识别」按钮（有回填上下文才可识别；无上下文只存档）。</summary>
    [ObservableProperty] private bool _isRecognizeVisible;

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
        IServiceProvider serviceProvider,
        IOcrRecognizeClient ocrClient)
    {
        _photoService = photoService;
        _printQueueService = printQueueService;
        _permissionService = permissionService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _ocrClient = ocrClient;
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

    /// <summary>注入识别回填上下文（null = 只存档不识别，如档案查询/补传入口）。</summary>
    public void ApplyAssignContext(IOcrAssignContext? context)
    {
        _assignContext = context;
        IsRecognizeVisible = context is not null;
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
                Photos.Add(new MobileArchivePhotoItem(p));
            HasPhotos = Photos.Count > 0;
            return Result.Success();
        }, "加载照片...");

        // 缩略图后台加载（不阻塞列表渲染；切类别/刷新会取消上一批）
        SafeFireAndForget(LoadThumbnailsAsync);
    }

    public override void OnDisappearing()
    {
        base.OnDisappearing();
        _thumbCts?.Cancel();
        _thumbCts?.Dispose();
        _thumbCts = null;
    }

    #region 列表缩略图与全屏预览

    /// <summary>缩略图后台加载的取消源（切类别/重新加载/离开页面时取消上一批）。</summary>
    private CancellationTokenSource? _thumbCts;

    /// <summary>全屏预览是否打开。</summary>
    [ObservableProperty]
    private bool _isPreviewOpen;

    /// <summary>预览大图（关闭时置 null 释放内存）。</summary>
    [ObservableProperty]
    private ImageSource? _previewImage;

    /// <summary>是否已取到预览大图（控制空态提示显隐）。</summary>
    public bool HasPreviewImage => PreviewImage is not null;

    /// <summary>预览空态（既没取到图也没在加载：读取失败/照片已删）。</summary>
    public bool ShowPreviewEmpty => !HasPreviewImage && !IsPreviewLoading;

    partial void OnPreviewImageChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(HasPreviewImage));
        OnPropertyChanged(nameof(ShowPreviewEmpty));
    }

    partial void OnIsPreviewLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowPreviewEmpty));

    /// <summary>预览信息条（类别 · 名称 · 采集时间）。</summary>
    [ObservableProperty]
    private string _previewInfo = string.Empty;

    /// <summary>预览大图取回/转码中。</summary>
    [ObservableProperty]
    private bool _isPreviewLoading;

    /// <summary>点列表项 → 全屏预览（取库内二进制 → WebP 转 JPEG 适配 → 展示）。</summary>
    [RelayCommand]
    private async Task PreviewPhotoAsync(ArchivePhoto? photo)
    {
        if (photo == null) return;

        PreviewImage = null;
        PreviewInfo = $"{photo.PhotoCategoryName} · {photo.DisplayName} · {photo.CreatedAt:yyyy-MM-dd HH:mm}";
        IsPreviewOpen = true;
        await LoadPreviewAsync(photo);
    }

    /// <summary>关闭全屏预览并释放大图。</summary>
    [RelayCommand]
    private void ClosePreview()
    {
        IsPreviewOpen = false;
        IsPreviewLoading = false;
        PreviewImage = null;
    }

    private async Task LoadPreviewAsync(ArchivePhoto photo)
    {
        try
        {
            IsPreviewLoading = true;

            var data = await _photoService.GetPhotoDataAsync(photo.Id, CancellationToken);
            if (data.IsFailure || data.Value == null || data.Value.Length == 0)
            {
                PreviewInfo = "照片读取失败（可能已被删除）";
                return;
            }

            var bytes = data.Value;
            var format = Helpers.ImageTranscoder.DetectFormat(bytes);
            if (string.IsNullOrEmpty(format))
                format = photo.ImageFormat;

            // WinUI 对 WebP 支持不一致：预览统一先转 JPEG（不改变库内格式），与桌面查询页同口径
            var displayBytes = bytes;
            if (format == PhotoFormatConstants.WebP
                && !Helpers.ImageTranscoder.TryToJpeg(bytes, out displayBytes))
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
    }

    /// <summary>为当前列表异步生成缩略图（4 路并发，切类别/刷新/离开页面时取消上一批）。</summary>
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

                // 输出 JPEG（该页手机/桌面都会打开，WinUI 对 WebP 支持不稳定）
                var thumb = ImageCompressor.CreateThumbnail(data.Value, maxEdge: 160, jpeg: true);
                if (thumb == null || token.IsCancellationRequested) return;

                MainThread.BeginInvokeOnMainThread(() =>
                    item.Thumbnail = ImageSource.FromStream(() => new MemoryStream(thumb)));
            }
            catch (OperationCanceledException)
            {
                // 切类别/重新加载：忽略
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

    #endregion

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

            // 先把字节读全，再走与「拖入图片」完全同一条保存链路（避免两份入库逻辑）
            var raws = new List<byte[]>(files.Count);
            var readFailed = 0;
            string? firstReadError = null;
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
                        readFailed++;
                        continue;
                    }

                    raws.Add(raw);
                }
                catch (Exception ex)
                {
                    readFailed++;
                    firstReadError ??= ex.Message;
                    _logger.Warn($"读取所选图片失败 {file.FileName}: {ex.Message}");
                }
            }

            await SaveRawBatchAsync(raws, readFailed, firstReadError, source: "采集");
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
    /// 拖入图片（Windows 拖放入口，由页面 code-behind 在 UI 线程回调）：
    /// 与「拍照 / 从相册选择」共用 <see cref="SaveRawBatchAsync"/> 的压缩、命名与入库链路。
    /// </summary>
    public async Task InsertFromDroppedPathsAsync(IReadOnlyList<string>? paths)
    {
        if (string.IsNullOrWhiteSpace(ApplicantIdCard))
        {
            await _dialogService.DisplayAlertAsync("提示", "缺少身份证号关联，无法存档", "确定");
            return;
        }

        try
        {
            if (paths == null || paths.Count == 0)
            {
                await _dialogService.DisplayAlertAsync("提示",
                    "未检测到可拖入的图片文件（支持 jpg / jpeg / png / webp / bmp / gif）", "确定");
                return;
            }

            var raws = new List<byte[]>(paths.Count);
            var readFailed = 0;
            string? firstReadError = null;
            foreach (var path in paths)
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(path);
                    if (bytes.Length == 0)
                    {
                        readFailed++;
                        continue;
                    }

                    raws.Add(bytes);
                }
                catch (Exception ex)
                {
                    readFailed++;
                    firstReadError ??= ex.Message;
                    _logger.Warn($"读取拖入图片失败 {Path.GetFileName(path)}: {ex.Message}");
                }
            }

            await SaveRawBatchAsync(raws, readFailed, firstReadError, source: "拖入");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "拖入照片失败");
            await _dialogService.DisplayAlertAsync("错误", $"导入失败：{ex.Message}", "确定");
        }
    }

    /// <summary>
    /// 批量入库（拍照 / 相册选图 / 拖入 **共用的唯一保存链路**）：
    /// 编号基准取该人该类别现有照片（2026-11-04 命名规则，往下递增不覆盖）→ 逐张交
    /// ArchivePhotoService 统一压缩为 WebP（单张 ≤500KB）落库 → 刷新列表 → 汇总提示。
    /// readFailed / firstReadError 是读取阶段已失败的张数与首个原因（计入总失败数）。
    /// </summary>
    private async Task SaveRawBatchAsync(
        IReadOnlyList<byte[]> raws, int readFailed, string? firstReadError, string source)
    {
        var savedCount = 0;
        var failedCount = readFailed;
        string? firstError = firstReadError;

        if (raws.Count == 0 && failedCount == 0)
            return; // 用户全部取消

        // 命名规则（2026-11-04）：姓名_身份证后4位_类别_编号，编号=该人该类别内流水，往下递增不覆盖。
        // 保存前取一次现有照片算基准号，本批内逐张递增。
        var existingResult = await _photoService.GetByPersonAsync(
            ApplicantIdCard.Trim(), SelectedCategoryCode, CancellationToken);
        var nextSeq = PhotoNameBuilder.NextSequence(
            existingResult.IsSuccess ? existingResult.Value ?? new List<ArchivePhoto>() : new List<ArchivePhoto>());

        if (raws.Count > 0)
        {
            await ExecuteAsync(async () =>
            {
                foreach (var raw in raws)
                {
                    try
                    {
                        // 原始字节交给 ArchivePhotoService 统一压缩为 WebP（单张 ≤500KB）并落库；
                        // 不在 ViewModel 里压缩，避免绕过服务层体积约束。
                        var photo = new ArchivePhoto
                        {
                            PhotoData = raw,
                            ApplicantIdCard = ApplicantIdCard.Trim(),
                            ApplicationId = ApplicationId,
                            PhotoCategory = SelectedCategoryCode,
                            PhotoName = PhotoNameBuilder.Build(ApplicantName, ApplicantIdCard, SelectedCategoryCode, nextSeq),
                            CreatedBy = App.CurrentUserId.HasValue ? (int?)App.CurrentUserId.Value : null,
                            CreatedByName = App.CurrentUserName
                        };

                        var save = await _photoService.SavePhotoAsync(photo, CancellationToken);
                        if (save.IsFailure)
                        {
                            failedCount++;
                            firstError ??= save.Message;
                            _logger.Warn($"照片保存失败: {save.Message}");
                            continue;
                        }

                        savedCount++;
                        nextSeq++; // 编号往下走：本批成功保存一张推进一次
                        _logger.LogBusiness("采集档案照片",
                            ("Source", source),
                            ("ApplicationId", ApplicationId?.ToString() ?? ""),
                            ("Category", SelectedCategoryCode),
                            ("PhotoId", save.Value.ToString()),
                            ("PhotoName", photo.PhotoName ?? ""),
                            ("OriginalSize", raw.Length.ToString()),
                            ("StoredSize", photo.PhotoData.Length.ToString()),
                            ("Format", photo.ImageFormat));
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        firstError ??= ex.Message;
                        _logger.Warn($"照片处理失败: {ex.Message}");
                    }
                }

                return Result.Success();
            }, raws.Count > 1 ? $"上传 {raws.Count} 张照片..." : "保存照片...");
        }

        await LoadPhotosAsync();

        if (savedCount == 0 && failedCount == 0)
            return; // 全部取消

        // 2026-11-04 交互改版：上传后不再逐张弹「识别询问」（原流程批量上传后要一张一张确认，
        // 效率太低）；识别统一走底部「批量扫描识别」按钮——一次识别当前人员名下全部照片。
        var message = failedCount == 0
            ? $"已保存 {savedCount} 张照片（{SelectedCategoryName}）"
            : $"已保存 {savedCount} 张，失败 {failedCount} 张（{SelectedCategoryName}）" +
              (string.IsNullOrWhiteSpace(firstError) ? string.Empty : $"\n原因：{firstError}");
        if (_assignContext is not null && OcrFieldKeys.GetFields(SelectedCategoryCode) is not null)
            message += "\n\n点下方「批量扫描识别」可一次性识别全部照片。";
        await ShowTipAsync(message);
    }

    /// <summary>
    /// 批量扫描识别（2026-11-04 改版，替代原「上传后逐张询问」流程）：
    /// 一次识别**当前人员名下全部照片**（跨类别、含此前已识别的全部重新识别），
    /// 3 路并发隐藏传输开销，结果统一带来源照片（缩略图 + 标签）跳分配页，
    /// 失败/无内容的照片也进清单（分配页锁定「不使用」并显示失败原因）。
    /// </summary>
    [RelayCommand]
    private async Task BatchRecognizeAsync()
    {
        if (_assignContext is null)
        {
            await ShowTipAsync("当前入口不支持识别回填（仅存档模式）");
            return;
        }

        if (string.IsNullOrWhiteSpace(ApplicantIdCard))
        {
            await ShowTipAsync("缺少身份证号关联，无法识别");
            return;
        }

        // 1. 取当前人员名下全部照片（跨类别），过滤出可识别类别
        var allResult = await _photoService.GetByPersonAsync(
            ApplicantIdCard.Trim(), category: null, CancellationToken);
        if (allResult.IsFailure)
        {
            await ShowTipAsync(allResult.Message ?? "照片列表加载失败");
            return;
        }

        var photos = (allResult.Value ?? new List<ArchivePhoto>())
            .Where(p => OcrFieldKeys.GetFields(p.PhotoCategory) is not null)
            .ToList();
        if (photos.Count == 0)
        {
            await ShowTipAsync("该人员名下没有可识别类别的照片");
            return;
        }

        // 2. 预提示（模型名 / 张数 / 预计耗时 / 内网声明）
        var estimate = OcrConstants.FormatEstimate(photos.Count);
        var start = await _dialogService.DisplayAlertAsync(OcrConstants.PromptTitle,
            $"将识别 {ApplicantName} 名下全部 {photos.Count} 张照片（跨类别，含此前已识别的，全部重新识别）。\n\n" +
            $"预计耗时 {estimate}（{OcrConstants.EstimateRangeText}，多张并发实际更快）。\n" +
            "图片仅发送至内网识别服务，不上传外网。\n" +
            "识别完成后可为每条结果选择回填位置。",
            "开始识别", "取消");
        if (!start)
        {
            _logger.LogBusiness("取消批量证件 OCR 识别",
                ("Applicant", DataMasker.MaskName(ApplicantName)),
                ("ImageCount", photos.Count.ToString()));
            return;
        }

        // 3. 并发识别（3 路；进度按完成张数推进）。每条结果带来源照片溯源信息。
        var results = new OcrRecognizeResult?[photos.Count];
        await ExecuteAsync(async () =>
        {
            using var gate = new SemaphoreSlim(OcrConstants.BatchConcurrency);
            var done = 0;
            var tasks = photos.Select(async (photo, index) =>
            {
                await gate.WaitAsync(CancellationToken);
                try
                {
                    results[index] = await RecognizeOnePhotoAsync(photo);
                    var n = Interlocked.Increment(ref done);
                    MainThread.BeginInvokeOnMainThread(() =>
                        LoadingMessage = string.Format(OcrConstants.ProgressTextFormat, n, photos.Count));
                }
                finally
                {
                    gate.Release();
                }
            });
            await Task.WhenAll(tasks);
            return Result.Success();
        }, string.Format(OcrConstants.ProgressTextFormat, 0, photos.Count));

        var ordered = results.Where(r => r is not null).Cast<OcrRecognizeResult>().ToList();
        var successCount = ordered.Count(r => r.HasAny);
        _logger.LogBusiness("批量证件 OCR 识别完成",
            ("Applicant", DataMasker.MaskName(ApplicantName)),
            ("Total", ordered.Count.ToString()),
            ("Success", successCount.ToString()));

        if (successCount == 0)
        {
            var firstFail = ordered.FirstOrDefault(r => !string.IsNullOrEmpty(r.FailReason))?.FailReason;
            await _dialogService.DisplayAlertAsync("识别结果",
                "均未识别出有效字段，请正对证件、保证光线充足后重拍，或手动录入。" +
                (string.IsNullOrWhiteSpace(firstFail) ? string.Empty : $"\n{firstFail}"), "确定");
            return;
        }

        // 4. 跳转识别结果分配页（含失败照片的完整清单，用户逐条选择回填去向）
        var completion = new TaskCompletionSource<int>();
        await NavigateToPageAsync<Pages.Mobile.OcrAssignPage, OcrAssignParameter>(
            new OcrAssignParameter(ordered, _assignContext!, applied =>
            {
                completion.TrySetResult(applied);
            }));

        var appliedCount = await completion.Task;
        if (appliedCount > 0)
        {
            _logger.LogBusiness("证件 OCR 回填返回档案照片页",
                ("Applicant", DataMasker.MaskName(ApplicantName)),
                ("Applied", appliedCount.ToString()));
            await ShowSnackBarAsync($"已回填 {appliedCount} 条识别结果", Components.SnackBarType.Success);
        }

        await LoadPhotosAsync();
    }

    /// <summary>识别单张存档照片：取回二进制 → 生成缩略图 → 按照片自身类别识别 → 回填溯源信息。
    /// 任何失败都返回带 <see cref="OcrRecognizeResult.FailReason"/> 的空结果（进清单，不阻断其余）。</summary>
    private async Task<OcrRecognizeResult> RecognizeOnePhotoAsync(ArchivePhoto photo)
    {
        var label = photo.DisplayName;
        try
        {
            var dataResult = await _photoService.GetPhotoDataAsync(photo.Id, CancellationToken);
            if (dataResult.IsFailure || dataResult.Value is not { Length: > 0 })
            {
                return new OcrRecognizeResult
                {
                    Category = photo.PhotoCategory,
                    PhotoId = photo.Id,
                    PhotoLabel = label,
                    FailReason = dataResult.Message ?? "照片读取失败"
                };
            }

            var bytes = dataResult.Value;
            var thumb = ImageCompressor.CreateThumbnail(bytes, OcrConstants.ThumbnailMaxEdge);
            using var stream = new MemoryStream(bytes);
            var result = await _ocrClient.RecognizeAsync(stream, photo.PhotoCategory, CancellationToken);

            if (result.IsSuccess && result.Value is not null)
            {
                var value = result.Value;
                value.PhotoId = photo.Id;
                value.PhotoLabel = label;
                value.Thumbnail = thumb;
                if (!value.HasAny)
                    value.FailReason = "未识别出有效字段";
                return value;
            }

            _logger.Warn($"照片 {label} 识别失败: {result.ErrorCode} {result.Message}");
            return new OcrRecognizeResult
            {
                Category = photo.PhotoCategory,
                PhotoId = photo.Id,
                PhotoLabel = label,
                Thumbnail = thumb,
                FailReason = result.Message ?? "识别失败"
            };
        }
        catch (Exception ex)
        {
            _logger.Warn($"照片 {label} 识别异常: {ex.Message}");
            return new OcrRecognizeResult
            {
                Category = photo.PhotoCategory,
                PhotoId = photo.Id,
                PhotoLabel = label,
                FailReason = ex.Message
            };
        }
    }

    /// <summary>
    /// 导出到系统相册（Android MediaStore / Windows 图片目录）：便于外发打印或留档。
    /// 先选导出格式：WebP（库内原图、体积小）/ JPG（民政等外部系统不支持 WebP 时选它）。
    /// </summary>
    [RelayCommand]
    private async Task ExportPhotoAsync(ArchivePhoto? photo)
    {
        if (photo == null) return;

        // 取消选格式即放弃导出
        var choice = await _dialogService.DisplayActionSheetAsync(
            "导出格式", "取消", null,
            new[] { "WebP（原图，体积小）", "JPG（外部系统兼容）" });
        if (string.IsNullOrEmpty(choice) || choice == "取消") return;
        var asJpeg = choice.StartsWith("JPG", StringComparison.Ordinal);

        var album = _serviceProvider.GetRequiredService<Services.Platform.IAlbumExportService>();
        var exportedFormat = string.Empty;

        var result = await ExecuteAsync(async () =>
        {
            var data = await _photoService.GetPhotoDataAsync(photo.Id, CancellationToken);
            if (data.IsFailure || data.Value == null || data.Value.Length == 0)
                return Result.Failure(data.ErrorCode ?? ErrorCodes.NOT_FOUND, data.Message ?? "照片读取失败");

            var bytes = data.Value;
            var format = Helpers.ImageTranscoder.DetectFormat(bytes);
            if (string.IsNullOrEmpty(format))
                format = photo.ImageFormat;

            // 导出为 JPG：已是 JPEG 透传，其余（WebP/PNG）转 JPEG q90；转码失败显式失败，不静默回退原格式
            if (asJpeg && format != PhotoFormatConstants.Jpeg)
            {
                if (!Helpers.ImageTranscoder.TryToJpeg(bytes, out var jpegBytes))
                    return Result.Failure(ErrorCodes.UNKNOWN_ERROR, "转 JPG 失败（原图无法解码）");
                bytes = jpegBytes;
                format = PhotoFormatConstants.Jpeg;
            }

            // 文件名同步命名规则（2026-11-04）：优先用 PhotoName（姓名_身份证后4位_类别_编号），
            // 旧数据无名时回退原 NewCosmos_ 前缀格式；追加 Id 防同名覆盖。
            var baseName = PhotoNameBuilder.SanitizeFileName(photo.PhotoName);
            var fileName = string.IsNullOrEmpty(baseName)
                ? $"NewCosmos_{photo.ApplicantIdCard}_{photo.PhotoCategory}_{DateTime.Now:yyyyMMddHHmmss}_{photo.Id}"
                : $"{baseName}_{photo.Id}";
            fileName += PhotoFormatConstants.GetExtension(format);
            var saved = await album.SaveToAlbumAsync(
                fileName, bytes, PhotoFormatConstants.GetMimeType(format), CancellationToken);
            if (saved.IsFailure)
                return Result.Failure(saved.ErrorCode!, saved.Message!);

            exportedFormat = format;
            _logger.LogBusiness("档案照片导出到相册",
                ("PhotoId", photo.Id),
                ("Category", photo.PhotoCategory),
                ("Format", format),
                ("Target", saved.Value ?? string.Empty));
            return Result.Success();
        }, "导出到相册...");

        if (result.IsSuccess)
            await ShowTipAsync($"已导出到相册（{PhotoFormatConstants.GetDisplayName(exportedFormat)}）");
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
                BusinessType = PrintBusinessTypes.ArchivePhoto,
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

/// <summary>
/// 手机端照片列表行：包一层实体带异步缩略图（与桌面查询页 <c>ArchivePhotoListItem</c> 同构）。
/// 实体本身不加 UI 字段，避免污染数据映射。
/// </summary>
public partial class MobileArchivePhotoItem : ObservableObject
{
    public MobileArchivePhotoItem(ArchivePhoto photo)
    {
        Photo = photo;
    }

    public ArchivePhoto Photo { get; }

    public long Id => Photo.Id;
    public string DisplayName => Photo.DisplayName;
    public DateTime CreatedAt => Photo.CreatedAt;

    /// <summary>缩略图（异步加载；null = 未加载/失败，显示占位底色）。</summary>
    [ObservableProperty]
    private ImageSource? _thumbnail;

    public bool HasThumbnail => Thumbnail is not null;

    partial void OnThumbnailChanged(ImageSource? value) => OnPropertyChanged(nameof(HasThumbnail));
}
