using NewCosmos.Constants;
using NewCosmos.Helpers;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 申报提交前置校验（2026-11 死亡说明简化·3b 下沉：原 DeathStatementViewModel.DeclareAsync
/// 的 9 段顺序校验纯移动至此，零行为变更；VM 只负责弹提示与组装请求）。
/// 返回第一条失败消息；全部通过返回 null。
/// </summary>
public static class DeathDeclareValidator
{
    /// <summary>校验输入（由 VM 按表单当前值组装；IsTownScope 时 DeclaredVillage 必填）</summary>
    public sealed class Input
    {
        public string? Name { get; init; }
        public string? IdCard { get; init; }
        public DateTime? DeathDateTime { get; init; }
        public string? DeclaredByName { get; init; }
        public string? DeclaredByIdCard { get; init; }
        public string? DeclaredByType { get; init; }
        public string? ReporterName { get; init; }
        public bool IsTownScope { get; init; }
        public string? DeclaredVillage { get; init; }
        /// <summary>业务事项选项总数（0 = 字典未加载，不拦）</summary>
        public int BusinessMatterOptionCount { get; init; }
        /// <summary>已勾选业务事项数</summary>
        public int BusinessMatterCheckedCount { get; init; }
    }

    /// <summary>按 DeclareAsync 原顺序逐段校验；第一条失败即返回。</summary>
    public static string? Validate(Input i)
    {
        if (string.IsNullOrWhiteSpace(i.Name))
            return "请填写死者姓名";
        if (string.IsNullOrWhiteSpace(i.IdCard))
            return "请填写死者身份证号";
        // 死亡日期与时刻必填（组件未选择前 DeathDateTime 恒为 null，此处为真实把关）
        if (i.DeathDateTime is null)
            return "请选择死亡日期与时间";
        if (i.DeathDateTime.Value > DateTime.Now)
            return "死亡时间不能晚于当前时间";
        if (string.IsNullOrWhiteSpace(i.DeclaredByName))
            return "请填写申报人姓名";
        if (string.IsNullOrWhiteSpace(i.DeclaredByIdCard))
            return "请填写申报人身份证号码（文书「申报人承诺」段需要）";
        if (!IdCardValidator.IsValid(i.DeclaredByIdCard.Trim()))
            return "申报人身份证号校验不通过，请核对后重新填写";
        if (string.IsNullOrWhiteSpace(i.DeclaredByType))
            return "请选择申报人类型";
        // 业务事项改多选后：字典选项在场时至少勾 1 项（字典未加载不拦，避免种子未播种卡死提交）
        if (i.BusinessMatterOptionCount > 0 && i.BusinessMatterCheckedCount == 0)
            return "请至少勾选一项相关业务事项";
        // 核实说明正文要写报告人；镇级降级时组织无村，落款「所在村」必须手填
        if (string.IsNullOrWhiteSpace(i.ReporterName))
            return "请填写发现（报告）人姓名";
        if (i.IsTownScope && string.IsNullOrWhiteSpace(i.DeclaredVillage))
            return "当前组织未绑定村社区，请填写「所在村」（文书落款与抬头需要）";

        return null;
    }
}
