// ============================================================================
// TempRelief（临时救助：入户调查/审核审批/公示/验收 + 月报临时救助汇总）
// 由 Constants\FieldKeys.cs 于 2026-10-16 按域拆分（纯组织改动：常量名与常量值一律未变）。
// 新增字段键纪律：常量加到对应域文件，同时 (1) Builder 供给点赋值 (2) 落本白名单
// (3) 禁中文键，值必须匹配 ^[A-Z0-9_]+$ 且与 config_json 的 fieldKey 一致。
// ============================================================================

namespace NewCosmos.Constants;

public static partial class FieldKeys
{
    // ── 临时救助（入户调查表/大额审批表/小额审批表/信息公示/验收报告） ──
    // 主表与通用字段
    public const string TEMP_REPORT_UNIT = "TEMP_REPORT_UNIT";                       // 填报单位
    public const string TEMP_REPORT_TIME = "TEMP_REPORT_TIME";                       // 填报时间
    public const string TEMP_APPLICANT_NAME = "TEMP_APPLICANT_NAME";                 // 申请人姓名
    public const string TEMP_APPLICANT_GENDER = "TEMP_APPLICANT_GENDER";             // 申请人性别
    public const string TEMP_APPLICANT_AGE = "TEMP_APPLICANT_AGE";                   // 申请人年龄
    public const string TEMP_APPLICANT_ID_CARD = "TEMP_APPLICANT_ID_CARD";           // 申请人身份证号
    public const string TEMP_FAMILY_ADDRESS = "TEMP_FAMILY_ADDRESS";                 // 家庭住址
    public const string TEMP_POLICY_ENJOYED = "TEMP_POLICY_ENJOYED";                 // 享受的政策救助情况
    public const string TEMP_FAMILY_MEMBER_STATUS = "TEMP_FAMILY_MEMBER_STATUS";     // 家庭成员状态
    public const string TEMP_DIFFICULTY_REASON = "TEMP_DIFFICULTY_REASON";           // 申请救助原因/困难情况
    public const string TEMP_DIFFICULTY_TYPE = "TEMP_DIFFICULTY_TYPE";               // 困难类型（疾病/意外灾害/教育支出/其他困难）
    public const string TEMP_DIFFICULTY_DETAILS = "TEMP_DIFFICULTY_DETAILS";         // 困难情况明细（按类型合并文本，多条序号展示）
    public const string TEMP_DISEASE_DIAGNOSIS = "TEMP_DISEASE_DIAGNOSIS";           // 疾病诊断结果（多条以分号分隔合并）
    public const string TEMP_DISEASE_CODE = "TEMP_DISEASE_CODE";                     // 疾病编码（ICD-10，多条以分号分隔合并）
    public const string TEMP_DISEASE_SELF_PAID = "TEMP_DISEASE_SELF_PAID";           // 自费费用（元，疾病明细共享值）
    public const string TEMP_DISEASE_HOSPITAL = "TEMP_DISEASE_HOSPITAL";             // 所在医院（疾病明细共享值）
    public const string TEMP_ACCIDENT_DETAILS = "TEMP_ACCIDENT_DETAILS";             // 意外灾害明细（多条序号展示）
    public const string TEMP_EDUCATION_DETAILS = "TEMP_EDUCATION_DETAILS";           // 教育支出明细（多条序号展示）
    public const string TEMP_APPLY_DATE = "TEMP_APPLY_DATE";                         // 申请日期
    public const string TEMP_FAMILY_SIZE = "TEMP_FAMILY_SIZE";                       // 家庭人口
    public const string TEMP_FAMILY_CATEGORY = "TEMP_FAMILY_CATEGORY";               // 户主家庭类别
    public const string TEMP_VERIFY_RESULT = "TEMP_VERIFY_RESULT";                   // 入户核实情况
    public const string TEMP_CONFIRM_AMOUNT = "TEMP_CONFIRM_AMOUNT";                 // 确定金额（小额审批表）
    public const string TEMP_PUBLICIZE_RANGE = "TEMP_PUBLICIZE_RANGE";               // 公示日期区间
    public const string TEMP_HUKOU_ADDRESS = "TEMP_HUKOU_ADDRESS";                   // 户籍所在地
    public const string TEMP_VILLAGE = "TEMP_VILLAGE";                               // 所在村/社区
    public const string TEMP_TOWN = "TEMP_TOWN";                                     // 所在镇/街道
    public const string TEMP_ACCEPTANCE_PERSON = "TEMP_ACCEPTANCE_PERSON";           // 乡镇验收人（民政助理）
    public const string TEMP_CONTACT_PHONE = "TEMP_CONTACT_PHONE";                   // 工作单位联系方式（公示异议反馈电话）
    public const string TEMP_RELIEF_TYPE_NAME = "TEMP_RELIEF_TYPE_NAME";             // 救助类型名称（大额/小额）
    public const string TEMP_APPLICATION_NO = "TEMP_APPLICATION_NO";                 // 申请编号
    public const string TEMP_APPLICANT_PHONE = "TEMP_APPLICANT_PHONE";               // 申请人联系电话（审批表）
    public const string TEMP_PUBLICIZE_START = "TEMP_PUBLICIZE_START";               // 公示开始日期（验收报告日期起）
    public const string TEMP_PUBLICIZE_END = "TEMP_PUBLICIZE_END";                   // 公示结束日期（验收报告日期止）
    public const string TEMP_APPLY_STATEMENT = "TEMP_APPLY_STATEMENT";               // 申请救助情况说明（纯叙述，不含成员状态追加段；入户调查表专用）
    public const string TEMP_POLICY_CHECKLIST = "TEMP_POLICY_CHECKLIST";               // 享受政策清单（□/√ 勾选格式；入户调查表专用）
    public const string TEMP_ACCEPTANCE_REASON = "TEMP_ACCEPTANCE_REASON";             // 临时救助原因（叙述+费用明细；验收报告专用）
    public const string TEMP_ACCEPTANCE_CONCLUSION = "TEMP_ACCEPTANCE_CONCLUSION";     // 验收结论整句（因病因学…，于…公示，…验收通过）
    public const string TEMP_INVESTIGATION_DATE = "TEMP_INVESTIGATION_DATE";           // 入户调查时间（动态：min(今日最近工作日, 公示开始前一工作日)；入户调查表/审核审批表用）
    public const string TEMP_TOWN_OPINION_DATE = "TEMP_TOWN_OPINION_DATE";             // 街道（乡镇）意见日期（=公示结束日；审核审批表用）
    public const string TEMP_DIFFICULTY_SUMMARY = "TEMP_DIFFICULTY_SUMMARY";           // 困难情况摘要（概括句，不含疾病名称/编码/费用；点名患病人；入户调查表+信息公示用）
    public const string TEMP_AUDIT_REASON = "TEMP_AUDIT_REASON";                       // 申请救助原因（精简句；审核审批表专用）
    public const string TEMP_BENEFICIARY_NAME = "TEMP_BENEFICIARY_NAME";               // 救助对象姓名（家庭成员中选定；空时兜底回退申请人）
    public const string TEMP_BENEFICIARY_ID_CARD = "TEMP_BENEFICIARY_ID_CARD";         // 救助对象身份证号
    public const string TEMP_BENEFICIARY_GENDER = "TEMP_BENEFICIARY_GENDER";           // 救助对象性别
    public const string TEMP_BENEFICIARY_AGE = "TEMP_BENEFICIARY_AGE";                 // 救助对象年龄
    public const string TEMP_BENEFICIARY_RELATION = "TEMP_BENEFICIARY_RELATION";       // 救助对象与户主关系

