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
/// 提交校验 / 提交申报 / 完结归档 / 作废。
/// 2026-11 死亡说明简化·阶段3a：从 DeathStatementViewModel.cs 纯移动拆出（零行为变更）。
/// </summary>
public partial class DeathStatementViewModel
{

    #region 申报（提交 / 核实 / 作废）

    /// <summary>提交申报：先查窗口期重复 → 确认 → 写台账 → 刷新选中</summary>
    [RelayCommand]
    private async Task DeclareAsync()
    {
        if (!EnsureScopeReady()) return;
        // 报告人默认同申报人：勾选态在校验/落库前同步一次（取消勾选后不覆盖独立填写值）
        if (IsReporterSameAsDeclarer) SyncReporterFromDeclarer();

        // 9 段前置校验已下沉 DeathDeclareValidator（2026-11 简化·3b），此处只弹首条失败消息；
        // 2026-11-08 规范化格式追加条目二（遗体处置分支）与条目五（矛盾纠纷）两段硬门
        var invalid = DeathDeclareValidator.Validate(new DeathDeclareValidator.Input
        {
            Name = Name,
            IdCard = IdCard,
            DeathDateTime = DeathDateTime,
            DeclaredByName = DeclaredByName,
            DeclaredByIdCard = DeclaredByIdCard,
            DeclaredByType = DeclaredByType,
            ReporterName = ReporterName,
            IsTownScope = _scope != null && _scope.IsTownScope,
            DeclaredVillage = DeclaredVillage,
            BusinessMatterOptionCount = BusinessMatterChecks.Count,
            BusinessMatterCheckedCount = BusinessMatterChecks.Count(o => o.IsChecked),
            DispositionBranch = SelectedDisposition,
            CremationDate = CremationDate,
            CremationPlace = CremationPlace,
            BurialDate = BurialDate,
            BurialPlace = BurialPlace,
            RemainsArrangement = RemainsArrangement,
            DisputeStatus = SelectedDispute,
            DisputeDesc = DisputeDesc,
            DisputeNone = DeathDeclarationConstants.DisputeNone,
            DisputeHas = DeathDeclarationConstants.DisputeHas
        });
        if (invalid != null)
        {
            await ShowTipAsync(invalid);
            return;
        }

        // 重复申报确认（窗口期 ±3 天）
        var countResult = await _deathStatementService.CountRecentValidAsync(IdCard, DeathDate, CancellationToken);
        if (countResult.IsFailure)
        {
            await ShowTipAsync($"重复申报检查失败：{countResult.Message}");
            return;
        }
        if (countResult.Value > 0)
        {
            var confirm = await _dialogService.DisplayAlertAsync("重复申报确认",
                $"该死者在死亡日期前后 {DeathDeclarationConstants.DuplicateWindowDays} 天内已有 {countResult.Value} 条有效申报。\n仍要提交本次申报吗？",
                "仍要提交", "取消");
            if (!confirm) return;
        }

        var request = new DeathDeclareRequest
        {
            Name = Name.Trim(),
            IdCard = IdCard.Trim(),
            DeathDate = DeathDate.Date,
            DeathTime = DeathTime,
            DeathReason = DeathReason?.Trim() ?? string.Empty,
            DeathCertNo = DeathCertNo?.Trim() ?? string.Empty,
            DeathPlace = DeathPlace?.Trim() ?? string.Empty,
            Relation = DeclaredRelation?.Trim() ?? string.Empty,
            DeclaredByName = DeclaredByName.Trim(),
            DeclaredByIdCard = DeclaredByIdCard?.Trim() ?? string.Empty,
            DeclaredByPhone = DeclaredByPhone?.Trim() ?? string.Empty,
            DeclaredByType = DeclaredByType,
            ReporterName = ReporterName?.Trim() ?? string.Empty,
            ReporterRelation = ReporterRelation?.Trim() ?? string.Empty,
            ReporterPhone = ReporterPhone?.Trim() ?? string.Empty,
            SituationDesc = SituationDesc?.Trim() ?? string.Empty,
            // 遗体处置：只落选中分支的字段（OnSelectedDispositionChanged 已清非选中分支，此处双保险）；
            // 「其他」分支文本落 remains_arrangement，结构化分支不回写该列（防切换分支残留脏值）
            RemainsArrangement = IsDispositionOther ? RemainsArrangement?.Trim() ?? string.Empty : string.Empty,
            BusinessAgent = BusinessAgent?.Trim() ?? string.Empty,
            BusinessMatter = BusinessMatter?.Trim() ?? string.Empty,
            CertType = SelectedCertType?.Trim() ?? string.Empty,
            CertIssueOrg = CertIssueOrg?.Trim() ?? string.Empty,
            CremationDate = IsDispositionCremation ? CremationDate : null,
            CremationPlace = IsDispositionCremation ? CremationPlace?.Trim() ?? string.Empty : string.Empty,
            CremationCertNo = IsDispositionCremation ? CremationCertNo?.Trim() ?? string.Empty : string.Empty,
            BurialDate = IsDispositionBurial ? BurialDate : null,
            BurialPlace = IsDispositionBurial ? BurialPlace?.Trim() ?? string.Empty : string.Empty,
            FamilySituation = FamilySituation?.Trim() ?? string.Empty,
            DisputeStatus = SelectedDispute?.Trim() ?? string.Empty,
            DisputeDesc = string.Equals(SelectedDispute, DeathDeclarationConstants.DisputeHas, StringComparison.Ordinal)
                ? DisputeDesc?.Trim() ?? string.Empty
                : string.Empty,
            ApplicationId = _context?.ApplicationId,
            VillageId = _scope!.IsTownScope ? null : _scope.VillageId,
            // 镇级降级：组织无村，落款村名取表单手填的「所在村」（写入 village_name 列）
            VillageName = _scope!.IsTownScope ? (DeclaredVillage?.Trim() ?? string.Empty) : _scope.VillageName,
            TownId = _scope!.TownId,
            OperatorName = string.IsNullOrEmpty(App.CurrentUserName) ? "System" : App.CurrentUserName,
            Channel = DeathDeclarationConstants.ResolveChannel()
        };

        await ExecuteAsync(async ct =>
        {
            var result = await _deathStatementService.DeclareAsync(request, ct);
            if (result.IsFailure)
            {
                await ShowTipAsync($"申报提交失败：{result.Message}");
                return;
            }

            var info = result.Value!;
            IsDeclared = true;
            StatusName = DeathDeclarationConstants.GetStatusName(DeathDeclarationConstants.StatusPending);
            DeclaredAtText = info.DeclaredAt.ToString("yyyy-MM-dd HH:mm");

            // 刷新列表并按新申报重新装载上下文
            var searchResult = await _deathStatementService.SearchAsync(
                _scope!, SearchKeyword, SearchFrom, SearchTo, ShowAll, ct);
            if (searchResult.IsSuccess && searchResult.Value != null)
            {
                SearchResults.Clear();
                foreach (var i in searchResult.Value) SearchResults.Add(i);
                HasSearchResults = SearchResults.Count > 0;
                SearchSummary = HasSearchResults ? $"共 {SearchResults.Count} 条（本村）" : "本村无匹配记录";
            }

            var created = searchResult.Value?.FirstOrDefault(i => i.DeclarationId == info.Id);
            if (created != null)
                await LoadContextAsync(created);

            await ShowTipAsync($"申报已提交（编号 {info.Id}，受理时间 {info.DeclaredAt:yyyy-MM-dd HH:mm}）");

            // 台账先行：提交成功后直接进入文书输出（三步向导第 3 步）
            CurrentStep = 3;
        }, "正在提交申报…");
    }

