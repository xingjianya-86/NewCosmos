using CommunityToolkit.Mvvm.ComponentModel;
using NewCosmos.Constants;

namespace NewCosmos.Models.Requests;

/// <summary>
/// 快速核查授权人信息（表单绑定模型）
/// </summary>
public partial class AssetCheckItemDto : ObservableObject
{
    /// <summary>
    /// 姓名
    /// </summary>
    [ObservableProperty]
    private string _applicantName = string.Empty;

    /// <summary>
    /// 证件类型
    /// </summary>
    [ObservableProperty]
    private string _applicantIdType = string.Empty;

    /// <summary>
    /// 身份证号
    /// </summary>
    [ObservableProperty]
    private string _applicantIdCard = string.Empty;

    /// <summary>
    /// 与户主关系    /// </summary>
    [ObservableProperty]
    private string _relationship = string.Empty;

    /// <summary>
    /// 与户主关系索引（Picker 绑定用）
    /// </summary>
    [ObservableProperty]
    private int _relationshipIndex;

    partial void OnRelationshipIndexChanged(int value)
    {
        if (value >= 0 && value < Relationships.Count)
            Relationship = Relationships[value];
    }

    /// <summary>
    /// 是否为户主（首行固定为true，不可删除）
    /// </summary>
    [ObservableProperty]
    private bool _isHead;

    /// <summary>
    /// 关系显示值列表（与 Picker 绑定）
    /// </summary>
    public static readonly List<string> Relationships = new()
    {
        "本人/户主", "配偶", "儿子", "女儿", "孙子", "孙女", "父亲", "母亲", "兄弟", "姐妹", "其他"
    };

    /// <summary>
    /// 批次号（用于批量提交标识）    /// </summary>
    [ObservableProperty]
    private string _batchId = string.Empty;

    /// <summary>
    /// 联系电话（可从表单头部同步）
    /// </summary>
    [ObservableProperty]
    private string _contactPhone = string.Empty;

    /// <summary>
    /// 是否委托代理    /// </summary>
    [ObservableProperty]
    private bool _isAgent;

    /// <summary>
    /// 代理人姓名    /// </summary>
    [ObservableProperty]
    private string _agentName = string.Empty;

    /// <summary>
    /// 代理人身份证号    /// </summary>
    [ObservableProperty]
    private string _agentIdCard = string.Empty;

    /// <summary>
    /// 代理人关系    /// </summary>
    [ObservableProperty]
    private string _agentRelationship = string.Empty;

    /// <summary>
    /// 代理人证件类型    /// </summary>
    [ObservableProperty]
    private string _agentIdType = string.Empty;
}