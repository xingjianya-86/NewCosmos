using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NewCosmos.Constants;
using NewCosmos.Helpers;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 土地确权人员（用于管理归户表中的人员名单和土地状态）
/// </summary>
public partial class LandConfirmationPerson : ObservableObject
{
    /// <summary>
    /// 所属土地确权组 ID（对应 confirmation_id 列，用于批量加载后归组）
    /// </summary>
    public long ConfirmationId { get; set; }

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private string _idCard = string.Empty;
    public string IdCard
    {
        get => _idCard;
        set
        {
            if (SetProperty(ref _idCard, value))
            {
                BirthDate = IdCardValidator.ExtractBirthDate(value);

                if (ShouldBeNoLandRight && LandStatus == LandStatusConstants.LIVING_ENTITLED)
                {
                    LandStatus = LandStatusConstants.NO_LAND_RIGHT;
                }

                OnPropertyChanged(nameof(ShouldBeNoLandRight));
            }
        }
    }

    private string _landStatus = LandStatusConstants.LIVING_ENTITLED;
    public string LandStatus
    {
        get => _landStatus;
        set
        {
            if (SetProperty(ref _landStatus, value))
            {
                OnPropertyChanged(nameof(IsDeceasedInheritance));
                OnPropertyChanged(nameof(CanInherit));
            }
        }
    }

    private string _landInheritTo = string.Empty;
    public string LandInheritTo
    {
        get => _landInheritTo;
        set => SetProperty(ref _landInheritTo, value);
    }

    /// <summary>
    /// 继承人候选名单（本组内其他成员姓名，由所属 LandConfirmationGroup 维护）
    /// </summary>
    public ObservableCollection<string> InheritCandidates { get; } = new();

    /// <summary>
    /// 刷新继承人候选名单（排除本人）
    /// </summary>
    public void RefreshInheritCandidates(IEnumerable<string> names)
    {
        InheritCandidates.Clear();
        foreach (var candidate in names)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && candidate.Trim() != Name.Trim())
                InheritCandidates.Add(candidate);
        }
    }

    private double _sharesCount = 1;
    public double SharesCount
    {
        get => _sharesCount;
        set => SetProperty(ref _sharesCount, value);
    }

    private double _totalLandArea;
    public double TotalLandArea
    {
        get => _totalLandArea;
        set => SetProperty(ref _totalLandArea, value);
    }

    private DateTime? _birthDate;
    public DateTime? BirthDate
    {
        get => _birthDate;
        private set => SetProperty(ref _birthDate, value);
    }

    /// <summary>
    /// 自动判定是否应为无土地权（出生日期晚于1998年12月31日）
    /// </summary>
    public bool ShouldBeNoLandRight => BirthDate.HasValue && BirthDate.Value > new DateTime(1998, 12, 31);

    public bool IsDeceasedInheritance => LandStatus == LandStatusConstants.DECEASED_INHERITANCE;
    public bool CanInherit => LandStatus == LandStatusConstants.DECEASED_INHERITANCE || LandStatus == LandStatusConstants.LIVING_ENTITLED;

    private bool _isImported;
    public bool IsImported
    {
        get => _isImported;
        set => SetProperty(ref _isImported, value);
    }
}
