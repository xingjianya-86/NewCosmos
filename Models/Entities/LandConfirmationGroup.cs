using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NewCosmos.Constants;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 土地确权归户表分组（每个承包方/户主一个独立组）
/// </summary>
public partial class LandConfirmationGroup : ObservableObject
{
    public long Id { get; set; }

    private int _groupId;
    public int GroupId
    {
        get => _groupId;
        set => SetProperty(ref _groupId, value);
    }

    private string _contractorName = string.Empty;
    public string ContractorName
    {
        get => _contractorName;
        set => SetProperty(ref _contractorName, value);
    }

    private string _villageName = string.Empty;
    public string VillageName
    {
        get => _villageName;
        set => SetProperty(ref _villageName, value);
    }

    private double _dryFieldArea;
    public double DryFieldArea
    {
        get => _dryFieldArea;
        set => SetProperty(ref _dryFieldArea, value);
    }

    private double _wetFieldArea;
    public double WetFieldArea
    {
        get => _wetFieldArea;
        set => SetProperty(ref _wetFieldArea, value);
    }

    public ObservableCollection<LandConfirmationRecord> Records { get; } = new();
    public ObservableCollection<LandConfirmationPerson> Persons { get; } = new();

    public double TotalArea => Records.Sum(r => (double)(r.MeasuredArea > 0 ? r.MeasuredArea : r.LandArea));
    public double TotalShares => Persons.Where(p => LandStatusConstants.ShouldCount(p.LandStatus)).Sum(p => p.SharesCount);
    public int PersonCount => Persons.Count(p => LandStatusConstants.ShouldCount(p.LandStatus));

    /// <summary>
    /// 人员姓名列表（供继承人 Picker 绑定，持久集合并随人员变动刷新）
    /// </summary>
    public ObservableCollection<string> PersonNames { get; } = new();

    /// <summary>
    /// 依据当前人员重建姓名列表，并同步刷新每位人员的继承人候选
    /// </summary>
    private void RefreshPersonNames()
    {
        var names = Persons.Where(p => !string.IsNullOrWhiteSpace(p.Name))
                           .Select(p => p.Name.Trim())
                           .Distinct()
                           .ToList();

        var removed = PersonNames.Where(n => !names.Contains(n)).ToList();
        foreach (var n in removed) PersonNames.Remove(n);
        foreach (var n in names)
        {
            if (!PersonNames.Contains(n))
                PersonNames.Add(n);
        }

        foreach (var person in Persons)
            person.RefreshInheritCandidates(names);
    }

    public string GroupDisplayName => string.IsNullOrWhiteSpace(ContractorName)
        ? $"归户表 {GroupId}"
        : ContractorName;

    public LandConfirmationGroup()
    {
        Records.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (LandConfirmationRecord record in e.NewItems)
                    record.PropertyChanged += OnRecordPropertyChanged;
            }
            if (e.OldItems != null)
            {
                foreach (LandConfirmationRecord record in e.OldItems)
                    record.PropertyChanged -= OnRecordPropertyChanged;
            }
            OnPropertiesChanged();
        };
        Persons.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (LandConfirmationPerson person in e.NewItems)
                {
                    person.PropertyChanged += OnPersonPropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (LandConfirmationPerson person in e.OldItems)
                {
                    person.PropertyChanged -= OnPersonPropertyChanged;
                }
            }
            RefreshPersonNames();
            OnPropertiesChanged();
        };
    }

    private void OnRecordPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LandConfirmationRecord.LandArea) ||
            e.PropertyName == nameof(LandConfirmationRecord.UnitPrice) ||
            e.PropertyName == nameof(LandConfirmationRecord.LandUsage) ||
            e.PropertyName == nameof(LandConfirmationRecord.MeasuredArea) ||
            e.PropertyName == nameof(LandConfirmationRecord.ContractArea) ||
            e.PropertyName == nameof(LandConfirmationRecord.LandValue))
        {
            OnPropertiesChanged();
        }
    }

    private void OnPersonPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LandConfirmationPerson.SharesCount) ||
            e.PropertyName == nameof(LandConfirmationPerson.LandStatus) ||
            e.PropertyName == nameof(LandConfirmationPerson.TotalLandArea))
        {
            OnPropertiesChanged();
        }
        else if (e.PropertyName == nameof(LandConfirmationPerson.Name))
        {
            RefreshPersonNames();
            OnPropertiesChanged();
        }
    }

    private void OnPropertiesChanged()
    {
        OnPropertyChanged(nameof(TotalArea));
        OnPropertyChanged(nameof(TotalShares));
        OnPropertyChanged(nameof(PersonCount));
        OnPropertyChanged(nameof(GroupDisplayName));
    }
}
