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
using System.ComponentModel;

namespace NewCosmos.ViewModels.Mobile;

/// <summary>
/// 手机端·历史档案照片补传：按业务域列出人员与已传照片数，手工勾选后
/// ① 逐人连续采集（复用档案照片页，带会话队列）；② 推送照片打印（PC 端执行）。
/// 照片按身份证归档，补传不改变任何业务表，老档案随即可用。
/// </summary>
public partial class MobilePhotoBackfillViewModel : PagedSearchViewModelBase
{
    private readonly IArchivePhotoService _photoService;
    private readonly IPrintQueueService _printQueueService;
    private readonly INewPermissionService _permissionService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    #region 筛选状态

    /// <summary>业务域过滤：空=全部，或 ElderlyBenefits / SocialAssistance / TempRelief。</summary>
    [ObservableProperty]
    private string _selectedDomain = string.Empty;

    [ObservableProperty]
    private string _keyword = string.Empty;

    /// <summary>是否已执行过搜索（未搜索时列表为空，提示用户输入姓名/身份证）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyHint))]
    private bool _hasSearched;

    /// <summary>类别选项（Code + 中文名），供搜索条 Picker 绑定；选定后透传采集页作为本次补传的预设类别。</summary>
    public ObservableCollection<PhotoCategoryOption> CategoryOptions { get; } = new();

    /// <summary>Picker 选中项（null = 不预设，采集页保持自身默认）。</summary>
    [ObservableProperty]
    private PhotoCategoryOption? _selectedCategoryOption;

    /// <summary>列表空态提示。</summary>
    public string EmptyHint => HasSearched
        ? "没有匹配的人员，换个关键词或切换业务域再试"
        : "输入姓名或身份证号后点「搜索」";

    public bool IsDomainAll => string.IsNullOrEmpty(SelectedDomain);
    public bool IsDomainElderly => SelectedDomain == "ElderlyBenefits";
    public bool IsDomainAssistance => SelectedDomain == "SocialAssistance";
    public bool IsDomainTempRelief => SelectedDomain == "TempRelief";

    partial void OnSelectedDomainChanged(string value)
    {
        OnPropertyChanged(nameof(IsDomainAll));
        OnPropertyChanged(nameof(IsDomainElderly));
        OnPropertyChanged(nameof(IsDomainAssistance));
        OnPropertyChanged(nameof(IsDomainTempRelief));
        PageIndex = 1;
        // 域切换只在已有搜索条件时重新检索（未搜索保持空列表）
        if (HasSearched) SafeFireAndForget(LoadDataAsync);
    }

    #endregion

    #region 列表

    [ObservableProperty]
    private ObservableCollection<PhotoBackfillCandidate> _candidates = new();

    /// <summary>当前勾选人数（底部按钮文案）。</summary>
    public int SelectedCount => Candidates.Count(c => c.IsSelected);

    /// <summary>勾选中已有照片的人数（可打印）。</summary>
    public int SelectedWithPhotoCount => Candidates.Count(c => c.IsSelected && c.HasPhoto);

    public string BackfillButtonText => SelectedCount > 0 ? $"补传照片({SelectedCount})" : "补传照片";

    public string PrintButtonText => SelectedWithPhotoCount > 0 ? $"打印选中({SelectedWithPhotoCount})" : "打印选中";

    public bool IsAllSelected => Candidates.Count > 0 && Candidates.All(c => c.IsSelected);

    /// <summary>「全选」按钮动态文案（与档案照片查询页/档案输出页一致：全选 ↔ 取消全选）。</summary>
    public string SelectAllText => IsAllSelected ? "取消全选" : "全选";

