using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.NearRelative;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 近亲属备案录入 ViewModel（A 形态：单条编辑）
/// 打开默认"新增备案"；工作人员身份证输入满 18 位自动处理：
///   ① 已存在有效备案 → 自动载入编辑（替代原"选择已有备案"下拉）
///   ② 未备案 → 多来源检索经办人员工作信息：系统用户优先，档案照料护理人补空
///      （姓名/联系方式/工作单位/职务/乡镇；来源：nc_sys_users → nc_biz_caregivers → nc_biz_destitute_persons）
/// 救助对象行输入身份证满 18 位 → 按本系统申请档案自动带出（一一对应回填关联申请ID）并锁定档案字段；
/// 编辑载入时已关联申请的行自动按档案刷新。
/// 保存只影响本条备案；版本校验：保存时比对 updated_at，他人已修改则提示刷新。
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
    private NearRelativeEntryParameter? _pendingParameter;

    /// <summary>上次工作人员身份证查询键（防重复查询；载入/清空时同步置位）</summary>
    private string _lastStaffLookupIdCard = string.Empty;

    #region 属性

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
        await ApplyPendingParameterAsync();
    }

    #region 导航参数（列表页编辑 / 工作流卡片户主上下文）

    /// <summary>由页面 SetParameterAsync 注入（OnAppearing 前调用），延后到首次出现时应用。</summary>
    public Task SetNavigationParameter(NearRelativeEntryParameter parameter)
    {
        _pendingParameter = parameter;
        return Task.CompletedTask;
    }

    private async Task ApplyPendingParameterAsync()
    {
        var parameter = _pendingParameter;
        _pendingParameter = null;
        if (parameter == null) return;

        // 编辑已有备案（列表页入口）：直接载入（不再走下拉开选择）
        if (parameter.StaffId > 0)
        {
            await LoadEntryAsync(parameter.StaffId);
            return;
        }

        // 工作流卡片入口：按申请ID从档案带出对象行（一一对应回填关联申请ID）
        if (parameter.ApplicationId is > 0)
            await ApplyFilingContextAsync(parameter.ApplicationId.Value,
                parameter.ApplicantName ?? string.Empty,
                parameter.ApplicantIdCard ?? string.Empty);
    }

    /// <summary>
    /// 工作流卡片入口：按申请档案带出完整对象行；档案取数失败时回退户主姓名/身份证（不阻断录入）。
    /// </summary>
    private async Task ApplyFilingContextAsync(long applicationId, string applicantName, string applicantIdCard)
    {
        var row = Links.FirstOrDefault(l => string.IsNullOrWhiteSpace(l.IdCard))
                  ?? (Links.Count == 1 && Links[0].IsEmpty ? Links[0] : null);
        if (row == null)
        {
            row = new NearRelativeLinkRow();
            Links.Add(row);
        }

        var fillResult = await _nearRelativeService.BuildLinkByApplicationAsync(
            applicationId, _staffId > 0 ? _staffId : null, CancellationToken);
        if (fillResult.IsSuccess && fillResult.Value != null)
        {
            ApplyArchiveFillToRow(row, fillResult.Value);
            StatusText = fillResult.Value.OccupiedByOtherStaff
                ? $"已关联申请 #{applicationId} 并带出对象信息；注意：该对象已由 [{fillResult.Value.OccupiedByStaffName}] 备案，保存将被拦截"
                : $"已关联申请 #{applicationId} 并按档案带出对象信息（补全与工作人员关系后保存）";
            _logger.Debug($"近亲属备案已带入户主上下文并读取档案: ApplicationId={applicationId}");
            return;
        }

        // 档案取数失败/未命中：回退为户主上下文（姓名/身份证），不阻断录入
        row.ApplicationId = applicationId.ToString();
        row.Name = applicantName;
        row.IdCard = applicantIdCard;
        StatusText = fillResult.IsFailure
            ? $"已关联申请 #{applicationId}（档案带出失败，请手工补全）"
            : $"已关联申请 #{applicationId} 户主 {applicantName}（补全关系等信息后保存）";
        _logger.Debug($"近亲属备案已带入户主上下文（未取到档案）: ApplicationId={applicationId}");
    }

    #endregion

    #region 身份证自动带出（工作人员）

    /// <summary>
    /// 工作人员身份证文本变化（页面 TextChanged 代码后置调用；WinUI 绑定失焦才提交源，故主动写回）。
    /// 满 18 位且校验码通过时：先查已有有效备案（载入编辑），否则按 系统用户 + 档案照料护理人 检索带出工作信息。
    /// </summary>
    public void OnStaffIdCardTextChanged(string newText)
    {
        newText = newText ?? string.Empty;
        if (StaffIdCard != newText)
            StaffIdCard = newText;

        if (!IdCardValidator.IsValid(newText)) return;
        if (string.Equals(_lastStaffLookupIdCard, newText, StringComparison.OrdinalIgnoreCase)) return;
        _lastStaffLookupIdCard = newText;
        SafeFireAndForget(() => LookupStaffAsync(newText));
    }

    private async Task LookupStaffAsync(string idCard)
    {
        // ① 已有有效备案 → 自动载入编辑（替代原下拉选择）
        var existing = await _nearRelativeService.GetByStaffIdCardAsync(idCard, CancellationToken);
        if (existing.IsFailure)
        {
            StatusText = existing.Message ?? "查询已有备案失败";
            return;
        }
        if (existing.Value != null)
        {
            if (_staffId == existing.Value.Staff.Id)
            {
                StatusText = $"编辑备案：{existing.Value.Staff.StaffName}（{existing.Value.Links.Count} 名对象）";
                return;
            }

            if (HasAnyOtherContent())
            {
                var confirm = await _dialogService.DisplayAlertAsync(
                    "提示",
                    $"该身份证已有备案（工作人员：{existing.Value.Staff.StaffName}），是否载入编辑？当前已填写内容将被覆盖。",
                    "载入", "取消");
                if (!confirm) return;
            }

            await LoadEntryAsync(existing.Value.Staff.Id);
            StatusText = $"已载入该身份证已有备案：{existing.Value.Staff.StaffName}（{existing.Value.Links.Count} 名对象）";
            _logger.Debug($"近亲属备案按身份证载入已有备案: StaffId={existing.Value.Staff.Id}");
            return;
        }

        // ② 系统用户 + 档案照料护理人（用户优先、照料人逐字段补空；乡镇用户优先否则取档案乡镇）
        var lookupResult = await _nearRelativeService.GetStaffWorkInfoByIdCardAsync(idCard, CancellationToken);
        if (lookupResult.IsFailure)
        {
            StatusText = lookupResult.Message ?? "查询经办人员工作信息失败";
            return;
        }
        if (lookupResult.Value == null)
        {
            StatusText = "未在系统用户及档案照料护理人中找到该身份证，请手工填写工作信息";
            return;
        }

        var info = lookupResult.Value.Staff;
        StaffName = info.StaffName;
        StaffPhone = info.StaffPhone;
        WorkUnit = info.WorkUnit;
        Position = info.Position;
        Town = info.Town;
        StatusText = BuildStaffLookupStatusText(lookupResult.Value);
        _logger.Debug(
            $"近亲属备案已带出经办人员工作信息（用户={lookupResult.Value.FoundInUsers}, " +
            $"申请档案照料人={lookupResult.Value.FoundInCaregivers}, 特困历史档案={lookupResult.Value.FoundInDestituteArchive}）");
    }

    /// <summary>经办人员带出来源提示文案（用户/档案照料人命中组合）</summary>
    private static string BuildStaffLookupStatusText(NearRelativeStaffLookup lookup)
    {
        var name = string.IsNullOrWhiteSpace(lookup.Staff.StaffName) ? "未命名" : lookup.Staff.StaffName;
        if (lookup.FoundInUsers)
        {
            return lookup.FoundInAnyCaregiver
                ? $"已从系统用户带出经办人员工作信息（{name}）；档案照料护理人另有记录，已补齐空缺信息"
                : $"已从系统用户带出经办人员工作信息（{name}）";
        }

        return $"已从档案照料护理人带出工作信息（{name}）";
    }

    /// <summary>表单（除工作人员身份证外）是否已有内容——自动载入已有备案前确认用</summary>
    private bool HasAnyOtherContent()
    {
        if (!string.IsNullOrWhiteSpace(StaffName)
            || !string.IsNullOrWhiteSpace(StaffPhone)
            || !string.IsNullOrWhiteSpace(WorkUnit)
            || !string.IsNullOrWhiteSpace(Position)
            || !string.IsNullOrWhiteSpace(Town))
            return true;
        return Links.Any(l => !l.IsEmpty);
    }

    #endregion

    #region 身份证自动带出（救助对象行）

    /// <summary>
    /// 对象行身份证文本变化（页面 TextChanged 代码后置调用；主动写回绑定）。
    /// 满 18 位且校验码通过时按本系统申请档案带出（一一对应回填关联申请ID）并锁定。
    /// </summary>
    public void OnLinkIdCardTextChanged(NearRelativeLinkRow row, string newText)
    {
        if (row == null) return;
        newText = newText ?? string.Empty;
        if (row.IdCard != newText)
            row.IdCard = newText;

        if (row.IsArchiveLinked || row.IsLookupBusy) return;
        if (!IdCardValidator.IsValid(newText)) return;
        if (string.Equals(row.LastLookupIdCard, newText, StringComparison.OrdinalIgnoreCase)) return;
        row.LastLookupIdCard = newText;
        SafeFireAndForget(() => LookupLinkAsync(row, newText));
    }

    private async Task LookupLinkAsync(NearRelativeLinkRow row, string idCard)
    {
        row.IsLookupBusy = true;
        try
        {
            var result = await _nearRelativeService.BuildLinkByIdCardAsync(
                idCard, _staffId > 0 ? _staffId : null, CancellationToken);
            if (result.IsFailure)
            {
                StatusText = result.Message ?? "档案查询失败";
                return;
            }
            if (result.Value == null)
            {
                StatusText = "未在本系统档案中找到该身份证的救助申请，请手工录入";
                return;
            }

            ApplyArchiveFillToRow(row, result.Value);
            StatusText = result.Value.OccupiedByOtherStaff
                ? $"已从档案带出：{FormatFillSource(result.Value)}；注意：该救助对象已由 [{result.Value.OccupiedByStaffName}] 备案，保存将被拦截"
                : $"已从档案带出：{FormatFillSource(result.Value)}";
            _logger.Debug($"近亲属备案对象行已从档案带出: ApplicationId={result.Value.ApplicationId}");
        }
        finally
        {
            row.IsLookupBusy = false;
        }
    }

    /// <summary>
    /// 把档案带出结果合并到对象行：只写档案来源字段（与工作人员关系等手工字段不动），并置锁定标记。
    /// </summary>
    private static void ApplyArchiveFillToRow(NearRelativeLinkRow row, NearRelativeArchiveFill fill)
    {
        var link = fill.Link;
        row.ApplicationId = link.ApplicationId?.ToString() ?? string.Empty;
        row.Name = link.Name;
        row.IdCard = link.IdCard;
        row.Gender = link.Gender;
        row.BirthDate = link.BirthDate;
        row.HukouAddress = link.HukouAddress;
        row.ResidenceAddress = link.ResidenceAddress;
        row.FamilyAddress = link.FamilyAddress;
        row.FamilySize = link.FamilySize?.ToString() ?? string.Empty;
        row.HelpType = link.HelpType;
        row.MonthAmount = link.MonthAmount?.ToString("F2") ?? string.Empty;
        row.ReportAmount = link.ReportAmount?.ToString("F2") ?? string.Empty;
        row.StartTime = link.StartTime;
        row.ApplyReason = link.ApplyReason;
        row.ApplyTime = link.ApplyTime;
        row.IsArchiveLinked = true;
        row.ArchiveSourceText = $"已关联：{FormatFillSource(fill)}";
    }

    private static string FormatFillSource(NearRelativeArchiveFill fill)
    {
        var no = string.IsNullOrWhiteSpace(fill.ApplicationNo) ? $"#{fill.ApplicationId}" : fill.ApplicationNo;
        var date = fill.ApplicationCreatedAt is { } d ? d.ToString("yyyy-MM-dd") : "-";
        return $"申请 {no}（{date}，{fill.StatusDisplay}）";
    }

    #endregion

    #region 加载

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
        _lastStaffLookupIdCard = entry.Staff.StaffIdCard ?? string.Empty;

        // 编辑载入：已关联申请的对象行按档案刷新（一一对应最新数据；失败保留原值不阻断）
        var appIds = entry.Links
            .Where(l => l.ApplicationId is > 0)
            .Select(l => l.ApplicationId!.Value)
            .Distinct()
            .ToList();
        var fillsByAppId = new Dictionary<long, NearRelativeArchiveFill>();
        var refreshNote = string.Empty;
        if (appIds.Count > 0)
        {
            var refreshResult = await _nearRelativeService.RefreshLinksFromArchiveAsync(
                appIds, _staffId > 0 ? _staffId : null, CancellationToken);
            if (refreshResult.IsSuccess && refreshResult.Value != null)
            {
                fillsByAppId = refreshResult.Value
                    .GroupBy(f => f.ApplicationId)
                    .ToDictionary(g => g.Key, g => g.First());
                if (fillsByAppId.Count > 0)
                    refreshNote = $"；已按档案刷新 {fillsByAppId.Count} 行";
                var missing = appIds.Count - fillsByAppId.Count;
                if (missing > 0)
                    refreshNote += $"；{missing} 行未找到对应申请（保留原值）";
            }
            else
            {
                refreshNote = "；档案刷新失败（保留原值）";
                _logger.Warn($"编辑载入按档案刷新失败: {refreshResult.Message}");
            }
        }

        var staff = entry.Staff;
        var entryLinks = entry.Links;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            StaffName = staff.StaffName;
            StaffIdCard = staff.StaffIdCard ?? string.Empty;
            StaffPhone = staff.StaffPhone;
            WorkUnit = staff.WorkUnit;
            Position = staff.Position;
            Town = staff.Town;

            Links.Clear();
            foreach (var link in entryLinks)
            {
                var row = NearRelativeLinkRow.FromEntity(link);
                if (link.ApplicationId is > 0 && fillsByAppId.TryGetValue(link.ApplicationId.Value, out var fill))
                {
                    ApplyArchiveFillToRow(row, fill);
                }
                else if (link.ApplicationId is > 0)
                {
                    row.IsArchiveLinked = true;
                    row.ArchiveSourceText = $"已关联申请 #{link.ApplicationId.Value}";
                }
                Links.Add(row);
            }
            if (Links.Count == 0)
                Links.Add(new NearRelativeLinkRow());

            StatusText = $"编辑备案：{staff.StaffName}（{entryLinks.Count} 名对象）{refreshNote}";
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
        _lastStaffLookupIdCard = string.Empty;
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

        // 保存成功：重新载入本条（刷新版本号与归档链接，避免下次误判冲突）
        _staffId = result.Value;
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
/// 档案带出（IsArchiveLinked=true）后档案来源字段只读锁定；与工作人员关系等手工字段仍可编辑。
/// </summary>
public partial class NearRelativeLinkRow : ObservableObject
{
    public long Id { get; set; }

