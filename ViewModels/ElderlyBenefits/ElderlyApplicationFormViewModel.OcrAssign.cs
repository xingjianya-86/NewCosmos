using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Platform;

namespace NewCosmos.ViewModels.ElderlyBenefits;

/// <summary>
/// 高龄津贴表单的识别结果回填上下文（partial 扩展）。
/// 高龄为单人申请：人物类记录回填申请人（Name/IdCard），银行卡类回填开户行/卡号。
/// 回填铁律：只填空字段；金额字段不回填（§9）。
/// 地址回填铁律（2026-11-05）：**先解析、后回填**——地址文本先在本地算出完整的
/// 市→县→镇→村链条，四级全部命中才一次性写入表单；任一级不匹配（如旧村名/自然屯）
/// 就整体放弃，表单保持原值不动，绝不出现"半截地址把已选乡镇清空"的破坏性结果。
/// </summary>
public partial class ElderlyApplicationFormViewModel : IOcrAssignContext
{
    /// <summary>本轮回填产生的用户可读提示（未回填原因）；分配页确认后汇总展示，避免静默改坏表单。</summary>
    private readonly List<string> _ocrWarnings = new();

    IReadOnlyList<string> IOcrAssignContext.Warnings => _ocrWarnings;

    void IOcrAssignContext.ClearWarnings() => _ocrWarnings.Clear();

    IReadOnlyList<OcrAssignmentTarget> IOcrAssignContext.GetTargets(string categoryCode)
    {
        var targets = new List<OcrAssignmentTarget>();
        switch (categoryCode)
        {
            case PhotoCategoryConstants.ID_CARD:
                targets.Add(new OcrAssignmentTarget(OcrAssignmentPlanner.TargetKeys.Head, "申请人"));
                // 2026-11-05：身份证背面住址 → 家庭住址（与「申请人」并列可选）
                targets.Add(new OcrAssignmentTarget(OcrAssignmentPlanner.TargetKeys.FormAddress, "家庭住址"));
                break;
            case PhotoCategoryConstants.BANK_CARD:
                targets.Add(new OcrAssignmentTarget(OcrAssignmentPlanner.TargetKeys.FormBank, "开户行/银行卡号"));
                break;

            case PhotoCategoryConstants.HOUSEHOLD_REGISTER:
                // 2026-11-04 修复：原实现漏了本分支，户口本记录在分配页只剩「不使用」，
                // 户籍地址根本无法回填（Fix：与低收入表单对齐补 FormHukou 目标）
                targets.Add(new OcrAssignmentTarget(OcrAssignmentPlanner.TargetKeys.FormHukou, "户籍地址"));
                break;
        }
        return targets;
    }

    IReadOnlyList<OcrAssignmentPlanner.MemberRow> IOcrAssignContext.GetMemberRows()
        => new List<OcrAssignmentPlanner.MemberRow>
        {
            new(OcrAssignmentPlanner.TargetKeys.Head, Name ?? string.Empty, IdCard ?? string.Empty)
        };

    Task<bool> IOcrAssignContext.ApplyAsync(OcrRecognizedRecord record, OcrAssignmentTarget target)
        => ApplyCoreAsync(record, target);

    private async Task<bool> ApplyCoreAsync(OcrRecognizedRecord record, OcrAssignmentTarget target)
    {
        var written = false;
        switch (target.Key)
        {
            case OcrAssignmentPlanner.TargetKeys.Head:
                if (record.Category == PhotoCategoryConstants.ID_CARD)
                {
                    var name = record.GetField(OcrFieldKeys.Name);
                    var idCard = record.GetField(OcrFieldKeys.IdCard);
                    if (OcrAssignmentPlanner.IsBlank(Name) && name.Length > 0) { Name = name; written = true; }
                    if (OcrAssignmentPlanner.IsBlank(IdCard) && idCard.Length > 0) { IdCard = idCard; written = true; }
                }
                break;

            case OcrAssignmentPlanner.TargetKeys.FormBank:
                var bankName = record.GetField(OcrFieldKeys.BankName);
                var cardNumber = record.GetField(OcrFieldKeys.CardNumber);
                if (OcrAssignmentPlanner.IsBlank(BankName) && bankName.Length > 0) { BankName = bankName; written = true; }
                if (OcrAssignmentPlanner.IsBlank(BankAccount) && cardNumber.Length > 0) { BankAccount = cardNumber; written = true; }
                break;

            case OcrAssignmentPlanner.TargetKeys.FormHukou:
                // 高龄表单户籍地址 = 级联（市/县/镇/村），保存时按级联拼 HukouAddress；
                // 故回填必须解析进级联，单写文本会被保存覆盖。
                var hukou = record.GetField(OcrFieldKeys.HukouAddress);
                if (hukou.Length > 0)
                    written = await TryApplyHukouFromOcrAsync(hukou);
                break;

            case OcrAssignmentPlanner.TargetKeys.FormAddress:
                // 身份证背面住址 → 家庭住址级联（同户籍：文本单写会被保存时的级联覆盖）
                var address = record.GetField(OcrFieldKeys.Address);
                if (address.Length > 0)
                    written = await TryApplyFamilyFromOcrAsync(address);
                break;
        }

        if (written)
        {
            Logger.LogBusiness("OCR 回填高龄津贴表单",
                ("Category", record.Category),
                ("Target", target.Key),
                ("IdCard", DataMasker.MaskIdCard(record.GetField(OcrFieldKeys.IdCard))));
        }
        return written;
    }

