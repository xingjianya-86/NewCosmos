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
using NewCosmos.Services.Utilities;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.ChangeManagement;
using System.Collections.ObjectModel;
using System.ComponentModel;

using Application = NewCosmos.Models.Entities.Application;

namespace NewCosmos.ViewModels.SocialAssistance;

public partial class ApplicationFormViewModel
{
    /// <summary>
    /// 初始化（加载字典数据和配置）
    /// </summary>
    public async Task InitializeAsync()
    {
        if (IsInitialized) return;

        try
        {
            IsBusy = true;
            LoadingMessage = "正在加载基础数据...";

            // 加载字典数据
            await LoadDictionariesAsync();

            // 加载映射数据
            await LoadDisabilityMappingsAsync();
            await LoadDiseaseMappingsAsync();
            LoadSpecialKeys();

            // 设置默认值
            SetDefaultValues();

            // 默认选中"无任何疾病"
            if (DiseaseCategoryOptions.Any(o => o.Key == DefaultValuesConstants.DISEASE_CATEGORY_KEY))
                SelectedDiseaseCategoryObj = DiseaseCategoryOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.DISEASE_CATEGORY_KEY);

            // 加载省市区数据
            await LoadRegionDataAsync();

            // 加载默认地区（基于当前用户单位）
            await LoadDefaultRegionAsync();

            // 加载调查员所属机构
            await LoadSurveyorOrganizationAsync();

            IsInitialized = true;
            _logger.LogBusiness("初始化完成");

            // 同步字段值到 FormFieldDescriptor 并触发验证
            ApplicantNameField.Value = ApplicantName;
            ApplicantIdCardField.Value = ApplicantIdCard;
            PhoneField.Value = ApplicantPhone;

            // 字典数据加载完成后刷新 Picker 绑定
            ForceRefreshPickerBindings();

            // 刷新收入明细人员选项
            RefreshIncomeMemberOptions();
        }
        catch (Exception ex)
        {
            _logger.Error($"初始化失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 加载字典数据
    /// </summary>
    private async Task LoadDictionariesAsync()
    {
        await LoadDictionaryAsync("Gender", GenderOptions);
        await LoadDictionaryAsync("Ethnicities", EthnicityOptions);
        await LoadDictionaryAsync("MaritalStatuses", MaritalStatusOptions);
        await LoadDictionaryAsync("EducationLevels", EducationLevelOptions);
        await LoadDictionaryAsync("PoliticalStatuses", PoliticalStatusOptions);
        await LoadDictionaryAsync("SupportModes", SupportModeOptions);
        await LoadDictionaryAsync("ApplicationReasons", ApplicationReasonOptions);
        await LoadDictionaryAsync("DiseaseCategories", DiseaseCategoryOptions);
        await LoadDictionaryAsync("DisabilityTypes", DisabilityTypeOptions);
        await LoadDictionaryAsync("DisabilityLevels", DisabilityLevelOptions);
        await LoadDictionaryAsync("HealthStatuses", HealthStatusOptions);
        await LoadDictionaryAsync("EmploymentStatuses", EmploymentStatusOptions);
        await LoadDictionaryAsync("IncomeSources", IncomeSourceOptions);
    }

    /// <summary>
    /// 加载字典数据（通用方法）
    /// </summary>
    private async Task LoadDictionaryAsync(string category, ObservableCollection<DictItemOption> target)
    {
        try
        {
            var result = await _dictionaryService.GetItemsByCategoryAsync(category);
            if (result.IsSuccess)
            {
                target.Clear();
                foreach (var item in result.Value)
                {
                    target.Add(new DictItemOption { Key = item.ItemKey, Display = item.ItemValue });
                }
            }
            else
            {
                _logger.Warn($"加载字典数据失败 [{category}]: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"加载字典数据失败 [{category}]: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载残疾类型/等级映射（从字典数据，不硬编码）
    /// </summary>
    private async Task LoadDisabilityMappingsAsync()
    {
        try
        {
            var typesResult = await _dictionaryService.GetItemsByCategoryAsync(DisabilityConstants.DISABILITY_TYPES_CATEGORY);
            if (typesResult.IsSuccess)
            {
                _disabilityTypeKeyMap.Clear();
                _disabilityTypeDisplayMap.Clear();
                char code = '1';
                foreach (var item in typesResult.Value.OrderBy(x => x.SortOrder))
                {
                    _disabilityTypeKeyMap[code++] = item.ItemKey;
                    _disabilityTypeDisplayMap[item.ItemKey] = item.ItemValue;
                }
            }

            var levelsResult = await _dictionaryService.GetItemsByCategoryAsync(DisabilityConstants.DISABILITY_LEVELS_CATEGORY);
            if (levelsResult.IsSuccess)
            {
                _disabilityLevelKeyMap.Clear();
                _disabilityLevelDisplayMap.Clear();
                char code = '1';
                foreach (var item in levelsResult.Value.OrderBy(x => x.SortOrder))
                {
                    var key = item.ItemKey;
                    _disabilityLevelKeyMap[code++] = key;
                    _disabilityLevelDisplayMap[key] = item.ItemValue;
                }
            }

            _logger.Info("残疾映射加载完成");
        }
        catch (Exception ex)
        {
            _logger.Error($"加载残疾映射失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载疾病分类映射（从 DiseaseNames 字典）
    /// </summary>
    private async Task LoadDiseaseMappingsAsync()
    {
        try
        {
            var result = await _dictionaryService.GetItemsByCategoryAsync(DiseaseConstants.DISEASE_NAMES_CATEGORY);
            if (result.IsSuccess)
            {
                _diseaseCategoryMap.Clear();
                foreach (var item in result.Value)
                {
                    // 使用 Description 作为键（疾病分类名称）
                    if (!_diseaseCategoryMap.TryGetValue(item.Description, out var list))
                    {
                        list = new List<DiseaseItem>();
                        _diseaseCategoryMap[item.Description] = list;
                    }
                    list.Add(new DiseaseItem
                    {
                        Key = item.ItemKey,
                        Value = item.ItemValue,
                        SortOrder = item.SortOrder
                    });
                }
            }
            _logger.Info("疾病映射加载完成");
        }
        catch (Exception ex)
        {
            _logger.Error($"加载疾病映射失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载特殊项 key
    /// </summary>
    private void LoadSpecialKeys()
    {
        try
        {
            if (DiseaseCategoryOptions.Any(o => o.Key == DefaultValuesConstants.DISEASE_CATEGORY_KEY))
                _noDiseaseKey = DefaultValuesConstants.DISEASE_CATEGORY_KEY;

            _severeLevelKeys = _disabilityLevelDisplayMap
                .Where(x => x.Value.StartsWith("一级") || x.Value.StartsWith("二级"))
                .Select(x => x.Key)
                .ToList();

            _logger.Info("特殊项 key 加载完成");
        }
        catch (Exception ex)
        {
            _logger.Error($"加载特殊项 key 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 设置默认值（从字典数据获取，不硬编码）
    /// </summary>
    private void SetDefaultValues()
    {
        try
        {
            if (SelectedEthnicity == null)
                SelectedEthnicity = EthnicityOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.ETHNICITY_KEY);

            if (SelectedMaritalStatus == null)
                SelectedMaritalStatus = MaritalStatusOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.MARITAL_STATUS_KEY);

            if (SelectedEducationLevel == null)
                SelectedEducationLevel = EducationLevelOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.EDUCATION_LEVEL_KEY);

            if (SelectedPoliticalStatus == null)
                SelectedPoliticalStatus = PoliticalStatusOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.POLITICAL_STATUS_KEY);

            if (SelectedApplicationReasonObj == null)
                SelectedApplicationReasonObj = ApplicationReasonOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.APPLICATION_REASON_KEY);

            if (SelectedHealthStatusObj == null)
                SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.HEALTH_STATUS_KEY);

            if (SelectedDiseaseCategoryObj == null)
                SelectedDiseaseCategoryObj = DiseaseCategoryOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.DISEASE_CATEGORY_KEY);

            if (SelectedSupportModeObj == null)
                SelectedSupportModeObj = SupportModeOptions.FirstOrDefault(o => o.Key == ClassificationConstants.SupportMode.SCATTERED);

            if (HukouType == null)
                HukouType = HukouTypeOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.HUKOU_TYPE_KEY);

            SurveyDate = ResolveDefaultSurveyDate();
            SurveyorName = App.CurrentUserFullName;

            _logger.Info("默认值设置完成");
        }
        catch (Exception ex)
        {
            _logger.Error($"设置默认值失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 新档「入户调查日期」默认值：今天；今天已进入（或晚于）当月 B 线公示窗口时回退
    /// 公示开始前一个工作日——口径与 C 线 GetTempReliefInvestigationPrintDate 一致。
    /// 窗口按「申请日期=今天」锚定（GetTimelineForApplicationDate：今天晚于当月会议日自动顺延下月窗口），
    /// 与档案产出打印的公示窗口同口径——如此刻已过本月会议日（如 10-12 > 10-08），新档窗口即顺延到
    /// 下月（公示 11-09~11-15），调查默认今天不会再落进本月公示期（刘明珍档 10-12 调查落 10-09~10-15 的根因）。
    /// </summary>
    private DateTime ResolveDefaultSurveyDate()
    {
        try
        {
            var today = DateTime.Today;
            var timeline = _serviceProvider.GetRequiredService<IBusinessTimelineService>();
            var bLine = timeline.GetTimelineForApplicationDate(today, TimelineType.BusinessProcess);
            var prePublicity = timeline.GetPrePublicityWorkday(bLine);
            return today > prePublicity ? prePublicity : today;
        }
        catch (Exception ex)
        {
            _logger.Warn($"计算调查日期默认值失败，回退今天: {ex.Message}");
            return DateTime.Today;
        }
    }

    /// <summary>
    /// 加载省市区数据
    /// </summary>
    private async Task LoadRegionDataAsync()
    {
        // 保护期内赋值 SelectedProvince：OnSelectedProvinceChanged 会触发 LoadCitiesAsync，
        // 其异步返回时 Clear 掉刚加载的城市选项（“小概率城市读不进”的根因竞态），必须屏蔽
        _isLoadingDefaults = true;
        try
        {
            ProvinceOptions.Clear();
            ProvinceOptions.Add(DefaultValuesConstants.HOME_PROVINCE);
            if (string.IsNullOrEmpty(SelectedProvince))
                SelectedProvince = DefaultValuesConstants.HOME_PROVINCE;

            await LoadCityOptionsAsync();

            if (CityOptions.Count == 0)
            {
                // 兜底重试：本地快照缺失 + DB 瞬时故障时短延迟再拉一次
                await Task.Delay(800);
                await LoadCityOptionsAsync();
            }

            if (CityOptions.Count == 0)
                _logger.Warn("城市数据加载失败: 重试后 CityOptions 仍为空");
        }
        catch (Exception ex)
        {
            _logger.Error($"加载省市区数据失败: {ex.Message}");
        }
        finally
        {
            _isLoadingDefaults = false;
        }
    }

    /// <summary>
    /// 拉取城市列表并同步填充现住址与户籍的城市选项（同一数据源，防止户籍城市 Picker 恒空）
    /// </summary>
    private async Task LoadCityOptionsAsync()
    {
        var citiesResult = await _regionService.GetCitiesAsync();
        _logger.Info($"城市数据加载结果: IsSuccess={citiesResult.IsSuccess}, Count={citiesResult.Value?.Count ?? 0}");

        if (citiesResult.IsSuccess && citiesResult.Value is { Count: > 0 })
        {
            CityOptions.Clear();
            HukouCityOptions.Clear();
            foreach (var city in citiesResult.Value)
            {
                CityOptions.Add(city.CityName);
                HukouCityOptions.Add(city.CityName);
            }
            _logger.Info($"CityOptions 加载完成: {CityOptions.Count} 个城市");
        }
        else if (citiesResult.IsFailure)
        {
            _logger.Warn($"城市数据加载失败: {citiesResult.Message}");
        }
    }

    /// <summary>
    /// 加载默认地区（基于当前用户单位）
    /// </summary>
    private async Task LoadDefaultRegionAsync()
    {
        try
        {
            // org 缺失/查询失败不再整体提前 return（原实现会连户籍级联预热一起跳过，
            // 导致户籍四个 Picker 全空）：降级为 null，走下方“取第一个可用区划”的兜底分支
            string? orgCity = null, orgCounty = null, orgTown = null, orgVillage = null;
            var orgId = App.CurrentUserOrganizationId;
            if (!orgId.HasValue || orgId.Value <= 0)
            {
                _logger.Warn("当前用户未配置组织单位，使用可用区划兜底默认地址");
            }
            else
            {
                var orgResult = await _organizationService.GetByIdAsync(orgId.Value);
                if (orgResult.IsSuccess && orgResult.Value != null)
                {
                    var org = orgResult.Value;
                    orgCity = org.CityName;
                    orgCounty = org.CountyName;
                    orgTown = org.TownName;
                    orgVillage = org.VillageName;
                    _logger.Info($"组织机构数据: CityName={org.CityName}, CountyName={org.CountyName}, TownName={org.TownName}, VillageName={org.VillageName}");
                }
                else
                {
                    _logger.Warn($"组织单位查询失败: OrgId={orgId.Value}，使用可用区划兜底默认地址");
                }
            }

            _isLoadingDefaults = true;

            // 1. 设置城市
            if (!string.IsNullOrEmpty(orgCity))
            {
                SelectedCity = orgCity;
                await LoadDistrictsAsync(orgCity);
            }
            else if (CityOptions.Count > 0)
            {
                // 如果组织机构没有城市信息，使用第一个可用城市
                SelectedCity = CityOptions[0];
                await LoadDistrictsAsync(SelectedCity);
            }

            // 2. 设置区县
            if (!string.IsNullOrEmpty(orgCounty))
            {
                SelectedDistrict = orgCounty;
                await LoadTownsForDistrictAsync(orgCounty);
            }
            else if (DistrictOptions.Count > 0)
            {
                // 如果组织机构没有区县信息，使用第一个可用区县
                SelectedDistrict = DistrictOptions[0];
                await LoadTownsForDistrictAsync(SelectedDistrict);
            }

            // 3. 设置乡镇
            if (!string.IsNullOrEmpty(orgTown))
            {
                SelectedTown = orgTown;
                await LoadVillagesForTownAsync(orgTown);
            }
            else if (TownOptions.Count > 0)
            {
                // 如果组织机构没有乡镇信息，使用第一个可用乡镇
                SelectedTown = TownOptions[0];
                await LoadVillagesForTownAsync(SelectedTown);
            }

            // 4. 设置村庄
            if (!string.IsNullOrEmpty(orgVillage))
            {
                SelectedVillage = orgVillage;
            }
            else if (VillageOptions.Count > 0)
            {
                // 如果组织机构没有村庄信息，使用第一个可用村庄
                SelectedVillage = VillageOptions[0];
            }

            // 5. 同步设置户籍地址（新建默认与现住址一致，之后各自独立）
            HukouProvince = DefaultValuesConstants.HOME_PROVINCE;
            HukouCity = SelectedCity;
            HukouDistrict = SelectedDistrict;
            HukouTown = SelectedTown;
            HukouVillage = SelectedVillage;

            // 6. 预热户籍级联选项与区划 ID（Picker 需要选项集合才能回显选中值）
            await LoadHukouDistrictsForCityAsync(HukouCity);
            await LoadHukouTownsForDistrictAsync(HukouDistrict);
            await LoadHukouVillagesForTownAsync(HukouTown);
            await LoadHukouTownIdByNameAsync(HukouCity, HukouTown);
            await LoadHukouVillageIdByNameAsync(HukouVillage);

            // 7. 更新户籍地址全文
            UpdateHukouAddress();

            _isLoadingDefaults = false;

            _logger.Info($"默认地区加载完成: {SelectedCity} {SelectedDistrict} {SelectedTown} {SelectedVillage}");
        }
        catch (Exception ex)
        {
            _isLoadingDefaults = false;
            _logger.Error($"加载默认地区失败: {ex.Message}");
        }
    }

    private async Task LoadSurveyorOrganizationAsync()
    {
        try
        {
            var orgId = App.CurrentUserOrganizationId;
            if (!orgId.HasValue) return;

            var orgResult = await _organizationService.GetByIdAsync(orgId.Value);
            if (orgResult.IsSuccess && orgResult.Value != null)
                SurveyorOrganization = orgResult.Value.Name ?? "";
        }
        catch (Exception ex)
        {
            _logger.Error($"加载调查员机构失败: {ex.Message}");
        }
    }

    public async Task LoadApplicationAsync(long applicationId)
    {
        _applicationId = applicationId;
        // 保留调用方设置的 OperationMode（Edit/View/Completion/Review），
        // 仅防御性兜底：Create 误入加载路径时改为 Edit
        if (OperationMode == FormOperationMode.Create)
            OperationMode = FormOperationMode.Edit;
        _isLoadingData = true;

        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("加载申请详情", ("ApplicationId", applicationId));

            var result = await _applicationService.GetByIdAsync(applicationId, ct);

            if (result.IsSuccess && result.Value != null)
            {
                var app = result.Value;
                // 向导入口固化复核前快照：Step5 判定与 SaveEconomicDetailsAsync 随后会覆写本档，
                // 此后读库不再是复核前状态（真旧值口径与 _loadedClassification 一致）
                _entryApplicationSnapshot = app;
                // 记录乐观并发令牌（加载时的 updated_at），保存时随实体传回做并发守卫
                _loadedUpdatedAt = app.UpdatedAt == default ? null : app.UpdatedAt;
                // 记录库中真实步骤：变更流程模式保存时回写，防止 UI 归位步数冲掉 current_step
                _loadedCurrentStep = app.CurrentStep;
                // 受理锚点（申请编号内嵌日期优先）：Step4「时间排斥」校验的窗口参照
                //（2026-10-16 起档案产出改用业务锚点=调查日，两者由周期差 {0,+1} 护栏保证同批次，
                // 见 ArchiveAnchorHelper.ResolveBusinessAnchorDate；草稿 = null → 校验按今天锚定）
                _applicationDateBase = ArchiveAnchorHelper.ResolveApplicationDate(app);
                // 覆写前真旧值：Step5 分类判定会覆写 classification_result/total_guarantee_amount，
                // 变更流程的新旧对比必须用此处捕获值，否则恒判"无变化"
                _loadedClassification = app.ClassificationResult;
                _loadedTotalGuaranteeAmount = app.TotalGuaranteeAmount;
                // 保障金变更登记/分类施保变更的口径旧值：户月保障金（不含分类施保与照料费）必须在
                // Step5 覆写前捕获，否则登记差额会把分类施保变动重复计入保障金
                _loadedHouseholdMonthlyGuaranteeAmount = app.HouseholdMonthlyGuaranteeAmount;
                _loadedClassifiedSubsidyAmount = app.ClassifiedSubsidyAmount;
                _loadedCaregiverSubsidyAmount = app.CaregiverSubsidyAmount;
                ApplicantName = app.ApplicantName;
                ApplicantIdCard = app.ApplicantIdCard;
                Gender = app.Gender ?? string.Empty;
                ApplicantPhone = app.ApplicantPhone ?? string.Empty;
                Address = app.Address ?? string.Empty;
                HukouType = HukouTypeOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.HukouType) ? DefaultValuesConstants.HUKOU_TYPE_KEY : app.HukouType));
                FamilySize = app.FamilySize;
                SelectedApplicationReasonObj = ApplicationReasonOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.ApplicationReason) ? DefaultValuesConstants.APPLICATION_REASON_KEY : app.ApplicationReason));
                ApplicationReasonDetail = app.ApplicationReasonDetail ?? string.Empty;

                // Step1 补充字段
                // 加载的户籍地址全文一律保护：此后任何级联变更都不再覆盖（防打开旧单改村冲掉户籍）
                _isHukouAddressManuallyEdited = true;
                HukouAddress = app.HukouAddress ?? string.Empty;
                BankName = app.BankName ?? string.Empty;
                BankAccount = app.BankAccount ?? string.Empty;

                // 级联加载地址选项（全部在 _isLoadingDefaults 保护内，防止 Setter 级联覆盖）
                // 注意：InitializeAsync → LoadDefaultRegionAsync 已从 org 加载了默认地址
                // 仅当 DB 有非空值时才覆盖默认值；空值不覆盖，保留 org 默认
                _isLoadingDefaults = true;
                try
                {
                    if (!string.IsNullOrEmpty(app.Province))
                        SelectedProvince = app.Province;

                    if (!string.IsNullOrEmpty(app.City))
                    {
                        SelectedCity = app.City;
                        await LoadDistrictsAsync(SelectedCity);
                        if (!string.IsNullOrEmpty(app.District))
                        {
                            SelectedDistrict = app.District;
                            await LoadTownsForDistrictAsync(SelectedDistrict);
                            if (!string.IsNullOrEmpty(app.Town))
                            {
                                SelectedTown = app.Town;
                                await LoadVillagesForTownAsync(SelectedTown);
                                if (!string.IsNullOrEmpty(app.Community))
                                    SelectedVillage = app.Community;
                                else if (VillageOptions.Count > 0)
                                    SelectedVillage = VillageOptions[0];
                            }
                        }
                    }

                    // 户籍级联回填：优先按已存区划 ID 解析名称并预热选项（名称+选项+ID 缓存）；
                    // 存量数据 ID 全空时按 hukou_address 全文做市→县→镇→村前缀解析。
                    // 全文已在此前置真保护（_isHukouAddressManuallyEdited），解析只回填级联不回写全文
                    if (app.HukouCityId.HasValue || app.HukouCountyId.HasValue || app.HukouTownId.HasValue || app.HukouVillageId.HasValue)
                    {
                        await LoadHukouCascadeFromIdsAsync(app.HukouCityId, app.HukouCountyId, app.HukouTownId, app.HukouVillageId);
                    }
                    else if (!string.IsNullOrWhiteSpace(app.HukouAddress))
                    {
                        await TryResolveHukouCascadeFromTextAsync(app.HukouAddress);
                    }
                }
                finally
                {
                    _isLoadingDefaults = false;
                }

                SelectedEthnicity = EthnicityOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.Ethnicity) ? DefaultValuesConstants.ETHNICITY_KEY : app.Ethnicity));
                SelectedMaritalStatus = MaritalStatusOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.MaritalStatus) ? DefaultValuesConstants.MARITAL_STATUS_KEY : app.MaritalStatus));
                SelectedEducationLevel = EducationLevelOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.EducationLevel) ? DefaultValuesConstants.EDUCATION_LEVEL_KEY : app.EducationLevel));
                SelectedPoliticalStatus = PoliticalStatusOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.PoliticalStatus) ? DefaultValuesConstants.POLITICAL_STATUS_KEY : app.PoliticalStatus));
                DisabilityCardNo = app.DisabilityCardNo ?? string.Empty;
                SelectedDiseaseCategoryObj = DiseaseCategoryOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.DiseaseName) ? DefaultValuesConstants.DISEASE_CATEGORY_KEY : app.DiseaseName));
                // 加载二级疾病选项并恢复选中状态
                if (!string.IsNullOrEmpty(app.DiseaseName))
                {
                    LoadSecondaryDiseases(app.DiseaseName);
                    if (!string.IsNullOrEmpty(app.SecondaryDiseaseName))
                    {
                        SelectedDiseaseNameObj = DiseaseNameOptions.FirstOrDefault(o => o.Key == app.SecondaryDiseaseName);
                    }
                }
                SelectedDisabilityTypeObj = DisabilityTypeOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.DisabilityType) ? "" : app.DisabilityType));
                SelectedDisabilityLevelObj = DisabilityLevelOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.DisabilityLevel) ? "" : app.DisabilityLevel));
                // 恢复残疾字段 key（UpdateHealthStatus 需要这些值才能正确推导）
                if (!string.IsNullOrEmpty(app.DisabilityType))
                    SelectedDisabilityTypeKey = app.DisabilityType;
                if (!string.IsNullOrEmpty(app.DisabilityLevel))
                    SelectedDisabilityLevelKey = app.DisabilityLevel;
                SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.HealthStatus) ? DefaultValuesConstants.HEALTH_STATUS_KEY : app.HealthStatus));
                // 健康状况是(疾病分类,残疾等级,年龄)的派生字段——在恢复所有依赖字段后重新推导，覆盖可能过时的 DB 值
                UpdateHealthStatus();

                // 恢复疾病编码 / 是否重病 / 二级疾病文本（重病勾选联动在 OnIsSevereDiseaseChanged 中生效）
                DiseaseCode = app.DiseaseCode ?? string.Empty;
                IsSevereDisease = app.IsSevereDisease;
                DiseaseNameText = app.SecondaryDiseaseName ?? string.Empty;

                // ── 第4步：验证映射结果，输出诊断日志 ──
                _logger.LogBusiness("Picker映射诊断",
                    ("HukouType", HukouType?.Key ?? "NULL"),
                    ("Gender", Gender ?? "NULL"),
                    ("Ethnicity", SelectedEthnicity?.Key ?? "NULL"),
                    ("MaritalStatus", SelectedMaritalStatus?.Key ?? "NULL"),
                    ("EducationLevel", SelectedEducationLevel?.Key ?? "NULL"),
                    ("PoliticalStatus", SelectedPoliticalStatus?.Key ?? "NULL"),
                    ("HealthStatus", SelectedHealthStatusObj?.Key ?? "NULL"),
                    ("DiseaseCategory", SelectedDiseaseCategoryObj?.Key ?? "NULL"),
                    ("DiseaseName", SelectedDiseaseNameObj?.Key ?? "NULL"),
                    ("DisabilityType", SelectedDisabilityTypeObj?.Key ?? "NULL"),
                    ("DisabilityLevel", SelectedDisabilityLevelObj?.Key ?? "NULL"),
                    ("ApplicationReason", SelectedApplicationReasonObj?.Key ?? "NULL"),
                    ("Province", SelectedProvince ?? "NULL"),
                    ("City", SelectedCity ?? "NULL"),
                    ("District", SelectedDistrict ?? "NULL"),
                    ("Town", SelectedTown ?? "NULL"),
                    ("Village", SelectedVillage ?? "NULL"),
                    ("Address", Address ?? "NULL"),
                    ("HukouAddress", HukouAddress ?? "NULL"),
                    ("DisabilityCardNo", DisabilityCardNo ?? "NULL"),
                    ("ApplicantName", ApplicantName ?? "NULL"),
                    ("ApplicantPhone", ApplicantPhone ?? "NULL"),
                    ("DB_Province", app.Province ?? "NULL"),
                    ("DB_City", app.City ?? "NULL"),
                    ("DB_District", app.District ?? "NULL"),
                    ("DB_Town", app.Town ?? "NULL"),
                    ("DB_Community", app.Community ?? "NULL"),
                    ("DB_Address", app.Address ?? "NULL"),
                    ("DB_HukouAddress", app.HukouAddress ?? "NULL"),
                    ("DB_DisabilityCardNo", app.DisabilityCardNo ?? "NULL"),
                    ("DB_DiseaseName", app.DiseaseName ?? "NULL"));

                // 经济信息
                WorkIncomeTotal = app.WorkIncomeTotal;
                BusinessIncomeTotal = app.BusinessIncomeTotal;
                PropertyIncomeTotal = app.PropertyIncomeTotal;
                TransferIncomeTotal = app.TransferIncomeTotal;
                OtherIncomeTotal = app.OtherIncomeTotal;
                AlimonyIncome = app.AlimonyIncome;
                TotalFamilyIncome = app.TotalFamilyIncome;
                PerCapitaIncome = app.PerCapitaIncome;
                TotalAnnualIncome = app.TotalAnnualIncome;
                PerCapitaAnnualIncome = app.PerCapitaAnnualIncome;
                RigidExpenditure = app.RigidExpenditure;

                // 土地信息
                FamilyLandArea = app.FamilyLandArea;
                SelfFarmedLandArea = app.SelfFarmedLandArea;
                SubleasedLandArea = app.SubleasedLandArea;
                ContractedLandArea = app.ContractedLandArea;
                LandIncomeTotal = app.LandIncomeTotal;
                SubsidyTotal = app.SubsidyTotal;

                // 分类结果
                ClassificationResult = app.ClassificationResult;
                ClassificationDescription = ClassificationConstants.ConvertFromCode(app.ClassificationResult ?? "");
                GuaranteeAmount = app.HouseholdMonthlyGuaranteeAmount;
                ClassifiedSubsidyAmount = app.ClassifiedSubsidyAmount;
                ClassifiedSubsidyType = app.ClassifiedSubsidyType ?? string.Empty;
                CaregiverSubsidyAmount = app.CaregiverSubsidyAmount;
                TotalGuaranteeAmount = app.TotalGuaranteeAmount;
                IsEligible = app.IsEligible;

                // 渐退期：主表列已迁移到独立表 nc_biz_grace_periods
                var graceRes = await _gracePeriodService.GetActiveAsync(applicationId, ct);
                if (graceRes.IsSuccess && graceRes.Value != null)
                {
                    IsInGracePeriod = true;
                    GracePeriodMonths = graceRes.Value.GracePeriodMonths ?? 0;
                    GracePeriodStartDate = graceRes.Value.StartDate;
                    GracePeriodEndDate = graceRes.Value.EndDate;
                    OriginalClassificationResult = graceRes.Value.OriginalClassification ?? string.Empty;
                    OriginalGuaranteeAmount = graceRes.Value.OriginalGuaranteeAmount;
                    GraceGrantAmount = graceRes.Value.GraceGrantAmount;
                    _gracePeriodEvaluated = true;
                }
                else
                {
                    IsInGracePeriod = false;
                    GracePeriodMonths = 0;
                    GracePeriodStartDate = null;
                    GracePeriodEndDate = null;
                    OriginalClassificationResult = string.Empty;
                    OriginalGuaranteeAmount = 0;
                    GraceGrantAmount = null;
                    _gracePeriodEvaluated = true;
                }

                // 变更链新建档案（户主死亡停旧建新等）：取上游档案原分类，
                // 供渐退期判定"低保→低收入"识别渐变前原分类（主表列已删，仅内存承载）
                _originalClassificationContext = null;
                _originalGuaranteeContext = null;
                _originalApplicationId = 0;
                _originalClassifiedContext = null;
                _originalHeadNameContext = null;
                if (app.OriginalApplicationId > 0)
                {
                    var oldAppRes = await _applicationService.GetByIdAsync(app.OriginalApplicationId, ct);
                    if (oldAppRes.IsSuccess && oldAppRes.Value != null)
                    {
                        _originalClassificationContext = oldAppRes.Value.ClassificationResult;
                        // 渐退封顶原额 = 上游"户月保障金"（不含分类施保；原取 TotalGuaranteeAmount 会把
                        // 分类施保并入户月，导致 313+208 被当作 521 户月续发——樊万河户实测错例）
                        _originalGuaranteeContext = oldAppRes.Value.HouseholdMonthlyGuaranteeAmount;
                        _originalApplicationId = app.OriginalApplicationId;
                        _originalClassifiedContext = oldAppRes.Value.ClassifiedSubsidyAmount;
                        _originalHeadNameContext = oldAppRes.Value.ApplicantName;
                    }
                }

                // 照料
                CaregiverType = app.CaregiverType ?? CaregiverTypeConstants.NONE;
                DestituteSupportType = app.DestituteSupportType ?? string.Empty;
                // 同步回填供养方式 Picker（预选默认值会造成"已选择"假象，须按数据匹配回显）
                SelectedSupportModeObj = SupportModeOptions.FirstOrDefault(o => o.Key == DestituteSupportType);

                // 单人保标记
                IsSingleRescueApplication = app.IsSingleRescue;
                _originalStatus = app.Status ?? ApplicationStatusCodes.DRAFT;
                ApplicationStatus = _originalStatus;
                OnPropertyChanged(nameof(IsEconomyEditable));

                // 表单不编辑、但保存时必须原样回写的主表字段（否则被实体默认值清零）
                _loadedSupportMode = app.SupportMode ?? string.Empty;
                _loadedSupportInstitutionId = app.SupportInstitutionId;
                _loadedConfirmedFamilySize = app.ConfirmedFamilySize;
                _loadedPersonCategoryProtectionTotalAmount = app.PersonCategoryProtectionTotalAmount;
                _loadedIsSpecialApproval = app.IsSpecialApproval;
                _loadedSpecialApprovalId = app.SpecialApprovalId;

                // 单人保申请自动设置分类已完成（分类结果已锁定）
                if (IsSingleRescueApplication && !string.IsNullOrEmpty(ClassificationResult))
                {
                    IsClassificationDone = true;
                }

                // 补全模式：建档时的认定结果作为只读事实锁定，不重新判定
                if (IsCompletionMode && !string.IsNullOrEmpty(ClassificationResult))
                {
                    IsClassificationDone = true;
                    OnPropertyChanged(nameof(IsClassificationLocked));
                }

                // 通知补全/复核模式相关属性刷新（OperationMode 变化后依赖属性需手动通知）
                OnPropertyChanged(nameof(IsCompletionMode));
                OnPropertyChanged(nameof(IsReviewMode));
                OnPropertyChanged(nameof(IsViewMode));
                OnPropertyChanged(nameof(IsEditable));
                OnPropertyChanged(nameof(IsSavable));
                OnPropertyChanged(nameof(IsNotCompletionMode));
                OnPropertyChanged(nameof(IsSaveDraftVisible));
                OnPropertyChanged(nameof(IsSubmitVisible));
                OnPropertyChanged(nameof(IsStep1Editable));
                OnPropertyChanged(nameof(IsStep2Editable));
                OnPropertyChanged(nameof(IsEconomyEditable));
                OnPropertyChanged(nameof(IsClassificationLocked));
                OnPropertyChanged(nameof(IsModeBannerVisible));
                OnPropertyChanged(nameof(ModeBannerText));
                OnPropertyChanged(nameof(IsFamilyCorrectionMode));
                OnPropertyChanged(nameof(IsEditFamilyInfoMode));
                OnPropertyChanged(nameof(IsMemberChangeMode));
                OnPropertyChanged(nameof(IsApplicantIdentityLocked));

                // 恢复当前步骤：补全模式 UI 归位到第5步（展示认定结果、提交按钮可见），
                // 复核模式 UI 归位到第3步（从经济步骤开始）；
                // 成员变更模式 UI 归位到第2步（从家庭成员开始）；
                // 家庭修正/编辑家庭信息模式 UI 归位到第1步（从户主信息开始）；数据库 current_step 保持原值由保存时写回
                CurrentStep = IsCompletionMode ? 5
                    : IsReviewMode ? 3
                    : IsMemberChangeMode ? 2
                    : (IsFamilyCorrectionMode || IsEditFamilyInfoMode) ? 1
                    : (app.CurrentStep > 0 ? app.CurrentStep : 1);

                // 加载家庭成员
                var membersResult = await _familyMemberService.GetByApplicationIdAsync(applicationId, ct);
                if (membersResult.IsFailure)
                {
                    _logger.Error($"家庭成员加载失败: {membersResult.Message}");
                    throw new BusinessException(ErrorCodes.DB_QUERY_ERROR, "家庭成员加载失败，已中止操作以避免保存时覆盖数据");
                }
                if (membersResult.Value?.Count > 0)
                {
                    FamilyMembers.Clear();
                    foreach (var member in membersResult.Value)
                    {
                        member.SetRegionService(_regionService);
                        await member.LoadInitialAddressOptionsAsync(
                            member.HomeCity, member.HomeDistrict, member.HomeTown);
                        SyncMemberDictOptions(member);
                        FamilyMembers.Add(member);
                    }
                    RefreshDerivedCollections();
                    _logger.LogBusiness("家庭成员加载完成", ("Count", FamilyMembers.Count));
                }

                // 成员变更模式的"变更前"快照：人数与成员名单（供 Before 快照与变更类型判定），
                // 必须在用户编辑成员之前捕获
                _loadedFamilySize = app.FamilySize;
                _loadedMemberEntities = membersResult.IsSuccess
                    ? membersResult.Value ?? new List<FamilyMember>()
                    : new List<FamilyMember>();
                _loadedMembersByIdCard = _loadedMemberEntities
                    .Where(m => !string.IsNullOrWhiteSpace(m.IdCard))
                    .GroupBy(m => m.IdCard.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Name ?? string.Empty, StringComparer.OrdinalIgnoreCase);

                // 身份类型快照：同一身份证的 member_category（归一化后比对）。
                // 历史数据存中文显示值（共同生活成员）、新数据存代码值（SharedLiving），两边都过 Normalize，
                // 否则每一户都会被误判成"身份类型变更"而错误地停旧建新。
                _loadedMemberCategoryByIdCard = _loadedMemberEntities
                    .Where(m => !string.IsNullOrWhiteSpace(m.IdCard))
                    .GroupBy(m => m.IdCard.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key,
                        g => MemberCategoryHelper.Normalize(
                            g.First().MemberCategory, g.First().IsApplicant, g.First().RelationshipToHead),
                        StringComparer.OrdinalIgnoreCase);

                _addedMemberReasons.Clear();
                _removedMemberEntries.Clear();

                // 加载赡养人
                var supporterService = _supporterService;
                var supportersResult = await supporterService.GetByApplicationIdAsync(applicationId, ct);
                if (supportersResult.IsFailure)
                {
                    _logger.Error($"赡养人加载失败: {supportersResult.Message}");
                    throw new BusinessException(ErrorCodes.DB_QUERY_ERROR, "赡养人加载失败，已中止操作以避免保存时覆盖数据");
                }
                if (supportersResult.Value?.Count > 0)
                {
                    Supporters.Clear();
                    foreach (var supporter in supportersResult.Value)
                    {
                        if (supporter.MonthlySupportFee <= 0 && supporter.AnnualSupportFee > 0)
                        {
                            supporter.MonthlySupportFee = Math.Round(supporter.AnnualSupportFee / 12, 2);
                            supporter.SupportMonths = 12;
                        }
                        supporter.SupporterFamilySize = supporter.SupporterFamilySize > 0
                            ? supporter.SupporterFamilySize
                            : 1;
                        SyncSupporterPickerOptions(supporter);
                        Supporters.Add(supporter);
                    }
                    _logger.LogBusiness("赡养人加载完成", ("Count", Supporters.Count));
                }

                // 加载照料人
                var caregiverService = _caregiverService;
                var caregiversResult = await caregiverService.GetByApplicationIdAsync(applicationId, ct);
                if (caregiversResult.IsFailure)
                {
                    _logger.Error($"照料人加载失败: {caregiversResult.Message}");
                    throw new BusinessException(ErrorCodes.DB_QUERY_ERROR, "照料人加载失败，已中止操作以避免保存时覆盖数据");
                }
                if (caregiversResult.Value?.Count > 0)
                {
                    Caregivers.Clear();
                    foreach (var caregiver in caregiversResult.Value)
                    {
                        caregiver.SelectedEthnicity = EthnicityOptions.FirstOrDefault(o => o.Key == caregiver.Ethnicity);
                        caregiver.SelectedMaritalStatus = MaritalStatusOptions.FirstOrDefault(o => o.Key == caregiver.MaritalStatus);
                        caregiver.SelectedEducationLevel = EducationLevelOptions.FirstOrDefault(o => o.Key == caregiver.EducationLevel);
                        caregiver.SelectedPoliticalStatus = PoliticalStatusOptions.FirstOrDefault(o => o.Key == caregiver.PoliticalStatus);
                        caregiver.SelectedHealthStatus = HealthStatusOptions.FirstOrDefault(o => o.Key == caregiver.HealthStatus);
                        caregiver.SelectedEmploymentStatus = EmploymentStatusOptions.FirstOrDefault(o => o.Key == caregiver.EmploymentStatus);
                        caregiver.SelectedIncomeSource = IncomeSourceOptions.FirstOrDefault(o => o.Key == caregiver.MainIncomeSource);
                        Caregivers.Add(caregiver);
                    }
                    _logger.LogBusiness("照料人加载完成", ("Count", Caregivers.Count));
                }

                // 加载入户调查
                var surveyService = _householdSurveyService;
                var surveyResult = await surveyService.GetByApplicationIdAsync(applicationId, ct);
                if (surveyResult.IsFailure)
                {
                    _logger.Error($"入户调查加载失败: {surveyResult.Message}");
                }
                if (surveyResult.Value != null)
                {
                    var survey = surveyResult.Value;
                    SurveyDate = survey.SurveyDate;
                    SurveyorName = survey.SurveyorName;
                    SurveyorOrganization = survey.SurveyorOrganization;
                    RespondentName = survey.RespondentName;
                    RespondentRelation = survey.RespondentRelation;
                    SurveyConclusion = survey.SurveyConclusion;
                    SurveyNotes = survey.SurveyNotes;
                    _logger.LogBusiness("入户调查加载完成");
                }

                // 数据补全模式（导入库建档）：入户调查日期统一取纳入时间（建档时间 created_at，即纳入月份首日）
                // 并锁定，防止人工改动；仅补全模式锁定，其他模式不干预（保持已存值可编辑）。
                if (IsCompletionMode)
                {
                    _lockedSurveyDate = app.CreatedAt.Date;
                    SurveyDate = _lockedSurveyDate;
                    IsSurveyDateLocked = true;
                    _logger.LogBusiness("补全模式入户调查日期锁定为纳入时间",
                        ("ApplicationId", applicationId),
                        ("Date", _lockedSurveyDate.Value.ToString("yyyy-MM-dd")));
                }
                else
                {
                    _lockedSurveyDate = null;
                    IsSurveyDateLocked = false;
                }

                // 入户调查日期加载快照：Step4「调查日期避公示」只拦新档或本次改过的值，
                // 存量档原值放行（历史档调查日可能本就落在当时公示窗口内）
                _loadedSurveyDate = SurveyDate;

                // 加载经济明细
                var economicResult = await _economicDetailService.LoadAllAsync(applicationId, ct);
                if (economicResult.IsSuccess)
                {
                    LaborIncomes.Clear();
                    BusinessIncomes.Clear();
                    PropertyIncomes.Clear();
                    TransferIncomes.Clear();
                    OtherIncomes.Clear();
                    Subsidies.Clear();
                    BreedingIncomes.Clear();
                    RigidExpenditures.Clear();

                    foreach (var item in economicResult.Value.LaborIncomes) LaborIncomes.Add(item);
                    foreach (var item in economicResult.Value.BusinessIncomes) BusinessIncomes.Add(item);
                    foreach (var item in economicResult.Value.PropertyIncomes) PropertyIncomes.Add(item);
                    foreach (var item in economicResult.Value.TransferIncomes) TransferIncomes.Add(item);
                    foreach (var item in economicResult.Value.OtherIncomes) OtherIncomes.Add(item);
                    foreach (var item in economicResult.Value.Subsidies) Subsidies.Add(item);
                    foreach (var item in economicResult.Value.BreedingIncomes) BreedingIncomes.Add(item);
                    foreach (var item in economicResult.Value.RigidExpenditures) RigidExpenditures.Add(item);

                    // 加载家庭财产
                    FamilyProperties.Clear();
                    foreach (var item in economicResult.Value.FamilyProperties) FamilyProperties.Add(item);

                    // 加载车辆
                    Vehicles.Clear();
                    foreach (var item in economicResult.Value.Vehicles) Vehicles.Add(item);

                    // 加载农机具
                    Machineries.Clear();
                    foreach (var item in economicResult.Value.Machineries) Machineries.Add(item);

                    // 加载金融资产
                    FinancialAssets.Clear();
                    foreach (var item in economicResult.Value.FinancialAssets) FinancialAssets.Add(item);
                    // Step3 金融资产区块绑定单对象：与集合首行同引用（改动即入集合，保存链路不变）
                    FinancialAsset = FinancialAssets.FirstOrDefault() ?? new FinancialAsset();

                    // 加载土地详情
                    LandRegistrations.Clear();
                    foreach (var item in economicResult.Value.LandRegistrations) LandRegistrations.Add(item);

                    // 加载土地确权归户表
                    LandConfirmationGroups.Clear();
                    foreach (var item in economicResult.Value.LandConfirmationGroups) LandConfirmationGroups.Add(item);

                    // 为归户表自动补充缺失的家庭成员人员
                    if (LandConfirmationGroups.Count > 0)
                    {
                        InitializePersonsForAllGroups();

                        // 为加载的归户表注册事件处理器
                        foreach (var group in LandConfirmationGroups)
                        {
                            group.Records.CollectionChanged += (s, e) => CalculateLandConfirmationArea();
                            group.Persons.CollectionChanged += (s, e) => CalculateLandConfirmationArea();
                        }

                        // 计算土地收入和面积
                        CalculateLandConfirmationArea();
                    }
                    else
                    {
                        RecalculateIncome();
                    }

                    _logger.LogBusiness("经济明细加载完成");
                }
                else
                {
                    _logger.Error($"加载经济明细失败: {economicResult.Message}");
                    await _serviceProvider.GetRequiredService<IDialogService>()
                        .DisplayAlertAsync("警告", $"加载经济明细失败: {economicResult.Message}", "确定");
                }

                _logger.LogBusiness("申请详情加载成功");
            }
            else
            {
                _logger.Error($"LoadApplication 失败: {result.Message}");
            }
        }, "加载申请详情...");

        _isLoadingData = false;

        // 数据加载完成后刷新 Picker 绑定
        ForceRefreshPickerBindings();

        // 刷新收入明细人员选项
        RefreshIncomeMemberOptions();

        // 恢复收入明细中各成员的 SelectedIncomeMember 引用（从 MemberName 反查匹配）
        RestoreIncomeMemberSelection();
    }


    /// <summary>
    /// 强制刷新所有 Picker 绑定（解决懒加载视图加入视觉树后 SelectedItem 不显示的问题）
    /// </summary>
    public void ForceRefreshPickerBindings()
    {
        OnPropertyChanged(nameof(SelectedProvince));
        OnPropertyChanged(nameof(SelectedCity));
        OnPropertyChanged(nameof(SelectedDistrict));
        OnPropertyChanged(nameof(SelectedTown));
        OnPropertyChanged(nameof(SelectedVillage));
        OnPropertyChanged(nameof(HukouCity));
        OnPropertyChanged(nameof(HukouDistrict));
        OnPropertyChanged(nameof(HukouTown));
        OnPropertyChanged(nameof(HukouVillage));
        OnPropertyChanged(nameof(SelectedEthnicity));
        OnPropertyChanged(nameof(SelectedMaritalStatus));
        OnPropertyChanged(nameof(HukouType));
        OnPropertyChanged(nameof(SelectedEducationLevel));
        OnPropertyChanged(nameof(SelectedPoliticalStatus));
        OnPropertyChanged(nameof(SelectedDiseaseCategoryObj));
        OnPropertyChanged(nameof(SelectedDiseaseNameObj));
    }


    /// <summary>
    /// 确保地区选项已加载（如有缺失则设置默认值）
    /// </summary>
    private async Task EnsureRegionOptionsLoadedAsync()
    {
        // 城市选项为空 = 首次加载失败或被竞态清空：进入步骤/校验前懒加载自愈（含户籍城市选项）
        if (CityOptions.Count == 0)
        {
            await LoadRegionDataAsync();
        }

        if (string.IsNullOrEmpty(SelectedCity) && CityOptions.Count > 0)
        {
            SelectedCity = CityOptions[0];
            await LoadDistrictsAsync(SelectedCity);
        }
        if (string.IsNullOrEmpty(SelectedDistrict) && DistrictOptions.Count > 0)
        {
            SelectedDistrict = DistrictOptions[0];
            await LoadTownsForDistrictAsync(SelectedDistrict);
        }
        if (string.IsNullOrEmpty(SelectedTown) && TownOptions.Count > 0)
        {
            SelectedTown = TownOptions[0];
            await LoadVillagesForTownAsync(SelectedTown);
        }
    }

    #region 省市区联动方法

    // 省市区镇级联加载共享取消源：新的级联触发会取消尚未完成的旧加载，避免乱序结果覆盖集合
    private CancellationTokenSource? _cascadeCts;

    private CancellationToken ResetCascadeToken()
    {
        _cascadeCts?.Cancel();
        _cascadeCts?.Dispose();
        _cascadeCts = new CancellationTokenSource();
        return _cascadeCts.Token;
    }

    /// <summary>户籍级联独立取消令牌（与现住址级联互不干扰，防止快速操作时互相取消）</summary>
    private CancellationTokenSource? _hukouCascadeCts;

    private CancellationToken ResetHukouCascadeToken()
    {
        _hukouCascadeCts?.Cancel();
        _hukouCascadeCts?.Dispose();
        _hukouCascadeCts = new CancellationTokenSource();
        return _hukouCascadeCts.Token;
    }

    partial void OnSelectedProvinceChanged(string value)
    {
        if (!string.IsNullOrEmpty(value) && !_isLoadingDefaults)
        {
            _ = LoadCitiesAsync(value, ResetCascadeToken());
        }
    }

    partial void OnSelectedCityChanged(string value)
    {
        if (!string.IsNullOrEmpty(value) && !_isLoadingDefaults)
        {
            // 户籍与现住址完全独立：改现住址不再触发户籍重拼
            _ = LoadDistrictsAsync(value, ResetCascadeToken());
        }
    }

    partial void OnSelectedDistrictChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            try
            {
                if (!_isLoadingDefaults)
                {
                    _ = LoadTownsForDistrictAsync(value, ResetCascadeToken());
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"区县变更处理失败: {ex.Message}");
            }
        }
    }

    partial void OnSelectedTownChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            try
            {
                if (!_isLoadingDefaults)
                {
                    _ = LoadVillagesForTownAsync(value, ResetCascadeToken());
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"乡镇变更处理失败: {ex.Message}");
            }
        }
    }

    #region 户籍级联（独立于现住址）

    partial void OnHukouCityChanged(string value)
    {
        if (_isLoadingDefaults || string.IsNullOrEmpty(value)) return;
        try
        {
            _ = LoadHukouDistrictsForCityAsync(value, ResetHukouCascadeToken());
            _ = LoadHukouTownIdByNameAsync(value);
            UpdateHukouAddress();
        }
        catch (Exception ex)
        {
            _logger.Error($"户籍城市变更处理失败: {ex.Message}");
        }
    }

    partial void OnHukouDistrictChanged(string value)
    {
        if (_isLoadingDefaults || string.IsNullOrEmpty(value)) return;
        try
        {
            _ = LoadHukouTownsForDistrictAsync(value, ResetHukouCascadeToken());
            UpdateHukouAddress();
        }
        catch (Exception ex)
        {
            _logger.Error($"户籍区县变更处理失败: {ex.Message}");
        }
    }

    partial void OnHukouTownChanged(string value)
    {
        if (_isLoadingDefaults || string.IsNullOrEmpty(value)) return;
        try
        {
            _ = LoadHukouVillagesForTownAsync(value, ResetHukouCascadeToken());
            _ = LoadHukouTownIdByNameAsync(HukouCity, value);
            UpdateHukouAddress();
        }
        catch (Exception ex)
        {
            _logger.Error($"户籍乡镇变更处理失败: {ex.Message}");
        }
    }

    partial void OnHukouVillageChanged(string value)
    {
        if (_isLoadingDefaults) return;
        try
        {
            _ = LoadHukouVillageIdByNameAsync(value);
            UpdateHukouAddress();
        }
        catch (Exception ex)
        {
            _logger.Error($"户籍村社区变更处理失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 手工编辑户籍地址全文：标记后级联重拼不再覆盖（加载数据不算手工编辑，由调用方显式置位）
    /// </summary>
    partial void OnHukouAddressChanged(string value)
    {
        if (!_isUpdatingHukouAddress)
        {
            _isHukouAddressManuallyEdited = true;
        }
    }

    /// <summary>
    /// 根据户籍城市加载户籍区县选项（独立于现住址）
    /// </summary>
    private async Task LoadHukouDistrictsForCityAsync(string city, CancellationToken ct = default)
    {
        HukouDistrictOptions.Clear();
        var countiesResult = await _regionService.GetCountiesByCityAsync(city, ct);
        if (ct.IsCancellationRequested) return;

        if (countiesResult.IsSuccess && countiesResult.Value != null)
        {
            foreach (var county in countiesResult.Value)
                HukouDistrictOptions.Add(county.CountyName);
        }
        else if (countiesResult.IsFailure)
        {
            _logger.Warn($"户籍区县选项加载失败: {countiesResult.Message}");
        }
    }

    /// <summary>
    /// 根据户籍区县加载户籍乡镇选项（独立于现住址）
    /// </summary>
    private async Task LoadHukouTownsForDistrictAsync(string district, CancellationToken ct = default)
    {
        HukouTownOptions.Clear();
        var countiesResult = await _regionService.GetCountiesAsync(ct);
        if (ct.IsCancellationRequested) return;

        if (countiesResult.IsSuccess && countiesResult.Value != null)
        {
            var county = countiesResult.Value.FirstOrDefault(c => c.CountyName == district);
            if (county != null)
            {
                _hukouCountyId = county.Id;
                var townsResult = await _regionService.GetTownsByCountyIdAsync(county.Id, ct);
                if (ct.IsCancellationRequested) return;

                if (townsResult.IsSuccess && townsResult.Value != null)
                {
                    foreach (var town in townsResult.Value)
                        HukouTownOptions.Add(town.TownName);
                }
            }
        }
        else if (countiesResult.IsFailure)
        {
            _logger.Warn($"户籍乡镇选项加载失败: {countiesResult.Message}");
        }
    }

    /// <summary>
    /// 根据户籍乡镇加载户籍村社区选项（独立于现住址）
    /// </summary>
    private async Task LoadHukouVillagesForTownAsync(string town, CancellationToken ct = default)
    {
        HukouVillageOptions.Clear();

        // 优先用已缓存的县区 ID 定位乡镇；否则按名称全量检索
        int? townId = null;
        if (_hukouCountyId.HasValue)
        {
            var townsResult = await _regionService.GetTownsByCountyIdAsync(_hukouCountyId.Value, ct);
            if (ct.IsCancellationRequested) return;
            if (townsResult.IsSuccess && townsResult.Value != null)
            {
                townId = townsResult.Value.FirstOrDefault(t => t.TownName == town)?.Id;
            }
        }

        if (!townId.HasValue)
        {
            var searchResult = await _regionService.SearchTownsAsync(town, ct);
            if (ct.IsCancellationRequested) return;
            if (searchResult.IsSuccess && searchResult.Value != null)
            {
                townId = searchResult.Value.FirstOrDefault(t => t.TownName == town)?.Id;
            }
        }

        if (!townId.HasValue) return;

        _hukouTownId = townId;
        var villagesResult = await _regionService.GetVillagesByTownIdAsync(townId.Value, ct);
        if (ct.IsCancellationRequested) return;

        if (villagesResult.IsSuccess && villagesResult.Value != null)
        {
            foreach (var village in villagesResult.Value)
                HukouVillageOptions.Add(village.VillageName);
        }
        else if (villagesResult.IsFailure)
        {
            _logger.Warn($"户籍村社区选项加载失败: {villagesResult.Message}");
        }
    }

    /// <summary>户籍区县 ID 缓存（乡镇选项定位用）</summary>
    private int? _hukouCountyId;

    /// <summary>
    /// 按已存区划 ID 回填户籍级联（名称+选项集合+ID 缓存），供编辑/查看模式恢复用。
    /// 必须在 _isLoadingDefaults=true 期间调用，防止 Setter 触发级联重拼。
    /// </summary>
    private async Task LoadHukouCascadeFromIdsAsync(int? cityId, int? countyId, int? townId, int? villageId)
    {
        if (cityId.HasValue)
        {
            var cityResult = await _regionService.GetCityByIdAsync(cityId.Value);
            if (cityResult.IsSuccess && cityResult.Value != null)
            {
                HukouCity = cityResult.Value.CityName;
                _hukouCityId = cityId;
                await LoadHukouDistrictsForCityAsync(HukouCity);
            }
            else if (cityResult.IsFailure)
            {
                _logger.Warn($"户籍城市-ID回填失败: CityId={cityId}, {cityResult.Message}");
            }
        }

        if (countyId.HasValue)
        {
            var countyResult = await _regionService.GetCountyByIdAsync(countyId.Value);
            if (countyResult.IsSuccess && countyResult.Value != null)
            {
                HukouDistrict = countyResult.Value.CountyName;
                _hukouCountyId = countyId;
                await LoadHukouTownsForDistrictAsync(HukouDistrict);
            }
            else if (countyResult.IsFailure)
            {
                _logger.Warn($"户籍区县-ID回填失败: CountyId={countyId}, {countyResult.Message}");
            }
        }

        if (townId.HasValue)
        {
            var townResult = await _regionService.GetTownByIdAsync(townId.Value);
            if (townResult.IsSuccess && townResult.Value != null)
            {
                HukouTown = townResult.Value.TownName;
                _hukouTownId = townId;
                await LoadHukouVillagesForTownAsync(HukouTown);
            }
            else if (townResult.IsFailure)
            {
                _logger.Warn($"户籍乡镇-ID回填失败: TownId={townId}, {townResult.Message}");
            }
        }

        if (villageId.HasValue)
        {
            var villageResult = await _regionService.GetVillageByIdAsync(villageId.Value);
            if (villageResult.IsSuccess && villageResult.Value != null)
            {
                HukouVillage = villageResult.Value.VillageName;
                _hukouVillageId = villageId;
            }
            else if (villageResult.IsFailure)
            {
                _logger.Warn($"户籍村社区-ID回填失败: VillageId={villageId}, {villageResult.Message}");
            }
        }
    }

    /// <summary>
    /// 按名称解析户籍城市/乡镇 ID 并缓存（保存时写库用）
    /// </summary>
    private async Task LoadHukouTownIdByNameAsync(string? city, string? town = null)
    {
        try
        {
            if (!string.IsNullOrEmpty(city))
            {
                var cityResult = await _regionService.GetCityByNameAsync(city);
                if (cityResult.IsSuccess && cityResult.Value != null)
                    _hukouCityId = cityResult.Value.Id;
            }

            if (!string.IsNullOrEmpty(town) && _hukouCountyId.HasValue)
            {
                var townsResult = await _regionService.GetTownsByCountyIdAsync(_hukouCountyId.Value);
                if (townsResult.IsSuccess && townsResult.Value != null)
                    _hukouTownId = townsResult.Value.FirstOrDefault(t => t.TownName == town)?.Id;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"户籍区划ID解析失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 按名称解析户籍村社区 ID 并缓存（保存时写库用）
    /// </summary>
    private async Task LoadHukouVillageIdByNameAsync(string? village)
    {
        try
        {
            if (string.IsNullOrEmpty(village) || !_hukouTownId.HasValue)
            {
                _hukouVillageId = null;
                return;
            }

            var villagesResult = await _regionService.GetVillagesByTownIdAsync(_hukouTownId.Value);
            if (villagesResult.IsSuccess && villagesResult.Value != null)
                _hukouVillageId = villagesResult.Value.FirstOrDefault(v => v.VillageName == village)?.Id;
        }
        catch (Exception ex)
        {
            _logger.Warn($"户籍村社区ID解析失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 存量数据兜底：hukou_address 全文按 市→县→镇→村 逐级最长前缀匹配，回填级联名称、选项集合与区划 ID。
    /// 必须在 _isLoadingDefaults=true 期间调用；调用方已置真 _isHukouAddressManuallyEdited，全文不会被回写。
    /// 任一级匹配失败即停止（保留已匹配部分并记 Warn），绝不猜测未匹配层级。
    /// </summary>
    private async Task TryResolveHukouCascadeFromTextAsync(string hukouAddress)
    {
        try
        {
            var address = hukouAddress.Trim();
            if (address.Length == 0) return;

            // 市（最长前缀匹配，防同前缀城市歧义）
            var citiesResult = await _regionService.GetCitiesAsync();
            var city = citiesResult.Value?
                .Where(c => !string.IsNullOrEmpty(c.CityName) && address.StartsWith(c.CityName, StringComparison.Ordinal))
                .OrderByDescending(c => c.CityName.Length)
                .FirstOrDefault();
            if (city == null)
            {
                _logger.Warn($"户籍文本解析: 未匹配到地级市: {address}");
                return;
            }

            HukouCity = city.CityName;
            _hukouCityId = city.Id;
            await LoadHukouDistrictsForCityAsync(city.CityName);

            var rest = address[city.CityName.Length..];

            // 县（限定在已匹配城市下）
            var countiesResult = await _regionService.GetCountiesByCityAsync(city.CityName);
            var county = countiesResult.Value?
                .Where(c => !string.IsNullOrEmpty(c.CountyName) && rest.StartsWith(c.CountyName, StringComparison.Ordinal))
                .OrderByDescending(c => c.CountyName.Length)
                .FirstOrDefault();
            if (county == null)
            {
                _logger.Warn($"户籍文本解析: 未匹配到区县: {rest}");
                return;
            }

            HukouDistrict = county.CountyName;
            // 该方法内部按名称回填 _hukouCountyId 并加载乡镇选项
            await LoadHukouTownsForDistrictAsync(county.CountyName);
            rest = rest[county.CountyName.Length..];

            // 镇（限定在已匹配县区下）
            var townsResult = await _regionService.GetTownsByCountyIdAsync(_hukouCountyId ?? county.Id);
            var town = townsResult.Value?
                .Where(t => !string.IsNullOrEmpty(t.TownName) && rest.StartsWith(t.TownName, StringComparison.Ordinal))
                .OrderByDescending(t => t.TownName.Length)
                .FirstOrDefault();
            if (town == null)
            {
                _logger.Warn($"户籍文本解析: 未匹配到乡镇: {rest}");
                return;
            }

            HukouTown = town.TownName;
            // 该方法内部按名称回填 _hukouTownId 并加载村选项
            await LoadHukouVillagesForTownAsync(town.TownName);
            rest = rest[town.TownName.Length..];

            // 村（限定在已匹配乡镇下，取最长前缀）
            var villagesResult = await _regionService.GetVillagesByTownIdAsync(_hukouTownId ?? town.Id);
            var village = villagesResult.Value?
                .Where(v => !string.IsNullOrEmpty(v.VillageName) && rest.StartsWith(v.VillageName, StringComparison.Ordinal))
                .OrderByDescending(v => v.VillageName.Length)
                .FirstOrDefault();
            if (village != null)
            {
                HukouVillage = village.VillageName;
                _hukouVillageId = village.Id;
            }
            else
            {
                _logger.Warn($"户籍文本解析: 未匹配到村社区: {rest}");
            }

            _logger.Info($"户籍文本解析完成: {HukouCity}/{HukouDistrict}/{HukouTown}/{HukouVillage}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"户籍文本解析失败: {ex.Message}");
        }
    }

    #endregion

    /// <summary>
    /// 更新户籍地址全文（省市区县乡镇村按户籍级联拼接）。
    /// 用户手工编辑过或从库加载的记录不再覆盖（_isHukouAddressManuallyEdited 保护）。
    /// </summary>
    private void UpdateHukouAddress()
    {
        if (_isHukouAddressManuallyEdited) return;

        var parts = new[] { HukouProvince, HukouCity, HukouDistrict, HukouTown, HukouVillage }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var composed = string.Concat(parts);

        _isUpdatingHukouAddress = true;
        try
        {
            HukouAddress = composed;
        }
        finally
        {
            _isUpdatingHukouAddress = false;
        }
    }

    private async Task LoadCitiesAsync(string province, CancellationToken cancellationToken = default)
    {
        try
        {
            // 治本：城市选项必须来自地级市列表。原实现用省名查 GetCountiesByCityAsync
            //（city_name='黑龙江省' 必空），返回后 Clear 掉刚加载的 CityOptions——
            // 这正是“小概率申请人所在地城市读不进”的根因竞态
            var citiesResult = await _regionService.GetCitiesAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;

            if (citiesResult.IsSuccess && citiesResult.Value is { Count: > 0 })
            {
                CityOptions.Clear();
                HukouCityOptions.Clear();
                foreach (var city in citiesResult.Value)
                {
                    CityOptions.Add(city.CityName);
                    HukouCityOptions.Add(city.CityName);
                }
                if (CityOptions.Count > 0 && string.IsNullOrEmpty(SelectedCity))
                {
                    SelectedCity = CityOptions[0];
                }
            }
            // 空结果不清空已有选项（保留既有数据，杜绝清空竞态）
        }
        catch (OperationCanceledException)
        {
            // 级联切换取消旧加载，静默处理
        }
        catch (Exception ex)
        {
            _logger.Error($"加载城市数据失败: {ex.Message}");
        }
    }

    private async Task LoadDistrictsAsync(string city, CancellationToken cancellationToken = default)
    {
        try
        {
            var countiesResult = await _regionService.GetCountiesByCityAsync(city, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;

            if (countiesResult.IsSuccess)
            {
                DistrictOptions.Clear();
                foreach (var county in countiesResult.Value)
                {
                    DistrictOptions.Add(county.CountyName);
                }
                if (DistrictOptions.Count > 0 && string.IsNullOrEmpty(SelectedDistrict))
                {
                    SelectedDistrict = DistrictOptions[0];
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 级联切换取消旧加载，静默处理
        }
        catch (Exception ex)
        {
            _logger.Error($"加载区县数据失败: {ex.Message}");
        }
    }

    private async Task LoadTownsForDistrictAsync(string districtName, CancellationToken cancellationToken = default)
    {
        try
        {
            var countiesResult = await _regionService.GetCountiesByCityAsync(SelectedCity, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;

            if (countiesResult.IsSuccess)
            {
                var county = countiesResult.Value.FirstOrDefault(c => c.CountyName == districtName);
                if (county != null)
                {
                    var townsResult = await _regionService.GetTownsByCountyIdAsync(county.Id, cancellationToken);
                    if (cancellationToken.IsCancellationRequested) return;

                    TownOptions.Clear();
                    _townIdMap.Clear();
                    if (townsResult.IsSuccess)
                    {
                        foreach (var town in townsResult.Value)
                        {
                            TownOptions.Add(town.TownName);
                            _townIdMap[town.TownName] = town.Id;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 级联切换取消旧加载，静默处理
        }
        catch (Exception ex)
        {
            _logger.Error($"加载乡镇数据失败: {ex.Message}");
        }
    }

    private async Task LoadVillagesForTownAsync(string townName, CancellationToken cancellationToken = default)
    {
        try
        {
            int townId;
            if (_townIdMap.TryGetValue(townName, out var mappedId))
            {
                townId = mappedId;
                _logger.Info($"乡镇精确匹配: {townName} → Id={townId}");
            }
            else
            {
                _logger.Warn($"乡镇名 '{townName}' 不在映射中，尝试模糊搜索");
                var townsResult = await _regionService.SearchTownsAsync(townName, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;

                if (!townsResult.IsSuccess || !townsResult.Value.Any())
                {
                    _logger.Warn($"乡镇 '{townName}' 搜索无结果，清空村庄列表");
                    VillageOptions.Clear();
                    return;
                }
                townId = townsResult.Value.First().Id;
                _logger.Info($"乡镇模糊匹配: {townName} → Id={townId}");
            }

            var villagesResult = await _regionService.GetVillagesByTownIdAsync(townId, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;

            VillageOptions.Clear();
            if (villagesResult.IsSuccess)
            {
                foreach (var village in villagesResult.Value)
                {
                    VillageOptions.Add(village.VillageName);
                }

                if (!_isLoadingDefaults && VillageOptions.Count > 0)
                {
                    SelectedVillage = VillageOptions[0];
                }

                _logger.Info($"加载村庄完成: 乡镇={townName}, 村庄数={VillageOptions.Count}");
            }
        }
        catch (OperationCanceledException)
        {
            // 级联切换取消旧加载，静默处理
        }
        catch (Exception ex)
        {
            _logger.Error($"加载村庄数据失败: {ex.Message}");
        }
    }

    #endregion

    #region 自动识别方法

    partial void OnApplicantIdCardChanged(string value)
    {
        value = value?.Trim() ?? string.Empty;
        ApplicantIdCardField.Value = value;

        if (!string.IsNullOrEmpty(value) && value.Length >= 17)
        {
            var genderDigit = value[16] - '0';
            DetectedGender = genderDigit % 2 == 1 ? "男" : "女";
            Gender = DetectedGender;

            DetectedAge = Helpers.IdCardValidator.ExtractAgeBasic(value);
        }
        else
        {
            DetectedGender = string.Empty;
            Gender = string.Empty;
            DetectedAge = null;
        }

        UpdateDetectedGenderDisplay();
        UpdateHealthStatus();
    }

    partial void OnApplicantNameChanged(string value)
    {
        ApplicantNameField.Value = value;
    }

    partial void OnApplicantPhoneChanged(string value)
    {
        PhoneField.Value = value;
    }

    partial void OnDetectedAgeChanged(int? value)
    {
        DetectedAgeDisplay = value.HasValue ? $"{value.Value}岁" : string.Empty;
        UpdateDetectedGenderDisplay();
    }

    private void UpdateDetectedGenderDisplay()
    {
        if (string.IsNullOrEmpty(DetectedGender))
        {
            DetectedGenderDisplay = string.Empty;
        }
        else
        {
            var agePart = string.IsNullOrEmpty(DetectedAgeDisplay) ? string.Empty : $" {DetectedAgeDisplay}";
            DetectedGenderDisplay = $"{DetectedGender}{agePart}";
        }
    }

    partial void OnDisabilityCardNoChanged(string value)
    {
        DisabilityCardNoField.Value = value;

        try
        {
            if (string.IsNullOrWhiteSpace(value) || value == "00")
            {
                SelectedDisabilityTypeObj = null;
                SelectedDisabilityLevelObj = null;
                SelectedDisabilityTypeKey = string.Empty;
                SelectedDisabilityLevelKey = string.Empty;
                UpdateHealthStatus();
                return;
            }

            if (value.Length >= 2)
            {
                var typeKey = _disabilityTypeKeyMap.TryGetValue(value[^2], out var tk) ? tk : string.Empty;
                var levelKey = _disabilityLevelKeyMap.TryGetValue(value[^1], out var lk) ? lk : string.Empty;
                var levelDisplay = _disabilityLevelDisplayMap.TryGetValue(levelKey, out var lv) ? lv : levelKey;

                SelectedDisabilityTypeObj = DisabilityTypeOptions.FirstOrDefault(o => o.Key == typeKey);
                SelectedDisabilityLevelObj = DisabilityLevelOptions.FirstOrDefault(o => o.Key == levelKey);
                SelectedDisabilityTypeKey = typeKey;
                SelectedDisabilityLevelKey = levelKey;
            }

            UpdateHealthStatus();
        }
        catch (Exception ex)
        {
            _logger.Error($"残疾证号变更处理失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 残疾等级显示值变化时同步更新 key
    /// </summary>
    partial void OnSelectedDisabilityLevelObjChanged(DictItemOption? value)
    {
        var key = value?.Key ?? "";
        SelectedDisabilityLevelKey = key;
    }

    /// <summary>
    /// 更新身体状况（使用 key 判断，不硬编码）
    /// </summary>
    private void UpdateHealthStatus()
    {
        try
        {
            bool hasDisease = !string.IsNullOrEmpty(SelectedDiseaseCategoryKey) &&
                              SelectedDiseaseCategoryKey != _noDiseaseKey;

            bool hasDisability = !string.IsNullOrEmpty(SelectedDisabilityTypeKey);

            bool hasSevereDisability = !string.IsNullOrEmpty(SelectedDisabilityLevelKey) &&
                                        _severeLevelKeys.Contains(SelectedDisabilityLevelKey);

            bool hasNonSevereDisability = hasDisability && !hasSevereDisability;

            var age = Helpers.IdCardValidator.ExtractAgeBasic(ApplicantIdCard);

            if (hasDisease && hasSevereDisability)
                SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == HealthStatusConstants.SEVERE_DISEASE_AND_DISABILITY);
            else if (hasDisease)
                SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == HealthStatusConstants.SEVERE_DISEASE);
            else if (hasSevereDisability)
                SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == HealthStatusConstants.SEVERE_DISABILITY);
            else if (hasNonSevereDisability)
                SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == HealthStatusConstants.FAIR_OR_WEAK);
            else if (age.HasValue && age.Value >= ClassificationConstants.ELDERLY_AGE)
                SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == HealthStatusConstants.FAIR_OR_WEAK);
            else
                SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == HealthStatusConstants.HEALTHY);

            // 人工判定"是否重病"优先：勾选时推导结果不低于"重病"
            if (IsSevereDisease &&
                (SelectedHealthStatusObj?.Key == HealthStatusConstants.HEALTHY ||
                 SelectedHealthStatusObj?.Key == HealthStatusConstants.FAIR_OR_WEAK))
            {
                SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == HealthStatusConstants.SEVERE_DISEASE);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"更新身体状况失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 勾选"是否重病"同步申请人健康状况为重病；取消勾选仅在当前为重病时回退为健康。
    /// </summary>
    partial void OnIsSevereDiseaseChanged(bool value)
    {
        if (value)
            SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == HealthStatusConstants.SEVERE_DISEASE);
        else if (SelectedHealthStatusObj?.Key == HealthStatusConstants.SEVERE_DISEASE)
            SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == HealthStatusConstants.HEALTHY);
    }

    /// <summary>
    /// 申请人疾病编码命中 ICD-10 字典：回填二级疾病文本、按章节自动归类一级疾病分类。
    /// 由 Step1 code-behind 的 TextChanged 即时调用（WinUI3 绑定默认失焦才提交源）。
    /// </summary>
    public void ApplyApplicantDiseaseCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        if (!Helpers.Icd10Catalog.TryGetName(code, out var name)) return;

        DiseaseNameText = name;

        var category = Helpers.Icd10Catalog.MapCategory(code);
        var match = DiseaseCategoryOptions.FirstOrDefault(o => o.Key == category);
        if (match != null && SelectedDiseaseCategoryObj?.Key != match.Key)
            SelectedDiseaseCategoryObj = match; // 触发级联加载 + UpdateHealthStatus
    }

    #endregion

    #region 疾病分类联动方法

    partial void OnSelectedDiseaseCategoryObjChanged(DictItemOption? value)
    {
        try
        {
            var key = value?.Key ?? "";
            SelectedDiseaseCategoryKey = key;
            if (!string.IsNullOrEmpty(key) && key != _noDiseaseKey)
            {
                LoadSecondaryDiseases(key);
            }
            else
            {
                DiseaseNameOptions.Clear();
                SelectedDiseaseNameObj = null;
            }
            UpdateHealthStatus();
        }
        catch (Exception ex)
        {
            _logger.Error($"疾病分类变更处理失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 从 DiseaseNames 字典加载二级分类
    /// </summary>
    private void LoadSecondaryDiseases(string categoryKey)
    {
        try
        {
            DiseaseNameOptions.Clear();
            if (_diseaseCategoryMap.TryGetValue(categoryKey, out var diseases))
            {
                foreach (var disease in diseases.OrderBy(x => x.SortOrder))
                {
                    // 使用 Key（疾病名称）作为 Display
                    DiseaseNameOptions.Add(new DictItemOption { Key = disease.Key, Display = disease.Key });
                }
                if (DiseaseNameOptions.Count > 0)
                {
                    SelectedDiseaseNameObj = DiseaseNameOptions[0];
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"加载二级疾病数据失败: {ex.Message}");
        }
    }

    #endregion

    #region 实时搜索方法

    [RelayCommand]
    private async Task SearchDiseaseCategoryAsync(string keyword)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                await LoadDictionaryAsync("DiseaseCategories", DiseaseCategoryOptions);
                return;
            }

            var result = await _dictionaryService.GetItemsByCategoryAsync("DiseaseCategories");
            if (result.IsSuccess)
            {
                DiseaseCategoryOptions.Clear();
                foreach (var item in result.Value.Where(x => x.ItemValue.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                {
                    DiseaseCategoryOptions.Add(new DictItemOption { Key = item.ItemKey, Display = item.ItemValue });
                }
            }
            else
            {
                _logger.Warn($"搜索疾病分类失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"搜索疾病分类失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private Task SearchDiseaseNameAsync(string keyword)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                LoadSecondaryDiseases(SelectedDiseaseCategoryKey);
                return Task.CompletedTask;
            }

            DiseaseNameOptions.Clear();
            if (_diseaseCategoryMap.TryGetValue(SelectedDiseaseCategoryKey, out var diseases))
            {
                foreach (var disease in diseases
                    .Where(x => x.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.SortOrder))
                {
                    DiseaseNameOptions.Add(new DictItemOption { Key = disease.Key, Display = disease.Key });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"搜索疾病名称失败: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    #endregion

    #region 身份证号搜索

    /// <summary>
    /// 根据身份证号跨表搜索人员信息
    /// </summary>
    [RelayCommand]
    private async Task SearchByIdCardAsync()
    {
        ApplicantIdCard = ApplicantIdCard?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(ApplicantIdCard) || ApplicantIdCard.Length != 18)
        {
            _logger.Warn("身份证号格式不正确，无法搜索");
            return;
        }

        try
        {
            IsBusy = true;
            LoadingMessage = "正在查询档案信息...";

            _logger.Info($"开始身份证号查询: {DataMasker.MaskIdCard(ApplicantIdCard)}");

            var result = await _personSearchService.SearchByIdCardAsync(ApplicantIdCard);

            if (result.IsFailure)
            {
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("查询失败", result.Message ?? "查询过程中出现错误", "确定");
                return;
            }

            if (result.Value == null || result.Value.Count == 0)
            {
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", $"未找到身份证号 {DataMasker.MaskIdCard(ApplicantIdCard)} 的相关信息", "确定");
                return;
            }

            SearchResults.Clear();
            foreach (var item in result.Value)
                SearchResults.Add(item);

            if (result.Value.Count == 1)
                SelectedSearchResult = result.Value[0];

            IsSearchPopupVisible = true;
            IsBusy = false;
        }
        catch (Exception ex)
        {
            _logger.Error($"身份证号查询失败: {ex.Message}");
            if (_serviceProvider.GetService<IDialogService>() is { } ds)
                await ds.DisplayAlertAsync("错误", $"查询失败: {ex.Message}", "确定");
        }
        finally
        {
            if (!IsSearchPopupVisible)
                IsBusy = false;
        }
    }

    /// <summary>
    /// 确认选择搜索结果
    /// </summary>
    [RelayCommand]
    private async Task ConfirmSearchResultAsync()
    {
        _logger.Info("[SEARCH_POPUP] ConfirmSearchResultAsync 被调用");
        if (SelectedSearchResult != null)
        {
            await FillFormWithSearchResultAsync(SelectedSearchResult);
            IsSearchPopupVisible = false;
        }
    }

    /// <summary>
    /// 取消选择搜索结果
    /// </summary>
    [RelayCommand]
    private void CancelSearchResult()
    {
        _logger.Info("[SEARCH_POPUP] CancelSearchResult 被调用");
        IsSearchPopupVisible = false;
        SelectedSearchResult = null;
    }

    /// <summary>
    /// 填充表单（从人员库检索）
    /// </summary>
    private async Task FillFormWithSearchResultAsync(PersonSearchResult item)
    {
        ApplicantName = item.Name ?? string.Empty;
        ApplicantPhone = item.Phone ?? string.Empty;

        if (!string.IsNullOrEmpty(item.IdCard))
        {
            ApplicantIdCard = item.IdCard;
            DetectedAge = Helpers.IdCardValidator.ExtractAgeBasic(item.IdCard);
        }

        // 加载城市/区县/乡镇/村
        if (!string.IsNullOrEmpty(item.City) || !string.IsNullOrEmpty(item.District))
        {
            await LoadRegionDataForSearchAsync(item.City, item.District, item.Town, item.Village);
        }

        // 导入家庭成员
        if (item.FamilyMembers.Count > 0)
        {
            FamilyMembers.Clear();
            foreach (var member in item.FamilyMembers)
            {
                member.ApplicationId = _applicationId;

                // 设置地址默认值（如果为空）
                if (string.IsNullOrEmpty(member.HomeCity))
                    member.HomeCity = SelectedCity ?? string.Empty;
                if (string.IsNullOrEmpty(member.HomeDistrict))
                    member.HomeDistrict = SelectedDistrict ?? string.Empty;
                if (string.IsNullOrEmpty(member.HomeTown))
                    member.HomeTown = SelectedTown ?? string.Empty;
                if (string.IsNullOrEmpty(member.HomeVillage))
                    member.HomeVillage = SelectedVillage ?? string.Empty;
                if (string.IsNullOrEmpty(member.HomeAddress))
                    member.HomeAddress = Address ?? string.Empty;
                if (string.IsNullOrEmpty(member.HomeProvince))
                    member.HomeProvince = DefaultValuesConstants.HOME_PROVINCE;

                // 设置字典默认值（如果为空）
                if (string.IsNullOrEmpty(member.Ethnicity))
                    member.Ethnicity = EthnicityOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.ETHNICITY_KEY)?.Key
                        ?? (EthnicityOptions.Count > 0 ? EthnicityOptions[0].Key : string.Empty);
                if (string.IsNullOrEmpty(member.MaritalStatus))
                    member.MaritalStatus = MaritalStatusOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.MARITAL_STATUS_KEY)?.Key
                        ?? (MaritalStatusOptions.Count > 0 ? MaritalStatusOptions[0].Key : string.Empty);
                if (string.IsNullOrEmpty(member.EducationLevel))
                    member.EducationLevel = EducationLevelOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.EDUCATION_LEVEL_KEY)?.Key
                        ?? (EducationLevelOptions.Count > 0 ? EducationLevelOptions[0].Key : string.Empty);
                if (string.IsNullOrEmpty(member.PoliticalStatus))
                    member.PoliticalStatus = PoliticalStatusOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.POLITICAL_STATUS_KEY)?.Key
                        ?? (PoliticalStatusOptions.Count > 0 ? PoliticalStatusOptions[0].Key : string.Empty);

                // 从身份证推导性别和年龄
                if (string.IsNullOrEmpty(member.Gender) && !string.IsNullOrEmpty(member.IdCard))
                    member.Gender = Helpers.IdCardValidator.ExtractGender(member.IdCard) ?? string.Empty;
                if (member.Age <= 0 && !string.IsNullOrEmpty(member.IdCard))
                    member.Age = Helpers.IdCardValidator.ExtractAgeBasic(member.IdCard);

                member.SetRegionService(_regionService);
                await member.LoadInitialAddressOptionsAsync(
                    member.HomeCity, member.HomeDistrict, member.HomeTown);
                SyncMemberDictOptions(member);
                FamilyMembers.Add(member);
            }
            RefreshDerivedCollections();
            UpdateFamilySize();
        }

        _logger.Info($"身份证号查询成功: {DataMasker.MaskIdCard(ApplicantIdCard)}, 来源: {item.SourceDisplay}, 城市: {item.City} {item.District}");
    }

    /// <summary>
    /// 为身份证号搜索结果加载地区数据
    /// </summary>
    private async Task LoadRegionDataForSearchAsync(string? city, string? district, string? town, string? village)
    {
        try
        {
            _isLoadingDefaults = true;

            if (!string.IsNullOrEmpty(city) && CityOptions.Contains(city))
            {
                SelectedCity = city;
                await LoadDistrictsAsync(city);
            }

            if (!string.IsNullOrEmpty(district) && DistrictOptions.Contains(district))
            {
                SelectedDistrict = district;
                await LoadTownsForDistrictAsync(district);
            }

            if (!string.IsNullOrEmpty(town) && TownOptions.Contains(town))
            {
                SelectedTown = town;
                await LoadVillagesForTownAsync(town);
            }

            if (!string.IsNullOrEmpty(village) && VillageOptions.Contains(village))
            {
                SelectedVillage = village;
            }

            _isLoadingDefaults = false;
        }
        catch (Exception ex)
        {
            _isLoadingDefaults = false;
            _logger.Error($"搜索结果地区加载失败: {ex.Message}");
        }
    }

    #endregion

    #region 表单验证方法

    /// <summary>
    /// 验证表单字段
    /// </summary>
    public bool ValidateFormFields()
    {
        var isValid = true;

        isValid &= ApplicantNameField.Validate();
        isValid &= ApplicantIdCardField.Validate();
        isValid &= DisabilityCardNoField.Validate();
        isValid &= PhoneField.Validate();
        isValid &= HukouAddressField.Validate();

        return isValid;
    }

    #endregion
}
