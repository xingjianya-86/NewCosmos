using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.DeathStatement;
using NewCosmos.Models.Entities;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Pages.ChangeManagement;
using NewCosmos.Pages.ElderlyBenefits;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.Printing;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.Platform;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;


namespace NewCosmos.ViewModels.ArchiveManagement;

/// <summary>
/// 表单字段 / 字典选项 / 默认值 / 预设同步 / 多选 / 折叠摘要。
/// 2026-11 死亡说明简化·阶段3a：从 DeathStatementViewModel.cs 纯移动拆出（零行为变更）。
/// </summary>
public partial class DeathStatementViewModel
{

    #region 死者与申报（表单 + 展示共用）

    [ObservableProperty]
    private bool _hasSelected;

    /// <summary>手工录入模式（不依赖库内已有数据）</summary>
    [ObservableProperty]
    private bool _isManualMode;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _idCard = string.Empty;

    /// <summary>
    /// 死亡日期与时刻（唯一事实来源；null = 未选择）。
    /// 组件 DateTimeEntryView 绑定本属性：未确认选择前保持 null，「必填」由提交校验真实把关
    /// （替代旧 DatePicker 默认今天 + TimePicker 默认 00:00 的假必填）。
    /// 原 DeathDate/DeathTime 两个绑定属性改为本属性的计算投影，供提交/文书字段取用。
    /// </summary>
    [ObservableProperty]
    private DateTime? _deathDateTime;

    /// <summary>死亡日期（DeathDateTime 投影；提交/文书字段取用）</summary>
    public DateTime DeathDate => DeathDateTime?.Date ?? default;

    /// <summary>死亡时刻（DeathDateTime 投影；提交/文书字段取用）</summary>
    public TimeSpan DeathTime => DeathDateTime?.TimeOfDay ?? TimeSpan.Zero;

    [ObservableProperty]
    private string _deathReason = string.Empty;

    [ObservableProperty]
    private string _deathCertNo = string.Empty;

    [ObservableProperty]
    private string _deathPlace = string.Empty;

    [ObservableProperty]
    private string _relation = string.Empty;

    [ObservableProperty]
    private string _declaredByName = string.Empty;

    [ObservableProperty]
    private string _declaredByIdCard = string.Empty;

    [ObservableProperty]
    private string _declaredByPhone = string.Empty;

    [ObservableProperty]
    private string _declaredByType = string.Empty;

    [ObservableProperty]
    private string _declaredRelation = string.Empty;

    [ObservableProperty]
    private string _declaredAtText = string.Empty;

    #region 核实说明要素（《死亡情况核实说明》，2026-10-26）

    /// <summary>所在村（镇级降级时手填，落款 {所在村}；村级默认取组织村）</summary>
    [ObservableProperty]
    private string _declaredVillage = string.Empty;

    /// <summary>死者性别（档案 Gender，为空时按身份证推导；只读展示）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GenderEthnicitySummary))]
    [NotifyPropertyChangedFor(nameof(HasGenderEthnicity))]
    private string _deathGender = string.Empty;

    /// <summary>死者民族（档案 Ethnicity 转显示名；只读展示）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GenderEthnicitySummary))]
    [NotifyPropertyChangedFor(nameof(HasGenderEthnicity))]
    private string _deathEthnicity = string.Empty;

    /// <summary>发现（报告）人姓名——与申报人独立手填</summary>
    [ObservableProperty]
    private string _reporterName = string.Empty;

    [ObservableProperty]
    private string _reporterRelation = string.Empty;

    [ObservableProperty]
    private string _reporterPhone = string.Empty;

    /// <summary>
    /// 报告人与申报人相同（默认勾选，2026-11 简化）：勾选时姓名/关系/电话自动同步申报人且
    /// 隐藏三个输入框（多数场景发现人即申报人，原并排两套字段是最大重复录入来源）；
    /// 取消勾选恢复独立手填。DB reporter_* 列与模板键不变，独立能力保留。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReporterEditable))]
    private bool _isReporterSameAsDeclarer = true;

    partial void OnIsReporterSameAsDeclarerChanged(bool value)
    {
        if (value) SyncReporterFromDeclarer();
    }

