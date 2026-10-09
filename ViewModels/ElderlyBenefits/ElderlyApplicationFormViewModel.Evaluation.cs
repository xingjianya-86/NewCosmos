using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Navigation;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.System;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace NewCosmos.ViewModels.ElderlyBenefits;

public partial class ElderlyApplicationFormViewModel
{
    public async Task InitializeAsync()
    {
        // 关系选项与操作模式无关，Create/Edit 均需（复核前置补全为 Edit 模式）
        LoadRelationOptions();

        if (OperationMode == FormOperationMode.Create)
        {
            ApplyDate = DateTime.Today;
            _id = 0;
            await LoadInitialRegionAsync();
            await ApplyOperatorRegionDefaultsAsync();
        }
    }

    /// <summary>
    /// 新建模式：按当前登录用户所在组织对应的区划预选户籍/家庭住址默认值
    /// （户籍与家庭住址默认一致，均可手动修改；组织未配置区划时保持原默认第一个城市）
    /// </summary>
    private async Task ApplyOperatorRegionDefaultsAsync()
    {
        if (App.CurrentUserId == null) return;

        var userResult = await _userService.GetByIdAsync(App.CurrentUserId.Value, CancellationToken);
        if (userResult.IsFailure || userResult.Value == null || !userResult.Value.OrganizationId.HasValue)
            return;

        var orgResult = await _organizationService.GetByIdAsync(userResult.Value.OrganizationId.Value, CancellationToken);
        if (orgResult.IsFailure || orgResult.Value == null)
            return;

        var org = orgResult.Value;

        // 城市
        var cityName = string.Empty;
        if (org.CityId.HasValue)
        {
            var city = await _regionService.GetCityByIdAsync(org.CityId.Value, CancellationToken);
            if (city.IsSuccess && city.Value != null && HukouCityOptions.Contains(city.Value.CityName))
                cityName = city.Value.CityName;
        }
        if (string.IsNullOrEmpty(cityName)) return;

        // 设置默认值期间抑制 OnChanged 异步级联，手动顺序 await，避免并行竞态覆盖选项
        _suppressRegionCascade = true;
        try
        {
            // 户籍（逐级设置并等待级联加载完成，避免异步乱序覆盖）
            SelectedHukouCity = cityName;
            await LoadHukouCountiesAsync();
            if (org.CountyId.HasValue && HukouCountyOptions.Contains(org.CountyName))
            {
                SelectedHukouCounty = org.CountyName;
                await LoadHukouTownsAsync();
                if (org.TownId.HasValue && HukouTownOptions.Contains(org.TownName))
                {
                    SelectedHukouTown = org.TownName;
                    await LoadHukouVillagesAsync();
                    if (org.VillageId.HasValue && HukouVillageOptions.Contains(org.VillageName))
                        SelectedHukouVillage = org.VillageName;
                }
            }

            // 家庭住址跟随户籍（默认一致，可改）
            SelectedFamilyCity = cityName;
            await LoadFamilyCountiesAsync();
            if (org.CountyId.HasValue && FamilyCountyOptions.Contains(org.CountyName))
            {
                SelectedFamilyCounty = org.CountyName;
                await LoadFamilyTownsAsync();
                if (org.TownId.HasValue && FamilyTownOptions.Contains(org.TownName))
                {
                    SelectedFamilyTown = org.TownName;
                    await LoadFamilyVillagesAsync();
                    if (org.VillageId.HasValue && FamilyVillageOptions.Contains(org.VillageName))
                        SelectedFamilyVillage = org.VillageName;
                }
            }
        }
        finally
        {
            _suppressRegionCascade = false;
        }
    }

