using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.NearRelative;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 近亲属备案录入 ViewModel（A 形态：单条编辑）
/// 打开默认"新增备案"；顶部下拉可选择已有备案载入编辑；保存只影响本条备案。
/// 版本校验：保存时比对 updated_at，他人已修改则提示刷新。
/// </summary>
public partial class NearRelativeEntryViewModel : ViewModelBase
{
    private readonly INearRelativeService _nearRelativeService = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IServiceProvider _serviceProvider = null!;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    private long _staffId;
    private DateTime? _loadedUpdatedAt;

    #region 属性

    [ObservableProperty]
    private ObservableCollection<NearRelativeBrief> _staffBriefs = new();

    [ObservableProperty]
    private NearRelativeBrief? _selectedBrief;

    [ObservableProperty]
    private string _staffName = string.Empty;

    [ObservableProperty]
    private string _staffIdCard = string.Empty;

    [ObservableProperty]
    private string _staffPhone = string.Empty;

    [ObservableProperty]
    private string _workUnit = string.Empty;

    [ObservableProperty]
    private string _position = string.Empty;

    [ObservableProperty]
    private string _town = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<NearRelativeLinkRow> _links = new();

    [ObservableProperty]
    private NearRelativeLinkRow? _selectedLink;

    public bool IsEditingExisting => _staffId > 0;

    #endregion