    #region 地址解析（只读）

    /// <summary>
    /// 解析结果：名称 + 本次解析所用的完整候选列表。
    /// 候选随结果一起返回，提交阶段直接替换选项、不再查库，
    /// 从根上避免"清空选项 → 查库失败 → 留下空选项"的中间态。
    /// </summary>
    private sealed record RegionResolution(
        string Province,
        string City,
        string County,
        string Town,
        string Village,
        string Detail,
        IReadOnlyList<string> CityOptions,
        IReadOnlyList<string> CountyOptions,
        IReadOnlyList<string> TownOptions,
        IReadOnlyList<string> VillageOptions);

    /// <summary>
    /// 户口本户籍地址 → 户籍级联（市→县→镇→村）。只在管辖范围内匹配；
    /// 四级必须全部命中，否则返回 null（Reason 为可读原因），调用方不得改动表单。
    /// </summary>
    private async Task<(RegionResolution? Resolution, string Reason)> ResolveHukouAsync(string address)
    {
        var reason = string.Empty;

        var citiesResult = await _regionService.GetCitiesAsync(CancellationToken);
        if (citiesResult.IsFailure || citiesResult.Value == null)
        {
            reason = "地级市列表加载失败";
            return (null, reason);
        }

        var cityOptions = citiesResult.Value
            .Where(c => AllowHukouCity(c.CityName))
            .Select(c => c.CityName)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        var cityMatch = OcrAssignmentPlanner.MatchRegionOption(cityOptions, address);
        var rest = address;
        RegionCity? city;
        if (cityMatch is not null)
        {
            city = citiesResult.Value.FirstOrDefault(c => c.CityName == cityMatch.Value.Name);
            rest = address[(cityMatch.Value.Index + cityMatch.Value.Length)..];
        }
        else
        {
            // 户口本常见写法省略地级市（省 → 县）：沿用表单当前市；当前市为空且管辖范围只剩一个市时用它
            var fallbackCityName = !string.IsNullOrEmpty(SelectedHukouCity)
                ? SelectedHukouCity
                : cityOptions.Count == 1 ? cityOptions[0] : string.Empty;
            city = citiesResult.Value.FirstOrDefault(c => c.CityName == fallbackCityName);
        }

        if (city is null || !AllowHukouCity(city.CityName))
        {
            reason = $"未匹配到本市城市（地址：{address}）";
            return (null, reason);
        }

        // 城市下拉须与解析出的省一致（省 ID 未回填时保留原候选，避免下拉被清空）
        var pickerCityOptions = city.ProvinceId is > 0
            ? citiesResult.Value
                .Where(c => c.ProvinceId == city.ProvinceId && AllowHukouCity(c.CityName))
                .Select(c => c.CityName)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList()
            : cityOptions;

        var countiesResult = await _regionService.GetCountiesByCityAsync(city.CityName, CancellationToken);
        if (countiesResult.IsFailure || countiesResult.Value == null)
        {
            reason = "区县列表加载失败";
            return (null, reason);
        }

        var countyOptions = countiesResult.Value
            .Where(c => AllowHukouCounty(c.CountyName))
            .Select(c => c.CountyName)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        var countyMatch = OcrAssignmentPlanner.MatchRegionOption(countyOptions, rest);
        if (countyMatch is null)
        {
            reason = $"未匹配到区县（剩余：{rest}）";
            return (null, reason);
        }

        var county = countiesResult.Value.FirstOrDefault(c => c.CountyName == countyMatch.Value.Name);
        if (county is null)
        {
            reason = "区县信息缺失";
            return (null, reason);
        }
        rest = rest[(countyMatch.Value.Index + countyMatch.Value.Length)..];

        var townsResult = await _regionService.GetTownsByCountyIdAsync(county.Id, CancellationToken);
        if (townsResult.IsFailure || townsResult.Value == null)
        {
            reason = "乡镇列表加载失败";
            return (null, reason);
        }

        var townOptions = townsResult.Value
            .Where(t => AllowHukouTown(t.TownName))
            .Select(t => t.TownName)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        var townMatch = OcrAssignmentPlanner.MatchRegionOption(townOptions, rest);
        if (townMatch is null)
        {
            reason = $"未匹配到乡镇（剩余：{rest}）";
            return (null, reason);
        }

        var town = townsResult.Value.FirstOrDefault(t => t.TownName == townMatch.Value.Name);
        if (town is null)
        {
            reason = "乡镇信息缺失";
            return (null, reason);
        }
        rest = rest[(townMatch.Value.Index + townMatch.Value.Length)..];

        var villagesResult = await _regionService.GetVillagesByTownIdAsync(town.Id, CancellationToken);
        if (villagesResult.IsFailure || villagesResult.Value == null)
        {
            reason = "村/社区列表加载失败";
            return (null, reason);
        }

        var villageOptions = villagesResult.Value
            .Where(v => AllowHukouVillage(v.VillageName))
            .Select(v => v.VillageName)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        // 村级匹配比其它级更严格：歧义（多个村共用词干）一律不猜
        var villageMatch = OcrAssignmentPlanner.MatchVillageOption(villageOptions, rest);
        if (villageMatch is null)
        {
            reason = $"未匹配到村/社区（剩余：{rest}，可能是旧村名或自然屯）";
            return (null, reason);
        }

        var hukouDetail = BuildAddressRemainder(rest, villageMatch.Value);

        return (new RegionResolution(
            ResolveProvinceName(city.ProvinceId, SelectedHukouProvince),
            city.CityName,
            county.CountyName,
            town.TownName,
            villageMatch.Value.Name,
            hukouDetail,
            pickerCityOptions,
            countyOptions,
            townOptions,
            villageOptions),
            string.Empty);
    }

