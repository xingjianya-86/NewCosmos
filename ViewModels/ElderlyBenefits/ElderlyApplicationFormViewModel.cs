using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Navigation;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.System;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace NewCosmos.ViewModels.ElderlyBenefits;

public partial class ElderlyApplicationFormViewModel : ViewModelBase
{
    private readonly IElderlyApplicationService _applicationService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly IRegionService _regionService = null!;
    private readonly IAddressScopeService _addressScopeService = null!;

    /// <summary>账号管辖范围（户籍地址逐级限制；未绑区划=null → 整个地址区禁用）</summary>
    private AddressScope? _addressScope;
    private readonly IUserService _userService = null!;
    private readonly IOrganizationService _organizationService = null!;
    private readonly IDictCacheService _dictCache = null!;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    private long _id;
    private bool _suppressEvaluate;

    /// <summary>保存后自动跳转到停发页（从导入库建档补全流程）</summary>
    public bool NavigateToStopAfterSave { get; private set; }

    /// <summary>待停发的档案ID（保存后跳转停发页用）</summary>
    public long PendingStopApplicationId { get; set; }

    /// <summary>导入库来源记录ID（source_type='ElderlySubsidyHistory' 时有效，补全保存后删除导入库用）</summary>
    private long? _importedHistoryId;

    /// <summary>设置 NavigateToStopAfterSave 并通知横幅属性刷新</summary>
    public void SetNavigateToStopAfterSave(bool value)
    {
        NavigateToStopAfterSave = value;
        OnPropertyChanged(nameof(IsModeBannerVisible));
        OnPropertyChanged(nameof(ModeBannerText));
    }

    /// <summary>保存后返回复核页继续办理（复核前置补全来源，不跳档案输出页）</summary>
    public bool ReturnToReview { get; private set; }

    /// <summary>设置 ReturnToReview 并通知横幅属性刷新</summary>
    public void SetReturnToReview(bool value)
    {
        ReturnToReview = value;
        OnPropertyChanged(nameof(IsModeBannerVisible));
        OnPropertyChanged(nameof(ModeBannerText));
    }

    /// <summary>与申请人关系预设选项（字典 FamilyRelationships 中文显示值；档案存量脏值加载时兜底追加保证可见）</summary>
    public ObservableCollection<string> RelationOptions { get; } = new();

    /// <summary>关系选项兜底项（字典缓存未就绪时使用，与 FamilyRelationships 字典显示值一致）</summary>
    private static readonly string[] s_relationFallback =
    [
        "本人/户主", "配偶", "子/婿", "女/媳", "孙子女/外孙子女",
        "父母/岳父母/公婆", "祖父母/外祖父母", "兄弟姐妹", "其他"
    ];

    /// <summary>加载关系选项（字典驱动，重复初始化不重复追加）</summary>
    internal void LoadRelationOptions()
    {
        if (RelationOptions.Count > 0) return;

        var options = _dictCache.GetOptions(Constants.DictionaryTypeCodes.FamilyRelationships)
            .Select(o => o.Display)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .ToList();
        if (options.Count == 0)
            options = s_relationFallback.ToList();

        foreach (var option in options)
            RelationOptions.Add(option);
    }

    /// <summary>确保档案存量关系值在选项内（存量自由文本如"子女"不在字典时追加，保证 Picker 可显示）</summary>
    internal void EnsureRelationOptionVisible(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) && !RelationOptions.Contains(value))
                RelationOptions.Add(value);
        }
    }

    public ElderlyApplicationFormViewModel(
        IElderlyApplicationService applicationService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        IDialogService dialogService,
        IRegionService regionService,
        IUserService userService,
        IOrganizationService organizationService,
        IAddressScopeService addressScopeService,
        IDictCacheService dictCache)
    {
        _applicationService = applicationService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _dialogService = dialogService;
        _regionService = regionService;
        _userService = userService;
        _organizationService = organizationService;
        _addressScopeService = addressScopeService;
        _dictCache = dictCache;
    }

    /// <summary>
    /// 扫描证件（Camera2 取景 + 类别识别）：跳转扫描页选拍证件 → 内网识别 → 分配页定向回填。
    /// 回填经 <see cref="Services.Platform.IOcrAssignContext"/>（本 VM 实现）：身份证填 Name/IdCard
    /// 即触发 OnIdCardChanged 自动评估（判类 + 补发计算）。
    /// </summary>
    [RelayCommand]
    private async Task ScanIdCardAsync()
    {
        if (DeviceInfo.Platform != DevicePlatform.Android)
        {
            await _dialogService.DisplayAlertAsync("提示", "扫描功能请在手机上使用；电脑端可在「照片采集」中选图识别", "确定");
            return;
        }

        try
        {
            var completion = new TaskCompletionSource<int>();
            await NavigateToPageAsync<Pages.Mobile.MobileOcrScanPage, OcrScanParameter>(
                new OcrScanParameter(this, applied => completion.TrySetResult(applied)));

            var applied = await completion.Task;
            if (applied > 0)
                await _dialogService.ShowSnackBarAsync($"已回填 {applied} 条识别结果", Components.SnackBarType.Success);

            _logger.LogBusiness("高龄津贴-证件扫描", ("Applied", applied.ToString()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "高龄津贴证件扫描失败");
            await _dialogService.DisplayAlertAsync("提示", "扫描失败，请重试或手动录入", "确定");
        }
    }

    /// <summary>
    /// 照片采集/查看：复用档案照片页（nc_biz_archive_photos），照片按身份证归档，
    /// 高龄津贴、低收入人口认定、临时救助等模块共享同一份人员照片。
    /// </summary>
    [RelayCommand]
    private async Task OpenPhotosAsync()
    {
        if (string.IsNullOrWhiteSpace(IdCard))
        {
            await _dialogService.DisplayAlertAsync("提示", "请先填写身份证号码，再采集照片", "确定");
            return;
        }

        _logger.LogBusiness("高龄津贴-照片采集/查看", ("ApplicationId", _id.ToString()));
        await NavigateToPageAsync<Pages.Mobile.MobileArchivePhotoPage, ArchivePhotoParameter>(
            new ArchivePhotoParameter(IdCard.Trim(), _id > 0 ? _id : null, Name,
                AssignContext: this));
    }

}

/// <summary>
/// 享受类别展示项（只读判定结果：命中 ☑ 高亮，未命中 □）
/// </summary>
public sealed record CategoryDisplayItem(string Label, bool Matched)
{
    /// <summary>勾选标记：命中 ☑，未命中 □</summary>
    public string Mark => Matched ? "☑" : "□";
}
