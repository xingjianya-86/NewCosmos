// ============================================================================
// Recovery（档案域：后补追缴/渐退期审批表/保障金减少）
// 由 Constants\FieldKeys.cs 于 2026-10-16 按域拆分（纯组织改动：常量名与常量值一律未变）。
// 新增字段键纪律：常量加到对应域文件，同时 (1) Builder 供给点赋值 (2) 落本白名单
// (3) 禁中文键，值必须匹配 ^[A-Z0-9_]+$ 且与 config_json 的 fieldKey 一致。
// ============================================================================

namespace NewCosmos.Constants;

public static partial class FieldKeys
{
    // ── 后补追缴（档案_追缴资金办理单 = 模板 240） ──
    // 2026-10-16 批2：常量值由"模板中文占位符"迁为 ASCII 键（= config_json 的 fieldKey），
    // T240 的 config_json 十行 fieldKey 同批迁移，占位符文本（{追缴单位} 等）仍由 config 的
    // placeholder 承担。常量名与全部供给点表达式一律未动 → 打印值零变化。
    public const string RECOVERY_UNIT = "RECOVERY_UNIT";                   // 填报单位 {追缴单位}
    public const string RECOVERY_CODE = "RECOVERY_CODE";                   // 编号（自动生成 YYYYMM-NNN）{追缴编码}
    public const string RECOVERY_SEQ = "RECOVERY_SEQ";                     // 行序号（单行模板固定 "1"）{编号}
    public const string RECOVERY_PERSON_NAME = "RECOVERY_PERSON_NAME";     // 被追缴人姓名 {户主姓名}
    public const string RECOVERY_ID_CARD = "RECOVERY_ID_CARD";             // 身份证号码 {户主身份证号}
    public const string RECOVERY_SOURCE_TYPE = "RECOVERY_SOURCE_TYPE";     // 追缴类别 {户主享受分类}
    public const string RECOVERY_AMOUNT = "RECOVERY_AMOUNT";               // 应缴款金额（元）{应追缴金额}
    public const string RECOVERED_AMOUNT = "RECOVERED_AMOUNT";             // 已缴款金额（元）{实追缴金额}
    public const string RECOVERY_REPORT_DATE = "RECOVERY_REPORT_DATE";     // 缴入民政局时间 {上报时间}
    public const string RECOVERY_RANGE = "RECOVERY_RANGE";                 // 追缴资金起止年度 {追缴起止时间}

    // ── 渐退期审批表（档案_渐退期审批表 = 模板 252） ──
    // 2026-10-16 批2：值迁 ASCII（= config fieldKey），T252 的 config_json 十行同步迁移；
    // 注释里的 {…} 是模板实际占位符（config 的 placeholder）。标注 config未引用 者为
    // 代码有供给但 T252 配置未消费（随批2 一并迁为 ASCII，保证值域全 ASCII 零例外）。
    public const string GP_HEAD_NAME = "GP_HEAD_NAME";                     // 新户主姓名 {新户主姓名}
    public const string GP_HEAD_GENDER = "GP_HEAD_GENDER";                 // 新户主性别 {新户主性别}
    public const string GP_HEAD_ID_CARD = "GP_HEAD_ID_CARD";               // 新户主身份证号 {新户主身份证号}
    public const string GP_HEAD_AGE = "GP_HEAD_AGE";                       // 新户主年龄（config 未引用）
    public const string GP_FAMILY_SIZE = "GP_FAMILY_SIZE";                 // 新家庭人口 {新家庭人口}
    public const string GP_CLASSIFICATION = "GP_CLASSIFICATION";           // 新享受类别（config 未引用）
    public const string GP_ADDRESS = "GP_ADDRESS";                         // 新家庭住址 {新家庭住址}
    public const string GP_PHONE = "GP_PHONE";                             // 新电话号码 {新电话号码}
    public const string GP_CHANGE_DETAIL = "GP_CHANGE_DETAIL";             // 变动情况说明 {详细说明享受对象家庭人员、收入、财产状况变动情况}
    public const string GP_PERIOD_MONTHS = "GP_PERIOD_MONTHS";             // 渐退期月数 {渐退期长度}
    public const string GP_START = "GP_START";                             // 渐退期开始日期（config 未引用）
    public const string GP_END = "GP_END";                                 // 渐退期结束日期（config 未引用）
    public const string GP_PERIOD_RANGE = "GP_PERIOD_RANGE";               // 起止组合 {渐退期启止时间}（yyyy年M月d日至yyyy年M月d日）
    public const string GP_EXIT_SITUATION = "GP_EXIT_SITUATION";           // 退出说明（分型拼句，未退出留空）{退出渐退期情况}
    public const string GP_AUDIT_TIME = "GP_AUDIT_TIME";                   // 审核确认时间（config 未引用）

    // ── （已删除 2026-10-16 · 清偿方案 D5 用户授权）档案_保障金减少族 GR_* 12 键 ──
    // 删除依据（三问全过）：① 模板不在 nc_biz_templates（库/项目/本地三处文件搜索均空）；
    // ② config 引用 0（NonAscii=0 等价证明）；③ 代码读取方 0（仅 Sidebar 填充与 dict 空值初始化，均随删）。
    // 若该模板后续入库：从 git 历史找回本族常量与供给，先按批2 同法把值迁为 ASCII（= 常量名）再配 config。
}
