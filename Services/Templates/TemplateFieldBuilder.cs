using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Services.Templates;

/// <summary>
/// 统一模板字段解析器
/// 所有模板的字段填充都走这里，保证同一模板在任何 ViewModel 中输出一致的字段值
/// ViewModel 只负责提供原始数据（RawFieldData），本类负责解析规则
/// </summary>
public class TemplateFieldBuilder
{
    private readonly AddressResolver _addressResolver;
    private readonly IDictCacheService _dictCacheService;
    private readonly ITemplateService _templateService;
    private readonly IBusinessTimelineService _timelineService;

    public TemplateFieldBuilder(
        AddressResolver addressResolver,
        IDictCacheService dictCacheService,
        ITemplateService templateService,
        IBusinessTimelineService timelineService)
    {
        _addressResolver = addressResolver;
        _dictCacheService = dictCacheService;
        _templateService = templateService;
        _timelineService = timelineService;
    }

    /// <summary>
    /// 根据模板配置 + 原始数据，生成统一的字段字典。
    /// 模板配置解析失败返回显式 Failure（禁止降级为无配置构建——编号成员字段会整体缺失）。
    /// </summary>
    public async Task<Result<Dictionary<string, string>>> BuildFieldsAsync(
        long templateId,
        RawFieldData data,
        CancellationToken ct = default)
    {
        var configResult = await GetConfigAsync(templateId, ct);
        if (configResult.IsFailure)
            return Result<Dictionary<string, string>>.Failure(
                configResult.ErrorCode,
                string.IsNullOrEmpty(configResult.Message) ? "模板配置解析失败" : configResult.Message);

        var config = configResult.Value;

        // 1. 构建基础字段（所有模板通用）
        // B 线窗口按「申请日期」锚定（申请晚于会议日顺延下一周期，保证公示起日 ≥ 申请日）；
        // 无申请日期时按今天锚定。
        var anchorDate = data.ApplicationDate != default ? data.ApplicationDate : DateTime.Today;
        // 业务锚点（申请日期=调查日期新口径 2026-10-16）：调查日与受理日周期差 {0,+1} 时用调查日，
        // 否则回退受理锚点——与档案产出（ArchiveProductionViewModel）同口径，禁止各自重算
        if (data.SurveyDate is { } surveyForAnchor && surveyForAnchor != default)
        {
            var appCycle = _timelineService.GetTimelineForDate(anchorDate, TimelineType.BusinessProcess);
            var svCycle = _timelineService.GetTimelineForDate(surveyForAnchor.Date, TimelineType.BusinessProcess);
            anchorDate = ArchiveAnchorHelper.ResolveBusinessAnchorDate(
                anchorDate, surveyForAnchor.Date,
                (appCycle.Year, appCycle.Month), (svCycle.Year, svCycle.Month));
        }
        var tl = _timelineService.GetTimelineForApplicationDate(anchorDate, TimelineType.BusinessProcess);
        var fields = BuildBaseFields(data, tl, anchorDate);

        // 2. 应用 addressResolver 统一解析地址类字段
        ResolveAddresses(fields, data);

        // 3. 应用 config 中的字段映射规则（编号字段等动态填充）
        if (config != null)
        {
            ApplyConfigMappings(fields, config, data);
        }

        // 4. 用 application 数据覆盖（如果有）
        if (data.ApplicationFields != null)
        {
            foreach (var kv in data.ApplicationFields)
            {
                if (!string.IsNullOrWhiteSpace(kv.Value))
                    fields[kv.Key] = kv.Value;
            }
        }

        return Result.Success(fields);
    }

