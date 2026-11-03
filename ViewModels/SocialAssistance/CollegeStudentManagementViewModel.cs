using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Constants;
using NewCosmos.Models.DeathStatement;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Navigation;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 大学生管理页面 ViewModel（Tab化：可关联/已关联/不在范围内/今年毕业）
/// </summary>
public partial class CollegeStudentManagementViewModel : ViewModelBase
{
    private readonly ICollegeStudentService _collegeStudentService;
    private readonly IDialogService _dialogService;
    private readonly IDeathStatementService _deathStatementService;
    private readonly INewPermissionService _permissionService;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerService _logger;

    #region 死亡待退出（死亡联动待办 College 域，2026-10-31 方案2 承接面）

    /// <summary>死亡待退出任务（死亡申报触发；COLLEGE_MANAGE 承接人可见）</summary>
    [ObservableProperty]
    private ObservableCollection<DeathLinkageTaskItem> _deathCollegeTasks = new();

    /// <summary>是否有死亡待退出任务（区块显隐：权限 ∧ 有任务，二者合一避免空壳区块）</summary>
    [ObservableProperty]
    private bool _hasDeathCollegeTasks;

    [ObservableProperty]
    private string _deathCollegeCountText = "0";

    #endregion

    #region Tab 状态

    [ObservableProperty]
    private int _selectedTabIndex;