    /// <summary>
    /// 加载已有登记（编辑/查看模式）
    /// </summary>
    public async Task LoadAsync(long id)
    {
        _id = id;
        _suppressEvaluate = true;
        await ExecuteAsync(async () =>
        {
            var result = await _applicationService.GetByIdAsync(id, CancellationToken);
            if (result.IsFailure || result.Value == null)
                return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "登记记录不存在");

            var app = result.Value;
            Name = app.Name;
            IdCard = app.IdCard;
            Gender = app.Gender;
            BirthDate = app.BirthDate;
            Age = app.Age;
            Phone = app.Phone;
            HukouAddress = app.HukouAddress;
            HukouVillage = app.HukouVillage;
            HukouDetailAddress = app.HukouDetailAddress;
            FamilyAddress = app.FamilyAddress;
            DetailAddress = app.DetailAddress;
            BankName = app.BankName;
            BankAccount = app.BankAccount;
            AgentName = app.AgentName;
            AgentRelation = app.AgentRelation;
            AgentReceiveName = app.AgentReceiveName;
            AgentReceiveRelation = app.AgentReceiveRelation;
            AgentReceiveBankName = app.AgentReceiveBankName;
            AgentReceiveBankAccount = app.AgentReceiveBankAccount;
            AgentReceiveReason = app.AgentReceiveReason;
            // 档案存量关系值为历史自由文本（如"子女"），不在字典选项时追加保证 Picker 可见
            EnsureRelationOptionVisible(AgentRelation, AgentReceiveRelation);
            Category = app.Category;
            CategoryName = app.CategoryName;
            IdentityFlag = app.IdentityFlag;
            IdentitySource = app.IdentitySource;
            MonthlyAmount = string.IsNullOrEmpty(app.Category) ? 0m : await GetCategoryMonthlyAmountAsync(app.Category, CancellationToken);
            IssueStartMonth = app.IssueStartMonth;
            IssueAmount = app.IssueAmount;
            PaybackStartMonth = app.PaybackStartMonth;
            PaybackEndMonth = app.PaybackEndMonth;
            AutoStartMonth = app.AutoStartMonth;
            AutoEndMonth = app.AutoEndMonth;
            PaybackMonths = app.PaybackMonths;
            PaybackAmount = app.PaybackAmount;
            PaybackReason = app.PaybackReason;
            ApplyPaybackReasonSelection(app.PaybackReason);
            IsSpecialCase = app.IsSpecialCase;
            SpecialReason = app.SpecialReason;
            ApplyDate = app.ApplyDate ?? DateTime.Today;

            // 记录导入库来源ID：数据补全（从导入库建档补全信息）保存后删除导入库记录
            _importedHistoryId = string.Equals(app.SourceType, "ElderlySubsidyHistory", StringComparison.OrdinalIgnoreCase)
                ? app.SourceId
                : null;

            HasEvaluateResult = true;
            EvaluateMessage = $"已匹配到：{CategoryName}";
            RefreshCategoryItems(Category);
            OnPropertyChanged(nameof(PaybackStartDisplay));
            OnPropertyChanged(nameof(PaybackEndDisplay));
            OnPropertyChanged(nameof(IssueStartMonthDisplay));
            OnPropertyChanged(nameof(PaybackAmountDisplay));
            OnPropertyChanged(nameof(IssueAmountDisplay));
            OnPropertyChanged(nameof(MonthlyAmountDisplay));

            // 回填补发分段明细（编辑/查看草稿时保留分段，避免保存时被清空）
            var segmentsResult = await _applicationService.GetSegmentsAsync(id, CancellationToken);
            if (segmentsResult.IsSuccess && segmentsResult.Value != null)
            {
                Segments.Clear();
                foreach (var segment in segmentsResult.Value)
                {
                    Segments.Add(segment);
                }
            }

            await LoadRegionCascadesForEditAsync(app);

            return Result.Success();
        }, "加载登记信息...");
        _suppressEvaluate = false;
    }

    /// <summary>
    /// 编辑/查看模式：按已存地区 id 逐级回显户籍与家庭级联
    /// </summary>
    private async Task LoadRegionCascadesForEditAsync(Models.Entities.ElderlyApplication app)
    {
        await LoadHukouCitiesAsync();
        await LoadFamilyCitiesAsync();

        if (app.HukouCityId.HasValue)
        {
            var city = await _regionService.GetCityByIdAsync(app.HukouCityId.Value, CancellationToken);
            if (city.IsSuccess && city.Value != null && HukouCityOptions.Contains(city.Value.CityName))
            {
                SelectedHukouCity = city.Value.CityName;
                await LoadHukouCountiesAsync();
                if (app.HukouCountyId.HasValue)
                {
                    var counties = await _regionService.GetCountiesByCityAsync(SelectedHukouCity, CancellationToken);
                    var county = counties.IsSuccess ? counties.Value?.FirstOrDefault(c => c.Id == app.HukouCountyId) : null;
                    if (county != null)
                    {
                        SelectedHukouCounty = county.CountyName;
                        await LoadHukouTownsAsync();
                        if (app.HukouTownId.HasValue)
                        {
                            var towns = await _regionService.GetTownsByCountyIdAsync(app.HukouCountyId.Value, CancellationToken);
                            var town = towns.IsSuccess ? towns.Value?.FirstOrDefault(t => t.Id == app.HukouTownId) : null;
                            if (town != null)
                            {
                                SelectedHukouTown = town.TownName;
                                await LoadHukouVillagesAsync();
                                if (app.HukouVillageId.HasValue)
                                {
                                    var villages = await _regionService.GetVillagesByTownIdAsync(app.HukouTownId.Value, CancellationToken);
                                    var village = villages.IsSuccess ? villages.Value?.FirstOrDefault(v => v.Id == app.HukouVillageId) : null;
                                    if (village != null) SelectedHukouVillage = village.VillageName;
                                }
                            }
                        }
                    }
                }
            }
        }

        if (app.FamilyCityId.HasValue)
        {
            var city = await _regionService.GetCityByIdAsync(app.FamilyCityId.Value, CancellationToken);
            if (city.IsSuccess && city.Value != null && FamilyCityOptions.Contains(city.Value.CityName))
            {
                SelectedFamilyCity = city.Value.CityName;
                await LoadFamilyCountiesAsync();
                if (app.FamilyCountyId.HasValue)
                {
                    var counties = await _regionService.GetCountiesByCityAsync(SelectedFamilyCity, CancellationToken);
                    var county = counties.IsSuccess ? counties.Value?.FirstOrDefault(c => c.Id == app.FamilyCountyId) : null;
                    if (county != null)
                    {
                        SelectedFamilyCounty = county.CountyName;
                        await LoadFamilyTownsAsync();
                        if (app.FamilyTownId.HasValue)
                        {
                            var towns = await _regionService.GetTownsByCountyIdAsync(app.FamilyCountyId.Value, CancellationToken);
                            var town = towns.IsSuccess ? towns.Value?.FirstOrDefault(t => t.Id == app.FamilyTownId) : null;
                            if (town != null)
                            {
                                SelectedFamilyTown = town.TownName;
                                await LoadFamilyVillagesAsync();
                                if (app.FamilyVillageId.HasValue)
                                {
                                    var villages = await _regionService.GetVillagesByTownIdAsync(app.FamilyTownId.Value, CancellationToken);
                                    var village = villages.IsSuccess ? villages.Value?.FirstOrDefault(v => v.Id == app.FamilyVillageId) : null;
                                    if (village != null) SelectedFamilyVillage = village.VillageName;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 新建模式：初始化城市选项
    /// </summary>
    public async Task LoadInitialRegionAsync()
    {
        await LoadHukouCitiesAsync();
        await LoadFamilyCitiesAsync();
    }

    /// <summary>
    /// 身份证联动：判类 + 补发分段计算
    /// </summary>
    private async Task EvaluateAsync()
    {
        if (string.IsNullOrWhiteSpace(IdCard)) return;

        // 身份证三要素本地解析：与享受门槛无关，先回填（未达80岁也应显示性别/出生日期/年龄）。
        // 评估失败只清类别/金额/补发，不再连带清空三要素（曾因门槛拦截把已解析的性别/年龄一并清掉）。
        var idCardTrimmed = IdCard.Trim();
        var parsedBirth = IdCardValidator.ExtractBirthDate(idCardTrimmed);
        if (parsedBirth != null)
        {
            Gender = IdCardValidator.ExtractGender(idCardTrimmed) ?? string.Empty;
            BirthDate = parsedBirth;
            var today = DateTime.Today;
            var parsedAge = today.Year - parsedBirth.Value.Year;
            if (parsedBirth.Value.Date > today.AddYears(-parsedAge)) parsedAge--;
            Age = parsedAge;
        }
        else
        {
            Gender = string.Empty;
            BirthDate = null;
            Age = null;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _applicationService.EvaluateAsync(IdCard, ApplyDate, CancellationToken);
            if (result.IsFailure)
            {
                ResetEvaluateResult();
                EvaluateMessage = result.Message ?? "评估失败";
                return result;
            }

            var eval = result.Value;
            Gender = eval.Gender;
            BirthDate = eval.BirthDate;
            Age = eval.Age;
            Category = eval.Category;
            CategoryName = eval.CategoryName;
            IdentityFlag = eval.IdentityFlag;
            IdentitySource = eval.IdentitySource;
            MonthlyAmount = eval.MonthlyAmount;
            IssueStartMonth = eval.IssueStartMonth;
            IssueAmount = eval.IssueAmount;
            ApplyEvalPayback(eval.Payback);
            RefreshCategoryItems(eval.Category);
            OnPropertyChanged(nameof(PaybackStartDisplay));
            OnPropertyChanged(nameof(PaybackEndDisplay));
            OnPropertyChanged(nameof(IssueStartMonthDisplay));
            OnPropertyChanged(nameof(PaybackAmountDisplay));
            OnPropertyChanged(nameof(IssueAmountDisplay));
            OnPropertyChanged(nameof(MonthlyAmountDisplay));

            HasEvaluateResult = true;
            EvaluateMessage = $"当前年龄 {eval.Age} 周岁，享受类别：{eval.CategoryName}，月标准 {eval.MonthlyAmount:F2} 元/月";
            if (!string.IsNullOrEmpty(eval.IdentitySource))
                EvaluateMessage += $"；已匹配导入库身份（{eval.IdentityFlag}）";

            // 输入身份证后从导入台账自动带出人员信息（直接填充，可改）
            var imported = await _applicationService.GetImportedByIdCardAsync(IdCard, CancellationToken);
            if (imported.IsSuccess && imported.Value != null)
            {
                Name = imported.Value.Name ?? string.Empty;
                Phone = imported.Value.Phone ?? string.Empty;
                BankAccount = imported.Value.BankAccount ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(imported.Value.Address))
                {
                    HukouAddress = imported.Value.Address;
                    HukouDetailAddress = imported.Value.Address;
                }
                EvaluateMessage += $"；已从导入台账自动填入[{imported.Value.Name}]基本信息";
                EvaluateMessage += $"；导入台账{imported.Value.DataYear}年有{imported.Value.SubsidyAmount:F2}元/月发放记录，历史月份若已发放，请勾选「特殊情况处理」调整或清空补发起止月";
            }

            if (!IsSpecialCase && eval.Payback.TotalMonths == 0)
                EvaluateMessage += "；本例无补发";

            _logger.LogBusiness("普惠高龄身份证评估",
                ("Age", eval.Age), ("Category", eval.Category), ("PaybackAmount", eval.Payback.TotalAmount));
            return result;
        }, "评估享受类别与补发...");
    }

    private void ApplyEvalPayback(ElderlyPaybackResult payback)
    {
        // 自动值始终留档：取消勾选特殊情况时据此恢复
        AutoStartMonth = payback.StartMonth;
        AutoEndMonth = payback.EndMonth;

        if (IsSpecialCase)
        {
            // 特殊情况：保留人工已调整/清空的起止月，仅按其同步分段（重新判定不覆盖人工修改）
            SafeFireAndForget(() => RecalculateFromMonthsAsync());
            return;
        }

        PaybackStartMonth = payback.StartMonth;
        PaybackEndMonth = payback.EndMonth;
        PaybackMonths = payback.TotalMonths;
        PaybackAmount = payback.TotalAmount;

        Segments.Clear();
        foreach (var segment in payback.Segments)
        {
            Segments.Add(segment);
        }
    }

    /// <summary>
    /// 刷新享受类别展示项（只读判定结果：命中 ☑ 高亮，未命中 □）
    /// </summary>
    private void RefreshCategoryItems(string category)
    {
        CategoryItems.Clear();
        var codes = new[]
        {
            ElderlyBenefitConstants.CatLowSubsidy,
            ElderlyBenefitConstants.CatOtherElderly,
            ElderlyBenefitConstants.Cat90To99,
            ElderlyBenefitConstants.Cat100Plus
        };
        foreach (var code in codes)
        {
            CategoryItems.Add(new CategoryDisplayItem(
                ElderlyBenefitConstants.GetCategoryName(code),
                code == category));
        }
    }

    private void ResetEvaluateResult()
    {
        HasEvaluateResult = false;
        // 性别/出生日期/年龄由 EvaluateAsync 开头的身份证本地解析管理，不随评估失败清空
        Category = string.Empty;
        CategoryName = string.Empty;
        IdentityFlag = string.Empty;
        IdentitySource = string.Empty;
        MonthlyAmount = 0;
        IssueStartMonth = string.Empty;
        IssueAmount = 0;
        PaybackStartMonth = string.Empty;
        PaybackEndMonth = string.Empty;
        AutoStartMonth = string.Empty;
        AutoEndMonth = string.Empty;
        PaybackMonths = 0;
        PaybackAmount = 0;
        Segments.Clear();
    }

    [RelayCommand]
    private async Task RecalculateAsync()
    {
        if (string.IsNullOrWhiteSpace(IdCard)) return;
        await ExecuteAsync(async () =>
        {
            var result = await _applicationService.EvaluateAsync(IdCard, ApplyDate, CancellationToken);
            if (result.IsFailure)
                return result;

            var eval = result.Value;
            Gender = eval.Gender;
            BirthDate = eval.BirthDate;
            Age = eval.Age;
            Category = eval.Category;
            CategoryName = eval.CategoryName;
            IdentityFlag = eval.IdentityFlag;
            IdentitySource = eval.IdentitySource;
            MonthlyAmount = eval.MonthlyAmount;
            IssueStartMonth = eval.IssueStartMonth;
            IssueAmount = eval.IssueAmount;
            ApplyEvalPayback(eval.Payback);
            HasEvaluateResult = true;
            EvaluateMessage = $"当前年龄 {eval.Age} 周岁，享受类别：{eval.CategoryName}，月标准 {eval.MonthlyAmount:F2} 元/月";
            if (!IsSpecialCase && eval.Payback.TotalMonths == 0)
                EvaluateMessage += "；本例无补发";
            return result;
        }, "重新评估...");
    }

    /// <summary>
    /// 判定按钮：手动触发判类 + 补发计算（身份证自动评估之外的手动判定入口）
    /// </summary>
    [RelayCommand]
    private async Task Evaluate()
    {
        if (string.IsNullOrWhiteSpace(IdCard))
        {
            await _dialogService.DisplayAlertAsync("提示", "请先填写申请人身份证号码", "确定");
            return;
        }

        await EvaluateAsync();
    }

    /// <summary>
    /// 按当前补发起止月同步补发月数/金额/分段（特殊情况手动模式、输入联动与取消勾选恢复共用）。
    /// 起止月都留空 = 明确无补发（归零）；单边填写/格式错误/起止倒置 = 归零并由 Validate 拦截保存，绝不保留评估旧值。
    /// </summary>
    private async Task RecalculateFromMonthsAsync()
    {
        var startOk = TryParseMonth(PaybackStartMonth, out var start);
        var endOk = TryParseMonth(PaybackEndMonth, out var end);
        if (BirthDate == null || !startOk || !endOk || start > end)
        {
            ClearPaybackCalculation();
            return;
        }

        // 手动起算月可能与自动 calc 不同（calc 内部取 max(政策,满80)）；此处强制使用用户输入的起止月
        // 循环外预取各档标准（委托内纯内存查表，禁止逐段 sync-over-async 死锁 UI 线程）
        var standardMap = await LoadStandardMapAsync();
        var manualSegments = BuildSegmentsFromRange(start, end, IdentityFlag, standardMap);
        PaybackMonths = manualSegments.Sum(s => s.Months);
        PaybackAmount = manualSegments.Sum(s => s.SegmentAmount);
        Segments.Clear();
        foreach (var segment in manualSegments)
        {
            Segments.Add(segment);
        }
        NotifyPaybackChanged();
    }

    /// <summary>补发归零：月数/金额/分段全部清空（起止月都留空=无补发，或非法输入不落旧值）</summary>
    private void ClearPaybackCalculation()
    {
        PaybackMonths = 0;
        PaybackAmount = 0;
        Segments.Clear();
        NotifyPaybackChanged();
    }

    /// <summary>补发月数/金额/分段变化后刷新派生显示属性</summary>
    private void NotifyPaybackChanged()
    {
        OnPropertyChanged(nameof(PaybackStartDisplay));
        OnPropertyChanged(nameof(PaybackEndDisplay));
        OnPropertyChanged(nameof(PaybackAmountDisplay));
    }

    /// <summary>
    /// 预取各年龄档月标准（一次性 await，供分段计算内存查表）
    /// </summary>
    private async Task<IReadOnlyDictionary<string, decimal>> LoadStandardMapAsync()
    {
        var map = new Dictionary<string, decimal>();
        foreach (var cat in new[]
                 {
                     ElderlyBenefitConstants.CatOtherElderly,
                     ElderlyBenefitConstants.CatLowSubsidy,
                     ElderlyBenefitConstants.Cat90To99,
                     ElderlyBenefitConstants.Cat100Plus
                 })
        {
            var r = await _applicationService.GetMonthlyAmountAsync(cat, CancellationToken);
            map[cat] = r.IsSuccess ? r.Value : 0m;
        }
        return map;
    }

    /// <summary>
    /// 按指定起止月逐月分段计算（特殊情况手动范围用）
    /// </summary>
    private List<ElderlyPaybackSegment> BuildSegmentsFromRange(DateTime start, DateTime end, string identityFlag, IReadOnlyDictionary<string, decimal> standardMap)
    {
        var segments = new List<ElderlyPaybackSegment>();
        ElderlyPaybackSegment? current = null;
        var cursor = start;
        var birth = BirthDate!.Value;

        while (cursor <= end)
        {
            var age = ElderlyPaybackCalculator.GetAgeAtMonth(birth, cursor);
            var cat = ElderlyPaybackCalculator.GetCategoryForAge(age, identityFlag);
            var amount = standardMap.GetValueOrDefault(cat);

            if (current == null || current.CategoryCode != cat || current.MonthlyAmount != amount)
            {
                current = new ElderlyPaybackSegment
                {
                    CategoryCode = cat,
                    MonthlyAmount = amount,
                    SegmentStartMonth = cursor.ToString("yyyy-MM")
                };
                segments.Add(current);
            }

            current.SegmentEndMonth = cursor.ToString("yyyy-MM");
            current.Months++;
            current.SegmentAmount = current.MonthlyAmount * current.Months;
            cursor = cursor.AddMonths(1);
        }

        return segments;
    }

    /// <summary>
    /// 解析选中的地区名称到 id，并拼接地址文本（户籍 + 家庭）
    /// </summary>
    private async Task ResolveRegionIdsAsync(Models.Entities.ElderlyApplication app)
    {
        // 户籍
        if (!string.IsNullOrEmpty(SelectedHukouCity))
        {
            var cities = await _regionService.GetCitiesAsync(CancellationToken);
            var city = cities.IsSuccess ? cities.Value?.FirstOrDefault(c => c.CityName == SelectedHukouCity) : null;
            if (city != null)
            {
                app.HukouCityId = city.Id;
                var counties = await _regionService.GetCountiesByCityAsync(SelectedHukouCity, CancellationToken);
                var county = counties.IsSuccess && !string.IsNullOrEmpty(SelectedHukouCounty)
                    ? counties.Value?.FirstOrDefault(c => c.CountyName == SelectedHukouCounty) : null;
                if (county != null)
                {
                    app.HukouCountyId = county.Id;
                    var towns = await _regionService.GetTownsByCountyIdAsync(county.Id, CancellationToken);
                    var town = towns.IsSuccess && !string.IsNullOrEmpty(SelectedHukouTown)
                        ? towns.Value?.FirstOrDefault(t => t.TownName == SelectedHukouTown) : null;
                    if (town != null)
                    {
                        app.HukouTownId = town.Id;
                        var villages = await _regionService.GetVillagesByTownIdAsync(town.Id, CancellationToken);
                        var village = villages.IsSuccess && !string.IsNullOrEmpty(SelectedHukouVillage)
                            ? villages.Value?.FirstOrDefault(v => v.VillageName == SelectedHukouVillage) : null;
                        if (village != null) app.HukouVillageId = village.Id;
                    }
                }
            }
            app.HukouAddress = $"{SelectedHukouCity}{SelectedHukouCounty}{SelectedHukouTown}{SelectedHukouVillage}".Trim();
            app.HukouVillage = SelectedHukouVillage;
        }

        // 家庭
        if (!string.IsNullOrEmpty(SelectedFamilyCity))
        {
            var cities = await _regionService.GetCitiesAsync(CancellationToken);
            var city = cities.IsSuccess ? cities.Value?.FirstOrDefault(c => c.CityName == SelectedFamilyCity) : null;
            if (city != null)
            {
                app.FamilyCityId = city.Id;
                var counties = await _regionService.GetCountiesByCityAsync(SelectedFamilyCity, CancellationToken);
                var county = counties.IsSuccess && !string.IsNullOrEmpty(SelectedFamilyCounty)
                    ? counties.Value?.FirstOrDefault(c => c.CountyName == SelectedFamilyCounty) : null;
                if (county != null)
                {
                    app.FamilyCountyId = county.Id;
                    var towns = await _regionService.GetTownsByCountyIdAsync(county.Id, CancellationToken);
                    var town = towns.IsSuccess && !string.IsNullOrEmpty(SelectedFamilyTown)
                        ? towns.Value?.FirstOrDefault(t => t.TownName == SelectedFamilyTown) : null;
                    if (town != null)
                    {
                        app.FamilyTownId = town.Id;
                        var villages = await _regionService.GetVillagesByTownIdAsync(town.Id, CancellationToken);
                        var village = villages.IsSuccess && !string.IsNullOrEmpty(SelectedFamilyVillage)
                            ? villages.Value?.FirstOrDefault(v => v.VillageName == SelectedFamilyVillage) : null;
                        if (village != null) app.FamilyVillageId = village.Id;
                    }
                }
            }
            app.FamilyAddress = $"{SelectedFamilyCity}{SelectedFamilyCounty}{SelectedFamilyTown}{SelectedFamilyVillage}{DetailAddress}".Trim();
        }
    }

    private ElderlyApplication BuildApplication()
    {
        return new ElderlyApplication
        {
            Id = _id,
            Name = Name?.Trim() ?? string.Empty,
            IdCard = IdCard?.Trim() ?? string.Empty,
            Gender = Gender ?? string.Empty,
            BirthDate = BirthDate,
            Phone = Phone?.Trim() ?? string.Empty,
            HukouAddress = HukouAddress?.Trim() ?? string.Empty,
            HukouVillage = HukouVillage?.Trim() ?? string.Empty,
            HukouDetailAddress = HukouDetailAddress?.Trim() ?? string.Empty,
            FamilyAddress = FamilyAddress?.Trim() ?? string.Empty,
            DetailAddress = DetailAddress?.Trim() ?? string.Empty,
            BankName = BankName?.Trim() ?? string.Empty,
            BankAccount = BankAccount?.Trim() ?? string.Empty,
            AgentName = AgentName?.Trim() ?? string.Empty,
            AgentRelation = AgentRelation?.Trim() ?? string.Empty,
            AgentReceiveName = AgentReceiveName?.Trim() ?? string.Empty,
            AgentReceiveRelation = AgentReceiveRelation?.Trim() ?? string.Empty,
            AgentReceiveBankName = AgentReceiveBankName?.Trim() ?? string.Empty,
            AgentReceiveBankAccount = AgentReceiveBankAccount?.Trim() ?? string.Empty,
            AgentReceiveReason = AgentReceiveReason?.Trim() ?? string.Empty,
            Category = Category ?? string.Empty,
            IdentityFlag = IdentityFlag ?? string.Empty,
            IdentitySource = IdentitySource ?? string.Empty,
            IsCategoryManual = false,
            IssueStartMonth = IssueStartMonth ?? string.Empty,
            IssueAmount = IssueAmount,
            PaybackStartMonth = PaybackStartMonth ?? string.Empty,
            PaybackEndMonth = PaybackEndMonth ?? string.Empty,
            AutoStartMonth = AutoStartMonth ?? string.Empty,
            AutoEndMonth = AutoEndMonth ?? string.Empty,
            PaybackMonths = PaybackMonths,
            PaybackAmount = PaybackAmount,
            PaybackReason = PaybackReason?.Trim() ?? string.Empty,
            IsSpecialCase = IsSpecialCase,
            SpecialReason = SpecialReason?.Trim() ?? string.Empty,
            ApplyDate = ApplyDate,
            CreatedBy = App.CurrentUserName,
            UpdatedBy = App.CurrentUserName
        };
    }

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "请填写申请人姓名";
        if (string.IsNullOrWhiteSpace(IdCard)) return "请填写身份证号码";
        if (Helpers.IdCardValidator.IsValid(IdCard.Trim()) == false) return "身份证号码无效";
        if (string.IsNullOrWhiteSpace(Phone)) return "请填写联系电话";
        if (string.IsNullOrWhiteSpace(SelectedHukouCity)) return "请选择户籍城市";
        if (string.IsNullOrWhiteSpace(SelectedHukouCounty)) return "请选择户籍区县";
        if (string.IsNullOrWhiteSpace(SelectedHukouTown)) return "请选择户籍乡镇";
        if (string.IsNullOrWhiteSpace(SelectedHukouVillage)) return "请选择户籍村/社区";
        if (string.IsNullOrWhiteSpace(BankName)) return "请填写社保卡开户行";
        if (string.IsNullOrWhiteSpace(BankAccount)) return "请填写社保卡账号";
        if (!HasEvaluateResult) return "请先通过身份证评估享受类别与补发金额";

        if (IsSpecialCase && string.IsNullOrWhiteSpace(SpecialReason))
            return "请填写特殊情况原因说明";

        if (IsSpecialCase)
        {
            var hasStart = !string.IsNullOrWhiteSpace(PaybackStartMonth);
            var hasEnd = !string.IsNullOrWhiteSpace(PaybackEndMonth);
            if (hasStart != hasEnd)
                return "特殊情况：补发起算月与止算月需同时填写，或同时留空表示无补发";
            if (hasStart)
            {
                if (!TryParseMonth(PaybackStartMonth, out var start) || !TryParseMonth(PaybackEndMonth, out var end))
                    return "补发起止月格式应为 yyyy-MM（如 2026-03）";
                if (start > end)
                    return "补发起算月不能晚于止算月";
            }
        }

        return null;
    }

    private async Task<decimal> GetCategoryMonthlyAmountAsync(string category, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(category)) return 0m;
        var result = await _applicationService.GetMonthlyAmountAsync(category, ct);
        return result.IsSuccess ? result.Value : 0m;
    }

    private static bool TryParseMonth(string month, out DateTime value)
    {
        if (DateTime.TryParseExact(month, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
        {
            value = new DateTime(parsed.Year, parsed.Month, 1);
            return true;
        }
        value = default;
        return false;
    }

    private async Task CloseAsync()
    {
        if (Helpers.WindowNavigator.CurrentPage?.Navigation != null)
        {
            await Helpers.WindowNavigator.CurrentPage.Navigation.PopAsync();
            RestoreWindowTitleFromNavigation();
        }
    }

    /// <summary>
    /// 数据补全保存成功后：若本登记源自高龄导入库（nc_biz_elderly_subsidy_history），
    /// 将对应名册行标记为"已并入当前库"（Active→Stopped），名册侧不再重复列为待建档对象。
    /// 只标记不物理删除：停止明细表"实际发放"列依赖名册行读历史金额（删行即回退计发金额）。
    /// 标记失败仅记录日志，不阻断保存流程。
    /// </summary>
    private async Task MarkImportedHistoryMigratedIfNeededAsync()
    {
        if (_importedHistoryId is not > 0) return;

        var result = await _applicationService.MarkHistoryMigratedAsync(IdCard, CancellationToken);
        if (result.IsSuccess)
        {
            _logger.LogBusiness("数据补全后标记名册行已并入当前库",
                ("HistoryId", _importedHistoryId.Value), ("MarkedCount", result.Value));
        }
        else
        {
            _logger.Warn($"数据补全后标记名册行失败: {result.ErrorCode} {result.Message}");
        }
        _importedHistoryId = null;
    }
}
