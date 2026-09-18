using NewCosmos.Models.Entities;

namespace NewCosmos.Models;

/// <summary>
/// 搜索结果项（支持选中状态变更通知）
/// </summary>
public class PersonSearchResult : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    public string SourceTable { get; set; } = string.Empty;
    public string SourceDisplay { get; set; } = string.Empty;
    public long? RecordId { get; set; }
    public long? ApplicationId { get; set; }
    public DateTime? RecordDate { get; set; }

    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string Town { get; set; } = string.Empty;
    public string Village { get; set; } = string.Empty;

    /// <summary>
    /// 是否选中（用于视觉反馈）
    /// </summary>
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public string DisplayText => $"{Name} - {IdCard} ({SourceDisplay})";

    /// <summary>
    /// 家庭成员列表（从档案表获取）
    /// </summary>
    public List<FamilyMember> FamilyMembers { get; set; } = new();
}
