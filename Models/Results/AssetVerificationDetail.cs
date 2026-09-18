using CommunityToolkit.Mvvm.ComponentModel;

namespace NewCosmos.Models.Results;

public class AssetVerificationDetail
{
    public long Id { get; set; }
    public string BatchId { get; set; } = string.Empty;
    public string ApplicantName { get; set; } = string.Empty;
    public string ApplicantIdType { get; set; } = string.Empty;
    public string ApplicantIdCard { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public bool IsHead { get; set; }
    public string HeadIdCard { get; set; } = string.Empty;
    public string FamilyAddress { get; set; } = string.Empty;
    public string Community { get; set; } = string.Empty;
    public string ApplicationReason { get; set; } = string.Empty;
    public DateTime ApplicationDate { get; set; }
    public string ContactPhone { get; set; } = string.Empty;
    public string Status { get; set; } = "0";
    public string StatusDisplay => Status switch
    {
        "0" => "待处",
        "1" => "已完",
        _ => "未知"
    };
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string OperatorUnitName { get; set; } = string.Empty;
    public bool HasAgent { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string AgentIdCard { get; set; } = string.Empty;
    public string AgentRelationship { get; set; } = string.Empty;
    public string AgentIdType { get; set; } = string.Empty;
    public List<AssetVerificationFamilyMember> FamilyMembers { get; set; } = new();
}

public partial class AssetVerificationFamilyMember : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string IdType { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public bool IsHead { get; set; }

    /// <summary>
    /// 是否选中（用于导入弹窗）
    /// </summary>
    [ObservableProperty]
    private bool _isSelected = true;
}