    public NearRelativeEntryViewModel(
        INearRelativeService nearRelativeService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider)
    {
        _nearRelativeService = nearRelativeService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        Title = "近亲属备案";
        StatusText = "新增备案模式";
        Links.Add(new NearRelativeLinkRow());
    }

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await LoadBriefsAsync();
    }

    #region 加载

    private async Task LoadBriefsAsync()
    {
        var result = await _nearRelativeService.GetBriefsAsync(ct: CancellationToken);
        if (result.IsFailure)
        {
            await ShowErrorAsync(result.Message);
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var previous = SelectedBrief?.Id;
            StaffBriefs.Clear();
            foreach (var b in result.Value)
                StaffBriefs.Add(b);
            if (previous.HasValue)
                SelectedBrief = StaffBriefs.FirstOrDefault(b => b.Id == previous.Value);
        });
    }

    partial void OnSelectedBriefChanged(NearRelativeBrief? value)
    {
        if (value == null)
            return;
        SafeFireAndForget(() => LoadEntryAsync(value.Id));
    }

    private async Task LoadEntryAsync(long id)
    {
        var result = await _nearRelativeService.GetByIdAsync(id, CancellationToken);
        if (result.IsFailure || result.Value == null)
        {
            await ShowErrorAsync(result.Message ?? "备案不存在");
            return;
        }

        var entry = result.Value;
        _staffId = entry.Staff.Id;
        _loadedUpdatedAt = entry.Staff.UpdatedAt;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            StaffName = entry.Staff.StaffName;
            StaffIdCard = entry.Staff.StaffIdCard;
            StaffPhone = entry.Staff.StaffPhone;
            WorkUnit = entry.Staff.WorkUnit;
            Position = entry.Staff.Position;
            Town = entry.Staff.Town;

            Links.Clear();
            foreach (var link in entry.Links)
                Links.Add(NearRelativeLinkRow.FromEntity(link));
            if (Links.Count == 0)
                Links.Add(new NearRelativeLinkRow());

            StatusText = $"编辑备案：{entry.Staff.StaffName}（{entry.Links.Count} 名对象）";
            OnPropertyChanged(nameof(IsEditingExisting));
        });
    }

    #endregion

    #region 命令

    /// <summary>新增备案（清空表单）</summary>
    [RelayCommand]
    private void NewEntry()
    {
        _staffId = 0;
        _loadedUpdatedAt = null;
        SelectedBrief = null;
        StaffName = string.Empty;
        StaffIdCard = string.Empty;
        StaffPhone = string.Empty;
        WorkUnit = string.Empty;
        Position = string.Empty;
        Town = string.Empty;
        Links.Clear();
        Links.Add(new NearRelativeLinkRow());
        StatusText = "新增备案模式";
        OnPropertyChanged(nameof(IsEditingExisting));
    }

    /// <summary>添加对象行</summary>
    [RelayCommand]
    private void AddLink()
    {
        Links.Add(new NearRelativeLinkRow());
    }

    /// <summary>删除对象行</summary>
    [RelayCommand]
    private void RemoveLink(NearRelativeLinkRow? link)
    {
        if (link == null) return;
        Links.Remove(link);
        if (Links.Count == 0)
            Links.Add(new NearRelativeLinkRow());
    }

    /// <summary>保存（只影响本条备案）</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(StaffName))
        {
            await _dialogService.DisplayAlertAsync("提示", "请填写工作人员姓名", "确定");
            return;
        }
        if (string.IsNullOrWhiteSpace(StaffIdCard))
        {
            await _dialogService.DisplayAlertAsync("提示", "请填写工作人员身份证号", "确定");
            return;
        }

        var entry = new NearRelativeEntry
        {
            Staff = new NearRelativeStaff
            {
                Id = _staffId,
                StaffName = StaffName.Trim(),
                StaffIdCard = StaffIdCard.Trim(),
                StaffPhone = StaffPhone.Trim(),
                WorkUnit = WorkUnit.Trim(),
                Position = Position.Trim(),
                Town = Town.Trim(),
                UpdatedAt = _loadedUpdatedAt,
                CreatedBy = App.CurrentUserFullName,
                UpdatedBy = App.CurrentUserFullName,
            },
            Links = Links
                .Where(l => !string.IsNullOrWhiteSpace(l.Name))
                .Select(l => l.ToEntity())
                .ToList()
        };

        var result = await ExecuteAsync(
            () => _nearRelativeService.SaveAsync(entry, CancellationToken),
            "正在保存备案...");

        if (result.IsFailure)
        {
            await ShowErrorAsync(result.Message);
            return;
        }

        // 保存成功：刷新下拉并重新载入本条（更新版本号，避免下次误判冲突）
        _staffId = result.Value;
        await LoadBriefsAsync();
        var brief = StaffBriefs.FirstOrDefault(b => b.Id == _staffId);
        SelectedBrief = brief;
        if (brief == null)
            await LoadEntryAsync(_staffId);

        await _dialogService.DisplayAlertAsync("成功", "近亲属备案已保存", "确定");
    }

    /// <summary>删除本条备案</summary>
    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_staffId == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "当前为新增模式，无需删除", "确定");
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认删除",
            $"确定要删除 [{StaffName}] 的备案及其 {Links.Count} 名对象吗？此操作不可恢复。",
            "删除", "取消");
        if (!confirm) return;

        var result = await ExecuteAsync(
            () => _nearRelativeService.DeleteAsync(_staffId, CancellationToken),
            "正在删除...");
        if (result.IsFailure)
        {
            await ShowErrorAsync(result.Message);
            return;
        }

        _logger.LogBusiness("删除近亲属备案", ("StaffId", _staffId.ToString()));
        await LoadBriefsAsync();
        NewEntry();
        await _dialogService.DisplayAlertAsync("成功", "备案已删除", "确定");
    }

    // 返回上一页：使用基类 GoBackCommand（含栈守卫与窗口标题恢复）

    private async Task ShowErrorAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        await _dialogService.DisplayAlertAsync("提示", message, "确定");
    }

    #endregion
}

/// <summary>
/// 对象行编辑模型（字符串字段，UI 绑定稳定；保存时映射为实体）
/// </summary>
public partial class NearRelativeLinkRow : ObservableObject
{
    public long Id { get; set; }

    /// <summary>关联申请ID（可选，档案输出用）</summary>
    [ObservableProperty]
    private string _applicationId = string.Empty;

    [ObservableProperty]
    private string _relation = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _idCard = string.Empty;

    [ObservableProperty]
    private string _gender = string.Empty;

    [ObservableProperty]
    private string _birthDate = string.Empty;

