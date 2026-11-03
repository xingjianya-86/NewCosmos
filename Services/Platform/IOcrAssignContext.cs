using NewCosmos.Helpers;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Platform;

/// <summary>
/// 识别结果回填上下文：由来源表单 ViewModel 实现，供分配页查询回填目标与执行回填。
/// 分配页不感知表单结构——目标列表/预匹配名单/应用逻辑全部经此接口下放到表单。
/// </summary>
public interface IOcrAssignContext
{
    /// <summary>取某类别可选的回填目标（"不使用"由分配页统一追加）</summary>
    IReadOnlyList<OcrAssignmentTarget> GetTargets(string categoryCode);

    /// <summary>取预匹配名单行（含户主行 Key=head）；无人物名单返回空列表</summary>
    public IReadOnlyList<OcrAssignmentPlanner.MemberRow> GetMemberRows();

    /// <summary>
    /// 应用一条识别记录到目标。只填空字段（防误覆盖），返回是否实际写入了内容。
    /// 内部自行处理：身份证校验码核验、残疾证联动解析、金额不落库（§9）等。
    /// </summary>
    Task<bool> ApplyAsync(OcrRecognizedRecord record, OcrAssignmentTarget target);
}

/// <summary>回填目标（分配页下拉项）</summary>
public sealed record OcrAssignmentTarget(string Key, string DisplayName);