    /// <summary>把申报人姓名/关系/电话同步到报告人（勾选态与提交前各调一次）</summary>
    private void SyncReporterFromDeclarer()
    {
        ReporterName = DeclaredByName ?? string.Empty;
        ReporterRelation = DeclaredRelation ?? string.Empty;
        ReporterPhone = DeclaredByPhone ?? string.Empty;
    }

    /// <summary>是否显示报告人独立输入区（= 未勾选"同申报人"）</summary>
    public bool IsReporterEditable => !IsReporterSameAsDeclarer;

    /// <summary>性别/民族一行摘要（2026-11 简化：原两个只读 Entry 合并为一行灰字）</summary>
    public string GenderEthnicitySummary
    {
        get
        {
            var g = string.IsNullOrWhiteSpace(DeathGender) ? null : DeathGender.Trim();
            var e = string.IsNullOrWhiteSpace(DeathEthnicity) ? null : DeathEthnicity.Trim();
            return g == null && e == null ? string.Empty
                : $"性别 / 民族：{g ?? "—"} · {e ?? "—"}";
        }
    }

    /// <summary>性别/民族摘要行是否显示（两者皆空则隐藏）</summary>
    public bool HasGenderEthnicity =>
        !string.IsNullOrWhiteSpace(DeathGender) || !string.IsNullOrWhiteSpace(DeathEthnicity);

    /// <summary>
    /// 核实要素折叠区展开态（2026-11 简化：业务事项/业务办理人/现场所见/遗体安置收进折叠区，
    /// 默认收起——字典首项默认值已自动填好，仅需微调时展开；提交校验「业务事项≥1」不变）。
    /// </summary>
    [ObservableProperty]
    private bool _isVerifyDetailExpanded;

    /// <summary>折叠区标题摘要（收起时显示已选项，展开时为收起提示）</summary>
    public string VerifyDetailSummary
    {
        get
        {
            var checkedMatters = BusinessMatterChecks.Where(o => o.IsChecked).Select(o => o.Name).ToList();
            var matters = checkedMatters.Count > 0 ? string.Join("、", checkedMatters) : "未勾选";
            var situation = string.IsNullOrWhiteSpace(SituationDesc) ? "现场所见：未填" : "现场所见已填";
            return $"业务事项：{matters}；{situation}";
        }
    }

    partial void OnSituationDescChanged(string value) => OnPropertyChanged(nameof(VerifyDetailSummary));
    partial void OnRemainsArrangementChanged(string value) => OnPropertyChanged(nameof(VerifyDetailSummary));

    /// <summary>核实要素折叠区展开/收起</summary>
    [RelayCommand]
    private void ToggleVerifyDetail() => IsVerifyDetailExpanded = !IsVerifyDetailExpanded;

    /// <summary>检索筛选折叠区展开态（2026-11 简化：日期范围/含作废默认收起，平时只留关键词+搜索）</summary>
    [ObservableProperty]
    private bool _isSearchFilterExpanded;

    /// <summary>检索筛选折叠区展开/收起</summary>
    [RelayCommand]
    private void ToggleSearchFilter() => IsSearchFilterExpanded = !IsSearchFilterExpanded;

    /// <summary>打印设置折叠区展开态（2026-11 简化·阶段2：打印机/份数/双面默认收起，收起时显示当前打印机名）</summary>
    [ObservableProperty]
    private bool _isPrintSettingsExpanded;

    /// <summary>三联区折叠态摘要（收起时显示已勾选数）</summary>
    public string TripleSummary => $"三联出具（{(PrintRetain ? 1 : 0) + (PrintGov ? 1 : 0) + (PrintClinic ? 1 : 0)}/3）";

    partial void OnPrintRetainChanged(bool value) => OnPropertyChanged(nameof(TripleSummary));
    partial void OnPrintGovChanged(bool value) => OnPropertyChanged(nameof(TripleSummary));
    partial void OnPrintClinicChanged(bool value) => OnPropertyChanged(nameof(TripleSummary));

    /// <summary>三联区折叠展开态（默认收起；PrintTripleCommand 逻辑不动）</summary>
    [ObservableProperty]
    private bool _isTripleExpanded;

