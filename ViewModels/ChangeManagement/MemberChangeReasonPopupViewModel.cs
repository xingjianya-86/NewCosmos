using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ChangeManagement;

/// <summary>增/减员原因选项</summary>
public class MemberChangeReasonOption
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>增/减员登记结果：确认 => 本记录；取消 => null</summary>
public sealed record MemberChangeReasonResult(string ReasonCode, string ReasonName, DateTime EventDate, string Remark);

/// <summary>
/// 家庭成员变更登记弹窗 ViewModel（增员/减员共用）：
/// 结果采用 TaskCompletionSource 模式（与 SelectMembersPopup 一致），弹窗由调用方 Push/Pop。
/// 选项唯一数据源 = 字典（减员 ChangeReasons / 增员 MemberAddReasons），字典空则选项空，无硬编码兜底。
/// </summary>
/// 不继承 ViewModelBase（审计豁免）：纯 TCS 结果弹窗 + 表单字段，仅同步读字典缓存快照，无异步 IO。
public partial class MemberChangeReasonPopupViewModel : ObservableObject
{
    private readonly IDictCacheService _dictCacheService;
    private readonly TaskCompletionSource<MemberChangeReasonResult?> _tcs = new();

    public MemberChangeReasonPopupViewModel(IDictCacheService dictCacheService)
    {
        _dictCacheService = dictCacheService;
    }

    /// <summary>弹窗结果：确认 => 增/减员登记信息；取消 => null</summary>
    public Task<MemberChangeReasonResult?> Result => _tcs.Task;

    [ObservableProperty]
    private bool _isRemoveMode;

    [ObservableProperty]
    private string _title = "成员变更登记";

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _reasonPrompt = string.Empty;

    [ObservableProperty]
    private string _hintText = string.Empty;

    #region 成员信息（减员时只读展示）

    [ObservableProperty]
    private string _memberName = string.Empty;

    [ObservableProperty]
    private string _memberIdCard = string.Empty;

    [ObservableProperty]
    private string _memberRelationship = string.Empty;

    [ObservableProperty]
    private int _memberAge;

    [ObservableProperty]
    private string _memberGender = string.Empty;

    [ObservableProperty]
    private string _memberHealthStatus = string.Empty;

    #endregion

    #region 变更登记表单

    [ObservableProperty]
    private ObservableCollection<MemberChangeReasonOption> _reasons = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private MemberChangeReasonOption? _selectedReason;

    /// <summary>事由日期（默认当天；永久减员记入死亡日期，其余为减员/增员生效日期）</summary>
    [ObservableProperty]
    private DateTime _eventDate = DateTime.Today;

    [ObservableProperty]
    private string _remark = string.Empty;

    #endregion

    /// <summary>按增/减员方向初始化（减员时展示待减员成员信息）</summary>
    public void Initialize(bool isRemoveMode, FamilyMember? member)
    {
        IsRemoveMode = isRemoveMode;
        Title = isRemoveMode ? "减员登记" : "增员登记";
        Subtitle = isRemoveMode ? "确认减员并登记原因与事由日期" : "登记增员原因与事由日期";
        ReasonPrompt = isRemoveMode ? "请选择减员原因" : "请选择增员原因";
        HintText = isRemoveMode
            ? "确认减员后将自动重新计算家庭人数与分类判定，并写入变更记录与明细"
            : "确认后请在家庭成员列表中填写新增成员信息，保存时重新计算家庭人数与分类判定";

        if (isRemoveMode && member != null)
        {
            MemberName = member.Name;
            MemberIdCard = member.IdCard;
            MemberRelationship = member.IsHouseholdHead
                ? "户主"
                : (string.IsNullOrWhiteSpace(member.RelationshipToHead) ? "成员" : member.RelationshipToHead);
            MemberAge = member.Age ?? 0;
            MemberGender = member.Gender;
            MemberHealthStatus = member.HealthStatusDisplay;
        }

        var category = isRemoveMode ? DictionaryTypeCodes.ChangeReasons : DictionaryTypeCodes.MemberAddReasons;
        Reasons = new ObservableCollection<MemberChangeReasonOption>(
            _dictCacheService.GetOptions(category).Select(o => new MemberChangeReasonOption
            {
                Code = o.Key,
                Name = o.Display
            }));
        SelectedReason = null;
    }

    /// <summary>确认（未选择原因时按钮不可用）</summary>
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (SelectedReason == null) return;

        _tcs.TrySetResult(new MemberChangeReasonResult(
            SelectedReason.Code,
            SelectedReason.Name,
            EventDate,
            Remark ?? string.Empty));
    }

    private bool CanConfirm() => SelectedReason != null;

    /// <summary>取消</summary>
    [RelayCommand]
    private void Cancel() => _tcs.TrySetResult(null);
}
