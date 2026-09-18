using NewCosmos.Models.Entities;
using NewCosmos.ViewModels.ChangeManagement;

namespace NewCosmos.Pages.ChangeManagement;

public partial class MemberChangeReasonPopup : ContentPage
{
    private readonly MemberChangeReasonPopupViewModel _viewModel;

    public MemberChangeReasonPopup(MemberChangeReasonPopupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>弹窗结果：确认 => 增/减员登记信息；取消 => null</summary>
    public Task<MemberChangeReasonResult?> Result => _viewModel.Result;

    /// <summary>按增/减员方向初始化（减员时传入待减员成员）</summary>
    public void Initialize(bool isRemoveMode, FamilyMember? member)
        => _viewModel.Initialize(isRemoveMode, member);
}