    /// <summary>
    /// 构建基础字段（从 RawFieldData 提取）
    /// </summary>
    private Dictionary<string, string> BuildBaseFields(RawFieldData data, TimelineResult timeline, DateTime applicationDate)
    {
        var fields = new Dictionary<string, string>
        {
            [FieldKeys.APPLICANT_NAME] = data.ApplicantName ?? "",
            [FieldKeys.APPLICANT_ID_CARD] = data.ApplicantIdCard ?? "",
            [FieldKeys.APPLICANT_ID_TYPE] = _dictCacheService.GetValue(DictionaryTypeCodes.IdTypes, data.ApplicantIdType ?? ""),
            [FieldKeys.APPLICATION_REASON] = string.IsNullOrWhiteSpace(data.ApplicationReason) ? "其他" : _dictCacheService.GetValue(DictionaryTypeCodes.ApplicationReasons, data.ApplicationReason),
            [FieldKeys.APPLICATION_DATE] = applicationDate != default ? applicationDate.ToString("yyyy-MM-dd") : DateTime.Now.ToString("yyyy-MM-dd"), // 业务锚点（=调查日新口径），见 BuildFieldsAsync
            [FieldKeys.CONTACT_PHONE] = data.ContactPhone ?? "",
            [FieldKeys.FAMILY_ADDRESS] = "",  // 统一由 ResolveAddresses 填充
            // 批D2 清理：原 FAMILY_VILLAGE 供给已删（全库 config 读取 0；同源同值 COMMUNITY 保留）
            [FieldKeys.COMMUNITY] = data.Community ?? "",
            [FieldKeys.FAMILY_DETAIL_ADDRESS] = data.DetailAddress ?? "",
            [FieldKeys.HEAD_FAMILY_SIZE] = data.FamilySize.ToString(),
            [FieldKeys.APPLICANT_COUNT] = data.FamilySize.ToString(),
            [FieldKeys.STATUS] = data.Status ?? "",
            [FieldKeys.HEAD_ID_CARD] = data.HeadIdCard ?? data.ApplicantIdCard ?? "",
            [FieldKeys.OPERATOR_NAME] = data.OperatorName ?? "",
            [FieldKeys.OPERATOR_UNIT] = data.OperatorUnit ?? "",
            [FieldKeys.REPORT_DATE] = DateTime.Now.ToString("yyyy-MM-dd"),
            [FieldKeys.CIVIL_ASSISTANT_NAME] = data.OperatorName ?? "",
            [FieldKeys.AGENT_NAME] = "-",
            [FieldKeys.AGENT_ID_CARD] = "-",
            [FieldKeys.AGENT_ID_TYPE] = "-",
            [FieldKeys.AGENT_CERT_TYPE] = "-",
            [FieldKeys.AGENT_RELATION] = "-",
            [FieldKeys.IS_AGENT] = "否",
            [FieldKeys.DISTRICT] = data.District ?? "",
            [FieldKeys.TOWN] = data.Town ?? "",
            [FieldKeys.DISTRICT_CIVIL_BUREAU] = data.Bureau ?? "",
            [FieldKeys.DISTRICT_CIVIL_BUREAU_PHONE] = data.BureauPhone ?? "",
            [FieldKeys.OPERATOR_UNIT_PHONE] = data.UnitPhone ?? "",
            [FieldKeys.NOTICE_NUMBER] = $"告知-{DateTime.Now:yyyyMMdd}-{data.RecordId:D3}",

            // 统一解析字段
            ["APPLICANT_GENDER"] = AddressResolver.ExtractGenderFromIdCard(data.ApplicantIdCard),
            ["APPLICANT_AGE"] = AddressResolver.ExtractAgeFromIdCard(data.ApplicantIdCard),
            ["SURVEY_DATE"] = ResolveSurveyDate(timeline, data.SurveyDate, applicationDate),
        };

        return fields;
    }

    /// <summary>
    /// 入户调查日期（文书打印口径）：优先用实际入户调查日，钳制到 [申请日, 公示前一工作日=会议日]；
    /// 无调查记录（补录/未录入）时取申请日当天。
    /// 与 ArchiveProductionViewModel.ResolveSurveyDate 同口径（2026-10-16 同步）：
    /// 上限铁律——调查不得落进公示期、不得晚于会议日；倒挂窗口（申请日 > 会议日）时上限优先，
    /// 正常窗口维持 [申请日, 会议日] 双钳。
    /// </summary>
    private static string ResolveSurveyDate(TimelineResult timeline, DateTime? actualSurvey, DateTime applicationDate)
    {
        var minDate = applicationDate.Date;
        var maxDate = timeline.MeetingDate.Date;
        var baseDate = actualSurvey.HasValue && actualSurvey.Value.Date != default
            ? actualSurvey.Value.Date
            : (maxDate < minDate ? maxDate : minDate);
        if (maxDate < minDate)
        {
            // 倒挂窗口：只保上限（公示外铁律），不抬到申请日
            if (baseDate > maxDate) baseDate = maxDate;
        }
        else
        {
            if (baseDate < minDate) baseDate = minDate;
            if (baseDate > maxDate) baseDate = maxDate;
        }
        return baseDate.ToString("yyyy年MM月dd日");
    }

    /// <summary>
    /// 统一地址解析：FAMILY_ADDRESS 和所有 FAMILY_MEMBER_ADDRESS_N 都走 addressResolver
    /// </summary>
    private void ResolveAddresses(Dictionary<string, string> fields, RawFieldData data)
    {
        // 主地址
        fields[FieldKeys.FAMILY_ADDRESS] = _addressResolver.BuildAddress(data.Community, data.DetailAddress);

        // 编号成员地址
        if (data.FamilyMembers != null)
        {
            for (int i = 0; i < data.FamilyMembers.Count; i++)
            {
                var m = data.FamilyMembers[i];
                var key = $"FAMILY_MEMBER_ADDRESS_{i + 1}";
                if (!fields.ContainsKey(key))
                    fields[key] = _addressResolver.BuildAddress(m.Community, m.FamilyAddress);
            }
        }
    }