    public bool IsTab0Selected => SelectedTabIndex == 0;
    public bool IsTab1Selected => SelectedTabIndex == 1;
    public bool IsTab2Selected => SelectedTabIndex == 2;
    public bool IsTab3Selected => SelectedTabIndex == 3;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTab0Selected));
        OnPropertyChanged(nameof(IsTab1Selected));
        OnPropertyChanged(nameof(IsTab2Selected));
        OnPropertyChanged(nameof(IsTab3Selected));

        ErrorMessage = string.Empty;
        _ = LoadCurrentTabDataAsync();
    }

    #endregion

    #region Tab 数据

    /// <summary>Tab0: 可关联的18-23岁家庭成员</summary>
    [ObservableProperty]
    private ObservableCollection<FamilyMember> _eligibleMembers = new();

    [ObservableProperty]
    private bool _hasEligibleMembers;

    /// <summary>Tab1: 已关联大学生档案（在读+已毕业+未收到高等教育）</summary>
    [ObservableProperty]
    private ObservableCollection<CollegeStudent> _associatedStudents = new();

    [ObservableProperty]
    private bool _hasAssociatedStudents;

    /// <summary>Tab2: 不在范围内（已退出/超择业期）</summary>
    [ObservableProperty]
    private ObservableCollection<CollegeStudent> _notInScopeStudents = new();

    [ObservableProperty]
    private bool _hasNotInScopeStudents;

    /// <summary>Tab3: 今年毕业</summary>
    [ObservableProperty]
    private ObservableCollection<CollegeStudent> _graduatingStudents = new();

    [ObservableProperty]
    private bool _hasGraduatingStudents;

    #endregion

    #region 统计计数

    [ObservableProperty]
    private string _eligibleCountText = "0";

    [ObservableProperty]
    private string _associatedCountText = "0";

    [ObservableProperty]
    private string _notInScopeCountText = "0";

    [ObservableProperty]
    private string _graduatingCountText = "0";

    #endregion

    #region 搜索

    /// <summary>搜索关键词（姓名或身份证）</summary>
    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    #endregion

    #region 编辑表单

    /// <summary>是否显示编辑表单</summary>
    [ObservableProperty]
    private bool _isEditorVisible;

    /// <summary>编辑标题（新增/编辑）</summary>
    [ObservableProperty]
    private string _editorTitle = string.Empty;

    private long _editingId;
    private long _editingApplicationId;
    private long _editingFamilyMemberId;

    /// <summary>大学生姓名</summary>
    [ObservableProperty]
    private string _editStudentName = string.Empty;

    /// <summary>身份证号</summary>
    [ObservableProperty]
    private string _editIdCard = string.Empty;

    /// <summary>学历层次</summary>
    [ObservableProperty]
    private string? _editEducationLevel;

    /// <summary>学校名称</summary>
    [ObservableProperty]
    private string _editSchoolName = string.Empty;

    /// <summary>学制显示文本</summary>
    [ObservableProperty]
    private string? _editSchoolDuration;

    /// <summary>入学年份</summary>
    [ObservableProperty]
    private int? _editEnrollmentYear;

    /// <summary>状态</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNoHigherEducation))]
    [NotifyPropertyChangedFor(nameof(IsSchoolInfoRequired))]
    private string? _editStatus;

    /// <summary>是否"未收到高等教育"（免填学校信息）</summary>
    public bool IsNoHigherEducation => EditStatus == CollegeStudentConstants.StatusNoHigherEducation;

    /// <summary>是否需要填写学校信息（反向）</summary>
    public bool IsSchoolInfoRequired => !IsNoHigherEducation;

    #endregion

    /// <summary>学历层次选项</summary>
    public string[] EducationLevelOptions => CollegeStudentConstants.EducationLevelOptions;

    /// <summary>学制选项</summary>
    public string[] SchoolDurationOptions => CollegeStudentConstants.SchoolDurationOptions;

    /// <summary>状态选项</summary>
    public string[] StatusOptions { get; } =
    {
        CollegeStudentConstants.StatusStudying,
        CollegeStudentConstants.StatusGraduated,
        CollegeStudentConstants.StatusNotEligible,
        CollegeStudentConstants.StatusNoHigherEducation
    };

    /// <summary>入学年份选项（近15年）</summary>
    public List<int> EnrollmentYearOptions { get; } =
        Enumerable.Range(DateTime.Today.Year - 14, 15).Reverse().ToList();

    public CollegeStudentManagementViewModel(
        IServiceProvider serviceProvider,
        ILoggerService logger,
        ICollegeStudentService collegeStudentService,
        IDialogService dialogService,
        IDeathStatementService deathStatementService,
        INewPermissionService permissionService)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _collegeStudentService = collegeStudentService;
        _dialogService = dialogService;
        _deathStatementService = deathStatementService;
        _permissionService = permissionService;
    }

    /// <inheritdoc/>
    public override async Task OnAppearingAsync()
    {
        // 首次进入即全量加载：原只载当前 Tab，其余三个 Tab 徽标计数停留初始值 "0"（不刷新）
        await LoadAllTabsAsync();
        // 死亡待退出承接面（方案2）：先权限过滤再按范围幂等补齐加载
        await LoadDeathCollegeAsync();
        await base.OnAppearingAsync();
    }

    /// <summary>
    /// 四个 Tab 一次性加载（入口刷新）：Tab1/Tab2 共用 GetAllAsync 单次查询拆两栏，
    /// 与 Tab0 可关联搜索、Tab3 今年毕业并行；徽标计数首进即真值。
    /// </summary>
    private async Task LoadAllTabsAsync()
    {
        try
        {
            IsBusy = true;
            LoadingMessage = "加载大学生数据中...";
            await Task.WhenAll(
                LoadEligibleMembersAsync(),
                LoadAssociatedAndNotInScopeAsync(),
                LoadGraduatingStudentsAsync());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "加载数据异常");
            ErrorMessage = "加载异常，请稍后重试";
        }
        finally
        {
            IsBusy = false;
        }
    }

    #region Tab 切换

    [RelayCommand]
    private void SwitchTab(string tabIndex)
    {
        if (int.TryParse(tabIndex, out int idx))
        {
            SelectedTabIndex = idx;
        }
    }

    #endregion

    #region 数据加载

    private async Task LoadCurrentTabDataAsync()
    {
        try
        {
            IsBusy = true;

            switch (SelectedTabIndex)
            {
                case 0:
                    LoadingMessage = "搜索可关联成员中...";
                    await LoadEligibleMembersAsync();
                    break;
                case 1:
                    LoadingMessage = "加载已关联档案中...";
                    await LoadAssociatedStudentsAsync();
                    break;
                case 2:
                    LoadingMessage = "加载不在范围内人员中...";
                    await LoadNotInScopeStudentsAsync();
                    break;
                case 3:
                    LoadingMessage = "加载今年毕业大学生中...";
                    await LoadGraduatingStudentsAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "加载数据异常");
            ErrorMessage = "加载异常，请稍后重试";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Tab0: 加载可关联成员</summary>
    private async Task LoadEligibleMembersAsync()
    {
        var result = await _collegeStudentService.SearchEligibleMembersAsync(SearchKeyword);
        if (result.IsSuccess)
        {
            EligibleMembers.Clear();
            foreach (var m in result.Value)
                EligibleMembers.Add(m);
            HasEligibleMembers = EligibleMembers.Count > 0;
            EligibleCountText = EligibleMembers.Count.ToString();
        }
        else
        {
            ErrorMessage = result.Message ?? "搜索失败";
        }
    }

    /// <summary>
    /// Tab1 已关联 + Tab2 已退出：共用一次 GetAllAsync 拆两栏（互补判定）。
    /// 已关联 = 在读+已毕业+未收到高等教育，排除已退出与保障已终止；
    /// 已退出 = 不符合人员 / 超择业期 / 保障已终止（家庭档案停保，按身份证实时计算）
    /// </summary>
    private async Task LoadAssociatedAndNotInScopeAsync()
    {
        var result = await _collegeStudentService.GetAllAsync();
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Message ?? "加载失败";
            return;
        }

        var associated = result.Value
            .Where(s => s.Status != CollegeStudentConstants.StatusNotEligible
                && !s.IsBeyondJobSeekingPeriod
                && !s.IsHouseholdStopped)
            .ToList();
        var notInScope = result.Value
            .Where(s => s.Status == CollegeStudentConstants.StatusNotEligible
                || s.IsBeyondJobSeekingPeriod
                || s.IsHouseholdStopped)
            .ToList();

        AssociatedStudents.Clear();
        foreach (var s in associated)
            AssociatedStudents.Add(s);
        HasAssociatedStudents = AssociatedStudents.Count > 0;
        AssociatedCountText = AssociatedStudents.Count.ToString();

        NotInScopeStudents.Clear();
        foreach (var s in notInScope)
            NotInScopeStudents.Add(s);
        HasNotInScopeStudents = NotInScopeStudents.Count > 0;
        NotInScopeCountText = NotInScopeStudents.Count.ToString();
    }

    /// <summary>Tab1: 加载已关联档案（与 Tab2 共用一次查询）</summary>
    private Task LoadAssociatedStudentsAsync() => LoadAssociatedAndNotInScopeAsync();

    /// <summary>Tab2: 加载不在范围内（已退出：不符合人员/超择业期，或保障已终止）</summary>
    private Task LoadNotInScopeStudentsAsync() => LoadAssociatedAndNotInScopeAsync();

    /// <summary>Tab3: 加载今年毕业大学生</summary>
    private async Task LoadGraduatingStudentsAsync()
    {
        var result = await _collegeStudentService.GetGraduatingStudentsAsync(DateTime.Today.Year);
        if (result.IsSuccess)
        {
            GraduatingStudents.Clear();
            foreach (var s in result.Value)
                GraduatingStudents.Add(s);
            HasGraduatingStudents = GraduatingStudents.Count > 0;
            GraduatingCountText = GraduatingStudents.Count.ToString();
        }
        else
        {
            ErrorMessage = result.Message ?? "加载失败";
        }
    }

    #endregion

    #region 搜索命令

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchMembersAsync()
    {
        await LoadEligibleMembersAsync();
    }

    private bool CanSearch() => !IsBusy;

    #endregion

    #region 跳转保障对象动态管理

    [RelayCommand]
    private async Task NavigateToChangeManagementAsync(CollegeStudent? student)
    {
        await PushChangePageWithIdCardAsync(student?.IdCard);
    }

    /// <summary>
    /// 跳保障对象动态管理（变更页）并预填身份证触发一次搜索——今年毕业与死亡待退出共用出口
    /// （大学生退出登记走变更流程，与既有「今年毕业 → 去变更」同一路径）。
    /// </summary>
    private async Task PushChangePageWithIdCardAsync(string? idCard)
    {
        try
        {
            var page = _serviceProvider.GetRequiredService<Pages.ChangeManagement.ChangePage>();

            if (!string.IsNullOrWhiteSpace(idCard) && page.BindingContext is NewCosmos.ViewModels.ChangeManagement.ChangeViewModel vm)
            {
                vm.SearchKeyword = idCard.Trim();
                if (vm.SearchApplicationsCommand.CanExecute(null))
                    await vm.SearchApplicationsCommand.ExecuteAsync(null);
            }

            ServiceProvider?.GetService<IWindowTitleService>()?.Register(page);

            await Helpers.WindowNavigator.CurrentPage!.Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "跳转保障对象动态管理失败");
            await _dialogService.DisplayAlertAsync("跳转失败", "未找到保障对象动态管理页面", "确定");
        }
    }

    #endregion

    #region 死亡待退出（死亡联动待办承接，2026-10-31 方案2）

    /// <summary>
    /// 加载死亡待退出任务（College 域）。无 COLLEGE_MANAGE → 区块隐藏；
    /// 服务侧先按范围幂等补齐再实时判定办结，仅返回未办任务。
    /// </summary>
    private async Task LoadDeathCollegeAsync()
    {
        DeathCollegeTasks.Clear();
        HasDeathCollegeTasks = false;
        DeathCollegeCountText = "0";

        try
        {
            var userId = App.CurrentUserId ?? 0;
            if (userId <= 0) return;
            var permissions = await _permissionService.CheckPermissionsAsync(userId, new[] { PermissionCodes.COLLEGE_MANAGE });
            if (permissions == null || !permissions.TryGetValue(PermissionCodes.COLLEGE_MANAGE, out var granted) || !granted)
                return;

            var scopeResult = await _deathStatementService.ResolveScopeAsync(CancellationToken);
            if (scopeResult.IsFailure)
            {
                Logger.Warn($"死亡待退出加载跳过（范围解析失败）: {scopeResult.Message}");
                return;
            }

            var tasks = await _deathStatementService.GetLinkageTasksAsync(
                scopeResult.Value, new[] { DeathLinkageConstants.DomainCollege }, CancellationToken);
            if (tasks.IsFailure)
            {
                Logger.Warn($"死亡待退出加载失败: {tasks.Message}");
                return;
            }

            foreach (var task in tasks.Value ?? new List<DeathLinkageTaskItem>())
                DeathCollegeTasks.Add(task);
            DeathCollegeCountText = DeathCollegeTasks.Count.ToString("N0");
            HasDeathCollegeTasks = DeathCollegeTasks.Count > 0;

            Logger.LogBusiness("加载死亡待退出", ("Count", DeathCollegeTasks.Count));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "死亡待退出加载异常");
        }
    }

    /// <summary>死亡待退出：跳变更页预填死者身份证（退出登记走既有变更流程；权限在区块可见性已过滤，此处复核兜底）</summary>
    [RelayCommand]
    private async Task HandleDeathCollegeAsync(DeathLinkageTaskItem? task)
    {
        if (task == null) return;

        var userId = App.CurrentUserId ?? 0;
        var canManage = userId > 0 &&
            await _permissionService.HasPermissionAsync(userId, PermissionCodes.COLLEGE_MANAGE, CancellationToken);
        if (!canManage)
        {
            await _dialogService.DisplayAlertAsync("提示", "无权限办理大学生退出，请联系管理员分配「大学生管理」权限。", "确定");
            return;
        }

        Logger.LogBusiness("死亡待退出-去办理",
            ("Name", DataMasker.MaskName(task.DeceasedName)),
            ("IdCard", DataMasker.MaskIdCard(task.DeceasedIdCard)),
            ("TaskId", task.Id));

        await PushChangePageWithIdCardAsync(task.DeceasedIdCard);
    }

    #endregion

    #region 编辑操作

    /// <summary>
    /// 从搜索结果选择成员，打开新增表单
    /// </summary>
    [RelayCommand]
    private void AddStudent(FamilyMember member)
    {
        if (member == null) return;

        _editingId = 0;
        _editingApplicationId = member.ApplicationId;
        _editingFamilyMemberId = member.Id;

        EditorTitle = "关联大学生档案";
        EditStudentName = member.Name;
        EditIdCard = member.IdCard ?? string.Empty;
        EditEducationLevel = null;
        EditSchoolName = string.Empty;
        EditSchoolDuration = null;
        EditEnrollmentYear = DateTime.Today.Year;
        EditStatus = CollegeStudentConstants.StatusStudying;

        IsEditorVisible = true;
        ErrorMessage = string.Empty;
    }

    /// <summary>
    /// 编辑已有大学生档案
    /// </summary>
    [RelayCommand]
    private void EditStudent(CollegeStudent student)
    {
        if (student == null) return;

        _editingId = student.Id;
        _editingApplicationId = student.ApplicationId;
        _editingFamilyMemberId = student.FamilyMemberId;

        EditorTitle = "编辑大学生档案";
        EditStudentName = student.StudentName;
        EditIdCard = student.IdCard ?? string.Empty;
        EditEducationLevel = student.EducationLevel;
        EditSchoolName = student.SchoolName ?? string.Empty;
        EditSchoolDuration = student.SchoolDurationDisplay;
        EditEnrollmentYear = student.EnrollmentYear;
        EditStatus = student.Status;

        IsEditorVisible = true;
        ErrorMessage = string.Empty;
    }

    /// <summary>
    /// 保存大学生档案（新增/更新）
    /// </summary>
    [RelayCommand]
    private async Task SaveStudentAsync()
    {
        if (string.IsNullOrWhiteSpace(EditStudentName))
        {
            await _dialogService.DisplayAlertAsync("提示", "请输入大学生姓名", "确定");
            return;
        }
        var isNoHigherEducation = EditStatus == CollegeStudentConstants.StatusNoHigherEducation;
        if (!isNoHigherEducation)
        {
            if (string.IsNullOrWhiteSpace(EditSchoolName))
            {
                await _dialogService.DisplayAlertAsync("提示", "请输入学校名称", "确定");
                return;
            }
            if (string.IsNullOrWhiteSpace(EditEducationLevel))
            {
                await _dialogService.DisplayAlertAsync("提示", "请选择学历层次", "确定");
                return;
            }
            if (string.IsNullOrWhiteSpace(EditSchoolDuration))
            {
                await _dialogService.DisplayAlertAsync("提示", "请选择学年制", "确定");
                return;
            }
            if (!EditEnrollmentYear.HasValue)
            {
                await _dialogService.DisplayAlertAsync("提示", "请选择入学年份", "确定");
                return;
            }
        }

        try
        {
            IsBusy = true;
            LoadingMessage = "保存大学生档案中...";

            var student = new CollegeStudent
            {
                Id = _editingId,
                ApplicationId = _editingApplicationId,
                FamilyMemberId = _editingFamilyMemberId,
                StudentName = EditStudentName.Trim(),
                IdCard = string.IsNullOrWhiteSpace(EditIdCard) ? null : EditIdCard.Trim(),
                EducationLevel = EditEducationLevel,
                SchoolName = EditSchoolName.Trim(),
                SchoolDuration = CollegeStudentConstants.GetSchoolDurationValue(EditSchoolDuration),
                EnrollmentYear = EditEnrollmentYear,
                Status = string.IsNullOrWhiteSpace(EditStatus) ? CollegeStudentConstants.StatusStudying : EditStatus
            };

            Result<bool> result;
            if (_editingId > 0)
            {
                result = await _collegeStudentService.UpdateAsync(student);
            }
            else
            {
                var saveResult = await _collegeStudentService.SaveAsync(student);
                result = saveResult.IsSuccess
                    ? Result<bool>.Success(true)
                    : Result<bool>.Failure(saveResult.ErrorCode!, saveResult.Message ?? "保存失败");
            }

            if (result.IsSuccess)
            {
                IsEditorVisible = false;
                await _dialogService.ShowSnackBarAsync(_editingId > 0 ? "大学生档案已更新" : "大学生档案已保存");
                await LoadCurrentTabDataAsync();
            }
            else
            {
                await _dialogService.DisplayAlertAsync("保存失败", result.Message ?? "保存失败", "确定");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "保存大学生档案异常");
            await _dialogService.DisplayAlertAsync("保存失败", "保存异常，请稍后重试", "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 关闭编辑表单
    /// </summary>
    [RelayCommand]
    private void CancelEdit()
    {
        IsEditorVisible = false;
        ErrorMessage = string.Empty;
    }

    /// <summary>
    /// 删除大学生档案
    /// </summary>
    [RelayCommand]
    private async Task DeleteStudentAsync(CollegeStudent student)
    {
        if (student == null) return;

        var confirmed = await _dialogService.DisplayAlertAsync(
            "删除确认", $"确认删除 {student.StudentName} 的大学生档案？此操作不可恢复。", "删除", "取消");
        if (!confirmed) return;

        try
        {
            var result = await _collegeStudentService.DeleteAsync(student.Id);
            if (result.IsSuccess)
            {
                AssociatedStudents.Remove(student);
                NotInScopeStudents.Remove(student);
                HasAssociatedStudents = AssociatedStudents.Count > 0;
                HasNotInScopeStudents = NotInScopeStudents.Count > 0;
                await _dialogService.ShowSnackBarAsync("大学生档案已删除");
                await LoadCurrentTabDataAsync();
            }
            else
            {
                await _dialogService.DisplayAlertAsync("删除失败", result.Message ?? "删除失败", "确定");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "删除大学生档案异常");
            await _dialogService.DisplayAlertAsync("删除失败", "删除异常，请稍后重试", "确定");
        }
    }

    #endregion
}
