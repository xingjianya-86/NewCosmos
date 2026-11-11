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
/// 搜索 / 带出 / 手工录入 / 装载 / 联动跳转。
/// 2026-11 死亡说明简化·阶段3a：从 DeathStatementViewModel.cs 纯移动拆出（零行为变更）。
/// </summary>
public partial class DeathStatementViewModel
{

    #region 搜索区

    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    [ObservableProperty]
    private DateTime _searchFrom;

    [ObservableProperty]
    private DateTime _searchTo;

    /// <summary>显示全部（含作废申报）</summary>
    [ObservableProperty]
    private bool _showAll;

    [ObservableProperty]
    private ObservableCollection<DeathSearchItem> _searchResults = new();

    [ObservableProperty]
    private bool _hasSearchResults;

    [ObservableProperty]
    private string _searchSummary = string.Empty;

    #endregion

    #region 身份 / 提醒 / 联动

    [ObservableProperty]
    private ObservableCollection<DeathIdentityItem> _identities = new();

    [ObservableProperty]
    private bool _hasIdentities;

    [ObservableProperty]
    private string _identitySummary = string.Empty;

    [ObservableProperty]
    private ObservableCollection<DeathReminderItem> _reminders = new();

    [ObservableProperty]
    private bool _hasReminders;

    [ObservableProperty]
    private ObservableCollection<DeathLinkAction> _links = new();

    [ObservableProperty]
    private bool _hasLinks;

    [ObservableProperty]
    private int _pendingLinkCount;

    #endregion

    #region 搜索与选择

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (!EnsureScopeReady()) return;

        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("死亡情况说明-搜索",
                ("Keyword", SearchKeyword?.Trim() ?? string.Empty),
                ("From", SearchFrom.ToString("yyyy-MM-dd")), ("To", SearchTo.ToString("yyyy-MM-dd")));

            var result = await _deathStatementService.SearchAsync(
                _scope!, SearchKeyword, SearchFrom, SearchTo, ShowAll, ct);

            if (result.IsFailure)
            {
                await ShowTipAsync($"搜索失败：{result.Message}");
                return;
            }

            SearchResults.Clear();
            foreach (var item in result.Value ?? [])
                SearchResults.Add(item);

