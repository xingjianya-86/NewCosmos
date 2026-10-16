// ============================================================================
// Elderly（普惠高龄补贴 + 高龄津贴调整备案表）
// 由 Constants\FieldKeys.cs 于 2026-10-16 按域拆分（纯组织改动：常量名与常量值一律未变）。
// 新增字段键纪律：常量加到对应域文件，同时 (1) Builder 供给点赋值 (2) 落本白名单
// (3) 禁中文键，值必须匹配 ^[A-Z0-9_]+$ 且与 config_json 的 fieldKey 一致。
// ============================================================================

namespace NewCosmos.Constants;

public static partial class FieldKeys
{
    // ── 普惠高龄补贴（登记表/享受须知/新增明细/停止明细/取消备案表） ──
    public const string ELDERLY_NAME = "ELDERLY_NAME";                               // 申请人姓名
    public const string ELDERLY_AGE = "ELDERLY_AGE";                                 // 申请人年龄
    public const string ELDERLY_PHONE = "ELDERLY_PHONE";                             // 联系电话
    public const string ELDERLY_ID_CARD = "ELDERLY_ID_CARD";                         // 身份证号
    public const string ELDERLY_HUKOU_ADDRESS = "ELDERLY_HUKOU_ADDRESS";             // 户籍地址
    public const string ELDERLY_FAMILY_ADDRESS = "ELDERLY_FAMILY_ADDRESS";           // 家庭住址
    public const string ELDERLY_BANK_NAME = "ELDERLY_BANK_NAME";                     // 社保卡开户行
    public const string ELDERLY_BANK_ACCOUNT = "ELDERLY_BANK_ACCOUNT";               // 社保卡账号
    public const string ELDERLY_AGENT_NAME = "ELDERLY_AGENT_NAME";                   // 代办人姓名
    public const string ELDERLY_AGENT_RELATION = "ELDERLY_AGENT_RELATION";           // 代办人关系
    public const string ELDERLY_AGENT_RECEIVE_NAME = "ELDERLY_AGENT_RECEIVE_NAME";   // 代领人姓名
    public const string ELDERLY_AGENT_RECEIVE_RELATION = "ELDERLY_AGENT_RECEIVE_RELATION"; // 代领人与申请人关系
    public const string ELDERLY_AGENT_RECEIVE_BANK_NAME = "ELDERLY_AGENT_RECEIVE_BANK_NAME"; // 代领人社保卡开户行
    public const string ELDERLY_AGENT_RECEIVE_BANK_ACCOUNT = "ELDERLY_AGENT_RECEIVE_BANK_ACCOUNT"; // 代领人社保卡账号
    public const string ELDERLY_AGENT_RECEIVE_REASON = "ELDERLY_AGENT_RECEIVE_REASON"; // 代领原因
    public const string ELDERLY_CATEGORY = "ELDERLY_CATEGORY";                       // 享受类别（中文名）
    public const string ELDERLY_APPLY_DATE = "ELDERLY_APPLY_DATE";                   // 申请日期
    public const string ELDERLY_ACCEPT_DATE = "ELDERLY_ACCEPT_DATE";                 // 受理日期
    public const string ELDERLY_ISSUE_START_MONTH = "ELDERLY_ISSUE_START_MONTH";     // 计发年月
    public const string ELDERLY_ISSUE_AMOUNT = "ELDERLY_ISSUE_AMOUNT";               // 计发金额
    public const string ELDERLY_PAYBACK_RANGE = "ELDERLY_PAYBACK_RANGE";             // 补发起止与补发月数（如 2024-10至2025-06，共9个月）
    public const string ELDERLY_PAYBACK_AMOUNT = "ELDERLY_PAYBACK_AMOUNT";           // 补发金额
    public const string ELDERLY_PAYBACK_REASON = "ELDERLY_PAYBACK_REASON";           // 补发原因
    public const string ELDERLY_STOP_REASON = "ELDERLY_STOP_REASON";                 // 停发原因
    public const string ELDERLY_DUE_STOP_DATE = "ELDERLY_DUE_STOP_DATE";             // 应停发时间
    public const string ELDERLY_ACTUAL_STOP_DATE = "ELDERLY_ACTUAL_STOP_DATE";       // 实停发时间
    public const string ELDERLY_IS_RECOVER = "ELDERLY_IS_RECOVER";                   // 是否追缴（是/否）
    public const string ELDERLY_RECOVER_RANGE = "ELDERLY_RECOVER_RANGE";             // 追缴起止时间与追缴月数
    public const string ELDERLY_RECOVER_AMOUNT = "ELDERLY_RECOVER_AMOUNT";           // 追缴金额
    public const string ELDERLY_REMARK = "ELDERLY_REMARK";                           // 备注
    public const string ELDERLY_STANDARD = "ELDERLY_STANDARD";                       // 补贴标准（元/月）
    public const string ELDERLY_ACTUAL_AMOUNT = "ELDERLY_ACTUAL_AMOUNT";             // 实际发放金额
    public const string ELDERLY_CONFIRM_DATE = "ELDERLY_CONFIRM_DATE";               // 审批时间
    public const string ELDERLY_CONTACT_NAME = "ELDERLY_CONTACT_NAME";               // 联系人姓名
    public const string ELDERLY_CONTACT_RELATION = "ELDERLY_CONTACT_RELATION";       // 联系关系
    public const string ELDERLY_GENDER = "ELDERLY_GENDER";                           // 性别
    public const string ELDERLY_ADDRESS = "ELDERLY_ADDRESS";                         // 住址（明细表）
    public const string ELDERLY_PAYBACK_TIME = "ELDERLY_PAYBACK_TIME";               // 补发时间（明细表）
    public const string ELDERLY_PAYBACK_MONTHS = "ELDERLY_PAYBACK_MONTHS";           // 补发月份（明细表）
    public const string ELDERLY_INDEX = "ELDERLY_INDEX";                             // 序号（明细表）
    public const string ELDERLY_STOP_TIME = "ELDERLY_STOP_TIME";                     // 停止时间（明细表）

    // ── 普惠高龄津贴调整备案表（类别复核变更专用） ──
    public const string ELDERLY_ADJUST_NAME = "ELDERLY_ADJUST_NAME";                             // 调整姓名
    public const string ELDERLY_ADJUST_ID_CARD = "ELDERLY_ADJUST_ID_CARD";                       // 调整身份证号
    public const string ELDERLY_ADJUST_HUKOU_ADDRESS = "ELDERLY_ADJUST_HUKOU_ADDRESS";           // 调整户籍地址
    public const string ELDERLY_ADJUST_FAMILY_ADDRESS = "ELDERLY_ADJUST_FAMILY_ADDRESS";         // 调整家庭地址
    public const string ELDERLY_ADJUST_REASON = "ELDERLY_ADJUST_REASON";                         // 调整原因（勾选文本）
    public const string ELDERLY_ADJUST_TIME = "ELDERLY_ADJUST_TIME";                             // 调整时间（年月）
    public const string ELDERLY_ADJUST_AMOUNT = "ELDERLY_ADJUST_AMOUNT";                         // 调整金额（由X元/月调整至Y元/月）
    public const string ELDERLY_ADJUST_PAYBACK_RANGE = "ELDERLY_ADJUST_PAYBACK_RANGE";           // 补发起止时间与补发月数
    public const string ELDERLY_ADJUST_PAYBACK_AMOUNT = "ELDERLY_ADJUST_PAYBACK_AMOUNT";         // 补发金额
    public const string ELDERLY_ADJUST_ACCEPT_DATE = "ELDERLY_ADJUST_ACCEPT_DATE";               // 受理日期
}