    [ObservableProperty]
    private string _familyAddress = string.Empty;

    [ObservableProperty]
    private string _hukouAddress = string.Empty;

    [ObservableProperty]
    private string _residenceAddress = string.Empty;

    [ObservableProperty]
    private string _familySize = string.Empty;

    [ObservableProperty]
    private string _helpType = string.Empty;

    [ObservableProperty]
    private string _monthAmount = string.Empty;

    [ObservableProperty]
    private string _reportAmount = string.Empty;

    [ObservableProperty]
    private string _startTime = string.Empty;

    [ObservableProperty]
    private string _familyRelation = string.Empty;

    [ObservableProperty]
    private string _applyReason = string.Empty;

    [ObservableProperty]
    private string _applyTime = string.Empty;

    [ObservableProperty]
    private string _familyDifficulty = string.Empty;

    [ObservableProperty]
    private string _economyInvestigate = string.Empty;

    [ObservableProperty]
    private string _townOpinion = string.Empty;

    [ObservableProperty]
    private string _dynamicRecord = string.Empty;

    public static NearRelativeLinkRow FromEntity(NearRelativeLink l) => new()
    {
        Id = l.Id,
        ApplicationId = l.ApplicationId?.ToString() ?? string.Empty,
        Relation = l.Relation,
        Name = l.Name,
        IdCard = l.IdCard,
        Gender = l.Gender,
        BirthDate = l.BirthDate,
        FamilyAddress = l.FamilyAddress,
        HukouAddress = l.HukouAddress,
        ResidenceAddress = l.ResidenceAddress,
        FamilySize = l.FamilySize?.ToString() ?? string.Empty,
        HelpType = l.HelpType,
        MonthAmount = l.MonthAmount?.ToString("F2") ?? string.Empty,
        ReportAmount = l.ReportAmount?.ToString("F2") ?? string.Empty,
        StartTime = l.StartTime,
        FamilyRelation = l.FamilyRelation,
        ApplyReason = l.ApplyReason,
        ApplyTime = l.ApplyTime,
        FamilyDifficulty = l.FamilyDifficulty,
        EconomyInvestigate = l.EconomyInvestigate,
        TownOpinion = l.TownOpinion,
        DynamicRecord = l.DynamicRecord,
    };

    public NearRelativeLink ToEntity() => new()
    {
        Id = Id,
        ApplicationId = long.TryParse(ApplicationId?.Trim(), out var appId) && appId > 0 ? appId : null,
        Relation = Relation?.Trim() ?? string.Empty,
        Name = Name?.Trim() ?? string.Empty,
        IdCard = IdCard?.Trim() ?? string.Empty,
        Gender = Gender?.Trim() ?? string.Empty,
        BirthDate = BirthDate?.Trim() ?? string.Empty,
        FamilyAddress = FamilyAddress?.Trim() ?? string.Empty,
        HukouAddress = HukouAddress?.Trim() ?? string.Empty,
        ResidenceAddress = ResidenceAddress?.Trim() ?? string.Empty,
        FamilySize = int.TryParse(FamilySize?.Trim(), out var fs) && fs > 0 ? fs : null,
        HelpType = HelpType?.Trim() ?? string.Empty,
        MonthAmount = decimal.TryParse(MonthAmount?.Trim(), out var ma) ? ma : null,
        ReportAmount = decimal.TryParse(ReportAmount?.Trim(), out var ra) ? ra : null,
        StartTime = StartTime?.Trim() ?? string.Empty,
        FamilyRelation = FamilyRelation?.Trim() ?? string.Empty,
        ApplyReason = ApplyReason?.Trim() ?? string.Empty,
        ApplyTime = ApplyTime?.Trim() ?? string.Empty,
        FamilyDifficulty = FamilyDifficulty?.Trim() ?? string.Empty,
        EconomyInvestigate = EconomyInvestigate?.Trim() ?? string.Empty,
        TownOpinion = TownOpinion?.Trim() ?? string.Empty,
        DynamicRecord = DynamicRecord?.Trim() ?? string.Empty,
    };
}