            HasSearchResults = SearchResults.Count > 0;
            SearchSummary = HasSearchResults ? $"共 {SearchResults.Count} 条（本村）" : "本村无匹配记录";
            if (!HasSearchResults)
                await ShowTipAsync("未找到匹配记录。可点「手工录入」直接申报，或调整姓名/身份证/日期范围。");
        }, "正在搜索…");
    }

    [RelayCommand]
    private async Task SelectItemAsync(DeathSearchItem item)
    {
        if (item == null || !EnsureScopeReady()) return;

        IsManualMode = false;
        await LoadContextAsync(new DeathSearchItem
        {
            DeclarationId = item.DeclarationId,
            DeathRecordId = item.DeathRecordId,
            ApplicationId = item.ApplicationId,
            Name = item.Name,
            IdCard = item.IdCard,
            DeathDateText = item.DeathDateText,
            DeathTimeText = item.DeathTimeText,
            DeathReason = item.DeathReason,
            DeathCertNo = item.DeathCertNo,
            Relation = item.Relation,
            DeclaredByName = item.DeclaredByName,
            DeclaredByType = item.DeclaredByType,
            DeclaredAtText = item.DeclaredAtText,
            Status = item.Status,
            StatusName = item.StatusName,
            IsImportCandidate = item.IsImportCandidate,
            ImportSourceLabel = item.ImportSourceLabel,
            VillageName = item.VillageName,
            HasDeclaration = item.HasDeclaration
        });
    }

    /// <summary>手工录入（不依赖库内已有数据；重置后按字典默认值回填）</summary>
    [RelayCommand]
    private void ManualEntry()
    {
        if (!EnsureScopeReady()) return;

        IsManualMode = true;
        HasSelected = false;
        HasPdf = false;
        PdfFilePath = string.Empty;
        _context = null;

        Name = string.Empty;
        IdCard = string.Empty;
        DeathDateTime = null;
        DeathReason = string.Empty;
        SyncDeathReasonSelection(string.Empty);
        DeathCertNo = string.Empty;
        DeathPlace = string.Empty;
        SelectedDeathPlace = string.Empty;
        IsDeathPlaceCustom = DeathPlaceOptions.Count == 0;
        Relation = string.Empty;

        // 核实说明要素重置（手工录入 = 全部重填，随后 ApplyFormDefaults 按字典首项补默认）
        DeathGender = string.Empty;
        DeathEthnicity = string.Empty;
        ReporterName = string.Empty;
        ReporterRelation = string.Empty;
        ReporterPhone = string.Empty;
        SituationDesc = string.Empty;
        SelectedSituation = string.Empty;
        RemainsArrangement = string.Empty;
        SelectedDisposition = string.Empty;
        SelectedCertType = string.Empty;
        CertIssueOrg = string.Empty;
        CremationDate = null;
        CremationPlace = string.Empty;
        CremationCertNo = string.Empty;
        BurialDate = null;
        BurialPlace = string.Empty;
        FamilySituation = string.Empty;
        SelectedDispute = string.Empty;
        DisputeDesc = string.Empty;
        BusinessAgent = App.CurrentUserFullName ?? string.Empty;
        BusinessMatter = string.Empty;

        DeclaredByIdCard = string.Empty;
        DeclaredByPhone = string.Empty;
        DeclaredRelation = string.Empty;
        DeclaredAtText = string.Empty;
        StatusName = string.Empty;
        IsDeclared = false;
        IsArchived = false;
        ArchiveInfo = string.Empty;
        Identities.Clear();
        Reminders.Clear();
        Links.Clear();
        HasIdentities = false;
        HasReminders = false;
        HasLinks = false;
        PendingLinkCount = 0;
        IdentitySummary = string.Empty;
        if (string.IsNullOrEmpty(DeclaredByType))
            DeclaredByType = DeathDeclarationConstants.DeclaredByVillage;

        // 默认值回填：三项预设取字典首项 + 申报人姓名取当前登录姓名 + 业务事项默认勾首项
        ApplyFormDefaults();
        RebuildBusinessMatterChecks();
    }

    private async Task LoadContextAsync(DeathSearchItem item)
    {
        await ExecuteAsync(async ct =>
        {
            var result = await _deathStatementService.BuildContextAsync(_scope!, item, ct);
            if (result.IsFailure)
            {
                await ShowTipAsync($"汇总失败：{result.Message}");
                return;
            }

            // 联动卡权限分流（2026-10-31 方案2）：写入先于绑定，无权限的卡禁用并显示「需镇级办理」
            await AnnotateLinkPermissionsAsync(result.Value.Links, ct);

            ApplyContext(result.Value);
        }, "正在汇总身份与提醒…");
    }

    /// <summary>联动卡权限缓存（VM 实例内一次批量查询；登录会话内权限稳定）</summary>
    private Dictionary<string, bool>? _linkPermMap;

    /// <summary>
    /// 按 NavKey 权限映射批量判定各联动卡可否办理（申报与办理分离：村级无承接权限 → 禁用态）。
    /// 查询失败按无权限处理（保守：宁可禁用也不放行越权跳转）。
    /// </summary>
    private async Task AnnotateLinkPermissionsAsync(List<DeathLinkAction>? links, CancellationToken ct)
    {
        if (links == null || links.Count == 0) return;

        if (_linkPermMap == null)
        {
            try
            {
                var userId = App.CurrentUserId ?? 0;
                var perms = userId > 0
                    ? await _permissionService.CheckPermissionsAsync(userId, DeathLinkageConstants.AllNavPermissions, ct)
                    : null;
                _linkPermMap = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (var code in DeathLinkageConstants.AllNavPermissions)
                    _linkPermMap[code] = perms != null && perms.TryGetValue(code, out var granted) && granted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "联动卡权限判定失败（按无权限处理）");
                _linkPermMap = DeathLinkageConstants.AllNavPermissions
                    .ToDictionary(c => c, _ => false, StringComparer.Ordinal);
            }
        }

        foreach (var link in links)
        {
            var need = DeathLinkageConstants.GetNavKeyPermission(link.NavKey);
            if (string.IsNullOrEmpty(need)) continue; // None/Declare 等无权限动作
            if (_linkPermMap.TryGetValue(need, out var can) && can) continue;
            link.CanHandle = false;
            if (!link.IsDone) link.ActionText = "需镇级办理";
        }
    }

    private void ApplyContext(DeathStatementContext context)
    {
        _context = context;
        HasSelected = true;

        Name = context.Name;
        IdCard = context.IdCard;
        if (DateTime.TryParse(context.DeathDateText, out var deathDate))
            DeathDateTime = TimeSpan.TryParse(context.DeathTimeText, out var deathTime)
                ? deathDate.Date + deathTime
                : deathDate.Date;
        DeathReason = context.DeathReason;
        SyncDeathReasonSelection(context.DeathReason);
        DeathCertNo = context.DeathCertNo;
        DeathPlace = context.DeathPlace;
        Relation = context.Relation;
        DeclaredByName = context.DeclaredByName;
        DeclaredByIdCard = context.DeclaredByIdCard;
        DeclaredByPhone = context.DeclaredByPhone;
        DeclaredByType = context.DeclaredByType;
        DeclaredRelation = context.DeclaredRelation;
        EnsureOption(RelationOptions, context.DeclaredRelation);
        DeclaredAtText = context.DeclaredAtText;
        StatusName = context.StatusName;
        IsDeclared = context.HasDeclaration;
        IsArchived = string.Equals(context.Status, DeathDeclarationConstants.StatusArchived, StringComparison.Ordinal);

        // 核实说明要素：档案带出的性别/民族 + 申报行里的报告人/情况描述/遗体安置/业务两项
        DeathGender = !string.IsNullOrWhiteSpace(context.Gender)
            ? context.Gender
            : (IdCardValidator.ExtractGender(context.IdCard) ?? string.Empty);
        DeathEthnicity = context.Ethnicity;
        ReporterName = context.ReporterName;
        ReporterRelation = context.ReporterRelation;
        EnsureOption(RelationOptions, context.ReporterRelation);
        ReporterPhone = context.ReporterPhone;
        // 勾选态按值回推：报告人姓名/关系/电话与申报人完全一致 → 视为"同申报人"（隐藏三输入框）；
        // 任一不同（老数据多为独立填写）→ 展开独立编辑，不覆盖已落库值
        IsReporterSameAsDeclarer = !string.IsNullOrWhiteSpace(context.ReporterName)
            && string.Equals(context.ReporterName?.Trim(), context.DeclaredByName?.Trim(), StringComparison.Ordinal)
            && string.Equals(context.ReporterRelation?.Trim(), DeclaredRelation?.Trim(), StringComparison.Ordinal)
            && string.Equals(context.ReporterPhone?.Trim(), DeclaredByPhone?.Trim(), StringComparison.Ordinal);
        SituationDesc = context.SituationDesc;
        RemainsArrangement = context.RemainsArrangement;
        BusinessAgent = context.BusinessAgent;
        BusinessMatter = context.BusinessMatter?.Trim() ?? string.Empty;

        // 规范化格式五条目回填（带出已申报行；处置分支由 SyncPresetSelections→SyncDispositionSelection 按值推断）
        EnsureOption(CertTypeOptions, context.CertType);
        SelectedCertType = context.CertType;
        CertIssueOrg = context.CertIssueOrg;
        CremationDate = DateTime.TryParse(context.CremationDateText, out var cremationDate) ? cremationDate.Date : null;
        CremationPlace = context.CremationPlace;
        CremationCertNo = context.CremationCertNo;
        BurialDate = DateTime.TryParse(context.BurialDateText, out var burialDate) ? burialDate.Date : null;
        BurialPlace = context.BurialPlace;
        FamilySituation = context.FamilySituation;
        EnsureOption(DisputeOptions, context.DisputeStatus);
        SelectedDispute = context.DisputeStatus;
        DisputeDesc = context.DisputeDesc;

        // 预设 Picker 回填（命中直选/未命中「其他」保留原文）→ 只补空默认值 → 业务事项勾选态重建
        SyncPresetSelections();
        ApplyFormDefaults();
        RebuildBusinessMatterChecks();

        ArchiveInfo = string.Join(" · ", new[]
        {
            string.IsNullOrEmpty(context.Classification) ? null : context.Classification,
            string.IsNullOrEmpty(context.Address) ? null : context.Address,
            string.IsNullOrEmpty(context.Relation) ? null : $"与户主关系：{context.Relation}"
        }.Where(s => !string.IsNullOrEmpty(s)));

        Identities.Clear();
        foreach (var i in context.Identities) Identities.Add(i);
        HasIdentities = Identities.Count > 0;
        IdentitySummary = HasIdentities
            ? $"系统内命中 {context.Identities.Count} 项身份" +
              (context.CrossVillageCount > 0 ? $"（另有 {context.CrossVillageCount} 条跨村记录已剔除）" : string.Empty)
            // 0 条本范围身份：先区分「跨村被剔除」，再区分「来自历史导入台账」，
            // 都不命中才显示"系统内无该人身份记录"（否则与搜索结果自相矛盾）
            : context.CrossVillageCount > 0
                ? $"系统内 {context.CrossVillageCount} 条身份记录均挂在其他村社区，已剔除出本说明"
                : !string.IsNullOrEmpty(context.ImportSourceLabel)
                    ? $"来源：{context.ImportSourceLabel}（历史导入台账，无当前在册身份）"
                    : "系统内无该人身份记录";

        Reminders.Clear();
        foreach (var r in context.Reminders) Reminders.Add(r);
        HasReminders = Reminders.Count > 0;

        Links.Clear();
        foreach (var l in context.Links) Links.Add(l);
        HasLinks = Links.Count > 0;
        PendingLinkCount = context.PendingLinkCount;

        HasPdf = false;
        PdfFilePath = string.Empty;

        _logger.LogBusiness("死亡情况说明-构建上下文",
            ("Name", DataMasker.MaskName(context.Name)),
            ("IdCard", DataMasker.MaskIdCard(context.IdCard)),
            ("Identities", context.Identities.Count.ToString()),
            ("Reminders", context.Reminders.Count.ToString()));
    }

    #endregion

    #region 跨流程联动（预填跳转）

    [RelayCommand]
    private async Task OpenLinkAsync(DeathLinkAction link)
    {
        if (link == null || string.IsNullOrEmpty(link.NavKey) || link.NavKey == "None") return;

        // 申报与办理分离（2026-10-31 方案2）：无承接权限的卡已禁用，此处兜底再拦
        //（权限可能在会话中途被管理员调整，缓存未必新鲜）
        var needPerm = DeathLinkageConstants.GetNavKeyPermission(link.NavKey);
        if (!string.IsNullOrEmpty(needPerm))
        {
            var canHandle = _linkPermMap != null
                && _linkPermMap.TryGetValue(needPerm, out var granted) && granted;
            if (!canHandle)
            {
                try
                {
                    var fresh = await _permissionService.HasPermissionAsync(App.CurrentUserId ?? 0, needPerm, CancellationToken);
                    if (!fresh)
                    {
                        await ShowTipAsync("本级无该手续办理权限，申报已入台账，请联系镇级经办处理");
                        return;
                    }
                    _linkPermMap ??= new Dictionary<string, bool>(StringComparer.Ordinal);
                    _linkPermMap[needPerm] = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"联动跳转权限复核失败: {needPerm}");
                    await ShowTipAsync("权限校验失败，请稍后重试");
                    return;
                }
            }
        }

        try
        {
            switch (link.NavKey)
            {
                case "ElderlyStop":
                    if (link.ElderlyApplicationId is not { } elderlyId)
                    {
                        await ShowTipAsync("缺少高龄档案ID，无法跳转");
                        return;
                    }
                    var appResult = await _elderlyApplicationService.GetByIdAsync(elderlyId, CancellationToken);
                    if (appResult.IsFailure || appResult.Value == null)
                    {
                        await ShowTipAsync($"高龄档案加载失败：{appResult.Message}");
                        return;
                    }
                    await NavigateToPageAsync<ElderlyStopPage, ElderlyStopPrefill>(new ElderlyStopPrefill(
                        appResult.Value, ParseDate(link.DeathDateText), link.DeathTimeText, link.DeathReason));
                    break;

                case "HouseholdDeath":
                    if (link.ApplicationId is not { } headAppId)
                    {
                        await ShowTipAsync("缺少关联档案ID，无法跳转");
                        return;
                    }
                    await NavigateToPageAsync<HouseholdDeathChangePage, HouseholdDeathPrefill>(
                        new HouseholdDeathPrefill(headAppId, ParseDate(link.DeathDateText), link.DeathTimeText,
                            link.DeathReason, link.DeathCertNo));
                    break;

                case "MemberChange":
                    if (link.ApplicationId is not { } memberAppId)
                    {
                        await ShowTipAsync("缺少关联档案ID，无法跳转");
                        return;
                    }
                    await NavigateToPageAsync<ChangePage, long>(memberAppId);
                    break;

                case "College":
                    await NavigateToPageAsync<Pages.SocialAssistance.CollegeStudentManagementPage>("大学生管理");
                    break;

                case "Recovery":
                    // 参数化跳转：落在「我的追缴记录」页签 + 死者身份证预填并自动加载，
                    // 操作员直达该人草稿（原仅传标题，到达后还需手工重搜）
                    await NavigateToPageAsync<Pages.Recovery.RecoverySearchPage,
                        Models.NavigationData.RecoverySearchParameter>(
                        new Models.NavigationData.RecoverySearchParameter(
                            Keyword: IdCard, OpenHistoryMode: true));
                    break;

                case "NearRelative":
                    await NavigateToPageAsync<Pages.SocialAssistance.NearRelativeListPage>("近亲属备案");
                    break;

                case "Declare":
                    ManualEntry();
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"联动跳转失败: {link.NavKey}");
            await ShowTipAsync($"跳转失败：{ex.Message}");
        }
    }

    private static DateTime? ParseDate(string? text) =>
        DateTime.TryParse(text, out var value) ? value : null;

    #endregion
}
