// ============================================================================
// FieldKeys 声明壳（partial）：全部常量已按域拆到同目录 FieldKeys.*.cs：
//   FieldKeys.Common.cs       通用跨域键（申请人/家庭/成员/收入财产/公示/特困/低保证明等）
//   FieldKeys.Elderly.cs      普惠高龄补贴 + 高龄津贴调整备案表
//   FieldKeys.TempRelief.cs   临时救助（入户调查/审核审批/公示/验收/月报汇总）
//   FieldKeys.NearRelative.cs 近亲属备案
//   FieldKeys.Recovery.cs     档案域（后补追缴/渐退期审批表/保障金减少）
//   FieldKeys.Change.cs       增减员调整表 + 动态管理记录 + 定期复核审批表
// 本文件仅保留声明与总注释；2026-10-16 拆分为纯组织改动，常量名/值一律不变。
//
// 【新增键纪律（批6 长效机制，2026-10-16）】新增 fieldKey 必须三同步，缺一即产生孤儿键：
//   ① 在对应 FieldKeys.*.cs 声明 public const string（值 = 键名，禁中文）；
//   ② 在供给该模板的 Builder/ViewModel 字典同步供给——与既有同源键并存时同值双写（观察期后删旧键行）；
//   ③ 上传/发版自动体检：入口 TemplateFieldKeyValidator（仅警告）+ 发版 publish_all.ps1 前置
//      Scripts\check_template_fields.ps1（-FailOn NonAscii,MultiSource -MaxUnknown 基线，命中即中断）。
// 未同步的后果 = config 有键、代码无供给 → 打印占位符残留或空值（先例：2026-10-15 {经办人}）。
// ============================================================================
namespace NewCosmos.Constants;

public static partial class FieldKeys
{
}