    /// <summary>打印设置折叠区展开/收起</summary>
    [RelayCommand]
    private void TogglePrintSettings() => IsPrintSettingsExpanded = !IsPrintSettingsExpanded;

    /// <summary>三联区折叠展开/收起</summary>
    [RelayCommand]
    private void ToggleTriple() => IsTripleExpanded = !IsTripleExpanded;

    /// <summary>现场所见情况描述（长文本）</summary>
    [ObservableProperty]
    private string _situationDesc = string.Empty;

    /// <summary>遗体安置情况（长文本）</summary>
    [ObservableProperty]
    private string _remainsArrangement = string.Empty;

    [ObservableProperty]
    private string _businessAgent = string.Empty;

    /// <summary>相关业务事项（多选拼接串；默认值=字典首项，由 RebuildBusinessMatterChecks 填充）</summary>
    [ObservableProperty]
    private string _businessMatter = string.Empty;

    #endregion

    #region 三联出具（推送单位三份不同，不能用份数复制）

    /// <summary>三联推送单位（默认按组织镇名推导，可在第⑥步文书输出改；值用 Preferences 记忆）</summary>
    [ObservableProperty]
    private string _pushUnitRetain = string.Empty;

    [ObservableProperty]
    private string _pushUnitGov = string.Empty;

    [ObservableProperty]
    private string _pushUnitClinic = string.Empty;

    [ObservableProperty]
    private bool _printRetain = true;

    [ObservableProperty]
    private bool _printGov = true;

    [ObservableProperty]
    private bool _printClinic = true;

    #endregion

