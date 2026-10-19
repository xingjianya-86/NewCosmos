// ============================================================================
// NearRelative（近亲属备案：批量备案信息/汇总表/关联信息表/救助对象信息表）
// 由 Constants\FieldKeys.cs 于 2026-10-16 按域拆分（纯组织改动：常量名与常量值一律未变）。
// 新增字段键纪律：常量加到对应域文件，同时 (1) Builder 供给点赋值 (2) 落本白名单
// (3) 禁中文键，值必须匹配 ^[A-Z0-9_]+$ 且与 config_json 的 fieldKey 一致。
// ============================================================================

namespace NewCosmos.Constants;

public static partial class FieldKeys
{
    // ── 近亲属备案（批量备案信息/汇总表/关联信息表/救助对象信息表） ──
    // 工作人员字段（单值）
    public const string NEAR_RELATIVE_STAFF_NAME = "NEAR_RELATIVE_STAFF_NAME";       // 工作人员姓名（近亲属姓名）
    public const string NEAR_RELATIVE_STAFF_ID_CARD = "NEAR_RELATIVE_STAFF_ID_CARD"; // 工作人员身份证号
    public const string NEAR_RELATIVE_STAFF_PHONE = "NEAR_RELATIVE_STAFF_PHONE";     // 工作人员联系方式
    public const string NEAR_RELATIVE_WORK_UNIT = "NEAR_RELATIVE_WORK_UNIT";         // 工作单位
    public const string NEAR_RELATIVE_POSITION = "NEAR_RELATIVE_POSITION";           // 职务（职级）
    // 汇总表公共字段
    public const string NEAR_RELATIVE_REPORT_DATE = "NEAR_RELATIVE_REPORT_DATE";     // 填报日期
    public const string NEAR_RELATIVE_REPORT_UNIT = "NEAR_RELATIVE_REPORT_UNIT";     // 填报单位
    // 公共时间（审核确认时间）
    public const string NEAR_RELATIVE_AUDIT_TIME = "NEAR_RELATIVE_AUDIT_TIME";       // 审核确认时间/填报时间

    // 备案对象行字段（索引槽位：_1.._N，批量备案每页5行、汇总每页9组）
    public const string NEAR_RELATIVE_RELATION = "NEAR_RELATIVE_RELATION";           // 救助对象与工作人员关系（对象关系）
    public const string NEAR_RELATIVE_NAME = "NEAR_RELATIVE_NAME";                   // 救助对象姓名
    public const string NEAR_RELATIVE_ID_CARD = "NEAR_RELATIVE_ID_CARD";             // 救助对象身份证号
    public const string NEAR_RELATIVE_GENDER = "NEAR_RELATIVE_GENDER";               // 性别
    public const string NEAR_RELATIVE_BIRTH_DATE = "NEAR_RELATIVE_BIRTH_DATE";       // 出生年月
    public const string NEAR_RELATIVE_HUKOU_ADDRESS = "NEAR_RELATIVE_HUKOU_ADDRESS"; // 户籍地址
    public const string NEAR_RELATIVE_RESIDENCE_ADDRESS = "NEAR_RELATIVE_RESIDENCE_ADDRESS"; // 居住地址
    public const string NEAR_RELATIVE_FAMILY_ADDRESS = "NEAR_RELATIVE_FAMILY_ADDRESS";     // 家庭住址
    public const string NEAR_RELATIVE_FAMILY_SIZE = "NEAR_RELATIVE_FAMILY_SIZE";     // 家庭人口/共同生活人数
    public const string NEAR_RELATIVE_HELP_TYPE = "NEAR_RELATIVE_HELP_TYPE";         // 社会救助种类/保障类别
    public const string NEAR_RELATIVE_MONTH_AMOUNT = "NEAR_RELATIVE_MONTH_AMOUNT";   // 月保障金
    public const string NEAR_RELATIVE_REPORT_AMOUNT = "NEAR_RELATIVE_REPORT_AMOUNT"; // 保障金额（保障金总额口径；原"报账金额"2026-10-17 更正）
    public const string NEAR_RELATIVE_START_TIME = "NEAR_RELATIVE_START_TIME";       // 救助起始时间
    public const string NEAR_RELATIVE_FAMILY_RELATION = "NEAR_RELATIVE_FAMILY_RELATION"; // 家庭关系
    public const string NEAR_RELATIVE_APPLY_REASON = "NEAR_RELATIVE_APPLY_REASON";   // 申请原因
    public const string NEAR_RELATIVE_APPLY_TIME = "NEAR_RELATIVE_APPLY_TIME";       // 申请时间
    public const string NEAR_RELATIVE_FAMILY_DIFFICULTY = "NEAR_RELATIVE_FAMILY_DIFFICULTY"; // 家庭主要困难
    public const string NEAR_RELATIVE_ECONOMY_INVESTIGATE = "NEAR_RELATIVE_ECONOMY_INVESTIGATE"; // 家庭经济情况调查情况
    public const string NEAR_RELATIVE_TOWN_OPINION = "NEAR_RELATIVE_TOWN_OPINION";   // 乡镇（街道）意见
    public const string NEAR_RELATIVE_DYNAMIC_RECORD = "NEAR_RELATIVE_DYNAMIC_RECORD"; // 动态管理记录

