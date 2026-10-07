using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.AssetVerification;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.Platform;
using NewCosmos.Services.System;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.AssetVerification;

public partial class MonthlyAssetAuditViewModel : ViewModelBase
{
    private readonly IAssetVerificationService _verificationService;
    private readonly IPdfVerificationService _pdfVerificationService;
    private readonly IDialogService _dialogService;
    private readonly IFileService _fileService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDictCacheService _dictCacheService;

    #region 属性

    // 共享年月选择
    [ObservableProperty] private int _selectedYear;
    [ObservableProperty] private int? _selectedMonth;
    [ObservableProperty] private string _periodText = string.Empty;

    // 报表周期统计（从数据库查询）
    [ObservableProperty] private int _submittedCount;
    [ObservableProperty] private int _hasReportCount;
    [ObservableProperty] private int _archivedCount;
    [ObservableProperty] private int _rejectedCount;
    [ObservableProperty] private int _totalCount;

    // 批量上传统计（上传操作结果）
    [ObservableProperty] private int _uploadPendingCount;
    [ObservableProperty] private int _uploadSuccessCount;
    [ObservableProperty] private int _uploadErrorCount;
    [ObservableProperty] private bool _hasUploadResults;
    [ObservableProperty] private int _uploadProgress;
    [ObservableProperty] private int _uploadTotal;
    [ObservableProperty] private string _uploadProgressText = string.Empty;
    [ObservableProperty] private bool _isUploading;

    public double UploadProgressPercent => UploadTotal > 0 ? (double)UploadProgress / UploadTotal : 0;

    partial void OnUploadProgressChanged(int value)
    {
        OnPropertyChanged(nameof(UploadProgressPercent));
    }

    partial void OnUploadTotalChanged(int value)
    {
        OnPropertyChanged(nameof(UploadProgressPercent));
    }

