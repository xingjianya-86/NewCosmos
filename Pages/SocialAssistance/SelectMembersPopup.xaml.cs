using System.Collections.ObjectModel;
using NewCosmos.Models.Entities;

namespace NewCosmos.Pages.SocialAssistance;

public partial class SelectMembersPopup : ContentPage
{
    private readonly ObservableCollection<FamilyMember> _allMembers;
    private readonly TaskCompletionSource<List<FamilyMember>> _tcs = new();

    public Task<List<FamilyMember>> Result => _tcs.Task;

    public SelectMembersPopup(ObservableCollection<FamilyMember> allMembers)
    {
        InitializeComponent();
        _allMembers = allMembers;

        foreach (var member in _allMembers)
        {
            member.IsSelected = false;
        }

        BindingContext = this;
    }

    public ObservableCollection<FamilyMember> Members => _allMembers;

    private void OnSelectAllClicked(object? sender, EventArgs e)
    {
        var allSelected = _allMembers.All(m => m.IsSelected);
        foreach (var member in _allMembers)
        {
            member.IsSelected = !allSelected;
        }
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        if (!_tcs.Task.IsCompleted)
        {
            _tcs.SetResult(new List<FamilyMember>());
        }
        await Navigation.PopModalAsync();
    }

    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        var selected = _allMembers.Where(m => m.IsSelected).ToList();

        if (selected.Count == 0)
        {
            await DisplayAlertAsync("提示", "请至少选择一个家庭成员", "确定");
            return;
        }

        if (!_tcs.Task.IsCompleted)
        {
            _tcs.SetResult(selected);
        }
        await Navigation.PopModalAsync();
    }
}