    /// <summary>
    /// 身份证背面住址 → 家庭住址级联（省→市→县→镇→村）。家庭住址全国可选、不受管辖范围限制；
    /// 同样要求四级全部命中，否则整体放弃（返回 null + 原因）。
    /// </summary>
    private async Task<(RegionResolution? Resolution, string Reason)> ResolveFamilyAsync(string address)
    {
        var reason = string.Empty;

        var citiesResult = await _regionService.GetCitiesAsync(CancellationToken);
        if (citiesResult.IsFailure || citiesResult.Value == null)
        {
            reason = "地级市列表加载失败";
            return (null, reason);
        }

        var cityOptions = citiesResult.Value
            .Select(c => c.CityName)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        var cityMatch = OcrAssignmentPlanner.MatchRegionOption(cityOptions, address);
        var rest = address;
        RegionCity? city;
        if (cityMatch is not null)
        {
            city = citiesResult.Value.FirstOrDefault(c => c.CityName == cityMatch.Value.Name);
            rest = address[(cityMatch.Value.Index + cityMatch.Value.Length)..];
        }
        else
        {
            city = citiesResult.Value.FirstOrDefault(c => c.CityName == SelectedFamilyCity);
        }

        if (city is null)
        {
            reason = $"未匹配到城市（地址：{address}）";
            return (null, reason);
        }

        // 城市下拉须与解析出的省一致（省 ID 未回填时保留全国候选）
        var pickerCityOptions = city.ProvinceId is > 0
            ? citiesResult.Value
                .Where(c => c.ProvinceId == city.ProvinceId)
                .Select(c => c.CityName)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList()
            : cityOptions;

        var countiesResult = await _regionService.GetCountiesByCityAsync(city.CityName, CancellationToken);
        if (countiesResult.IsFailure || countiesResult.Value == null)
        {
            reason = "区县列表加载失败";
            return (null, reason);
        }

        var countyOptions = countiesResult.Value
            .Select(c => c.CountyName)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        var countyMatch = OcrAssignmentPlanner.MatchRegionOption(countyOptions, rest);
        if (countyMatch is null)
        {
            reason = $"未匹配到区县（剩余：{rest}）";
            return (null, reason);
        }

        var county = countiesResult.Value.FirstOrDefault(c => c.CountyName == countyMatch.Value.Name);
        if (county is null)
        {
            reason = "区县信息缺失";
            return (null, reason);
        }
        rest = rest[(countyMatch.Value.Index + countyMatch.Value.Length)..];

        var townsResult = await _regionService.GetTownsByCountyIdAsync(county.Id, CancellationToken);
        if (townsResult.IsFailure || townsResult.Value == null)
        {
            reason = "乡镇列表加载失败";
            return (null, reason);
        }

        var townOptions = townsResult.Value
            .Select(t => t.TownName)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        var townMatch = OcrAssignmentPlanner.MatchRegionOption(townOptions, rest);
        if (townMatch is null)
        {
            reason = $"未匹配到乡镇（剩余：{rest}）";
            return (null, reason);
        }

        var town = townsResult.Value.FirstOrDefault(t => t.TownName == townMatch.Value.Name);
        if (town is null)
        {
            reason = "乡镇信息缺失";
            return (null, reason);
        }
        rest = rest[(townMatch.Value.Index + townMatch.Value.Length)..];

        var villagesResult = await _regionService.GetVillagesByTownIdAsync(town.Id, CancellationToken);
        if (villagesResult.IsFailure || villagesResult.Value == null)
        {
            reason = "村/社区列表加载失败";
            return (null, reason);
        }

        var villageOptions = villagesResult.Value
            .Select(v => v.VillageName)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        var villageMatch = OcrAssignmentPlanner.MatchVillageOption(villageOptions, rest);
        if (villageMatch is null)
        {
            reason = $"未匹配到村/社区（剩余：{rest}，可能是旧村名或自然屯）";
            return (null, reason);
        }

        var familyDetail = BuildAddressRemainder(rest, villageMatch.Value);

        return (new RegionResolution(
            ResolveProvinceName(city.ProvinceId, SelectedFamilyProvince),
            city.CityName,
            county.CountyName,
            town.TownName,
            villageMatch.Value.Name,
            familyDetail,
            pickerCityOptions,
            countyOptions,
            townOptions,
            villageOptions),
            string.Empty);
    }