    // 批量备案信息：对象行索引槽位（模板固定 5 行/页，前缀 _1.._5）
    public const string NEAR_RELATIVE_ROW_RELATION = "NEAR_RELATIVE_ROW_RELATION";           // {关系N}
    public const string NEAR_RELATIVE_ROW_NAME = "NEAR_RELATIVE_ROW_NAME";                   // {姓名N}
    public const string NEAR_RELATIVE_ROW_ID_CARD = "NEAR_RELATIVE_ROW_ID_CARD";             // {身份证号N}
    public const string NEAR_RELATIVE_ROW_HUKOU = "NEAR_RELATIVE_ROW_HUKOU";                 // {户籍地址N}
    public const string NEAR_RELATIVE_ROW_RESIDENCE = "NEAR_RELATIVE_ROW_RESIDENCE";         // {居住地址N}
    public const string NEAR_RELATIVE_ROW_HELP_TYPE = "NEAR_RELATIVE_ROW_HELP_TYPE";         // {社会救助类型N}
    public const string NEAR_RELATIVE_ROW_MONTH_AMOUNT = "NEAR_RELATIVE_ROW_MONTH_AMOUNT";   // {月保障金N}

    // 汇总表：行索引槽位（模板固定 9 组/页，前缀 _1.._9）
    public const string NEAR_RELATIVE_SUMMARY_STAFF_NAME = "NEAR_RELATIVE_SUMMARY_STAFF_NAME";       // 工作人员姓名 {姓名N}
    public const string NEAR_RELATIVE_SUMMARY_WORK_UNIT = "NEAR_RELATIVE_SUMMARY_WORK_UNIT";         // 工作单位 {工作单位N}
    public const string NEAR_RELATIVE_SUMMARY_POSITION = "NEAR_RELATIVE_SUMMARY_POSITION";           // 职务职级 {职务职级N}
    public const string NEAR_RELATIVE_SUMMARY_RELATION = "NEAR_RELATIVE_SUMMARY_RELATION";           // 对象关系 {对象关系N}
    public const string NEAR_RELATIVE_SUMMARY_NAME = "NEAR_RELATIVE_SUMMARY_NAME";                   // 救助姓名 {救助姓名N}
    public const string NEAR_RELATIVE_SUMMARY_ID_CARD = "NEAR_RELATIVE_SUMMARY_ID_CARD";             // 救助身份证号 {救助身份证号N}
    public const string NEAR_RELATIVE_SUMMARY_HUKOU = "NEAR_RELATIVE_SUMMARY_HUKOU";                 // 救助户籍地址 {救助户籍地址N}
    public const string NEAR_RELATIVE_SUMMARY_RESIDENCE = "NEAR_RELATIVE_SUMMARY_RESIDENCE";         // 救助居住地址 {救助居住地址N}
    public const string NEAR_RELATIVE_SUMMARY_HELP_TYPE = "NEAR_RELATIVE_SUMMARY_HELP_TYPE";         // 救助保障类别 {救助保障类别N}
    public const string NEAR_RELATIVE_SUMMARY_FAMILY_SIZE = "NEAR_RELATIVE_SUMMARY_FAMILY_SIZE";     // 救助共同生活人数 {救助共同生活人数N}
    public const string NEAR_RELATIVE_SUMMARY_REPORT_AMOUNT = "NEAR_RELATIVE_SUMMARY_REPORT_AMOUNT"; // 救助保障金额 {救助保障金额N}（原"救助报账金额"，2026-10-17 模板与键注释一并更正）

    // 档案门控标记：该申请存在关联备案（装配时置 "1"，档案输出页据此显隐近亲属模板）
    public const string NEAR_RELATIVE_HAS_DATA = "NEAR_RELATIVE_HAS_DATA";
}
