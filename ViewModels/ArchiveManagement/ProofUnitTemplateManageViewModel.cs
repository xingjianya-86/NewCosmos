using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ArchiveManagement;

/// <summary>
/// 单位证明模板维护 ViewModel
/// 管理"接收单位 → 专属证明模板"映射（全局共享）：新增（上传模板文件）、编辑（改名/替换文件）、删除
/// </summary>
public partial class ProofUnitTemplateManageViewModel : ViewModelBase
{
    private readonly IProofUnitTemplateService _proofUnitTemplateService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IFileService _fileService;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    private static readonly string[] ValidTemplateExtensions = { ".docx", ".xlsx" };

    /// <summary>正在编辑的映射 ID（0 表示新增）</summary>
    private long _editingId;

    #region 列表

    [ObservableProperty]
    private ObservableCollection<ProofUnitTemplateItem> _unitTemplates = new();

    [ObservableProperty]
    private ProofUnitTemplateItem? _selectedUnitTemplate;

    [ObservableProperty]
    private bool _hasSelectedTemplate;

    [ObservableProperty]
    private bool _isBrowsing = true;

    [ObservableProperty]
    private bool _isEditingForm;

    [ObservableProperty]
    private bool _isSaving;

    #endregion

    #region 表单

    /// <summary>单位名称</summary>
    [ObservableProperty]
    private string _unitName = string.Empty;

    /// <summary>选中文件路径</summary>
    [ObservableProperty]
    private string _selectedFilePath = string.Empty;

    /// <summary>选中文件名（用于提取模板名）</summary>
    [ObservableProperty]
    private string _fileName = string.Empty;

    /// <summary>模板类型（Word/Excel）</summary>
    [ObservableProperty]
    private string _templateType = "Word";

    /// <summary>字段映射配置（可选，JSON）</summary>
    [ObservableProperty]
    private string _configJson = string.Empty;

    /// <summary>是否为新增模式（false 为编辑）</summary>
    [ObservableProperty]
    private bool _isCreateMode = true;

    /// <summary>表单标题</summary>
    public string FormTitle => IsCreateMode ? "新增单位模板" : "编辑单位模板";

    #endregion

