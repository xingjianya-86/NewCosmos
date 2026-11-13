using NewCosmos.Services.Core;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 档案照片归属人（PC 端「插入照片 / 打开采集页」的前置校验结果）：
/// 插入的照片必须挂到**已存在的档案人员**名下——身份证须 18 位合法且在低收入/高龄/临时救助
/// 任一业务域存在。数据源与 <see cref="PhotoBackfillCandidate"/> 同一段三域 UNION CTE，
/// 差别仅在本类型按身份证精确定位到一个人（而非按关键词分页模糊检索）。
/// </summary>
public class PhotoOwner
{
    /// <summary>身份证号（写入照片 applicant_id_card）。</summary>
    public string IdCard { get; set; } = string.Empty;

    /// <summary>人员姓名（照片命名规则 2026-11-04 的姓名段）。</summary>
    public string PersonName { get; set; } = string.Empty;

    /// <summary>所属业务域，'/' 分隔：ElderlyBenefits/SocialAssistance/TempRelief。</summary>
    public string BusinessTypes { get; set; } = string.Empty;

    /// <summary>代表业务 ID（写入照片 application_id 追溯用；跨域聚合取其一，无法确定为 null）。</summary>
    public long? ApplicationId { get; set; }

    /// <summary>该身份证已归档照片张数（全类别合计，仅作提示）。</summary>
    public int PhotoCount { get; set; }

    public bool HasPhoto => PhotoCount > 0;

    /// <summary>脱敏身份证（确认框 / 日志展示）。</summary>
    public string IdCardMasked => DataMasker.MaskIdCard(IdCard);

    /// <summary>业务域中文名（多域用「·」连接）。</summary>
    public string BusinessTypeNames => string.Join(" · ",
        BusinessTypes.Split('/', StringSplitOptions.RemoveEmptyEntries)
                     .Select(PhotoBackfillCandidate.ToBusinessDisplayName));

    /// <summary>确认框回显文案：张三（110101********1234）。</summary>
    public string Summary => string.IsNullOrWhiteSpace(PersonName)
        ? IdCardMasked
        : $"{PersonName}（{IdCardMasked}）";
}
