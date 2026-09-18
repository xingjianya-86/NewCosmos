using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.SocialAssistance;

public partial class FamilyMembersViewModel : ViewModelBase
{
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    public FamilyMembersViewModel(ILoggerService logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        Title = "家庭成员";
    }

    [ObservableProperty]
    private ObservableCollection<MemberItem> _members = new();

    [ObservableProperty]
    private bool _supportersEnabled = true;

    public void AddMember(MemberItem member) => Members.Add(member);

    [RelayCommand]
    private void AddNewMember()
    {
        Members.Add(new MemberItem
        {
            Name = "新成员",
            RelationshipToHead = "子女",
            IsEnabled = true
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await Task.CompletedTask;
    }
}

public partial class MemberItem : ObservableObject
{
    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _idCard = string.Empty;

    [ObservableProperty]
    private string _relationshipToHead = string.Empty;

    [ObservableProperty]
    private string _gender = string.Empty;

    [ObservableProperty]
    private int _age;

    [ObservableProperty]
    private string _healthStatus = string.Empty;

    [ObservableProperty]
    private string _workCapacity = string.Empty;

    public long Id { get; set; }
}