    // 已删除（2026-11 死亡说明简化·阶段2）：VerifyAsync/VerifyCommand（核实）孤儿命令——
    // 全仓 PC/手机两页均无 UI 入口（零绑定取证）。2026-11-08 P1-5 进一步删除 Service/接口的
    // VerifyAsync 与 DeathDeclarationConstants.StatusVerified：状态不可达且全库 0 行。

    [RelayCommand]
    private async Task RevokeAsync()
    {
        if (_context?.DeclarationId is not { } declarationId) return;

        var reason = await _dialogService.DisplayPromptAsync("作废申报",
            "请填写作废原因（将记入台账与审计日志）", "作废", "取消");
        if (string.IsNullOrWhiteSpace(reason))
        {
            if (reason == null) return;
            await ShowTipAsync("作废原因不能为空");
            return;
        }

        await ExecuteAsync(async ct =>
        {
            var result = await _deathStatementService.RevokeAsync(declarationId, reason, ct);
            if (result.IsFailure)
            {
                await ShowTipAsync($"作废失败：{result.Message}");
                return;
            }

            IsDeclared = false;
            StatusName = DeathDeclarationConstants.GetStatusName(DeathDeclarationConstants.StatusRevoked);
            await ShowTipAsync("该申报已作废");
        }, "正在作废…");
    }

    /// <summary>
    /// 完结归档（Pending/Verified → Archived）：申报流程办结结案，退出首页联动提醒；
    /// 已作废不可归档（服务端拦截）。归档后本按钮禁用，台账列表徽标显示「已归档」。
    /// </summary>
    [RelayCommand]
    private async Task ArchiveDeclarationAsync()
    {
        if (_context?.DeclarationId is not { } declarationId) return;

        var confirm = await _dialogService.DisplayAlertAsync("完结归档",
            "归档表示该死亡申报已全部办结（文书已出、联动事项已处理完毕），归档后将不再出现在首页联动提醒中。\n确认归档？",
            "确认归档", "取消");
        if (!confirm) return;

        await ExecuteAsync(async ct =>
        {
            var result = await _deathStatementService.ArchiveAsync(declarationId, ct);
            if (result.IsFailure)
            {
                await ShowTipAsync($"归档失败：{result.Message}");
                return;
            }

            IsArchived = true;
            StatusName = DeathDeclarationConstants.GetStatusName(DeathDeclarationConstants.StatusArchived);
            await ShowTipAsync("该申报已完结归档");
        }, "正在归档…");
    }

    #endregion
}