    // 一对一匹配
    [ObservableProperty] private AssetVerificationTask? _selectedMatchTask;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPrintReport))]
    private bool _hasSelectedMatchTask;
    [ObservableProperty] private string _matchInfoText = string.Empty;
    [ObservableProperty] private string _pdfPreviewUrl = string.Empty;
    [ObservableProperty] private bool _hasPdfPreview;

    /// <summary>
    /// 打印按钮可用：已选中待核查人员且无其他操作占用忙碌状态
    /// </summary>
    public bool CanPrintReport => HasSelectedMatchTask && !IsBusy;

    protected override void OnBusyStateChanged()
    {
        OnPropertyChanged(nameof(CanPrintReport));
    }

    // 待核查人员搜索（姓名/身份证号关键字）
    [ObservableProperty] private string _searchKeyword = string.Empty;

    // 选项列表
    public List<int> YearOptions { get; } = Enumerable.Range(DateTime.Now.Year - 5, 11).ToList();
    public List<int?> MonthOptions { get; } = new List<int?> { null, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

    // 集合
    public ObservableCollection<UploadResultItem> UploadResults { get; } = new();
    public ObservableCollection<AssetVerificationTask> MatchTasks { get; } = new();

    #endregion

    public MonthlyAssetAuditViewModel(
        IAssetVerificationService verificationService,
        IPdfVerificationService pdfVerificationService,
        IDialogService dialogService,
        IFileService fileService,
        IServiceProvider serviceProvider,
        IDictCacheService dictCacheService,
        ILoggerService logger)
    {
        _verificationService = verificationService;
        _pdfVerificationService = pdfVerificationService;
        _dialogService = dialogService;
        _fileService = fileService;
        _serviceProvider = serviceProvider;
        _dictCacheService = dictCacheService;
        Logger = logger;

        Title = "上传核查报告";

        // 默认选中当前月
        _selectedYear = DateTime.Now.Year;
        _selectedMonth = DateTime.Now.Month;
    }

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger { get; }

    #region 初始化

    public async Task InitializeAsync()
    {
        UpdatePeriodText();
        await LoadMatchTasksAsync();
        await LoadUploadStatsAsync();
    }

    #endregion

    #region 年月变化

    // 年月切换触发的刷新取消源：新触发先取消旧刷新，避免并发刷新竞争同一集合
    private CancellationTokenSource? _refreshCts;

    private CancellationToken ResetRefreshToken()
    {
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        return _refreshCts.Token;
    }

    partial void OnSelectedYearChanged(int value)
    {
        Logger.Debug($"报表周期年份变更: {value}");
        UpdatePeriodText();
        _ = RefreshDataAsync(ResetRefreshToken());
    }

    partial void OnSelectedMonthChanged(int? value)
    {
        Logger.Debug($"报表周期月份变更: {value}");
        UpdatePeriodText();
        _ = RefreshDataAsync(ResetRefreshToken());
    }

    private async Task RefreshDataAsync(CancellationToken token = default)
    {
        try
        {
            // 顺序加载，避免并行查询导致UI卡顿
            await LoadUploadStatsAsync(token);
            await LoadMatchTasksAsync(token);
        }
        catch (OperationCanceledException)
        {
            // 被更新的刷新请求取消，静默处理
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "刷新数据失败");
        }
    }

    private void UpdatePeriodText()
    {
        var (start, end) = ALinePeriodHelper.GetPeriod(SelectedYear, SelectedMonth ?? 1);
        // end 是排他上界（半开区间），展示"最后一天"用 end 前一天（本月10日）
        PeriodText = SelectedMonth.HasValue
            ? $"A线周期：{start:yyyy-MM-dd} ~ {end.AddDays(-1):yyyy-MM-dd}"
            : $"A线年份：{SelectedYear}年";
    }

    #endregion

    #region 批量上传

    [RelayCommand]
    private async Task BatchUploadAsync()
    {
        try
        {
            var folderPath = await PickExportFolderAsync("选择包含PDF核查报告的文件夹", cancelMessage: "已取消选择");
            if (string.IsNullOrEmpty(folderPath)) return;

            var pdfFiles = Directory.GetFiles(folderPath, "*.pdf");
            if (pdfFiles.Length == 0)
            {
                await _dialogService.DisplayAlertAsync("提示", "所选文件夹中没有PDF文件", "确定");
                return;
            }

            UploadResults.Clear();
            HasUploadResults = true;
            UploadPendingCount = 0;
            UploadSuccessCount = 0;
            UploadErrorCount = 0;
            UploadTotal = pdfFiles.Length;
            UploadProgress = 0;
            IsUploading = true;

            for (int i = 0; i < pdfFiles.Length; i++)
            {
                var pdfFile = pdfFiles[i];
                var fileName = Path.GetFileNameWithoutExtension(pdfFile);
                var matchName = MatchNameFromFileName(fileName);

                UploadProgressText = $"扫描中 {i + 1}/{pdfFiles.Length} - {fileName}";

                var result = new UploadResultItem
                {
                    FileName = fileName,
                    MatchName = matchName,
                    FilePath = pdfFile
                };

                // 尝试匹配申请人
                var searchResult = await _verificationService.SearchByNameOrIdCardAsync(matchName);
                if (searchResult.IsSuccess && searchResult.Value != null && searchResult.Value.Count > 0)
                {
                    // 多条结果时让用户选择
                    AssetVerificationTask? match = null;
                    if (searchResult.Value.Count == 1)
                    {
                        match = searchResult.Value[0];
                    }
                    else
                    {
                        // 多条候选，让用户选择
                        var candidateNames = searchResult.Value
                            .Select((t, idx) => $"{idx + 1}. {t.ArchiveName}（{DataMasker.MaskIdCard(t.ArchiveIdCard)}）")
                            .ToArray();
                        var selected = await _dialogService.DisplayActionSheetAsync(
                            $"「{matchName}」匹配到多条记录，请选择", "取消", null, candidateNames);

                        if (!string.IsNullOrEmpty(selected))
                        {
                            // 解析用户选择的序号
                            var indexStr = selected.Split('.')[0];
                            if (int.TryParse(indexStr, out var selectedIndex) && selectedIndex >= 1 && selectedIndex <= searchResult.Value.Count)
                            {
                                match = searchResult.Value[selectedIndex - 1];
                            }
                        }
                    }

                    if (match == null)
                    {
                        // 用户取消选择
                        result.IsMatched = false;
                        result.StatusIcon = "✗";
                        result.StatusText = "已跳过";
                        result.StatusColor = "#6B7280";
                    }
                    else
                    {
                        result.IsMatched = true;
                        result.MatchedName = match.ArchiveName;
                        result.CheckId = match.Id;
                        result.BatchId = match.BatchId;
                        result.ApplicantName = match.ArchiveName;
                        result.ApplicantIdCard = match.ArchiveIdCard;

                        // 检查是否是更新的报告
                        var pdfBytes = await File.ReadAllBytesAsync(pdfFile);
                        var fileHash = ComputeFileHash(pdfBytes);
                        var existsResult = await _pdfVerificationService.ReportExistsByFileHashAsync(fileHash);

                        if (existsResult.IsSuccess && existsResult.Value == true)
                        {
                            result.StatusIcon = "○";
                            result.StatusText = "未变更";
                            result.StatusColor = "#6B7280";
                            UploadSuccessCount++;
                        }
                        else
                        {
                            // 检查是否已有报告（有则为更新）
                            var hasReport = await _pdfVerificationService.ReportExistsByCheckIdAsync(match.Id);
                            if (hasReport.IsSuccess && hasReport.Value == true)
                            {
                                result.IsUpdate = true;
                                result.StatusIcon = "↻";
                                result.StatusText = "待更新";
                                result.StatusColor = "#2563EB";
                                UploadPendingCount++;
                            }
                            else
                            {
                                result.StatusIcon = "✓";
                                result.StatusText = "待上传";
                                result.StatusColor = "#059669";
                                UploadPendingCount++;
                            }
                        }
                    }
                }
                else
                {
                    result.IsMatched = false;
                    result.StatusIcon = "✗";
                    result.StatusText = "未匹配";
                    result.StatusColor = "#DC2626";
                    UploadErrorCount++;
                }

                UploadResults.Add(result);
                UploadProgress = i + 1;
            }

            // 上传匹配成功的文件（排除未变更的）
            var toUpload = UploadResults.Where(r => r.IsMatched && r.StatusText != "未变更").ToList();
            if (toUpload.Count > 0)
            {
                var updateCount = toUpload.Count(r => r.IsUpdate);
                var newCount = toUpload.Count - updateCount;
                var msg = $"找到 {toUpload.Count} 个需要上传的文件";
                if (updateCount > 0) msg += $"（{newCount} 新增，{updateCount} 更新）";
                msg += "，是否上传？";

                var confirm = await _dialogService.DisplayAlertAsync("确认", msg, "上传", "取消");

                if (confirm)
                {
                    UploadPendingCount = toUpload.Count;
                    UploadSuccessCount = 0;
                    UploadErrorCount = 0;

                    for (int i = 0; i < toUpload.Count; i++)
                    {
                        var item = toUpload[i];
                        UploadProgressText = $"上传中 {i + 1}/{toUpload.Count} - {item.FileName}";

                        try
                        {
                            await UploadSingleReportAsync(item);
                            UploadSuccessCount++;
                        }
                        catch (Exception)
                        {
                            UploadErrorCount++;
                            item.StatusIcon = "✗";
                            item.StatusText = "上传失败";
                            item.StatusColor = "#DC2626";
                        }

                        UploadProgress = i + 1;
                    }

                    await _dialogService.DisplayAlertAsync("完成",
                        $"上传完成，成功 {UploadSuccessCount} 个，失败 {UploadErrorCount} 个", "确定");

                    await LoadMatchTasksAsync();
                    await LoadUploadStatsAsync();
                }
            }
            else
            {
                await _dialogService.DisplayAlertAsync("提示", "所有文件均已存在或未匹配", "确定");
            }

            IsUploading = false;
            UploadProgressText = string.Empty;
        }
        catch (Exception ex)
        {
            IsUploading = false;
            UploadProgressText = string.Empty;
            await _dialogService.DisplayAlertAsync("错误", $"批量上传失败: {ex.Message}", "确定");
        }
    }

    private async Task UploadSingleReportAsync(UploadResultItem item)
    {
        var pdfBytes = await File.ReadAllBytesAsync(item.FilePath);
        var fileHash = ComputeFileHash(pdfBytes);

        // 检查是否已存在（哈希去重）：即使已存在也按当前身份补更核查状态
        var existsResult = await _pdfVerificationService.ReportExistsByFileHashAsync(fileHash);
        if (existsResult.IsSuccess && existsResult.Value == true)
        {
            item.StatusIcon = "✓";
            item.StatusText = "已存在";
            item.StatusColor = "#D97706";

            if (item.CheckId.HasValue)
                await UpdateCheckStatusByIdentityAsync(item.CheckId.Value, item.ApplicantName, item.ApplicantIdCard);
            return;
        }

        if (!item.CheckId.HasValue)
        {
            item.StatusIcon = "✗";
            item.StatusText = "缺少核查ID";
            item.StatusColor = "#DC2626";
            return;
        }

        // 上传报告
        await _pdfVerificationService.UploadReportAsync(
            item.CheckId.Value,
            item.BatchId,
            item.ApplicantName,
            item.ApplicantIdCard,
            item.FileName + ".pdf",
            pdfBytes,
            fileHash,
            true,
            null);

        // 更新状态：按低收入身份判定 已纳入(2)/有报告(1)，与 PDF 批量验证流程口径一致
        await UpdateCheckStatusByIdentityAsync(item.CheckId.Value, item.ApplicantName, item.ApplicantIdCard);

        item.StatusIcon = "✓";
        item.StatusText = "已上传";
        item.StatusColor = "#059669";
    }

    /// <summary>
    /// 按申请人当前低收入身份更新核查状态：
    /// 已具有低保/边缘/特困身份 → 已纳入(INCLUDED)；否则 → 有报告未建档(VERIFIED)。
    /// 与 PdfVerificationViewModel 上传判定逻辑保持同一口径。
    /// </summary>
    private async Task UpdateCheckStatusByIdentityAsync(long checkId, string applicantName, string applicantIdCard)
    {
        var hasLowIncome = await _pdfVerificationService.HasLowIncomeIdentityAsync(applicantName, applicantIdCard);
        var targetStatus = (hasLowIncome.IsSuccess && hasLowIncome.Value)
            ? AssetCheckStatusConstants.INCLUDED
            : AssetCheckStatusConstants.VERIFIED;
        await _pdfVerificationService.UpdateCheckStatusAsync(checkId, targetStatus);
    }

    private static string MatchNameFromFileName(string fileName)
    {
        // 去掉扩展名
        var name = Path.GetFileNameWithoutExtension(fileName);
        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
            return name;

        // 两种格式都是：第一部分_姓名_第三部分
        // 第一部分是数字ID，第二部分是姓名
        return parts[1];
    }

    private static string ComputeFileHash(byte[] data)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(data);
        return Convert.ToHexString(hash).ToLower();
    }

    #endregion

    #region 一对一精细比对

    partial void OnSelectedMatchTaskChanged(AssetVerificationTask? value)
    {
        HasSelectedMatchTask = value != null;

        if (value != null)
        {
            MatchInfoText = $"申请人：{value.ArchiveName}  |  身份证：{value.ArchiveIdCard}";
            _ = LoadExistingReportAsync(value.Id);
        }
        else
        {
            MatchInfoText = string.Empty;
            HasPdfPreview = false;
            PdfPreviewUrl = string.Empty;
        }
    }

    [RelayCommand]
    private void SelectMatchTask(AssetVerificationTask task)
    {
        if (task == null) return;
        SelectedMatchTask = task;
        Logger.Debug($"手动选中: {task.ArchiveName}");
    }

    /// <summary>
    /// 不予认定：将"有报告未建档"(状态'1')的整户核查申请标记为 '3'（不予认定），
    /// 完成核查状态闭环，使待建档清单真实清零
    /// </summary>
    [RelayCommand]
    private async Task RejectCheckAsync(AssetVerificationTask task)
    {
        if (task == null) return;

        if (task.Status != "1")
        {
            await _dialogService.DisplayAlertAsync("提示", "仅\"有报告未建档\"状态的核查申请可执行不予认定", "确定");
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync("不予认定",
            $"确定将 {task.ArchiveName}（{task.ArchiveIdCard}）整户核查申请标记为\"不予认定\"吗？\n标记后该户将不再出现在待建档清单中。",
            "确定", "取消");
        if (!confirm) return;

        try
        {
            var result = await _verificationService.UpdateCheckStatusAsync(task.Id, "3");
            if (result.IsSuccess)
            {
                Logger.LogBusiness("核查申请不予认定", ("CheckId", task.Id), ("Name", task.ArchiveName));
                if (SelectedMatchTask?.Id == task.Id)
                    SelectedMatchTask = null;
                await RefreshDataAsync(ResetRefreshToken());
            }
            else
            {
                await _dialogService.DisplayAlertAsync("错误", $"操作失败: {result.Message}", "确定");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "不予认定操作失败");
            await _dialogService.DisplayAlertAsync("错误", $"操作失败: {ex.Message}", "确定");
        }
    }

    private async Task LoadExistingReportAsync(long checkId)
    {
        try
        {
            Logger.Debug($"尝试加载核查ID={checkId}的已有报告");

            var result = await _pdfVerificationService.GetReportDataByCheckIdAsync(checkId);
            if (result.IsSuccess && result.Value != null)
            {
                Logger.Debug($"找到报告数据，大小={result.Value.Length}字节");

                // 存储到应用临时目录（避免污染桌面）
                var previewDir = OutputPathHelper.GetTempDirectory();
                Directory.CreateDirectory(previewDir);

                // 清理前一天的旧预览文件
                var yesterday = DateTime.Today.AddDays(-1);
                foreach (var oldFile in Directory.GetFiles(previewDir, "cosmos_preview_*.pdf"))
                {
                    if (File.GetCreationTime(oldFile) <= yesterday)
                        File.Delete(oldFile);
                }

                var filePath = Path.Combine(previewDir, $"cosmos_preview_{checkId}.pdf");
                await File.WriteAllBytesAsync(filePath, result.Value);

                var fileExists = File.Exists(filePath);
                Logger.Debug($"PDF文件写入: {filePath}, 文件存在: {fileExists}, 大小: {new FileInfo(filePath).Length}");

                PdfPreviewUrl = filePath;
                HasPdfPreview = true;
                MatchInfoText += "  |  已加载历史报告";
                Logger.Debug($"PDF预览路径已设置: {PdfPreviewUrl}, HasPdfPreview: {HasPdfPreview}");
            }
            else
            {
                Logger.Debug($"未找到核查ID={checkId}的报告: {result.Message}");
                HasPdfPreview = false;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "加载报告预览失败");
            HasPdfPreview = false;
        }
    }

    #endregion

    #region 打印核查报告

    /// <summary>
    /// 打印选中人员的已上传核查报告（nc_biz_asset_check_reports 中的 PDF）。
    /// 报告先落输出根 {输出根}\核查报告\（打印=生成+落盘+调打印，与四件套口径一致），再走系统默认 PDF 应用（verb=print）打印。
    /// 选中人员必已上传报告（右侧有 PDF 预览）；无报告时兜底提示。
    /// </summary>
    [RelayCommand]
    private async Task PrintSelectedAsync()
    {
        if (SelectedMatchTask == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择待核查人员", "确定");
            return;
        }

        var task = SelectedMatchTask;

        await ExecuteAsync(async ct =>
        {
            var reportPath = await CreateReportFileAsync(task, ct);
            if (reportPath == null) return;

            await ShellPrintPdfAsync(reportPath);

            Logger.LogBusiness("打印核查报告",
                ("CheckId", task.Id),
                ("Name", DataMasker.MaskName(task.ArchiveName)));
        });
    }

    /// <summary>
    /// 「保存」：取选中人员的已上传核查报告，落 {输出根}\核查报告\（不打印），完成后打开目录。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPrintReport))]
    private async Task SaveReportAsync()
    {
        if (SelectedMatchTask == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择待核查人员", "确定");
            return;
        }

        var task = SelectedMatchTask;

        await ExecuteAsync(async ct =>
        {
            var reportPath = await CreateReportFileAsync(task, ct);
            if (reportPath == null) return;

            await ShowExportSuccessAsync(Path.GetDirectoryName(reportPath)!, new[] { Path.GetFileName(reportPath) });

            Logger.LogBusiness("保存核查报告",
                ("CheckId", task.Id),
                ("Name", DataMasker.MaskName(task.ArchiveName)));
        });
    }

    /// <summary>
    /// 取选中人员的核查报告并落 {输出根}\核查报告\；无报告时弹提示并返回 null（打印/保存共用）。
    /// </summary>
    private async Task<string?> CreateReportFileAsync(AssetVerificationTask task, CancellationToken ct)
    {
        var reportResult = await _pdfVerificationService.GetReportDataByCheckIdAsync(task.Id, ct);
        if (reportResult.IsFailure || reportResult.Value is not { Length: > 0 })
        {
            await _dialogService.DisplayAlertAsync("提示", reportResult.Message ?? "该人员暂无已上传的核查报告", "确定");
            return null;
        }

        var reportDir = Path.Combine(OutputPathHelper.OutputRoot, "核查报告");
        Directory.CreateDirectory(reportDir);
        var reportPath = Path.Combine(reportDir, $"核查报告_{DateTime.Now:yyyyMMddHHmmssfff}.pdf");
        await _fileService.WriteAllBytesAsync(reportPath, reportResult.Value, ct);
        return reportPath;
    }

    /// <summary>
    /// 使用系统默认 PDF 应用打印核查报告。
    /// 首选 verb=print 静默打印；系统未注册 print 动词（Win32 1155）时回退为默认阅读器打开并提示用户手动打印。
    /// </summary>
    private async Task ShellPrintPdfAsync(string pdfPath)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(pdfPath)
            {
                UseShellExecute = true,
                Verb = "print"
            };
            using var process = System.Diagnostics.Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1155)
        {
            // 无 .pdf 的 print 动词关联：回退为默认程序打开，由用户手动打印
            Logger.Warn($"PDF print verb 不可用，回退为默认阅读器打开: {pdfPath}");
            try
            {
                using var fallback = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(pdfPath) { UseShellExecute = true });
                await _dialogService.DisplayAlertAsync("提示",
                    "已用默认 PDF 阅读器打开核查报告，请在其中执行打印。", "确定");
            }
            catch (Exception openEx)
            {
                Logger.LogError(openEx, $"PDF 打开失败: {pdfPath}");
                await _dialogService.DisplayAlertAsync("打印失败",
                    $"无法打开 PDF 报告，请检查默认 PDF 阅读器设置。{openEx.Message}", "确定");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"PDF ShellPrint 失败: {pdfPath}");
            await _dialogService.DisplayAlertAsync("打印失败", $"打印失败：{ex.Message}", "确定");
        }
    }

    #endregion

    #region 待核查人员搜索

    /// <summary>全时间范围检索常量（与业务申请工作流检索口径一致，不受报表周期限制）</summary>
    private static readonly DateTime FullRangeStart = new(2000, 1, 1);
    private static readonly DateTime FullRangeEnd = new(2100, 12, 31);

    [RelayCommand]
    private async Task SearchAsync(CancellationToken token = default)
    {
        // 单独检索：全时间范围（不受报表周期限制）——有关键字按姓名/身份证过滤，清空关键字列出全部待核查户主
        await LoadMatchTasksAsync(token, fullRange: true);
    }

    [RelayCommand]
    private async Task ViewDetailAsync(AssetVerificationTask task)
    {
        if (task == null) return;
        await _dialogService.DisplayAlertAsync("核查详情",
            $"姓名：{task.ArchiveName}\n" +
            $"身份证号：{task.ArchiveIdCard}\n" +
            $"关系：{DictDisplayHelper.GetFamilyRelationshipDisplay(task.Relationship)}\n" +
            $"状态：{task.StatusDisplay}\n" +
            $"创建时间：{task.CreatedAt:yyyy-MM-dd}",
            "确定");
    }

    #endregion

    #region 辅助方法

    private async Task LoadUploadStatsAsync(CancellationToken token = default)
    {
        try
        {
            var (start, end) = ALinePeriodHelper.GetPeriod(SelectedYear, SelectedMonth ?? 1);
            Logger.Debug($"加载统计: 年={SelectedYear}, 月={SelectedMonth}, 周期={start:yyyy-MM-dd}~{end:yyyy-MM-dd}");

            var result = await _verificationService.GetStatsByDateRangeAsync(start, end, token);
            if (token.IsCancellationRequested) return;

            if (result.IsSuccess && result.Value != null)
            {
                SubmittedCount = result.Value.SubmittedCount;
                HasReportCount = result.Value.HasReportCount;
                ArchivedCount = result.Value.ArchivedCount;
                RejectedCount = result.Value.RejectedCount;
                TotalCount = result.Value.TotalCount;
                Logger.Debug($"统计结果: 已提交={SubmittedCount}, 有报告={HasReportCount}, 已完成={ArchivedCount}, 已拒绝={RejectedCount}, 总计={TotalCount}");
            }
            else
            {
                Logger.Debug($"统计查询失败: {result.Message}");
            }
        }
        catch (OperationCanceledException)
        {
            // 被更新的刷新请求取消，静默处理
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "加载统计失败");
        }
    }

    private async Task LoadMatchTasksAsync(CancellationToken token = default, bool fullRange = false)
    {
        try
        {
            // fullRange=false：按 A线周期（上月11日~本月11日）加载当期名单（页面初始化/年月切换）；
            // fullRange=true：全时间范围单独检索（搜索按钮触发），关键字透传服务端过滤（姓名/身份证号 ILIKE）
            var (start, end) = fullRange
                ? (FullRangeStart, FullRangeEnd)
                : ALinePeriodHelper.GetPeriod(SelectedYear, SelectedMonth ?? 1);
            var keyword = string.IsNullOrWhiteSpace(SearchKeyword) ? null : SearchKeyword.Trim();
            var result = await _verificationService.SearchByDateRangePagedAsync(
                start, end, keyword, null, 1, 1000, token);

            if (token.IsCancellationRequested) return;

            if (result.IsSuccess && result.Value != null)
            {
                // 按户主身份证号去重，只保留户主记录
                var headTasks = result.Value.Items
                    .GroupBy(x => x.HeadIdCard)
                    .Select(g => g.FirstOrDefault(x => x.HeadIdCard == x.ArchiveIdCard) ?? g.First())
                    .ToList();

                MatchTasks.Clear();
                foreach (var task in headTasks)
                    MatchTasks.Add(task);
            }
        }
        catch (OperationCanceledException)
        {
            // 被更新的刷新请求取消，静默处理
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "加载待核查人员失败");
        }
    }

    #endregion
}

#region 辅助类

public class UploadResultItem
{
    public string FileName { get; set; } = string.Empty;
    public string MatchName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public bool IsMatched { get; set; }
    public string MatchedName { get; set; } = string.Empty;
    public long? CheckId { get; set; }
    public string BatchId { get; set; } = string.Empty;
    public string ApplicantName { get; set; } = string.Empty;
    public string ApplicantIdCard { get; set; } = string.Empty;
    public string StatusIcon { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public string StatusColor { get; set; } = string.Empty;
    public bool IsUpdate { get; set; }
    public string DisplayText => IsUpdate
        ? $"{FileName} → 更新报告（匹配到 {MatchedName}）"
        : IsMatched
            ? $"{FileName} → 匹配到 {MatchedName}"
            : $"{FileName} → 未匹配到申请人";
}

#endregion
