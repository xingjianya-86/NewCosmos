using CommunityToolkit.Mvvm.ComponentModel;
using NewCosmos.Services.Core;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 照片补传候选（人员维度）：同一身份证在高龄/低收入/临时救助中的业务记录聚合为一行。
/// 照片按身份证归档，因此"已传照片"的人同样可以继续补传（不做未传过滤）。
/// </summary>
public partial class PhotoBackfillCandidate : ObservableObject
{
    /// <summary>列表勾选状态（手工选择后批量补传/打印/导出）。</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>所属业务域代码，'/' 分隔：ElderlyBenefits/SocialAssistance/TempRelief。</summary>
    public string BusinessTypes { get; set; } = string.Empty;

    /// <summary>代表业务ID（补传时写入照片 application_id 追溯用）。</summary>
    public long BusinessId { get; set; }

    /// <summary>人员姓名。</summary>
    public string PersonName { get; set; } = string.Empty;

    /// <summary>身份证号（界面展示走 <see cref="IdCardMasked"/>）。</summary>
    public string IdCard { get; set; } = string.Empty;

    /// <summary>该身份证已归档照片张数（全类别合计）。</summary>
    public int PhotoCount { get; set; }

    /// <summary>业务域中文名（多域用「·」连接）。</summary>
    public string BusinessTypeNames => string.Join(" · ",
        BusinessTypes.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(ToDisplayName));

    /// <summary>照片状态徽章文案。</summary>
    public string PhotoStateText => PhotoCount > 0 ? $"已传 {PhotoCount} 张" : "未传";

    public bool HasPhoto => PhotoCount > 0;

    /// <summary>脱敏身份证（列表展示）。</summary>
    public string IdCardMasked => DataMasker.MaskIdCard(IdCard);

    public string Subtitle => BusinessTypeNames;

    private static string ToDisplayName(string code) => code switch
    {
        "ElderlyBenefits" => "高龄津贴",
        "SocialAssistance" => "低收入人口",
        "TempRelief" => "临时救助",
        _ => code
    };
}

/// <summary>补传会话中的一位人员（跳转照片页时携带，支持"下一位"连续采集）。</summary>
public sealed record PhotoBackfillPerson(string IdCard, long? ApplicationId, string Name);
