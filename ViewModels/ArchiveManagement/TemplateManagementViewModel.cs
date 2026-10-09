using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Categories;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Platform;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ArchiveManagement;

public enum TemplatePageMode
{
    Browsing,
    Uploading
}

public partial class TemplateManagementViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly ITemplateService _templateService = null!;
    private readonly IDialogService _dialogService = null!;

    private readonly ILoggerService _logger = null!;
    private readonly IFileService _fileService = null!;

    [ObservableProperty]
    private TemplatePageMode _pageMode = TemplatePageMode.Browsing;

    public bool IsBrowsing => PageMode == TemplatePageMode.Browsing;
    public bool IsUploadFormVisible => PageMode == TemplatePageMode.Uploading;

    [ObservableProperty]
    private ObservableCollection<Template> _templates = new();

    [ObservableProperty]
    private Template _selectedTemplate = new();

    [ObservableProperty]
    private bool _hasSelectedTemplate;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<TemplateCategoryNode> _flattenedNodes = new();

    private readonly List<TemplateCategoryNode> _rootNodes;

    [ObservableProperty]
    private string _selectedFilePath = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _templateName = string.Empty;

    [ObservableProperty]
    private string _templateType = "Word";

    [ObservableProperty]
    private string _configJson = string.Empty;

    [ObservableProperty]
    private bool _isUploading;

    [ObservableProperty]
    private bool _isEditingConfig;

    [ObservableProperty]
    private string _editingConfigJson = string.Empty;

    [ObservableProperty]
    private bool _isEditingCategories;

    public bool IsNotEditing => !IsEditingConfig && !IsEditingCategories;

    public List<string> TemplateTypes { get; } = new() { "Word", "Excel" };

    public TemplateManagementViewModel(
        IServiceProvider serviceProvider,
        ITemplateService templateService,
        IDialogService dialogService,
        ILoggerService logger,
        IFileService fileService)
    {
        _serviceProvider = serviceProvider;
        _templateService = templateService;
        _dialogService = dialogService;
        _logger = logger;
        _fileService = fileService;
        Title = "模板管理";

        _rootNodes = TemplateCategoryProvider.BuildCategoryTree();
        foreach (var node in _rootNodes)
            SubscribeExpandToggle(node);
        BuildFlatList();
    }

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    public override async Task OnAppearingAsync()
    {
        await LoadTemplatesAsync();
    }

    partial void OnSelectedTemplateChanged(Template value)
    {
        HasSelectedTemplate = value != null;
    }

    partial void OnIsEditingConfigChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotEditing));
    }

    partial void OnIsEditingCategoriesChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotEditing));
    }

    // ========================
    //  模板列表
    // ========================

    [RelayCommand]
    private async Task LoadTemplatesAsync()
    {
        try
        {
            var allResult = await _templateService.GetAllAsync(CancellationToken);
            if (allResult.IsFailure)
                throw new BusinessException(allResult.ErrorCode!, allResult.Message!);
            var filtered = (allResult.Value ?? new List<NewCosmos.Models.Entities.Template>()).AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                filtered = filtered.Where(t =>
                    t.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            }

            Templates.Clear();
            foreach (var t in filtered)
                Templates.Add(t);
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败");
            await _dialogService.DisplayAlertAsync("错误", $"加载失败: {ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadTemplatesAsync();
    }

    [RelayCommand]
    private void Search()
    {
        SafeFireAndForget(LoadTemplatesAsync);
    }

    // ========================
    //  分类树
    // ========================

    [RelayCommand]
    private void ToggleExpand(TemplateCategoryNode node)
    {
        if (node == null) return;
        node.IsExpanded = !node.IsExpanded;
        BuildFlatList();
    }

    private void BuildFlatList()
    {
        FlattenedNodes.Clear();
        foreach (var root in _rootNodes)
        {
            FlattenNode(root, 0);
        }
    }

    private void FlattenNode(TemplateCategoryNode node, int depth)
    {
        node.Depth = depth;
        FlattenedNodes.Add(node);
        if (node.IsExpanded)
        {
            foreach (var child in node.Children)
            {
                FlattenNode(child, depth + 1);
            }
        }
    }

    private void SubscribeExpandToggle(TemplateCategoryNode node)
    {
        node.OnExpandToggled = BuildFlatList;
        foreach (var child in node.Children)
            SubscribeExpandToggle(child);
    }

    // ========================
    //  页面模式切换
    // ========================

    [RelayCommand]
    private void ToggleUploadForm()
    {
        if (PageMode == TemplatePageMode.Uploading)
        {
            PageMode = TemplatePageMode.Browsing;
        }
        else
        {
            PageMode = TemplatePageMode.Uploading;
            ResetUploadForm();
        }
        OnPropertyChanged(nameof(IsBrowsing));
        OnPropertyChanged(nameof(IsUploadFormVisible));
    }

    private void ResetUploadForm()
    {
        SelectedFilePath = string.Empty;
        FileName = string.Empty;
        TemplateName = string.Empty;
        TemplateType = "Word";
        ConfigJson = string.Empty;
        IsUploading = false;
        UncheckAllNodes();
    }

    private void UncheckAllNodes()
    {
        foreach (var node in _rootNodes.SelectMany(n => n.Flatten()))
            node.IsChecked = false;
    }

    // ========================
    //  上传模板
    // ========================

    private static readonly string[] ValidTemplateExtensions = TemplateFileTypes.ValidExtensions;

    [RelayCommand]
    private async Task SelectFileAsync()
    {
        var path = await PickFileWithFeedbackAsync("选择模板文件", ValidTemplateExtensions);
        if (string.IsNullOrEmpty(path)) return;

        SelectedFilePath = path;
        FileName = Path.GetFileName(path);
        TemplateName = Path.GetFileNameWithoutExtension(path);
        TemplateType = path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
            ? TemplateFileTypes.Word : TemplateFileTypes.Excel;
    }

    [RelayCommand]
    private async Task UploadAsync()
    {
        if (string.IsNullOrEmpty(SelectedFilePath))
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择模板文件", "确定");
            return;
        }

        if (string.IsNullOrWhiteSpace(TemplateName))
        {
            await _dialogService.DisplayAlertAsync("提示", "请输入模板名称", "确定");
            return;
        }

        IsUploading = true;
        try
        {
            var fileBytes = await _fileService.ReadAllBytesAsync(SelectedFilePath);
            var categoryArray = _rootNodes
                .SelectMany(n => n.GetSelectedPaths())
                .ToArray();

            var config = string.IsNullOrWhiteSpace(ConfigJson) ? null : ConfigJson;

            await _templateService.SaveAsync(TemplateName, TemplateType, fileBytes, categoryArray, config!);

            await _dialogService.DisplayAlertAsync("成功", "模板上传成功", "确定");

            PageMode = TemplatePageMode.Browsing;
            OnPropertyChanged(nameof(IsBrowsing));
            OnPropertyChanged(nameof(IsUploadFormVisible));
            await LoadTemplatesAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败");
            await _dialogService.DisplayAlertAsync("错误", $"上传失败: {ex.Message}", "确定");
        }
        finally
        {
            IsUploading = false;
        }
    }

    // ========================
    //  下载 / 删除
    // ========================

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (SelectedTemplate == null) return;

        try
        {
            var folder = await PickExportFolderAsync("选择模板保存位置");
            if (string.IsNullOrEmpty(folder)) return;

            var ext = TemplateFileTypes.ToExtension(SelectedTemplate.FileType);
            var fileName = $"{SelectedTemplate.Name}{ext}";
            var filePath = await _templateService.ExportTemplateAsync(SelectedTemplate.Id, folder, fileName);

            await ShowExportSuccessAsync(folder, new[] { System.IO.Path.GetFileName(filePath) });
        }
        catch (Exception ex)
        {
            _logger.Error($"导出模板失败: {ex.Message}");
            await _dialogService.DisplayAlertAsync("错误", $"导出失败: {ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedTemplate == null) return;

        var confirm = await _dialogService.DisplayAlertAsync("确认删除",
            $"确定要删除模板\"{SelectedTemplate.Name}\" 吗？", "删除", "确定");

            if (!confirm) return;

        try
        {
            var templateId = SelectedTemplate.Id;
            var result = await _templateService.DeleteAsync(templateId);
            if (result.IsFailure)
            {
                await ShowFailureAsync(result, "删除模板");
                return;
            }

            _logger.LogBusiness("模板删除成功", ("TemplateId", templateId.ToString()));
            SelectedTemplate = null;
            HasSelectedTemplate = false;
            await LoadTemplatesAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败");
            await _dialogService.DisplayAlertAsync("错误", $"删除失败: {ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private async Task ReplaceFileAsync()
    {
        if (SelectedTemplate == null) return;

        var path = await PickFileWithFeedbackAsync("选择替换模板文件", ValidTemplateExtensions);
        if (string.IsNullOrEmpty(path)) return;

        var fileName = Path.GetFileName(path);
        var confirm = await _dialogService.DisplayAlertAsync("确认替换",
            $"确定要用 \"{fileName}\" 替换模板 \"{SelectedTemplate.Name}\" 的文件吗",
            "替换", "取消");
        if (!confirm) return;

        try
        {
            var fileBytes = await _fileService.ReadAllBytesAsync(path);
            var fileType = path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
                ? TemplateFileTypes.Word : TemplateFileTypes.Excel;

            await _templateService.ReplaceFileAsync(SelectedTemplate.Id, fileBytes, fileType);

            await _dialogService.DisplayAlertAsync("成功", "模板文件已替换成功", "确定");
            await LoadTemplatesAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"替换模板文件失败: {ex.Message}");
            await _dialogService.DisplayAlertAsync("错误", $"替换失败: {ex.Message}", "确定");
        }
    }

    // ========================
    //  配置编辑
    // ========================

    [RelayCommand]
    private void EditConfig()
    {
        if (SelectedTemplate == null) return;
        EditingConfigJson = SelectedTemplate.ConfigJson ?? string.Empty;
        IsEditingConfig = true;
    }

    [RelayCommand]
    private async Task SaveConfigAsync()
    {
        if (SelectedTemplate == null) return;

        if (!string.IsNullOrWhiteSpace(EditingConfigJson))
        {
            try
            {
                var _ = NewCosmos.Services.Templates.TemplateConfig.FromJson(EditingConfigJson);
            }
            catch
            {
                await _dialogService.DisplayAlertAsync("格式错误", "JSON 格式不正确，请检查语法", "确定");
                return;
            }
        }

        try
        {
            await _templateService.UpdateConfigJsonAsync(SelectedTemplate.Id, EditingConfigJson);
            SelectedTemplate.ConfigJson = EditingConfigJson;
            IsEditingConfig = false;
            await _dialogService.DisplayAlertAsync("成功", "字段映射配置已保存", "确定");
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败");
            await _dialogService.DisplayAlertAsync("错误", $"保存失败: {ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private void CancelEditConfig()
    {
        IsEditingConfig = false;
        EditingConfigJson = string.Empty;
    }

    // ========================
    //  分类编辑
    // ========================

    [RelayCommand]
    private void EditCategories()
    {
        if (SelectedTemplate == null) return;

        UncheckAllNodes();

        if (SelectedTemplate.Categories != null)
        {
            foreach (var cat in SelectedTemplate.Categories)
            {
                foreach (var node in _rootNodes.SelectMany(n => n.Flatten()))
                {
                    if (node.Path == cat)
                        node.IsChecked = true;
                }
            }
        }

        IsEditingCategories = true;
    }

    [RelayCommand]
    private async Task SaveCategoriesAsync()
    {
        if (SelectedTemplate == null) return;

        var selectedCategories = _rootNodes
            .SelectMany(n => n.GetSelectedPaths())
            .ToArray();

        try
        {
            await _templateService.UpdateCategoriesAsync(SelectedTemplate.Id, selectedCategories);
            SelectedTemplate.Categories = selectedCategories;
            IsEditingCategories = false;
            await LoadTemplatesAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败");
            await _dialogService.DisplayAlertAsync("错误", $"保存分类失败: {ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private void CancelEditCategories()
    {
        IsEditingCategories = false;
        UncheckAllNodes();
    }

    [RelayCommand]
    private async Task MoveUpAsync()
    {
        if (SelectedTemplate == null) return;
        var idx = Templates.IndexOf(SelectedTemplate);
        if (idx <= 0) return;

        // 交换 sort_order
        var prev = Templates[idx - 1];
        var tmpOrder = SelectedTemplate.SortOrder;
        SelectedTemplate.SortOrder = prev.SortOrder;
        prev.SortOrder = tmpOrder;

        try
        {
            await _templateService.ReorderAsync(SelectedTemplate.Id, SelectedTemplate.SortOrder);
            await _templateService.ReorderAsync(prev.Id, prev.SortOrder);
            Templates.Move(idx, idx - 1);
        }
        catch (Exception ex)
        {
            // ReorderAsync 失败现在会抛异常（不再静默丢失）——还原内存中的交换并提示
            prev.SortOrder = SelectedTemplate.SortOrder;
            SelectedTemplate.SortOrder = tmpOrder;
            _logger.LogError(ex, "模板排序保存失败");
            await _dialogService.DisplayAlertAsync("排序失败", $"排序保存失败: {ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private async Task MoveDownAsync()
    {
        if (SelectedTemplate == null) return;
        var idx = Templates.IndexOf(SelectedTemplate);
        if (idx < 0 || idx >= Templates.Count - 1) return;

        var next = Templates[idx + 1];
        var tmpOrder = SelectedTemplate.SortOrder;
        SelectedTemplate.SortOrder = next.SortOrder;
        next.SortOrder = tmpOrder;

        try
        {
            await _templateService.ReorderAsync(SelectedTemplate.Id, SelectedTemplate.SortOrder);
            await _templateService.ReorderAsync(next.Id, next.SortOrder);
            Templates.Move(idx, idx + 1);
        }
        catch (Exception ex)
        {
            next.SortOrder = SelectedTemplate.SortOrder;
            SelectedTemplate.SortOrder = tmpOrder;
            _logger.LogError(ex, "模板排序保存失败");
            await _dialogService.DisplayAlertAsync("排序失败", $"排序保存失败: {ex.Message}", "确定");
        }
    }
}
