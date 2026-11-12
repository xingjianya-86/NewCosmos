using CommunityToolkit.Mvvm.ComponentModel;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Domain.SevereDisease;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// AI 重病判定接线（P1，2026-11-12）——申请人（Step1/移动 Step1）与家庭成员（Step2 卡片/移动 Step2）双入口：
/// 编码命中 ICD-10 字典后 → ① 本地确定性秒出（SevereDiseaseJudge，离线毫秒）→ ② 未命中消抖 600ms
/// 走 缓存/服务端 LLM 管线（SevereDiseaseAdviceService 三级管线）。
///
/// 安全策略（2026-11-11 补测定稿）：
/// - 自动预勾选只走 ISevereDiseaseAdviceService.ShouldAutoCheck 闸门：severe 且 matched_item∈清单；
/// - 只勾不取消：AI 永不把 IsSevereDisease 从 true 改 false；
/// - 人工优先：用户取消过一次（true→false 只能来自用户）即否决，本会话不再自动勾选；
/// - verdict≠severe 不出建议条（不制造噪音、不误导）；
/// - 判定是建议通道：失败=无建议（客户端已记日志），异常不冒泡、不阻塞表单（§7 降级口径）。
/// </summary>
public partial class ApplicationFormViewModel
{
    private readonly ISevereDiseaseAdviceService _severeDiseaseAdvice;

    // 消抖令牌：连续敲码只判最后一次（申请人/成员各自独立，互不取消）
    private CancellationTokenSource? _applicantAiCts;
    private CancellationTokenSource? _memberAiCts;

    // 加载恢复 IsSevereDisease 期间抑制否决跟踪（同 VM 复用恢复的 true→false 不是用户否决）
    private bool _suppressSevereRejectTrack;

    // 申请人已人工否决自动勾选（成员的否决态在 FamilyMember.AiSevereDiseaseRejected）
    private bool _severeDiseaseAutoCheckRejected;

    /// <summary>申请人 AI 建议条文案（verdict=severe 才有值；由 AiJudgeConstants.FormatSuggestion 拼装）</summary>
    [ObservableProperty]
    private string _aiSeveritySuggestionText = string.Empty;

    /// <summary>是否显示申请人 AI 建议条</summary>
    [ObservableProperty]
    private bool _hasAiSeveritySuggestion;

    /// <summary>忽略申请人建议条（点击隐藏；已自动勾选的保持不变）</summary>
    public void DismissAiSeveritySuggestion() => HasAiSeveritySuggestion = false;

    /// <summary>
    /// 申请人入口：由 ApplyApplicantDiseaseCode 在编码命中字典（name 已带出）后调用。
    /// 确定性命中同步秒出；否则消抖后走完整管线。
    /// </summary>
    public void RequestApplicantAiJudgment(string? code, string name)
    {
        HasAiSeveritySuggestion = false; // 换输入即失效旧建议

        var deterministic = _severeDiseaseAdvice.TryDeterministic(name, code);
        if (deterministic != null)
        {
            ApplyApplicantJudgment(deterministic);
            return;
        }
        _ = DebouncedApplicantJudgmentAsync(code ?? string.Empty, name);
    }

    /// <summary>成员入口：由 Step2 卡片/移动 Step2 的 TextChanged 在 ApplyDiseaseCode 后调用。</summary>
    public void RequestMemberAiJudgment(FamilyMember member, string? code)
    {
        if (member == null) return;
        member.AiSuggestionText = string.Empty; // 换输入即失效旧建议
        if (!Icd10Catalog.TryGetName(code, out var name)) return; // 未命中字典：名称不可信，不判定

        var deterministic = _severeDiseaseAdvice.TryDeterministic(name, code);
        if (deterministic != null)
        {
            ApplyMemberJudgment(member, deterministic);
            return;
        }
        _ = DebouncedMemberJudgmentAsync(member, code ?? string.Empty, name);
    }