    [ObservableProperty]
    private string _statusName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanArchive))]
    [NotifyPropertyChangedFor(nameof(CanRevoke))]
    private bool _isDeclared;

    /// <summary>已完结归档（第⑥步「完结归档」按钮禁用；带出/汇总时按台账状态回填）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanArchive))]
    [NotifyPropertyChangedFor(nameof(CanRevoke))]
    private bool _isArchived;

    /// <summary>可完结归档：已提交申报且尚未归档（已作废由 IsDeclared=false 挡住）</summary>
    public bool CanArchive => IsDeclared && !IsArchived && IsNotBusy;

    /// <summary>可作废：已提交申报且尚未归档（2026-11 简化·阶段2：给 RevokeCommand 补 UI 入口）</summary>
    public bool CanRevoke => IsDeclared && !IsArchived && IsNotBusy;

    [ObservableProperty]
    private string _archiveInfo = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _declaredByTypeOptions = new();

    #region 表单 Picker 选项（设计语言：关系/原因/事项/所在村一律下拉，2026-10-29）

    /// <summary>
    /// 关系选项（申报人关系 / 报告人关系共用）：亲属关系字典口径（同 MemberRelationOptions），
    /// 存中文显示文本（与台账存量「父子」等文本兼容）；带出旧值不在列表时自动追加。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<string> _relationOptions = new();

    /// <summary>死因选项（含「其他」哨兵；选中「其他」浮现手填框）</summary>
    [ObservableProperty]
    private ObservableCollection<string> _deathReasonOptions = new();

    /// <summary>当前选中的死因（Picker 绑定；「其他」→ IsDeathReasonCustom 手填）</summary>
    [ObservableProperty]
    private string _selectedDeathReason = string.Empty;

    /// <summary>选中「其他」死因 → 显示手填输入框（值落 DeathReason）</summary>
    [ObservableProperty]
    private bool _isDeathReasonCustom;

    #region 表单预设选项（2026-10-30：选项来自字典 nc_dict_items，不进代码；默认值=字典首项）

    /// <summary>死亡地点选项（字典分类 DeathPlaces）</summary>
    [ObservableProperty]
    private ObservableCollection<string> _deathPlaceOptions = new();

    /// <summary>当前选中死亡地点（Picker 绑定；「其他」→ IsDeathPlaceCustom 手填）</summary>
    [ObservableProperty]
    private string _selectedDeathPlace = string.Empty;

    /// <summary>死亡地点选「其他」→ 显示手填输入框（值落 DeathPlace）</summary>
    [ObservableProperty]
    private bool _isDeathPlaceCustom;

    /// <summary>现场所见预设（字典分类 DeathSituations；模板 {死者情况描述}）</summary>
    [ObservableProperty]
    private ObservableCollection<string> _situationOptions = new();

    /// <summary>当前选中现场所见预设（Picker 绑定；「其他」→ 不覆盖手填框现值）</summary>
    [ObservableProperty]
    private string _selectedSituation = string.Empty;

    #region 规范化格式采集（2026-11-08 五条目；落库列见 20261108_death_statement_standard_format.sql）

    /// <summary>死亡证明类型选项（常量 CertTypeOptions：死亡医学证明 / 非正常死亡证明）</summary>
    [ObservableProperty]
    private ObservableCollection<string> _certTypeOptions = new();

    /// <summary>当前选中死亡证明类型（条目一，选填）</summary>
    [ObservableProperty]
    private string _selectedCertType = string.Empty;

    /// <summary>死亡证明出具机构（条目一，选填）</summary>
    [ObservableProperty]
    private string _certIssueOrg = string.Empty;

    /// <summary>遗体处置方式选项（火化 / 安葬 / 其他，常量单一来源）</summary>
    [ObservableProperty]
    private ObservableCollection<string> _dispositionOptions = new();

    /// <summary>当前选中遗体处置方式（条目二；三选一互斥显隐，切换即清非选中分支字段保持写入不变量）</summary>
    [ObservableProperty]
    private string _selectedDisposition = string.Empty;

    /// <summary>火化分支显隐（SelectedDisposition=火化）</summary>
    [ObservableProperty]
    private bool _isDispositionCremation;

    /// <summary>安葬分支显隐（SelectedDisposition=安葬）</summary>
    [ObservableProperty]
    private bool _isDispositionBurial;

    /// <summary>其他分支显隐（SelectedDisposition=其他 → 自由说明，落 remains_arrangement）</summary>
    [ObservableProperty]
    private bool _isDispositionOther;

    /// <summary>火化日期（date-only；DateTimeEntryView ShowTime=False）</summary>
    [ObservableProperty]
    private DateTime? _cremationDate;

    /// <summary>火化地点/殡仪馆</summary>
    [ObservableProperty]
    private string _cremationPlace = string.Empty;

    /// <summary>火化证明编号（选填）</summary>
    [ObservableProperty]
    private string _cremationCertNo = string.Empty;

    /// <summary>安葬日期（已安葬或拟安葬，可为未来日期）</summary>
    [ObservableProperty]
    private DateTime? _burialDate;

    /// <summary>安葬地点/公益性墓地</summary>
    [ObservableProperty]
    private string _burialPlace = string.Empty;

    /// <summary>家属情况及诉求（条目四，长文本）</summary>
    [ObservableProperty]
    private string _familySituation = string.Empty;

    /// <summary>矛盾纠纷及信访隐患选项（无 / 有）</summary>
    [ObservableProperty]
    private ObservableCollection<string> _disputeOptions = new();

    /// <summary>当前选中矛盾纠纷勾选态（条目五，必选）</summary>
    [ObservableProperty]
    private string _selectedDispute = string.Empty;

    /// <summary>选「有」→ 展开矛盾纠纷说明输入框</summary>
    [ObservableProperty]
    private bool _isDisputeHas;

    /// <summary>矛盾纠纷说明（勾「有」必填）</summary>
    [ObservableProperty]
    private string _disputeDesc = string.Empty;

    /// <summary>安葬日期可选上限（拟安葬允许未来日期，放开到 1 年内；火化日期用组件默认上限=当前时刻）</summary>
    public DateTime BurialDateLimit => DateTime.Today.AddYears(1);

    #endregion

    /// <summary>相关业务事项多选行（字典分类 BusinessMatters；勾选拼回 BusinessMatter 串）</summary>
    [ObservableProperty]
    private ObservableCollection<BusinessMatterOption> _businessMatterChecks = new();

    /// <summary>业务事项字典原始顺序（拼接默认值与历史值追加用）</summary>
    private readonly List<string> _matterOptions = new();

    #endregion

    /// <summary>是否镇级降级范围（true 时「所在村」显示下拉必填；false 时显示自动带出的村名）</summary>
    [ObservableProperty]
    private bool _isTownScope;

    /// <summary>本镇村社区列表（镇级降级时「所在村」Picker 数据源）</summary>
    [ObservableProperty]
    private ObservableCollection<string> _villageOptions = new();

    partial void OnSelectedDeathReasonChanged(string value)
    {
        var isOther = DeathDeclarationConstants.IsOtherOption(value);
        IsDeathReasonCustom = isOther;
        if (!isOther && !string.IsNullOrWhiteSpace(value))
            DeathReason = value;
    }

    /// <summary>按库内死因值回填 Picker 选中项（命中选项 → 直选；非空未命中 → 「其他」+ 保留原文手填；空 → 清选）</summary>
    private void SyncDeathReasonSelection(string? value)
    {
        var reason = value?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(reason))
        {
            SelectedDeathReason = string.Empty;
            IsDeathReasonCustom = false;
            return;
        }

        var match = DeathReasonOptions.FirstOrDefault(o =>
            string.Equals(o, reason, StringComparison.Ordinal));
        if (match != null)
        {
            SelectedDeathReason = match; // OnSelectedDeathReasonChanged 会把 DeathReason 写回同值，无副作用
            return;
        }

        SelectedDeathReason = DeathDeclarationConstants.DeathReasonOther;
        IsDeathReasonCustom = true;
    }

    /// <summary>业务事项值不在选项内时追加（历史自由文本兼容，同高龄表单 LoadRelationOptions 容错）</summary>
    private void EnsureOption(ObservableCollection<string> options, string? value)
    {
        var v = value?.Trim();
        if (!string.IsNullOrWhiteSpace(v) && !options.Contains(v))
            options.Add(v);
    }

    #region 表单预设：字典加载 / Picker 联动 / 默认值（2026-10-30）

    partial void OnSelectedDeathPlaceChanged(string value)
    {
        var isOther = DeathDeclarationConstants.IsOtherOption(value);
        IsDeathPlaceCustom = isOther;
        if (!isOther && !string.IsNullOrWhiteSpace(value))
            DeathPlace = value;
    }

    partial void OnSelectedSituationChanged(string value)
    {
        // 手填框常显（正文可微调）：选预设回填，选「其他」保留现值供编辑
        if (!DeathDeclarationConstants.IsOtherOption(value) && !string.IsNullOrWhiteSpace(value))
            SituationDesc = value;
    }

    /// <summary>
    /// 遗体处置三选一联动（条目二）：置分支显隐 + 清非选中分支字段——
    /// 保证落库只有一组处置数据（回填判定与组句输出同口径，防切换分支残留脏值）。
    /// </summary>
    partial void OnSelectedDispositionChanged(string value)
    {
        IsDispositionCremation = string.Equals(value, DeathDeclarationConstants.DispositionCremation, StringComparison.Ordinal);
        IsDispositionBurial = string.Equals(value, DeathDeclarationConstants.DispositionBurial, StringComparison.Ordinal);
        IsDispositionOther = string.Equals(value, DeathDeclarationConstants.DispositionOther, StringComparison.Ordinal);

        if (!IsDispositionCremation)
        {
            CremationDate = null;
            CremationPlace = string.Empty;
            CremationCertNo = string.Empty;
        }
        if (!IsDispositionBurial)
        {
            BurialDate = null;
            BurialPlace = string.Empty;
        }
        // 「其他」分支文本不随切走清空：回填时先灌 RemainsArrangement 再选分支，
        // 若此时清掉会把带出的原文抹掉；改分支（火化/安葬）时才清——上面两个 if 已覆盖结构化字段，
        // 文本仅在用户显式改走后由落库语义（结构化优先）自然失效，不再回写。
    }

    /// <summary>矛盾纠纷选「有」→ 展开说明输入框（条目五）</summary>
    partial void OnSelectedDisputeChanged(string value)
    {
        IsDisputeHas = string.Equals(value, DeathDeclarationConstants.DisputeHas, StringComparison.Ordinal);
    }

    /// <summary>
    /// 加载表单预设选项（死因/死亡地点/现场所见/业务事项，字典单一来源；每 VM 实例一次）。
    /// 字典查询失败仅告警不阻断——选项空时表单仍可手填（死亡地点/死因回落手填态）。
    /// 2026-11-08 死因统一读字典 DeathReasons（原常量数组与字典两套选项不一致，已删常量双写）；
    /// 遗体安置不再走字典 DeathRemainsArrangements——条目二改结构化三分支（火化/安葬/其他），
    /// 字典种子保留但本表单不再消费。
    /// </summary>
    private async Task LoadFormOptionsAsync()
    {
        if (_formOptionsLoaded) return;
        _formOptionsLoaded = true;

        await LoadDictOptionsAsync(DictionaryTypeCodes.DeathReasons, DeathReasonOptions);
        await LoadDictOptionsAsync(DictionaryTypeCodes.DeathPlaces, DeathPlaceOptions);
        await LoadDictOptionsAsync(DictionaryTypeCodes.DeathSituations, SituationOptions);
        await LoadBusinessMatterOptionsAsync();

        // 字典没选项时死亡地点/死因回落为手填态（否则无处输入）
        if (DeathPlaceOptions.Count == 0)
            IsDeathPlaceCustom = true;
        if (DeathReasonOptions.Count == 0)
            IsDeathReasonCustom = true;
    }

    private async Task LoadDictOptionsAsync(string category, ObservableCollection<string> target)
    {
        try
        {
            var result = await _dictionaryService.GetItemsByCategoryAsync(category, CancellationToken);
            if (result.IsFailure || result.Value == null)
            {
                _logger.Warn($"死亡说明-加载字典选项失败[{category}]: {result.Message}");
                return;
            }
            target.Clear();
            foreach (var item in result.Value)
                if (!string.IsNullOrWhiteSpace(item.ItemValue))
                    target.Add(item.ItemValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"死亡说明-加载字典选项异常[{category}]");
        }
    }

    private async Task LoadBusinessMatterOptionsAsync()
    {
        try
        {
            var result = await _dictionaryService.GetItemsByCategoryAsync(DictionaryTypeCodes.BusinessMatters, CancellationToken);
            if (result.IsFailure || result.Value == null)
            {
                _logger.Warn($"死亡说明-加载业务事项字典失败: {result.Message}");
                return;
            }
            _matterOptions.Clear();
            foreach (var item in result.Value)
                if (!string.IsNullOrWhiteSpace(item.ItemValue))
                    _matterOptions.Add(item.ItemValue);
            RebuildBusinessMatterChecks();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "死亡说明-加载业务事项字典异常");
        }
    }

    /// <summary>
    /// 按当前 BusinessMatter 串重建多选勾选态：按「、」拆分匹配字典项；
    /// BusinessMatter 为空 → 默认勾选字典首项（默认值不进代码）；
    /// 历史值不在字典内 → 追加为已勾选项（存量兼容，同 EnsureOption 口径）。
    /// </summary>
    private void RebuildBusinessMatterChecks()
    {
        foreach (var old in BusinessMatterChecks)
            old.IsCheckedChanged -= OnMatterCheckedChanged;
        BusinessMatterChecks.Clear();

        if (_matterOptions.Count == 0) return;

        var selected = (BusinessMatter ?? string.Empty)
            .Split(DeathDeclarationConstants.BusinessMatterSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        if (selected.Count == 0)
            selected.Add(_matterOptions[0]); // 默认勾选字典首项

        foreach (var name in _matterOptions)
        {
            var option = new BusinessMatterOption(name, selected.Contains(name));
            option.IsCheckedChanged += OnMatterCheckedChanged;
            BusinessMatterChecks.Add(option);
        }
        foreach (var name in selected.Where(s => !_matterOptions.Contains(s)))
        {
            var option = new BusinessMatterOption(name, true);
            option.IsCheckedChanged += OnMatterCheckedChanged;
            BusinessMatterChecks.Add(option);
        }

        JoinBusinessMatterChecks();
    }

    private void OnMatterCheckedChanged(object? sender, EventArgs e)
    {
        JoinBusinessMatterChecks();
        OnPropertyChanged(nameof(VerifyDetailSummary)); // 折叠区摘要随勾选刷新
    }

    /// <summary>勾选态 → BusinessMatter 拼接串（落库与模板同源；一项未勾时为空串，提交校验把关）</summary>
    private void JoinBusinessMatterChecks()
    {
        BusinessMatter = string.Join(DeathDeclarationConstants.BusinessMatterSeparator,
            BusinessMatterChecks.Where(o => o.IsChecked).Select(o => o.Name));
    }

    /// <summary>
    /// 表单默认值（只补空不覆盖；选项已加载时才补预设项）。
    /// 调用时机：首次进入页面 / 手工录入重置后 / 从库内带出后（三处统一）。
    /// 申报人姓名默认当前登录用户姓名字段（FullName，空回退账号名）。
    /// </summary>
    private void ApplyFormDefaults()
    {
        if (string.IsNullOrWhiteSpace(DeathPlace) && DeathPlaceOptions.Count > 0)
        {
            IsDeathPlaceCustom = false;
            SelectedDeathPlace = DeathPlaceOptions[0];
            DeathPlace = DeathPlaceOptions[0];
        }
        if (string.IsNullOrWhiteSpace(SituationDesc) && SituationOptions.Count > 0)
        {
            SelectedSituation = SituationOptions[0];
            SituationDesc = SituationOptions[0];
        }
        // 遗体处置无自动默认（条目二三分支必选由提交校验把关；原字典首项默认已随结构化分支移除）
        if (string.IsNullOrWhiteSpace(DeclaredByName))
            DeclaredByName = App.CurrentUserFullName ?? App.CurrentUserName ?? string.Empty;
    }

    /// <summary>按库内值回填两个预设 Picker + 遗体处置分支（命中→直选；未命中→「其他」保留原文；空→清选）</summary>
    private void SyncPresetSelections()
    {
        SyncPresetSelection(DeathPlace, DeathPlaceOptions, v => SelectedDeathPlace = v, v => IsDeathPlaceCustom = v);
        SyncPresetSelection(SituationDesc, SituationOptions, v => SelectedSituation = v, null);
        SyncDispositionSelection();
    }

    /// <summary>
    /// 遗体处置分支回填（条目二）：结构化字段命中 → 对应分支；
    /// 无结构化但有安置自由文本（存量行）→ 「其他」；全空 → 清选。
    /// 须在 Cremation*/Burial*/RemainsArrangement 已灌值之后调用（切换回调会清非选中分支）。
    /// </summary>
    private void SyncDispositionSelection()
    {
        if (CremationDate.HasValue || !string.IsNullOrWhiteSpace(CremationPlace) || !string.IsNullOrWhiteSpace(CremationCertNo))
            SelectedDisposition = DeathDeclarationConstants.DispositionCremation;
        else if (BurialDate.HasValue || !string.IsNullOrWhiteSpace(BurialPlace))
            SelectedDisposition = DeathDeclarationConstants.DispositionBurial;
        else if (!string.IsNullOrWhiteSpace(RemainsArrangement))
            SelectedDisposition = DeathDeclarationConstants.DispositionOther;
        else
            SelectedDisposition = string.Empty;
    }

    private void SyncPresetSelection(string? value, ObservableCollection<string> options,
        Action<string> setSelected, Action<bool>? setCustom)
    {
        var v = value?.Trim() ?? string.Empty;
        if (v.Length == 0)
        {
            setSelected(string.Empty);
            setCustom?.Invoke(false);
            return;
        }
        var match = options.FirstOrDefault(o => string.Equals(o, v, StringComparison.Ordinal));
        if (match != null)
        {
            setSelected(match);
            setCustom?.Invoke(false);
            return;
        }
        // 未命中：「其他」哨兵 + 保留原文手填（DeathPlace 框需显示；正文两框常显无需切换）
        EnsureOption(options, DeathDeclarationConstants.DeathReasonOther);
        setSelected(DeathDeclarationConstants.DeathReasonOther);
        setCustom?.Invoke(true);
    }

    #endregion

    #endregion

    #endregion
}