    public MobilePhotoBackfillViewModel(
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

        PageSize = 20;
        Title = "照片补传";

        // 类别预设默认 = 身份证（与采集页默认一致，不改变既有行为）
        foreach (var (code, name) in PhotoCategoryConstants.All)
            CategoryOptions.Add(new PhotoCategoryOption(code, name));
        _selectedCategoryOption = CategoryOptions.FirstOrDefault(o => o.Code == PhotoCategoryConstants.ID_CARD);
    }

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        // 从采集页返回后刷新（照片数会变化）
        await LoadDataAsync();
    }

    protected override async Task LoadDataAsync()
    {
        // 独立功能：必须先在搜索框输入姓名/身份证才检索（避免一进来就拉全库人员）
        var keyword = Keyword?.Trim() ?? string.Empty;
        if (keyword.Length == 0)
        {
            foreach (var item in Candidates)
                item.PropertyChanged -= OnCandidatePropertyChanged;
            Candidates.Clear();
            TotalCount = 0;
            HasSearched = false;
            NotifySelectionChanged();
            return;
        }

        HasSearched = true;

        await ExecuteAsync(async () =>
        {
            var result = await _photoService.GetBackfillCandidatesAsync(
                string.IsNullOrEmpty(SelectedDomain) ? null : SelectedDomain,
                keyword, PageIndex, PageSize, CancellationToken);

            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);

            foreach (var item in Candidates)
                item.PropertyChanged -= OnCandidatePropertyChanged;

            Candidates.Clear();
            foreach (var item in result.Value?.Items ?? new List<PhotoBackfillCandidate>())
            {
                item.PropertyChanged += OnCandidatePropertyChanged;
                Candidates.Add(item);
            }

            TotalCount = result.Value?.TotalCount ?? 0;
            NotifySelectionChanged();
            return Result.Success();
        }, "搜索人员...");
    }

    private void OnCandidatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PhotoBackfillCandidate.IsSelected))
            NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(BackfillButtonText));
        OnPropertyChanged(nameof(PrintButtonText));
        OnPropertyChanged(nameof(IsAllSelected));
        OnPropertyChanged(nameof(SelectAllText));
    }

    #endregion

    #region 命令

    [RelayCommand]
    private void SwitchDomain(string? domain)
    {
        SelectedDomain = domain switch
        {
            "ElderlyBenefits" => "ElderlyBenefits",
            "SocialAssistance" => "SocialAssistance",
            "TempRelief" => "TempRelief",
            _ => string.Empty
        };
    }

    [RelayCommand]
    private void ToggleSelectAll()
    {
        var selectAll = !IsAllSelected;
        foreach (var item in Candidates)
            item.IsSelected = selectAll;
        NotifySelectionChanged();
    }

    /// <summary>清空勾选（与档案照片查询页 ClearSelection 同口径）。</summary>
    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in Candidates)
            item.IsSelected = false;
        NotifySelectionChanged();
    }

    /// <summary>单人查看/补传（行内「查看」按钮）。</summary>
    [RelayCommand]
    private async Task OpenPersonAsync(PhotoBackfillCandidate? candidate)
    {
        if (candidate == null) return;
        await NavigateToPageAsync<Pages.Mobile.MobileArchivePhotoPage, ArchivePhotoParameter>(
            new ArchivePhotoParameter(candidate.IdCard, candidate.BusinessId, candidate.PersonName,
                PresetCategory: SelectedCategoryOption?.Code));
    }

    /// <summary>补传照片：把勾选人员组成会话队列，进入照片页后可连续"下一位"采集。</summary>
    [RelayCommand]
    private async Task BackfillSelectedAsync()
    {
        var selected = Candidates.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先勾选需要补传照片的人员", "确定");
            return;
        }

        var queue = selected
            .Select(c => new PhotoBackfillPerson(c.IdCard, c.BusinessId, c.PersonName))
            .ToList();

        _logger.LogBusiness("历史档案照片补传-开始采集",
            ("Count", queue.Count),
            ("Domain", string.IsNullOrEmpty(SelectedDomain) ? "全部" : SelectedDomain));

        await NavigateToPageAsync<Pages.Mobile.MobileArchivePhotoPage, ArchivePhotoParameter>(
            new ArchivePhotoParameter(queue[0].IdCard, queue[0].ApplicationId, queue[0].Name, queue, 0,
                PresetCategory: SelectedCategoryOption?.Code));
    }

    /// <summary>
    /// 打印选中：仅对已有照片的人入队（无照片跳过），版式统一选一次，任务由 PC 端打印代理执行。
    /// </summary>
    [RelayCommand]
    private async Task PrintSelectedAsync()
    {
        var selected = Candidates.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先勾选需要打印照片的人员", "确定");
            return;
        }

        var withPhoto = selected.Where(c => c.HasPhoto).ToList();
        var skipped = selected.Count - withPhoto.Count;
        if (withPhoto.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "选中人员均无照片，请先补传后再打印", "确定");
            return;
        }

        // 权限门控（与采集页/查询页共用 PhotoPrintPushHelper，同一权限码）
        if (!await PhotoPrintPushHelper.EnsurePrintPermissionAsync(
                _permissionService, _dialogService, CancellationToken))
            return;

        var skipHint = skipped > 0 ? $"（{skipped} 人无照片将跳过）" : string.Empty;
        var confirm = await _dialogService.DisplayAlertAsync("确认打印",
            $"将为 {withPhoto.Count} 人推送照片打印{skipHint}，任务由电脑端执行。", "继续", "取消");
        if (!confirm) return;

        // 选版式：与档案照片查询页同款「按默认 + 三档强制」；取消则中止
        var choice = await _dialogService.DisplayActionSheetAsync(
            "选择打印版式", "取消", null, PhotoLayoutConstants.BuildChoiceButtons());
        if (string.IsNullOrEmpty(choice) || choice == "取消") return;

        // null = 按默认 → 逐张按类别取默认版式（身份证原寸，其余整页）
        var forcedLayout = PhotoLayoutConstants.FromDisplayName(choice);

        await ExecuteAsync(async () =>
        {
            // 按人取照片 → 组推送项（取图/组任务/入队循环收敛到 PhotoPrintPushHelper，与查询页共用）
            var failedPeople = new List<string>();
            var items = new List<PhotoPrintPushHelper.PushItem>();

            foreach (var person in withPhoto)
            {
                try
                {
                    var photosResult = await _photoService.GetByPersonAsync(person.IdCard, null, CancellationToken);
                    if (photosResult.IsFailure || photosResult.Value == null || photosResult.Value.Count == 0)
                    {
                        failedPeople.Add(person.PersonName);
                        continue;
                    }

                    foreach (var photo in photosResult.Value)
                    {
                        items.Add(new PhotoPrintPushHelper.PushItem(
                            Photo: photo,
                            BusinessId: person.BusinessId,
                            ApplicantName: person.PersonName,
                            ApplicantIdCard: person.IdCard,
                            FailureLabel: person.PersonName));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"推送照片打印失败: {DataMasker.MaskName(person.PersonName)}");
                    failedPeople.Add(person.PersonName);
                }
            }

            var push = await PhotoPrintPushHelper.PushAsync(
                _photoService, _printQueueService, items, forcedLayout, CancellationToken);
            var successPhotos = push.OkCount;

            _logger.LogBusiness("历史档案照片补传-推送打印",
                ("People", withPhoto.Count),
                ("Photos", successPhotos),
                ("Layout", forcedLayout ?? "按默认"));

            // 失败汇总：取图阶段失败的人 + 逐张入队失败（label = 人员姓名），去重后按人展示
            var failedAll = failedPeople
                .Concat(push.Failures.Select(f => f.Split('：')[0]))
                .Distinct()
                .ToList();

            var message = $"已推送 {successPhotos} 张照片打印，电脑端登录后自动打印。";
            if (skipped > 0) message += $"\n{skipped} 人无照片已跳过。";
            if (failedAll.Count > 0) message += $"\n失败 {failedAll.Count} 人：{string.Join("、", failedAll.Take(5))}";

            await _dialogService.DisplayAlertAsync(successPhotos > 0 ? "已推送打印" : "推送失败", message, "确定");
            return Result.Success();
        }, "推送照片打印...");
    }

    #endregion
}