    /// <summary>
    /// 根据模板 config 的 fieldKey 映射，填充编号字段等动态字段
    /// </summary>
    private void ApplyConfigMappings(Dictionary<string, string> fields, TemplateConfig config, RawFieldData data)
    {
        foreach (var mapping in config.Fields)
        {
            var key = mapping.FieldKey;
            if (fields.ContainsKey(key)) continue;

            var lastUnderscore = key.LastIndexOf('_');
            int rowNum = 0;
            var hasNumber = lastUnderscore > 0
                && int.TryParse(key[(lastUnderscore + 1)..], out rowNum);

            if (hasNumber && rowNum >= 1 && data.FamilyMembers != null && rowNum <= data.FamilyMembers.Count)
            {
                var member = data.FamilyMembers[rowNum - 1];
                var isHead = rowNum == 1 && data.HeadIdCard == member.IdCard;
                var relDisplay = isHead
                    ? "本人/户主"
                    : (_dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, member.Relationship ?? "") is var rel && !string.IsNullOrEmpty(rel) ? rel : member.Relationship ?? "");

                var value = key[..lastUnderscore] switch
                {
                    var p when p.EndsWith("NAME") => member.Name ?? "",
                    var p when p.EndsWith("ID_CARD") => member.IdCard ?? "",
                    var p when p.EndsWith("CERT_TYPE") => member.IdTypeDisplay ?? "",
                    var p when p.EndsWith("RELATION") => relDisplay,
                    var p when p.EndsWith("ADDRESS") => _addressResolver.BuildAddress(member.Community, member.FamilyAddress),
                    _ => ""
                };
                if (!string.IsNullOrEmpty(value))
                    fields[key] = value;
            }
            else
            {
                // 非编号字段：仅在已有数据或有默认值时填入，无数据则留空
                if (!string.IsNullOrEmpty(mapping.DefaultValue))
                    fields[key] = mapping.DefaultValue;
            }
        }
    }

    /// <summary>
    /// Success(null) = 模板不存在或未配置 ConfigJson（无配置构建，占位符与字段同名，属正常形态）；
    /// Failure = ConfigJson 已存在但解析失败（配置损坏），必须显式失败——
    /// 过去降级为无配置构建会导致编号成员字段/默认值整体缺失（打印缺字段）。
    /// </summary>
    private async Task<Result<TemplateConfig?>> GetConfigAsync(long templateId, CancellationToken ct)
    {
        try
        {
            var templateResult = await _templateService.GetByIdAsync(templateId, ct);
            if (templateResult.IsFailure)
                return Result.Failure<TemplateConfig?>(
                    templateResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    $"读取模板失败: TemplateId={templateId}, {templateResult.Message}");
            var template = templateResult.Value;
            if (template == null || string.IsNullOrEmpty(template.ConfigJson))
                return Result.Success<TemplateConfig?>(null);

            return Result.Success<TemplateConfig?>(TemplateConfig.FromJson(template.ConfigJson));
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "[TemplateFieldBuilder] 模板配置解析失败 TemplateId={TemplateId}", templateId);
            return Result.Failure<TemplateConfig?>(
                ErrorCodes.FILE_FORMAT_ERROR,
                $"模板配置解析失败: TemplateId={templateId}");
        }
    }
}

/// <summary>
/// 原始数据载体 — ViewModel 只需要填充这个，不需要关心字段解析规则
/// </summary>
public class RawFieldData
{
    // 基础信息
    public long RecordId { get; set; }
    public string? ApplicantName { get; set; }
    public string? ApplicantIdCard { get; set; }
    public string? ApplicantIdType { get; set; }
    public string? ApplicationReason { get; set; }
    public DateTime ApplicationDate { get; set; }
    public DateTime? SurveyDate { get; set; }
    public string? ContactPhone { get; set; }
    public string? HeadIdCard { get; set; }
    public string? Status { get; set; }

    // 地址（原始值，由 AddressResolver 统一解析）
    public string? Community { get; set; }      // village_code
    public string? DetailAddress { get; set; }   // 门牌号

    // 组织
    public string? OperatorName { get; set; }
    public string? OperatorUnit { get; set; }
    public string? District { get; set; }
    public string? Town { get; set; }
    public string? Bureau { get; set; }
    public string? BureauPhone { get; set; }
    public string? UnitPhone { get; set; }

    // 家庭成员（编号字段的数据源）
    public int FamilySize { get; set; }
    public List<FamilyMemberData>? FamilyMembers { get; set; }

    // application 档案数据（如有，优先覆盖）
    public Dictionary<string, string>? ApplicationFields { get; set; }
}

/// <summary>
/// 家庭成员原始数据
/// </summary>
public class FamilyMemberData
{
    public string? Name { get; set; }
    public string? IdCard { get; set; }
    public string? IdTypeDisplay { get; set; }
    public string? Relationship { get; set; }
    public string? Community { get; set; }
    public string? FamilyAddress { get; set; }
}