    private async Task DebouncedApplicantJudgmentAsync(string code, string name)
    {
        _applicantAiCts?.Cancel();
        _applicantAiCts?.Dispose();
        var cts = _applicantAiCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600), cts.Token);
            var result = await _severeDiseaseAdvice.JudgeAsync(name, code, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (result.IsSuccess)
                ApplyApplicantJudgment(result.Value);
            // 失败=无建议：静默降级（错误码已由 RemoteAiJudgeClient 记日志），表单不受影响
        }
        catch (OperationCanceledException)
        {
            // 被更新的输入取代：预期路径
        }
        catch (Exception ex)
        {
            _logger.Warn($"AI 重病判定异常（已降级为无建议）: {ex.Message}");
        }
    }

    private async Task DebouncedMemberJudgmentAsync(FamilyMember member, string code, string name)
    {
        _memberAiCts?.Cancel();
        _memberAiCts?.Dispose();
        var cts = _memberAiCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600), cts.Token);
            var result = await _severeDiseaseAdvice.JudgeAsync(name, code, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (result.IsSuccess)
                ApplyMemberJudgment(member, result.Value);
        }
        catch (OperationCanceledException)
        {
            // 被更新的输入取代：预期路径
        }
        catch (Exception ex)
        {
            _logger.Warn($"AI 重病判定异常（成员，已降级为无建议）: {ex.Message}");
        }
    }

    /// <summary>应用判定到申请人：severe 出建议条（过闸门则自动勾选）；非 severe 隐藏建议条（勾选不动）</summary>
    private void ApplyApplicantJudgment(SevereDiseaseJudgment judgment)
    {
        if (judgment.Verdict != SevereDiseaseJudgment.VerdictSevere)
        {
            HasAiSeveritySuggestion = false;
            return;
        }
        var auto = ISevereDiseaseAdviceService.ShouldAutoCheck(judgment, _severeDiseaseAutoCheckRejected);
        if (auto && !IsSevereDisease)
            IsSevereDisease = true; // 只勾不取消
        AiSeveritySuggestionText = AiJudgeConstants.FormatSuggestion(judgment, auto);
        HasAiSeveritySuggestion = true;
    }

    /// <summary>应用判定到成员（同申请人策略；否决态在成员实体上）</summary>
    private void ApplyMemberJudgment(FamilyMember member, SevereDiseaseJudgment judgment)
    {
        if (judgment.Verdict != SevereDiseaseJudgment.VerdictSevere)
        {
            member.AiSuggestionText = string.Empty;
            return;
        }
        var auto = ISevereDiseaseAdviceService.ShouldAutoCheck(judgment, member.AiSevereDiseaseRejected);
        if (auto && !member.IsSevereDisease)
            member.IsSevereDisease = true; // 只勾不取消
        member.AiSuggestionText = AiJudgeConstants.FormatSuggestion(judgment, auto);
    }

    #region 费用口径重病判定（黑龙政办法第六十八条，2026-11-12 轨二）

    /// <summary>申请人前12个月个人医疗自付累计（元）；失焦提交后触发费用口径判定</summary>
    [ObservableProperty]
    private decimal? _medicalSelfPaid12m;

    /// <summary>城乡档取值：与表单其他判定同口径（HukouType 非 Urban 即农村档，见 DetailCommands/BankFlow）</summary>
    private bool IsRuralHukou => HukouType?.Key != "Urban";

    partial void OnMedicalSelfPaid12mChanged(decimal? value)
        => _ = EvaluateApplicantCostAsync(value);

    /// <summary>申请人费用口径判定：阈值经建议服务单点读（缺失=不判定），过闸门才自动勾选。</summary>
    private async Task EvaluateApplicantCostAsync(decimal? paid)
    {
        try
        {
            if (paid is null or <= 0m) return; // 未录入/无效：不判定也不查配置（阈值语义见 SevereDiseaseByCostRule）

            var thresholdResult = await _severeDiseaseAdvice.GetCostThresholdAsync(IsRuralHukou);
            if (thresholdResult.IsFailure || thresholdResult.Value is not { } threshold) return;
            if (MedicalSelfPaid12m != paid) return; // 输入已变，放弃过期结果
            if (!SevereDiseaseByCostRule.IsSevereByCost(paid, threshold)) return;

            var auto = !_severeDiseaseAutoCheckRejected; // 与 AI 同闸门：用户否决过则只出建议不自动勾
            if (auto && !IsSevereDisease)
                IsSevereDisease = true; // 只勾不取消
            AiSeveritySuggestionText = SevereDiseaseByCostRule.FormatSuggestion(paid!.Value, threshold, IsRuralHukou, auto);
            HasAiSeveritySuggestion = true;
        }
        catch (Exception ex)
        {
            _logger.Warn($"费用口径重病判定异常（已降级为无建议）: {ex.Message}");
        }
    }

    /// <summary>成员费用口径入口（成员卡/移动 Step2 的自付字段 TextChanged 调用）</summary>
    public void RequestMemberCostJudgment(FamilyMember member)
        => _ = EvaluateMemberCostAsync(member);

    private async Task EvaluateMemberCostAsync(FamilyMember member)
    {
        try
        {
            var paid = member.MedicalSelfPaid12m;
            if (paid is null or <= 0m) return;

            var thresholdResult = await _severeDiseaseAdvice.GetCostThresholdAsync(IsRuralHukou);
            if (thresholdResult.IsFailure || thresholdResult.Value is not { } threshold) return;
            if (member.MedicalSelfPaid12m != paid) return; // 输入已变，放弃过期结果
            if (!SevereDiseaseByCostRule.IsSevereByCost(paid, threshold)) return;

            var auto = !member.AiSevereDiseaseRejected;
            if (auto && !member.IsSevereDisease)
                member.IsSevereDisease = true; // 只勾不取消
            member.AiSuggestionText = SevereDiseaseByCostRule.FormatSuggestion(paid!.Value, threshold, IsRuralHukou, auto);
        }
        catch (Exception ex)
        {
            _logger.Warn($"费用口径重病判定异常（成员，已降级为无建议）: {ex.Message}");
        }
    }

    #endregion
}
