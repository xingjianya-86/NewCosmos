using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ArchiveManagement;

/// <summary>
/// 每月公示文档输出 ViewModel
/// 公示名单以 5 个导入库为基数，合并当前库在保、排除已退出（月度口径，见 PublicityOutputService）；
/// 按村分组预览名单；按村生成《公共_每月公示名单》文档（每页 22 户，超出自动拆页）。
/// </summary>
public partial class MonthlyPublicityViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPublicityOutputService _publicityService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    #region 公示年月（仅用于文档标题，不过滤数据）

    [ObservableProperty]
    private ObservableCollection<int> _yearOptions = new();

    [ObservableProperty]
    private ObservableCollection<int> _monthOptions = new();

    [ObservableProperty]
    private int _selectedYear;

    [ObservableProperty]
    private int _selectedMonth;

    #endregion

    #region 名单数据

    /// <summary>已加载的家庭行（原始取数）</summary>
    private List<PublicityFamilyRow>? _families;

    /// <summary>按村分组（村 → 户数）</summary>
    [ObservableProperty]
    private ObservableCollection<PublicityVillageGroupItem> _villageGroups = new();

    [ObservableProperty]
    private PublicityVillageGroupItem? _selectedVillage;

    /// <summary>选中村的公示名单（表格展示行）</summary>
    [ObservableProperty]
    private ObservableCollection<PublicityFamilyDisplayRow> _familyItems = new();

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _villageCount;

    #endregion

    #region 输出（按村生成每月公示名单文档）

    /// <summary>输出按钮是否可用（模板已接入，可用）</summary>
    public bool IsOutputAvailable => true;

    /// <summary>输出提示文案</summary>
    public string OutputHint => "按村逐份生成《公共_每月公示名单》（每页 22 户，超出自动拆页）";

    /// <summary>是否正在生成公示文档</summary>
    [ObservableProperty]
    private bool _isGenerating;

    /// <summary>生成结果提示（成功显示输出目录，失败显示错误）</summary>
    [ObservableProperty]
    private string _outputMessage = string.Empty;

    /// <summary>是否有生成结果提示可展示</summary>
    public bool HasOutputMessage => !string.IsNullOrWhiteSpace(OutputMessage);

    partial void OnOutputMessageChanged(string value) => OnPropertyChanged(nameof(HasOutputMessage));

    #endregion

    public MonthlyPublicityViewModel(
        IServiceProvider serviceProvider,
        IPublicityOutputService publicityService,
        IDialogService dialogService,
        ILoggerService logger)
    {
        _serviceProvider = serviceProvider;
        _publicityService = publicityService;
        _dialogService = dialogService;
        _logger = logger;
        Title = "每月公示文档输出";

        var now = DateTime.Now;
        for (var y = now.Year - 5; y <= now.Year + 1; y++)
            YearOptions.Add(y);
        for (var m = 1; m <= 12; m++)
            MonthOptions.Add(m);
        SelectedYear = now.Year;
        SelectedMonth = now.Month;
    }

    /// <summary>按村生成每月公示名单文档（模板：公共_每月公示名单）</summary>
    [RelayCommand]
    private async Task GenerateDocumentsAsync()
    {
        if (IsGenerating) return;
        IsGenerating = true;
        OutputMessage = string.Empty;
        try
        {
            var ct = CancellationToken;
            var result = await _publicityService.GeneratePublicityFilesAsync(SelectedYear, SelectedMonth, ct);
            if (result.IsFailure || result.Value == null)
            {
                OutputMessage = $"生成失败：{result.Message}";
                await _dialogService.DisplayAlertAsync("提示", OutputMessage, "确定");
                return;
            }

            var value = result.Value;
            OutputMessage = $"已生成 {value.VillageCount} 个村、{value.Files.Count} 个文件\n输出目录：{value.OutputDirectory}";
            await _dialogService.DisplayAlertAsync("生成完成", OutputMessage, "确定");
        }
        catch (OperationCanceledException)
        {
            OutputMessage = "生成已取消";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "每月公示名单生成失败");
            OutputMessage = $"生成失败：{ex.Message}";
            await _dialogService.DisplayAlertAsync("提示", OutputMessage, "确定");
        }
        finally
        {
            IsGenerating = false;
        }
    }

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        if (!HasData)
            await LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("每月公示-加载名单");

            var result = await _publicityService.GetPublicityFamiliesAsync(ct);
            if (result.IsFailure || result.Value == null)
            {
                await _dialogService.DisplayAlertAsync("提示", $"加载公示名单失败：{result.Message}", "确定");
                return;
            }

            _families = result.Value;
            var groups = _publicityService.GroupByVillage(_families);

            VillageGroups.Clear();
            foreach (var g in groups)
            {
                VillageGroups.Add(new PublicityVillageGroupItem(g));
            }

            TotalCount = _families.Count;
            VillageCount = groups.Count;
            HasData = TotalCount > 0;

            if (VillageGroups.Count > 0)
                SelectedVillage = VillageGroups[0];

            _logger.LogBusiness("每月公示-名单加载完成", ("Total", TotalCount), ("Villages", VillageCount));
        }, "正在加载公示名单...");
    }

    partial void OnSelectedVillageChanged(PublicityVillageGroupItem? value)
    {
        FamilyItems.Clear();
        if (value == null) return;

        var index = 1;
        foreach (var f in value.Group.Families)
        {
            FamilyItems.Add(new PublicityFamilyDisplayRow(
                index++,
                f.ApplicantName,
                f.Category,
                f.GuaranteeSize,
                f.BaseAmount,
                f.ClassifiedAmount,
                f.CareCost,
                f.TotalAmount,
                f.Remark));
        }
    }

    private async Task ShowTipAsync(string message)
    {
        await _dialogService.DisplayAlertAsync("提示", message, "确定");
    }
}

/// <summary>村分组展示项</summary>
public sealed class PublicityVillageGroupItem
{
    public PublicityVillageGroup Group { get; }
    public string Village => Group.Village;
    public int Count => Group.Count;
    public string Display => $"{Group.Village}  （{Group.Count} 户）";

    public PublicityVillageGroupItem(PublicityVillageGroup group)
    {
        Group = group;
    }
}

/// <summary>公示名单表格展示行（不含身份证号，公示场景隐私合规）</summary>
public sealed record PublicityFamilyDisplayRow(
    int Index,
    string Name,
    string Category,
    int GuaranteeSize,
    decimal BaseAmount,
    decimal ClassifiedAmount,
    decimal CareCost,
    decimal TotalAmount,
    string Remark);
