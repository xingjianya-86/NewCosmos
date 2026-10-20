using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Categories;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Platform;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

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

    /// <summary>模板默认打印份数（编辑中值；1-99，保存只写 config_json 的 copies 键）</summary>
    [ObservableProperty]
    private int _editingCopies = 1;

    /// <summary>
    /// 份数区提示（选中模板/保存后刷新；不放 getter 里解析 JSON——getter 会被 XAML 反复求值）：
    /// 存在分类级份数时说明"分类命中时优先于默认份数"，避免改了不生效的困惑。
    /// </summary>
    [ObservableProperty]
    private string _copiesHint = string.Empty;

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
        // 载入模板默认份数与份数提示；损坏配置由 TemplateCopiesResolver 回退默认值，不抛异常
        EditingCopies = value == null ? 1 : TemplateCopiesResolver.ResolveDefault(value.ConfigJson);
        UpdateCopiesHint(value?.ConfigJson);
    }

    /// <summary>刷新份数提示（选中模板/保存后调用）；损坏或空配置按"未配置分类级份数"显示。</summary>
    private void UpdateCopiesHint(string? configJson)
    {
        var overrides = TemplateCopiesResolver.ClassificationCopies(configJson);
        CopiesHint = overrides.Count > 0
            ? $"该模板已配置 {overrides.Count} 项分类级份数（分类命中时优先于下方默认份数）"
            : "未配置分类级份数：所有分类按下方默认份数打印";
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

            // 批6（2026-10-16）：config 字段键体检——命中仅警告不阻断（人工裁量），防「config 有键、
            // 代码无供给」的占位符残留事故（先例 2026-10-15 {经办人}）与同占位符多键值串复发。
            var configIssues = TemplateFieldKeyValidator.Validate(config);
            if (configIssues.Count > 0)
            {
                _logger.Warn($"模板 config 字段键体检命中 {configIssues.Count} 项（不阻断上传）：{TemplateName}");
                var text = configIssues.Count > 20
                    ? string.Join("\n", configIssues.Take(20)) + $"\n…（共 {configIssues.Count} 项，详见日志）"
                    : string.Join("\n", configIssues);
                await _dialogService.DisplayAlertAsync("配置警告（不阻断上传）", text, "确定");
            }

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
    //  默认打印份数
    // ========================

    /// <summary>
    /// 保存模板默认打印份数：只改 config_json 的 copies 键，
    /// fields/tables/compositeFields/copiesByClassification 等其余配置原样保留（非对象结构拒绝写入）。
    /// 生效优先级见 TemplateConfig.ResolveCopies：分类映射 → copies → 1。
    /// </summary>
    [RelayCommand]
    private async Task SaveCopiesAsync()
    {
        if (SelectedTemplate == null) return;

        var copies = Math.Clamp(EditingCopies, 1, 99);
        if (copies != EditingCopies) EditingCopies = copies;

        var json = UpdateCopiesJson(SelectedTemplate.ConfigJson, copies);
        if (json == null)
        {
            await _dialogService.DisplayAlertAsync("配置异常",
                "该模板配置不是 JSON 对象，无法写入份数；请先用「修改字段映射」修正配置结构", "确定");
            return;
        }

        try
        {
            await _templateService.UpdateConfigJsonAsync(SelectedTemplate.Id, json);
            SelectedTemplate.ConfigJson = json;
            if (IsEditingConfig) EditingConfigJson = json;
            UpdateCopiesHint(json);
            _logger.LogBusiness("模板默认份数修改",
                ("TemplateId", SelectedTemplate.Id),
                ("Copies", copies));
            await _dialogService.DisplayAlertAsync("成功", $"默认份数已保存：{copies} 份", "确定");
        }
        catch (Exception ex)
        {
            _logger.Error($"保存默认份数失败: {ex.Message}");
            await _dialogService.DisplayAlertAsync("错误", $"保存失败: {ex.Message}", "确定");
        }
    }

    /// <summary>只写/改 config_json 的 copies 键；结构非对象或解析失败返回 null（绝不覆盖原配置）。</summary>
    private static string? UpdateCopiesJson(string? configJson, int copies)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return new JsonObject { ["copies"] = copies }.ToJsonString(
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(configJson);
        }
        catch
        {
            return null;
        }

        if (node is not JsonObject obj) return null;

        obj["copies"] = copies;
        return obj.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            // 不用默认编码器：它会把中文占位符转义成 \uXXXX，JSON 面板会变得不可读
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
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
