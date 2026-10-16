using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.SocialAssistance;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 申请表单 · 回访区 partial（2026-10-16 拍板：认定通过后补录 / 申请详情内嵌 / T3 模板名不动）。
/// 低收入人口认定流程的附属回访——不加申请状态、不动 ApplicationStateMachine、与入户调查表分离。
/// T3「档案_咨询登记台账」打印侧按身份证取最近一条填 VISIT_* 三键（本处补录 = 唯一数据源）。
/// </summary>
public partial class ApplicationFormViewModel
{
    /// <summary>回访服务（主构造注入，字段声明在此 partial——同类型一体编译）</summary>
    private readonly IVisitRecordService _visitRecordService;

    [ObservableProperty] private bool hasVisitSection;
    [ObservableProperty] private string visitStatusText = "回访：未加载";
    [ObservableProperty] private bool visitStatusIsWarning;
    [ObservableProperty] private bool visitPanelVisible;
    [ObservableProperty] private ObservableCollection<VisitRecord> visitRecords = new();
    [ObservableProperty] private DateTime newVisitAt = DateTime.Now;
    [ObservableProperty] private string newVisitBy = App.CurrentUserFullName ?? "";
    [ObservableProperty] private string newVisitResult = "已回访";
    [ObservableProperty] private string newVisitRemark = "";
    [ObservableProperty] private bool isSavingVisit;

    /// <summary>回访情况下拉选项（设计稿确认点6 默认值；业务定稿后改此处一行）</summary>
    public string[] VisitResultOptions { get; } = { "已回访", "电话回访", "未联系上", "无需回访" };

    /// <summary>
    /// 加载当前申请的回访历史与状态标（由 LoadApplicationAsync 尾部调用；
    /// 异常内部吞掉记日志，绝不阻断表单加载——§7 加载失败显式留痕而非抛出打断）。
    /// </summary>
    internal async Task LoadVisitAsync()
    {
        if (_applicationId <= 0)
        {
            HasVisitSection = false;
            return;
        }

        HasVisitSection = true;
        try
        {
            var list = await _visitRecordService.GetListByApplicationIdAsync(_applicationId);
            var records = list.IsSuccess && list.Value != null ? list.Value : new List<VisitRecord>();
            VisitRecords = new ObservableCollection<VisitRecord>(records);

            if (records.Count > 0)
            {
                var latest = records[0];
                VisitStatusText = $"已回访 {latest.VisitAt:yyyy-MM-dd} · {latest.VisitBy} · {latest.VisitResult}（共 {records.Count} 条）";
                VisitStatusIsWarning = false;
            }
            else
            {
                VisitStatusText = "待回访 —— 认定通过后请补录回访情况";
                VisitStatusIsWarning = true;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"回访记录加载失败: {ex.Message}");
            VisitStatusText = "回访：加载失败";
            VisitStatusIsWarning = true;
        }
    }

    [RelayCommand]
    private void ToggleVisitPanel() => VisitPanelVisible = !VisitPanelVisible;

    [RelayCommand]
    private async Task SaveVisitAsync()
    {
        if (_applicationId <= 0)
        {
            await Dialog.DisplayAlertAsync("提示", "请先保存申请后再补录回访", "确定");
            return;
        }
        if (IsSavingVisit) return;
        if (string.IsNullOrWhiteSpace(NewVisitBy))
        {
            await Dialog.DisplayAlertAsync("提示", "请填写回访经办人员", "确定");
            return;
        }
        if (string.IsNullOrWhiteSpace(NewVisitResult))
        {
            await Dialog.DisplayAlertAsync("提示", "请选择回访情况", "确定");
            return;
        }

        IsSavingVisit = true;
        try
        {
            var result = await _visitRecordService.AddAsync(
                new VisitRecord
                {
                    ApplicationId = _applicationId,
                    VisitAt = NewVisitAt,
                    VisitBy = NewVisitBy.Trim(),
                    VisitResult = NewVisitResult,
                    VisitRemark = string.IsNullOrWhiteSpace(NewVisitRemark) ? null : NewVisitRemark.Trim(),
                },
                App.CurrentUserFullName);

            if (result.IsFailure)
            {
                await Dialog.DisplayAlertAsync("错误", result.Message ?? "回访保存失败", "确定");
                return;
            }

            NewVisitRemark = "";
            NewVisitAt = DateTime.Now;
            VisitPanelVisible = false;
            await LoadVisitAsync();
            await Dialog.DisplayAlertAsync("成功", "回访已补录", "确定");
        }
        catch (Exception ex)
        {
            _logger.Error($"回访补录异常: {ex.Message}");
            await Dialog.DisplayAlertAsync("错误", $"回访补录失败: {ex.Message}", "确定");
        }
        finally
        {
            IsSavingVisit = false;
        }
    }

    /// <summary>对话框（按需解析——与 ClassificationAndSave partial 同款模式）</summary>
    private IDialogService Dialog => _serviceProvider.GetRequiredService<IDialogService>();
}