    /// <summary>是否已从档案带出（置位后档案来源字段只读）</summary>
    [ObservableProperty]
    private bool _isArchiveLinked;

    /// <summary>档案来源提示（如"已关联：申请 MX2026...（2026-01-15，已审批）"）</summary>
    [ObservableProperty]
    private string _archiveSourceText = string.Empty;

    /// <summary>查询进行中（非 UI，防重复触发）</summary>
    public bool IsLookupBusy { get; set; }

    /// <summary>上次已查询的身份证（防同值重复查询）</summary>
    public string LastLookupIdCard { get; set; } = string.Empty;

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

    /// <summary>是否所有字段为空（工作人员自动载入已有备案前确认用）</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(ApplicationId) && string.IsNullOrWhiteSpace(Relation)
        && string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(IdCard)
        && string.IsNullOrWhiteSpace(Gender) && string.IsNullOrWhiteSpace(BirthDate)
        && string.IsNullOrWhiteSpace(FamilyAddress) && string.IsNullOrWhiteSpace(HukouAddress)
        && string.IsNullOrWhiteSpace(ResidenceAddress) && string.IsNullOrWhiteSpace(FamilySize)
        && string.IsNullOrWhiteSpace(HelpType) && string.IsNullOrWhiteSpace(MonthAmount)
        && string.IsNullOrWhiteSpace(ReportAmount) && string.IsNullOrWhiteSpace(StartTime)
        && string.IsNullOrWhiteSpace(FamilyRelation) && string.IsNullOrWhiteSpace(ApplyReason)
        && string.IsNullOrWhiteSpace(ApplyTime) && string.IsNullOrWhiteSpace(FamilyDifficulty)
        && string.IsNullOrWhiteSpace(EconomyInvestigate) && string.IsNullOrWhiteSpace(TownOpinion)
        && string.IsNullOrWhiteSpace(DynamicRecord);

    /// <summary>解除档案关联：保留当前值转手工可改（关联申请ID一并清空）</summary>
    [RelayCommand]
    private void UnlinkArchive()
    {
        IsArchiveLinked = false;
        ArchiveSourceText = string.Empty;
        ApplicationId = string.Empty;
        LastLookupIdCard = string.Empty;
    }

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
