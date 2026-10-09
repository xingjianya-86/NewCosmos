using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Exceptions;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.AssetVerification;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.SocialAssistance;

using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.System;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.ChangeManagement;
using System.Collections.ObjectModel;
using System.ComponentModel;

using Application = NewCosmos.Models.Entities.Application;

namespace NewCosmos.ViewModels.SocialAssistance;

public partial class ApplicationFormViewModel
{
    #region 经济数据实时订阅（输入即刷新，防抖保性能）

    /// <summary>已订阅 PropertyChanged 的经济数据实体（防止重复订阅 / 卸载泄漏）</summary>
    private readonly HashSet<object> _subscribedEconomyItems = new();

    /// <summary>
    /// 赡养人关闭"有赡养能力"开关时暂存的月费（重开时恢复用）。
    /// 按 Supporter 对象引用为 key，生命周期随本表单实例（ViewModel 为 Transient）。
    /// </summary>
    private readonly Dictionary<Supporter, decimal> _supporterFeeBackup = new();

    private bool _recalcScheduled;
    private bool _landRecalcScheduled;

    /// <summary>
    /// 监听集合增删：重建子项订阅，并触发对应重算（传入重算子）
    /// </summary>
    private void WireCollection<T>(ObservableCollection<T> collection, Action recalculate) where T : INotifyPropertyChanged
    {
        collection.CollectionChanged += (_, _) =>
        {
            SyncSubscriptions(collection);
            if (_isLoadingData) return;
            recalculate();
        };
    }

    /// <summary>
    /// 差异收敛订阅：新增项订阅 PropertyChanged，已移除项退订（兼容 Clear/Reset）
    /// </summary>
    private void SyncSubscriptions<T>(ObservableCollection<T> collection) where T : INotifyPropertyChanged
    {
        var current = new HashSet<object>(collection.Cast<object>());
        foreach (var stale in _subscribedEconomyItems.Where(o => o is T && !current.Contains(o)).ToList())
        {
            ((INotifyPropertyChanged)stale).PropertyChanged -= OnEconomyItemPropertyChanged;
            _subscribedEconomyItems.Remove(stale);
        }
        foreach (var item in collection)
        {
            if (_subscribedEconomyItems.Add(item))
                item.PropertyChanged += OnEconomyItemPropertyChanged;
        }
    }