    public ProofUnitTemplateManageViewModel(
        IProofUnitTemplateService proofUnitTemplateService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        IFileService fileService)
    {
        _proofUnitTemplateService = proofUnitTemplateService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _fileService = fileService;
        Title = "单位模板维护";
    }

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await LoadTemplatesAsync();
    }

    partial void OnSelectedUnitTemplateChanged(ProofUnitTemplateItem? value)
    {
        HasSelectedTemplate = value != null;
    }

    partial void OnIsCreateModeChanged(bool value)
    {
        OnPropertyChanged(nameof(FormTitle));
    }

    private async Task LoadTemplatesAsync()
    {
        await ExecuteAsync(async ct =>
        {
            var result = await _proofUnitTemplateService.GetAllAsync(ct);
            if (result.IsFailure)
            {
                _logger.LogError(new Exception(result.Message ?? "未知错误"), "加载单位模板失败");
                return;
            }

            UnitTemplates.Clear();
            foreach (var item in result.Value ?? [])
            {
                UnitTemplates.Add(item);
            }
        }, "正在加载单位模板...");
    }

    #region 列表操作

    /// <summary>新增：打开表单</summary>
    [RelayCommand]
    private void StartCreate()
    {
        _editingId = 0;
        IsCreateMode = true;
        UnitName = string.Empty;
        SelectedFilePath = string.Empty;
        FileName = string.Empty;
        TemplateType = "Word";
        ConfigJson = string.Empty;
        IsEditingForm = true;
        IsBrowsing = false;
    }

    /// <summary>编辑：填充表单</summary>
    [RelayCommand]
    private void StartEdit()
    {
        if (SelectedUnitTemplate == null)
        {
            _ = ShowTipAsync("请先选择要编辑的单位模板");
            return;
        }

        _editingId = SelectedUnitTemplate.Id;
        IsCreateMode = false;
        UnitName = SelectedUnitTemplate.UnitName;
        SelectedFilePath = string.Empty;
        FileName = string.Empty;
        TemplateType = TemplateFileTypes.IsExcel(SelectedUnitTemplate.FileType)
            ? TemplateFileTypes.Excel : TemplateFileTypes.Word;
        ConfigJson = string.Empty;
        IsEditingForm = true;
        IsBrowsing = false;
    }

    /// <summary>删除（仅删映射，模板文件保留）</summary>
    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedUnitTemplate == null)
        {
            await ShowTipAsync("请先选择要删除的单位模板");
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync("确认删除",
            $"确定要删除单位 [{SelectedUnitTemplate.UnitName}] 的证明模板吗？\n（仅解除映射，模板文件保留）",
            "删除", "取消");
        if (!confirm) return;

        await ExecuteAsync(async ct =>
        {
            var result = await _proofUnitTemplateService.DeleteAsync(SelectedUnitTemplate.Id, ct);
            if (result.IsFailure)
            {
                await ShowTipAsync($"删除失败: {result.Message}");
                return;
            }
            await LoadTemplatesAsync();
            await _dialogService.DisplayAlertAsync("成功", "删除成功", "确定");
        }, "正在删除...");
    }

    /// <summary>返回列表</summary>
    [RelayCommand]
    private void CancelEdit()
    {
        IsEditingForm = false;
        IsBrowsing = true;
    }

    #endregion

    #region 表单操作

    /// <summary>选择模板文件（.docx/.xlsx）</summary>
    [RelayCommand]
    private async Task SelectFileAsync()
    {
        var path = await PickFileWithFeedbackAsync("选择证明模板文件", ValidTemplateExtensions);
        if (string.IsNullOrEmpty(path)) return;

        SelectedFilePath = path;
        FileName = Path.GetFileName(path);
        TemplateType = path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
            ? TemplateFileTypes.Word : TemplateFileTypes.Excel;
    }

    /// <summary>保存（新增建模板+映射；编辑改名/替换文件）</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(UnitName))
        {
            await ShowTipAsync("请输入接收单位名称");
            return;
        }

        IsSaving = true;
        try
        {
            if (IsCreateMode)
            {
                if (string.IsNullOrEmpty(SelectedFilePath))
                {
                    await ShowTipAsync("请选择证明模板文件（.docx / .xlsx）");
                    return;
                }

                var fileBytes = await _fileService.ReadAllBytesAsync(SelectedFilePath);
                var config = string.IsNullOrWhiteSpace(ConfigJson) ? null : ConfigJson;
                var result = await _proofUnitTemplateService.CreateAsync(
                    UnitName, fileBytes, FileName, TemplateType, config, App.CurrentUserId, CancellationToken);

                if (result.IsFailure)
                {
                    await ShowTipAsync($"保存失败: {result.Message}");
                    return;
                }
            }
            else
            {
                byte[]? replaceBytes = null;
                string? replaceType = null;
                if (!string.IsNullOrEmpty(SelectedFilePath))
                {
                    replaceBytes = await _fileService.ReadAllBytesAsync(SelectedFilePath);
                    replaceType = TemplateType;
                }

                var result = await _proofUnitTemplateService.UpdateAsync(
                    _editingId, UnitName, replaceBytes, replaceType, App.CurrentUserId, CancellationToken);
                if (result.IsFailure)
                {
                    await ShowTipAsync($"保存失败: {result.Message}");
                    return;
                }
            }

            _logger.LogBusiness("单位模板保存成功", ("UnitName", UnitName), ("Mode", IsCreateMode ? "Create" : "Update"));
            IsEditingForm = false;
            IsBrowsing = true;
            await LoadTemplatesAsync();
            await _dialogService.DisplayAlertAsync("成功", IsCreateMode ? "新增成功" : "保存成功", "确定");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存单位模板失败");
            await ShowTipAsync($"保存失败: {ex.Message}");
        }
        finally
        {
            IsSaving = false;
        }
    }

    // 返回上一页：使用基类 GoBackCommand（含栈守卫与窗口标题恢复）

    #endregion

    private async Task ShowTipAsync(string message)
    {
        await _dialogService.DisplayAlertAsync("提示", message, "确定");
    }
}