    // ── 月报_临时救助新增汇总表 ──
    public const string TR_REPORT_MONTH = "TR_REPORT_MONTH";                       // 申报月份（标题前缀）
    public const string TR_SEQ = "TR_SEQ";                                         // 序号（索引槽位：_1.._10）
    public const string TR_NAME = "TR_NAME";                                       // 姓名（申请人）
    public const string TR_ID_CARD = "TR_ID_CARD";                                 // 身份证号
    public const string TR_AGE = "TR_AGE";                                         // 年龄
    public const string TR_GENDER = "TR_GENDER";                                   // 性别
    public const string TR_FAMILY_SIZE = "TR_FAMILY_SIZE";                         // 家庭人口
    public const string TR_ADDRESS = "TR_ADDRESS";                                 // 家庭住址
    public const string TR_CATEGORY = "TR_CATEGORY";                               // 最低生活保障分类（个人类别）
    public const string TR_PHONE = "TR_PHONE";                                     // 联系方式
    public const string TR_AMOUNT = "TR_AMOUNT";                                   // 救助金额（小额=确定金额；大额留空）
    public const string TR_AUDIT_DATE = "TR_AUDIT_DATE";                           // 审批时间（小额=确认时间；大额留空）
    public const string TR_REASON = "TR_REASON";                                   // 申请理由（精简句）
    public const string TR_SELF_PAY = "TR_SELF_PAY";                               // 自付金额合计
    public const string TR_BANK_ACCOUNT = "TR_BANK_ACCOUNT";                       // 一卡通账号（最低生活保障申请 bank_account）

    // 家庭成员索引槽位（大额/小额审批表，1..5 行）
    public const string TEMP_MEMBER_NAME = "TEMP_MEMBER_NAME";                       // 家庭成员姓名（索引槽位：_1.._5）
    public const string TEMP_MEMBER_GENDER = "TEMP_MEMBER_GENDER";                   // 家庭成员性别
    public const string TEMP_MEMBER_RELATION = "TEMP_MEMBER_RELATION";               // 与户主关系
    public const string TEMP_MEMBER_ID_CARD = "TEMP_MEMBER_ID_CARD";                 // 家庭成员身份证号
    public const string TEMP_MEMBER_WORK_UNIT = "TEMP_MEMBER_WORK_UNIT";             // 家庭成员工作单位
    public const string TEMP_MEMBER_ANNUAL_INCOME = "TEMP_MEMBER_ANNUAL_INCOME";     // 家庭成员年收入

    // 审核审批表（原大额/小额审批表合并）与验收报告2 专用
    public const string TEMP_FAMILY_CATEGORY_CHECKLIST = "TEMP_FAMILY_CATEGORY_CHECKLIST"; // 户主家庭类别勾选清单（□/☑ 9 项固定映射）
    public const string TEMP_ACCEPTANCE_DATE = "TEMP_ACCEPTANCE_DATE";               // 验收时间（公示结束日期，空则打印当天）
    public const string TEMP_PARENT_UNIT = "TEMP_PARENT_UNIT";                       // 父级单位（大额→县民政部门意见，小额→镇政府意见）
}