    /// <summary>
    /// 实体项内属性变化 → 触发重算（土地组走土地汇总，其余走收入重算）
    /// </summary>
    private void OnEconomyItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isLoadingData) return;
        // 赡养人"有赡养能力"开关：先联动月费清零/恢复，再走防抖重算
        if (sender is Supporter supporter && e.PropertyName == nameof(Supporter.IsSupportAbility))
            HandleSupporterAbilityToggled(supporter);
        if (sender is LandConfirmationGroup)
            ScheduleLandRecalculate();
        else
            ScheduleRecalculate();
    }

    /// <summary>
    /// 赡养人"有赡养能力"开关联动：
    /// - 关闭：暂存当前月费（>0 才暂存）并清零 → 年赡养费联动为 0（卡片清零），收入汇总排除该人；
    /// - 打开：若当前月费已清零且有备份则恢复（不覆盖用户重填值），收入汇总重新计入。
    /// 仅在用户切换时触发（加载赋值在 _isLoadingData=true 下被上方短路，历史数据不受影响）。
    /// </summary>
    private void HandleSupporterAbilityToggled(Supporter supporter)
    {
        if (supporter.IsSupportAbility)
        {
            if (supporter.MonthlySupportFee <= 0 && _supporterFeeBackup.TryGetValue(supporter, out var backup))
            {
                supporter.MonthlySupportFee = backup;
            }
            _supporterFeeBackup.Remove(supporter);
        }
        else
        {
            if (supporter.MonthlySupportFee > 0)
                _supporterFeeBackup[supporter] = supporter.MonthlySupportFee;
            supporter.MonthlySupportFee = 0;
        }
    }

    /// <summary>
    /// 防抖调度收入重算：150ms 内连续变更合并为一次（UI 线程执行，无积压）
    /// </summary>
    private void ScheduleRecalculate()
    {
        if (_recalcScheduled) return;
        var dispatcher = Microsoft.Maui.Controls.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            _recalcScheduled = false;
            SafeRecalculateIncome();
            return;
        }
        _recalcScheduled = true;
        dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), () =>
        {
            _recalcScheduled = false;
            SafeRecalculateIncome();
        });
    }

    /// <summary>
    /// 防抖调度土地汇总重算（CalculateLandConfirmationArea 末尾自行调用 RecalculateIncome）
    /// </summary>
    private void ScheduleLandRecalculate()
    {
        if (_landRecalcScheduled) return;
        var dispatcher = Microsoft.Maui.Controls.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            _landRecalcScheduled = false;
            SafeCalculateLandConfirmationArea();
            return;
        }
        _landRecalcScheduled = true;
        dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), () =>
        {
            _landRecalcScheduled = false;
            SafeCalculateLandConfirmationArea();
        });
    }

    /// <summary>延迟回调重算收入：异常记录且不崩溃（防调度回调内 RecalculateIncome 抛异常导致闪退）</summary>
    private void SafeRecalculateIncome()
    {
        try
        {
            RecalculateIncome();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "延迟收入重算失败");
        }
    }

    /// <summary>延迟回调土地汇总重算：异常记录且不崩溃</summary>
    private void SafeCalculateLandConfirmationArea()
    {
        try
        {
            CalculateLandConfirmationArea();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "延迟土地汇总重算失败");
        }
    }

    #endregion
    #region 家庭成员命令

    [RelayCommand]
    private async Task AddSharedLivingMemberAsync()
    {
        _logger.LogBusiness("添加共同生活成员");

        // 家庭成员变更模式：先登记增员原因与事由日期（取消则不新增）
        MemberChangeReasonResult? reason = null;
        if (IsMemberChangeMode)
        {
            reason = await ShowMemberChangeReasonPopupAsync(isRemove: false, member: null);
            if (reason == null) return;
        }

        var member = CreateDefaultFamilyMember(MemberCategoryConstants.SHARED_LIVING);
        if (reason != null)
            _addedMemberReasons[member] = reason;
        FamilyMembers.Add(member);
        RefreshDerivedCollections();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task AddSupportMemberAsync()
    {
        _logger.LogBusiness("添加赡养抚养扶养人");

        // 家庭成员变更模式：先登记增员原因与事由日期（取消则不新增）
        MemberChangeReasonResult? reason = null;
        if (IsMemberChangeMode)
        {
            reason = await ShowMemberChangeReasonPopupAsync(isRemove: false, member: null);
            if (reason == null) return;
        }

        await EnsureRegionOptionsLoadedAsync();

        var member = CreateDefaultFamilyMember(MemberCategoryConstants.SUPPORT);
        member.HukouProvince = DefaultValuesConstants.HOME_PROVINCE;
        member.HukouCity = SelectedCity ?? string.Empty;
        member.HukouDistrict = SelectedDistrict ?? string.Empty;
        member.HukouTown = SelectedTown ?? string.Empty;

        if (reason != null)
            _addedMemberReasons[member] = reason;
        FamilyMembers.Add(member);
        RefreshDerivedCollections();
        await Task.CompletedTask;
    }

    /// <summary>
    /// 弹出增/减员登记弹窗（家庭成员变更模式专用）：确认返回登记信息，取消返回 null。
    /// 弹窗由调用方 Push/Pop（结果模式与 SelectMembersPopup 一致）。
    /// </summary>
    private async Task<MemberChangeReasonResult?> ShowMemberChangeReasonPopupAsync(bool isRemove, FamilyMember? member)
    {
        var popup = _serviceProvider.GetRequiredService<Pages.ChangeManagement.MemberChangeReasonPopup>();
        popup.Initialize(isRemove, member);

        var navigation = Helpers.WindowNavigator.CurrentPage?.Navigation;
        if (navigation == null)
        {
            _logger.Warn("当前页面为空，无法弹出成员变更登记弹窗");
            return null;
        }
        await navigation.PushModalAsync(popup);
        var result = await popup.Result;
        if (navigation.ModalStack.Contains(popup))
        {
            await navigation.PopModalAsync();
        }
        return result;
    }

    /// <summary>
    /// 创建带默认值的家庭成员
    /// </summary>
    private FamilyMember CreateDefaultFamilyMember(string memberCategory)
    {
        var member = new FamilyMember
        {
            ApplicationId = _applicationId,
            MemberCategory = memberCategory,
            RelationshipToHead = MemberRelationOptions.FirstOrDefault()?.Key ?? string.Empty,
            Ethnicity = GetDefaultKey(EthnicityOptions, DefaultValuesConstants.ETHNICITY_KEY),
            MaritalStatus = GetDefaultKey(MaritalStatusOptions, DefaultValuesConstants.MARITAL_STATUS_KEY),
            EducationLevel = GetDefaultKey(EducationLevelOptions, DefaultValuesConstants.EDUCATION_LEVEL_KEY),
            PoliticalStatus = GetDefaultKey(PoliticalStatusOptions, DefaultValuesConstants.POLITICAL_STATUS_KEY),
            HealthStatus = GetDefaultKey(HealthStatusOptions, DefaultValuesConstants.HEALTH_STATUS_KEY),
            DiseaseCategory = GetDefaultKey(DiseaseCategoryOptions, DefaultValuesConstants.DISEASE_CATEGORY_KEY),
            HomeProvince = DefaultValuesConstants.HOME_PROVINCE,
            HomeCity = SelectedCity ?? string.Empty,
            HomeDistrict = SelectedDistrict ?? string.Empty,
            HomeTown = SelectedTown ?? string.Empty,
            HomeVillage = SelectedVillage ?? string.Empty,
            HomeAddress = Address ?? string.Empty,
            HukouAddress = HukouAddress,
            HukouProvince = DefaultValuesConstants.HOME_PROVINCE,
            CreatedAt = DateTime.Now
        };

        member.SetRegionService(_regionService);
        _ = member.LoadInitialAddressOptionsAsync(SelectedCity, SelectedDistrict, SelectedTown);
        SyncMemberDictOptions(member);

        return member;
    }

    /// <summary>
    /// 获取字典选项的默认Key（优先使用DefaultValuesConstants中的值）
    /// </summary>
    private string GetDefaultKey(ObservableCollection<DictItemOption> options, string defaultKey)
    {
        if (options.Any(o => o.Key == defaultKey))
            return defaultKey;
        return options.FirstOrDefault()?.Key ?? string.Empty;
    }

    [RelayCommand]
    private async Task RemoveFamilyMemberAsync(FamilyMember? member)
    {
        if (member == null) return;

        if (member.RelationshipToHead == "本人/户主" || member.IsHouseholdHead)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "户主不能直接删除，请先变更户主", "确定");
            return;
        }

        // 检查是否只剩户主
        var nonHeadCount = FamilyMembers.Count(m => m.RelationshipToHead != "本人/户主");
        if (nonHeadCount <= 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "已经是最后一个家庭成员，不能删除", "确定");
            return;
        }

        // 家庭成员变更模式：先登记减员原因与事由日期（取消则不减员）
        MemberChangeReasonResult? reason = null;
        if (IsMemberChangeMode)
        {
            reason = await ShowMemberChangeReasonPopupAsync(isRemove: true, member: member);
            if (reason == null) return;
        }

        // 删除赡养抚养扶养人时同步移除 Step3 赡养人条目（同一 DB 行的两个视图，否则保存时会复活）
        if (member.MemberCategory == MemberCategoryConstants.SUPPORT)
        {
            var memberIdCard = (member.IdCard ?? "").Trim();
            var match = Supporters.FirstOrDefault(s =>
                (!string.IsNullOrWhiteSpace(s.IdCard) && string.Equals(s.IdCard.Trim(), memberIdCard, StringComparison.OrdinalIgnoreCase))
                || string.Equals(s.Name, member.Name, StringComparison.Ordinal));
            if (match != null)
            {
                _supporterFeeBackup.Remove(match);
                Supporters.Remove(match);
            }
        }

        if (reason != null)
            _removedMemberEntries.Add((member, reason));

        FamilyMembers.Remove(member);
        RefreshDerivedCollections();
        RecalculateIncome();
    }

    private void UpdateFamilySize()
    {
        FamilySize = SharedLivingMembers.Count + 1; // +1 是户主
    }

    [RelayCommand]
    private async Task SetAsHeadAsync(FamilyMember? member)
    {
        if (member == null) return;

        var currentHead = FamilyMembers.FirstOrDefault(m => m.RelationshipToHead == "本人/户主");
        if (currentHead != null)
            currentHead.RelationshipToHead = "其他";

        member.RelationshipToHead = "本人/户主";
        RefreshDerivedCollections();
        await Task.CompletedTask;
    }

    #endregion

    #region 赡养人辅助方法

    /// <summary>
    /// 获取缓存的低保标准（内存缓存，避免每次添加/导入赡养人都查 DB）。
    /// 仅用于赡养费"默认预填值"；读取失败返回 0 并告警——禁止回退过期硬编码标准。
    /// 判定与保障金计算走 ClassificationService（配置缺失会显式失败）。
    /// </summary>
    private decimal _cachedSubsistenceStandard;
    private bool _subsistenceStandardLoaded;

    private async Task<decimal> GetCachedSubsistenceStandardAsync()
    {
        if (_subsistenceStandardLoaded && _cachedSubsistenceStandard > 0)
            return _cachedSubsistenceStandard;

        try
        {
            bool isRural = HukouType?.Key != "Urban";
            var standardType = isRural ? "RuralSubsistenceStandard" : "UrbanSubsistenceStandard";
            var result = await _standardConfigService.GetStandardValueAsync(standardType);
            if (result.IsSuccess && result.Value > 0)
                _cachedSubsistenceStandard = result.Value;
            else
                _logger.Warn($"低保标准读取失败，赡养费默认值置 0：{result.Message}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"低保标准读取异常，赡养费默认值置 0：{ex.Message}");
        }
        _subsistenceStandardLoaded = true;
        return _cachedSubsistenceStandard;
    }

    /// <summary>
    /// 批量查询身份证关联的档案分类结果（一次 DB 查询替代 N+1）
    /// </summary>
    private async Task<Dictionary<string, string>> BatchGetClassificationByIdCardsAsync(List<string> idCards)
    {
        var map = new Dictionary<string, string>();
        if (idCards.Count == 0) return map;

        try
        {
            var result = await _applicationService.GetClassificationsByIdCardsAsync(idCards, CancellationToken);
            if (result.IsSuccess && result.Value != null)
                return result.Value;
            _logger.Warn($"批量查询赡养人档案失败: {result.Message}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"批量查询赡养人档案失败: {ex.Message}");
        }
        return map;
    }

    /// <summary>
    /// 同步判定赡养人是否有赡养能力（基于内存数据，无 DB 查询）
    /// </summary>
    private bool DetermineSupportAbilityFromMember(FamilyMember member, Dictionary<string, string> classificationMap)
    {
        // 规则1：健康/残疾状况判定（重病、重残含三级智力/精神 → 无赡养能力）
        if (DictionaryConstants.HealthStatus.HasSevereDisease(member.HealthStatus)
            || DictionaryConstants.HealthStatus.HasSevereDisability(member.HealthStatus)
            || ClassificationConstants.DisabilityLevel.IsSevereForAssistance(
                member.DisabilityLevelKeyResolved, member.DisabilityType))
        {
            return false;
        }

        // 规则2：年龄判定（< 18岁 或 > 70岁 均无赡养能力）
        if (member.Age.HasValue && (member.Age < ClassificationConstants.SUPPORT_ABILITY_MIN_AGE || member.Age > ClassificationConstants.SUPPORT_ABILITY_MAX_AGE))
        {
            return false;
        }

        // 规则3：经济状况判定（从内存缓存查，无 DB 查询）
        if (!string.IsNullOrWhiteSpace(member.IdCard) &&
            classificationMap.TryGetValue(member.IdCard.Trim(), out var classification))
        {
            if (classification.Contains("Subsistence") || classification.Contains("LowIncome"))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 由家庭成员（赡养抚养扶养人）构建 Supporter（默认赡养费 + 赡养能力判定结果）
    /// </summary>
    private Supporter BuildSupporterFromMember(FamilyMember member, decimal defaultMonthlyFee, bool hasSupportAbility)
    {
        return new Supporter
        {
            ApplicationId = _applicationId,
            Id = member.Id,
            Name = member.Name,
            IdCard = member.IdCard,
            Relationship = member.RelationshipToHead,
            SelectedRelationship = MemberRelationOptions.FirstOrDefault(o => o.Key == member.RelationshipToHead),
            PersonType = "赡养",
            SelectedPersonType = PersonTypeOptions.FirstOrDefault(o => o.Key == "赡养"),
            MonthlySupportFee = defaultMonthlyFee,
            SupportMonths = 12,
            SupporterFamilySize = 1,
            IsSupportAbility = hasSupportAbility,
            Gender = member.Gender,
            Age = member.Age,
            Ethnicity = member.Ethnicity,
            Phone = member.Phone,
            HukouType = member.HukouType,
            MaritalStatus = member.MaritalStatus,
            EducationLevel = member.EducationLevel,
            PoliticalStatus = member.PoliticalStatus,
            HealthStatus = member.HealthStatus,
            WorkUnit = member.WorkUnit,
            EmploymentStatus = member.EmploymentStatus,
            MainIncomeSource = member.MainIncomeSource,
            WorkCapacity = member.WorkCapacity,
            AnnualIncome = member.AnnualIncome,
            HomeProvince = member.HomeProvince,
            HomeCity = member.HomeCity,
            HomeDistrict = member.HomeDistrict,
            HomeTown = member.HomeTown,
            HomeVillage = member.HomeVillage,
            HomeAddress = member.HomeAddress,
            HukouProvince = member.HukouProvince,
            HukouCity = member.HukouCity,
            HukouDistrict = member.HukouDistrict,
            HukouTown = member.HukouTown
        };
    }

    /// <summary>
    /// 保存前把 Step2"赡养抚养扶养"成员合并进 Supporters 集合（仅补缺失，不覆盖已有编辑值）。
    /// 未点"导入家庭成员"时保障其赡养费计入收入并随本单保存，避免被整表软删丢失。
    /// 已有匹配项以家庭成员为准同步基础字段（保留 Step3 编辑的赡养费/月数/人数/能力/人员类型）。
    /// </summary>
    private async Task MergeSupportMembersIntoSupportersAsync()
    {
        var missing = new List<FamilyMember>();
        foreach (var member in SupportMembers)
        {
            if (string.IsNullOrWhiteSpace(member.Name) || string.IsNullOrWhiteSpace(member.IdCard))
                continue;

            var existing = Supporters.FirstOrDefault(s =>
                (!string.IsNullOrWhiteSpace(s.IdCard) && string.Equals(s.IdCard.Trim(), member.IdCard.Trim(), StringComparison.OrdinalIgnoreCase))
                || string.Equals(s.Name, member.Name, StringComparison.Ordinal));

            if (existing == null)
                missing.Add(member);
            else
                SyncSupporterBasicsFromMember(existing, member);
        }

        if (missing.Count == 0) return;

        decimal subsistenceStandard = await GetCachedSubsistenceStandardAsync();
        decimal defaultMonthlyFee = Math.Round(subsistenceStandard * IncomeTypeConstants.DEFAULT_SUPPORT_FEE_RATIO, 2);

        var idCards = missing.Select(m => m.IdCard!.Trim()).Distinct().ToList();
        var classificationMap = await BatchGetClassificationByIdCardsAsync(idCards);

        foreach (var member in missing)
        {
            var hasAbility = DetermineSupportAbilityFromMember(member, classificationMap);
            Supporters.Add(BuildSupporterFromMember(member, defaultMonthlyFee, hasAbility));
            _logger.LogBusiness("保存前合并赡养人", ("Name", DataMasker.MaskName(member.Name)));
        }
    }

    /// <summary>
    /// 以家庭成员为准同步已有赡养人的基础字段（保留 Step3 编辑的赡养费/月数/家庭人数/能力/人员类型）
    /// </summary>
    private void SyncSupporterBasicsFromMember(Supporter supporter, FamilyMember member)
    {
        if (member.Id > 0) supporter.Id = member.Id;
        supporter.ApplicationId = _applicationId;
        supporter.Name = member.Name ?? string.Empty;
        supporter.IdCard = member.IdCard ?? string.Empty;
        supporter.Relationship = member.RelationshipToHead ?? string.Empty;
        supporter.SelectedRelationship = MemberRelationOptions.FirstOrDefault(o => o.Key == member.RelationshipToHead);
        supporter.Gender = member.Gender ?? string.Empty;
        supporter.Age = member.Age;
        supporter.Ethnicity = member.Ethnicity ?? string.Empty;
        supporter.Phone = member.Phone ?? string.Empty;
        supporter.HukouType = member.HukouType ?? string.Empty;
        supporter.MaritalStatus = member.MaritalStatus ?? string.Empty;
        supporter.EducationLevel = member.EducationLevel ?? string.Empty;
        supporter.PoliticalStatus = member.PoliticalStatus ?? string.Empty;
        supporter.HealthStatus = member.HealthStatus ?? string.Empty;
        supporter.WorkUnit = member.WorkUnit ?? string.Empty;
        supporter.EmploymentStatus = member.EmploymentStatus ?? string.Empty;
        supporter.MainIncomeSource = member.MainIncomeSource ?? string.Empty;
        supporter.WorkCapacity = member.WorkCapacity ?? string.Empty;
        supporter.AnnualIncome = member.AnnualIncome;
        supporter.HomeProvince = member.HomeProvince ?? string.Empty;
        supporter.HomeCity = member.HomeCity ?? string.Empty;
        supporter.HomeDistrict = member.HomeDistrict ?? string.Empty;
        supporter.HomeTown = member.HomeTown ?? string.Empty;
        supporter.HomeVillage = member.HomeVillage ?? string.Empty;
        supporter.HomeAddress = member.HomeAddress ?? string.Empty;
        supporter.HukouProvince = member.HukouProvince ?? string.Empty;
        supporter.HukouCity = member.HukouCity ?? string.Empty;
        supporter.HukouDistrict = member.HukouDistrict ?? string.Empty;
        supporter.HukouTown = member.HukouTown ?? string.Empty;
    }

    #endregion

    #region 赡养人命令

    [RelayCommand]
    private async Task AddSupporterAsync(string? personType)
    {
        var type = personType ?? "赡养";
        _logger.LogBusiness($"添加{type}人");

        // 低保标准：优先用缓存，避免每次添加都查 DB
        decimal subsistenceStandard = await GetCachedSubsistenceStandardAsync();
        decimal defaultMonthlyFee = Math.Round(subsistenceStandard * IncomeTypeConstants.DEFAULT_SUPPORT_FEE_RATIO, 2);
        decimal defaultAnnualFee = defaultMonthlyFee * 12;

        var supporter = new Supporter
        {
            ApplicationId = _applicationId,
            PersonType = type,
            SelectedPersonType = PersonTypeOptions.FirstOrDefault(o => o.Key == type),
            MonthlySupportFee = defaultMonthlyFee,
            SupportMonths = 12,
            SupporterFamilySize = 1,
            IsSupportAbility = true,
            Relationship = "",
            SelectedRelationship = null
        };

        // 手动添加时无法判定赡养能力（无 FamilyMember 关联），默认为有赡养能力
        // 导入时由 ImportSupportMembersAsync 在创建前判定

        _logger.LogBusiness($"添加{type}人完成",
            ("PersonType", type),
            ("MonthlyFee", defaultMonthlyFee.ToString()),
            ("AnnualFee", defaultAnnualFee.ToString()),
            ("IsSupportAbility", supporter.IsSupportAbility.ToString()));

        Supporters.Add(supporter);
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RemoveSupporterAsync(Supporter? supporter)
    {
        if (supporter == null) return;

        _logger.LogBusiness("删除赡养人", ("Name", DataMasker.MaskName(supporter.Name)));

        _supporterFeeBackup.Remove(supporter);
        Supporters.Remove(supporter);
        RecalculateIncome();
        await Task.CompletedTask;
    }

    /// <summary>
    /// 综合判定赡养人是否有赡养能力（基于 FamilyMember 数据）
    /// 规则：
    /// 1. 重病/重残（含三级智力、三级精神）→ 无赡养能力
    /// 2. 年龄 &lt; 18 或 &gt; 70 → 无赡养能力
    /// 3. 本身是低保/低收入 → 无赡养能力
    /// </summary>
    private async Task<bool> DetermineSupportAbilityFromMemberAsync(FamilyMember member)
    {
        // 规则1：健康/残疾状况判定（重病、重残含三级智力/精神 → 无赡养能力）
        if (DictionaryConstants.HealthStatus.HasSevereDisease(member.HealthStatus)
            || DictionaryConstants.HealthStatus.HasSevereDisability(member.HealthStatus)
            || ClassificationConstants.DisabilityLevel.IsSevereForAssistance(
                member.DisabilityLevelKeyResolved, member.DisabilityType))
        {
            _logger.Info($"赡养人{DataMasker.MaskName(member.Name)}因健康状况或残疾等级判定为无赡养能力");
            return false;
        }

        // 规则2：年龄判定（< 18岁 或 > 70岁 均无赡养能力）
        if (member.Age.HasValue && (member.Age < ClassificationConstants.SUPPORT_ABILITY_MIN_AGE || member.Age > ClassificationConstants.SUPPORT_ABILITY_MAX_AGE))
        {
            _logger.Info($"赡养人{DataMasker.MaskName(member.Name)}因年龄({member.Age})判定为无赡养能力");
            return false;
        }

        // 规则3：经济状况判定（查询是否有低保/低收入档案）
        if (!string.IsNullOrWhiteSpace(member.IdCard))
        {
            try
            {
                var appResult = await _applicationService.GetByIdCardAsync(member.IdCard);
                if (appResult.IsSuccess && appResult.Value?.Count > 0)
                {
                    var existingApp = appResult.Value.FirstOrDefault(a =>
                        a.Status == ApplicationStatusCodes.APPROVED || a.Status == ApplicationStatusCodes.COMPLETED);

                    if (existingApp != null)
                    {
                        var classification = existingApp.ClassificationResult;
                        if (!string.IsNullOrEmpty(classification) &&
                            (classification.Contains("Subsistence") || classification.Contains("LowIncome")))
                        {
                            _logger.Info($"赡养人{DataMasker.MaskName(member.Name)}因是低保/低收入({classification})判定为无赡养能力");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"查询赡养人档案失败: {ex.Message}");
            }
        }

        return true;
    }

    [RelayCommand]
    private async Task ImportSupportMembersAsync()
    {
        var supportMembers = FamilyMembers
            .Where(m => m.MemberCategory == MemberCategoryConstants.SUPPORT)
            .ToList();

        if (supportMembers.Count == 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请先在家庭成员页面添加赡养抚养扶养人", "确定");
            return;
        }

        // 低保标准：优先用缓存，避免每次导入都查 DB
        decimal subsistenceStandard = await GetCachedSubsistenceStandardAsync();
        decimal defaultMonthlyFee = Math.Round(subsistenceStandard * IncomeTypeConstants.DEFAULT_SUPPORT_FEE_RATIO, 2);

        // 批量查询赡养人关联的档案（替代 N+1 逐人查询）
        var idCards = supportMembers
            .Where(m => !string.IsNullOrWhiteSpace(m.IdCard))
            .Select(m => m.IdCard!.Trim())
            .Distinct()
            .ToList();
        var classificationMap = await BatchGetClassificationByIdCardsAsync(idCards);

        var count = 0;
        foreach (var member in supportMembers)
        {
            if (Supporters.Any(s => s.Name == member.Name))
                continue;

            // 基于 FamilyMember 数据 + 内存缓存判定赡养能力（无 DB 查询）
            var hasAbility = DetermineSupportAbilityFromMember(member, classificationMap);

            Supporters.Add(BuildSupporterFromMember(member, defaultMonthlyFee, hasAbility));
            count++;
        }

        if (count > 0)
        {
            RecalculateIncome();
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("成功", $"已导入 {count} 名赡养抚养扶养人", "确定");
        }
        else
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "所有赡养抚养扶养人已导入", "确定");
        }
    }

    #endregion

    #region 照料人命令

    [RelayCommand]
    private async Task AddCaregiverAsync()
    {
        _logger.LogBusiness("添加照料人");

        var caregiver = new Caregiver
        {
            ApplicationId = _applicationId,
            Relationship = MemberRelationOptions.FirstOrDefault()?.Key ?? string.Empty,
            Ethnicity = GetDefaultKey(EthnicityOptions, DefaultValuesConstants.ETHNICITY_KEY),
            MaritalStatus = GetDefaultKey(MaritalStatusOptions, DefaultValuesConstants.MARITAL_STATUS_KEY),
            EducationLevel = GetDefaultKey(EducationLevelOptions, DefaultValuesConstants.EDUCATION_LEVEL_KEY),
            PoliticalStatus = GetDefaultKey(PoliticalStatusOptions, DefaultValuesConstants.POLITICAL_STATUS_KEY),
            HealthStatus = GetDefaultKey(HealthStatusOptions, DefaultValuesConstants.HEALTH_STATUS_KEY),
            CreatedAt = DateTime.Now,
            SelectedEthnicity = EthnicityOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.ETHNICITY_KEY),
            SelectedMaritalStatus = MaritalStatusOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.MARITAL_STATUS_KEY),
            SelectedEducationLevel = EducationLevelOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.EDUCATION_LEVEL_KEY),
            SelectedPoliticalStatus = PoliticalStatusOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.POLITICAL_STATUS_KEY),
            SelectedHealthStatus = HealthStatusOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.HEALTH_STATUS_KEY)
        };

        Caregivers.Add(caregiver);
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RemoveCaregiverAsync(Caregiver? caregiver)
    {
        if (caregiver == null) return;

        _logger.LogBusiness("删除照料人", ("Name", DataMasker.MaskName(caregiver.Name)));
        Caregivers.Remove(caregiver);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 照料人身份证号变化时自动更新性别和年龄
    /// </summary>
    public void OnCaregiverIdCardChanged(Caregiver caregiver)
    {
        if (caregiver == null || string.IsNullOrWhiteSpace(caregiver.IdCard))
            return;

        if (caregiver.IdCard.Length >= 17)
        {
            // 从身份证号推导性别（第17位奇数为男，偶数为女）
            var genderDigit = caregiver.IdCard[16] - '0';
            caregiver.Gender = genderDigit % 2 == 1 ? "男" : "女";

            // 从身份证号推导年龄
            var birthYearStr = caregiver.IdCard.Substring(6, 4);
            if (int.TryParse(birthYearStr, out var birthYear))
            {
                var now = DateTime.Now;
                var age = now.Year - birthYear;
                if (now.Month < int.Parse(caregiver.IdCard.Substring(10, 2)) ||
                    (now.Month == int.Parse(caregiver.IdCard.Substring(10, 2)) &&
                     now.Day < int.Parse(caregiver.IdCard.Substring(12, 2))))
                {
                    age--;
                }
                caregiver.Age = age;
            }

            // 设置性别+年龄显示（与 FamilyMember 保持一致）
            if (caregiver.Age > 0)
                caregiver.GenderAgeDisplay = $"{caregiver.Gender} {caregiver.Age}岁";
            else
                caregiver.GenderAgeDisplay = caregiver.Gender;

            // 触发 UI 刷新
            caregiver.NotifyDisplayChanged();

            _logger.Info($"照料人身份证号变化: 性别={caregiver.Gender}, 年龄={caregiver.Age}, 显示={caregiver.GenderAgeDisplay}");
        }
    }

    #endregion

    #region 收入/支出项命令（通用方法）

    /// <summary>
    /// 刷新收入明细人员选项（户主 + 共同生活成员）
    /// </summary>
    private void RefreshIncomeMemberOptions()
    {
        IncomeMemberOptions.Clear();
        if (!string.IsNullOrWhiteSpace(ApplicantName))
        {
            IncomeMemberOptions.Add(new FamilyMember
            {
                Name = ApplicantName,
                IdCard = ApplicantIdCard,
                Age = Helpers.IdCardValidator.ExtractAgeBasic(ApplicantIdCard),
                MemberCategory = MemberCategoryConstants.HOUSEHOLD_HEAD
            });
        }
        foreach (var m in FamilyMembers.Where(m => m.MemberCategory == Constants.MemberCategoryConstants.SHARED_LIVING && !string.IsNullOrWhiteSpace(m.Name)))
        {
            IncomeMemberOptions.Add(m);
        }
    }

    /// <summary>
    /// 数据库加载完成后，根据 MemberName + MemberIdCard 反查 IncomeMemberOptions 中匹配的
    /// FamilyMember 并回填各收入实体的 SelectedIncomeMember，使 UI Picker 显示正确。
    /// </summary>
    private void RestoreIncomeMemberSelection()
    {
        if (IncomeMemberOptions.Count == 0) return;

        void MatchCollection<T>(IEnumerable<T> items) where T : class
        {
            foreach (var item in items)
            {
                var name = typeof(T).GetProperty("MemberName")?.GetValue(item) as string;
                var idCard = typeof(T).GetProperty("MemberIdCard")?.GetValue(item) as string;
                if (string.IsNullOrWhiteSpace(name)) continue;

                var match = IncomeMemberOptions.FirstOrDefault(m =>
                    m.Name == name && (string.IsNullOrEmpty(idCard) || m.IdCard == idCard));
                if (match == null) continue;

                typeof(T).GetProperty("SelectedIncomeMember")?.SetValue(item, match);
            }
        }

        MatchCollection(LaborIncomes);
        MatchCollection(BusinessIncomes);
        MatchCollection(PropertyIncomes);
        MatchCollection(TransferIncomes);
    }

    /// <summary>
    /// 弹出选择器让用户选择收入归属人员
    /// </summary>
    private async Task<FamilyMember?> PickIncomeMemberAsync(string title)
    {
        if (IncomeMemberOptions.Count == 0)
            RefreshIncomeMemberOptions();

        if (IncomeMemberOptions.Count == 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请先添加家庭成员", "确定");
            return null;
        }

        var names = IncomeMemberOptions.Select(m => m.Name).ToArray();
        var dialogService = _serviceProvider.GetRequiredService<IDialogService>();
        var result = await dialogService.DisplayActionSheetAsync(title, "取消", null, names);

        if (string.IsNullOrEmpty(result) || result == "取消")
            return null;

        return IncomeMemberOptions.FirstOrDefault(m => m.Name == result);
    }

    /// <summary>
    /// 添加收入项到集合（通用方法）
    /// </summary>
    private async Task AddIncomeItemAsync<T>(ObservableCollection<T> collection, T item, string itemName) where T : class
    {
        _logger.LogBusiness($"添加{itemName}");
        collection.Add(item);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 删除收入项（需要重新计算收入）
    /// </summary>
    private async Task RemoveIncomeItemAsync<T>(ObservableCollection<T> collection, T? item, string itemName) where T : class
    {
        if (item == null) return;
        try
        {
            _logger.LogBusiness($"删除{itemName}");

            // 让出当前 UI 帧并稍作等待，待 WinUI 完成按钮点击后的焦点/布局处理后再移除行，
            // 规避"移除含焦点控件"导致的 WinUI 原生层崩溃（删除补贴闪退根因）。
            await Task.Yield();
            await Task.Delay(30);

            collection.Remove(item);
            RecalculateIncome();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"删除{itemName}失败");
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// 删除资产项（无需重新计算收入）
    /// </summary>
    private async Task RemoveAssetItemAsync<T>(ObservableCollection<T> collection, T? item, string itemName) where T : class
    {
        if (item == null) return;
        try
        {
            _logger.LogBusiness($"删除{itemName}");

            // 同上：让出 UI 帧后移除行，规避移除含焦点控件的 WinUI 崩溃
            await Task.Yield();
            await Task.Delay(30);

            collection.Remove(item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"删除{itemName}失败");
        }
        await Task.CompletedTask;
    }

    #endregion

    #region 务工收入命令

    [RelayCommand]
    private async Task AddLaborIncomeAsync()
    {
        var member = await PickIncomeMemberAsync("选择务工人员");
        if (member == null) return;
        await AddIncomeItemAsync(LaborIncomes, new LaborIncome
        {
            ApplicationId = _applicationId,
            MemberId = member.Id,
            MemberName = member.Name,
            MemberIdCard = member.IdCard ?? "",
            MemberAge = member.Age ?? 0,
            MonthsWorked = 12,
            SelectedIncomeMember = member,
            CreatedAt = DateTime.Now
        }, "务工收入");
    }

    [RelayCommand]
    private async Task RemoveLaborIncomeAsync(LaborIncome? income) =>
        await RemoveIncomeItemAsync(LaborIncomes, income, "务工收入");

    #endregion

    #region 经营收入命令

    [RelayCommand]
    private async Task AddBusinessIncomeAsync()
    {
        var member = await PickIncomeMemberAsync("选择经营人员");
        if (member == null) return;
        await AddIncomeItemAsync(BusinessIncomes, new BusinessIncome
        {
            ApplicationId = _applicationId,
            MemberId = member.Id,
            MemberName = member.Name,
            MemberIdCard = member.IdCard ?? "",
            MemberAge = member.Age ?? 0,
            SelectedIncomeMember = member,
            CreatedAt = DateTime.Now
        }, "经营收入");
    }

    [RelayCommand]
    private async Task RemoveBusinessIncomeAsync(BusinessIncome? income) =>
        await RemoveIncomeItemAsync(BusinessIncomes, income, "经营收入");

    #endregion

    #region 刚性支出命令

    [RelayCommand]
    private async Task AddRigidExpenditureAsync() =>
        await AddIncomeItemAsync(RigidExpenditures, new RigidExpenditure { ApplicationId = _applicationId, ExpenditureType = DictionaryConstants.RigidExpenditureType.MEDICAL }, "刚性支出");

    [RelayCommand]
    private async Task RemoveRigidExpenditureAsync(RigidExpenditure? expenditure) =>
        await RemoveIncomeItemAsync(RigidExpenditures, expenditure, "刚性支出");

    #endregion

    #region 土地登记命令

    [RelayCommand]
    private async Task AddLandRegistrationAsync() =>
        await AddIncomeItemAsync(LandRegistrations, new LandRegistration { ApplicationId = _applicationId, LandUsage = DictionaryConstants.LandUsage.SELF_FARM, CreatedAt = DateTime.Now }, "土地登记");

    [RelayCommand]
    private async Task RemoveLandRegistrationAsync(LandRegistration? registration) =>
        await RemoveIncomeItemAsync(LandRegistrations, registration, "土地登记");

    #endregion

    #region 补贴命令

    [RelayCommand]
    private async Task AddSubsidyAsync() =>
        await AddIncomeItemAsync(Subsidies, new Subsidy { ApplicationId = _applicationId, SubsidyType = DictionaryConstants.SubsidyType.LAND_FERTILITY, Count = 1, RatioFactor = 1, CreatedAt = DateTime.Now }, "农业补贴");

    [RelayCommand]
    private async Task RemoveSubsidyAsync(Subsidy? subsidy) =>
        await RemoveIncomeItemAsync(Subsidies, subsidy, "农业补贴");

    #endregion

    #region 财产净收入命令

    [RelayCommand]
    private async Task AddPropertyIncomeAsync()
    {
        var member = await PickIncomeMemberAsync("选择财产归属人员");
        if (member == null) return;
        await AddIncomeItemAsync(PropertyIncomes, new PropertyIncome
        {
            ApplicationId = _applicationId,
            MemberId = member.Id,
            MemberName = member.Name,
            MemberIdCard = member.IdCard ?? "",
            MemberAge = member.Age ?? 0,
            SelectedIncomeMember = member,
            CreatedAt = DateTime.Now
        }, "财产净收入");
    }

    [RelayCommand]
    private async Task RemovePropertyIncomeAsync(PropertyIncome? income) =>
        await RemoveIncomeItemAsync(PropertyIncomes, income, "财产净收入");

    #endregion

    #region 转移净收入命令

    [RelayCommand]
    private async Task AddTransferIncomeAsync()
    {
        var member = await PickIncomeMemberAsync("选择转移收入人员");
        if (member == null) return;
        await AddIncomeItemAsync(TransferIncomes, new TransferIncome
        {
            ApplicationId = _applicationId,
            MemberId = member.Id,
            MemberName = member.Name,
            MemberIdCard = member.IdCard ?? "",
            MemberAge = member.Age ?? 0,
            MonthsOrTimes = 12,
            SelectedIncomeMember = member,
            CreatedAt = DateTime.Now
        }, "转移净收入");
    }

    [RelayCommand]
    private async Task RemoveTransferIncomeAsync(TransferIncome? income) =>
        await RemoveIncomeItemAsync(TransferIncomes, income, "转移净收入");

    #endregion

    #region 其他收入命令

    [RelayCommand]
    private async Task AddOtherIncomeAsync() =>
        await AddIncomeItemAsync(OtherIncomes, new OtherIncome { ApplicationId = _applicationId, CreatedAt = DateTime.Now }, "其他收入");

    [RelayCommand]
    private async Task RemoveOtherIncomeAsync(OtherIncome? income) =>
        await RemoveIncomeItemAsync(OtherIncomes, income, "其他收入");

    #endregion

    #region 通用添加收入命令

    [RelayCommand]
    private async Task AddIncomeAsync()
    {
        var options = new[] { "务工收入", "经营收入", "转移净收入", "财产净收入", "其他收入" };
        var dialogService = _serviceProvider.GetRequiredService<IDialogService>();
        var result = await dialogService.DisplayActionSheetAsync("选择收入类型", "取消", null, options);
        if (result == null || result == "取消") return;

        switch (result)
        {
            case "务工收入":
                await AddLaborIncomeAsync();
                break;
            case "经营收入":
                await AddBusinessIncomeAsync();
                break;
            case "转移净收入":
                await AddTransferIncomeAsync();
                break;
            case "财产净收入":
                await AddPropertyIncomeAsync();
                break;
            case "其他收入":
                await AddOtherIncomeAsync();
                break;
        }
    }

    #endregion

    #region 资产命令

    [RelayCommand]
    private async Task AddFamilyPropertyAsync() =>
        await AddIncomeItemAsync(FamilyProperties, new FamilyProperty { ApplicationId = _applicationId, CreatedAt = DateTime.Now }, "房产");

    [RelayCommand]
    private async Task RemoveFamilyPropertyAsync(FamilyProperty? property) =>
        await RemoveAssetItemAsync(FamilyProperties, property, "房产");

    [RelayCommand]
    private async Task AddVehicleAsync() =>
        await AddIncomeItemAsync(Vehicles, new Vehicle { ApplicationId = _applicationId, CreatedAt = DateTime.Now }, "车辆");

    [RelayCommand]
    private async Task RemoveVehicleAsync(Vehicle? vehicle) =>
        await RemoveAssetItemAsync(Vehicles, vehicle, "车辆");

    [RelayCommand]
    private async Task AddMachineryAsync() =>
        await AddIncomeItemAsync(Machineries, new Machinery { ApplicationId = _applicationId, Quantity = 1, CreatedAt = DateTime.Now }, "农机具");

    [RelayCommand]
    private async Task RemoveMachineryAsync(Machinery? machinery) =>
        await RemoveAssetItemAsync(Machineries, machinery, "农机具");

    private void SyncSupporterPickerOptions(Supporter supporter)
    {
        supporter.SelectedPersonType = PersonTypeOptions.FirstOrDefault(o => o.Key == supporter.PersonType);
        supporter.SelectedRelationship = MemberRelationOptions.FirstOrDefault(o => o.Key == supporter.Relationship);
    }

    #endregion

    #region 经济状况分组展开/折叠命令

    [RelayCommand]
    private void ToggleIncomeExpanded() => IsIncomeExpanded = !IsIncomeExpanded;

    [RelayCommand]
    private void ToggleLandExpanded() => IsLandExpanded = !IsLandExpanded;

    [RelayCommand]
    private void ToggleSubsidyExpanded() => IsSubsidyExpanded = !IsSubsidyExpanded;

    [RelayCommand]
    private void ToggleRigidExpenditureExpanded() => IsRigidExpenditureExpanded = !IsRigidExpenditureExpanded;

    [RelayCommand]
    private void TogglePropertyExpanded() => IsPropertyExpanded = !IsPropertyExpanded;

    [RelayCommand]
    private void ToggleSupportExpanded() => IsSupportExpanded = !IsSupportExpanded;

    #endregion


    [RelayCommand]
    private async Task ImportSubsidyDataAsync()
    {
        _logger.LogBusiness("导入农业补贴数据");

        try
        {
            IsBusy = true;
            var idCards = new List<string>();
            if (!string.IsNullOrWhiteSpace(ApplicantIdCard))
                idCards.Add(ApplicantIdCard);

            // 身份证来源：只取户主 + 共同生活成员，排除赡养抚养扶养人
            foreach (var member in SharedLivingMembers)
            {
                if (!string.IsNullOrWhiteSpace(member.IdCard) && !idCards.Contains(member.IdCard))
                    idCards.Add(member.IdCard);
            }

            var result = await _subsidyDataService.GetSubsidiesByIdCardsAsync(idCards);
            if (result.IsSuccess && result.Value?.Count > 0)
            {
                Subsidies.Clear();
                foreach (var record in result.Value)
                {
                    // 地力补贴 SQL 返回 area=0、amount=政府原始总金额；种植/轮作补贴返回实际面积。
                    // 地力补贴有面积但 SQL 未返回，用金额÷单价反推亩数。
                    var area = record.Area;
                    if (area == 0 && record.Amount > 0)
                    {
                        // 地力补贴：金额÷单价反推面积
                        var unitPrice = SubsidyPriceConstants.GetPrice(record.SubsidyType);
                        area = unitPrice > 0
                            ? (decimal)SubsidyPriceConstants.CalculateArea(record.Amount, unitPrice)
                            : 0;
                    }
                    var subsidy = new Models.Entities.Subsidy
                    {
                        ApplicationId = _applicationId,
                        SubsidyType = record.SubsidyType,
                        Area = area,
                        MemberName = record.Name,
                        MemberIdCard = record.IdCard,
                        CreatedAt = DateTime.Now
                    };
                    Subsidies.Add(subsidy);
                }
                RecalculateIncome();
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("成功", $"共导入 {result.Value.Count} 条补贴数据", "确定");
            }
            else
            {
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", "未找到农业补贴数据", "确定");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"导入农业补贴失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }


    #region 土地确权归户表命令

    [RelayCommand]
    private void AddLandConfirmationGroup()
    {
        var group = new LandConfirmationGroup
        {
            GroupId = _nextGroupId++,
            ContractorName = ApplicantName ?? string.Empty
        };
        group.Records.CollectionChanged += (s, e) => CalculateLandConfirmationArea();
        group.Persons.CollectionChanged += (s, e) => CalculateLandConfirmationArea();
        LandConfirmationGroups.Add(group);

        // 自动从家庭成员填充人员
        InitializePersonsForAllGroups();
    }

    [RelayCommand]
    private async Task RemoveLandConfirmationGroup(LandConfirmationGroup? group)
    {
        if (group == null) return;
        // 让出 UI 帧后再移除行，规避 WinUI 移除含焦点控件的原生层崩溃
        await Task.Yield();
        await Task.Delay(30);
        LandConfirmationGroups.Remove(group);
        CalculateLandConfirmationArea();
    }

    [RelayCommand]
    private void AddLandConfirmationRecord(LandConfirmationGroup? group)
    {
        if (group == null) return;
        var record = new LandConfirmationRecord
        {
            MemberName = group.ContractorName,
            UnitPrice = GetUnitPriceByLandUsage(DictionaryConstants.LandUsage.SELF_FARM)
        };
        record.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(LandConfirmationRecord.LandUsage))
            {
                record.UnitPrice = GetUnitPriceByLandUsage(record.LandUsage);
                CalculateLandConfirmationArea();
            }
            else if (e.PropertyName == nameof(LandConfirmationRecord.LandArea)
                  || e.PropertyName == nameof(LandConfirmationRecord.UnitPrice))
            {
                CalculateLandConfirmationArea();
            }
        };
        group.Records.Add(record);
    }

    [RelayCommand]
    private async Task RemoveLandConfirmationRecord(LandConfirmationRecord? record)
    {
        if (record == null) return;
        // 让出 UI 帧后再移除行，规避 WinUI 移除含焦点控件的原生层崩溃
        await Task.Yield();
        await Task.Delay(30);
        foreach (var group in LandConfirmationGroups)
        {
            if (group.Records.Contains(record))
            {
                group.Records.Remove(record);
                break;
            }
        }
        CalculateLandConfirmationArea();
    }

    [RelayCommand]
    private void AddLandConfirmationPerson(LandConfirmationGroup? group)
    {
        if (group == null) return;
        var person = new LandConfirmationPerson
        {
            LandStatus = LandStatusConstants.LIVING_ENTITLED,
            SharesCount = 1
        };
        person.PropertyChanged += (s, e) => CalculateLandConfirmationArea();
        group.Persons.Add(person);
        CalculateLandConfirmationArea();
    }

    [RelayCommand]
    private async Task RemoveLandConfirmationPerson(LandConfirmationPerson? person)
    {
        if (person == null) return;
        // 让出 UI 帧后再移除行，规避 WinUI 移除含焦点控件的原生层崩溃
        await Task.Yield();
        await Task.Delay(30);
        foreach (var group in LandConfirmationGroups)
        {
            if (group.Persons.Contains(person))
            {
                group.Persons.Remove(person);
                break;
            }
        }
        CalculateLandConfirmationArea();
    }

    public decimal GetUnitPriceByLandUsage(string landUsage)
    {
        return landUsage switch
        {
            DictionaryConstants.LandUsage.SELF_FARM => SelfFarmUnitPrice,
            DictionaryConstants.LandUsage.SUBLEASE => SubleaseUnitPrice,
            DictionaryConstants.LandUsage.CONTRACT => ContractUnitPrice,
            _ => 0
        };
    }

    [RelayCommand]
    private async Task ImportLandConfirmationAsync()
    {
        try
        {
            IsBusy = true;
            _logger.LogBusiness("导入土地确权归户表");

        var names = new List<string>();
        if (!string.IsNullOrWhiteSpace(ApplicantName))
            names.Add(ApplicantName);
        foreach (var member in SharedLivingMembers)
        {
            if (!string.IsNullOrWhiteSpace(member.Name) && !names.Contains(member.Name))
                names.Add(member.Name);
        }

            var result = await _landContractService.GetRecordsByNamesAsync(names);
            if (result.IsSuccess && result.Value.Count > 0)
            {
                var recordsByContractor = result.Value
                    .GroupBy(r => r.MemberName)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var kvp in recordsByContractor)
                {
                    var existingGroup = LandConfirmationGroups
                        .FirstOrDefault(g => g.ContractorName == kvp.Key);

                    if (existingGroup == null)
                    {
                        var newGroup = new LandConfirmationGroup
                        {
                            GroupId = _nextGroupId++,
                            ContractorName = kvp.Key
                        };

                        foreach (var record in kvp.Value)
                        {
                            record.IsImported = true;
                            record.UnitPrice = GetUnitPriceByLandUsage(record.LandUsage);
                            record.PropertyChanged += (s, e) =>
                            {
                                if (e.PropertyName == nameof(LandConfirmationRecord.LandUsage))
                                {
                                    record.UnitPrice = GetUnitPriceByLandUsage(record.LandUsage);
                                    CalculateLandConfirmationArea();
                                }
                                else if (e.PropertyName == nameof(LandConfirmationRecord.LandArea)
                                      || e.PropertyName == nameof(LandConfirmationRecord.UnitPrice))
                                {
                                    CalculateLandConfirmationArea();
                                }
                            };
                            newGroup.Records.Add(record);
                        }

                        newGroup.Records.CollectionChanged += (s, e) => CalculateLandConfirmationArea();
                        newGroup.Persons.CollectionChanged += (s, e) => CalculateLandConfirmationArea();
                        LandConfirmationGroups.Add(newGroup);
                    }
                    else
                    {
                        foreach (var record in kvp.Value)
                        {
                            var exists = existingGroup.Records.Any(r => r.PlotCode == record.PlotCode);
                            if (!exists)
                            {
                                record.IsImported = true;
                                record.UnitPrice = GetUnitPriceByLandUsage(record.LandUsage);
                                record.PropertyChanged += (s, e) =>
                                {
                                    if (e.PropertyName == nameof(LandConfirmationRecord.LandUsage))
                                    {
                                        record.UnitPrice = GetUnitPriceByLandUsage(record.LandUsage);
                                        CalculateLandConfirmationArea();
                                    }
                                    else if (e.PropertyName == nameof(LandConfirmationRecord.LandArea)
                                          || e.PropertyName == nameof(LandConfirmationRecord.UnitPrice))
                                    {
                                        CalculateLandConfirmationArea();
                                    }
                                };
                                existingGroup.Records.Add(record);
                            }
                        }
                    }
                }

                InitializePersonsForAllGroups();
                CalculateLandConfirmationArea();

                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("成功", $"已导入{result.Value.Count}条记录，分为{recordsByContractor.Count}个归户表", "确定");
            }
            else
            {
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", "未找到土地确权数据", "确定");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"导入土地数据失败: {ex.Message}");
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("错误", $"导入失败: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 批量设置土地用途（全自种/全转包/全承包）
    /// </summary>
    [RelayCommand]
    private void BatchSetLandUsage(string landUsage)
    {
        if (LandConfirmationGroups.Count == 0) return;

        _logger.LogBusiness("批量设置土地用途", ("LandUsage", landUsage));

        // 根据用途设置默认单价
        decimal defaultPrice = landUsage switch
        {
            DictionaryConstants.LandUsage.SELF_FARM => SelfFarmUnitPrice,
            DictionaryConstants.LandUsage.SUBLEASE => SubleaseUnitPrice,
            DictionaryConstants.LandUsage.CONTRACT => ContractUnitPrice,
            _ => 0m
        };

        foreach (var group in LandConfirmationGroups)
        {
            foreach (var record in group.Records)
            {
                record.LandUsage = landUsage;
                record.UnitPrice = defaultPrice;
            }
        }
        CalculateLandConfirmationArea();
    }

    /// <summary>
    /// 计算土地确权汇总
    /// </summary>
    private void CalculateLandConfirmationArea()
    {
        if (LandConfirmationGroups.Count == 0)
        {
            TotalConfirmedLandArea = 0;
            TotalLandShares = 0;
            TotalLandPersonCount = 0;
            FamilyLandArea = 0;
            FamilyLandShares = 0;
            FamilyLandPersonCount = 0;
            SelfFarmedLandArea = 0;
            SubleasedLandArea = 0;
            ContractedLandArea = 0;
            LandIncomeTotal = 0;
            OnPropertyChanged(nameof(CalculatedPerPersonArea));
            OnPropertyChanged(nameof(LandCalculationFormula));
            RecalculateIncome();
            return;
        }

        TotalConfirmedLandArea = (decimal)LandConfirmationGroups.Sum(g => g.TotalArea);
        TotalLandShares = (decimal)LandConfirmationGroups.Sum(g => g.TotalShares);
        TotalLandPersonCount = LandConfirmationGroups.Sum(g => g.PersonCount);

        // 构建家庭成员姓名集（户主 + 共同居住人员）
        var familyNames = new HashSet<string>();
        if (!string.IsNullOrWhiteSpace(ApplicantName))
            familyNames.Add(ApplicantName);
        foreach (var member in FamilyMembers)
        {
            if (member.IsHouseholdHead || member.MemberCategory == MemberCategoryConstants.SHARED_LIVING)
            {
                if (!string.IsNullOrWhiteSpace(member.Name))
                    familyNames.Add(member.Name);
            }
        }

        // 按种植类型汇总面积和收入（全部记录）
        decimal selfFarmed = 0, subleased = 0, contracted = 0;
        decimal selfFarmedIncome = 0, subleasedIncome = 0, contractedIncome = 0;
        foreach (var group in LandConfirmationGroups)
        {
            foreach (var record in group.Records)
            {
                switch (record.LandUsage)
                {
                    case DictionaryConstants.LandUsage.SELF_FARM:
                        selfFarmed += record.LandArea;
                        selfFarmedIncome += record.LandValue;
                        break;
                    case DictionaryConstants.LandUsage.SUBLEASE:
                        subleased += record.LandArea;
                        subleasedIncome += record.LandValue;
                        break;
                    case DictionaryConstants.LandUsage.CONTRACT:
                        contracted += record.LandArea;
                        contractedIncome += record.LandValue;
                        break;
                }
            }
        }

        FamilyLandArea = 0;
        FamilyLandShares = 0;
        FamilyLandPersonCount = 0;

        // 按组计算家庭收入（家庭面积占比 × 该组全部收入）
        decimal familyIncome = 0;
        foreach (var group in LandConfirmationGroups)
        {
            var gArea = (decimal)group.TotalArea;
            var gShares = (decimal)group.TotalShares;
            if (gShares <= 0) continue;

            // 死亡继承份额转移等口径统一收敛到 LandShareCalculator（唯一权威实现）：
            // 本组内"死亡继承"人员的份额累加到指定继承人（LandInheritTo）名下，
            // 死亡人员本身不再单独计入家庭份额；未指定继承人/继承人不计份额时该份额不参与家庭计算。
            var effectiveShares = LandShareCalculator.ComputeEffectiveShares(group);

            decimal familyRatio = 0;
            foreach (var kv in effectiveShares)
            {
                if (familyNames.Contains(kv.Key))
                {
                    FamilyLandShares += kv.Value;
                    FamilyLandPersonCount++;
                    familyRatio += kv.Value;
                }
            }
            familyRatio = familyRatio / gShares;

            decimal groupIncome = 0;
            foreach (var record in group.Records)
                groupIncome += record.LandValue;

            familyIncome += Math.Round(groupIncome * familyRatio, 2);
        }

        FamilyLandArea = TotalLandShares > 0
            ? Math.Round(TotalConfirmedLandArea / TotalLandShares * FamilyLandShares, 2)
            : 0;

        // 按家庭份额折算三个子分类面积
        decimal familyRatioAll = TotalLandShares > 0 ? FamilyLandShares / TotalLandShares : 0;
        SelfFarmedLandArea = Math.Round(selfFarmed * familyRatioAll, 2);
        SubleasedLandArea = Math.Round(subleased * familyRatioAll, 2);
        ContractedLandArea = Math.Round(contracted * familyRatioAll, 2);

        LandIncomeTotal = Math.Round(familyIncome, 2);
        RecalculateIncome();

        OnPropertyChanged(nameof(CalculatedPerPersonArea));
        OnPropertyChanged(nameof(LandCalculationFormula));

        // 按组内份额计算每人的面积
        foreach (var group in LandConfirmationGroups)
        {
            var gArea = (decimal)group.TotalArea;
            var gShares = (decimal)group.TotalShares;
            foreach (var person in group.Persons)
            {
                if (gShares > 0 && LandStatusConstants.ShouldCount(person.LandStatus))
                    person.TotalLandArea = Math.Round((double)(gArea / gShares * (decimal)person.SharesCount), 2);
                else
                    person.TotalLandArea = 0;
            }
        }
    }

    /// <summary>
    /// 初始化归户表人员（仅对人员表为空的组补种户主 + 共同生活成员）。
    /// 已有人员的组（含用户手工增删过的）不再补种，避免把用户删除的人员自动加回；
    /// 赡养抚养扶养人（Support）不属于本户共同生活成员，不补种（否则会稀释家庭土地份额）。
    /// </summary>
    private void InitializePersonsForAllGroups()
    {
        foreach (var group in LandConfirmationGroups)
        {
            // 仅补种空人员表：已有保存/用户编辑结果时保持原样
            if (group.Persons.Count > 0) continue;

            if (!string.IsNullOrWhiteSpace(ApplicantName))
            {
                var head = new LandConfirmationPerson
                {
                    Name = ApplicantName,
                    IdCard = ApplicantIdCard ?? "",
                    LandStatus = LandStatusConstants.LIVING_ENTITLED,
                    SharesCount = 1
                };
                head.PropertyChanged += (s, e) => CalculateLandConfirmationArea();
                group.Persons.Add(head);
            }

            foreach (var member in SharedLivingMembers)
            {
                if (string.IsNullOrWhiteSpace(member.Name)) continue;

                var person = new LandConfirmationPerson
                {
                    Name = member.Name,
                    IdCard = member.IdCard ?? "",
                    LandStatus = LandStatusConstants.LIVING_ENTITLED,
                    SharesCount = 1
                };
                person.PropertyChanged += (s, e) => CalculateLandConfirmationArea();
                group.Persons.Add(person);
            }
        }
    }

    #endregion

    #region 事件处理方法（供 Page.xaml.cs 调用）

    public void OnIdCardChanged(FamilyMember member)
    {
        if (member == null) return;
        member.UpdateGenderAndAgeFromIdCard();
        string? diseaseKey = null;
        if (member.DiseaseCategory != null && _diseaseCategoryMap.ContainsKey(member.DiseaseCategory))
            diseaseKey = member.DiseaseCategory;
        member.UpdateHealthStatus(_noDiseaseKey, _severeLevelKeys, diseaseKey);
    }

    public void OnDisabilityCertificateChanged(FamilyMember member)
    {
        if (member == null) return;

        // 证号为空或长度不足2位 → 清空残疾信息（清除旧残留）
        if (string.IsNullOrWhiteSpace(member.DisabilityCertificateNo) || member.DisabilityCertificateNo.Length < 2)
        {
            member.ClearDisabilityInfo();
            // 更新健康状态
            string? diseaseKey = null;
            if (member.DiseaseCategory != null && _diseaseCategoryMap.ContainsKey(member.DiseaseCategory))
                diseaseKey = member.DiseaseCategory;
            member.UpdateHealthStatus(_noDiseaseKey, _severeLevelKeys, diseaseKey);
            return;
        }

        // 恰好2位时解析残疾证号（存字典 key，显示由 DisabilityTypeDisplay/DisabilityLevelDisplay 提供）
        member.ParseDisabilityCertificate(
            _disabilityTypeKeyMap,
            _disabilityLevelKeyMap);

        // 解析后同步更新健康状态
        string? diseaseKey2 = null;
        if (member.DiseaseCategory != null && _diseaseCategoryMap.ContainsKey(member.DiseaseCategory))
            diseaseKey2 = member.DiseaseCategory;
        member.UpdateHealthStatus(_noDiseaseKey, _severeLevelKeys, diseaseKey2);
    }

    public void OnDiseaseCategoryChanged(FamilyMember member)
    {
        if (member == null) return;
        // 先保存当前二级疾病值，防止 LoadSecondaryDiseases 的 Clear 触发级联清空
        var savedDiseaseName = member.DiseaseName;
        member.LoadSecondaryDiseases(_diseaseCategoryMap);
        // 手动切换一级分类时，旧二级可能不在新列表中（正确行为：清空）
        // 加载/同分类重触发时，旧值仍在新列表中（自动保留）
        if (!string.IsNullOrEmpty(savedDiseaseName) && member.SecondaryDiseaseOptions.Any(o => o.Key == savedDiseaseName))
            member.DiseaseName = savedDiseaseName;
        member.UpdateHealthStatus(_noDiseaseKey, _severeLevelKeys, member.DiseaseCategory);
    }

    public void OnSearchResultSelectionChanged(object? previousSelection, object? currentSelection)
    {
        _logger.Info($"搜索结果选中变化");
    }

    /// <summary>
    /// 同步家庭成员的字典选项属性（从 string Key → DictItemOption）
    /// </summary>
    private void SyncMemberDictOptions(FamilyMember member)
    {
        if (!string.IsNullOrEmpty(member.Ethnicity))
            member.SelectedEthnicity = EthnicityOptions.FirstOrDefault(o => o.Key == member.Ethnicity);

        if (!string.IsNullOrEmpty(member.MaritalStatus))
            member.SelectedMaritalStatus = MaritalStatusOptions.FirstOrDefault(o => o.Key == member.MaritalStatus);

        if (!string.IsNullOrEmpty(member.EducationLevel))
            member.SelectedEducationLevel = EducationLevelOptions.FirstOrDefault(o => o.Key == member.EducationLevel);

        if (!string.IsNullOrEmpty(member.PoliticalStatus))
            member.SelectedPoliticalStatus = PoliticalStatusOptions.FirstOrDefault(o => o.Key == member.PoliticalStatus);

        if (!string.IsNullOrEmpty(member.HukouType))
            member.SelectedHukouType = HukouTypeOptions.FirstOrDefault(o => o.Key == member.HukouType);

        if (!string.IsNullOrEmpty(member.EmploymentStatus))
            member.SelectedEmploymentStatus = EmploymentStatusOptions.FirstOrDefault(o => o.Key == member.EmploymentStatus);

        if (!string.IsNullOrEmpty(member.MainIncomeSource))
            member.SelectedIncomeSource = IncomeSourceOptions.FirstOrDefault(o => o.Key == member.MainIncomeSource);

        if (!string.IsNullOrEmpty(member.DiseaseCategory))
        {
            member.SelectedDiseaseCategory = DiseaseCategoryOptions.FirstOrDefault(o => o.Key == member.DiseaseCategory);
            // 先保存疾病名称，防止 LoadSecondaryDiseases 的 Clear 触发级联清空
            var savedDiseaseName = member.DiseaseName;
            member.LoadSecondaryDiseases(_diseaseCategoryMap);
            // 恢复二级疾病选中（加载时 LoadSecondaryDiseases 已触发 SelectionChanged 清空了 DiseaseName）
            if (!string.IsNullOrEmpty(savedDiseaseName))
                member.DiseaseName = savedDiseaseName;
            if (!string.IsNullOrEmpty(member.DiseaseName))
                member.SelectedDiseaseNameObj = member.SecondaryDiseaseOptions.FirstOrDefault(o => o.Key == member.DiseaseName);
        }

        if (!string.IsNullOrEmpty(member.RelationshipToHead))
            member.SelectedRelationshipToHead = MemberRelationOptions.FirstOrDefault(o => o.Key == member.RelationshipToHead);
    }

    #endregion
}