    /// <summary>市 → 省名（省表缺失/城市未回填省 ID 时沿用表单当前省）</summary>
    private string ResolveProvinceName(int? provinceId, string fallback)
    {
        if (provinceId is > 0)
        {
            var hit = _provinceIdMap.FirstOrDefault(kv => kv.Value == provinceId.Value);
            if (!string.IsNullOrEmpty(hit.Key)) return hit.Key;
        }
        return fallback ?? string.Empty;
    }

    /// <summary>地址文本规范化：OCR 常在行政区划之间插空格/换行（全角空格一并处理）</summary>
    private static string NormalizeAddressText(string? addressText)
        => new string((addressText ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray()).Trim();

    /// <summary>村名之后的剩余文本 = 门牌号/屯组，去掉首尾标点</summary>
    private static string CleanAddressRemainder(string rest)
        => (rest ?? string.Empty).Trim(' ', '，', ',', '、', '；', ';', '：', ':', '-', '—', '。', '.');

    /// <summary>
    /// 详细地址取「村名匹配段 + 其后剩余」：库内「柞木村委会」对上户口本「柞木屯245号」时，
    /// 详细地址应保留「柞木屯245号」而不是残缺的「屯245号」；门牌号开头则不重复村名。
    /// </summary>
    private static string BuildAddressRemainder(string rest, OcrAssignmentPlanner.RegionMatch village)
    {
        if (string.IsNullOrEmpty(rest) || village.Index < 0 || village.Index + village.Length > rest.Length)
            return CleanAddressRemainder(rest);

        var matched = rest.Substring(village.Index, village.Length);
        var tail = rest[(village.Index + village.Length)..];
        var needsVillageName = tail.Length > 0 && "屯组队片庄沟堡".IndexOf(tail[0]) >= 0;
        return CleanAddressRemainder(needsVillageName ? matched + tail : tail);
    }

    #endregion

    #region 地址提交（一次性写入）

    private async Task<bool> TryApplyHukouFromOcrAsync(string addressText)
    {
        var address = NormalizeAddressText(addressText);
        if (address.Length == 0) return false;

        var (resolution, reason) = await ResolveHukouAsync(address);
        if (resolution is null)
        {
            WarnNotApplied("户籍地址", reason, address);
            return false;
        }

        var previousSuppress = _suppressRegionCascade;
        _suppressRegionCascade = true;
        try
        {
            CommitHukou(resolution);
        }
        finally
        {
            _suppressRegionCascade = previousSuppress;
        }

        Logger.Info($"OCR 户籍地址解析完成: {resolution.City}/{resolution.County}/{resolution.Town}/{resolution.Village} 详细：{HukouDetailAddress}");
        return true;
    }

    private async Task<bool> TryApplyFamilyFromOcrAsync(string addressText)
    {
        var address = NormalizeAddressText(addressText);
        if (address.Length == 0) return false;

        var (resolution, reason) = await ResolveFamilyAsync(address);
        if (resolution is null)
        {
            WarnNotApplied("家庭住址", reason, address);
            return false;
        }

        var previousSuppress = _suppressRegionCascade;
        _suppressRegionCascade = true;
        try
        {
            CommitFamily(resolution);
        }
        finally
        {
            _suppressRegionCascade = previousSuppress;
        }

        Logger.Info($"OCR 家庭住址解析完成: {resolution.City}/{resolution.County}/{resolution.Town}/{resolution.Village} 详细：{DetailAddress}");
        return true;
    }

    /// <summary>解析失败：整体放弃（表单零改动），把原因交给分配页提示用户</summary>
    private void WarnNotApplied(string fieldName, string reason, string address)
    {
        _ocrWarnings.Add($"{fieldName}未回填：{reason}；表单原地址保持不变，请手工核对。");
        Logger.Warn($"OCR {fieldName}解析放弃（表单未改动）：{reason}（原文：{address}）");
    }

    /// <summary>
    /// 一次性写入户籍级联：先装选项、再设选中值（调用方已置 _suppressRegionCascade）。
    /// 全程不查库、不 await，不存在"选项已清空但新数据没回来"的中间态。
    /// </summary>
    private void CommitHukou(RegionResolution resolution)
    {
        if (!string.IsNullOrEmpty(resolution.Province) && HukouProvinceOptions.Contains(resolution.Province))
            SelectedHukouProvince = resolution.Province;

        ReplaceOptions(HukouCityOptions, resolution.CityOptions);
        SelectedHukouCity = resolution.City;

        ReplaceOptions(HukouCountyOptions, resolution.CountyOptions);
        SelectedHukouCounty = resolution.County;

        ReplaceOptions(HukouTownOptions, resolution.TownOptions);
        SelectedHukouTown = resolution.Town;

        ReplaceOptions(HukouVillageOptions, resolution.VillageOptions);
        SelectedHukouVillage = resolution.Village;

        if (resolution.Detail.Length > 0 && OcrAssignmentPlanner.IsBlank(HukouDetailAddress))
            HukouDetailAddress = resolution.Detail;
    }

    /// <summary>一次性写入家庭住址级联（规则同 <see cref="CommitHukou"/>）</summary>
    private void CommitFamily(RegionResolution resolution)
    {
        if (!string.IsNullOrEmpty(resolution.Province) && FamilyProvinceOptions.Contains(resolution.Province))
            SelectedFamilyProvince = resolution.Province;

        ReplaceOptions(FamilyCityOptions, resolution.CityOptions);
        SelectedFamilyCity = resolution.City;

        ReplaceOptions(FamilyCountyOptions, resolution.CountyOptions);
        SelectedFamilyCounty = resolution.County;

        ReplaceOptions(FamilyTownOptions, resolution.TownOptions);
        SelectedFamilyTown = resolution.Town;

        ReplaceOptions(FamilyVillageOptions, resolution.VillageOptions);
        SelectedFamilyVillage = resolution.Village;

        if (resolution.Detail.Length > 0 && OcrAssignmentPlanner.IsBlank(DetailAddress))
            DetailAddress = resolution.Detail;
    }

    #endregion
}
